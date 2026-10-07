using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteStatsResponsiveBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/athlete/michael-lustgarten", 390, 100)]
    [InlineData("/athlete/michael-lustgarten", 375, 200)]
    [InlineData("/athlete/michael-lustgarten", 390, 200)]
    [InlineData("/athlete/michael-lustgarten", 480, 200)]
    [InlineData("/about", 390, 200)]
    public async Task ProfileStats_KeepLabelsValuesAndUnitsInsideTheirRows(string path, int width, int textPercent)
    {
        await using var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/about")
        {
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();
        }
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten' && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var rows = page.Locator(".athlete-info-table tr:not(.athlete-info-divider-row)");
        Assert.True(await rows.CountAsync() > 0);
        var problems = await rows.EvaluateAllAsync<string[]>(
            """
            rows => rows.flatMap(row => {
                const bounds = row.getBoundingClientRect();
                return [...row.querySelectorAll('th, td')].flatMap(cell => {
                    const range = document.createRange();
                    range.selectNodeContents(cell);
                    return [...range.getClientRects()]
                        .filter(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1)
                        .map(() => `${row.querySelector('th')?.textContent.trim()}: clipped text`);
                });
            })
            """);
        Assert.True(problems.Length == 0, $"{path}, {width}px / {textPercent}%: {string.Join(", ", problems)}");
        Assert.Equal(await rows.CountAsync(), await rows.Locator("th[scope='row']").CountAsync());
    }
}
