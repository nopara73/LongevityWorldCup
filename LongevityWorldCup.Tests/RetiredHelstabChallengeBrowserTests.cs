using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class RetiredHelstabChallengeBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task OldChallengeLink_ShowsNotFoundWithWorkingHomeAction(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/helstab-kihivas");

        Assert.Equal(404, response?.Status);
        await Assertions.Expect(page.Locator(".not-found-main")).ToBeVisibleAsync();
        Assert.Equal(0, await page.Locator("#helstabSignupForm").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth + 1"));

        await page.Locator(".not-found-visual img").EvaluateAsync("image => image.decode()");
        await page.EvaluateAsync("() => document.fonts.ready");
        var screenshots = Path.Combine(FindRepositoryRoot(), ".artifacts", "retired-helstab-challenge");
        Directory.CreateDirectory(screenshots);
        await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"retired-{width}.png"), FullPage = true });

        await page.Locator(".not-found-actions .option-button").ClickAsync();
        await page.WaitForURLAsync(App.BaseAddress.ToString());
        await Assertions.Expect(page.Locator(".not-found-main")).ToHaveCountAsync(0);
    }
}
