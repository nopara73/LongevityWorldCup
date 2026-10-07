using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FooterResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 1100, 100)]
    [InlineData("/", 1100, 125)]
    [InlineData("/", 1280, 150)]
    [InlineData("/", 1440, 200)]
    [InlineData("/history", 1100, 125)]
    [InlineData("/events", 1100, 125)]
    public async Task SharedFooter_ContainsEveryLinkWhenDesktopTextIsEnlarged(string path, int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 900);
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var links = page.Locator(".footer .footer-link");
        Assert.Equal(15, await links.CountAsync());
        var problems = await links.EvaluateAllAsync<string[]>(
            """
            links => links.flatMap(link => {
                const rect = link.getBoundingClientRect();
                const name = link.textContent.trim();
                return [
                    rect.left < -1 || rect.right > innerWidth + 1 ? `${name}: clipped` : null,
                    rect.width < 44 || rect.height < 44 ? `${name}: small target` : null
                ].filter(Boolean);
            })
            """);
        Assert.True(problems.Length == 0, $"{path} at {width}px / {textPercent}%: {string.Join(", ", problems)}");
    }
}
