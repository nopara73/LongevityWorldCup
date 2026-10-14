using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class RetiredMortalityPageBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task OldSharedLink_ShowsNotFoundWithoutTrackingOrCalculator(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/mortality-age#sex=1&sbp=115&apob=90");

        Assert.Equal(404, response?.Status);
        await Assertions.Expect(page.Locator(".not-found-main")).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("() => window.LwcDisableGoogleAnalytics === true"));
        Assert.Equal(0, await page.Locator("#mortalityAgeForm").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));
        var screenshots = Path.Combine(FindRepositoryRoot(), ".artifacts", "remove-mortality-page");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"retired-{width}.png"), FullPage = true });
    }
}
