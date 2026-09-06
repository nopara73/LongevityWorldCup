using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.HomepageChromeRegressionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class HomepagePlayActionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task PlayAction_IsNeverAbsentInCompactLandscapeOrAfterScrolling()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        // /play deliberately removes the redundant join buttons because it is
        // already their destination. The dedicated event board also keeps its
        // long-standing full-screen chrome. These routes retain the global CTA.
        await Parallel.ForEachAsync(
            new[] { "/", "/leaderboard", "/ruleset", "/history" },
            new ParallelOptions { MaxDegreeOfParallelism = 4 },
            async (path, _) =>
            {
                var page = await context.NewPageAsync();
                try
                {
                    await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
                    foreach (var viewport in new[]
                             {
                                 new ViewportSize { Width = 320, Height = 720 },
                                 new ViewportSize { Width = 390, Height = 844 },
                                 new ViewportSize { Width = 641, Height = 844 },
                                 new ViewportSize { Width = 720, Height = 900 },
                                 new ViewportSize { Width = 667, Height = 375 },
                                 new ViewportSize { Width = 844, Height = 390 },
                                 new ViewportSize { Width = 900, Height = 450 },
                                 new ViewportSize { Width = 1026, Height = 473 }
                             })
                    {
                        await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                        var phases = await MeasureScrollPhasesAsync(page);

                        var atTopActions = phases.AtTop;
                        var atTop = atTopActions
                            .Where(action => IsActionFullyInsideViewport(viewport, action))
                            .ToArray();
                        Assert.True(atTop.Length > 0,
                            $"{path} had no fully visible Play action at the top of {viewport.Width}x{viewport.Height}. " +
                            DescribeActions(atTopActions));
                        Assert.All(atTop, action => AssertActionInsideViewport(path, viewport, action));

                        if (phases.StickyHeaderVisible)
                        {
                            var stickyAction = Assert.Single(
                                phases.AtStickyBoundary,
                                action => action.Visible && action.IsScrolled);
                            AssertActionInsideViewport(path, viewport, stickyAction);
                        }

                        var afterScrollActions = phases.AfterScroll;
                        var afterScroll = afterScrollActions
                            .Where(action => IsActionFullyInsideViewport(viewport, action))
                            .ToArray();
                        Assert.True(afterScroll.Length > 0,
                            $"{path} had no fully visible Play action after scrolling at {viewport.Width}x{viewport.Height}. " +
                            DescribeActions(afterScrollActions));
                        Assert.All(afterScroll, action => AssertActionInsideViewport(path, viewport, action));
                    }
                }
                finally
                {
                    await page.CloseAsync();
                }
            });
    }

    [Theory]
    [InlineData(ReducedMotion.NoPreference)]
    [InlineData(ReducedMotion.Reduce)]
    public async Task HeaderResize_KeepsTheBrandSeparateFromPlayThroughoutTheTransition(ReducedMotion motion)
    {
        await using var context = await NewContextAsync(Browser, App, motion);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(900, 450);
        await page.GotoAsync("/leaderboard");
        await SettleLayoutAsync(page);
        await page.EvaluateAsync(
            """
            () => {
                window.headerResizeSamples = [];
                window.headerResizeComplete = false;
                window.addEventListener('resize', () => {
                    let frames = 0;
                    const sample = () => {
                        const brand = document.querySelector('header[role="banner"] .header-link').getBoundingClientRect();
                        const action = document.querySelector('header[role="banner"] .join-game:not(.scrolled-button)').getBoundingClientRect();
                        window.headerResizeSamples.push({
                            BrandRight: brand.right,
                            ActionLeft: action.left,
                            Overlap: action.left < brand.right && action.right > brand.left
                                && action.top < brand.bottom && action.bottom > brand.top
                        });
                        if (++frames < 20) requestAnimationFrame(sample);
                        else window.headerResizeComplete = true;
                    };
                    sample();
                }, { once: true });
            }
            """);

        await page.SetViewportSizeAsync(1026, 473);
        await page.WaitForFunctionAsync("() => window.headerResizeComplete === true");
        var samples = await page.EvaluateAsync<HeaderResizeSample[]>("() => window.headerResizeSamples");
        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.False(sample.Overlap,
            $"Brand ended at {sample.BrandRight:F1}px while Play began at {sample.ActionLeft:F1}px during resize."));
    }

    private sealed class HeaderResizeSample
    {
        public double BrandRight { get; set; }
        public double ActionLeft { get; set; }
        public bool Overlap { get; set; }
    }

    private static async Task<ScrollPhaseDiagnostics> MeasureScrollPhasesAsync(IPage page)
    {
        await ScrollToStablePositionAsync(page, 0);
        var atTop = await MeasurePlayActionsAsync(page);
        await ScrollToStablePositionAsync(page, 52);
        var atStickyBoundary = await MeasurePlayActionsAsync(page);
        var stickyHeaderVisible = await page.EvaluateAsync<bool>(
            "() => document.getElementById('site-sticky-header')?.classList.contains('visible') === true");
        await ScrollToStablePositionAsync(page, 700);
        var afterScroll = await MeasurePlayActionsAsync(page);
        return new ScrollPhaseDiagnostics
        {
            AtTop = atTop,
            AtStickyBoundary = atStickyBoundary,
            AfterScroll = afterScroll,
            StickyHeaderVisible = stickyHeaderVisible
        };
    }

    private sealed class ScrollPhaseDiagnostics
    {
        public VisibleActionDiagnostics[] AtTop { get; set; } = [];
        public VisibleActionDiagnostics[] AtStickyBoundary { get; set; } = [];
        public VisibleActionDiagnostics[] AfterScroll { get; set; } = [];
        public bool StickyHeaderVisible { get; set; }
    }

}
