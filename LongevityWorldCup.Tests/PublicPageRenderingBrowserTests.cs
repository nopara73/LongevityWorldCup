using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class PublicPageRenderingBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/?filters=amateur")]
    [InlineData("/?view=pheno")]
    [InlineData("/?filters=women%27s,gen%2520x&view=pheno")]
    [InlineData("/leaderboard")]
    [InlineData("/leaderboard?utm_source=chatgpt.com")]
    [InlineData("/league/bortz")]
    [InlineData("/league/pheno")]
    [InlineData("/league/improvement")]
    [InlineData("/league/bortz-improvement")]
    [InlineData("/league/crowd")]
    [InlineData("/league/amateur")]
    [InlineData("/league/womens")]
    [InlineData("/league/gen-x")]
    [InlineData("/flag/hungary")]
    [InlineData("/leaderboard?filters=women%27s,gen%2520x&view=pheno")]
    public async Task InitialHtml_MatchesInteractiveRanksScoresAndAnchors(string path)
    {
        await using var staticContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var staticPage = await staticContext.NewPageAsync();
        await staticPage.GotoAsync(path);
        var initial = await ReadRows(staticPage);
        await Assertions.Expect(staticPage.Locator(".leaderboard table tbody")).ToHaveAttributeAsync("data-server-rendered", "true");
        if (!path.Contains("crowd")) Assert.NotEmpty(initial);

        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => !document.querySelector('.leaderboard table tbody').hasAttribute('data-server-rendered') && document.getElementById('leaderboardStatus').textContent === 'Leaderboard loaded.'");
        Assert.Equal(initial, await ReadRows(page));
        if (path == "/")
        {
            Assert.Equal(await staticPage.Locator(".podium .athlete-name").AllTextContentsAsync(), await page.Locator(".podium .athlete-name").AllTextContentsAsync());
            Assert.Equal(await staticPage.Locator(".podium .age-reduction").AllTextContentsAsync(), await page.Locator(".podium .age-reduction").AllTextContentsAsync());
            Assert.Equal(await staticPage.Locator(".podium a.athlete-profile-link").EvaluateAllAsync<string[]>("links => links.map(link => link.getAttribute('href'))"),
                await page.Locator(".podium a.athlete-profile-link").EvaluateAllAsync<string[]>("links => links.map(link => link.getAttribute('href'))"));
            Assert.Equal(7, initial.Length);
        }
    }

    [Theory]
    [InlineData("michael-lustgarten", 1280)]
    [InlineData("ron-lugbill", 390)]
    public async Task DirectProfile_IsReadableBeforeScripts_AndKeepsItsModal(string slug, int width)
    {
        await using var staticContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false, ViewportSize = new() { Width = width, Height = 844 } });
        var staticPage = await staticContext.NewPageAsync();
        await staticPage.GotoAsync($"/athlete/{slug}");
        await Assertions.Expect(staticPage.Locator("#detailsModal")).ToBeVisibleAsync();
        await Assertions.Expect(staticPage.Locator("#guessAgeContainer")).ToBeHiddenAsync();
        var initialPortrait = await staticPage.Locator("#modalProfilePic").GetAttributeAsync("src");
        var initial = await ProfileFacts(staticPage);
        Assert.False(string.IsNullOrWhiteSpace(initial[0]));
        Assert.DoesNotContain("Loading", initial[0]);
        await CaptureProfile(staticPage, $"{slug}-{width}-initial");

        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/athlete/{slug}");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').hasAttribute('data-server-rendered-profile') && document.querySelector('#modalProfilePic').hasAttribute('data-full-src')");
        Assert.Equal(initial, await ProfileFacts(page));
        Assert.Equal(initialPortrait, await page.Locator("#modalProfilePic").GetAttributeAsync("src"));
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await CaptureProfile(page, $"{slug}-{width}-interactive");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
    }

    [Fact]
    public async Task SlowDataRequest_PreservesContent_AndProfileCanCloseBeforeItFinishes()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/athlete/michael-lustgarten?utm_source=chatgpt.com#leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync("Michael Lustgarten");
            await Assertions.Expect(page.Locator(".podium [data-athlete-name]")).ToHaveCountAsync(3);
            await Assertions.Expect(page.Locator(".leaderboard .server-rendered-leaderboard-row")).ToHaveCountAsync(7);
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            Assert.Equal("/?utm_source=chatgpt.com#leaderboard", new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);
        }
        finally { release.TrySetResult(); }
        await page.WaitForFunctionAsync("() => document.getElementById('leaderboardStatus').textContent === 'Leaderboard loaded.'");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ProfileFailure_KeepsReadableDetails_AndRetryPreservesTheHomepage()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var offline = true;
        await context.RouteAsync("**/api/data/athletes", route => offline ? route.AbortAsync() : route.ContinueAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/michael-lustgarten");
        await Assertions.Expect(page.Locator("#athleteLoadError")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync("Michael Lustgarten");
        await Assertions.Expect(page.Locator("#athleteBio")).Not.ToBeEmptyAsync();
        offline = false;
        await page.Locator("#retryAthleteLoad").ClickAsync();
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').hasAttribute('data-server-rendered-profile') && document.querySelector('#modalProfilePic').hasAttribute('data-full-src')");
        await Assertions.Expect(page.Locator("#athleteLoadError")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#shareAthleteProfile")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Equal("/", new Uri(page.Url).AbsolutePath);
        await Assertions.Expect(page.Locator(".podium [data-athlete-name]:visible")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".leaderboard table tbody tr[data-athlete-name]:visible")).ToHaveCountAsync(7);
    }

    [Fact]
    public async Task DataFailure_DoesNotEraseThePublicLeaderboard()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.RouteAsync("**/api/data/athletes", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard could not load.");
        Assert.True(await page.Locator(".server-rendered-leaderboard-row").CountAsync() > 10);
        await Assertions.Expect(page.Locator(".leaderboard-retry-button")).ToBeVisibleAsync();
    }

    private static Task<string[]> ReadRows(IPage page) => page.Locator(".leaderboard table tbody tr[data-athlete-name]:visible").EvaluateAllAsync<string[]>(
        "rows => rows.map(row => [row.id, row.dataset.athleteName, row.querySelector('.rank').textContent, row.querySelector('.age-reduction').textContent].join('|'))");

    private static Task<string[]> ProfileFacts(IPage page) => page.EvaluateAsync<string[]>(
        "() => ['athleteName', 'athleteRankings', 'athleteBio', 'chronologicalAge', 'lowestPhenoAge', 'lowestBortzAge', 'ageReduction', 'ageReductionPercent', 'bortzAgeReduction', 'bortzAgeReductionPercent'].map(id => document.getElementById(id).textContent.trim())");

    private static async Task CaptureProfile(IPage page, string name)
    {
        var directory = Path.Combine(FindRepositoryRoot(), ".artifacts", "public-rendering");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png") });
    }
}
