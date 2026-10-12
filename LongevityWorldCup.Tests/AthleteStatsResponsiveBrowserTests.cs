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

    [Theory]
    [InlineData("/athlete/michael-lustgarten", 320, false, 100)]
    [InlineData("/athlete/michael-lustgarten", 375, true, 100)]
    [InlineData("/athlete/michael-lustgarten", 320, false, 200)]
    [InlineData("/athlete/michael-lustgarten", 375, true, 200)]
    [InlineData("/athlete/michael-lustgarten", 390, false, 200)]
    [InlineData("/athlete/michael-lustgarten", 480, true, 200)]
    [InlineData("/about", 320, true, 200)]
    [InlineData("/about", 375, false, 200)]
    public async Task ProfileScoresAndRanks_ReflowWithEnlargedText(string path, int width, bool dark, int textPercent)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (path == "/about")
            await page.Locator("main.documentation-page a[href='/athlete/michael-lustgarten']").First.ClickAsync();

        await page.WaitForFunctionAsync("""
            () => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === 'michael-lustgarten'
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
                && document.querySelector('#ageRadarCenter')?.dataset.diffSign === 'negative'
                && typeof document.querySelector('#detailsModal .modal-content')._scrollProgressListener === 'function'
                && [...document.querySelectorAll('link[rel="stylesheet"]')].every(link => !!link.sheet)
            """);
        // External chart scripts are stubbed in this fixture; expose the real summary
        // so the same values and styles are exercised as in a rendered radar chart.
        await page.EvaluateAsync("""
            () => {
                document.getElementById('ageRadarFallback').style.display = 'none';
                document.getElementById('ageRadarCenter').style.display = 'flex';
            }
            """);
        // A three-digit placement must remain readable as the competition grows.
        await page.Locator("#athleteRankings strong").First.EvaluateAsync("element => element.textContent = '#199'");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.WaitForFunctionAsync("percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100", textPercent);
        await page.Locator("#detailsModal .modal-content").EvaluateAsync("element => element.scrollTop = element.scrollHeight");
        await Assertions.Expect(page.Locator("#modalStickyHeader")).ToBeVisibleAsync();
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);

        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                for (const selector of ['#athleteName', '#athleteRankings a', '.age-radar-summary', '.proofs-heading']) {
                    for (const element of document.querySelectorAll(selector)) {
                        if (element.getBoundingClientRect().height === 0) continue;
                        const bounds = element.getBoundingClientRect();
                        const range = document.createRange();
                        range.selectNodeContents(element);
                        if ([...range.getClientRects()].some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                            failures.push(`${selector}: text leaves its card`);
                        if (element.scrollWidth > element.clientWidth + 1)
                            failures.push(`${selector}: horizontal overflow`);
                    }
                }
                const score = document.querySelector('#ageRadarCenterValue').firstChild;
                const number = document.createRange();
                number.setStart(score, 0);
                number.setEnd(score, score.textContent.indexOf(' '));
                const numberLines = [...number.getClientRects()];
                if (numberLines.some(rect => Math.abs(rect.top - numberLines[0].top) > 1))
                    failures.push('age score: numeric value wraps');
                const modal = document.querySelector('#detailsModal .modal-content');
                if (modal.scrollWidth > modal.clientWidth + 1)
                    failures.push('dialog: horizontal overflow after scrolling');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{path}, {width}px, dark={dark}, {textPercent}% text: {string.Join(", ", problems)}");
    }
}
