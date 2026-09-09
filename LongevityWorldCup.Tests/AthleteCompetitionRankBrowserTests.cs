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
    [InlineData("Michael Lustgarten", false, 390)]
    [InlineData("Michael Lustgarten", false, 1280)]
    [InlineData("Benjamin Garden", true, 390)]
    [InlineData("Benjamin Garden", true, 1280)]
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

        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync($"Ultimate League #{ultimateRank}");
        Assert.Contains($"ranked #{ultimateRank}", await page.Locator("#shareAthleteProfile").GetAttributeAsync("data-share-text"));
        Assert.DoesNotContain("rank:", await page.Locator("#lowestPhenoAgeContainer").InnerTextAsync());
        await AssertClockRankLinkAsync(page, "lowestPhenoAgeRank", "Pheno", phenoRank, ultimateRank);
        if (isPro)
            await AssertClockRankLinkAsync(page, "bortzAgeReductionRank", "Bortz", bortzRank!, ultimateRank);
        else
            await Assertions.Expect(page.Locator("#bortzAgeReductionRank")).ToBeHiddenAsync();

        var phenoLink = page.Locator("#lowestPhenoAgeRank");
        await phenoLink.ScrollIntoViewIfNeededAsync();
        var stickyRank = page.Locator("#stickyAthleteName .modal-league-rank");
        await Assertions.Expect(stickyRank).ToBeVisibleAsync();
        Assert.True(await stickyRank.EvaluateAsync<bool>("""
            el => {
                const text = document.createRange();
                text.selectNodeContents(el);
                const label = text.getBoundingClientRect();
                const title = el.parentElement.getBoundingClientRect();
                return label.left >= title.left && label.right <= title.right
                    && label.top >= title.top && label.bottom <= title.bottom;
            }
            """), "The sticky heading must show the complete competition and rank.");
        await phenoLink.FocusAsync();
        await Assertions.Expect(phenoLink).ToBeFocusedAsync();
        await phenoLink.PressAsync("Enter");
        await AssertRankDestinationAsync(page, name, "pheno", phenoRank, ultimateRank);
        await page.ReloadAsync();
        await AssertRankDestinationAsync(page, name, "pheno", phenoRank, ultimateRank);

        await page.GoBackAsync();
        await page.WaitForURLAsync(profileUrl);
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync($"Ultimate League #{ultimateRank}");

        if (isPro)
        {
            await page.Locator("#bortzAgeReductionRank").ClickAsync();
            await AssertRankDestinationAsync(page, name, "bortz", bortzRank!, ultimateRank);
            await page.GoBackAsync();
            await page.WaitForURLAsync(profileUrl);
            await WaitForProfileAsync(page);
        }

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
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync($"Ultimate League #{ultimateRank}");
        await AssertClockRankLinkAsync(page, "lowestPhenoAgeRank", "Pheno", phenoRank, ultimateRank);
        await page.Locator("#lowestPhenoAgeRank").ClickAsync();
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
        await Assertions.Expect(page.Locator("#bortzAgeReductionRank")).ToBeVisibleAsync();
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        await page.Locator("#athleteSearch").FillAsync("Michael Lustgarten");
        var ultimateRank = (await Row(page, "Michael Lustgarten").Locator(".rank").InnerTextAsync()).Trim();
        await Row(page, "Michael Lustgarten").Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#bortzAgeReductionRank")).ToBeHiddenAsync();
        Assert.Null(await page.Locator("#bortzAgeReductionRank").GetAttributeAsync("href"));
        await Assertions.Expect(page.Locator("#lowestPhenoAgeRank")).ToHaveAttributeAsync("href", $"/league/pheno#rank-{ultimateRank}");
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

    private static async Task AssertClockRankLinkAsync(IPage page, string id, string clock, string rank, string ultimateRank)
    {
        var link = page.Locator($"#{id}");
        await Assertions.Expect(link).ToHaveTextAsync($"#{rank}");
        await Assertions.Expect(link).ToHaveAttributeAsync("aria-label", $"{clock} Age League rank {rank}");
        await Assertions.Expect(link).ToHaveAttributeAsync("href", $"/league/{clock.ToLowerInvariant()}#rank-{ultimateRank}");
        var rowId = clock == "Pheno" ? "ageReductionContainer" : "bortzAgeReductionContainer";
        Assert.True(await link.EvaluateAsync<bool>("(el, id) => el.closest('tr')?.id === id", rowId));
    }

    private static async Task AssertRankDestinationAsync(IPage page, string name, string view, string rank, string ultimateRank)
    {
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == $"/league/{view}" && new Uri(url).Fragment == $"#rank-{ultimateRank}");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator($"#view-{view}")).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(1);
        await Assertions.Expect(Row(page, name).Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(Row(page, name)).ToBeInViewportAsync();
    }

    private static Task WaitForProfileAsync(IPage page) => page.WaitForSelectorAsync(
        "#detailsModal[style*='display: block'] .modal-content[data-athlete-slug]:not(.is-loading):not(.has-load-error)");

    private static ILocator Row(IPage page, string name) => page.Locator($".leaderboard tbody tr[data-athlete-name=\"{name}\"]");
}
