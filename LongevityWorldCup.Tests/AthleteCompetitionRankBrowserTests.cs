using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class AthleteCompetitionRankBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("Michael Lustgarten", false, 320)]
    [InlineData("Michael Lustgarten", false, 390)]
    [InlineData("Michael Lustgarten", false, 1280)]
    [InlineData("Benjamin Garden", true, 390)]
    [InlineData("Benjamin Garden", true, 1280)]
    [InlineData("Benjamin Garden", true, 320)]
    public async Task ProfileRanks_NameTheirCompetitionAndOpenTheMatchingAthlete(
        string name, bool isPro, int width)
    {
        await using var context = await NewContextAsync(width);
        var page = await context.NewPageAsync();

        var ultimateRank = await ReadLeaderboardRankAsync(page, name, "ultimate");
        var bortzRank = isPro ? await ReadLeaderboardRankAsync(page, name, "bortz") : null;
        var phenoRank = await ReadLeaderboardRankAsync(page, name, "pheno");
        var callingUrl = page.Url;
        await Row(page, name).Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        var profileUrl = page.Url;

        await Assertions.Expect(page.Locator("#athleteName")).Not.ToContainTextAsync("#");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync(name);
        await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("strong")).ToHaveTextAsync($"#{ultimateRank}");
        Assert.Contains($"ranked #{ultimateRank}", await page.Locator("#shareAthleteProfile").GetAttributeAsync("data-share-text"));
        Assert.DoesNotContain("rank:", await page.Locator("#lowestPhenoAgeContainer").InnerTextAsync());
        await AssertClockRankLinkAsync(page, "pheno", "Pheno", phenoRank, ultimateRank);
        if (isPro)
            await AssertClockRankLinkAsync(page, "bortz", "Bortz", bortzRank!, ultimateRank);
        else
            await Assertions.Expect(CompetitionLink(page, "bortz")).ToBeHiddenAsync();

        var phenoLink = CompetitionLink(page, "pheno");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const rankings = document.querySelector('#athleteRankings').getBoundingClientRect();
                const name = document.querySelector('#athleteName').getBoundingClientRect();
                const bio = document.querySelector('#athleteBio').getBoundingClientRect();
                return rankings.top >= name.bottom && rankings.bottom <= bio.top
                    && rankings.bottom <= innerHeight
                    && [...document.querySelectorAll('#athleteRankings a')].every(link => {
                        const box = link.getBoundingClientRect();
                        return box.width >= 44 && box.height >= 44 && box.left >= rankings.left && box.right <= rankings.right + 1;
                    });
            }
            """), "Every competition rank must be visible below the name on opening, with usable links.");
        await phenoLink.FocusAsync();
        await Assertions.Expect(phenoLink).ToBeFocusedAsync();
        await phenoLink.PressAsync("Enter");
        await AssertRankDestinationAsync(page, name, "pheno", phenoRank, ultimateRank);
        await page.ReloadAsync();
        await AssertRankDestinationAsync(page, name, "pheno", phenoRank, ultimateRank);

        await page.GoBackAsync();
        await page.WaitForURLAsync(profileUrl);
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#athleteName")).Not.ToContainTextAsync("#");

        if (isPro)
        {
            await CompetitionLink(page, "bortz").ClickAsync();
            await AssertRankDestinationAsync(page, name, "bortz", bortzRank!, ultimateRank);
            await page.GoBackAsync();
            await page.WaitForURLAsync(profileUrl);
            await WaitForProfileAsync(page);
        }

        await CompetitionLink(page, "ultimate").ClickAsync();
        await AssertRankDestinationAsync(page, name, "ultimate", ultimateRank, ultimateRank);
        await page.GoBackAsync();
        await page.WaitForURLAsync(profileUrl);
        await WaitForProfileAsync(page);

        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await page.WaitForURLAsync(callingUrl);
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Assertions.Expect(Row(page, name).Locator(".rank")).ToHaveTextAsync(phenoRank);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LatePodiumHydration_PreservesTheIncomingRankOrOpenProfileUrl(bool openProfile)
    {
        await using var context = await NewContextAsync(1280);
        var releasePodium = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/bitcoin/btcusd", async route =>
        {
            await releasePodium.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = "{\"btcToUsdRate\":100000}" });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/?view=pheno&utm_source=rank-link#rank-37");
            await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(1);
            if (openProfile)
            {
                await Row(page, "Michael Lustgarten").Locator(".athlete-name").ClickAsync();
                await WaitForProfileAsync(page);
            }
            var expectedUrl = page.Url;
            releasePodium.TrySetResult();
            await Assertions.Expect(page.Locator(".podium-item:not(.podium-skeleton-item)")).ToHaveCountAsync(3);
            Assert.Equal(expectedUrl, page.Url);
            if (!openProfile)
                Assert.Equal("#rank-37", new Uri(page.Url).Fragment);
        }
        finally
        {
            releasePodium.TrySetResult();
        }
    }

    [Theory]
    [InlineData("/athlete/michael-lustgarten")]
    [InlineData("/about")]
    public async Task DirectAndSharedProfiles_UseTheSameCompetitionContext(string entry)
    {
        await using var context = await NewContextAsync(390);
        var page = await context.NewPageAsync();
        const string name = "Michael Lustgarten";
        var ultimateRank = await ReadLeaderboardRankAsync(page, name, "ultimate");
        var phenoRank = await ReadLeaderboardRankAsync(page, name, "pheno");
        await page.GotoAsync(entry);
        if (entry == "/about")
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#athleteName")).Not.ToContainTextAsync("#");
        await AssertClockRankLinkAsync(page, "pheno", "Pheno", phenoRank, ultimateRank);
        await CompetitionLink(page, "pheno").ClickAsync();
        await AssertRankDestinationAsync(page, name, "pheno", phenoRank, ultimateRank);
    }

    [Fact]
    public async Task ReusingTheProfile_DoesNotKeepAnotherAthletesClockLink()
    {
        await using var context = await NewContextAsync(390);
        var page = await context.NewPageAsync();
        await ReadLeaderboardRankAsync(page, "Benjamin Garden", "ultimate");
        await Row(page, "Benjamin Garden").Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(CompetitionLink(page, "bortz")).ToBeVisibleAsync();
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        await page.Locator("#athleteSearch").FillAsync("Michael Lustgarten");
        var ultimateRank = (await Row(page, "Michael Lustgarten").Locator(".rank").InnerTextAsync()).Trim();
        await Row(page, "Michael Lustgarten").Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(CompetitionLink(page, "bortz")).ToBeHiddenAsync();
        await Assertions.Expect(CompetitionLink(page, "bortz")).ToHaveCountAsync(0);
        await Assertions.Expect(CompetitionLink(page, "pheno")).ToHaveAttributeAsync("href", $"/league/pheno#rank-{ultimateRank}");
    }

    private async Task<IBrowserContext> NewContextAsync(int width)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        return context;
    }

    private static async Task<string> ReadLeaderboardRankAsync(IPage page, string name, string view)
    {
        var route = view == "ultimate" ? "/leaderboard" : $"/league/{view}";
        await page.GotoAsync($"{route}?search={Uri.EscapeDataString(name)}");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        if (view != "ultimate")
            await Assertions.Expect(page.Locator($"#view-{view}")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync(name);
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(view == "ultimate" ? 1 : 2);
        await Assertions.Expect(Row(page, name)).ToBeVisibleAsync();
        return (await Row(page, name).Locator(".rank").InnerTextAsync()).Trim();
    }

    private static async Task AssertClockRankLinkAsync(IPage page, string view, string clock, string rank, string ultimateRank)
    {
        var link = CompetitionLink(page, view);
        await Assertions.Expect(link.Locator("span")).ToHaveTextAsync($"{clock} Age");
        await Assertions.Expect(link.Locator("strong")).ToHaveTextAsync($"#{rank}");
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-label", $"{clock} Age League rank {rank}");
        await Assertions.Expect(link).ToHaveAttributeAsync("href", $"/league/{clock.ToLowerInvariant()}#rank-{ultimateRank}");
    }

    private static async Task AssertRankDestinationAsync(IPage page, string name, string view, string rank, string ultimateRank)
    {
        var path = view == "ultimate" ? "/leaderboard" : $"/league/{view}";
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == path && new Uri(url).Fragment == $"#rank-{ultimateRank}");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator($"#view-{view}")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(view == "ultimate" ? 0 : 1);
        await Assertions.Expect(Row(page, name).Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(Row(page, name)).ToBeInViewportAsync();
    }

    private static Task WaitForProfileAsync(IPage page) => page.WaitForSelectorAsync(
        "#detailsModal[style*='display: block'] .modal-content[data-athlete-slug]:not(.is-loading):not(.has-load-error)");

    private static ILocator Row(IPage page, string name) => page.Locator($".leaderboard tbody tr[data-athlete-name=\"{name}\"]");
    private static ILocator CompetitionLink(IPage page, string view) => page.Locator($"#athleteRankings a[data-competition='{view}']");
}
