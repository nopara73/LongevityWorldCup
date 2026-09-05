using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LeaderboardSelectionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
        var results = page.Locator("#showLeaderboardResults");
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
        Assert.Equal("Show " + await page.Locator("#leaderboardResultCount").InnerTextAsync(), await results.InnerTextAsync());
        await page.Locator("#clearSidebarFiltersBtn").ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("data-filter-count", "0");
        Assert.Equal("Show " + await page.Locator("#leaderboardResultCount").InnerTextAsync(), await results.InnerTextAsync());
        await results.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth"));
    }

    private static ILocator Rows(IPage page) => page.Locator(".leaderboard tbody tr[data-athlete-name]:visible");

    private static async Task OpenFilteredAsync(IPage page)
    {
        await page.GotoAsync("/leaderboard?filters=hungary,women%27s&view=pheno");
        await Assertions.Expect(page.Locator(".leaderboard-selection-chip")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("#leaderboardResultCount")).ToHaveTextAsync($"{await Rows(page).CountAsync()} athletes");
    }
}
