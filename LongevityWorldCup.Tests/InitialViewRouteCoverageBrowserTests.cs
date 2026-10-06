using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InitialViewRouteCoverageBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    // The document catalog covers every current page template, including
    // generated documents, private tools, redirects, embeds, and error pages.
    // Mode-specific first-paint checks live in InitialViewBrowserTests and the
    // profile, Challenge and calculator browser suites.
    public static IEnumerable<object[]> Documents =>
        from path in new[] {
            "/", "/contribute", "/leaderboard", "/events", "/longevitymaxxing", "/helstab-kihivas",
            "/media", "/about", "/history", "/rejuvenation-olympics", "/ruleset", "/privacy",
            "/play", "/join", "/apply", "/review", "/proofs", "/select-athlete", "/dashboard", "/edit-profile",
            "/unsubscribe", "/pheno-age", "/bortz-age",
            "/league/bortz", "/league/pheno", "/league/improvement", "/league/bortz-improvement", "/league/crowd",
            "/league/amateur", "/league/womens", "/league/gen-x", "/flag/hungary", "/athlete/michael-lustgarten",
            "/event-board-embed.html?embed=1&athlete=michael_lustgarten&theme=dark&rows=all&linkNames=0",
            "/internal/site-statistics.html", "/internal/custom-event-designer.html",
            "/missing-public-page", "/error/502.html", "/error/503.html", "/error/504.html", "/swagger/index.html"
        }
        from width in new[] { 390, 1280 }
        select new object[] { path, width };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryDocument_ResolvesItsViewWithoutStrandingLoadingOrThrowing(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Michael Lustgarten', DisplayName: 'Michael Lustgarten',
                AccountEmail: 'firstpaint@example.test', ProfilePic: '/assets/content-images/play-athlete-placeholder.jpg',
                Division: "Men's", Flag: 'United States', Why: 'More healthy years.',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
            }));
            sessionStorage.setItem('contactEmail', 'firstpaint@example.test');
            """);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        await Assertions.Expect(page.Locator(".initial-view-recovery")).ToHaveCountAsync(0);
        if (path == "/longevitymaxxing")
            await Assertions.Expect(page.Locator("main")).ToHaveAttributeAsync("aria-busy", "false");
        if (path == "/dashboard")
            await Assertions.Expect(page.Locator("#athleteDashboardTitle")).ToHaveTextAsync("Michael Lustgarten");
        if (path == "/select-athlete")
            await Assertions.Expect(page.Locator("#athleteSelectionPanel")).ToBeVisibleAsync();
        if (path.StartsWith("/athlete"))
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.Empty(errors);
    }
}
