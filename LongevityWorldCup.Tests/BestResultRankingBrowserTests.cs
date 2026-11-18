using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class BestResultRankingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task RankingAndProfileUseBestReductionDespiteAnOlderLowerAge(int width)
    {
        const string name = "Michael Lustgarten";
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 950 },
            ReducedMotion = ReducedMotion.Reduce,
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll','true')");
        PhenoStatsCalculator.Result? expected = null;
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var target = athletes.OfType<JsonObject>().Single(a => a["Name"]!.GetValue<string>() == name);
            target["Biomarkers"] = new JsonArray(
                RedemptionArcTests.Panel("2026-11-01", 0),
                RedemptionArcTests.Panel("2026-01-01", 0));
            expected = PhenoStatsCalculator.Compute(target, DateTime.UtcNow);
            Assert.True(expected.LowestPhenoAge < expected.PhenoRankResult!.BiologicalAge);
            Assert.True(expected.LowestBortzAge < expected.BortzRankResult!.BiologicalAge);
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.WaitForFunctionAsync("name => window.getLeaderboardPeek?.(name)?.phenoRankResult != null", name);
        foreach (var clock in new[] { "pheno", "bortz" })
        {
            var result = clock == "pheno" ? expected!.PhenoRankResult! : expected!.BortzRankResult!;
            var values = await page.EvaluateAsync<double[]>("""
                ({ name, clock }) => {
                    const result = window.getLeaderboardPeek(name)[clock + 'RankResult'];
                    return [result.bio, result.chrono];
                }
                """, new { name, clock });
            Assert.Equal(result.BiologicalAge, values[0], precision: 8);
            Assert.Equal(result.ChronologicalAge, values[1], precision: 8);
            await page.Locator($"label[for='view-{clock}']").ClickAsync();
            var row = page.Locator($"#athleteLeaderboard tbody tr[data-athlete-name='{name}']");
            var score = await row.Locator(".age-reduction").InnerTextAsync();
            var displayed = double.Parse(System.Text.RegularExpressions.Regex.Match(score, @"-?\d+(?:\.\d+)?").Value,
                System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(displayed, result.AgeReduction - 0.051, result.AgeReduction + 0.051);
        }
        await page.Locator($"#athleteLeaderboard tbody tr[data-athlete-name='{name}'] .athlete-name").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeVisibleAsync();
        foreach (var clock in new[] { "Pheno", "Bortz" })
        {
            await page.Locator("#ageTab" + clock).ClickAsync();
            await Assertions.Expect(page.Locator("#ageResultDate")).ToHaveAttributeAsync("datetime", "2026-11-01");
            await Assertions.Expect(page.Locator("#ageResultSelect")).ToHaveValueAsync("1");
            var result = clock == "Pheno" ? expected!.PhenoRankResult! : expected!.BortzRankResult!;
            var percentId = clock == "Pheno" ? "ageReductionPercent" : "bortzAgeReductionPercent";
            await Assertions.Expect(page.Locator("#" + percentId)).ToHaveTextAsync(
                result.AgeReductionPercent!.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        var directory = Path.Combine(AppContext.BaseDirectory, "ranking-screenshots");
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"profile-{width}.png") });
    }
}
