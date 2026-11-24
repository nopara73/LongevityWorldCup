using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class BioageComparisonBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IPage> OpenAsync(IBrowserContext context, string clock)
    {
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name:'Comparison Athlete', DateOfBirth:{Year:1980,Month:5,Day:20},
                Biomarkers:[{Date:'2026-06-01',AlbGL:45,AlpUL:83,AltUL:22,ApoA1GL:1.52,
                    CholesterolMmolL:5.6,CreatUmolL:72,CrpMgL:1.35,CystatinCMgL:0.9,GluMmolL:5.1,
                    GgtUL:29,Hba1cMmolMol:35.5,LymPc:28.6,MchPg:31.8,McvFL:92,MonocytePc:7.2,
                    NeutrophilPc:64.2,Rbc10e12L:4.5,RdwPc:13.4,ShbgNmolL:45.6,UreaMmolL:5.4,
                    VitaminDNmolL:50,Wbc1000cellsuL:6.54}]
            }));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age?update=1");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");
        return page;
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task ComparisonTyping_KeepsNodesAndAvoidsPresentationLayoutReads(string clock)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await OpenAsync(context, clock);
        await page.Locator("#glucose").FillAsync("90");
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.comparisonReads = 0;
                const width = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth');
                Object.defineProperty(HTMLElement.prototype, 'offsetWidth', {...width, get() {
                    if (this.matches('.bioage-input-comparison-chip,.bioage-biomarker-progress')) window.comparisonReads++;
                    return width.get.call(this);
                }});
                const other = document.querySelector('[data-bioage-comparison-for="glucose"]');
                window.otherComparisonNodes = [...other.childNodes];
            }
            """);
        await page.Locator("#wbc").PressSequentiallyAsync("5,9");
        await WaitForFiniteAnimationsAsync(page);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.comparisonReads"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const chip = document.querySelector('[data-bioage-comparison-for="wbc"]');
                const children = [...chip.childNodes];
                document.querySelector('#wbc').dispatchEvent(new Event('input', {bubbles:true}));
                return children.every((child, i) => child === chip.childNodes[i])
                    && window.otherComparisonNodes.every((child, i) => child === document.querySelector('[data-bioage-comparison-for="glucose"]').childNodes[i]);
            }
            """));
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='wbc'] .bioage-input-comparison-chip__previous")).ToHaveTextAsync("6.54");
        await page.Locator("#wbc").FillAsync("-5,9");
        // Deltas of ten or more retain the existing one-decimal display rule.
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='wbc'] .bioage-input-comparison-chip__text")).ToHaveTextAsync("12.4 lower");
        await page.Locator("#wbc").FillAsync("");
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='wbc']")).ToBeHiddenAsync();
        await page.Locator("#wbc").FillAsync("6.54");
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='wbc']")).ToHaveAttributeAsync("aria-label", "same as last; last 6.54");
        await page.Locator("#glucoseUnit").SelectOptionAsync("18.016");
        await page.Locator("#glucose").FillAsync("90,08");
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='glucose'] .bioage-input-comparison-chip__previous")).ToHaveTextAsync("91.88");
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='glucose'] .bioage-input-comparison-chip__text")).ToHaveTextAsync("1.8 lower");
        await page.Locator("#glucoseUnit").SelectOptionAsync("1");
        await page.Locator("#glucose").FillAsync("5");
        await Assertions.Expect(page.Locator("[data-bioage-comparison-for='glucose']")).ToHaveAttributeAsync("aria-label", "0.1 lower; last 5.1");
    }

    [Theory]
    [InlineData("pheno", 320, "light")]
    [InlineData("pheno", 1440, "dark")]
    [InlineData("bortz", 320, "dark")]
    [InlineData("bortz", 1440, "light")]
    public async Task Comparisons_FitBelowIntactMeasurementsWithEnlargedText(string clock, int width, string theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = theme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await OpenAsync(context, clock);
        await page.EvaluateAsync("""
            () => {
                document.documentElement.style.fontSize = '200%';
                for (const input of document.querySelectorAll('.biomarker-card input[type="text"]')) {
                    input.value = input.placeholder;
                    input.dispatchEvent(new Event('input', {bubbles:true}));
                }
            }
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        Assert.Equal(clock == "pheno" ? 9 : 22, await page.Locator(".bioage-input-comparison-chip:visible").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('.bioage-input-comparison-chip:not([hidden])')].every(chip => {
                const input = document.getElementById(chip.dataset.bioageComparisonFor);
                const group = input.closest('.input-group');
                const row = input.closest('.biomarker-card');
                const cr = chip.getBoundingClientRect(), gr = group.getBoundingClientRect(), rr = row.getBoundingClientRect();
                return !group.contains(chip) && getComputedStyle(group).overflow === 'hidden'
                    && cr.top >= gr.bottom && cr.bottom <= rr.bottom + 1
                    && cr.left >= rr.left && cr.right <= rr.right + 1
                    && chip.scrollWidth <= chip.clientWidth + 1 && chip.scrollHeight <= chip.clientHeight + 1;
            })
            """));
    }
}
