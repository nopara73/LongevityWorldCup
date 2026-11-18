using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepageHighlightsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Athletes = """
        [{"AthleteSlug":"ada_aging","Name":"Ada Aging","CurrentPlacement":3,"ProfilePicThumb":"/assets/favicon-64x64.png"},
         {"AthleteSlug":"bo_biomarker","Name":"Bo Biomarker","CurrentPlacement":12}]
        """;
    private const string Events = """
        [{"Id":"future","Type":4,"Text":"athletes[300]","OccurredAt":"2026-11-17T01:00:00Z","Relevance":10},
         {"Id":"pro","Type":9,"Text":"slug[ada_aging]","OccurredAt":"2026-11-17T00:10:00Z","Relevance":10},
         {"Id":"milestone","Type":4,"Text":"athletes[250]","OccurredAt":"2026-11-16T23:50:00Z","Relevance":10},
         {"Id":"joined","Type":1,"Text":"slug[bo_biomarker]","OccurredAt":"2026-11-16T23:30:00Z","Relevance":5},
         {"Id":"donation","Type":3,"Text":"tx[abc] sats[8455]","OccurredAt":"2026-11-15T10:00:00Z","Relevance":5}]
        """;

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1200, 900, "dark")]
    [InlineData(992, 768, "light")]
    [InlineData(768, 1024, "dark")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task CompactTimelineAlignsEveryKindOfPortraitAndGroupsLocalDays(int width, int height, string scheme)
    {
        await using var context = await NewContextAsync(width, height, scheme);
        var page = await OpenAsync(context);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const problems = [];
                const rows = [...document.querySelectorAll('#events-root-index tr.main-row')];
                let previousDay = '';
                for (const row of rows) {
                    const style = getComputedStyle(row);
                    const image = row.querySelector('.avatar, .avatar-fallback');
                    const box = image.getBoundingClientRect();
                    const railX = row.getBoundingClientRect().left + parseFloat(style.backgroundPositionX) + 1;
                    if (!style.backgroundImage.includes('gradient') || Math.abs(railX - box.left - box.width / 2) > 1)
                        problems.push(`${row.dataset.eventId}: rail misses portrait`);
                    if (Math.abs(box.width - 30) > 1 || Math.abs(box.height - 30) > 1)
                        problems.push(`${row.dataset.eventId}: inconsistent portrait size ${box.width}x${box.height}`);
                    if (!image.closest('.event-avatar-frame')) problems.push('unframed event icon');
                    const repeated = row.dataset.day === previousDay;
                    if (row.classList.contains('day-repeat') !== repeated) problems.push('incorrect day group');
                    const time = row.querySelector('time[datetime]');
                    if (!time?.textContent.trim() || !time.getAttribute('aria-label')) problems.push('missing accessible date');
                    if (time && getComputedStyle(time).opacity !== (repeated ? '0' : '1')) problems.push('repeated date is visible');
                    previousDay = row.dataset.day;
                }
                const board = document.querySelector('#events-root-index .events-board').getBoundingClientRect();
                const swag = document.querySelector('#lwc-countdown-box').getBoundingClientRect();
                if (innerWidth >= 992 && (Math.abs(board.top - swag.top) > 1 || Math.abs(board.bottom - swag.bottom) > 1))
                    problems.push('paired cards are misaligned');
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page overflows');
                return problems;
            }
            """);
        Assert.Empty(problems);
        Assert.Equal("grid", await page.Locator("#events-root-index .event-kind").First
            .EvaluateAsync<string>("e => getComputedStyle(e).display"));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Bangkok")]
    [InlineData("America/Los_Angeles")]
    public async Task HomepageAndFullTimelineShareFreshnessAcrossMidnightAndExpiry(string timezone)
    {
        await using var context = await NewContextAsync(1440, 900, "light", timezone);
        var page = await OpenAsync(context);
        foreach (var route in new[] { "/", "/events" })
        {
            if (route != "/") await page.GotoAsync(route);
            await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(5);
            var fresh = await page.Locator("tr.main-row.is-fresh").EvaluateAllAsync<string[]>(
                "rows => rows.map(r => r.dataset.eventId).sort()");
            Assert.Equal(new[] { "joined", "milestone", "pro" }, fresh);
            var localDay = await page.EvaluateAsync<string>("""
                () => { const d = new Date('2026-11-16T23:50:00Z'); return `${d.getFullYear()}-${d.getMonth()+1}-${d.getDate()}`; }
                """);
            await Assertions.Expect(page.Locator("tr[data-event-id='milestone']")).ToHaveAttributeAsync("data-day", localDay);
        }
        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("tr.main-row.is-fresh")).ToHaveCountAsync(3);
        await page.Clock.FastForwardAsync(25 * 60 * 60 * 1000);
        await Assertions.Expect(page.Locator("tr.main-row.is-fresh")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task NestedTextChangesDoNotRestyleTheFeedAndRefreshRetainsFocusedRows()
    {
        await using var context = await NewContextAsync(1440, 900, "dark");
        var page = await OpenAsync(context);
        await page.Locator("tr[data-event-id='pro'] .avatar-link").FocusAsync();
        var writes = await page.EvaluateAsync<int>("""
            async () => {
                const rows = [...document.querySelectorAll('#events-root-index tr.main-row')];
                window.reviewRows = rows;
                window.reviewFocus = document.activeElement;
                const styles = new Set(rows.map(row => row.style));
                const original = CSSStyleDeclaration.prototype.setProperty;
                let writes = 0;
                CSSStyleDeclaration.prototype.setProperty = function(...args) {
                    if (styles.has(this)) writes++;
                    return original.apply(this, args);
                };
                try {
                    rows[1].querySelector('.event-message-cell').append(document.createTextNode(''));
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    return writes;
                } finally { CSSStyleDeclaration.prototype.setProperty = original; }
            }
            """);
        Assert.Equal(0, writes);
        var response = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/events"));
        await page.EvaluateAsync("() => window.refreshEventsFeed()");
        await response;
        await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "false");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => window.reviewRows.every((row, i) => document.querySelectorAll('#events-root-index tr.main-row')[i] === row)
                && window.reviewFocus === document.activeElement
                && [...document.querySelectorAll('#events-root-index .event-message-cell')].every(e => getComputedStyle(e).opacity === '1')
            """));
    }

    private async Task<IBrowserContext> NewContextAsync(int width, int height, string scheme, string timezone = "UTC")
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", TimezoneId = timezone,
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce, ViewportSize = new() { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Events }));
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync(new() { Time = "2026-11-17T00:30:00Z" });
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#events-root-index tr.main-row")).ToHaveCountAsync(5);
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }
}
