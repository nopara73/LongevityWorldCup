using System.Collections;
using System.Reflection;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RedemptionArcTests
{
    internal static readonly DateTime AsOf = new(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AwardsUseWorstToLatestForBothClocksAndKeepTiedWinners()
    {
        var comeback = Athlete("comeback", Panel("2024-01-01", 0), Panel("2025-01-01", 2), Panel("2026-01-01", 1));
        var tied = (JsonObject)comeback.DeepClone();
        tied["AthleteSlug"] = "tied";
        var smaller = Athlete("smaller", Panel("2025-01-01", 1.5), Panel("2026-01-01", 1));
        var stats = PhenoStatsCalculator.Compute(comeback, AsOf);
        Assert.True(stats.PhenoAgeDiffFromBaseline > 0);
        Assert.True(stats.BortzAgeDiffFromBaseline > 0);
        Assert.True(stats.PhenoAgeImprovementFromWorst < 0);
        Assert.True(stats.BortzAgeImprovementFromWorst < 0);

        var awards = ImprovementAwards(comeback, tied, smaller);
        foreach (var clock in new[] { "Pheno", "Bortz" })
            Assert.Equal(new[] { "comeback", "tied" }, awards.Where(a => a.Label == $"{clock} Age best improvement").Select(a => a.Slug).Order());
    }

    [Fact]
    public void AppendedOlderExcellentResultDoesNotBecomeLatestOrEarnAComeback()
    {
        var athlete = Athlete("backfill", Panel("2025-01-01", 1), Panel("2026-01-01", 2), Panel("2024-01-01", 0));
        var ordered = Athlete("ordered", Panel("2024-01-01", 0), Panel("2025-01-01", 1), Panel("2026-01-01", 2));
        var actual = PhenoStatsCalculator.Compute(athlete, AsOf);
        var expected = PhenoStatsCalculator.Compute(ordered, AsOf);
        Assert.Equal(0d, actual.PhenoAgeImprovementFromWorst);
        Assert.Equal(0d, actual.BortzAgeImprovementFromWorst);
        Assert.Equal(expected.PhenoAgeDiffFromBaseline, actual.PhenoAgeDiffFromBaseline);
        Assert.Equal(expected.BortzAgeDiffFromBaseline, actual.BortzAgeDiffFromBaseline);
        Assert.Empty(ImprovementAwards(athlete));
    }

    [Fact]
    public void SingleEligibleResultAndUnchangedResultsDoNotEarnRedemption()
    {
        var incomplete = Panel("2026-02-01", 0);
        incomplete.Remove("CrpMgL"); // Required by both clocks.
        var single = Athlete("single", Panel("2026-01-01", 1), incomplete);
        var unchanged = Athlete("unchanged", Panel("2026-01-01", 1), Panel("2026-01-01", 1));
        Assert.Empty(ImprovementAwards(single, unchanged));
    }

    [Fact]
    public void EachClockRequiresItsOwnEligibleHistory()
    {
        var early = Panel("2025-01-01", 2);
        early.Remove("CystatinCMgL");
        var athlete = Athlete("pheno-only-comeback", early, Panel("2026-01-01", 1));
        var award = Assert.Single(ImprovementAwards(athlete));
        Assert.Equal("Pheno Age best improvement", award.Label);
    }

    private static (string Label, string Slug)[] ImprovementAwards(params JsonObject[] athletes)
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var stats = typeof(BadgeDataService).GetMethod("BuildAthleteStats", flags)!
            .Invoke(null, [AsOf, new JsonArray(athletes.Select(a => a.DeepClone()).ToArray())]);
        var rowType = typeof(BadgeDataService).GetNestedType("AwardRow", BindingFlags.NonPublic)!;
        var awards = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(rowType))!;
        typeof(BadgeDataService).GetMethod("AddImprovementAwards", flags)!.Invoke(null, [stats, awards]);
        return awards.Cast<object>().Select(a => (
            (string)rowType.GetProperty("BadgeLabel")!.GetValue(a)!,
            (string)rowType.GetProperty("AthleteSlug")!.GetValue(a)!)).ToArray();
    }

    internal static JsonObject Athlete(string slug, params JsonObject[] panels) => new()
    {
        ["AthleteSlug"] = slug,
        ["Name"] = slug,
        ["DateOfBirth"] = new JsonObject { ["Year"] = 1980, ["Month"] = 1, ["Day"] = 1 },
        ["Biomarkers"] = new JsonArray(panels.Cast<JsonNode>().ToArray())
    };

    internal static JsonObject Panel(string date, double severity) => new()
    {
        ["Date"] = date,
        ["AlbGL"] = 45 - 5 * severity,
        ["AlpUL"] = 55 + 30 * severity,
        ["UreaMmolL"] = 5 + 2 * severity,
        ["CholesterolMmolL"] = 5 + severity,
        ["CreatUmolL"] = 80 + 20 * severity,
        ["CystatinCMgL"] = 0.85 + 0.3 * severity,
        ["Hba1cMmolMol"] = 34 + 12 * severity,
        ["CrpMgL"] = 0.8 + 4 * severity,
        ["GgtUL"] = 25 + 30 * severity,
        ["Rbc10e12L"] = 4.8,
        ["McvFL"] = 90 + 3 * severity,
        ["RdwPc"] = 12.5 + severity,
        ["Wbc1000cellsuL"] = 5 + severity,
        ["MonocytePc"] = 6d,
        ["NeutrophilPc"] = 55d,
        ["LymPc"] = 32 - 5 * severity,
        ["AltUL"] = 22 + 10 * severity,
        ["ShbgNmolL"] = 45d,
        ["VitaminDNmolL"] = 90 - 20 * severity,
        ["GluMmolL"] = 5 + severity,
        ["MchPg"] = 31d,
        ["ApoA1GL"] = 1.6
    };
}
