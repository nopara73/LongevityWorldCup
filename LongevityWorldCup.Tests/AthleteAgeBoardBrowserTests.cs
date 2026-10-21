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

    [Theory]
    [InlineData("benjamin-garden")]
    [InlineData("michael-lustgarten")]
    public async Task ServerAndClient_KeepTheSameClockValuesPacesAndGlobalPaceRanks(string slug)
    {
        await using var staticContext = await NewContextAsync(390, true, false);
        var initial = await OpenAsync(staticContext, slug, false);
        await using var context = await NewContextAsync(390, true, true);
        var hydrated = await OpenAsync(context, slug, true);
        const string facts = """
            () => [...document.querySelectorAll('.athlete-age-board > tr')].filter(tr => tr.checkVisibility())
                .map(tr => `${tr.id}: ${tr.textContent.trim().replace(/\s+/g, ' ')}`)
            """;
        var before = await initial.EvaluateAsync<string[]>(facts);
        Assert.Contains(before, text => text.StartsWith("paceOfAgingContainer:"));
        if (slug == "benjamin-garden") Assert.Contains(before, text => text.StartsWith("bortzPaceOfAgingContainer:"));
        Assert.Equal(before, await hydrated.EvaluateAsync<string[]>(facts));
        Assert.Equal(before.Length, await hydrated.GetByRole(AriaRole.Rowgroup).Filter(new() { Has = hydrated.Locator("#chronologicalAgeContainer") }).GetByRole(AriaRole.Rowheader).CountAsync());
    }

    [Theory]
    [InlineData(1440, 100)]
    [InlineData(390, 100)]
    [InlineData(320, 200)]
    public async Task Rerender_PreservesClockPairsHiddenReferencesAndWrappedSavedGuesses(int width, int textPercent)
    {
        await using var context = await NewContextAsync(width, true, true);
        var page = await OpenAsync(context, "benjamin-garden", true);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        var problems = await page.EvaluateAsync<string[]>("""
            async () => {
                const failures = [], api = window.ageBoardTest;
                const full = (await window.getSharedAthletes()).find(a => a.Name === 'Benjamin Garden');
                const original = api.results().find(a => a.name === full.Name);
                const board = document.querySelector('.athlete-age-board');
                const box = id => document.getElementById(id).getBoundingClientRect();
                const settle = () => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const checkText = state => {
                    for (const tile of board.children) {
                        if (!tile.checkVisibility()) continue;
                        const bounds = tile.getBoundingClientRect(), walker = document.createTreeWalker(tile, NodeFilter.SHOW_TEXT);
                        while (walker.nextNode()) {
                            if (!walker.currentNode.textContent.trim()) continue;
                            const range = document.createRange(); range.selectNodeContents(walker.currentNode);
                            for (const rect of range.getClientRects())
                                if (rect.width && (rect.left < bounds.left - 1 || rect.right > bounds.right + 1)) failures.push(`${state}: ${tile.id} text escapes`);
                        }
                    }
                };
                for (const clock of ['both', 'bortz', 'pheno']) {
                    const data = { ...original };
                    if (clock === 'bortz') Object.assign(data, { lowestPhenoAge: null, ageReduction: null, ageReductionPercent: null, chronoAtLowestPhenoAge: null });
                    if (clock === 'pheno') Object.assign(data, { lowestBortzAge: null, bortzAgeReduction: null, bortzAgeReductionPercent: null, chronoAtLowestBortzAge: null });
                    window.LwcGuessState.get = () => null;
                    api.populateModal(full, data, api.computeBestLeagueRank(data));
                    api.updateCrowdContainer(null, 0);
                    await settle();
                    for (const [name, id] of [['bortz', 'lowestBortzAgeContainer'], ['pheno', 'lowestPhenoAgeContainer']])
                        if (document.getElementById(id).checkVisibility() !== (clock === 'both' || clock === name)) failures.push(`${clock}: wrong ${name} visibility`);
                    checkText(clock);
                    if (clock !== 'both') {
                        const tiles = [...board.querySelectorAll('tr[data-clock]')].filter(tile => tile.checkVisibility());
                        const count = tiles.filter(tile => Math.abs(tile.getBoundingClientRect().top - tiles[0].getBoundingClientRect().top) < 1).length;
                        const expected = innerWidth > 1000 ? 3 : innerWidth > 320 ? 2 : 1;
                        if (count !== expected) failures.push(`${clock}: ${count} tiles in first row instead of ${expected}`);
                    }
                    if (clock === 'both') {
                        data.chronoAtLowestBortzAge = null;
                        api.populateModal(full, data, api.computeBestLeagueRank(data));
                        await settle();
                        if (document.getElementById('bortzPaceOfAgingContainer').checkVisibility()) failures.push('missing pace remains visible');
                        const left = box('bortzAgeReductionContainer'), right = box('ageReductionContainer');
                        if (innerWidth > 320 && Math.abs(left.top - right.top) > 1) failures.push('missing pace mispairs the reduction row');
                    }
                }
                api.populateModal(full, original, api.computeBestLeagueRank(original));
                for (const crowd of [false, true]) for (const guess of [null, {value:50,first:true}, {value:51,first:true}, {value:150,first:true}]) {
                    api.updateCrowdContainer(crowd ? 60 : null, crowd ? 123456 : 0);
                    window.LwcGuessState.get = () => guess;
                    window.updateYourGuess();
                    // Other inline styles must not affect layout visibility.
                    document.getElementById('chronologicalAgeContainer').style.userSelect = 'none';
                    await settle(); checkText(`crowd=${crowd}, guess=${guess?.value}`);
                    const firstClock = box('lowestBortzAgeContainer');
                    for (const row of board.querySelectorAll('tr:not([data-clock])'))
                        if (row.checkVisibility() && row.getBoundingClientRect().bottom > firstClock.top + 1) failures.push('reference follows a clock tile');
                }
                api.updateCrowdContainer(null, 0); window.LwcGuessState.get = () => null; window.updateYourGuess();
                document.getElementById('chronologicalAgeContainer').hidden = true;
                await settle();
                if (Math.abs(box('lowestBortzAgeContainer').top - board.getBoundingClientRect().top) > 1) failures.push('hidden references leave an empty row');
                document.getElementById('chronologicalAgeContainer').hidden = false;
                for (const id of ['lowestBortzAge', 'lowestPhenoAge', 'crowdCount']) document.getElementById(id).textContent = '12345678901234567890.1';
                await settle(); checkText('long numeric stress fixture');
                return failures;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join("; ", problems)}");
    }

    private async Task<IBrowserContext> NewContextAsync(int width, bool dark, bool javascript)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            JavaScriptEnabled = javascript
        });
        if (javascript) await context.RouteAsync("**/js/leaderboard-page.js*", async route =>
        {
            var response = await route.FetchAsync();
            var source = await response.TextAsync();
            var end = source.LastIndexOf("})();", StringComparison.Ordinal);
            Assert.True(end > 0);
            await route.FulfillAsync(new() { Response = response, Body = source.Insert(end,
                "window.ageBoardTest = {populateModal, computeBestLeagueRank, updateCrowdContainer, results: () => athleteResults};\n") });
        });
        return context;
    }

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
        {
            // Without script, frames never run, so poll on a timer and skip the frame settle.
            await page.WaitForFunctionAsync("() => [...document.querySelectorAll('link[rel=\"stylesheet\"]')].every(link => !!link.sheet)",
                null, new() { PollingInterval = 100, Timeout = 30000 });
            await page.EvaluateAsync("() => document.fonts?.ready");
            return page;
        }
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return page;
    }
}
