using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// Entering a blood panel reads as one paced sheet: every section opens with a
// band, each domain band tallies its biomarkers, and a value with its unit is
// one field.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageEntrySheetBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private readonly List<IBrowserContext> _contexts = [];

    [Theory]
    [InlineData("/pheno-age", 1440, 900, ColorScheme.Light)]
    [InlineData("/bortz-age", 390, 844, ColorScheme.Dark)]
    public async Task EverySection_OpensWithAFullBleedBand(string path, int width, int height, ColorScheme scheme)
    {
        var page = await OpenAsync(path, width, height, scheme);
        var stepOne = await MeasureBandsAsync(page, "#lwc-step-1");
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        var stepTwo = await MeasureBandsAsync(page, "#lwc-step-2");

        Assert.Equal(2, stepOne.Length);
        Assert.True(stepTwo.Length >= 5, $"{path}: expected a band per domain, saw {stepTwo.Length}.");
        Assert.All(stepOne.Concat(stepTwo), band => Assert.NotEqual("rgba(0, 0, 0, 0)", band.Background));
        Assert.All(stepTwo, band =>
        {
            Assert.InRange(band.Left, -1, 1);
            Assert.InRange(band.Right, -1, 1);
        });

        Assert.InRange(stepOne[0].Left, -1, 1);
        Assert.InRange(stepOne[1].Right, -1, 1);
        if (width >= 960)
        {
            // On a desktop the two date sections share one band row across the card.
            Assert.InRange(stepOne[1].Top - stepOne[0].Top, -1, 1);
            Assert.InRange(stepOne[1].AbsoluteLeft - stepOne[0].AbsoluteRight, -1, 1);
        }
        else
        {
            // On a phone the draw date follows the birth date like the domains do.
            Assert.True(stepOne[1].Top > stepOne[0].Top + 40, $"{path}: the draw date band shares the birth date row.");
            Assert.InRange(stepOne[0].Right, -1, 1);
            Assert.InRange(stepOne[1].Left, -1, 1);
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task DomainBand_TalliesItsBiomarkersAndMarksAGap()
    {
        var page = await OpenAsync("/pheno-age", 390, 844, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));

        var pips = page.Locator("#lwc-step-2 > fieldset:first-of-type .bioage-domain-pip");
        await Assertions.Expect(pips).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator(".bioage-domain-tally").First).ToHaveAttributeAsync("aria-hidden", "true");

        await page.Locator("#wbc").FillAsync("6.54");
        await Assertions.Expect(pips.Nth(0)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bioage-domain-pip--complete"));

        await page.Locator("#lymphocyte").FillAsync("28");
        await page.Locator("#lymphocyte").FillAsync("");
        await page.Locator("#mcv").FocusAsync();
        await Assertions.Expect(pips.Nth(1)).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bioage-domain-pip--invalid"));

        await page.Locator("#lymphocyte").FillAsync("28");
        await page.Locator("#mcv").FillAsync("92");
        await page.Locator("#rcdw").FillAsync("13.4");
        await Assertions.Expect(page.Locator("#lwc-step-2 > fieldset:first-of-type"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex("bioage-domain--complete"));
    }

    [Theory]
    [InlineData("/pheno-age", 1440, 900, ColorScheme.Dark)]
    [InlineData("/bortz-age", 390, 844, ColorScheme.Light)]
    public async Task ValueAndUnit_AreOneFieldWithAlignedUnitCaps(string path, int width, int height, ColorScheme scheme)
    {
        var page = await OpenAsync(path, width, height, scheme);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));

        var groups = await page.EvaluateAsync<double[][]>(
            """
            () => [...document.querySelectorAll('#lwc-step-2 .biomarker-card .input-group')].map(group => {
                const input = group.querySelector('input');
                const unit = [...group.children].find(child => child !== input && getComputedStyle(child).display !== 'none' && !child.classList.contains('visually-hidden'));
                const box = group.getBoundingClientRect();
                const unitBox = unit.getBoundingClientRect();
                return [
                    parseFloat(getComputedStyle(group).borderTopWidth),
                    parseFloat(getComputedStyle(input).borderTopWidth),
                    unitBox.top - box.top,
                    box.right - unitBox.right,
                    unitBox.width,
                    unitBox.left
                ];
            })
            """);

        Assert.NotEmpty(groups);
        Assert.All(groups, group =>
        {
            Assert.True(group[0] >= 1, "The measurement field draws one outer border.");
            Assert.Equal(0, group[1]);
            Assert.InRange(group[2], 0, 2);
            Assert.InRange(group[3], 0, 2);
        });
        Assert.InRange(groups.Max(group => group[4]) - groups.Min(group => group[4]), 0, 1);
        Assert.InRange(groups.Max(group => group[5]) - groups.Min(group => group[5]), 0, 1);
    }

    [Fact]
    public async Task DomainBand_StaysInViewBelowTheSiteBarsOnAPhone()
    {
        var page = await OpenAsync("/bortz-age", 390, 844, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.Locator("#mch").FocusAsync();
        await page.EvaluateAsync("() => window.scrollBy(0, 300)");
        await page.WaitForTimeoutAsync(400);

        var result = await page.EvaluateAsync<double[]>(
            """
            () => {
                const band = document.querySelector('#lwc-step-2 > fieldset:first-of-type > legend').getBoundingClientRect();
                const bars = ['site-sticky-header', 'site-sticky-progress']
                    .map(id => document.getElementById(id))
                    .filter(bar => bar && getComputedStyle(bar).position === 'fixed')
                    .map(bar => bar.getBoundingClientRect())
                    .filter(box => box.height > 0 && box.top <= 1);
                const barBottom = Math.max(0, ...bars.map(box => box.bottom));
                const fieldset = document.querySelector('#lwc-step-2 > fieldset:first-of-type').getBoundingClientRect();
                return [band.top, barBottom, fieldset.top];
            }
            """);

        Assert.True(result[2] < result[1], "The first domain should have scrolled under the site bars.");
        Assert.InRange(result[0], result[1] - 1, result[1] + 1);
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

    private sealed record Band(double Left, double Right, double Top, string Background, double AbsoluteLeft, double AbsoluteRight);

    private static async Task<Band[]> MeasureBandsAsync(IPage page, string step)
    {
        var rows = await page.EvaluateAsync<string[][]>(
            """
            step => {
                const form = document.querySelector('form.bioageform');
                const box = form.getBoundingClientRect();
                const style = getComputedStyle(form);
                const left = box.left + parseFloat(style.borderLeftWidth);
                const right = box.right - parseFloat(style.borderRightWidth);
                return [...document.querySelectorAll(`${step} > fieldset > legend`)].map(legend => {
                    const band = legend.getBoundingClientRect();
                    return [String(band.left - left), String(right - band.right), String(band.top), getComputedStyle(legend).backgroundColor, String(band.left), String(band.right)];
                });
            }
            """,
            step);
        return rows.Select(row => new Band(
            double.Parse(row[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(row[2], System.Globalization.CultureInfo.InvariantCulture),
            row[3],
            double.Parse(row[4], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(row[5], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
    }
}
