using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class PageWeightBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Navigation_ReusesSharedAssetsWithoutChangingPageOrDialogBehavior(int width)
    {
        // Routing disables Chromium's HTTP cache, so this test deliberately uses
        // an ordinary browser context against the local production-equivalent app.
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.Load });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        var assets = new[]
        {
            "/js/site-header.js", "/js/site-footer.js", "/js/leaderboard-page.js",
            "/js/leaderboard-embed-listener.js", "/js/guess-my-age.js",
            "/css/site-header.css", "/css/site-footer.css", "/css/leaderboard-content.css",
            "/css/guess-my-age.css", "/css/age-visualization.css"
        };
        Assert.Equal(assets.Length, await CountAssetsAsync(page, assets, cached: false));

        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.Load });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        Assert.Equal(assets.Length, await CountAssetsAsync(page, assets, cached: true));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        await page.EvaluateAsync("() => window.openAthleteModalBySlug('michael-lustgarten')");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.Locator("#detailsModal").GetByRole(AriaRole.Button, new() { Name = "Skip", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToContainTextAsync("Michael Lustgarten");
        Assert.Empty(errors);
    }

    private static Task<int> CountAssetsAsync(IPage page, string[] paths, bool cached)
        => page.EvaluateAsync<int>("""
            ({ paths, cached }) => performance.getEntriesByType('resource').filter(entry =>
                paths.includes(new URL(entry.name).pathname) && entry.decodedBodySize > 0 &&
                (cached ? entry.transferSize === 0 : entry.transferSize > 0)).length
            """, new { paths, cached });
}
