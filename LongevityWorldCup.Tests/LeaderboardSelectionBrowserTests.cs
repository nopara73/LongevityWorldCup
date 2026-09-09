using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardSelectionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("ultimate", "ULTIMATE LEAGUE")]
    [InlineData("pheno", "PHENO AGE LEAGUE")]
    [InlineData("bortz", "BORTZ AGE LEAGUE")]
    [InlineData("improvement", "PHENO IMPROVEMENT LEAGUE")]
    [InlineData("bortz-improvement", "BORTZ IMPROVEMENT LEAGUE")]
    [InlineData("crowd", "CROWD AGE LEAGUE")]
    public async Task ShortResults_KeepTheFullLeagueNameVisibleWithoutStretchingRows(string view, string title)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 844 }, ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        if (view == "crowd")
        {
            await context.RouteAsync("**/api/data/athletes", async route =>
            {
                var response = await route.FetchAsync();
                var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
                foreach (var athlete in athletes.OfType<JsonObject>().Take(5))
                {
                    athlete["CrowdAge"] = 30;
                    athlete["CrowdCount"] = 150;
                }
                await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
            });
        }

        var page = await context.NewPageAsync();
        await page.GotoAsync(view == "ultimate" ? "/leaderboard" : $"/league/{view}");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(view == "ultimate" ? 0 : 1);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var originalCount = await Rows(page).CountAsync();
        var name = view == "pheno" ? "Max" : (await Rows(page).First.GetAttributeAsync("data-athlete-name"))!;
        var row = Rows(page).Filter(new() { Has = page.Locator(".athlete-name").Filter(new() { HasText = name }) }).First;
        var originalHeight = (await row.BoundingBoxAsync())!.Height;
        await page.Locator("#athleteSearch").FillAsync(view == "pheno" ? "Max -18.30" : name);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await AssertVisibleRailAsync(page, title);
        Assert.InRange(Math.Abs((await Rows(page).BoundingBoxAsync())!.Height - originalHeight), 0, 1);

        await page.ReloadAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await AssertVisibleRailAsync(page, title);
        if (view is "pheno" or "improvement")
        {
            await page.SetViewportSizeAsync(800, 844);
            await AssertVisibleRailAsync(page, title);
            await page.SetViewportSizeAsync(390, 844);
            await Assertions.Expect(page.Locator(".collapsed-title")).ToBeHiddenAsync();
            await page.WaitForFunctionAsync("() => !document.querySelector('.leaderboard').style.getPropertyValue('--leaderboard-title-height')");
            await page.SetViewportSizeAsync(1280, 844);
            await AssertVisibleRailAsync(page, title);
        }

        await page.Locator("#athleteSearch").FillAsync("nobody-matches-this-phrase");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync("0 athletes");
        await AssertVisibleRailAsync(page, title);
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(originalCount);
        await AssertVisibleRailAsync(page, title);
    }

    private static Task AssertVisibleRailAsync(IPage page, string title) => page.WaitForFunctionAsync(
        """
        expected => {
            const title = document.querySelector('.collapsed-title');
            const rail = document.querySelector('.sidebar').getBoundingClientRect();
            const frame = document.querySelector('.leaderboard').getBoundingClientRect();
            const rect = title.getBoundingClientRect();
            return title.textContent.trim() === expected && rect.height > 0
                && rect.top >= rail.top && rect.bottom <= rail.bottom + 1
                && title.scrollHeight <= title.clientHeight + 1
                && Math.abs(frame.bottom - rail.bottom) <= 2
                && document.documentElement.scrollWidth <= innerWidth;
        }
        """, title);

    [Theory]
    [InlineData("/leaderboard", 0)]
    [InlineData("/league/pheno", 1)]
    [InlineData("/league/bortz", 1)]
    [InlineData("/league/improvement", 1)]
    [InlineData("/league/bortz-improvement", 1)]
    [InlineData("/league/crowd", 1)]
    [InlineData("/league/amateur", 1)]
    [InlineData("/flag/hungary", 1)]
    [InlineData("/leaderboard?filters=women%27s,gen%20x&view=pheno", 3)]
    public async Task Searching_PreservesTheSelectedLeagueRanksThroughReloadAndClear(string path, int filterCount)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        if (path == "/league/crowd")
        {
            await context.RouteAsync("**/api/data/athletes", async route =>
            {
                var response = await route.FetchAsync();
                var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
                foreach (var athlete in athletes.OfType<JsonObject>().Take(5))
                {
                    athlete["CrowdAge"] = 30;
                    athlete["CrowdCount"] = 150;
                }
                await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
            });
        }

        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync(new System.Text.RegularExpressions.Regex(@"^\d+ athletes?$"));
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(filterCount);
        var initialCount = await Rows(page).CountAsync();
        Assert.True(initialCount > 1);
        var row = Rows(page).Nth(initialCount / 2);
        var name = (await row.GetAttributeAsync("data-athlete-name"))!;
        var rank = await row.Locator(".rank").InnerTextAsync();
        var metric = await row.Locator(".age-reduction").InnerTextAsync();
        var athleteLabel = (await row.Locator(".athlete-name").GetAttributeAsync("aria-label"))!;
        var targetRow = Rows(page).Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = athleteLabel, Exact = true }) });
        Assert.NotEqual("1", rank);

        await page.Locator("#athleteSearch").FillAsync(name);
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(filterCount + 1);
        var matchingCount = await Rows(page).CountAsync();
        Assert.InRange(matchingCount, 1, initialCount - 1);
        await Assertions.Expect(targetRow.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(targetRow.Locator(".age-reduction")).ToHaveTextAsync(metric);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(filterCount + 1);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(matchingCount);
        await Assertions.Expect(targetRow.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(targetRow.Locator(".age-reduction")).ToHaveTextAsync(metric);

        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(initialCount);
        await Assertions.Expect(targetRow.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(targetRow.Locator(".age-reduction")).ToHaveTextAsync(metric);
    }

    [Fact]
    public async Task SearchingByRank_UsesTheDisplayedRankAndRecomputesAfterAViewChange()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?search=michael%20lustgarten%201");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync("1");

        await page.Locator("#athleteSearch").FillAsync("michael lustgarten");
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Pheno age filter", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator("#view-ultimate")).ToBeCheckedAsync();
        var ultimateRank = (await Rows(page).First.GetAttributeAsync("data-rank"))!;
        Assert.NotEqual("1", ultimateRank);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync(ultimateRank);
        await page.Locator("#athleteSearch").FillAsync($"michael lustgarten {ultimateRank}");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync(ultimateRank);
        await page.GotoAsync($"/league/pheno?search=michael%20lustgarten%20{ultimateRank}");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync("0 athletes");
    }

    [Fact]
    public async Task SearchingByDisplayedScore_PreservesItsPrecisionThroughReloadAndClear()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(1);
        var row = Rows(page).Filter(new() { Has = page.Locator(".age-reduction").Filter(new() { HasTextRegex = new(@"\d+\.\d{2} years") }) }).First;
        var name = (await row.GetAttributeAsync("data-athlete-name"))!;
        var rank = await row.Locator(".rank").InnerTextAsync();
        var metric = await row.Locator(".age-reduction").InnerTextAsync();
        var label = (await row.Locator(".athlete-name").GetAttributeAsync("aria-label"))!;
        var target = Rows(page).Filter(new() { Has = page.GetByRole(AriaRole.Button, new() { Name = label, Exact = true }) });

        await page.Locator("#athleteSearch").FillAsync($"{name} {metric}");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        await Assertions.Expect(target).ToHaveCountAsync(1);
        await Assertions.Expect(target.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(target.Locator(".age-reduction")).ToHaveTextAsync(metric);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        await Assertions.Expect(target.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(target.Locator(".age-reduction")).ToHaveTextAsync(metric);

        var oldScore = double.Parse(metric.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)
            .ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        await page.Locator("#athleteSearch").FillAsync($"{name} {oldScore}");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip").Last).ToContainTextAsync(oldScore);
        await Assertions.Expect(target).ToHaveCountAsync(1);
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(1);
        await Assertions.Expect(target.Locator(".rank")).ToHaveTextAsync(rank);
        await Assertions.Expect(target.Locator(".age-reduction")).ToHaveTextAsync(metric);
    }

    [Fact]
    public async Task SidebarViewSwitch_UsesTheProposedCompetitionRanksDuringSearch()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?search=Max%2011");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync("0 athletes");
        await page.Locator(".sidebar-toggle").ClickAsync();
        var pheno = page.Locator("input[name=agingClockView][value=pheno]");
        await Assertions.Expect(pheno).ToBeEnabledAsync();
        await Assertions.Expect(pheno.Locator("..").Locator(".filter-count")).ToHaveTextAsync("1");
        await pheno.CheckAsync();
        await Assertions.Expect(page.Locator("#view-pheno")).ToBeCheckedAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page)).ToHaveAttributeAsync("data-athlete-name", "Max");
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync("11");
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("Max 11");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync("11");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultLeaderboard_InitializesClockCountsAndPreservesDirectLinks(bool profileLink)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        var path = profileLink ? "/leaderboard?athlete=michael-lustgarten" : "/leaderboard?utm_source=rank-link#rank-37";
        await page.GotoAsync(path);
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        if (profileLink)
        {
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
            await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
            Assert.Equal("/leaderboard", new Uri(page.Url).AbsolutePath);
        }
        var pheno = page.Locator("label[data-aging-clock-view=pheno]");
        var bortz = page.Locator("label[data-aging-clock-view=bortz]");
        await Assertions.Expect(pheno.Locator(".filter-count")).ToHaveTextAsync((await Rows(page).CountAsync()).ToString());
        Assert.True(int.Parse(await bortz.Locator(".filter-count").InnerTextAsync()) > 0);
        if (!profileLink) Assert.Equal(path, new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);
    }

    [Fact]
    public async Task SidebarMultiSelect_CanChangeTheRankOfAnAlreadyMatchingAthlete()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?filters=open&search=michael%20lustgarten");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        var originalRank = await Rows(page).Locator(".rank").InnerTextAsync();
        await page.Locator(".sidebar-toggle").ClickAsync();
        var women = page.Locator("input[name=division][value=\"Women's\"]");
        await Assertions.Expect(women.Locator("..").Locator(".filter-count")).ToHaveTextAsync("0");
        await Assertions.Expect(women).ToBeEnabledAsync();
        await women.CheckAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(3);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        Assert.True(int.Parse(await Rows(page).Locator(".rank").InnerTextAsync()) > int.Parse(originalRank));
        var combinedRank = await Rows(page).Locator(".rank").InnerTextAsync();
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(3);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync(combinedRank);
    }

    [Fact]
    public async Task SidebarMultiSelect_HidesEmptyFlagsThatCannotChangeTheResult()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?search=michael%20lustgarten");
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync("1");
        await page.Locator(".sidebar-toggle").ClickAsync();
        var flags = page.Locator("#flag-filter-section input[name=flag]");
        Assert.True(await flags.CountAsync() > 1);
        await page.Locator("#flag-filter-section input:not(:disabled)").First.CheckAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#flag-filter-section li:visible")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#flag-filter-section input:not(:disabled)")).ToHaveCountAsync(1);
        await Assertions.Expect(Rows(page).Locator(".rank")).ToHaveTextAsync("1");

        // Clearing the search restores real alternative members in the same group.
        await page.Locator("#athleteSearch").FillAsync("");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#flag-filter-section input:not(:disabled)")).ToHaveCountAsync(await flags.CountAsync());
    }

    [Theory]
    [InlineData("division")]
    [InlineData("flag")]
    [InlineData("generation")]
    [InlineData("leagueTrack")]
    public async Task SidebarMultiSelect_KeepsOtherMembersOfTheSameGroupAvailable(string group)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        var options = page.Locator($"input[name={group}]");
        var first = options.Nth(0);
        var second = options.Nth(1);
        var firstCount = int.Parse(await first.Locator("..").Locator(".filter-count").InnerTextAsync());
        var secondCount = int.Parse(await second.Locator("..").Locator(".filter-count").InnerTextAsync());
        await first.CheckAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(firstCount);
        await Assertions.Expect(second).ToBeEnabledAsync();
        await Assertions.Expect(second).ToBeVisibleAsync();
        await Assertions.Expect(second.Locator("..").Locator(".filter-count")).ToHaveTextAsync(secondCount.ToString());
        await second.CheckAsync();
        await Assertions.Expect(first).ToBeCheckedAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(firstCount + secondCount);
        if (group == "leagueTrack")
        {
            Assert.True(await Rows(page).EvaluateAllAsync<bool>("rows => rows.every(row => row.querySelector('.rank').textContent === row.dataset.rank)"));
        }
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(group == "leagueTrack" ? 0 : 2);
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(firstCount + secondCount);
    }

    [Fact]
    public async Task RemovingOneSelection_PreservesTheOthersAndTheSharedUrl()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        var page = await context.NewPageAsync();
        await OpenFilteredAsync(page);
        var initialCount = await Rows(page).CountAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Hungary filter", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        Assert.True(await Rows(page).CountAsync() > initialCount);
        Assert.True(await page.Locator("input[name=division][value=\"Women's\"]").IsCheckedAsync());
        Assert.True(await page.Locator("#view-pheno").IsCheckedAsync());
        Assert.Contains("view=pheno", page.Url);
        Assert.DoesNotContain("hungary", page.Url);
        Assert.True(await page.Locator(".leaderboard-selection-chip").Last.EvaluateAsync<bool>("e=>e===document.activeElement"));

        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(2);
        Assert.True(await page.Locator("#view-pheno").IsCheckedAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Pheno age filter", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(1);
        Assert.True(await page.Locator("#view-ultimate").IsCheckedAsync());
        Assert.True(await page.Locator("input[name=division][value=\"Women's\"]").IsCheckedAsync());
        await page.Locator("#clearLeaderboardSelection").ClickAsync();
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(0);
        Assert.Equal("/leaderboard", new Uri(page.Url).PathAndQuery);
        Assert.True(await page.Locator("#athleteSearch").EvaluateAsync<bool>("e=>e===document.activeElement"));
        Assert.Equal($"{await Rows(page).CountAsync()} athletes", await page.Locator("#leaderboardResultCount").InnerTextAsync());
    }

    [Fact]
    public async Task TrackChips_UseTheSameProAndAmateurLabelsAsTheSelectedFilters()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        foreach (var (filter, label) in new[] { ("professional", "Pro"), ("amateur", "Amateur") })
        {
            await page.GotoAsync($"/leaderboard?filters={filter}");
            var chip = page.GetByRole(AriaRole.Button, new() { Name = $"Remove {label} filter", Exact = true });
            await chip.WaitForAsync();
            Assert.Equal(1, await page.Locator(".leaderboard-selection-chip").CountAsync());
            var selectedTrack = page.Locator("input[name=leagueTrack]:checked");
            Assert.Equal(filter, (await selectedTrack.InputValueAsync()).ToLowerInvariant());
            Assert.Contains(label, await page.Locator("#rankingExplanation").InnerTextAsync());
            await chip.ClickAsync();
            await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(0);
            Assert.Equal("/leaderboard", new Uri(page.Url).PathAndQuery);
        }
    }

    [Fact]
    public async Task EmptySearch_CanBeRemovedWithoutLosingTheSelectedLeague()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await OpenFilteredAsync(page);
        var initialCount = await Rows(page).CountAsync();
        await page.Locator("#athleteSearch").FillAsync("nobody-matches-this-phrase");
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync("0 athletes");
        await page.GetByRole(AriaRole.Button, new() { Name = "Remove Search: nobody-matches-this-phrase filter", Exact = true }).ClickAsync();
        await Assertions.Expect(Rows(page)).ToHaveCountAsync(initialCount);
        Assert.Equal("", await page.Locator("#athleteSearch").InputValueAsync());
        Assert.Equal(3, await page.Locator(".leaderboard-selection-chip").CountAsync());
        Assert.True(await page.Locator("#view-pheno").IsCheckedAsync());
        Assert.DoesNotContain("search=", page.Url);
    }

    [Theory]
    [InlineData(320, 720, false)]
    [InlineData(390, 844, true)]
    [InlineData(844, 390, false)]
    public async Task MobileDrawer_KeepsItsResultsActionVisibleWhileFiltersScroll(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await OpenFilteredAsync(page);
        var toggle = page.Locator(".sidebar-toggle");
        Assert.Equal("3", await toggle.GetAttributeAsync("data-filter-count"));
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        var results = page.Locator("#showLeaderboardResults");
        await Assertions.Expect(results).ToBeVisibleAsync();
        var before = await results.BoundingBoxAsync();
        Assert.NotNull(before);
        Assert.InRange(before.Y + before.Height, 44, height);
        var list = page.Locator(".sidebar-filter-list");
        await list.EvaluateAsync("e=>e.scrollTop=e.scrollHeight");
        Assert.True(await list.EvaluateAsync<bool>("e=>e.scrollTop>0"));
        var after = await results.BoundingBoxAsync();
        Assert.NotNull(after);
        Assert.InRange(Math.Abs(before.Y - after.Y), 0, 1);
        Assert.InRange(Math.Abs(after.Width - (await list.BoundingBoxAsync())!.Width), 0, 1);
        await Assertions.Expect(results).ToHaveTextAsync("Show " + await page.Locator("#leaderboardResultCount").InnerTextAsync(), new() { UseInnerText = true });
        await page.Locator("#clearSidebarFiltersBtn").ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("data-filter-count", "0");
        await Assertions.Expect(results).ToHaveTextAsync("Show " + await page.Locator("#leaderboardResultCount").InnerTextAsync(), new() { UseInnerText = true });
        await results.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth"));
    }

    private static ILocator Rows(IPage page) => page.Locator(".leaderboard tbody tr[data-athlete-name]:visible");

    private static async Task OpenFilteredAsync(IPage page)
    {
        await page.GotoAsync("/leaderboard?filters=hungary,women%27s&view=pheno");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync($"{await Rows(page).CountAsync()} athletes");
    }
}
