using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class BioageResultScoreboardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", "#phenoAgeResult", 1440, 900)]
    [InlineData("/bortz-age", "#bortzAgeResult", 1440, 900)]
    [InlineData("/pheno-age", "#phenoAgeResult", 390, 844)]
    [InlineData("/bortz-age", "#bortzAgeResult", 390, 844)]
    [InlineData("/pheno-age", "#phenoAgeResult", 820, 1180)]
    [InlineData("/bortz-age", "#bortzAgeResult", 820, 1180)]
    [InlineData("/pheno-age", "#phenoAgeResult", 1000, 610)]
    [InlineData("/bortz-age", "#bortzAgeResult", 1000, 610)]
    [InlineData("/pheno-age", "#phenoAgeResult", 844, 390)]
    [InlineData("/bortz-age", "#bortzAgeResult", 320, 640)]
    public async Task Result_ReadsAsOneScoreboardAlignedWithTheForm(string path, string resultSelector, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        if (path == "/bortz-age")
            await FillBortzBiomarkersAsync(page);
        else
            await FillPhenoBiomarkersAsync(page);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForFunctionAsync("selector => document.querySelector(selector)?.classList.contains('show')", resultSelector);
        await page.WaitForFunctionAsync("() => /\\d/.test(document.getElementById('yearsText').textContent)");

        var layout = await page.EvaluateAsync<double[]>(
            """
            selector => {
                const result = document.querySelector(selector).getBoundingClientRect();
                const form = document.querySelector('.bioageform').getBoundingClientRect();
                const label = document.querySelector('#validAgeInput > div:first-child').getBoundingClientRect();
                const age = document.getElementById('animatedAge').getBoundingClientRect();
                const years = document.getElementById('yearsText');
                const yearsBox = years.getBoundingClientRect();
                return [result.width, form.width, age.top - label.bottom, yearsBox.top - age.bottom,
                    yearsBox.width, result.width, parseFloat(getComputedStyle(years).fontWeight)];
            }
            """, resultSelector);
        var scenario = $"{path} {width}x{height}";
        Assert.True(Math.Abs(layout[0] - layout[1]) <= 2, $"{scenario}: the result card ({layout[0]}px) should match the form card ({layout[1]}px).");
        Assert.True(layout[2] <= 24, $"{scenario}: the label floats {layout[2]}px above the age.");
        Assert.True(layout[3] <= 24, $"{scenario}: the reduction floats {layout[3]}px below the age.");
        Assert.True(layout[4] < layout[5] * 0.8, $"{scenario}: the reduction should be a compact badge, not a full-width line.");
        Assert.True(layout[6] >= 600, $"{scenario}: the reduction should read as part of the result (weight {layout[6]}).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/pheno-age", 390, 844, true)]
    [InlineData("/bortz-age", 390, 844, true)]
    [InlineData("/pheno-age", 1440, 900, false)]
    [InlineData("/bortz-age", 1440, 900, false)]
    public async Task Result_CompletesItsInitialScrollWhenPendingRankLoads(string path, int width, int height, bool reducedMotion)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync(
            """
            window.__rankGate = new Promise(resolve => window.__releaseRank = resolve);
            window.getSharedAthletes = () => window.__rankGate.then(() => fetch('/api/data/athletes').then(response => response.json()));
            """);
        var page = await context.NewPageAsync();
        await CalculateAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-bioage-result-rank]')?.getAttribute('aria-busy') === 'true'");
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform.show')?.dataset.bioageResultStage === 'rank'");
        await page.WaitForFunctionAsync("() => { const age = document.querySelector('#animatedAge').getBoundingClientRect(); return age.top >= 0 && age.bottom <= innerHeight; }");
        await page.EvaluateAsync("window.__releaseRank()");
        await page.WaitForFunctionAsync("() => document.querySelector('[data-bioage-result-rank]')?.getAttribute('aria-busy') === 'false'");
        await page.WaitForFunctionAsync("() => { const rank = document.querySelector('[data-bioage-result-rank]').getBoundingClientRect(); const dock = document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect(); return rank.bottom <= dock.top - 8; }");
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rank = document.querySelector('[data-bioage-result-rank]').getBoundingClientRect();
                const actions = document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect();
                return [rank.bottom, actions.top, window.scrollY];
            }
            """);
        Assert.True(layout[0] <= layout[1] - 8, $"{path}: loaded rank ends at {layout[0]}px behind actions at {layout[1]}px.");
    }

    [Theory]
    [InlineData("/pheno-age", 320, 200, -12345.6)]
    [InlineData("/bortz-age", 390, 200, 1234567890.1)]
    [InlineData("/pheno-age", 390, 200, 0)]
    [InlineData("/bortz-age", 320, 200, 0)]
    [InlineData("/pheno-age", 1440, 200, 1234567890.1)]
    [InlineData("/bortz-age", 844, 100, -12345.6)]
    public async Task Result_ContainsFiniteScoresReductionAndRankAtEnlargedText(string path, int width, int textPercent, double score)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = width < 600,
            IsMobile = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => window.PhenoAge && window.BortzAge && window.LwcBioageFlow");
        await page.EvaluateAsync(
            """
            ({ path, textPercent, score }) => {
                document.documentElement.style.fontSize = `${textPercent}%`;
                const api = path === '/pheno-age' ? window.PhenoAge : window.BortzAge;
                const method = path === '/pheno-age' ? 'calculatePhenoAge' : 'calculateBortzAge';
                // Presentation fixtures exercise finite extremes and an exactly zero
                // difference without changing the production calculation contract.
                api[method] = values => score === 0 ? values[0] : score;
            }
            """, new { path, textPercent, score });
        await FillAndCalculateAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-bioage-result-rank]')?.getAttribute('aria-busy') === 'false'");
        await page.EvaluateAsync("() => document.fonts.ready");
        var widths = await page.EvaluateAsync<double[]>("() => [document.querySelector('.bioageform.show').getBoundingClientRect().width, document.querySelector('form.bioageform').getBoundingClientRect().width]");
        Assert.InRange(Math.Abs(widths[0] - widths[1]), 0, 2);
        var clipped = await page.EvaluateAsync<string[]>(
            """
            () => {
                const result = document.querySelector('.bioageform.show').getBoundingClientRect();
                return Array.from(document.querySelectorAll('#validAgeInput, #animatedAge, [data-bioage-result-visual], #yearsText, [data-bioage-result-rank], .bioage-rank-number, .bioage-rank-row, .bioage-rank-row > *'))
                    .filter(element => {
                        const rect = element.getBoundingClientRect();
                        return rect.width > 0 && (rect.left < result.left || rect.right > result.right || element.scrollWidth > element.clientWidth + 1);
                    }).map(element => `${element.id || element.className}: ${element.scrollWidth}/${element.clientWidth}`);
            }
            """);
        Assert.Empty(clipped);
        if (score == 0)
            Assert.Equal("years", await page.Locator("#yearsText").TextContentAsync());
        Assert.True(await page.Locator("#continueButton").IsEnabledAsync());
        await page.Locator("[data-bioage-result-rank]").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        var bottom = await page.EvaluateAsync<double[]>(
            """
            () => [document.querySelector('[data-bioage-result-rank]').getBoundingClientRect().bottom,
                document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect().top]
            """);
        Assert.True(bottom[0] <= bottom[1] - 8, $"{path}: the rank panel cannot scroll clear of actions ({bottom[0]} >= {bottom[1]}).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task Result_EnlargedMotionSettlesWithItsLabelAndScoreVisible(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await FillAndCalculateAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform.show')?.dataset.bioageResultStage === 'rank'");
        var layout = await page.EvaluateAsync<double[]>(
            """
            () => [document.querySelector('#validAgeInput > div:first-child').getBoundingClientRect().top,
                parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop),
                document.querySelector('#animatedAge').getBoundingClientRect().bottom,
                document.querySelector('.phenoage-result-actions, .bioage-result-actions').getBoundingClientRect().top]
            """);
        Assert.True(layout[0] >= layout[1] - 1, $"{path}: the result label settled behind the header ({layout[0]} < {layout[1]}).");
        Assert.True(layout[2] <= layout[3] - 8, $"{path}: the result settled behind the actions ({layout[2]} >= {layout[3]}).");
    }

    [Theory]
    [InlineData("/pheno-age", 1440, 900, false)]
    [InlineData("/bortz-age", 1024, 768, true)]
    [InlineData("/pheno-age", 390, 844, true)]
    [InlineData("/bortz-age", 320, 640, false)]
    public async Task Rank_RevealsItsPlaceLikeTheAgeAndContainsNeighbourContent(string path, int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await CalculateAsync(page, path);
        await page.WaitForFunctionAsync("() => document.querySelector('[data-bioage-result-rank]')?.getAttribute('aria-busy') === 'false' && document.querySelector('.bioage-rank-row')");
        await page.EvaluateAsync("() => document.fonts.ready");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const preview = document.querySelector('[data-bioage-result-rank]');
                const box = preview.getBoundingClientRect();
                const style = getComputedStyle(preview);
                const textCenter = element => {
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const rect = range.getBoundingClientRect();
                    return rect.left + rect.width / 2;
                };
                const label = preview.querySelector('.bioage-rank-label');
                const number = preview.querySelector('.bioage-rank-number');
                return [style.backgroundColor === 'rgba(0, 0, 0, 0)' || style.backgroundColor === 'transparent' ? 1 : 0,
                    parseFloat(style.borderLeftWidth), box.left + box.width / 2,
                    textCenter(label), textCenter(number),
                    label.getBoundingClientRect().bottom <= number.getBoundingClientRect().top + 1 ? 1 : 0,
                    getComputedStyle(number).backgroundColor === 'rgba(0, 0, 0, 0)' ? 1 : 0];
            }
            """);
        var scenario = $"{path} {width}x{height}";
        Assert.True(layout[0] == 1 && layout[1] == 0, $"{scenario}: the rank should be part of the result card, not a grey box inside it.");
        Assert.True(layout[5] == 1, $"{scenario}: the rank's label should lead its place, as the age's label leads the age.");
        Assert.True(Math.Abs(layout[3] - layout[2]) <= 2 && Math.Abs(layout[4] - layout[2]) <= 2,
            $"{scenario}: the label and place should be centred like the age ({layout[3]}, {layout[4]} vs {layout[2]}).");
        Assert.True(layout[6] == 1, $"{scenario}: the place should be a headline number, not a tinted chip.");
        await AssertReadableRankRowsAsync(page);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private static async Task CalculateAsync(IPage page, string path)
    {
        await page.GotoAsync(path);
        await FillAndCalculateAsync(page, path);
    }

    private static async Task FillAndCalculateAsync(IPage page, string path)
    {
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        if (path == "/bortz-age") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform.show') && /\\d|years/.test(document.getElementById('yearsText').textContent)");
    }
}
