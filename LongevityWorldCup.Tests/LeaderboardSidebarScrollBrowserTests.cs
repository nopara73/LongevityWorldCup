using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardSidebarScrollBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task IncomingClockView_DoesNotInterruptEarlySidebarScrolling()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 1000 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var sidebar = page.Locator(".sidebar");
        var box = (await sidebar.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + 150);
        await Assertions.Expect(page.Locator(".sidebar-title-text")).ToBeVisibleAsync();
        await WaitForLayoutAsync(page);
        var list = page.Locator(".sidebar-filter-list");
        box = (await list.BoundingBoxAsync())!;
        var before = await page.EvaluateAsync<double>("window.scrollY");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + 70);
        await page.WheelAndWaitForInputAsync(0, 100000);
        await page.WaitForFunctionAsync("() => { const e = document.querySelector('.sidebar-filter-list'); return e.scrollTop > 0 && e.scrollHeight - e.clientHeight - e.scrollTop <= 1; }");
        // Cross the delayed initial-positioning window while the filters remain in use.
        await page.EvaluateAsync("() => new Promise(resolve => setTimeout(resolve, 1100))");
        await WaitForLayoutAsync(page);
        Assert.InRange(Math.Abs(before - await page.EvaluateAsync<double>("window.scrollY")), 0, 1);
        Assert.True(await sidebar.EvaluateAsync<bool>("e => e.matches(':hover')"));
        var last = (await page.Locator(".sidebar-filter-list label:visible").Last.BoundingBoxAsync())!;
        Assert.InRange(last.Y + last.Height, 0, 1000);
    }

    [Theory]
    [InlineData(1280, 900, 16)]
    [InlineData(976, 966, 16)]
    [InlineData(1280, 966, 20)]
    public async Task HoverOpenedFilters_ScrollWithinTheViewport(int width, int height, int fontSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync("() => window.scrollBy({ top: document.querySelector('.sidebar').getBoundingClientRect().top - 100, behavior: 'instant' })");
        var sidebar = page.Locator(".sidebar");
        var box = (await sidebar.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + 20, box.Y + 150);
        await WaitForLayoutAsync(page);
        var hoverDiagnostic = await sidebar.EvaluateAsync<string>("e => JSON.stringify({hover:e.matches(':hover'),width:e.getBoundingClientRect().width,top:e.getBoundingClientRect().top,boardWidth:e.parentElement.getBoundingClientRect().width,fontSize:getComputedStyle(document.documentElement).fontSize})");
        Assert.True(await sidebar.EvaluateAsync<bool>("e => e.matches(':hover')"), hoverDiagnostic);
        await Assertions.Expect(page.Locator(".sidebar-title-text")).ToBeVisibleAsync();
        await WaitForLayoutAsync(page);
        Assert.DoesNotContain("expanded", (await sidebar.GetAttributeAsync("class"))!);
        var first = (await page.Locator(".sidebar-filter-list label:visible").First.BoundingBoxAsync())!;
        var before = await page.EvaluateAsync<double>("window.scrollY");
        await page.Mouse.MoveAsync(first.X + first.Width / 2, first.Y + first.Height / 2);
        await page.WheelAndWaitForInputAsync(0, 100000);
        await page.WaitForFunctionAsync("() => { const e = document.querySelector('.sidebar-filter-list'); return e.scrollTop > 0 && e.scrollHeight - e.clientHeight - e.scrollTop <= 1; }");
        await WaitForLayoutAsync(page);
        var last = (await page.Locator(".sidebar-filter-list label:visible").Last.BoundingBoxAsync())!;
        var diagnostic = await sidebar.EvaluateAsync<string>("e => JSON.stringify({scrollTop:e.scrollTop,clientHeight:e.clientHeight,scrollHeight:e.scrollHeight,top:e.getBoundingClientRect().top,bottom:e.getBoundingClientRect().bottom,listDisplay:getComputedStyle(e.querySelector('.sidebar-filter-list')).display})");
        Assert.InRange(Math.Abs(before - await page.EvaluateAsync<double>("window.scrollY")), 0, 1);
        Assert.True(last.Y >= 0 && last.Y + last.Height <= height, $"Last option at {last.Y}..{last.Y + last.Height}, viewport {height}: {diagnostic}");
        var list = page.Locator(".sidebar-filter-list");
        Assert.True(await list.EvaluateAsync<bool>("e => e.scrollTop > 0 && e.scrollHeight - e.clientHeight - e.scrollTop <= 1"), diagnostic);
        Assert.Equal(0, await sidebar.EvaluateAsync<double>("e => e.scrollTop"));
        var heading = (await page.Locator(".sidebar-heading").BoundingBoxAsync())!;
        Assert.True(heading.Y >= 0 && heading.Y + heading.Height <= height);
        var output = Environment.GetEnvironmentVariable("LWC_SIDEBAR_SCREENSHOTS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"sidebar-hover-bottom-{width}-{height}-{fontSize}.png") });
        }
    }

    [Theory]
    [InlineData(1280, 900, 16, false)]
    [InlineData(976, 966, 20, true)]
    [InlineData(800, 900, 20, true)]
    [InlineData(390, 844, 16, true)]
    [InlineData(844, 390, 16, false)]
    public async Task WheelOverFilters_ReachesTheLastOptionWithoutScrollingThePage(int width, int height, int fontSize, bool byFlag)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(byFlag ? "/leaderboard?group=flags" : "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("expanded"));
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.EvaluateAsync("() => { if (!document.body.classList.contains('sidebar-drawer-open')) window.scrollBy({ top: document.querySelector('.sidebar').getBoundingClientRect().top - 100, behavior: 'instant' }); }");
        await WaitForLayoutAsync(page);

        var list = page.Locator(".sidebar-filter-list");
        var box = (await list.BoundingBoxAsync())!;
        var pageScroll = await page.EvaluateAsync<double>("window.scrollY");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, Math.Min(box.Y + 80, height - 24));
        await page.WheelAndWaitForInputAsync(0, 100000);
        await page.WaitForFunctionAsync("() => { const e = document.querySelector('.sidebar-filter-list'); return e.scrollTop > 0 && e.scrollHeight - e.clientHeight - e.scrollTop <= 1; }");
        await WaitForLayoutAsync(page);
        var diagnostic = await page.EvaluateAsync<string>(
            """
            () => {
                const list = document.querySelector('.sidebar-filter-list');
                const sidebar = document.querySelector('.sidebar');
                const box = list.getBoundingClientRect();
                return JSON.stringify({ scrollTop: list.scrollTop, scrollHeight: list.scrollHeight, clientHeight: list.clientHeight,
                    listTop: box.top, listBottom: box.bottom, sidebarHeight: sidebar.getBoundingClientRect().height,
                    sidebarOverflow: getComputedStyle(sidebar).overflowY, listOverflow: getComputedStyle(list).overflowY, pageScroll: scrollY });
            }
            """);
        Assert.True(await list.EvaluateAsync<bool>("e => e.scrollTop > 0 && e.scrollHeight - e.clientHeight - e.scrollTop <= 1"), diagnostic);
        Assert.InRange(Math.Abs(pageScroll - await page.EvaluateAsync<double>("window.scrollY")), 0, 1);
        var last = (await page.Locator(".sidebar-filter-list label:visible").Last.BoundingBoxAsync())!;
        var viewport = (await list.BoundingBoxAsync())!;
        Assert.InRange(last.Y, viewport.Y - 1, height - last.Height + 1);
        Assert.True(last.Y + last.Height <= viewport.Y + viewport.Height + 1, diagnostic);

        var output = Environment.GetEnvironmentVariable("LWC_SIDEBAR_SCREENSHOTS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"sidebar-bottom-{width}-{height}-{fontSize}-{byFlag}.png") });
        }
    }

    private static Task WaitForLayoutAsync(IPage page) => page.WaitForFunctionAsync(
        """
        () => new Promise(resolve => {
            let previous = '', stableFrames = 0;
            const check = () => {
                const rect = document.querySelector('.sidebar').getBoundingClientRect();
                const current = JSON.stringify([scrollY, rect.x, rect.y, rect.width, rect.height]);
                stableFrames = current === previous ? stableFrames + 1 : 0;
                previous = current;
                if (stableFrames >= 4) resolve(true);
                else requestAnimationFrame(check);
            };
            requestAnimationFrame(check);
        })
        """);
}
