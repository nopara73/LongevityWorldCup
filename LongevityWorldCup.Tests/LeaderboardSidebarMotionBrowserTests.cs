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
    [InlineData("/leaderboard?search=Valerie", false)]
    public async Task DesktopDeck_CloseKeepsItsContentsUntilExitAndCanBeReversed(string path, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await WaitForFiniteAnimationsAsync(page);
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const rail = document.querySelector('.sidebar'), panel = rail.querySelector('.sidebar-panel');
                const issues = [], height = rail.getBoundingClientRect().height;
                document.querySelector('.sidebar-close').click();
                if (!panel.inert) issues.push('Closing deck remains interactive');
                if (getComputedStyle(panel).visibility !== 'visible') issues.push('Deck contents disappear before exit');
                if (Math.abs(rail.getBoundingClientRect().height - height) > 1) issues.push('Deck collapses before exit');
                const closing = panel.getAnimations().filter(a => a.playState === 'running');
                if (!closing.length) issues.push('Closing deck has no exit');
                closing.forEach(a => {a.pause(); a.currentTime = a.effect.getTiming().duration / 2;});
                document.querySelector('.sidebar-toggle').click();
                if (panel.inert || rail.classList.contains('is-closing')) issues.push('Reopening leaves stale close state');
                return issues;
            }
            """);
        Assert.Empty(issues);
        await WaitForFiniteAnimationsAsync(page);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-panel")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        await page.Locator(".sidebar-rail").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".sidebar-close")).ToBeFocusedAsync();
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
            "e => e.getAnimations({subtree:true}).every(a => a.effect.getTiming().duration <= 1)"));
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
                await page.WaitForFunctionAsync("() => document.body.classList.contains('sidebar-drawer-open')");
            }
            else
            {
                await page.Locator(".leaderboard .athlete-name").First.ClickAsync();
                await page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content:not(.is-loading):not(.guess-mode)')");
            }

            // Native scrollbar visibility can update after the class mutation.
            await page.WaitForFunctionAsync("() => document.documentElement.clientWidth === innerWidth");
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
            await page.WaitForFunctionAsync("() => !document.body.classList.contains('sidebar-drawer-open') && !document.body.classList.contains('no-scroll')");
            await page.WaitForFunctionAsync("gap => Math.abs(innerWidth - document.documentElement.clientWidth - gap) < 1", before[2]);
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("document.body.clientWidth") - before[0]), 0, 1);
            Assert.InRange(Math.Abs(await page.EvaluateAsync<double>("document.querySelector('.leaderboard').getBoundingClientRect().left") - before[1]), 0, 1);
        }
    }
}
