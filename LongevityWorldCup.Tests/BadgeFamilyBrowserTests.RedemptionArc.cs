using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Theory]
    [InlineData(1440, true)]
    [InlineData(390, true)]
    [InlineData(1440, false)]
    [InlineData(390, false)]
    public async Task RedemptionArcUsesWorstToLatestInProfileAndRawBadgeTooltips(int width, bool serverMetrics)
    {
        await using var context = await NewBadgeContextAsync(width, 900, true);
        PhenoStatsCalculator.Result expected = null!;
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var athlete = athletes.OfType<JsonObject>().Single(a => a["AthleteSlug"]!.GetValue<string>() == "michael_lustgarten");
            athlete["Biomarkers"] = new JsonArray(
                RedemptionArcTests.Panel("2025-01-01", 2),
                RedemptionArcTests.Panel("2026-01-01", 1),
                RedemptionArcTests.Panel("2024-01-01", 0));
            athlete["Badges"] = new JsonArray(
                new JsonObject { ["BadgeLabel"] = "Pheno Age best improvement", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Bortz Age best improvement", ["Place"] = 1 });
            // Old first-to-last fields must not leak into either explanation.
            athlete["PhenoAgeDiffFromBaseline"] = -99;
            athlete["BortzAgeDiffFromBaseline"] = -99;
            expected = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
            foreach (var (key, value) in new[] {
                ("PhenoAgeImprovementFromWorst", expected.PhenoAgeImprovementFromWorst),
                ("BortzAgeImprovementFromWorst", expected.BortzAgeImprovementFromWorst) })
            {
                if (serverMetrics) athlete[key] = value;
                else athlete.Remove(key);
            }
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        var shelf = page.Locator("#modalBadgeStrip");
        var toggles = shelf.Locator(".modal-badge-toggle");
        await Assertions.Expect(toggles).ToHaveCountAsync(2);
        var values = new[] { expected.PhenoAgeImprovementFromWorst, expected.BortzAgeImprovementFromWorst };
        for (var i = 0; i < values.Length; i++)
        {
            await toggles.Nth(i).ClickAsync();
            var explanation = shelf.Locator(".modal-badge-explainer");
            await Assertions.Expect(explanation).ToContainTextAsync("from worst submission to latest result");
            await Assertions.Expect(explanation).ToContainTextAsync(values[i]!.Value.ToString("F1", CultureInfo.InvariantCulture) + " years");
            await Assertions.Expect(explanation).Not.ToContainTextAsync("-99");
            Assert.True(await explanation.EvaluateAsync<bool>("""
                el => {
                    const rect = el.getBoundingClientRect();
                    return el.scrollWidth <= el.clientWidth && rect.left >= 0 && rect.right <= innerWidth;
                }
                """));
        }
        var artifacts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.artifacts/redemption"));
        Directory.CreateDirectory(artifacts);
        await shelf.ScreenshotAsync(new() { Path = Path.Combine(artifacts, $"profile-{width}-{serverMetrics}.png") });
        var rawTooltip = await page.EvaluateAsync<string>("""
            () => window.makeTooltipFromServerBadge({ BadgeLabel: 'Pheno Age best improvement' }, {
                PhenoAgeImprovementFromWorst: -7.25, PhenoAgeDiffFromBaseline: -99
            })
            """);
        Assert.Contains("-7.3 years", rawTooltip);
        Assert.DoesNotContain("-99", rawTooltip);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }
}
