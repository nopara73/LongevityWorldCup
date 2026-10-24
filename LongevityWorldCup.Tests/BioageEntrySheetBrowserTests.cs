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

    [Theory]
    [InlineData("/pheno-age", 1440, 900)]
    [InlineData("/bortz-age", 390, 844)]
    [InlineData("/bortz-age", 844, 390)]
    public async Task DomainBand_StaysBelowTheStackedSiteBarsAndTracksTheirLayout(string path, int width, int height)
    {
        var page = await OpenAsync(path, width, height, ColorScheme.Light);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.EvaluateAsync(
            """
            () => {
                const fieldset = document.querySelector('#lwc-step-2 > fieldset:first-of-type');
                window.scrollTo(0, fieldset.getBoundingClientRect().top + scrollY + 60);
            }
            """);
        await Assertions.Expect(page.Locator("#site-sticky-progress")).ToBeVisibleAsync();
        // These routes normally hide the compact site header. Exercise the
        // supported stacked-bar geometry explicitly rather than letting a
        // zero-height header make the top-zero regression pass unnoticed.
        await page.Locator("#site-sticky-header").EvaluateAsync(
            "bar => { bar.style.setProperty('display', 'flex', 'important'); bar.style.setProperty('transform', 'none', 'important'); bar.style.opacity = '1'; bar.style.minHeight = '52px'; window.LwcStickyProgress.refresh(); }");
        var bars = await page.EvaluateAsync<double[]>(
            "() => ['site-sticky-header', 'site-sticky-progress'].map(id => document.getElementById(id).getBoundingClientRect().bottom)");
        Assert.True(bars[0] >= 52 && bars[1] > bars[0] + 30, "The progress row must actually be stacked below the header in this regression test.");
        await AssertBandTopAsync(page, "site-sticky-progress");

        // A font/content reflow can resize a fixed bar without a viewport event.
        await page.Locator("#site-sticky-progress").EvaluateAsync("bar => bar.style.minHeight = '80px'");
        await AssertBandTopAsync(page, "site-sticky-progress");
        await page.Locator("#site-sticky-progress").EvaluateAsync("bar => bar.style.display = 'none'");
        await AssertBandTopAsync(page, "site-sticky-header");
        await page.Locator("#site-sticky-progress").EvaluateAsync("bar => bar.style.removeProperty('display')");
        await AssertBandTopAsync(page, "site-sticky-progress");

        // Offset writes settle instead of causing a style/observer feedback loop.
        var writes = await page.EvaluateAsync<int>(
            """
            async () => {
                let writes = 0;
                const observer = new MutationObserver(records => writes += records.length);
                observer.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
                await new Promise(resolve => setTimeout(resolve, 150));
                observer.disconnect();
                return writes;
            }
            """);
        Assert.Equal(0, writes);
    }

    private static Task AssertBandTopAsync(IPage page, string barId)
        => page.WaitForFunctionAsync(
            """
            id => {
                const band = document.querySelector('#lwc-step-2 > fieldset:first-of-type > legend').getBoundingClientRect();
                const bar = document.getElementById(id).getBoundingClientRect();
                return Math.abs(band.top - bar.bottom) <= 1;
            }
            """, barId);

    [Theory]
    [InlineData("/pheno-age", "pheno")]
    [InlineData("/bortz-age", "bortz")]
    public async Task EverySelectableUnit_PreservesTheEquivalentPanelResult(string path, string clock)
    {
        var page = await OpenAsync(path, 1440, 900, ColorScheme.Light);
        await FillBioageStepOneAsync(page, "2026-09-01");
        if (clock == "bortz") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);

        var problems = await page.EvaluateAsync<string[]>(
            """
            clock => {
                const age = () => clock === 'pheno' ? lastCalculatedPhenoAge : lastCalculatedBortzAge;
                const fields = [...document.querySelectorAll('#lwc-step-2 input[type=number]')].map(input => {
                    const select = document.getElementById(input.id + 'Unit');
                    return { input, select, raw: Number(input.value), index: select.selectedIndex, factor: Number(select.value) };
                });
                calculateResult();
                const expected = age();
                const problems = [];
                if (!Number.isFinite(expected)) return ['The reference panel did not calculate.'];
                for (const field of fields) for (let index = 0; index < field.select.options.length; index++) {
                    if (index === field.index) continue;
                    fields.forEach(original => { original.input.value = String(original.raw); original.select.selectedIndex = original.index; });
                    const option = field.select.options[index];
                    const text = option.textContent.trim();
                    let raw = field.raw / field.factor * Number(option.value);
                    if (['lymphocyte', 'lymphocyte_percentage', 'neutrophil_percentage', 'monocyte_percentage'].includes(field.input.id) && text !== '%') {
                        raw = fields.find(candidate => candidate.input.id === 'wbc').raw * field.raw / 100 * Number(option.value);
                    }
                    if (field.input.id === 'hba1c') {
                        const percent = field.raw / 10.929 + 2.15;
                        if (text.includes('%')) raw = percent;
                        else if (text.includes('eAG')) raw = (percent * 28.7 - 46.7) / (text.includes('mmol/L') ? 18.018 : 1);
                    }
                    field.input.value = String(raw);
                    field.select.selectedIndex = index;
                    field.input.dispatchEvent(new Event('input', { bubbles: true }));
                    field.select.dispatchEvent(new Event('change', { bubbles: true }));
                    calculateResult();
                    if (!Number.isFinite(age()) || Math.abs(age() - expected) > 1e-9)
                        problems.push(`${field.input.id} / ${text}: ${age()} instead of ${expected}`);
                    if (field.select.selectedIndex !== index)
                        problems.push(`${field.input.id} did not retain the selected equivalent unit ${text}`);
                }
                return problems;
            }
            """, clock);
        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
    }

    [Theory]
    [InlineData("/pheno-age", 160, 450)]
    [InlineData("/bortz-age", 160, 450)]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/bortz-age", 390, 844)]
    public async Task ReverseUnitFocus_KeepsTheSelectedControlClearOfItsStickyBand(string path, int width, int height)
    {
        var page = await OpenAsync(path, width, height, ColorScheme.Light);
        await FillBioageStepOneAsync(page, "2026-09-01");
        if (path == "/bortz-age") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);

        // Reverse keyboard navigation must still reach the unit on a phone,
        // where forward Tab intentionally advances directly between values.
        var secondValue = page.Locator("#lwc-step-2 input[type=number]").Nth(1);
        await secondValue.FocusAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(page.Locator("#wbcUnit")).ToBeFocusedAsync();

        var units = await page.Locator("#lwc-step-2 select:visible").EvaluateAllAsync<string[]>(
            "selects => selects.map(select => select.id).reverse()");
        foreach (var id in units)
        {
            await page.Locator($"#{id}").FocusAsync();
            await page.WaitForFunctionAsync(
                """
                id => {
                    const unit = document.getElementById(id);
                    const box = unit.getBoundingClientRect();
                    return document.activeElement === unit
                        && document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2) === unit;
                }
                """, id);
        }
    }

    [Theory]
    [InlineData("/pheno-age", "pheno")]
    [InlineData("/bortz-age", "bortz")]
    public async Task CalculatedPanel_KeepsUnitsReadableWhenMeasurementsAreEdited(string path, string clock)
    {
        var page = await OpenAsync(path, 160, 450, ColorScheme.Light);
        await FillBioageStepOneAsync(page, "2026-09-01");
        if (clock == "bortz") await FillBortzBiomarkersAsync(page);
        else await FillPhenoBiomarkersAsync(page);
        var entryWidth = await page.Locator("form.bioageform").EvaluateAsync<double>("form => form.getBoundingClientRect().width");

        await page.Locator(".bioage-calculate-button").ClickAsync();
        await Assertions.Expect(page.Locator($"#{clock}AgeResult")).ToBeVisibleAsync();
        var initialAge = await page.EvaluateAsync<double>("clock => clock === 'pheno' ? lastCalculatedPhenoAge : lastCalculatedBortzAge", clock);
        await page.Locator("#wbc").FillAsync("7.54");
        await page.Keyboard.PressAsync("Tab");
        await page.WaitForFunctionAsync("({ clock, initialAge }) => (clock === 'pheno' ? lastCalculatedPhenoAge : lastCalculatedBortzAge) !== initialAge && !document.getElementById('continueButton').disabled", new { clock, initialAge });
        await Assertions.Expect(page.Locator($"#{clock}AgeResult")).ToBeVisibleAsync();
        var editedAge = await page.EvaluateAsync<double>("clock => clock === 'pheno' ? lastCalculatedPhenoAge : lastCalculatedBortzAge", clock);
        Assert.NotEqual(initialAge, editedAge);

        var problems = await page.EvaluateAsync<string[]>(
            """
            entryWidth => {
                const issues = [];
                const form = document.querySelector('form.bioageform').getBoundingClientRect();
                if (Math.abs(form.width - entryWidth) > 1) issues.push('The editable panel shrank after calculating.');
                if (form.left < 0 || form.right > innerWidth + 1) issues.push('The editable panel overflows.');
                const ctx = document.createElement('canvas').getContext('2d');
                for (const unit of document.querySelectorAll('#lwc-step-2 select')) {
                    if (!unit.getBoundingClientRect().width) continue;
                    const style = getComputedStyle(unit);
                    ctx.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
                    const available = unit.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                    for (const option of unit.options) {
                        if (ctx.measureText(option.textContent.trim()).width > available + 1)
                            issues.push(`${unit.id}: ${option.textContent.trim()} is clipped after calculating.`);
                    }
                }
                return issues;
            }
            """, entryWidth);
        Assert.True(problems.Length == 0, string.Join(Environment.NewLine, problems));
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
