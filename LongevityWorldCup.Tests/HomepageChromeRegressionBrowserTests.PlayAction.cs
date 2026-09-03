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

    private static Task<ScrollPhaseDiagnostics> MeasureScrollPhasesAsync(IPage page)
        => page.EvaluateAsync<ScrollPhaseDiagnostics>(
            """
            async () => {
                await (document.fonts?.ready || Promise.resolve());
                const settleAt = async requestedTop => {
                    for (let frame = 0; frame < 3; frame += 1) {
                        const maxScroll = Math.max(0, document.documentElement.scrollHeight - innerHeight);
                        const target = Math.min(Math.max(0, requestedTop), maxScroll);
                        if (Math.abs(scrollY - target) > 0.5)
                            window.scrollTo({ top: target, behavior: 'instant' });
                        await new Promise(resolve => requestAnimationFrame(resolve));
                    }
                };
                const measure = () => [...document.querySelectorAll('header[role="banner"] .join-game')]
                    .map(action => {
                        const style = getComputedStyle(action);
                        const rect = action.getBoundingClientRect();
                        const brand = action.classList.contains('scrolled-button')
                            ? document.querySelector('.site-sticky-header-link')
                            : document.querySelector('header[role="banner"] .header-link');
                        const brandRect = brand?.getBoundingClientRect();
                        const background = style.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
                        return {
                            Name: action.getAttribute('aria-label'),
                            Text: action.innerText.trim().replace(/\s+/g, ' '),
                            Foreground: style.color,
                            BackgroundImage: style.backgroundImage,
                            IsScrolled: action.classList.contains('scrolled-button'),
                            Display: style.display,
                            Visibility: style.visibility,
                            ScrollY: scrollY,
                            MaxScroll: Math.max(0, document.documentElement.scrollHeight - innerHeight),
                            Visible: style.display !== 'none' && style.visibility !== 'hidden'
                                && rect.width > 0 && rect.height > 0
                                && rect.right > 0 && rect.left < innerWidth
                                && rect.bottom > 0 && rect.top < innerHeight,
                            Left: rect.left,
                            Right: rect.right,
                            Top: rect.top,
                            Bottom: rect.bottom,
                            Width: rect.width,
                            Height: rect.height,
                            OverlapsBrand: brandRect
                                ? rect.left < brandRect.right && rect.right > brandRect.left
                                    && rect.top < brandRect.bottom && rect.bottom > brandRect.top
                                : false,
                            BackgroundAlpha: background[3] ?? 1
                        };
                    });

                await settleAt(0);
                const atTop = measure();
                await settleAt(52);
                const atStickyBoundary = measure();
                const stickyHeaderVisible = document.getElementById('site-sticky-header')
                    ?.classList.contains('visible') === true;
                await settleAt(700);
                const afterScroll = measure();
                return { AtTop: atTop, AtStickyBoundary: atStickyBoundary, AfterScroll: afterScroll, StickyHeaderVisible: stickyHeaderVisible };
            }
            """);

    private sealed class ScrollPhaseDiagnostics
    {
        public VisibleActionDiagnostics[] AtTop { get; set; } = [];
        public VisibleActionDiagnostics[] AtStickyBoundary { get; set; } = [];
        public VisibleActionDiagnostics[] AfterScroll { get; set; } = [];
        public bool StickyHeaderVisible { get; set; }
    }

}
