using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AlbuminCapTests
{
    // The approved ceiling is 54 g/L; 66 g/L is the result reported in issue #320.
    [Theory]
    [InlineData(54d)]
    [InlineData(54.001d)]
    [InlineData(55d)]
    [InlineData(66d)]
    [InlineData(108d)]
    public void BothClocks_StopAlbuminImprovementAt54GramsPerLitre(double albumin)
    {
        var atCap = AlbuminCapTestData.PhenoValues(54);
        var actual = AlbuminCapTestData.PhenoValues(albumin);
        var belowCap = AlbuminCapTestData.PhenoValues(53.999);

        Assert.Equal(54 * -0.0336 + 51 * 0.0019,
            PhenoAgeHelper.CalculateLiverScore(actual), precision: 12);
        Assert.Equal(PhenoAgeHelper.CalculatePhenoAge(atCap),
            PhenoAgeHelper.CalculatePhenoAge(actual), precision: 12);
        Assert.Equal(PhenoAgeHelper.CalculateLiverPhenoAgeContributor(atCap),
            PhenoAgeHelper.CalculateLiverPhenoAgeContributor(actual), precision: 12);
        Assert.True(PhenoAgeHelper.CalculatePhenoAge(belowCap) > PhenoAgeHelper.CalculatePhenoAge(atCap));

        var bortzAtCap = AlbuminCapTestData.BortzValues(54);
        var bortzActual = AlbuminCapTestData.BortzValues(albumin);
        var bortzBelowCap = AlbuminCapTestData.BortzValues(53.999);
        var feature = Assert.Single(BortzAgeHelper.Features, item => item.Id == "albumin");

        Assert.Equal((54 - 45.1238763) * -0.011331946,
            BortzAgeHelper.CalculateFeatureContribution(albumin, feature), precision: 12);
        Assert.Equal(BortzAgeHelper.CalculateBAA(bortzAtCap),
            BortzAgeHelper.CalculateBAA(bortzActual), precision: 12);
        Assert.Equal(BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, bortzAtCap),
            BortzAgeHelper.CalculateBortzAgeFromRaw(AlbuminCapTestData.Age, bortzActual), precision: 12);
        Assert.True(BortzAgeHelper.CalculateBAA(bortzBelowCap) > BortzAgeHelper.CalculateBAA(bortzAtCap));
    }

    [Theory]
    [InlineData(55d)]
    [InlineData(66d)]
    [InlineData(108d)]
    public void StoredAndSubmittedResults_KeepCappedScoresAndOriginalLaboratoryValues(double albumin)
    {
        var atCap = AlbuminCapTestData.Athlete(54);
        var actual = AlbuminCapTestData.Athlete(albumin);
        foreach (var athlete in new[] { atCap, actual })
        {
            var baseline = AlbuminCapTestData.Marker(45);
            baseline.Date = "2025-01-01";
            athlete["Biomarkers"]!.AsArray().Insert(0, JsonSerializer.SerializeToNode(baseline));
        }

        var expected = PhenoStatsCalculator.Compute(atCap, AlbuminCapTestData.ResultDate);
        var result = PhenoStatsCalculator.Compute(actual, AlbuminCapTestData.ResultDate);

        Assert.NotNull(result.AgeReduction);
        Assert.NotNull(result.BortzAgeReduction);
        Assert.Equal(expected.LowestPhenoAge, result.LowestPhenoAge);
        Assert.Equal(expected.LowestBortzAge, result.LowestBortzAge);
        Assert.Equal(expected.AgeReduction, result.AgeReduction);
        Assert.Equal(expected.BortzAgeReduction, result.BortzAgeReduction);
        Assert.Equal(expected.PhenoAgeDiffFromBaseline, result.PhenoAgeDiffFromBaseline);
        Assert.Equal(expected.BortzAgeDiffFromBaseline, result.BortzAgeDiffFromBaseline);
        Assert.Equal(expected.PhenoAgeImprovementFromWorst, result.PhenoAgeImprovementFromWorst);
        Assert.Equal(expected.BortzAgeImprovementFromWorst, result.BortzAgeImprovementFromWorst);
        Assert.Equal(PhenoAgeHelper.CalculateLiverPhenoAgeContributor(expected.BestMarkerValues!),
            PhenoAgeHelper.CalculateLiverPhenoAgeContributor(result.BestMarkerValues!), precision: 12);
        Assert.Equal(albumin, result.BestMarkerValues![1]);
        Assert.Equal(albumin, actual["Biomarkers"]![1]!["AlbGL"]!.GetValue<double>());

        var submitted = SubmittedAgeDifferenceCalculator.Calculate(
            AlbuminCapTestData.DateOfBirth, [AlbuminCapTestData.Marker(albumin)],
            includePheno: true, includeBortz: true);
        var submittedAtCap = SubmittedAgeDifferenceCalculator.Calculate(
            AlbuminCapTestData.DateOfBirth, [AlbuminCapTestData.Marker(54)],
            includePheno: true, includeBortz: true);
        Assert.NotNull(submitted.PhenoDifference);
        Assert.NotNull(submitted.BortzDifference);
        Assert.Equal(submittedAtCap, submitted);
    }
}

internal static class AlbuminCapTestData
{
    public static DateTime ResultDate => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static DateOfBirthData DateOfBirth => new() { Year = 1980, Month = 1, Day = 1 };
    public static double Age => (ResultDate - new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalDays / 365.2425;

    public static BiomarkerData Marker(double albumin) => new()
    {
        Date = "2026-01-01", AlbGL = albumin, AlpUL = 51, UreaMmolL = 5.4,
        CholesterolMmolL = 4.8, CreatUmolL = 88.4, CystatinCMgL = 0.85,
        Hba1cMmolMol = 34, CrpMgL = 0.8, GgtUL = 22, Rbc10e12L = 4.7,
        McvFL = 96.7, RdwPc = 12.5, Wbc1000cellsuL = 6.3, MonocytePc = 7.2,
        NeutrophilPc = 58.1, LymPc = 25.5, AltUL = 24, ShbgNmolL = 45,
        VitaminDNmolL = 85, GluMmolL = 5.05, MchPg = 31.5, ApoA1GL = 1.55
    };

    public static JsonObject Athlete(double albumin, string name = "Albumin") => new()
    {
        ["AthleteSlug"] = name.ToLowerInvariant(),
        ["Name"] = name,
        ["DateOfBirth"] = JsonSerializer.SerializeToNode(DateOfBirth),
        ["Biomarkers"] = new JsonArray(JsonSerializer.SerializeToNode(Marker(albumin)))
    };

    public static double[] PhenoValues(double albumin) =>
        [Age, albumin, 88.4, 5.05, Math.Log(0.8 / 10), 6.3, 25.5, 96.7, 12.5, 51];

    public static double[] BortzValues(double albumin) =>
        [Age, albumin, 51, 5.4, 4.8, 88.4, 0.85, 34, 0.8, 22, 4.7, 96.7, 12.5,
         6.3 * 7.2 / 100, 6.3 * 58.1 / 100, 25.5, 24, 45, 85, 5.05, 31.5, 1.55];
}
