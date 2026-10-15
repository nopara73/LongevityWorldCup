using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeaderboardSidebarMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/leaderboard", false)]
    [InlineData("/leaderboard?group=flags", true)]
    public async Task Expansion_RevealsWholeControlsAfterTheRailFitsThem(string path, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 844 },
            ReducedMotion = ReducedMotion.NoPreference,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var rail = page.Locator(".sidebar");
        var collapsedWidth = (await rail.BoundingBoxAsync())!.Width;
        await page.Locator(".sidebar-toggle").HoverAsync();
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");
        Assert.InRange(Math.Abs((await rail.BoundingBoxAsync())!.Width - collapsedWidth), 0, 1);

        // Start and pause in one browser operation so even a busy runner inspects
        // the actual intermediate layout instead of missing a short transition.
        var widths = await page.EvaluateAsync<double[]>("""
            () => {
                const rail = document.querySelector('.sidebar');
                document.querySelector('.sidebar-toggle').click();
                rail.getBoundingClientRect();
                const transition = rail.getAnimations().find(a => a.transitionProperty === 'width');
                if (!transition) throw new Error('Expected a sidebar width transition');
                const duration = transition.effect.getTiming().duration;
                const animations = rail.getAnimations({subtree:true})
                    .filter(a => a === transition || a.animationName === 'sidebar-panel-reveal');
                animations.forEach(a => a.pause());
                const measure = () => [rail, rail.querySelector('.sidebar-heading'), rail.querySelector('.sidebar-filter-list'), rail.querySelector('.filter-section label')]
                    .map(element => element.getBoundingClientRect().width);
                animations.forEach(a => { a.currentTime = duration / 3; });
                const during = measure();
                const list = rail.querySelector('.sidebar-filter-list');
                const controlsVisible = () => list.checkVisibility({opacityProperty:true, visibilityProperty:true});
                if (controlsVisible())
                    throw new Error('Partially opened rail exposes cropped filter controls');
                animations.forEach(a => { a.currentTime = duration * 0.95; });
                if (controlsVisible())
                    throw new Error('Filters appear before the rail finishes widening');
                animations.forEach(a => a.finish());
                const after = measure();
                if (!controlsVisible()) throw new Error('Fully opened filters remain hidden');

                document.querySelector('.sidebar-close').click();
                rail.getBoundingClientRect();
                const closing = rail.getAnimations().find(a => a.transitionProperty === 'width');
                closing.pause();
                closing.currentTime = duration / 3;
                if (rail.querySelector('.filter-section').checkVisibility({opacityProperty:true, visibilityProperty:true}))
                    throw new Error('Closing rail exposes filter controls');
                document.querySelector('.sidebar-toggle').click();
                if (controlsVisible()) throw new Error('Rapid reopening exposes cropped filter controls');
                rail.getAnimations({subtree:true}).forEach(a => a.finish());
                return [...during, ...after];
            }
            """);
        Assert.True(widths[0] > collapsedWidth + 1 && widths[0] < widths[4] - 1,
            $"Expected an intermediate rail width, got {widths[0]} between {collapsedWidth} and {widths[4]}.");
        for (var index = 1; index <= 3; index++)
            Assert.InRange(Math.Abs(widths[index] - widths[index + 4]), 0, 1);
        Assert.InRange(Math.Abs(widths[1] - widths[4]), 0, 2);
        Assert.True(widths[3] > 200, "The filter labels must retain their full readable width during expansion.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task Close_UnderThePointerStaysClosedUntilThePointerLeaves()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var rail = page.Locator(".sidebar");
        var collapsedWidth = (await rail.BoundingBoxAsync())!.Width;
        await rail.HoverAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(page.Locator(".sidebar-filter-list")).ToBeVisibleAsync();
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");

        // Close while the pointer remains over the part of the rail that survives
        // collapse; clicking the distant Close button would miss this regression.
        await page.Locator(".sidebar-close").EvaluateAsync("button => button.click()");
        await rail.EvaluateAsync("e => Promise.all(e.getAnimations().map(animation => animation.finished))");
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".collapsed-title")).ToBeVisibleAsync();
        Assert.InRange(Math.Abs((await rail.BoundingBoxAsync())!.Width - collapsedWidth), 0, 1);
        await page.Mouse.MoveAsync(0, 0);
        await rail.HoverAsync(new() { Position = new() { X = 20, Y = 20 } });
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1280, 844)]
    [InlineData(800, 844)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task ReducedMotion_OpensImmediatelyAndKeepsFiltersUsable(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".filter-section").First).ToBeVisibleAsync();
        Assert.True(await page.Locator(".sidebar").EvaluateAsync<bool>(
            "e => e.getAnimations().every(a => a.effect.getTiming().duration <= 1)"));
        var pheno = page.Locator(".sidebar input[name=agingClockView][value=pheno]");
        await pheno.CheckAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task MobileClose_PreservesTheDrawerUntilItsExitAndCanBeReversed(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?filters=women%27s");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator(".sidebar").EvaluateAsync("e => Promise.all(e.getAnimations().map(a => a.finished))");
        Assert.Equal("1", await page.EvaluateAsync<string>("getComputedStyle(document.body, '::before').opacity"));
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const rail = document.querySelector('.sidebar'), issues = [];
                const list = rail.querySelector('.sidebar-filter-list');
                const before = list.getBoundingClientRect().height;
                document.querySelector('.sidebar-close').click();
                if (getComputedStyle(rail.querySelector('.filter-section')).display === 'none') issues.push('Filters vanish');
                if (getComputedStyle(rail.querySelector('.sidebar-heading-actions')).display === 'none') issues.push('Heading actions vanish');
                if (Math.abs(list.getBoundingClientRect().height - before) > 1) issues.push(`List height changes from ${before} to ${list.getBoundingClientRect().height}`);
                if (!rail.inert) issues.push('Closing drawer remains interactive');
                if (!document.body.classList.contains('sidebar-drawer-open')) issues.push('Scroll unlocks before the exit');
                document.querySelector('.sidebar-toggle').click();
                return issues;
            }
            """);
        Assert.Empty(issues);
        await page.Locator(".sidebar").EvaluateAsync("e => Promise.all(e.getAnimations().map(a => a.finished))");
        Assert.True(await page.EvaluateAsync<bool>("document.body.classList.contains('sidebar-drawer-open') && !document.body.classList.contains('sidebar-drawer-closing')"));
        await page.Locator(".sidebar-close").ClickAsync();
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('sidebar-drawer-open')");
        await Assertions.Expect(page.Locator(".sidebar")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task DesktopOpening_DoesNotMoveThePage()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var before = await page.EvaluateAsync<double>("() => { const top = document.querySelector('.sidebar-toggle').getBoundingClientRect().top + scrollY; window.scrollTo({top:Math.max(0, top - 200), behavior:'instant'}); return scrollY; }");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator(".sidebar").EvaluateAsync("e => Promise.all(e.getAnimations().map(a => a.finished))");
        Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("scrollY") - before), 0, 1);
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task AthleteClose_FadesTheCardAndBackdropWithoutMovingThePage(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var before = await page.EvaluateAsync<double>("document.querySelector('.leaderboard').getBoundingClientRect().left");
        await page.Locator(".leaderboard .athlete-name").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content:not(.is-loading):not(.guess-mode)')");
        var opening = await page.EvaluateAsync<double>("document.querySelector('.leaderboard').getBoundingClientRect().left");
        Assert.InRange(Math.Abs(opening - before), 0, 1);
        var closing = await page.EvaluateAsync<bool>("""
            () => {
                closeModal({fromHistory:true});
                const modal = document.getElementById('detailsModal');
                return getComputedStyle(modal.querySelector('.modal-content')).visibility === 'visible'
                    && document.body.style.position === 'fixed';
            }
            """);
        Assert.True(closing, "The card and locked background must remain in place during the fade.");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var after = await page.EvaluateAsync<double>("document.querySelector('.leaderboard').getBoundingClientRect().left");
        Assert.InRange(Math.Abs(after - before), 0, 1);
        Assert.Equal("", await page.EvaluateAsync<string>("document.body.style.position"));
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(1280, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(844, 390, true)]
    public async Task ScrollLocks_PreserveBackgroundWidthAndLeaveOverlaysTheFullViewport(int width, int height, bool drawer)
    {
        // Headless Chromium normally hides native scrollbars. Exercise the real
        // scrollbar layout as well as the default browser used by other cases.
        await using var scrollBrowser = await Browser.BrowserType.LaunchAsync(new()
        {
            Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"]
        });
        await using var context = await NewContextAsync(scrollBrowser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // A custom classic scrollbar also exercises compensation on machines
        // whose default scrollbar normally overlays the page.
        await page.AddStyleTagAsync(new() { Content = "html::-webkit-scrollbar { width:18px; }" });
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var before = await page.EvaluateAsync<double[]>("""
            [document.body.clientWidth, document.querySelector('.leaderboard').getBoundingClientRect().left,
                innerWidth - document.documentElement.clientWidth]
            """);
        Assert.InRange(before[2], 17, 19);

        for (var cycle = 0; cycle < 2; cycle++)
        {
            if (drawer)
            {
                await page.Locator(".sidebar-toggle").ClickAsync();
                await page.WaitForFunctionAsync("document.body.classList.contains('sidebar-drawer-open')");
            }
            else
            {
                await page.Locator(".leaderboard .athlete-name").First.ClickAsync();
                await page.WaitForFunctionAsync("document.querySelector('#detailsModal .modal-content:not(.is-loading):not(.guess-mode)')");
            }

            // Native scrollbar visibility can update after the class mutation.
            await page.WaitForFunctionAsync("document.documentElement.clientWidth === innerWidth");
            var locked = await page.EvaluateAsync<double[]>("""
                drawer => [document.body.clientWidth, document.querySelector('.leaderboard').getBoundingClientRect().left,
                    drawer ? parseFloat(getComputedStyle(document.body, '::before').width)
                        : document.getElementById('detailsModal').getBoundingClientRect().width,
                    innerWidth, document.documentElement.clientWidth, document.documentElement.scrollWidth]
                """, drawer);
            Assert.InRange(Math.Abs(locked[0] - before[0]), 0, 1);
            Assert.InRange(Math.Abs(locked[1] - before[1]), 0, 1);
            Assert.InRange(Math.Abs(locked[2] - locked[3]), 0, 1);
            Assert.InRange(Math.Abs(locked[4] - locked[3]), 0, 1);
            Assert.True(locked[5] <= locked[3] + 1);

            var directory = Environment.GetEnvironmentVariable("LWC_SCROLL_LOCK_CAPTURE_DIR");
            if (cycle == 0 && !string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{width}-{height}-{(drawer ? "drawer" : "athlete")}.png") });
            }

            await page.Locator(drawer ? ".sidebar-close" : "#closeAthleteDetailsModal").ClickAsync();
            await page.WaitForFunctionAsync("!document.body.classList.contains('sidebar-drawer-open') && !document.body.classList.contains('no-scroll')");
            await page.WaitForFunctionAsync("gap => Math.abs(innerWidth - document.documentElement.clientWidth - gap) < 1", before[2]);
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("document.body.clientWidth") - before[0]), 0, 1);
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("document.querySelector('.leaderboard').getBoundingClientRect().left") - before[1]), 0, 1);
        }
    }
}
