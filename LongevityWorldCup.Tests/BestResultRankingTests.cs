using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BestResultRankingTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OlderLowerBiologicalAgeCannotWorsenTheBestReduction(bool pro, bool reverse)
    {
        var latest = RedemptionArcTests.Panel("2026-11-01", 0);
        var older = RedemptionArcTests.Panel("2026-01-01", 0);
        if (!pro)
        {
            latest.Remove("CystatinCMgL");
            older.Remove("CystatinCMgL");
        }
        var athlete = RedemptionArcTests.Athlete("backfill", latest);
        var expected = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
        athlete["Biomarkers"]!.AsArray().Add(older);
        if (reverse)
            athlete["Biomarkers"] = new JsonArray(athlete["Biomarkers"]!.AsArray().Reverse().Select(n => n!.DeepClone()).ToArray());
        var actual = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);

        Assert.True(actual.LowestPhenoAge < expected.LowestPhenoAge);
        Assert.Equal(expected.AgeReduction, actual.AgeReduction);
        Assert.Equal(expected.PhenoRankResult, actual.PhenoRankResult);
        Assert.Equal(new DateTime(2026, 1, 1), actual.LowestPhenoAgeDateUtc!.Value.Date);
        if (pro)
        {
            Assert.True(actual.LowestBortzAge < expected.LowestBortzAge);
            Assert.Equal(expected.BortzAgeReduction, actual.BortzAgeReduction);
            Assert.Equal(expected.BortzRankResult, actual.BortzRankResult);
        }
        else Assert.Null(actual.BortzRankResult);

        var source = new JsonArray(athlete);
        var snapshot = new PublicLeaderboardSnapshot(source, RedemptionArcTests.AsOf);
        foreach (var view in pro ? new[] { "pheno", "bortz", "ultimate" } : new[] { "pheno", "ultimate" })
        {
            var row = Assert.Single(snapshot.Select($"/league/{view}", QueryCollection.Empty).Snapshot.Rows);
            var reduction = pro && view != "pheno" ? expected.BortzAgeReduction : expected.AgeReduction;
            Assert.Equal(reduction!.Value, row.EffectiveAgeReductionYears!.Value, precision: 2);
        }
    }

    [Fact]
    public void IncompleteAndInvalidPanelsDoNotReplaceRankingResults()
    {
        var valid = RedemptionArcTests.Panel("2026-01-01", 0);
        var athlete = RedemptionArcTests.Athlete("invalid", valid);
        var expected = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
        var incomplete = RedemptionArcTests.Panel("2026-11-01", 0);
        incomplete.Remove("CrpMgL");
        var invalid = RedemptionArcTests.Panel("2026-11-02", 0);
        invalid["CrpMgL"] = 0;
        athlete["Biomarkers"]!.AsArray().Add(incomplete);
        athlete["Biomarkers"]!.AsArray().Add(invalid);
        var actual = PhenoStatsCalculator.Compute(athlete, RedemptionArcTests.AsOf);
        Assert.Equal(expected.PhenoRankResult, actual.PhenoRankResult);
        Assert.Equal(expected.BortzRankResult, actual.BortzRankResult);
    }
}
