using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BortzAgeCapTests
{
    [Fact]
    public void ConfiguredCaps_StopFurtherImprovementInRawLaboratoryUnits()
    {
        foreach (var feature in BortzAgeHelper.Features.Where(feature => feature.Cap.HasValue))
        {
            var cap = feature.Cap!.Value;
            var beyondCap = feature.CapMode == BortzAgeHelper.CapMode.Floor
                ? cap / 2d
                : cap * 2d;

            var atCap = BortzAgeHelper.CalculateFeatureContribution(cap, feature);
            var beyond = BortzAgeHelper.CalculateFeatureContribution(beyondCap, feature);

            Assert.True(double.IsFinite(atCap), $"{feature.Id} contribution at its cap should be finite.");
            Assert.Equal(atCap, beyond, precision: 12);
        }
    }

    [Theory]
    [InlineData("alt", 29d)]
    [InlineData("vitamin_d", 112.6d)]
    public void LogTransformedCaps_AreAppliedBeforeTheTransform(string featureId, double rawCap)
    {
        var feature = Assert.Single(BortzAgeHelper.Features, feature => feature.Id == featureId);
        var expected = (Math.Log(rawCap) - feature.Mean) * feature.BaaCoeff;

        var actual = BortzAgeHelper.CalculateFeatureContribution(rawCap * 10d, feature);

        Assert.Equal(expected, actual, precision: 12);
    }

    [Fact]
    public void CalculateBaa_PlateausWhenLogTransformedMarkersExceedTheirCaps()
    {
        var atCaps = BortzAgeHelper.Features
            .Select(feature => feature.IsLog ? Math.Exp(feature.Mean) : feature.Mean)
            .ToArray();
        var beyondCaps = (double[])atCaps.Clone();

        SetRawValue(atCaps, "alt", 29d);
        SetRawValue(atCaps, "vitamin_d", 112.6d);
        SetRawValue(beyondCaps, "alt", 290d);
        SetRawValue(beyondCaps, "vitamin_d", 1126d);

        Assert.Equal(
            BortzAgeHelper.CalculateBAA(atCaps),
            BortzAgeHelper.CalculateBAA(beyondCaps),
            precision: 12);
    }

    private static void SetRawValue(double[] values, string featureId, double value)
    {
        var index = Array.FindIndex(BortzAgeHelper.Features, feature => feature.Id == featureId);
        Assert.True(index >= 0, $"Missing Bortz feature {featureId}.");
        values[index] = value;
    }
}
