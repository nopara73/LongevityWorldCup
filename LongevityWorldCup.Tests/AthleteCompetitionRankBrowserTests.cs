using Microsoft.Playwright;
using System.Text.Json.Nodes;
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
    [InlineData("Valerie Orsoni", true, 320)]
    [InlineData("Valerie Orsoni", true, 390)]
    [InlineData("Valerie Orsoni", true, 1280)]
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
        await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("span")).ToHaveTextAsync("Ultimate League");
        await Assertions.Expect(CompetitionLink(page, "ultimate")).ToHaveAttributeAsync("aria-label", $"Ultimate League rank {ultimateRank}");
        await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("strong")).ToHaveTextAsync($"#{ultimateRank}");
        Assert.Contains($"ranked #{ultimateRank}", await page.Locator("#shareAthleteProfile").GetAttributeAsync("data-share-text"));
        Assert.DoesNotContain("rank:", await page.Locator("#lowestPhenoAgeContainer").InnerTextAsync());
        var bestView = isPro ? "ultimate" : "pheno";
        var bestRank = isPro ? ultimateRank : phenoRank;
        if (isPro)
        {
            Assert.Equal(ultimateRank, bortzRank);
            Assert.True(int.Parse(ultimateRank) <= int.Parse(phenoRank));
        }
        else
        {
            Assert.True(int.Parse(phenoRank) < int.Parse(ultimateRank));
            await AssertClockRankLinkAsync(page, "pheno", "Pheno", phenoRank, ultimateRank);
        }
        await Assertions.Expect(page.Locator("#athleteRankings a")).ToHaveCountAsync(isPro ? 1 : 2);
        await Assertions.Expect(page.Locator("#athleteRankings a").First).ToHaveAttributeAsync("data-competition", "ultimate");
        await Assertions.Expect(CompetitionLink(page, "bortz")).ToHaveCountAsync(0);
        if (isPro)
            await Assertions.Expect(CompetitionLink(page, "pheno")).ToHaveCountAsync(0);

        var bestLink = CompetitionLink(page, bestView);
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
        await bestLink.FocusAsync();
        await Assertions.Expect(bestLink).ToBeFocusedAsync();
        await bestLink.PressAsync("Enter");
        await AssertRankDestinationAsync(page, name, bestView, bestRank, ultimateRank);
        await page.ReloadAsync();
        await AssertRankDestinationAsync(page, name, bestView, bestRank, ultimateRank);

        await page.GoBackAsync();
        await page.WaitForURLAsync(profileUrl);
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#athleteName")).Not.ToContainTextAsync("#");

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
    [InlineData("improvement", "pheno-improvement", "Pheno Improvement", 320)]
    [InlineData("improvement", "pheno-improvement", "Pheno Improvement", 1280)]
    [InlineData("bortz-improvement", "bortz-improvement", "Bortz Improvement", 320)]
    [InlineData("bortz-improvement", "bortz-improvement", "Bortz Improvement", 1280)]
    [InlineData("crowd", "crowd", "Crowd Age", 320)]
    [InlineData("crowd", "crowd", "Crowd Age", 1280)]
    [InlineData("unqualified-crowd", "bortz", "Bortz Age", 390)]
    public async Task ProfileRanks_IncludeImprovementAndQualifiedCrowdViews(
        string scenario, string competition, string label, int width)
    {
        const string name = "Siim Land";
        var view = scenario == "unqualified-crowd" ? "bortz" : scenario;
        await using var context = await NewContextAsync(width);
        await context.RouteAsync("**/api/data/athletes*", async route =>
        {
            var isImageRefresh = new Uri(route.Request.Url).Query.Contains("profileImageRefresh=", StringComparison.Ordinal);
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var target = athletes.OfType<JsonObject>().Single(athlete => athlete["Name"]?.GetValue<string>() == name);
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                var isTarget = ReferenceEquals(athlete, target);
                // Keep the other improvement views behind the target's clock ranks.
                athlete["PhenoAgeImprovementFromWorst"] = isTarget
                    ? scenario == "improvement" ? -1000 : 0
                    : -1;
                athlete["BortzAgeImprovementFromWorst"] = isTarget
                    ? scenario == "bortz-improvement" ? -1000 : 0
                    : -1;
                athlete["CrowdAge"] = 20;
                athlete["CrowdCount"] = isTarget && scenario == "crowd" && !isImageRefresh ? 100
                    : isTarget && scenario == "unqualified-crowd" ? 99 : 0;
                if (isTarget && isImageRefresh)
                    athlete["ProfileImageId"] = new string('f', 64);
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        var ultimateRank = await ReadLeaderboardRankAsync(page, name, "ultimate");
        var fallbackView = "ultimate";
        var fallbackRank = ultimateRank;
        if (scenario is "unqualified-crowd" or "crowd")
        {
            var bortzRank = await ReadLeaderboardRankAsync(page, name, "bortz");
            var phenoRank = await ReadLeaderboardRankAsync(page, name, "pheno");
            if (int.Parse(bortzRank) < int.Parse(fallbackRank))
                (fallbackView, fallbackRank) = ("bortz", bortzRank);
            if (int.Parse(phenoRank) < int.Parse(fallbackRank))
                (fallbackView, fallbackRank) = ("pheno", phenoRank);
        }
        if (scenario == "unqualified-crowd")
        {
            view = competition = fallbackView;
            label = view == "ultimate" ? "Ultimate League" : view == "bortz" ? "Bortz Age" : "Pheno Age";
        }
        var bestRank = await ReadLeaderboardRankAsync(page, name, view);
        if (scenario != "unqualified-crowd")
            Assert.Equal("1", bestRank);
        await Row(page, name).Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);

        await Assertions.Expect(page.Locator("#athleteRankings a")).ToHaveCountAsync(view == "ultimate" ? 1 : 2);
        await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("strong")).ToHaveTextAsync($"#{ultimateRank}");
        var bestLink = CompetitionLink(page, competition);
        await Assertions.Expect(bestLink.Locator("span")).ToHaveTextAsync(label);
        await Assertions.Expect(bestLink.Locator("strong")).ToHaveTextAsync($"#{bestRank}");
        await Assertions.Expect(bestLink).ToHaveAttributeAsync("href", $"{(view == "ultimate" ? "/leaderboard" : $"/league/{view}")}#rank-{ultimateRank}");
        Assert.True(await page.Locator("#athleteRankings").EvaluateAsync<bool>("""
            rankings => {
                const links = [...rankings.querySelectorAll('a')];
                const ranks = links.map(link => link.querySelector('strong').getBoundingClientRect());
                return links.every(link => link.scrollWidth <= link.clientWidth)
                    && ranks.every(rank => Math.abs(ranks[0].bottom - rank.bottom) <= 1);
            }
            """), "Complete ranking view labels must fit and their rank values must align on mobile and desktop.");
        await bestLink.ClickAsync();
        await AssertRankDestinationAsync(page, name, view, bestRank, ultimateRank);
        await page.ReloadAsync();
        await AssertRankDestinationAsync(page, name, view, bestRank, ultimateRank);

        if (scenario == "crowd")
        {
            await page.GoBackAsync();
            await WaitForProfileAsync(page);
            Assert.True(await page.EvaluateAsync<bool>("() => window.refreshAthleteAfterStaleGuess('siim-land')"));
            await Assertions.Expect(page.Locator("#athleteRankings a")).ToHaveCountAsync(fallbackView == "ultimate" ? 1 : 2);
            await Assertions.Expect(CompetitionLink(page, "crowd")).ToHaveCountAsync(0);
            await Assertions.Expect(CompetitionLink(page, fallbackView).Locator("strong")).ToHaveTextAsync($"#{fallbackRank}");
            await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("strong")).ToHaveTextAsync($"#{ultimateRank}");
        }
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
    public async Task ReusingTheProfile_RemovesThePreviousAthletesBetterRankingView()
    {
        await using var context = await NewContextAsync(390);
        var page = await context.NewPageAsync();
        await ReadLeaderboardRankAsync(page, "Michael Lustgarten", "ultimate");
        await Row(page, "Michael Lustgarten").Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(CompetitionLink(page, "pheno")).ToBeVisibleAsync();
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();

        await page.Locator("#athleteSearch").FillAsync("Benjamin Garden");
        var ultimateRank = (await Row(page, "Benjamin Garden").Locator(".rank").InnerTextAsync()).Trim();
        await Row(page, "Benjamin Garden").Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(page.Locator("#athleteRankings a")).ToHaveCountAsync(1);
        await Assertions.Expect(CompetitionLink(page, "bortz")).ToHaveCountAsync(0);
        await Assertions.Expect(CompetitionLink(page, "pheno")).ToHaveCountAsync(0);
        await Assertions.Expect(CompetitionLink(page, "ultimate")).ToHaveAttributeAsync("href", $"/leaderboard#rank-{ultimateRank}");
    }

    [Theory]
    [InlineData(99)]
    [InlineData(100)]
    public async Task AcceptedCrowdGuess_RefreshesTheBestViewAndSurvivesReopening(int initialGuessCount)
    {
        const string name = "Siim Land";
        await using var context = await NewContextAsync(390, skipGuess: false);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                var isTarget = athlete["Name"]?.GetValue<string>() == name;
                athlete["CrowdAge"] = 100;
                athlete["CrowdCount"] = isTarget ? initialGuessCount : 100;
                athlete["PhenoAgeImprovementFromWorst"] = isTarget ? 0 : -1;
                athlete["BortzAgeImprovementFromWorst"] = isTarget ? 0 : -1;
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });
        var submissions = 0;
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            submissions++;
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = new JsonObject
                {
                    ["crowdAge"] = 1, ["crowdCount"] = initialGuessCount + 1,
                    ["actualAge"] = 40, ["guessAccepted"] = true
                }.ToJsonString()
            });
        });
        var page = await context.NewPageAsync();
        var ultimateRank = await ReadLeaderboardRankAsync(page, name, "ultimate");
        await Row(page, name).Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(CompetitionLink(page, "crowd")).ToHaveCountAsync(0);
        await page.Locator("#gmaRange").EvaluateAsync("range => { range.value = '40'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        await Assertions.Expect(CompetitionLink(page, "crowd").Locator("strong")).ToHaveTextAsync("#1");
        await Assertions.Expect(page.Locator("#crowdCount")).ToHaveTextAsync((initialGuessCount + 1).ToString());
        await Assertions.Expect(page.Locator("#athleteRankings a")).ToHaveCountAsync(2);
        await Assertions.Expect(CompetitionLink(page, "ultimate").Locator("strong")).ToHaveTextAsync($"#{ultimateRank}");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await Row(page, name).Locator(".athlete-name").ClickAsync();
        await WaitForProfileAsync(page);
        await Assertions.Expect(CompetitionLink(page, "crowd").Locator("strong")).ToHaveTextAsync("#1");
        await Assertions.Expect(CompetitionLink(page, "crowd")).ToHaveAttributeAsync("href", $"/league/crowd#rank-{ultimateRank}");
        await Assertions.Expect(page.Locator("#crowdCount")).ToHaveTextAsync((initialGuessCount + 1).ToString());
        Assert.Equal(1, submissions);
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool skipGuess = true)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (skipGuess)
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
