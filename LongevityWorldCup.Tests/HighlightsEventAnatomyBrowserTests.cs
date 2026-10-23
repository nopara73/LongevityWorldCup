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
                    if (style.backgroundColor === style.color) issues.push(`${row}: glyph has no contrast`);
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

        // Hover still shows it is a link.
        if (width > 600)
        {
            await name.HoverAsync();
            Assert.Contains("underline", await name.EvaluateAsync<string>("e => getComputedStyle(e).textDecorationLine"));
        }
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
