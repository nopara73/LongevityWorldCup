using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class HomepageVisitLayoutTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 4)]
    [InlineData("/index.html", 4)]
    [InlineData("/?search=michael", 3)]
    [InlineData("/?view=pheno", 3)]
    [InlineData("/?athlete=michael-lustgarten", 3)]
    public async Task HomepageVisitCounter_IsGuardedToHomepageRoutes(string path, int expectedVisits)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('lwcHomepageVisitCount:v1', '3');");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");

        Assert.Equal(expectedVisits, await page.EvaluateAsync<int>(
            "Number(localStorage.getItem('lwcHomepageVisitCount:v1'))"));
        await Assertions.Expect(page.Locator("main")).ToHaveAttributeAsync(
            "data-homepage-visit-layout", expectedVisits == 4 ? "repeat" : "default");
    }
}
