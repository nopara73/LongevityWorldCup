using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// The calculators read as one instrument: a top band carrying the step track,
// full-width biomarker rows, and a result whose bands meet the card edges.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageInstrumentBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, 900, ColorScheme.Light)]
    [InlineData("/bortz-age", 1024, 768, ColorScheme.Dark)]
    [InlineData("/pheno-age", 390, 844, ColorScheme.Dark)]
    [InlineData("/bortz-age", 320, 640, ColorScheme.Light)]
    public async Task StepTrack_SpansTheCardAndFillsWithEnteredBiomarkers(string path, int width, int height, ColorScheme scheme)
    {
        var page = await OpenAsync(path, width, height, scheme);
        var scenario = $"{path} {width}x{height} {scheme}";

        var band = await MeasureBandAsync(page);
        Assert.True(Math.Abs(band[0] - band[1]) <= 2, $"{scenario}: the step band ({band[0]}px) should span the card ({band[1]}px).");
        Assert.True(band[2] <= 2, $"{scenario}: the step band should start at the card's top edge, not {band[2]}px below it.");
        Assert.True(band[3] >= band[1] * 0.35, $"{scenario}: each step segment should span its half of the band ({band[3]}px).");

        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        Assert.Equal(0, await SegmentFillAsync(page));
        await SetFormValuesAsync(page, new Dictionary<string, string> { ["wbc"] = "6.54", ["mcv"] = "92" });
        var total = await page.Locator("#lwc-step-2 .biomarker-card input[type=number][required]").CountAsync();
        var expected = Math.Round(2.0 / total * 100);
        Assert.InRange(await SegmentFillAsync(page), expected - 1, expected + 1);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/bortz-age", 390, 844)]
    [InlineData("/pheno-age", 1440, 900)]
    [InlineData("/bortz-age", 768, 1024)]
    public async Task BiomarkerRows_AreFullWidthRulesWithoutEmptyCardTail(string path, int width, int height)
    {
        var page = await OpenAsync(path, width, height, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const form = document.querySelector('form.bioageform');
                const box = form.getBoundingClientRect();
                const inner = box.width - parseFloat(getComputedStyle(form).borderLeftWidth) - parseFloat(getComputedStyle(form).borderRightWidth);
                const rows = [...form.querySelectorAll('#lwc-step-2 .biomarker-card')];
                const widest = Math.min(...rows.map(row => row.getBoundingClientRect().width));
                const sideBorders = Math.max(...rows.map(row => parseFloat(getComputedStyle(row).borderLeftWidth) + parseFloat(getComputedStyle(row).borderRightWidth)));
                const actions = document.getElementById('lwcStepTwoActions');
                const end = form.contains(actions) ? actions.getBoundingClientRect().bottom : rows.at(-1).getBoundingClientRect().bottom;
                return [widest, inner, sideBorders, box.bottom - end];
            }
            """);
        var scenario = $"{path} {width}x{height}";
        Assert.True(Math.Abs(layout[0] - layout[1]) <= 1, $"{scenario}: biomarker rows ({layout[0]}px) should run edge to edge of the card ({layout[1]}px).");
        Assert.Equal(0, layout[2]);
        Assert.True(layout[3] <= 40, $"{scenario}: the card ends {layout[3]}px below its last row or action band.");
    }

    [Fact]
    public async Task InvalidBiomarker_TintsItsWholeRow()
    {
        var page = await OpenAsync("/pheno-age", 390, 844, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        var wbc = page.Locator("#wbc");
        await wbc.FillAsync("6.54");
        await wbc.FillAsync("");
        await page.Locator("#mcv").FocusAsync();
        await Assertions.Expect(wbc).ToHaveAttributeAsync("aria-invalid", "true");

        var backgrounds = await page.EvaluateAsync<string[]>(
            "() => ['#wbc', '#mcv'].map(id => getComputedStyle(document.querySelector(id).closest('.biomarker-card')).backgroundColor)");
        Assert.NotEqual("rgba(0, 0, 0, 0)", backgrounds[0]);
        Assert.NotEqual(backgrounds[0], backgrounds[1]);
    }

    [Theory]
    [InlineData("/pheno-age", "#phenoAgeResult", 1440, 900)]
    [InlineData("/bortz-age", "#bortzAgeResult", 390, 844)]
    [InlineData("/pheno-age", "#phenoAgeResult", 320, 640)]
    public async Task Result_BandsMeetTheCardEdgesAndMarksUseTheCompetitionFace(string path, string resultSelector, int width, int height)
    {
        var page = await OpenAsync(path, width, height, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        if (path == "/bortz-age") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('[data-bioage-result-rank]')?.getAttribute('aria-busy') === 'false'");

        var layout = await page.EvaluateAsync<double[]>(
            """
            selector => {
                const card = document.querySelector(selector);
                const box = card.getBoundingClientRect();
                const style = getComputedStyle(card);
                const inner = box.width - parseFloat(style.borderLeftWidth) - parseFloat(style.borderRightWidth);
                const label = document.querySelector('#validAgeInput > div:first-child').getBoundingClientRect();
                const rankLabel = card.querySelector('.bioage-rank-label').getBoundingClientRect();
                const rows = [...card.querySelectorAll('.bioage-rank-row')].map(row => row.getBoundingClientRect().width);
                const tone = parseFloat(style.borderTopWidth);
                return [inner, label.width, label.top - box.top - tone, rankLabel.width, Math.min(...rows), tone];
            }
            """, resultSelector);
        var scenario = $"{path} {width}x{height}";
        Assert.True(Math.Abs(layout[1] - layout[0]) <= 1, $"{scenario}: the clock band ({layout[1]}px) should span the card ({layout[0]}px).");
        Assert.True(layout[2] <= 1, $"{scenario}: the clock band should sit on the card's top edge, not {layout[2]}px below it.");
        Assert.True(Math.Abs(layout[3] - layout[0]) <= 1, $"{scenario}: the rank band ({layout[3]}px) should span the card ({layout[0]}px).");
        Assert.True(Math.Abs(layout[4] - layout[0]) <= 1, $"{scenario}: neighbour rows ({layout[4]}px) should span the card ({layout[0]}px).");
        Assert.True(layout[5] >= 2, $"{scenario}: the result should carry its tone along the top edge.");

        var fonts = await page.EvaluateAsync<string[]>(
            "() => ['#animatedAge', '.bioage-rank-number'].map(selector => getComputedStyle(document.querySelector(selector)).fontFamily)");
        Assert.All(fonts, font => Assert.StartsWith("Orbitron", font));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private readonly List<IBrowserContext> _contexts = [];

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task EnlargedDateFields_KeepTheFullSelectedMonthReadable(string path)
    {
        var page = await OpenAsync(path, 1440, 900, ColorScheme.Light);
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.Locator("#dob-month").SelectOptionAsync("12");
        await page.EvaluateAsync("() => document.fonts.ready");

        var widths = await page.Locator("#dob-month").EvaluateAsync<double[]>(
            """
            select => {
                const style = getComputedStyle(select);
                const canvas = document.createElement('canvas');
                const context = canvas.getContext('2d');
                context.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                const longestMonth = Math.max(...Array.from(select.options,
                    option => context.measureText(option.textContent).width));
                // Native select text is not a DOM text node and can be clipped
                // even when the select itself has no horizontal overflow.
                const available = select.clientWidth
                    - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                return [available, longestMonth];
            }
            """);

        Assert.True(widths[0] >= widths[1],
            $"{path}: the month needs {widths[1]}px but its select leaves only {widths[0]}px for text.");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    public override async Task DisposeAsync()
    {
        foreach (var context in _contexts) await context.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<IPage> OpenAsync(string path, int width, int height, ColorScheme scheme)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = scheme,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        _contexts.Add(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => document.querySelector('.bioage-biomarker-entry-ready')");
        return page;
    }

    private static Task<double[]> MeasureBandAsync(IPage page) => page.EvaluateAsync<double[]>(
        """
        () => {
            const form = document.querySelector('form.bioageform');
            const box = form.getBoundingClientRect();
            const inner = box.width - parseFloat(getComputedStyle(form).borderLeftWidth) - parseFloat(getComputedStyle(form).borderRightWidth);
            const band = form.querySelector('.lwc-wizard-nav').getBoundingClientRect();
            const segment = parseFloat(getComputedStyle(document.getElementById('lwcDot2'), '::before').width);
            return [band.width, inner, band.top - box.top - parseFloat(getComputedStyle(form).borderTopWidth), segment];
        }
        """);

    private static async Task<double> SegmentFillAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => document.getElementById('lwcDot2').classList.contains('lwc-dot--on')");
        return await page.EvaluateAsync<double>(
            """
            () => {
                const size = getComputedStyle(document.getElementById('lwcDot2'), '::before').backgroundSize;
                return parseFloat(size) || 0;
            }
            """);
    }
}
