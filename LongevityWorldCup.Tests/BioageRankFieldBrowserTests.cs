using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class BioageRankFieldBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(195, 100)]
    [InlineData(390, 200)]
    public async Task RankPreview_OrdinaryRankUsesTheCardWidthBeforeWrapping(int width, int textPercent)
    {
        // 195 CSS pixels is a 390px browser window at native 200% zoom.
        // The independent delivery gallery also exercises chrome.tabs.setZoom(2).
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/pheno-age");
        await page.WaitForFunctionAsync("() => window.LwcBioAgeRankPreview");
        var fits = await page.EvaluateAsync<bool>("""
            async textPercent => {
                document.documentElement.style.fontSize = textPercent + '%';
                const result = document.querySelector('#phenoAgeResult');
                result.classList.add('show');
                result.dataset.bioageResultStage = 'rank';
                window.getSharedAthletes = () => Promise.resolve([]);
                await window.LwcBioAgeRankPreview.render('phenoAgeRankPreview', {
                    clock: 'pheno', ageReduction: -5, dateOfBirth: new Date(1980, 0, 1)
                });
                const number = document.querySelector('.bioage-rank-number');
                // A presentation fixture for an ordinary three-digit field rank.
                number.textContent = '#220';
                await document.fonts.ready;
                const range = document.createRange();
                range.selectNodeContents(number);
                const lines = [...range.getClientRects()].filter(r => r.width > 0);
                const card = result.getBoundingClientRect();
                return new Set(lines.map(r => Math.round(r.top))).size === 1
                    && lines.every(r => r.left >= card.left && r.right <= card.right);
            }
            """, textPercent);
        Assert.True(fits, "An ordinary rank must fit on one line using the card width, without reducing its font.");
    }

    [Theory]
    [InlineData("pheno", false)]
    [InlineData("pheno", true)]
    [InlineData("bortz", false)]
    [InlineData("bortz", true)]
    public async Task RankPreview_UsesBestReductionAcrossDatedPanels(string clock, bool reverse)
    {
        var older = RedemptionArcTests.Panel("2026-01-01", 0);
        var latest = RedemptionArcTests.Panel("2026-11-01", 0);
        var athlete = RedemptionArcTests.Athlete("backfill", older);
        var before = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
        athlete["Biomarkers"]!.AsArray().Add(latest);
        if (reverse)
            athlete["Biomarkers"] = new JsonArray(latest.DeepClone(), older.DeepClone());
        var expected = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
        var oldReduction = (clock == "pheno" ? before.AgeReduction : before.BortzAgeReduction)!.Value;
        var bestReduction = (clock == "pheno" ? expected.AgeReduction : expected.BortzAgeReduction)!.Value;
        Assert.True(bestReduction < oldReduction);
        Assert.True(clock == "pheno"
            ? expected.LowestPhenoAge < expected.PhenoRankResult!.BiologicalAge
            : expected.LowestBortzAge < expected.BortzRankResult!.BiologicalAge);

        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/{clock}-age");
        await page.WaitForFunctionAsync("() => window.LwcBioAgeRankPreview && window.PhenoAge && window.BortzAge");
        await page.EvaluateAsync("""
            async ({clock, athlete, reduction}) => {
                window.getSharedAthletes = () => Promise.resolve([JSON.parse(athlete)]);
                await window.LwcBioAgeRankPreview.render(clock + 'AgeRankPreview', {
                    clock, ageReduction: reduction, dateOfBirth: new Date(1980, 0, 1)
                });
            }
            """, new { clock, athlete = athlete.ToJsonString(), reduction = (bestReduction + oldReduction) / 2 });
        var preview = page.Locator($"#{clock}AgeRankPreview");
        await Assertions.Expect(preview.Locator(".bioage-rank-number")).ToHaveTextAsync("#2");
        await Assertions.Expect(preview.Locator(".bioage-rank-meta")).ToContainTextAsync("2 of 2");
        await Assertions.Expect(preview.Locator(".bioage-rank-row-name")).ToHaveTextAsync(new[] { "backfill", "You" });
    }
}
