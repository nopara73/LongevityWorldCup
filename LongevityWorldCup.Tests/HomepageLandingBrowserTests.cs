using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepageLandingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(1024, 768, 100)]
    [InlineData(768, 1024, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(1024, 768, 200)]
    [InlineData(800, 900, 200)]
    public async Task Podium_StandsOnOneStageCardWhoseFloorIsThePrizeSteps(int width, int height, int textPercent)
    {
        var (context, page) = await OpenHomepageAsync(width, height);
        await using var _ = context;
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var stage = await page.EvaluateAsync<StageMeasurement>(
            """
            () => {
                const podium = document.querySelector('.podium');
                const style = getComputedStyle(podium);
                const box = podium.getBoundingClientRect();
                const cards = [...podium.querySelectorAll('.podium-item:not(.podium-skeleton-item)')];
                const rect = rank => cards.find(card => card.classList.contains(rank)).getBoundingClientRect();
                const steps = [...podium.querySelectorAll('.podium-item-lower')].map(step => step.getBoundingClientRect());
                return {
                    BorderWidth: parseFloat(style.outlineWidth) * (style.outlineStyle === 'solid' && parseFloat(style.outlineOffset) === -parseFloat(style.outlineWidth) ? 1 : 0),
                    Radius: parseFloat(style.borderTopLeftRadius),
                    Overflow: style.overflowX,
                    Left: box.left,
                    Right: box.right,
                    Bottom: box.bottom,
                    FirstLeft: rect('first').left,
                    FirstRight: rect('first').right,
                    FirstBottom: rect('first').bottom,
                    SecondTop: rect('second').top,
                    SecondBottom: rect('second').bottom,
                    ThirdTop: rect('third').top,
                    SecondRight: rect('second').right,
                    ThirdLeft: rect('third').left,
                    CardShadows: cards.filter(card => getComputedStyle(card).boxShadow !== 'none').length,
                    LowestStepBottom: Math.max(...steps.map(step => step.bottom))
                };
            }
            """);

        Assert.Equal(1, stage.BorderWidth);
        Assert.True(stage.Radius >= 8, "The stage must be a rounded card.");
        Assert.Equal("hidden", stage.Overflow);
        Assert.Equal(0, stage.CardShadows);
        Assert.InRange(stage.Bottom - stage.LowestStepBottom, 0, 2);
        var stacked = stage.SecondRight > stage.ThirdLeft + 1;
        if (stacked)
        {
            Assert.InRange(stage.SecondTop - stage.FirstBottom, 0, 1);
            Assert.InRange(stage.ThirdTop - stage.SecondBottom, 0, 1);
        }
        else
            Assert.InRange(Math.Abs(stage.SecondRight - stage.ThirdLeft), 0, width > 768 ? 400 : 1);
        if (width <= 768 || stacked)
        {
            Assert.InRange(stage.FirstLeft - stage.Left, 0, 2);
            Assert.InRange(stage.Right - stage.FirstRight, 0, 2);
        }
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task Board_JoinsControlsStandingsAndViewAllAthletesInOneCard(int width, int height)
    {
        var (context, page) = await OpenHomepageAsync(width, height);
        await using var _ = context;

        var board = await page.EvaluateAsync<BoardMeasurement>(
            """
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const footer = document.querySelector('.full-rankings-action');
                const style = getComputedStyle(footer);
                return {
                    ControlsBottom: rect('.leaderboard-controls').bottom,
                    StandingsTop: rect('#athleteLeaderboard').top,
                    StandingsBottom: rect('#athleteLeaderboard').bottom,
                    FooterTop: rect('.full-rankings-action').top,
                    FooterLeft: rect('.full-rankings-action').left,
                    FooterRight: rect('.full-rankings-action').right,
                    StandingsLeft: rect('#athleteLeaderboard').left,
                    StandingsRight: rect('#athleteLeaderboard').right,
                    FooterBorder: parseFloat(style.borderBottomWidth),
                    FooterRadius: parseFloat(style.borderBottomLeftRadius),
                    StandingsRadius: parseFloat(getComputedStyle(document.querySelector('#athleteLeaderboard')).borderBottomLeftRadius)
                };
            }
            """);

        Assert.InRange(Math.Abs(board.StandingsTop - board.ControlsBottom), 0, 1.5);
        Assert.InRange(Math.Abs(board.FooterTop - board.StandingsBottom), 0, 1.5);
        Assert.InRange(Math.Abs(board.FooterLeft - board.StandingsLeft), 0, 1);
        Assert.InRange(Math.Abs(board.FooterRight - board.StandingsRight), 0, 1);
        Assert.Equal(1, board.FooterBorder);
        Assert.True(board.FooterRadius >= 8, "The board must close on rounded corners under View all athletes.");
        Assert.Equal(0, board.StandingsRadius);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, 24)]
    [InlineData(1024, 768, 24)]
    [InlineData(390, 844, 16)]
    public async Task Sections_FollowTheThreeActRhythm(int width, int height, double gap)
    {
        var (context, page) = await OpenHomepageAsync(width, height);
        await using var _ = context;

        var gaps = await page.EvaluateAsync<double[]>(
            """
            () => {
                const blocks = ['.podium', '.leaderboard-controls', '.full-rankings-action',
                    '.lwc-highlights-countdown-wrapper', '#hall-of-fame', '#faq', '.homepage-panel-pair']
                    .map(selector => document.querySelector(selector).getBoundingClientRect());
                const pairs = [[0, 1], [2, 3], [3, 4], [4, 5], [5, 6]];
                return pairs.map(([above, below]) => blocks[below].top - blocks[above].bottom);
            }
            """);

        // Within an act cards keep one gap; acts breathe twice as wide, and
        // Hall of fame and the FAQ are one block joined at a seam.
        var expected = new[] { gap, gap, gap * 2, 0, gap * 2 };
        for (var i = 0; i < gaps.Length; i++)
            Assert.InRange(gaps[i], expected[i] - 1, expected[i] + 1);
    }

    [Fact]
    public async Task Highlights_KeepTheirRowsWhenOnlyTheWindowHeightChanges()
    {
        var (context, page) = await OpenHomepageAsync(1440, 900);
        await using var _ = context;
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#events-root-index tr.main-row').length > 2");
        await page.WaitForTimeoutAsync(1500);
        var rows = await CountHighlightsAsync(page);
        await page.EvaluateAsync("() => window.retainedHighlights = [...document.querySelectorAll('#events-root-index tr.main-row')]");

        await page.SetViewportSizeAsync(1440, 600);
        await page.WaitForTimeoutAsync(260);
        Assert.Equal(rows, await CountHighlightsAsync(page));

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(260);
        Assert.Equal(rows, await CountHighlightsAsync(page));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => window.retainedHighlights.every(row => row.isConnected)"),
            "A height-only resize must retain the fitted rows themselves.");
    }

    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(1024, 768, 200)]
    [InlineData(844, 390, 200)]
    [InlineData(390, 844, 100)]
    [InlineData(390, 844, 200)]
    [InlineData(320, 640, 200)]
    public async Task Podium_LongNamesMedalsAndPrizeGlyphsStayInsideTheirPlacing(int width, int height, int textPercent)
    {
        var (context, page) = await OpenHomepageAsync(width, height);
        await using var _ = context;
        await page.EvaluateAsync(
            """
            percent => {
                document.documentElement.style.fontSize = percent + '%';
                const names = ['Alexandertheexceptionallylongunbrokentokenforreflow',
                    'Nóra Marosvölgyi A Very Long Competition Name', 'Siim Land'];
                document.querySelectorAll('.podium .athlete-name').forEach((name, index) => name.textContent = names[index]);
            }
            """, textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const stage = document.querySelector('.podium').getBoundingClientRect();
                for (const card of document.querySelectorAll('.podium-item')) {
                    const box = card.getBoundingClientRect();
                    for (const element of card.querySelectorAll('.athlete-name, .age-reduction, .prize-money, .btc-amount')) {
                        const range = document.createRange();
                        range.selectNodeContents(element);
                        for (const r of range.getClientRects()) {
                            if (r.width > 0 && (r.left < Math.max(box.left, stage.left) - 1
                                || r.right > Math.min(box.right, stage.right) + 1
                                || r.top < Math.max(box.top, stage.top) - 1
                                || r.bottom > Math.min(box.bottom, stage.bottom) + 1))
                                problems.push(card.className + ': ' + element.textContent);
                        }
                    }
                    const medal = card.querySelector('.podium-rank').getBoundingClientRect();
                    if (medal.left < box.left || medal.right > box.right || medal.top < box.top || medal.bottom > box.bottom)
                        problems.push(card.className + ': clipped medal');
                }
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, string.Join("; ", problems));
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("content", false)]
    [InlineData("content", true)]
    [InlineData("text", false)]
    [InlineData("width", false)]
    public async Task Highlights_RefitWhenTheirLayoutInputsChangeWithoutAWindowResize(string change, bool repeat)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        if (repeat)
            await context.AddInitScriptAsync("localStorage.setItem('lwcHomepageVisitCount:v1', '4')");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#events-root-index tr.main-row");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.WaitForTimeoutAsync(3500);
        var initialRows = await CountHighlightsAsync(page);
        Assert.InRange(initialRows, 2, 11);
        var initialWidth = await page.EvaluateAsync<int>("innerWidth");

        await page.EvaluateAsync(
            """
            change => {
                if (change === 'content') {
                    const content = document.createElement('div');
                    content.id = 'product-information-fixture';
                    content.style.cssText = 'height:400px;flex-shrink:0';
                    content.textContent = 'Expanded product information';
                    document.querySelector('.lwc-merch-body').append(content);
                } else if (change === 'text') {
                    document.documentElement.style.fontSize = '200%';
                } else {
                    document.querySelector('.lwc-highlights-countdown-wrapper').style.maxWidth = '700px';
                }
            }
            """, change);
        await page.WaitForFunctionAsync(
            "initial => document.querySelectorAll('#events-root-index tr.main-row').length !== initial", initialRows);
        await page.WaitForTimeoutAsync(3500);
        Assert.Equal(initialWidth, await page.EvaluateAsync<int>("innerWidth"));

        await page.EvaluateAsync(
            """
            change => {
                if (change === 'content') document.getElementById('product-information-fixture').remove();
                else if (change === 'text') document.documentElement.style.fontSize = '';
                else document.querySelector('.lwc-highlights-countdown-wrapper').style.maxWidth = '';
            }
            """, change);
        await page.WaitForFunctionAsync(
            "initial => document.querySelectorAll('#events-root-index tr.main-row').length === initial", initialRows);
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(initialRows, await CountHighlightsAsync(page));
    }

    [Fact]
    public async Task Highlights_RefittingRetainsAnExpandedAnnouncementAndItsKeyboardFocus()
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var events = Enumerable.Range(0, 12).Select(index => new
        {
            Id = $"announcement-{index}", Type = 6, Relevance = 10,
            OccurredAt = new DateTime(2026, 10, 19, 8, 0, 0, DateTimeKind.Utc).AddDays(-index),
            Text = $"Season announcement {index}\n\nThe complete announcement. [Read the rules](https://longevityworldcup.com/ruleset)"
        });
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = JsonSerializer.Serialize(events)
        }));
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#events-root-index .custom-event-expander");
        await page.WaitForTimeoutAsync(3500);
        var expander = page.Locator("#events-root-index .custom-event-expander").Last;
        await expander.FocusAsync();
        await expander.PressAsync("Enter");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await page.EvaluateAsync(
            """
            () => {
                window.retainedAnnouncement = document.activeElement;
                window.retainedAnnouncementRow = document.activeElement.closest('tr.main-row');
                document.querySelector('.lwc-merch-copy').style.paddingBottom = '400px';
            }
            """);
        await page.WaitForTimeoutAsync(3500);
        await page.EvaluateAsync("() => document.querySelector('.lwc-merch-copy').style.paddingBottom = ''");
        await page.WaitForTimeoutAsync(3500);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => window.retainedAnnouncement === document.activeElement
                && window.retainedAnnouncementRow.isConnected
                && window.retainedAnnouncement.getAttribute('aria-expanded') === 'true'
            """), "Refitting must retain the reader's expanded row, control and focus.");
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task Highlights_LoadAsOneListAndKeepDecodedIconsDuringLayoutRefits(int width)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.NoPreference);
        await context.AddInitScriptAsync("""
            localStorage.setItem('lwcHomepageVisitCount:v1', '4');
            window.highlightPaints = [];
            window.highlightGhosts = 0;
            const sampleHighlights = () => {
                const rows = [...document.querySelectorAll('#events-root-index tr.main-row')];
                if (rows.length) {
                    const ids = rows.map(row => row.dataset.eventId).join(',');
                    if (highlightPaints.at(-1) !== ids) highlightPaints.push(ids);
                    highlightGhosts = Math.max(highlightGhosts, document.querySelectorAll('.events-exit-ghost').length);
                }
                window.highlightFrame = requestAnimationFrame(sampleHighlights);
            };
            requestAnimationFrame(sampleHighlights);
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync("#events-root-index tr.main-row");
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.WaitForTimeoutAsync(1800);
        await page.EvaluateAsync("() => cancelAnimationFrame(highlightFrame)");
        Assert.Single(await page.EvaluateAsync<string[]>("highlightPaints"));
        Assert.Equal(0, await page.EvaluateAsync<int>("highlightGhosts"));

        var stable = await page.EvaluateAsync<bool>("""
            async () => {
                const root = document.querySelector('#events-root-index');
                const images = [...root.querySelectorAll('img.avatar')];
                const rows = [...root.querySelectorAll('tr.main-row')];
                let removals = 0;
                const observer = new MutationObserver(records => {
                    for (const record of records) for (const node of record.removedNodes)
                        if (node.nodeType === 1)
                            removals += node.matches('img.avatar') ? 1 : node.querySelectorAll('img.avatar').length;
                });
                observer.observe(root, { childList: true, subtree: true });
                await fitHomepageHighlights();
                document.fonts.dispatchEvent(new Event('loadingdone'));
                await new Promise(resolve => setTimeout(resolve, 500));
                observer.disconnect();
                return removals === 0 && rows.every(row => row.isConnected)
                    && images.every(image => image.isConnected);
            }
            """);
        Assert.True(stable, "A layout refit must not detach decoded icons or replay feed animations.");
    }

    private static Task<int> CountHighlightsAsync(IPage page) =>
        page.EvaluateAsync<int>("() => document.querySelectorAll('#events-root-index tr.main-row').length");

    private async Task<(IBrowserContext, IPage)> OpenHomepageAsync(int width, int height)
    {
        var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.WaitForSelectorAsync("#eventsTable tr.main-row");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return (context, page);
    }

    private sealed class StageMeasurement
    {
        public double BorderWidth { get; set; }
        public double Radius { get; set; }
        public string Overflow { get; set; } = "";
        public double Left { get; set; }
        public double Right { get; set; }
        public double Bottom { get; set; }
        public double FirstLeft { get; set; }
        public double FirstRight { get; set; }
        public double FirstBottom { get; set; }
        public double SecondTop { get; set; }
        public double SecondBottom { get; set; }
        public double ThirdTop { get; set; }
        public double SecondRight { get; set; }
        public double ThirdLeft { get; set; }
        public int CardShadows { get; set; }
        public double LowestStepBottom { get; set; }
    }

    private sealed class BoardMeasurement
    {
        public double ControlsBottom { get; set; }
        public double StandingsTop { get; set; }
        public double StandingsBottom { get; set; }
        public double FooterTop { get; set; }
        public double FooterLeft { get; set; }
        public double FooterRight { get; set; }
        public double StandingsLeft { get; set; }
        public double StandingsRight { get; set; }
        public double FooterBorder { get; set; }
        public double FooterRadius { get; set; }
        public double StandingsRadius { get; set; }
    }
}
