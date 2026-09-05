using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardRouteBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("100%", false, 1)]
    [InlineData("%E0%A4%A", false, 0)]
    [InlineData("literal %20", true, 0)]
    [InlineData("michael lustgarten", true, 1)]
    public async Task SearchLinks_LoadLiteralPercentsAndPreserveSharedUrlRoundTrips(
        string query, bool legacyEncoding, int expectedRows)
    {
        await using var context = await NewContextAsync(Browser, App);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var athlete = athletes.OfType<JsonObject>()
                .Single(item => item["Name"]?.GetValue<string>() == "Michael Lustgarten");
            athlete["Why"] = "100% committed";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = new JsonArray(athlete.DeepClone()).ToJsonString()
            });
        });
        var page = await context.NewPageAsync();
        var encoded = Uri.EscapeDataString(legacyEncoding ? Uri.EscapeDataString(query) : query);
        await page.GotoAsync("/leaderboard?search=" + encoded);
        await WaitForLeaderboardAsync(page);

        Assert.Equal(query, await page.Locator("#athleteSearch").InputValueAsync());
        await Assertions.Expect(page.Locator(".leaderboard tbody tr[data-athlete-name]:visible"))
            .ToHaveCountAsync(expectedRows);

        // Filtering writes the shareable URL. Loading it again must keep its meaning.
        await page.ReloadAsync();
        await WaitForLeaderboardAsync(page);
        Assert.Equal(query.ToLowerInvariant(), await page.Locator("#athleteSearch").InputValueAsync());
        await Assertions.Expect(page.Locator(".leaderboard tbody tr[data-athlete-name]:visible"))
            .ToHaveCountAsync(expectedRows);
    }

    [Fact]
    public async Task FlagLeaderboard_ActionAndDirectRoutes_ShowFullLeaderboardsWithBoundedAccurateTitles()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForLeaderboardAsync(page);
        var homepageTitle = await page.TitleAsync();

        await page.Locator(".sidebar-toggle").ClickAsync();
        await page.Locator("#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]").CheckAsync();
        await page.WaitForFunctionAsync(
            """
            () => location.pathname === '/flag/hungary'
                && document.getElementById('viewAllAthletesBtn')?.textContent?.includes('VIEW THIS LEADERBOARD')
            """);
        Assert.Equal(homepageTitle, await page.TitleAsync());

        var expectedCount = await GetActionCountAsync(page);
        Assert.True(expectedCount > 10, $"The Hungary fixture must exercise the former top-10 truncation; found {expectedCount} athletes.");

        var documentResponse = page.WaitForResponseAsync(response =>
            response.Request.ResourceType == "document" &&
            new Uri(response.Url).AbsolutePath.Equals("/flag/hungary", StringComparison.OrdinalIgnoreCase));
        await page.Locator("#viewAllAthletesBtn").ClickAsync();
        var response = await documentResponse;
        Assert.True(response.Ok);

        await AssertFullLeaderboardAsync(page, "/flag/hungary", "#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]:checked", expectedCount);
        Assert.Equal(expectedCount, await CountVisibleAthleteRowsAsync(page));
        Assert.Equal("Leaderboard: Hungary | Longevity World Cup", await page.TitleAsync());

        var directPage = await context.NewPageAsync();
        await directPage.GotoAsync("/flag/hungary", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(directPage, "/flag/hungary", "#flag-filter-section input[name=\"flag\"][value=\"Hungary\"]:checked", expectedCount);
        Assert.Equal(expectedCount, await CountVisibleAthleteRowsAsync(directPage));
        Assert.Equal("Leaderboard: Hungary | Longevity World Cup", await directPage.TitleAsync());

        await directPage.Locator("#clearSidebarFiltersBtn").EvaluateAsync("button => button.click()");
        await directPage.WaitForFunctionAsync("() => location.pathname === '/leaderboard'");
        Assert.NotNull(await directPage.QuerySelectorAsync("[data-leaderboard-page=\"full\"]"));

        await directPage.GotoAsync("/flag/czech-republic", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(
            directPage,
            "/flag/czech-republic",
            "#flag-filter-section input[name=\"flag\"][value=\"Czech Republic\"]:checked");
        await directPage.WaitForFunctionAsync(
            "() => document.querySelector('.collapsed-title')?.textContent?.trim() === 'CZECH REPUBLIC'");
        Assert.Equal("CZECH REPUBLIC", (await directPage.Locator(".collapsed-title").TextContentAsync())?.Trim());
        Assert.Equal("Leaderboard: Czech Republic", await directPage.Locator(".collapsed-title").GetAttributeAsync("aria-label"));
        Assert.Equal("Leaderboard: Czech Republic | Longevity World Cup", await directPage.TitleAsync());

        await directPage.GotoAsync("/flag/live-long-enough-to-live-forever", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(
            directPage,
            "/flag/live-long-enough-to-live-forever",
            "#flag-filter-section input[name=\"flag\"][value=\"Live long enough to live forever\"]:checked");
        await directPage.WaitForFunctionAsync(
            """
            () => {
                const title = document.querySelector('.collapsed-title');
                return title?.textContent?.trim() === 'FLAG'
                    && title.getAttribute('title') === 'Leaderboard: Live long enough to live forever';
            }
            """);

        var collapsedTitle = directPage.Locator(".collapsed-title");
        Assert.Equal("FLAG", (await collapsedTitle.TextContentAsync())?.Trim());
        Assert.Equal("Leaderboard: Live long enough to live forever", await collapsedTitle.GetAttributeAsync("title"));
        Assert.Equal("Leaderboard: Live long enough to live forever", await collapsedTitle.GetAttributeAsync("aria-label"));
        Assert.Equal("Leaderboard: Live long enough to live forever | Longevity World Cup", await directPage.TitleAsync());

        var geometry = await directPage.EvaluateAsync<JsonElement>(
            """
            () => {
                const sidebar = document.querySelector('.leaderboard > .sidebar').getBoundingClientRect();
                const table = document.querySelector('.leaderboard > table').getBoundingClientRect();
                const title = document.querySelector('.collapsed-title');
                return {
                    sidebarHeight: sidebar.height,
                    tableHeight: table.height,
                    titleOverflow: title.scrollHeight - title.clientHeight
                };
            }
            """);
        Assert.InRange(Math.Abs(geometry.GetProperty("sidebarHeight").GetDouble() - geometry.GetProperty("tableHeight").GetDouble()), 0, 1.5);
        Assert.InRange(geometry.GetProperty("titleOverflow").GetDouble(), -1, 1.5);
    }

    [Fact]
    public async Task LeaderboardPresentation_KeepsCopyRoutesAndModalTitleAlignedWithFilterState()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await AssertFullLeaderboardAsync(page, "/leaderboard", "#view-ultimate:checked");

        Assert.Equal("Ultimate League | Longevity World Cup", await page.TitleAsync());
        Assert.Equal(
            "Ultimate League ranks Pro athletes before Amateur athletes, then by effective age reduction and tie-breakers within each track.",
            (await page.Locator("#rankingExplanation").InnerTextAsync()).Trim());
        Assert.Equal("Show league filters", await page.Locator(".sidebar-toggle").GetAttributeAsync("aria-label"));

        await page.Locator(".sidebar-toggle").ClickAsync();
        Assert.Equal("Hide league filters", await page.Locator(".sidebar-toggle").GetAttributeAsync("aria-label"));
        Assert.Equal("Hide league filters", await page.Locator(".sidebar-close").GetAttributeAsync("aria-label"));
        Assert.Equal("Ranking views", (await page.Locator("#aging-clock-filter-section h3").InnerTextAsync()).Trim());
        Assert.Equal("Exclusive leagues", (await page.Locator("#exclusive-filter-section h3").InnerTextAsync()).Trim());

        var improvementView = page.Locator("input[name=\"agingClockView\"][value=\"improvement\"]");
        await improvementView.CheckAsync();
        await page.WaitForFunctionAsync("() => location.pathname === '/league/improvement' && document.title === 'Pheno Improvement Leaderboard | Longevity World Cup'");
        Assert.Equal("PHENO IMPROVEMENT LEAGUE", (await page.Locator(".collapsed-title").GetAttributeAsync("data-full-rail-text"))?.Trim());
        Assert.Equal(
            "Pheno improvement ranks each athlete’s latest pheno age against their worst pheno age.",
            (await page.Locator("#rankingExplanation").InnerTextAsync()).Trim());

        var crowdView = page.Locator("input[name=\"agingClockView\"][value=\"crowd\"]");
        await crowdView.EvaluateAsync(
            "input => { input.disabled = false; input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForFunctionAsync("() => location.pathname === '/league/crowd'");
        Assert.Equal(
            "Crowd age is a visual age estimate from visitors. Athletes qualify once they reach 100 accepted guesses.",
            (await page.Locator("#rankingExplanation").InnerTextAsync()).Trim());
        await crowdView.EvaluateAsync(
            "input => { input.disabled = false; input.checked = false; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForFunctionAsync("() => location.pathname === '/leaderboard'");

        await page.EvaluateAsync(
            """
            () => {
                const inputs = ['Silent Generation', 'Gen X']
                    .map(value => document.querySelector(`input[name="generation"][value="${value}"]`));
                inputs.forEach(input => { input.disabled = false; input.checked = true; });
                inputs.at(-1).dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await page.WaitForFunctionAsync("() => document.title === 'Multi-generation League | Longevity World Cup'");
        Assert.Equal("MULTI-GENERATION LEAGUE", await page.Locator(".collapsed-title").GetAttributeAsync("data-full-rail-text"));

        await page.Locator("#clearSidebarFiltersBtn").EvaluateAsync("button => button.click()");
        await page.WaitForFunctionAsync("() => location.pathname === '/leaderboard' && document.title === 'Ultimate League | Longevity World Cup'");
        await page.EvaluateAsync(
            """
            () => {
                const inputs = ["Men's", "Women's", 'Open']
                    .map(value => document.querySelector(`input[name="division"][value="${value}"]`));
                inputs.forEach(input => { input.disabled = false; input.checked = true; });
                inputs.at(-1).dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
        await page.WaitForFunctionAsync("() => document.title === 'All Divisions League | Longevity World Cup'");
        Assert.Equal("ALL DIVISIONS LEAGUE", await page.Locator(".collapsed-title").GetAttributeAsync("data-full-rail-text"));

        await page.Locator("#clearSidebarFiltersBtn").EvaluateAsync("button => button.click()");
        var proTrack = page.Locator("input[name=\"leagueTrack\"][value=\"Professional\"]");
        Assert.False(await proTrack.IsDisabledAsync());
        await proTrack.EvaluateAsync(
            "input => { input.checked = true; input.dispatchEvent(new Event('change', { bubbles: true })); }");
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/leaderboard' && new URLSearchParams(location.search).get('filters') === 'professional' && document.title === 'Pro League | Longevity World Cup'");

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForLeaderboardAsync(page);
        Assert.True(await proTrack.IsCheckedAsync());
        Assert.Equal("Pro League | Longevity World Cup", await page.TitleAsync());

        await page.GotoAsync("/league/professional", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await WaitForLeaderboardAsync(page);
        await page.WaitForFunctionAsync(
            "() => location.pathname === '/leaderboard' && new URLSearchParams(location.search).get('filters') === 'professional'");
        Assert.True(await proTrack.IsCheckedAsync());
        Assert.Equal("Pro League | Longevity World Cup", await page.TitleAsync());

        var leaderboardTitle = await page.TitleAsync();
        await page.Locator(".leaderboard tbody tr[data-athlete-name]:visible .athlete-name").First.ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('detailsModal')?.style.display === 'block' && document.title.includes('(#')");
        Assert.NotEqual(leaderboardTitle, await page.TitleAsync());
        await page.EvaluateAsync("() => window.closeModal()");
        await page.WaitForFunctionAsync("title => document.getElementById('detailsModal')?.style.display === 'none' && document.title === title", leaderboardTitle);
        Assert.Equal(leaderboardTitle, await page.TitleAsync());
    }

    [Theory]
    [InlineData("/league/amateur", "#league-track-filter-section input[value=\"Amateur\"]:checked")]
    [InlineData("/league/bortz", "#view-bortz:checked")]
    public async Task LeagueRoute_RendersFullLeaderboardAndPreservesCanonicalPath(
        string path,
        string activeStateSelector)
    {
        var app = App;
        var browser = Browser;
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
        return context;
    }

    private static async Task WaitForLeaderboardAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
            "() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");
    }

    private static async Task AssertFullLeaderboardAsync(
        IPage page,
        string expectedPath,
        string activeStateSelector,
        int? expectedVisibleRowCount = null)
    {
        await page.WaitForFunctionAsync(
            """
            ({ expectedPath, activeStateSelector }) =>
                location.pathname === expectedPath
                    && document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'
                    && document.querySelector('[data-leaderboard-page="full"]')
                    && document.querySelector(activeStateSelector)
                    && !document.getElementById('viewAllAthletesBtn')
            """,
            new { expectedPath, activeStateSelector });
        if (expectedVisibleRowCount is not null)
        {
            await page.WaitForFunctionAsync(
                """
                expectedCount => Array.from(document.querySelectorAll('.leaderboard tbody tr[data-athlete-name]'))
                    .filter(row => {
                        const rect = row.getBoundingClientRect();
                        const style = getComputedStyle(row);
                        return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                    }).length === expectedCount
                """,
                expectedVisibleRowCount.Value);
        }

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
