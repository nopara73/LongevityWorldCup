using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class FlagLeaderboardReviewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task ServerRenderedTeamPortraits_CarryTheSamePublicNamesAsTheirMembers()
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?group=flags");
        var teams = page.Locator(".flag-team");
        Assert.True(await teams.CountAsync() > 0);
        foreach (var team in await teams.AllAsync())
        {
            var expected = await team.Locator(".flag-athlete-name").EvaluateAllAsync<string[]>("names=>names.map(name=>name.firstChild.textContent)");
            var portraits = team.Locator(".flag-team-portraits img");
            var actual = await portraits.EvaluateAllAsync<string[]>("images=>images.map(image=>image.dataset.athleteName)");
            Assert.Equal(expected, actual);
            Assert.All(actual, name => Assert.False(string.IsNullOrWhiteSpace(name)));
            foreach (var image in await portraits.AllAsync()) await Assertions.Expect(image).ToHaveAttributeAsync("alt", "");
        }
    }

    [Fact]
    public async Task MissingTeamPortraits_KeepEachPublicIdentityWithoutAddingDecorativeAltText()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["Flag"] = index < 3 ? "Hungary" : index < 6 ? "Germany" : "";
                athlete["DisplayName"] = $"Public Athlete {++index}";
                foreach (var key in athlete.Select(p => p.Key).Where(key => key.StartsWith("ProfilePic", StringComparison.Ordinal)).ToArray())
                    athlete[key] = null;
            }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var portraits = page.Locator(".flag-team-portraits img");
        await Assertions.Expect(portraits).ToHaveCountAsync(6);
        foreach (var image in await portraits.AllAsync())
        {
            await Assertions.Expect(image).ToHaveAttributeAsync("data-athlete-name", new System.Text.RegularExpressions.Regex("^Public Athlete "));
            await PortraitSystemBrowserTests.AssertDecodedFallbackAsync(image, "PA");
            await Assertions.Expect(image).ToHaveAttributeAsync("alt", "");
        }
    }

    [Fact]
    public async Task FlagSearch_UsesPublicNames_AndSidebarCountsFollowWholeMatchingTeams()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await UseTwoTeamsAsync(context, "Hungary", "Germany");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator("#athleteSearch").FillAsync("Public Flag Winner");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveAttributeAsync("data-flag", "Hungary");
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveTextAsync("1");
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveAttributeAsync("aria-label", "1 flag");
        await Assertions.Expect(page.Locator("#flag-filter-section")).ToHaveAttributeAsync("hidden", "");
        await page.Locator("#athleteSearch").FillAsync("Hungary");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveTextAsync("1");
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task EnteringFlagCompetition_ClearsFlagSelectionAndPreservesTheCohort(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 1000 }, ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?view=pheno&filters=gen%2520x");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar-toggle").ClickAsync();
        var flag = page.Locator("input[name=flag]:enabled").First;
        var selectedFlag = await flag.InputValueAsync();
        await flag.CheckAsync();
        await page.Locator(".sidebar-close").ClickAsync();
        await page.Locator("label[for=group-flags]").ClickAsync();
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync("GEN X PHENO AGE BY FLAG");
        await Assertions.Expect(page.Locator("input[name=flag]:checked")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("input[name=flag]:enabled")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = $"Remove {selectedFlag} filter", Exact = true })).ToHaveCountAsync(0);
        Assert.DoesNotContain(Uri.EscapeDataString(selectedFlag.ToLowerInvariant()), page.Url);
        await Assertions.Expect(page.Locator("input[name=generation]:checked")).ToHaveValueAsync("Gen X");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".collapsed-title")).ToHaveTextAsync("GEN X PHENO AGE BY FLAG");
        var count = await page.Locator(".flag-team").CountAsync();
        Assert.True(count > 1);
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveTextAsync(count.ToString());
        await page.Locator("label[for=group-flags]").ClickAsync();
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#flag-filter-section")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("input[name=flag]:checked")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Hydration_PreservesAnExpandedFlagAndItsKeyboardFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/league/pheno?group=flags", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var summary = page.Locator(".flag-team summary").First;
            await summary.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await page.EvaluateAsync("() => window.flagReviewSummary = document.activeElement");
            release.SetResult();
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === window.flagReviewSummary && window.flagReviewSummary.isConnected"));
            await Assertions.Expect(page.Locator(".flag-team-athletes").First).ToBeVisibleAsync();
            await page.EvaluateAsync("() => document.getElementById('group-flags').dispatchEvent(new Event('change', { bubbles: true }))");
            Assert.True(await page.EvaluateAsync<bool>("() => document.activeElement === window.flagReviewSummary"));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task NonLatinFlags_RemainSeparateAndSearchableThroughReload()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await UseTwoTeamsAsync(context, "日本", "한국");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(2);
        Assert.True(await page.EvaluateAsync<bool>("() => window.LwcFlags.matchesFlagOption({ name: '日本', key: '日本', count: 3 }, '日')"));
        Assert.Contains("<strong>日</strong>本", await page.EvaluateAsync<string>("() => window.LwcFlags.renderFlagOptionLabel('日本', '日')"));
        await page.Locator("#athleteSearch").FillAsync("日本");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveAttributeAsync("data-flag", "日本");
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveAttributeAsync("data-flag", "日本");
    }

    private Task UseTwoTeamsAsync(IBrowserContext context, string first, string second) =>
        RouteAthleteFixtureAsync(context, athletes =>
        {
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["Flag"] = index < 3 ? first : index < 6 ? second : "";
                athlete["Why"] = "";
                athlete["MediaContact"] = "";
                if (index++ == 0) athlete["DisplayName"] = "Public Flag Winner";
            }
        });
}
