using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsRocketBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Athlete = """
        [{"Key":"rocket-athlete","AthleteSlug":"rocket-athlete","Name":"Nóra Marosvölgyi","CurrentPlacement":232,
          "DateOfBirth":{"Year":1980,"Month":1,"Day":1},"Biomarkers":[],"Placements":[],"Badges":[],"Proofs":[],"Division":"Women's","Flag":"Hungary"}]
        """;
    private const string Events = """
        [
          {"Id":"rocket-joined","Type":1,"Text":"slug[rocket-athlete]","OccurredAt":"2026-10-18T08:00:00Z","Relevance":10},
          {"Id":"rocket-custom","Type":6,"Text":"Season update\n\nA message for slug[rocket-athlete]. [Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-17T08:00:00Z","Relevance":10},
          {"Id":"rocket-rank","Type":2,"Text":"slug[rocket-athlete] rank[3]","OccurredAt":"2026-10-16T08:00:00Z","Relevance":10},
          {"Id":"rocket-missing","Type":1,"Text":"slug[undated-athlete]","Relevance":1},
          {"Id":"rocket-invalid","Type":1,"Text":"slug[invalid-date-athlete]","OccurredAt":"unknown","Relevance":1}
        ]
        """;

    [Fact]
    public async Task ShortHomepageDate_DoesNotLeaveAgoOnItsOwnLine()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            TimezoneId = "UTC",
            ReducedMotion = ReducedMotion.Reduce
        });
        await RouteFixturesAsync(context, joinedOnly: true);
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, "/");
        var row = page.Locator("tr[data-event-id='rocket-joined'] .event-message-cell");
        await row.Locator(".fa-rocket").WaitForAsync();
        var lineDrift = await row.EvaluateAsync<double>(
            """
            cell => {
                const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT);
                while (walker.nextNode()) {
                    const node = walker.currentNode;
                    const start = node.textContent.indexOf('days');
                    if (start < 0) continue;
                    const range = document.createRange();
                    range.setStart(node, start);
                    range.setEnd(node, start + 4);
                    return Math.abs(range.getBoundingClientRect().top
                        - cell.querySelector('.event-rocket-tail').getBoundingClientRect().top);
                }
                throw new Error('The three-day fixture was not rendered.');
            }
            """);
        Assert.InRange(lineDrift, 0, 1);
    }

    [Theory]
    [InlineData("/events", false)]
    [InlineData("/events", true)]
    [InlineData("/", false)]
    [InlineData("/", true)]
    [InlineData("/event-board-embed.html?embed=1&athlete=rocket-athlete", false)]
    [InlineData("/event-board-embed.html?embed=1&athlete=rocket-athlete&theme=dark", true)]
    public async Task RelativeDates_ReflowOnTheHomepageFeedAndAthleteEmbed(string path, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            TimezoneId = "UTC",
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        // Desktop fitting can select only one highlight. Keep the joined event
        // eligible instead of competing with a higher-priority rank for its athlete.
        await RouteFixturesAsync(context, yesterday: true, joinedOnly: path == "/");
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, path);
        await page.Locator("tr[data-event-id='rocket-joined'] .fa-rocket").WaitForAsync();
        // The 320px athlete dialog leaves only 238px for its Highlights iframe.
        var widths = path.StartsWith("/event-board-embed")
            ? new[] { 238, 320, 390, 600, 768, 1440 }
            : new[] { 320, 390, 600, 768, 1440 };
        foreach (var width in widths)
        foreach (var percent in new[] { 100, 200 })
        {
            await page.SetViewportSizeAsync(width, width == 1440 ? 900 : 844);
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", percent);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
            if (path == "/")
                await page.EvaluateAsync("() => fitHomepageHighlights()");
            // Resolve the rendered fixture and measure its font and geometry in
            // one browser task, so a refit cannot replace rows between checks.
            var layout = await MeasureRocketLayoutAsync(page);
            Assert.True(layout.FontSize >= 13 * percent / 100d, $"{path}: actual text is only {layout.FontSize}px at {percent}%");
            Assert.True(layout.Problems.Length == 0, $"{path} {width}px / {percent}%: {string.Join("; ", layout.Problems)}");
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RelativeDates_KeepWordingDatesAccessibilityAndOneRocketThroughTimeAndRerenders()
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = "UTC" });
        await RouteFixturesAsync(context);
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, "/events");
        var row = page.Locator("tr[data-event-id='rocket-joined']");
        await row.Locator(".fa-rocket").WaitForAsync();
        foreach (var (now, label) in new[]
        {
            ("2026-10-17", "Today"), ("2026-10-18", "Today"), ("2026-10-19", "Yesterday"),
            ("2026-10-21", "3 days ago"), ("2026-11-18", "1 month ago"),
            ("2026-12-18", "2 months ago"), ("2027-10-18", "1 year ago"), ("2028-10-18", "2 years ago")
        })
        {
            await page.Clock.SetFixedTimeAsync(DateTime.Parse(now + "T12:00:00Z").ToUniversalTime());
            for (var pass = 0; pass < 2; pass++)
            {
                await page.EvaluateAsync("async () => { window.__rocketPreviousRow = document.querySelector('tr[data-event-id=\"rocket-joined\"]'); await loadEventsTable(null, true, null, true); }");
                Assert.True(await page.EvaluateAsync<bool>("() => window.__rocketPreviousRow === document.querySelector('tr[data-event-id=\"rocket-joined\"]')"));
                await Assertions.Expect(row.Locator(".fa-rocket")).ToHaveCountAsync(1);
                await Assertions.Expect(row.Locator(".event-message-cell")).ToHaveTextAsync($"Nóra Marosvölgyi (#232) joined {label}");
                await Assertions.Expect(row.Locator(".col-date")).ToHaveTextAsync(now.StartsWith("2026") ? "Oct 18" : "Oct 18, 2026");
                Assert.Equal("true", await row.Locator(".fa-rocket").GetAttributeAsync("aria-hidden"));
                Assert.Null(await row.Locator(".event-rocket-tail").GetAttributeAsync("aria-hidden"));
                await Assertions.Expect(row.Locator(".event-message .event-athlete-link")).ToHaveAccessibleNameAsync("Nóra Marosvölgyi");
                await Assertions.Expect(row.Locator(".placement-link")).ToHaveAccessibleNameAsync("(#232)");
            }
        }
        foreach (var id in new[] { "rocket-missing", "rocket-invalid", "rocket-custom", "rocket-rank" })
            await Assertions.Expect(page.Locator($"tr[data-event-id='{id}'] .fa-rocket")).ToHaveCountAsync(0);
        foreach (var id in new[] { "rocket-missing", "rocket-invalid" })
            Assert.Equal("", await page.Locator($"tr[data-event-id='{id}'] .col-date").TextContentAsync());
        var expander = page.Locator("tr[data-event-id='rocket-custom'] .custom-event-expander");
        await expander.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Read the rules" })).ToBeVisibleAsync();
    }

    private static async Task RouteFixturesAsync(IBrowserContext context, bool yesterday = false, bool joinedOnly = false)
    {
        var events = JsonNode.Parse(Events)!.AsArray();
        if (joinedOnly)
            foreach (var item in events.Where(item => item!["Id"]!.GetValue<string>() != "rocket-joined").ToArray())
                events.Remove(item);
        var body = events.ToJsonString();
        if (yesterday)
            body = body.Replace("2026-10-18T08:00:00Z", "2026-10-17T08:00:00Z");
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athlete }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = body
        }));
    }

    private sealed class RocketLayout
    {
        public double FontSize { get; set; }
        public string[] Problems { get; set; } = [];
    }

    private static async Task<RocketLayout> MeasureRocketLayoutAsync(IPage page)
    {
        await using var snapshot = await page.WaitForFunctionAsync(MeasureRocketLayout);
        return await snapshot.JsonValueAsync<RocketLayout>();
    }

    private const string MeasureRocketLayout = """
        () => {
            const fixture = document.querySelector("tr[data-event-id='rocket-joined'] .event-message-cell");
            if (!fixture?.isConnected || !fixture.querySelector('.fa-rocket')) return false;
            if (fixture.closest('[aria-busy="true"]')) return false;
            const fontSize = parseFloat(getComputedStyle(fixture).fontSize);
            if (!Number.isFinite(fontSize)) return false;
            const issues = [];
            const rockets = [...document.querySelectorAll('.events-board .fa-rocket')];
            if (!rockets.length) issues.push('fixture has no rocket');
            for (const rocket of rockets) {
                const tail = rocket.closest('.event-rocket-tail');
                const range = document.createRange();
                range.selectNodeContents(tail);
                const fragments = [...range.getClientRects()].filter(r => r.width);
                const icon = rocket.getBoundingClientRect();
                if (fragments.some(r => Math.abs((r.top + r.bottom - icon.top - icon.bottom) / 2) > r.height / 2))
                    issues.push('last word and rocket split across lines');
                const cell = rocket.closest('.event-message-cell').getBoundingClientRect();
                if (fragments.some(r => r.left < cell.left - 1 || r.right > cell.right + 1))
                    issues.push('rocket tail escapes its message');
                for (let el = rocket.parentElement; el; el = el.parentElement) {
                    const box = el.getBoundingClientRect(), css = getComputedStyle(el);
                    if (['hidden', 'clip', 'auto', 'scroll'].includes(css.overflowX)
                        && fragments.some(r => r.left < box.left - 1 || r.right > box.right + 1))
                        issues.push('rocket tail is clipped by ' + el.className);
                }
            }
            if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
            for (const rank of document.querySelectorAll('.events-board .placement-link')) {
                const range = document.createRange(); range.selectNodeContents(rank);
                if (new Set([...range.getClientRects()].filter(r => r.width).map(r => Math.round(r.top))).size > 1)
                    issues.push('rank token breaks');
            }
            return { FontSize: fontSize, Problems: issues };
        }
        """;
}
