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
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveTextAsync("3");
        await Assertions.Expect(page.Locator("label[data-flag=Germany] .filter-count")).ToHaveTextAsync("0");
        await page.Locator("#athleteSearch").FillAsync("Hungary");
        await Assertions.Expect(page.Locator(".flag-team")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("label[data-aging-clock-view=pheno] .filter-count")).ToHaveTextAsync("3");
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

    private static Task UseTwoTeamsAsync(IBrowserContext context, string first, string second) =>
        context.RouteAsync("**/api/data/athletes", async route =>
        {
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var index = 0;
            foreach (var athlete in athletes.OfType<JsonObject>())
            {
                athlete["Flag"] = index < 3 ? first : index < 6 ? second : "";
                athlete["Why"] = "";
                athlete["MediaContact"] = "";
                if (index++ == 0) athlete["DisplayName"] = "Public Flag Winner";
            }
            await route.FulfillAsync(new() { ContentType = "application/json", Body = athletes.ToJsonString() });
        });
}
