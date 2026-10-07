using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LeagueFilterResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 200)]
    public async Task LeagueDrawer_KeepsEveryLabelAndCountReadableWithEnlargedText(int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.Locator(".sidebar-toggle").ClickAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var problems = await page.Locator(".filter-section label:visible").EvaluateAllAsync<string[]>(
            """
            labels => labels.flatMap(label => {
                const box = label.getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(label);
                const clipped = [...range.getClientRects()].some(rect => rect.left < box.left - 1 || rect.right > box.right + 1);
                return clipped ? [label.textContent.trim()] : [];
            })
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join(", ", problems)}");
        Assert.True(await page.Locator(".sidebar-filter-list").EvaluateAsync<bool>("list => list.scrollWidth <= list.clientWidth + 1"));

        var pro = page.Locator("input[name='leagueTrack'][value='professional' i]");
        await pro.CheckAsync();
        await Assertions.Expect(pro).ToBeCheckedAsync();
        await Assertions.Expect(page.Locator("label[data-league-track='professional' i]")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("active"));
        await page.Locator("#showLeaderboardResults").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
    }
}
