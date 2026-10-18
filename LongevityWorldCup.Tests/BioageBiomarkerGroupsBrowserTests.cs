using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageBiomarkerGroupsBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 1440, 900, false)]
    [InlineData("/bortz-age", 1440, 900, true)]
    [InlineData("/pheno-age", 390, 844, true)]
    [InlineData("/bortz-age", 320, 640, false)]
    public async Task BiomarkerEntry_GroupsEachDomainIntoOneCardOfRows(string path, int width, int height, bool dark)
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
        await page.GotoAsync(path);
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.WaitForFunctionAsync("() => document.querySelector('.bioageform')?.classList.contains('bioage-biomarker-entry-ready')");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const problems = [];
                const groups = [...document.querySelectorAll('#lwc-step-2 > fieldset')].filter(group => group.querySelector('.biomarker-card'));
                if (groups.length < 3) problems.push(`only ${groups.length} domains found`);
                for (const group of groups) {
                    const name = group.querySelector('legend')?.textContent.trim();
                    const cards = [...group.querySelectorAll(':scope > .biomarker-card')];
                    const radius = card => parseFloat(getComputedStyle(card).borderTopLeftRadius);
                    const bottomRadius = card => parseFloat(getComputedStyle(card).borderBottomLeftRadius);
                    if (radius(cards[0]) < 4) problems.push(`${name}: the domain card should round its top corners`);
                    if (bottomRadius(cards[cards.length - 1]) < 4) problems.push(`${name}: the domain card should round its bottom corners`);
                    for (let i = 1; i < cards.length; i++) {
                        const gap = cards[i].getBoundingClientRect().top - cards[i - 1].getBoundingClientRect().bottom;
                        if (gap > 0.5) problems.push(`${name}: a ${gap.toFixed(0)}px gap splits its rows into separate boxes`);
                        if (radius(cards[i]) > 0.5) problems.push(`${name}: inner rows should not be rounded boxes`);
                    }
                }
                if (document.documentElement.scrollWidth > innerWidth) problems.push('page scrolls sideways');
                return problems;
            }
            """);
        Assert.True(problems.Length == 0, $"{path} {width}x{height}: {string.Join("; ", problems)}");
    }
}
