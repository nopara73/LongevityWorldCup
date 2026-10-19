using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepageLandingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1024, 768)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
    public async Task Podium_StandsOnOneStageCardWhoseFloorIsThePrizeSteps(int width, int height)
    {
        var (context, page) = await OpenHomepageAsync(width, height);
        await using var _ = context;

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
        Assert.InRange(Math.Abs(stage.SecondRight - stage.ThirdLeft), 0, width > 768 ? 400 : 1);
        if (width <= 768)
        {
            Assert.InRange(stage.FirstLeft - stage.Left, 0, 2);
            Assert.InRange(stage.Right - stage.FirstRight, 0, 2);
        }
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(768, 1024)]
    [InlineData(390, 844)]
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
    public async Task Sections_FollowOneVerticalRhythm(int width, int height, double gap)
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

        Assert.All(gaps, measured => Assert.InRange(measured, gap - 1, gap + 1));
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

        await page.SetViewportSizeAsync(1440, 600);
        await page.WaitForTimeoutAsync(260);
        Assert.Equal(rows, await CountHighlightsAsync(page));

        await page.SetViewportSizeAsync(1440, 900);
        await page.WaitForTimeoutAsync(260);
        Assert.Equal(rows, await CountHighlightsAsync(page));
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
