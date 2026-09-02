using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class HomepageChromeRegressionBrowserTests
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

        var stickyAction = page.Locator("header[role=\"banner\"] .join-game.scrolled-button");
        await stickyAction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("PLAY", (await stickyAction.InnerTextAsync()).Trim());

        var fit = await page.EvaluateAsync<StickyActionFitDiagnostics>(
            """
            () => {
                const action = document.querySelector('header[role="banner"] .join-game.scrolled-button');
                const stickyHeader = document.getElementById('site-sticky-header');
                const actionRect = action.getBoundingClientRect();
                const stickyRect = stickyHeader.getBoundingClientRect();
                const halo = getComputedStyle(action, '::before');
                const haloTop = actionRect.top + parseFloat(halo.top);
                const haloBottom = actionRect.bottom - parseFloat(halo.bottom);
                return {
                    ActionWidth: actionRect.width,
                    ActionHeight: actionRect.height,
                    StickyTop: stickyRect.top,
                    StickyBottom: stickyRect.bottom,
                    HaloTop: haloTop,
                    HaloBottom: haloBottom,
                    CenterOffset: Math.abs(
                        (haloTop + haloBottom) / 2
                        - (stickyRect.top + stickyRect.bottom) / 2)
                };
            }
            """);

        Assert.True(fit.HaloTop >= fit.StickyTop - 0.5,
            $"Compact Play halo escaped above the sticky header: {fit.HaloTop:F1}px < {fit.StickyTop:F1}px.");
        Assert.True(fit.HaloBottom <= fit.StickyBottom + 0.5,
            $"Compact Play halo escaped below the sticky header: {fit.HaloBottom:F1}px > {fit.StickyBottom:F1}px.");
        Assert.InRange(fit.CenterOffset, 0, 1);
        Assert.True(fit.ActionWidth > fit.ActionHeight,
            $"Compact Play action became too square: {fit.ActionWidth:F1}x{fit.ActionHeight:F1}px.");

        await stickyAction.ClickAsync();
        await page.WaitForDomContentLoadedUrlAsync("**/play");
        Assert.EndsWith("/play", new Uri(page.Url).AbsolutePath, StringComparison.Ordinal);
    }

}
