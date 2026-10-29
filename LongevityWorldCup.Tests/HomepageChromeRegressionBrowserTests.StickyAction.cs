using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.HomepageChromeRegressionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageStickyActionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task StickyPlayAction_RemainsAWorkingRouteToTheGame()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(305, 844);
        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("window.scrollTo(0, Math.min(700, document.documentElement.scrollHeight - innerHeight))");
        await SettleLayoutAsync(page);

        var stickyAction = page.Locator("#site-sticky-header .join-game.scrolled-button");
        await stickyAction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("PLAY", (await stickyAction.InnerTextAsync()).Trim());

        var fit = await page.EvaluateAsync<double[]>(
            """
            () => {
                const action = document.querySelector('#site-sticky-header .join-game.scrolled-button');
                const actionRect = action.getBoundingClientRect();
                const bar = action.closest('#site-sticky-header').getBoundingClientRect();
                const halo = getComputedStyle(action, '::before');
                return [actionRect.top - bar.top, bar.bottom - actionRect.bottom,
                    actionRect.width, actionRect.height,
                    actionRect.top + parseFloat(halo.top), actionRect.bottom - parseFloat(halo.bottom)];
            }
            """);

        Assert.InRange(fit[0], 0, 200);
        Assert.InRange(fit[1], 0, 200);
        Assert.True(fit[2] > fit[3] && fit[3] >= 44, "PLAY must remain a readable touch target.");
        Assert.InRange(fit[4], 0, 844);
        Assert.InRange(fit[5], 0, 844);
        await StickyPlayScrollBrowserTests.AssertControlsAsync(page);

        await stickyAction.ClickAsync();
        await page.WaitForDomContentLoadedUrlAsync("**/play");
        Assert.EndsWith("/play", new Uri(page.Url).AbsolutePath, StringComparison.Ordinal);
    }

}
