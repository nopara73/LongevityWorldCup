using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardSidebarScrollBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/?group=flags")]
    [InlineData("/leaderboard?search=Valerie")]
    [InlineData("/leaderboard?search=NoMatchingAthleteForSidebarHeight")]
    [InlineData("/leaderboard?group=flags&search=Valerie")]
    [InlineData("/league/silent-generation")]
    [InlineData("/flag/united-states")]
    public async Task Deck_ReservesOnlyItsVisibleHeightAndDoesNotChangeTheRankings(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        var before = await page.Locator(".leaderboard").BoundingBoxAsync();
        var ranks = await page.Locator(".leaderboard > table tbody tr:visible, .flag-team > summary").AllTextContentsAsync();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await page.Locator(".sidebar-rail").HoverAsync(new() { Position = new() { X = 20, Y = 20 } });
            await Assertions.Expect(page.Locator(".sidebar-panel")).ToBeHiddenAsync();
            Assert.True(await page.Locator(".sidebar-panel").EvaluateAsync<bool>("e => e.inert"));
            await page.Locator(".sidebar-toggle").ClickAsync();
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            Assert.True(await page.EvaluateAsync<bool>("""
                () => {
                    const board = document.querySelector('.leaderboard');
                    const panel = board.querySelector('.sidebar-panel');
                    const results = board.classList.contains('leaderboard--flags') ? board.querySelector('.flag-leaderboard') : board.querySelector('table');
                    const p = panel.getBoundingClientRect(), r = results.getBoundingClientRect();
                    return r.top >= p.bottom - 1 && Math.abs(r.left - p.left) <= 2
                        && Math.abs(r.right - p.right) <= 2 && panel.scrollWidth <= panel.clientWidth + 1;
                }
                """));
            Assert.Equal(ranks, await page.Locator(".leaderboard > table tbody tr:visible, .flag-team > summary").AllTextContentsAsync());
            await page.Locator(".sidebar-close").ClickAsync();
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            Assert.InRange(Math.Abs((await page.Locator(".leaderboard").BoundingBoxAsync())!.Height - before!.Height), 0, 2);
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopDeck_LeavesTheFirstAthleteVisibleAndPreservesTheScrolledResultOnClose(bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var first = page.Locator(".leaderboard tbody .athlete-name").First;
        Assert.True(await first.EvaluateAsync<bool>("e => { const r=e.getBoundingClientRect(); return r.top >= 52 && r.bottom <= innerHeight && e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2)); }"));
        var row = page.Locator(".leaderboard tbody tr[data-athlete-name]").Nth(10);
        await row.EvaluateAsync("e => scrollTo({top:scrollY+e.getBoundingClientRect().top-150,behavior:'instant'})");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var top = (await row.BoundingBoxAsync())!.Y;
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        Assert.InRange(Math.Abs((await row.BoundingBoxAsync())!.Y - top), 0, 2);
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToBeFocusedAsync();
        await page.Locator(".sidebar-rail").EvaluateAsync("e => e.click()");
        var close = (await page.Locator(".sidebar-close").BoundingBoxAsync())!;
        Assert.InRange(close.Y, 52, 900 - close.Height);
    }

    [Theory]
    [InlineData(1440, 900, 16)]
    [InlineData(800, 900, 20)]
    [InlineData(390, 844, 16)]
    [InlineData(844, 390, 16)]
    public async Task LongFilters_KeepTheLastOptionReachableWithoutMovingThePage(int width, int height, int fontSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
        await page.Locator(".sidebar-toggle").ClickAsync();
        var mobile = width <= 768 || width <= 932 && height <= 480;
        var selector = mobile ? ".sidebar-filter-list" : "#flag-filter-section ul";
        var list = page.Locator(selector);
        await list.ScrollIntoViewIfNeededAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var before = await page.EvaluateAsync<double>("scrollY");
        var box = (await list.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + Math.Min(70, box.Height / 2));
        await page.WheelAndWaitForInputAsync(0, 100000);
        await page.WaitForFunctionAsync("selector => {const e=document.querySelector(selector);return e.scrollTop>0 && e.scrollHeight-e.clientHeight-e.scrollTop<=1;}", selector);
        Assert.InRange(Math.Abs(before - await page.EvaluateAsync<double>("scrollY")), 0, 1);
        var last = list.Locator("label:visible").Last;
        Assert.True(await last.EvaluateAsync<bool>("e => {const r=e.getBoundingClientRect();return r.top>=0 && r.bottom<=innerHeight && e.contains(document.elementFromPoint(r.x+r.width/2,r.y+r.height/2));}"));
        Assert.True(await list.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"));
    }
}
