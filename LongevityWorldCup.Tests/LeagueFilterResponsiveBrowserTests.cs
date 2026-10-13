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
    [InlineData(320, false)]
    [InlineData(320, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task LeaderboardControls_KeepSearchAndFilterReadableWithEnlargedText(int width, bool dark)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/leaderboard", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet)");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.Locator("#athleteSearch").FillAsync("Michael");
        await Assertions.Expect(page.Locator(".clear-icon")).ToBeVisibleAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const button = document.querySelector('.sidebar-toggle').getBoundingClientRect();
                const icon = document.querySelector('.sidebar-toggle i').getBoundingClientRect();
                if (icon.left < button.left + 1 || icon.right > button.right - 1 || icon.top < button.top + 1 || icon.bottom > button.bottom - 1)
                    problems.push('Filter icon escapes its button');
                const input = document.querySelector('#athleteSearch');
                const style = getComputedStyle(input);
                const canvas = document.createElement('canvas').getContext('2d');
                canvas.font = style.font;
                const textWidth = input.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                // Long queries may scroll; a useful portion must remain readable beside Clear.
                if (textWidth < canvas.measureText('Mich').width)
                    problems.push(`Search has only ${textWidth}px for its text`);
                const clear = document.querySelector('.clear-icon').getBoundingClientRect();
                const field = input.getBoundingClientRect();
                if (clear.left < field.left || clear.right > field.right)
                    problems.push('Clear search escapes the field');
                if (document.documentElement.scrollWidth > innerWidth + 1)
                    problems.push('Page overflows horizontally');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / 200% / {(dark ? "dark" : "light")}: {string.Join(", ", problems)}");

        await page.Locator(".clear-icon").ClickAsync();
        await Assertions.Expect(page.Locator("#athleteSearch")).ToHaveValueAsync("");
        await page.Locator(".sidebar-toggle").ClickAsync();
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "true");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".sidebar-toggle")).ToHaveAttributeAsync("aria-expanded", "false");
    }

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
