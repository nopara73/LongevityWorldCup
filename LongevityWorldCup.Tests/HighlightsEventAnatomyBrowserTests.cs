using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// Each Highlights entry says what kind of moment it is from its portrait, names
// read as names instead of a row of underlines, and badges sit on the text line.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsEventAnatomyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Events =
        """
        [
          { "Id": "a-joined", "Type": 1, "Text": "slug[ada_aging]", "OccurredAt": "2026-10-20T10:00:00Z", "Relevance": 5 },
          { "Id": "a-rank", "Type": 2, "Text": "slug[ada_aging] rank[3] prev[bo_biomarker]", "OccurredAt": "2026-10-19T10:00:00Z", "Relevance": 10 },
          { "Id": "a-badge", "Type": 5, "Text": "slug[bo_biomarker] badge[Best domain – vitamin D] cat[Global] val[] place[1]", "OccurredAt": "2026-10-18T10:00:00Z", "Relevance": 10 },
          { "Id": "a-pro", "Type": 9, "Text": "slug[bo_biomarker]", "OccurredAt": "2026-10-17T10:00:00Z", "Relevance": 10 },
          { "Id": "a-younger", "Type": 10, "Text": "slug[ada_aging] clock[pheno] from[41.94] to[40.29]", "OccurredAt": "2026-10-16T10:00:00Z", "Relevance": 10 },
          { "Id": "a-crowd", "Type": 11, "Text": "slug[bo_biomarker] place[5] prevPlace[7] prev[ada_aging] crowdAge[46.5] crowdCount[862]", "OccurredAt": "2026-10-15T10:00:00Z", "Relevance": 10 },
          { "Id": "a-improvement", "Type": 12, "Text": "slug[ada_aging] clock[bortz] place[5] prev[bo_biomarker] improvement[-1.19] ageReduction[-15.9]", "OccurredAt": "2026-10-14T10:00:00Z", "Relevance": 10 },
          { "Id": "a-test", "Type": 13, "Text": "slug[bo_biomarker] date[2026-04-14]", "OccurredAt": "2026-10-13T10:00:00Z", "Relevance": 5 },
          { "Id": "a-season", "Type": 7, "Text": "slug[ada_aging] season[2025] place[2] clock[PhenoAge] ageDiff[-5.28]", "OccurredAt": "2026-10-12T10:00:00Z", "Relevance": 10 },
          { "Id": "a-challenge", "Type": 8, "Text": "challenge[longevitymaxxing] pid[p1] name[Ada Aging] place[1] checkedIn[14] points[125] days[14] slug[ada_aging] completed[1]", "OccurredAt": "2026-10-11T10:00:00Z", "Relevance": 10 },
          { "Id": "a-donation", "Type": 3, "Text": "tx[abc] sats[8455]", "OccurredAt": "2026-10-10T10:00:00Z", "Relevance": 5 }
        ]
        """;

    private const string Athletes =
        """
        [
          { "AthleteSlug": "ada_aging", "Name": "Ada Aging", "CurrentPlacement": 3, "ProfilePicThumb": "/missing/ada-aging.webp" },
          { "AthleteSlug": "bo_biomarker", "Name": "Bo Biomarker", "CurrentPlacement": 12 }
        ]
        """;

    [Theory]
    [InlineData(1280, "light")]
    [InlineData(390, "dark")]
    public async Task EachAthleteMomentCarriesItsKindOnThePortrait(int width, string scheme)
    {
        await using var context = await OpenEventsAsync(width, scheme);
        var page = context.Pages[0];

        var expected = new Dictionary<string, string>
        {
            ["a-joined"] = "joined",
            ["a-rank"] = "rank",
            ["a-badge"] = "badge",
            ["a-pro"] = "pro",
            ["a-younger"] = "younger",
            ["a-crowd"] = "crowd",
            ["a-improvement"] = "improvement",
            ["a-challenge"] = "challenge"
        };
        foreach (var (id, kind) in expected)
        {
            var mark = page.Locator($"tr.main-row[data-event-id='{id}'] td.col-avatar .event-kind");
            Assert.True(await mark.CountAsync() == 1, $"{id} has no kind mark; rows: "
                + string.Join(",", await page.Locator("tr.main-row").EvaluateAllAsync<string[]>("rows => rows.map(r => r.dataset.eventId)")));
            Assert.Equal(kind, await mark.GetAttributeAsync("data-kind"));
            Assert.Equal("true", await mark.GetAttributeAsync("aria-hidden"));
        }

        // Rows that are already an icon carry no second mark.
        Assert.Equal(0, await page.Locator("tr.main-row[data-event-id='a-donation'] .event-kind").CountAsync());

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const mark of document.querySelectorAll('.event-board-page .event-kind')) {
                    const row = mark.closest('tr').dataset.eventId;
                    const portrait = mark.parentElement.querySelector('.avatar, .avatar-fallback');
                    if (!portrait) { issues.push(`${row}: mark lost its portrait`); continue; }
                    const p = portrait.getBoundingClientRect(), m = mark.getBoundingClientRect();
                    const style = getComputedStyle(mark);
                    if (style.display === 'none' || m.width < 14 || m.width > 20) issues.push(`${row}: mark is ${m.width}px`);
                    // It sits on the portrait's lower right edge, not beside it.
                    const cx = m.left + m.width / 2, cy = m.top + m.height / 2;
                    if (cx < p.left + p.width / 2 || cx > p.right + 2 || cy < p.top + p.height / 2 || cy > p.bottom + 2)
                        issues.push(`${row}: mark is not on the portrait's lower right`);
                    const luminance = color => {
                        const channels = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(v => {
                            const c = v / 255;
                            return c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4;
                        });
                        return channels[0] * .2126 + channels[1] * .7152 + channels[2] * .0722;
                    };
                    const ink = luminance(getComputedStyle(mark.querySelector('i')).color);
                    const fill = luminance(style.backgroundColor);
                    const contrast = (Math.max(ink, fill) + .05) / (Math.min(ink, fill) + .05);
                    if (contrast < 3) issues.push(`${row}: glyph contrast is ${contrast.toFixed(2)}:1`);
                }
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows horizontally');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme}: {string.Join("; ", problems)}");
    }

    [Fact]
    public async Task ABrokenPortraitKeepsItsKindMark()
    {
        await using var context = await OpenEventsAsync(1280, "light");
        var page = context.Pages[0];
        var frame = page.Locator("tr.main-row[data-event-id='a-joined'] .event-avatar-frame");
        await Assertions.Expect(frame.Locator(".avatar-fallback")).ToHaveTextAsync("AA");
        Assert.Equal(0, await frame.Locator("img").CountAsync());
        Assert.Equal("joined", await frame.Locator(".event-kind").GetAttributeAsync("data-kind"));
        var link = frame.Locator("..");
        Assert.Equal("/athlete/ada-aging", await link.GetAttributeAsync("href"));
        Assert.Equal("Ada Aging", await link.GetAttributeAsync("aria-label"));
        Assert.Equal("ada-aging", await link.GetAttributeAsync("data-athlete-slug"));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("dark")]
    public async Task ProfileFeedKeepsLossMeaningAndBrokenPortraitNavigation(string scheme)
    {
        await using var context = await NewContextAsync(390, scheme);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/event-board-embed.html?embed=1&athlete=bo-biomarker&rows=all&theme={scheme}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var rank = page.Locator("tr.main-row[data-event-id='a-rank']");
        await Assertions.Expect(rank).ToContainTextAsync("lost");
        await Assertions.Expect(rank.Locator(".event-kind")).ToHaveAttributeAsync("data-lost", "true");
        Assert.Equal("none", await rank.Locator(".event-kind").EvaluateAsync<string>("e => getComputedStyle(e).display"));
        Assert.Equal(1, await rank.Locator(".fa-arrow-trend-down").CountAsync());
        Assert.Equal("test", await page.Locator("tr.main-row[data-event-id='a-test'] .event-kind").GetAttributeAsync("data-kind"));

        // Ada's profile projects Bo's Crowd Age gain as Ada's loss, retaining
        // the winner's original new-tab name link.
        await page.GotoAsync($"/event-board-embed.html?embed=1&athlete=ada-aging&rows=all&theme={scheme}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var lostCrowd = page.Locator("tr.main-row[data-event-id='a-crowd']");
        await Assertions.Expect(lostCrowd).ToContainTextAsync("lost");
        await Assertions.Expect(lostCrowd.Locator(".event-kind")).ToHaveAttributeAsync("data-lost", "true");
        Assert.Equal(1, await lostCrowd.Locator(".fa-arrow-trend-down").CountAsync());
        var winner = lostCrowd.GetByRole(AriaRole.Link, new() { Name = "Bo Biomarker", Exact = true });
        Assert.Equal("/athlete/bo-biomarker", await winner.GetAttributeAsync("href"));
        Assert.Equal("_blank", await winner.GetAttributeAsync("target"));
        Assert.Equal("season", await page.Locator("tr.main-row[data-event-id='a-season'] .event-kind").GetAttributeAsync("data-kind"));

        // Ask for Bo's profile again, with an event that mentions Ada but gives no
        // loss projection. The broken image must keep its own new-tab link.
        var moreEvents = System.Text.Json.Nodes.JsonNode.Parse(Events)!.AsArray();
        moreEvents.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"Id":"cross-portrait","Type":1,"Text":"slug[ada_aging] slug[bo_biomarker]","OccurredAt":"2026-10-22T10:00:00Z"}"""));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() {ContentType="application/json", Body=moreEvents.ToJsonString()}));
        await page.GotoAsync($"/event-board-embed.html?embed=1&athlete=bo-biomarker&rows=all&theme={scheme}",
            new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var portrait = page.Locator("tr.main-row[data-event-id='cross-portrait'] .avatar-link");
        await Assertions.Expect(portrait.Locator(".avatar-fallback")).ToHaveTextAsync("AA");
        Assert.Equal("/athlete/ada-aging", await portrait.GetAttributeAsync("href"));
        Assert.Equal("_blank", await portrait.GetAttributeAsync("target"));
        Assert.Contains("noopener", await portrait.GetAttributeAsync("rel"));
        await context.RouteAsync("**/athlete/ada-aging", route => route.FulfillAsync(new() {ContentType="text/html", Body="<title>Ada destination</title>"}));
        var popup = await page.RunAndWaitForPopupAsync(() => portrait.ClickAsync());
        await Assertions.Expect(popup).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/athlete/ada-aging$"));
        await popup.CloseAsync();
    }

    [Fact]
    public async Task UnknownAndAggregateEventsKeepTheirExistingMeaning()
    {
        await using var context = await NewContextAsync(1280, "light");
        var extra = System.Text.Json.Nodes.JsonNode.Parse(Events)!.AsArray();
        extra.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"Id":"unknown","Type":987,"Text":"An unfamiliar event","OccurredAt":"2026-10-21T10:00:00Z"}"""));
        extra.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"Id":"second-badge","Type":5,"Text":"slug[bo_biomarker] badge[PhenoAge - lowest] cat[Global] val[] place[2]","OccurredAt":"2026-10-18T10:00:05Z"}"""));
        extra.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"Id":"no-athlete-challenge","Type":8,"Text":"name[Challenge Guest] place[4] checkedIn[0] points[0] days[14]","OccurredAt":"2026-10-21T09:00:00Z"}"""));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() {ContentType="application/json", Body=extra.ToJsonString()}));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("tr.main-row[data-event-id='unknown']")).ToContainTextAsync("An unfamiliar event");
        Assert.Equal(0, await page.Locator("tr.main-row[data-event-id='unknown'] .event-kind").CountAsync());
        var badges = page.Locator("tr.main-row[data-event-id='a-badge']");
        Assert.Equal("badge", await badges.Locator(".event-kind").GetAttributeAsync("data-kind"));
        await Assertions.Expect(badges).ToContainTextAsync("2");
        var challenge = page.Locator("tr.main-row[data-event-id='no-athlete-challenge']");
        Assert.Equal(0, await challenge.Locator(".event-kind").CountAsync());
        await Assertions.Expect(challenge).ToContainTextAsync("0/14 checked-in days and 0 points");
        Assert.Equal(new[] {"0/14", "0"}, await challenge.Locator(".count-accent").AllInnerTextsAsync());
    }

    [Theory]
    [InlineData(1280, "light")]
    [InlineData(390, "dark")]
    public async Task NamesReadAsNamesAndTheRankStaysQuiet(int width, string scheme)
    {
        await using var context = await OpenEventsAsync(width, scheme);
        var page = context.Pages[0];
        var row = page.Locator("tr.main-row[data-event-id='a-rank']");
        var name = row.Locator(".event-message .name-and-rank > a.event-athlete-link").First;
        var rank = row.Locator(".event-message .name-and-rank .placement-link").First;
        await Assertions.Expect(name).ToHaveTextAsync("Ada Aging");
        await Assertions.Expect(rank).ToHaveTextAsync("(#3)");

        var styles = await row.EvaluateAsync<string[]>(
            """
            row => {
                const name = getComputedStyle(row.querySelector('.name-and-rank > a.event-athlete-link'));
                const rank = getComputedStyle(row.querySelector('.name-and-rank .placement-link'));
                const text = getComputedStyle(row.querySelector('.event-message-cell'));
                return [name.textDecorationLine, name.fontWeight, rank.color, name.color,
                        String(parseFloat(rank.fontSize) < parseFloat(text.fontSize))];
            }
            """);
        Assert.Equal("none", styles[0]);
        Assert.True(int.Parse(styles[1]) >= 700, $"name weight {styles[1]}");
        Assert.NotEqual(styles[3], styles[2]);
        Assert.Equal("true", styles[4]);
        Assert.Equal(0, await row.Locator(".rank-badge strong[style]").CountAsync());
        Assert.Equal(await name.EvaluateAsync<string>("e => getComputedStyle(e).color"),
            await row.Locator(".rank-badge strong").First.EvaluateAsync<string>("e => getComputedStyle(e).color"));

        // Hover still shows it is a link.
        if (width > 600)
        {
            await name.HoverAsync();
            Assert.Contains("underline", await name.EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine"));
        }
        await rank.FocusAsync();
        await Assertions.Expect(rank).ToHaveCSSAsync("color",
            await name.EvaluateAsync<string>("e => getComputedStyle(e).color"));
        Assert.Contains("underline", await rank.EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine"));
    }

    [Fact]
    public async Task AnimatedRerendersReleaseOldRowsAndKeepOneResizeHandler()
    {
        await using var context = await NewContextAsync(1280, "light");
        await context.AddInitScriptAsync(
            """
            (() => {
                const listeners = new Set();
                const add = window.addEventListener, remove = window.removeEventListener;
                window.addEventListener = function(type, fn, options) {
                    if (type === 'resize') listeners.add(fn);
                    return add.call(this, type, fn, options);
                };
                window.removeEventListener = function(type, fn, options) {
                    if (type === 'resize') listeners.delete(fn);
                    return remove.call(this, type, fn, options);
                };
                const targets = new Set();
                const NativeObserver = window.IntersectionObserver;
                window.IntersectionObserver = class extends NativeObserver {
                    observed = new Set();
                    observe(element) { this.observed.add(element); targets.add(element); super.observe(element); }
                    unobserve(element) { this.observed.delete(element); targets.delete(element); super.unobserve(element); }
                    disconnect() { this.observed.forEach(element => targets.delete(element)); this.observed.clear(); super.disconnect(); }
                };
                window.eventLifecycle = () => ({
                    listeners: listeners.size,
                    detachedRows: [...targets].filter(element => element.matches('tr.row-appear') && !element.isConnected).length
                });
            })();
            """);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('#events-root')?.getAttribute('aria-busy') === 'false'");
        var listeners = await page.EvaluateAsync<int>("() => eventLifecycle().listeners");
        for (var i = 0; i < 4; i++)
        {
            await page.EvaluateAsync("() => loadEventsTable(null, false)");
            await page.WaitForFunctionAsync("() => document.querySelector('#events-root')?.getAttribute('aria-busy') === 'false'");
        }
        Assert.Equal(listeners, await page.EvaluateAsync<int>("() => eventLifecycle().listeners"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => eventLifecycle().detachedRows"));
        var last = page.Locator("tr.main-row[data-event-id='a-donation']");
        await last.ScrollIntoViewIfNeededAsync();
        await page.SetViewportSizeAsync(1000, 700);
        await Assertions.Expect(last).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bis-visible\b"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.EvaluateAsync("() => loadEventsTable(null, false)");
        await page.WaitForFunctionAsync("() => document.querySelector('#events-root')?.getAttribute('aria-busy') === 'false'");
        Assert.Equal(0, await page.Locator("tr.row-appear:not(.is-visible)").CountAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("() => eventLifecycle().detachedRows"));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task ABadgeSitsOnItsTextLine(int width)
    {
        await using var context = await OpenEventsAsync(width, "light");
        var page = context.Pages[0];
        var offset = await page.Locator("tr.main-row[data-event-id='a-badge'] .event-message-cell").EvaluateAsync<double>(
            """
            cell => {
                const badge = cell.querySelector('.badge-class').getBoundingClientRect();
                const word = [...cell.childNodes].find(node => node.nodeType === 3 && node.textContent.includes('adds'));
                const range = document.createRange();
                range.selectNodeContents(word);
                const line = [...range.getClientRects()].filter(rect => rect.width > 0).pop();
                return (badge.top + badge.height / 2) - (line.top + line.height / 2);
            }
            """);
        Assert.InRange(offset, -3, 3);
    }

    [Fact]
    public async Task ChallengeResultsAccentOnlyTheirNumbers()
    {
        await using var context = await OpenEventsAsync(1280, "light");
        var page = context.Pages[0];
        var message = page.Locator("tr.main-row[data-event-id='a-challenge'] .event-message-cell");
        await Assertions.Expect(message).ToContainTextAsync("with 14/14 checked-in days and 125 points");
        var accents = await message.Locator(".count-accent").AllInnerTextsAsync();
        Assert.Equal(new[] { "14/14", "125" }, accents);
    }

    [Fact]
    public async Task CompactFeedsLeaveTheirSmallPortraitsUnmarked()
    {
        await using var context = await NewContextAsync(1280, "light");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var mark = page.Locator("#events-root tr.main-row .event-kind").First;
        await mark.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        Assert.Equal("none", await mark.EvaluateAsync<string>("e => getComputedStyle(e).display"));
        Assert.Equal("none", await page.Locator("#events-root .name-and-rank > a").First
            .EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine"));
    }

    private async Task<IBrowserContext> OpenEventsAsync(int width, string scheme)
    {
        var context = await NewContextAsync(width, scheme);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("tr.main-row[data-event-id='a-donation']").WaitForAsync();
        return context;
    }

    private async Task<IBrowserContext> NewContextAsync(int width, string scheme)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            TimezoneId = "UTC",
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });
        await context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri))
            {
                await route.ContinueAsync();
                return;
            }

            if (uri.IsLoopback)
            {
                var body = uri.AbsolutePath.Equals("/api/events", StringComparison.OrdinalIgnoreCase) ? Events
                    : uri.AbsolutePath.Equals("/api/data/athletes", StringComparison.OrdinalIgnoreCase) ? Athletes
                    : null;
                if (body is not null)
                    await route.FulfillAsync(new RouteFulfillOptions { Status = 200, ContentType = "application/json", Body = body });
                else
                    await route.ContinueAsync();
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = route.Request.ResourceType switch
                {
                    "script" => "application/javascript",
                    "stylesheet" => "text/css",
                    _ => "text/plain"
                },
                Body = uri.AbsolutePath.Contains("/aos/", StringComparison.OrdinalIgnoreCase) ? "window.AOS={init(){},refresh(){}};" : ""
            });
        });
        return context;
    }
}
