using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class EventBoardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task StoryEvent_ShowsTheFullLinkedHeaderOnDesktopAndMobile(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });
        const string article = "https://nopara73.medium.com/crossfit-vs-hyrox-4d151061c611";
        var item = new EventItem("crossfit-hyrox-story", EventType.CustomEvent,
            "CrossFit vs HYROX\n\nWhat can Longevity World Cup learn from their growth?\n\n[Read the story](" + article + ")",
            DateTime.UtcNow, 15, true);
        var eventJson = item.ToJson();
        eventJson["Artwork"]!["ImageUrl"] = new Uri(App.BaseAddress, "/assets/social/crossfit-vs-hyrox.png").ToString();
        await RouteEventBoardDependenciesAsync(context, "[" + eventJson.ToJsonString() + "]");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events?event=crossfit-hyrox-story", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var image = page.Locator(".custom-event-artwork img");
        await image.WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.WaitForFunctionAsync("() => document.querySelector('.custom-event-artwork img')?.naturalWidth > 0");
        Assert.Equal(article, await image.Locator("..").GetAttributeAsync("href"));
        var bounds = Assert.IsType<LocatorBoundingBoxResult>(await image.BoundingBoxAsync());
        Assert.InRange(bounds.Width, 100, Math.Min(width, 720));
        Assert.InRange(bounds.Width / bounds.Height, 1.76f, 1.79f);
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        Assert.Contains("What can Longevity World Cup learn from their growth?", await page.Locator(".custom-event-content").InnerTextAsync());
        if (Environment.GetEnvironmentVariable("LWC_STORY_CAPTURE_DIR") is { Length: > 0 } capture)
        {
            Directory.CreateDirectory(capture);
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"event-{width}.png"), FullPage = true });
        }
    }

    [Fact]
    public async Task CustomEventRowChrome_TogglesDetailsWithoutStealingInteractiveClicks()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 635, Height = 360 }
        });
        var eventsBody = JsonSerializer.Serialize(new[]
        {
            new
            {
                Id = "custom-row-hit-area",
                Type = 6,
                Text = "Launch [details](https://example.test/details)\n\nThe full announcement remains available here.",
                OccurredAt = DateTimeOffset.UtcNow,
                Relevance = 10
            }
        });
        await RouteEventBoardDependenciesAsync(context, eventsBody);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Error fetching athletes:", StringComparison.Ordinal))
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(
            "/",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var row = page.Locator("#eventsTable tbody tr.custom-event-row");
        await row.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        var expander = row.Locator(".custom-event-expander");
        var details = page.Locator("#eventsTable tbody tr.custom-event-details");

        await AssertExpanderIsContainedAndCenteredAsync(row, expander);
        await expander.HoverAsync();
        await AssertExpanderIsContainedAndCenteredAsync(row, expander);

        await row.Locator(".col-date").ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        Assert.Equal("true", await expander.GetAttributeAsync("aria-expanded"));
        await details.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        await row.Locator(".col-date").ClickAsync();
        Assert.False(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));

        await expander.ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));

        var titleLink = row.Locator(".custom-event-title a");
        Assert.Equal(1, await titleLink.CountAsync());
        await titleLink.EvaluateAsync("element => element.addEventListener('click', event => event.preventDefault(), { once: true })");
        await titleLink.ClickAsync();
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1440, 100, "en-US")]
    [InlineData(820, 100, "en-US")]
    [InlineData(390, 100, "en-US")]
    [InlineData(320, 100, "en-US")]
    [InlineData(390, 200, "en-US")]
    [InlineData(601, 100, "hu-HU")]
    [InlineData(1440, 200, "de-DE")]
    [InlineData(320, 200, "ja-JP")]
    public async Task HighlightsPage_KeepsEachDateBesideItsEventAndTheChevronBesideItsTitle(int width, int textPercent, string locale)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = locale,
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });
        var currentYear = DateTime.UtcNow.Year;
        var historicalYear = currentYear - 1;
        var eventsBody = JsonSerializer.Serialize(new object[]
        {
            new { Id = "timeline-announcement", Type = 6, Text = "Season update\n\nThe full announcement is here.", OccurredAt = new DateTimeOffset(currentYear, 9, 26, 8, 0, 0, TimeSpan.Zero), Relevance = 10 },
            new { Id = "timeline-short", Type = 6, Text = "Short note", OccurredAt = new DateTimeOffset(currentYear, 9, 15, 8, 0, 0, TimeSpan.Zero), Relevance = 10 },
            new { Id = "timeline-historical", Type = 6, Text = "Historical note", OccurredAt = new DateTimeOffset(historicalYear, 9, 26, 8, 0, 0, TimeSpan.Zero), Relevance = 10 }
        });
        await RouteEventBoardDependenciesAsync(context, eventsBody);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("tr[data-event-id='timeline-short']").WaitForAsync(new() { State = WaitForSelectorState.Visible });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        Assert.Contains(historicalYear.ToString(), await page.Locator("tr.main-row[data-event-id='timeline-historical'] .col-date").InnerTextAsync());

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const board = document.querySelector('.events-board').getBoundingClientRect();
                for (const row of document.querySelectorAll('#eventsTable tbody tr.main-row')) {
                    const date = row.querySelector('.col-date');
                    const text = row.querySelector('.event-message-cell');
                    const dateBox = date.getBoundingClientRect();
                    const textBox = text.getBoundingClientRect();
                    const id = row.dataset.eventId;
                    if (!date.textContent.trim() || dateBox.width === 0 || getComputedStyle(date).display === 'none')
                        issues.push(`${id}: date hidden`);
                    if (dateBox.left < board.left - 1 || dateBox.right > board.right + 1)
                        issues.push(`${id}: date leaves the board`);
                    const dateStyle = getComputedStyle(date);
                    const dateRange = document.createRange();
                    // Wide, the day leads and its month is the chapter heading above.
                    dateRange.selectNodeContents(innerWidth > 600 ? date.querySelector('.event-date-day') ?? date : date);
                    for (const line of dateRange.getClientRects()) {
                        if (line.width > 0 && (line.left < dateBox.left + parseFloat(dateStyle.paddingLeft) - 1
                            || line.right > dateBox.right - parseFloat(dateStyle.paddingRight) + 1))
                            issues.push(`${id}: date text escapes its column`);
                    }
                    // Wide: the date leads the row, right beside the portrait. Narrow: it labels its day over the message.
                    const besideMessage = dateBox.right <= textBox.left + 1 && textBox.left - dateBox.right < 120;
                    const overMessage = dateBox.bottom <= textBox.top + 1 && textBox.top - dateBox.bottom < 12 && Math.abs(dateBox.left - textBox.left) <= 1;
                    const repeatedDay = innerWidth <= 600 && row.classList.contains('day-repeat') && dateBox.width <= 1;
                    if (!besideMessage && !overMessage && !repeatedDay) issues.push(`${id}: date is far from its event`);
                }
                const title = document.querySelector('tr.main-row[data-event-id="timeline-announcement"] .custom-event-title');
                const expander = title.querySelector('.custom-event-expander');
                const range = document.createRange();
                range.selectNodeContents(title.firstChild);
                const words = [...range.getClientRects()].pop();
                const chevron = expander.getBoundingClientRect();
                const glyph = getComputedStyle(expander, '::before');
                // Translation can report 43.9999847 for a 44px box. Tolerate
                // floating-point noise while still rejecting actual scaling.
                if (chevron.width + .001 < 44 || chevron.height + .001 < 44) {
                    const transforms=[];
                    for(let node=expander;node;node=node.parentElement){const value=getComputedStyle(node).transform;if(value!=='none')transforms.push([node.tagName,node.className,value]);}
                    issues.push(`chevron touch target is small (${chevron.width}x${chevron.height}; ${JSON.stringify(transforms)})`);
                }
                // After the last word, or (when the title fills its line) starting the next line under it.
                const afterWords = Math.abs(chevron.top + chevron.height / 2 - (words.top + words.height / 2)) <= 4
                    && chevron.left >= words.right - 1 && chevron.left - words.right <= 16;
                const underTitle = chevron.top >= words.bottom - 1 && chevron.top - words.bottom <= 12
                    && Math.abs(chevron.left - parseFloat(getComputedStyle(expander).marginLeft) - title.getBoundingClientRect().left) <= 2;
                if (!afterWords && !underTitle) issues.push(`chevron is not beside its title (words ${Math.round(words.left)}-${Math.round(words.right)} x ${Math.round(words.top)}-${Math.round(words.bottom)}, chevron ${Math.round(chevron.left)} x ${Math.round(chevron.top)}-${Math.round(chevron.bottom)}, title left ${Math.round(title.getBoundingClientRect().left)})`);
                if (getComputedStyle(expander).position === 'static' && glyph.position === 'absolute')
                    issues.push('chevron glyph escapes its button');
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");

        var row = page.Locator("tr.main-row[data-event-id='timeline-announcement']");
        var wasOpen = await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')");
        await row.Locator(".custom-event-expander").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.NotEqual(wasOpen, await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        if (wasOpen) await page.Keyboard.PressAsync("Enter");
        Assert.True(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        var alignment = await page.EvaluateAsync<double[]>(
            """
            () => {
                const row = document.querySelector('tr.main-row[data-event-id="timeline-announcement"]');
                const details = row.nextElementSibling.querySelector('.details-cell');
                const content = details.querySelector('.custom-event-content').getBoundingClientRect();
                return [content.left, row.querySelector('.event-message-cell').getBoundingClientRect().left,
                    details.getBoundingClientRect().width, row.getBoundingClientRect().width];
            }
            """);
        Assert.InRange(Math.Abs(alignment[0] - alignment[1]), 0, 1);
        Assert.InRange(Math.Abs(alignment[2] - alignment[3]), 0, 1);
        await page.Keyboard.PressAsync("Enter");
        Assert.False(await row.EvaluateAsync<bool>("element => element.classList.contains('is-open')"));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('tr.main-row[data-event-id=\"timeline-announcement\"]').nextElementSibling.getBoundingClientRect().height <= 1");
    }

    private static async Task AssertExpanderIsContainedAndCenteredAsync(
        ILocator row,
        ILocator expander)
    {
        // Entrance motion moves both elements together. Sample their geometry
        // in one frame so movement between protocol calls cannot skew the result.
        var bounds = await row.EvaluateAsync<float[]>("""
            row => {
                const rowBox = row.getBoundingClientRect();
                const expanderBox = row.querySelector('.custom-event-expander').getBoundingClientRect();
                return [rowBox.top, rowBox.bottom, expanderBox.top, expanderBox.bottom];
            }
            """);
        var transform = await expander.EvaluateAsync<string>("element => getComputedStyle(element).transform");
        var rowCenter = (bounds[0] + bounds[1]) / 2;
        var expanderCenter = (bounds[2] + bounds[3]) / 2;

        Assert.Equal("none", transform);
        Assert.True(
            bounds[2] >= bounds[0] && bounds[3] <= bounds[1],
            $"Expected the expander to stay within the row. Row: {bounds[0]}-{bounds[1]}; expander: {bounds[2]}-{bounds[3]}.");
        Assert.True(
            Math.Abs(rowCenter - expanderCenter) < 1,
            $"Expected the expander to be vertically centered. Row center: {rowCenter}; expander center: {expanderCenter}.");
    }

    [Fact]
    public async Task AthleteEmbed_ImprovementLeaderEventStartsSentenceOnNameLine()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 635, Height = 160 }
        });
        await RouteEventBoardDependenciesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Error fetching athletes:", StringComparison.Ordinal))
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync(
            "/event-board-embed.html?athlete=majoros_gabor&rows=all&viewAll=false&linkNames=false&theme=dark&embed=1",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#eventsTable tbody tr.main-row .event-message-cell");

        var text = (await page.Locator(".event-message-cell").First.InnerTextAsync()).Replace('\u00A0', ' ');
        Assert.Contains("Majoros Gábor (#99) took 1st place in Pheno Improvement", text);

        var diagnosticsJson = await page.EvaluateAsync<string>(
            """
            () => {
                const cell = document.querySelector('.event-message-cell');
                const name = cell?.querySelector('.name-and-rank');
                let textNode = name?.nextSibling || null;
                while (textNode && (textNode.nodeType !== Node.TEXT_NODE || !textNode.textContent.trim())) {
                    textNode = textNode.nextSibling;
                }

                const start = textNode ? textNode.textContent.search(/\S/) : -1;
                const range = document.createRange();
                if (textNode && start >= 0) {
                    range.setStart(textNode, start);
                    range.setEnd(textNode, Math.min(textNode.textContent.length, start + 4));
                }

                const nameRect = name?.getBoundingClientRect();
                const wordRect = textNode && start >= 0 ? range.getBoundingClientRect() : null;
                const cellRect = cell?.getBoundingClientRect();
                return JSON.stringify({
                    text: cell?.innerText || '',
                    display: cell ? getComputedStyle(cell).display : '',
                    cellWidth: cellRect?.width || 0,
                    nameTop: nameRect?.top || 0,
                    wordTop: wordRect?.top || 0,
                    nameRight: nameRect?.right || 0,
                    wordLeft: wordRect?.left || 0
                });
            }
            """);

        using var diagnostics = JsonDocument.Parse(diagnosticsJson);
        var root = diagnostics.RootElement;
        var display = root.GetProperty("display").GetString();
        var nameTop = root.GetProperty("nameTop").GetDouble();
        var wordTop = root.GetProperty("wordTop").GetDouble();

        Assert.Equal("block", display);
        Assert.True(
            Math.Abs(nameTop - wordTop) < 3,
            $"Expected the sentence to continue on the athlete-name line. Diagnostics: {diagnosticsJson}");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(635)]
    [InlineData(360)]
    public async Task AcceptedTestsAppearOnlyOnTheirAthletesProfileWithTheCorrectTestDate(int width)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            TimezoneId = "America/Los_Angeles",
            ViewportSize = new ViewportSize { Width = width, Height = 480 }
        });
        var eventsBody = JsonSerializer.Serialize(new[]
        {
            new { Id = "accepted-latest", Type = 13, Text = "slug[majoros_gabor] date[2026-08-31]", OccurredAt = DateTime.UtcNow, Relevance = 5 },
            new { Id = "accepted-backfill", Type = 13, Text = "slug[majoros_gabor] date[2024-01-01]", OccurredAt = DateTime.UtcNow, Relevance = 5 },
            new { Id = "accepted-other", Type = 13, Text = "slug[nopara73] date[2026-08-31]", OccurredAt = DateTime.UtcNow, Relevance = 5 },
            new { Id = "improvement", Type = 10, Text = "slug[majoros_gabor] clock[pheno] from[40] to[35]", OccurredAt = DateTime.UtcNow, Relevance = 9 }
        });
        await RouteEventBoardDependenciesAsync(context, eventsBody);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Console += (_, message) =>
        {
            if (message.Type == "error" && !message.Text.StartsWith("Error fetching athletes:", StringComparison.Ordinal))
                errors.Add(message.Text);
        };

        await page.GotoAsync("/event-board-embed.html?athlete=majoros_gabor&rows=all&viewAll=false&linkNames=false&theme=dark&embed=1");
        var latest = page.Locator("tr[data-event-id='accepted-latest']");
        try
        {
            await latest.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        }
        catch (TimeoutException)
        {
            throw new Xunit.Sdk.XunitException(string.Join("\n", errors) + "\n" + await page.Locator("body").InnerTextAsync());
        }
        Assert.Contains("Majoros Gábor (#99) submitted a new test (Aug 31", (await latest.InnerTextAsync()).Replace('\u00A0', ' '));
        Assert.Contains("submitted a new test (Jan 1, 2024)", await page.Locator("tr[data-event-id='accepted-backfill']").InnerTextAsync());
        Assert.Equal(3, await page.Locator("tr.main-row").CountAsync());
        Assert.Equal(0, await page.Locator("tr[data-event-id='accepted-other']").CountAsync());
        Assert.Equal(1, await latest.Locator(".avatar-disabled").CountAsync());
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth"));

        foreach (var url in new[] { "/events", "/" })
        {
            await page.GotoAsync(url);
            await page.Locator("tr[data-event-id='improvement']").WaitForAsync();
            Assert.Equal(0, await page.Locator("tr[data-event-id^='accepted-']").CountAsync());
        }
        Assert.Empty(errors);
    }

    private static async Task RouteEventBoardDependenciesAsync(IBrowserContext context, string? eventsBody = null)
    {
        eventsBody ??=
            """
            [
              {
                "Id": "event-improvement-leader",
                "Type": 12,
                "Text": "slug[majoros_gabor] clock[pheno] place[1] prev[nopara73] improvement[-9.15] ageReduction[-12.15]",
                "OccurredAt": "2026-06-29T08:00:00Z",
                "Relevance": 10
              }
            ]
            """;

        await context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri))
            {
                await route.ContinueAsync();
                return;
            }

            if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback)
            {
                if (uri.AbsolutePath.Equals("/api/events", StringComparison.OrdinalIgnoreCase))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body = eventsBody
                    });
                    return;
                }

                if (uri.AbsolutePath.Equals("/api/data/athletes", StringComparison.OrdinalIgnoreCase))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body =
                            """
                            [
                              {
                                "Key": "majoros_gabor",
                                "Name": "Majoros Gábor",
                                "CurrentPlacement": 99
                              },
                              {
                                "Key": "nopara73",
                                "Name": "nopara73",
                                "CurrentPlacement": 19
                              }
                            ]
                            """
                    });
                    return;
                }

                await route.ContinueAsync();
                return;
            }

            if (route.Request.ResourceType == "script")
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/javascript",
                    Body = uri.AbsolutePath.Contains("/aos/", StringComparison.OrdinalIgnoreCase)
                        ? "window.AOS={init(){},refresh(){}};"
                        : ""
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = route.Request.ResourceType == "stylesheet" ? "text/css" : "text/plain",
                Body = ""
            });
        });
    }
}
