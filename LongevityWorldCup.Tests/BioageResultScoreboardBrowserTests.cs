using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BioageResultScoreboardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", "#phenoAgeResult", 1440, 900)]
    [InlineData("/bortz-age", "#bortzAgeResult", 1440, 900)]
    [InlineData("/pheno-age", "#phenoAgeResult", 390, 844)]
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
}
