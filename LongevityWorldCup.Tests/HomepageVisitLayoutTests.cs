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
        await context.AddInitScriptAsync("""
            localStorage.setItem('lwcHomepageVisitCount:v1', '3');
            const firstSection = new MutationObserver(() => {
                const section = [...document.querySelectorAll('main > :is(.main-container, .lwc-highlights-countdown-wrapper)')]
                    .find(candidate => candidate.getClientRects().length);
                if (!section) return;
                window.highlightsOpenedFirst = section.matches('.lwc-highlights-countdown-wrapper');
                window.storeOpenedWithHighlights = !!section.querySelector('#lwc-countdown-box .lwc-merch-mobile-slide');
                firstSection.disconnect();
            });
            firstSection.observe(document, { childList: true, subtree: true });
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");

        Assert.Equal(expectedVisits == 4, await page.EvaluateAsync<bool>("window.highlightsOpenedFirst"));
        Assert.Equal(expectedVisits == 4, await page.EvaluateAsync<bool>("window.storeOpenedWithHighlights"));
        Assert.Equal(expectedVisits, await page.EvaluateAsync<int>(
            "Number(localStorage.getItem('lwcHomepageVisitCount:v1'))"));
        await Assertions.Expect(page.Locator("main")).ToHaveAttributeAsync(
            "data-homepage-visit-layout", expectedVisits == 4 ? "repeat" : "default");

        var band = await page.Locator(".game-description").BoundingBoxAsync();
        var standings = await page.Locator(".podium:visible, .leaderboard-controls").First.BoundingBoxAsync();
        Assert.NotNull(band);
        Assert.NotNull(standings);
        Assert.InRange(Math.Abs(band.Y + band.Height - standings.Y), 0, 1);
    }
}
