using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// The age results in the profile dialog read as a scoreboard: the reference
// ages first, each clock in its own column, figures larger than the basics.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteAgeBoardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, true, true)]
    [InlineData(390, false, true)]
    [InlineData(390, true, false)]
    public async Task TwoClocks_PairBortzAndPhenoColumnsAboveTheBasics(int width, bool dark, bool javascript)
    {
        await using var context = await NewContextAsync(width, dark, javascript);
        var page = await OpenAsync(context, "benjamin-garden", javascript);
        var problems = await page.EvaluateAsync<string[]>("""
            () => {
                const failures = [];
                const box = id => document.getElementById(id).getBoundingClientRect();
                const pairs = [['lowestBortzAgeContainer', 'lowestPhenoAgeContainer'],
                    ['bortzAgeReductionContainer', 'ageReductionContainer']];
                for (const [bortz, pheno] of pairs) {
                    const left = box(bortz), right = box(pheno);
                    if (Math.abs(left.top - right.top) > 1) failures.push(`${bortz} and ${pheno} are not one row`);
                    if (!(left.right <= right.left + 1)) failures.push(`${bortz} is not left of ${pheno}`);
                }
                if (!(box('chronologicalAgeContainer').bottom <= box('lowestBortzAgeContainer').top + 1))
                    failures.push('chronological age does not lead the clocks');
                const basics = document.getElementById('athleteDivision').closest('tr').getBoundingClientRect();
                if (!(box('ageReductionContainer').bottom <= basics.top + 1)) failures.push('age results do not come before the basics');
                const size = element => parseFloat(getComputedStyle(element).fontSize);
                const basic = size(document.getElementById('athleteDivision').closest('td'));
                for (const id of ['lowestBortzAge', 'lowestPhenoAge', 'chronologicalAge', 'ageReduction'])
                    if (!(size(document.getElementById(id)) >= basic * 1.4)) failures.push(`${id} figure is not set above the basics`);
                const bortz = getComputedStyle(document.getElementById('lowestBortzAge')).color;
                const pheno = getComputedStyle(document.getElementById('lowestPhenoAge')).color;
                if (bortz === pheno) failures.push('lowest ages do not carry their clock colours');
                for (const th of document.querySelectorAll('.athlete-age-board th'))
                    if (th.offsetParent && th.textContent.trim().endsWith(':')) failures.push(`${th.textContent.trim()} keeps a row colon`);
                const modal = document.querySelector('#detailsModal .modal-content');
                if (modal.scrollWidth > modal.clientWidth + 1) failures.push('dialog overflows horizontally');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px, js={javascript}: {string.Join(", ", problems)}");
    }

    [Theory]
    [InlineData(1440, 3)]
    [InlineData(390, 2)]
    public async Task OneClock_SetsAgePaceAndReductionAsTilesOnOneGrid(int width, int perRow)
    {
        await using var context = await NewContextAsync(width, true, true);
        var page = await OpenAsync(context, "michael-lustgarten", true);
        var problems = await page.EvaluateAsync<string[]>("""
            perRow => {
                const failures = [];
                const ids = ['lowestPhenoAgeContainer', 'paceOfAgingContainer', 'ageReductionContainer'];
                const tops = ids.map(id => Math.round(document.getElementById(id).getBoundingClientRect().top));
                const firstRow = tops.filter(top => Math.abs(top - tops[0]) <= 1).length;
                if (firstRow !== perRow) failures.push(`${firstRow} clock tiles share the first row instead of ${perRow}`);
                const board = document.querySelector('.athlete-age-board').getBoundingClientRect();
                for (const tile of document.querySelectorAll('.athlete-age-board > tr')) {
                    if (!tile.offsetParent) continue;
                    const bounds = tile.getBoundingClientRect();
                    if (bounds.left < board.left - 1 || bounds.right > board.right + 1) failures.push(`${tile.id} leaves the board`);
                    const range = document.createRange();
                    range.selectNodeContents(tile);
                    if ([...range.getClientRects()].some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                        failures.push(`${tile.id} text leaves its tile`);
                }
                const last = document.getElementById('ageReductionContainer').getBoundingClientRect();
                if (perRow === 2 && Math.abs(last.width - board.width) > 2) failures.push('the lone reduction tile does not take the row');
                return failures;
            }
            """, perRow);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join(", ", problems)}");
    }

    private Task<IBrowserContext> NewContextAsync(int width, bool dark, bool javascript) =>
        AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            JavaScriptEnabled = javascript
        });

    private static async Task<IPage> OpenAsync(IBrowserContext context, string slug, bool javascript)
    {
        if (javascript)
            await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/athlete/{slug}", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (javascript)
            await page.WaitForFunctionAsync("""
                slug => document.querySelector('#detailsModal .modal-content')?.dataset.athleteSlug === slug
                    && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
                    && [...document.querySelectorAll('link[rel="stylesheet"]')].every(link => !!link.sheet)
                """, slug);
        else
            await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=\"stylesheet\"]')].every(link => !!link.sheet)");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return page;
    }
}
