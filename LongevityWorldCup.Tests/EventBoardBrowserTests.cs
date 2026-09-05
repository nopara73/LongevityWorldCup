using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class EventBoardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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

    private static async Task AssertExpanderIsContainedAndCenteredAsync(
        ILocator row,
        ILocator expander)
    {
        var rowBox = Assert.IsType<LocatorBoundingBoxResult>(await row.BoundingBoxAsync());
        var expanderBox = Assert.IsType<LocatorBoundingBoxResult>(await expander.BoundingBoxAsync());
        var transform = await expander.EvaluateAsync<string>("element => getComputedStyle(element).transform");
        var rowCenter = rowBox.Y + (rowBox.Height / 2);
        var expanderCenter = expanderBox.Y + (expanderBox.Height / 2);

        Assert.Equal("none", transform);
        Assert.True(
            expanderBox.Y >= rowBox.Y && expanderBox.Y + expanderBox.Height <= rowBox.Y + rowBox.Height,
            $"Expected the expander to stay within the row. Row: {rowBox.Y}-{rowBox.Y + rowBox.Height}; expander: {expanderBox.Y}-{expanderBox.Y + expanderBox.Height}.");
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
