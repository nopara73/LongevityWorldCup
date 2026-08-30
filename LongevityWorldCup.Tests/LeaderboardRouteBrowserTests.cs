using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LeaderboardRouteBrowserTests
{
    [Fact]
    public async Task FlagLeaderboard_ActionAndDirectRoutes_ShowFullCountryLeaderboardsWithCountryTitle()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        var pageErrors = new List<string>();
        page.PageError += (_, error) => pageErrors.Add(error);

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForLeaderboardAsync(page);

        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator("#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]").CheckAsync();
        await page.WaitForFunctionAsync(
            """
            () => location.pathname === '/flag/hungary'
                && document.getElementById('viewAllAthletesBtn')?.textContent?.includes('VIEW THIS LEADERBOARD')
            """);

        var expectedCount = await GetActionCountAsync(page);
        Assert.True(expectedCount > 10, $"The Hungary fixture must exercise the former top-10 truncation; found {expectedCount} athletes.");

        var documentResponse = page.WaitForResponseAsync(response =>
            response.Request.ResourceType == "document" &&
            new Uri(response.Url).AbsolutePath.Equals("/flag/hungary", StringComparison.OrdinalIgnoreCase));
        pageErrors.Clear();
        await page.Locator("#viewAllAthletesBtn").ClickAsync();
        var response = await documentResponse;
        Assert.True(response.Ok);

        await page.WaitForTimeoutAsync(1500);
        var statusAfterNavigation = await page.Locator("#leaderboardStatus").TextContentAsync();
        Assert.True(
            statusAfterNavigation == "Leaderboard loaded.",
            $"Flag leaderboard did not finish loading at {page.Url}. Status: {statusAfterNavigation}. Page errors: {string.Join(" | ", pageErrors)}");

        await AssertFullLeaderboardAsync(page, "/flag/hungary", "#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]:checked");
        Assert.Equal(expectedCount, await CountVisibleAthleteRowsAsync(page));

        var directPage = await context.NewPageAsync();
        await directPage.GotoAsync("/flag/hungary", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(directPage, "/flag/hungary", "#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]:checked");
        Assert.Equal(expectedCount, await CountVisibleAthleteRowsAsync(directPage));

        await directPage.Locator("#clearSidebarFiltersBtn").EvaluateAsync("button => button.click()");
        await directPage.WaitForFunctionAsync("() => location.pathname === '/leaderboard'");
        Assert.NotNull(await directPage.QuerySelectorAsync("[data-leaderboard-page=\"full\"]"));

        await directPage.GotoAsync("/flag/czech-republic", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(
            directPage,
            "/flag/czech-republic",
            "#flag-filter-section input[name=\"flag\"][value=\"Czech Republic\"]:checked");
        Assert.Equal("CZECH REPUBLIC", (await directPage.Locator(".collapsed-title").TextContentAsync())?.Trim());
    }

    [Theory]
    [InlineData("/league/amateur", "#league-track-filter-section input[value=\"Amateur\"]:checked")]
    [InlineData("/league/bortz", "#view-bortz:checked")]
    public async Task LeagueRoute_RendersFullLeaderboardAndPreservesCanonicalPath(string path, string activeStateSelector)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await AssertFullLeaderboardAsync(page, path, activeStateSelector);
    }

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser, BrowserTestApp app)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/bitcoin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var body = path.EndsWith("/btcusd", StringComparison.OrdinalIgnoreCase)
                ? """{"btcToUsdRate":0}"""
                : path.EndsWith("/donation-address", StringComparison.OrdinalIgnoreCase)
                    ? """{"address":""}"""
                    : """{"totalReceivedSatoshis":0}""";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });
        return context;
    }

    private static async Task WaitForLeaderboardAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
    }

    private static async Task AssertFullLeaderboardAsync(IPage page, string expectedPath, string activeStateSelector)
    {
        await WaitForLeaderboardAsync(page);
        await page.WaitForTimeoutAsync(700);

        Assert.Equal(expectedPath, new Uri(page.Url).AbsolutePath);
        Assert.NotNull(await page.QuerySelectorAsync("[data-leaderboard-page=\"full\"]"));
        Assert.Equal(1, await page.Locator(activeStateSelector).CountAsync());
        Assert.Null(await page.QuerySelectorAsync("#viewAllAthletesBtn"));
    }

    private static async Task<int> GetActionCountAsync(IPage page)
    {
        return await page.Locator("#viewAllAthletesBtn").EvaluateAsync<int>(
            """
            button => {
                const match = button.textContent.match(/\((\d+)\)/);
                return match ? Number(match[1]) : 0;
            }
            """);
    }

    private static Task<int> CountVisibleAthleteRowsAsync(IPage page)
    {
        return page.Locator(".leaderboard tbody tr[data-athlete-name]:visible").CountAsync();
    }
}
