using System.Reflection;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeLabelCanonicalizationTests
{
    [Theory]
    [InlineData("Most Submissions", "Most submissions")]
    [InlineData("PhenoAge – Lowest", "Pheno Age – lowest")]
    [InlineData("Bortz Age - Lowest", "Bortz Age – lowest")]
    [InlineData("Best Domain – Vitamin D", "Best domain – vitamin D")]
    [InlineData("Crowd - Most Guessed", "Crowd – most guessed")]
    [InlineData("Perfect Application", "Perfect application")]
    public void StoredBadgeLabelsCanonicalizeToCurrentDisplayLabels(string storedLabel, string expected)
    {
        var method = typeof(BadgeDataService).GetMethod(
            "CanonicalBadgeLabelForAwardRow",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = Assert.IsType<string>(method.Invoke(null, [storedLabel]));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Chronological Age - Oldest", "Chronological age – oldest")]
    [InlineData("Pheno Age - lowest", "Pheno Age – lowest")]
    [InlineData("Bortz Age – Lowest", "Bortz Age – lowest")]
    [InlineData("Crowd Age - lowest", "Crowd Age – lowest")]
    [InlineData("Best Domain – Vitamin D", "Best domain – vitamin D")]
    public void EventBadgeLabelsNormalizeToDisplayLabels(string storedLabel, string expected)
    {
        Assert.Equal(expected, EventHelpers.NormalizeBadgeLabel(storedLabel));
    }

    [Theory]
    [InlineData("Best Domain – Vitamin D", "vitamin D")]
    [InlineData("Best domain - immune", "immune")]
    public void BadgeDomainExtractionUsesDisplayCanonicalization(string label, string expected)
    {
        Assert.Equal(expected, EventHelpers.ExtractDomainFromLabel(label));
    }

    [Theory]
    [InlineData("vitamin_d", "Best domain – vitamin D")]
    [InlineData("vitamin D", "Best domain – vitamin D")]
    [InlineData("Vitamin-D", "Best domain – vitamin D")]
    [InlineData("immune", "Best domain – immune")]
    public void BestDomainWinnerLookupRecognizesCanonicalDomainKeys(string domainKey, string expected)
    {
        var method = typeof(AthleteDataService).GetMethod(
            "BestDomainBadgeLabelForDomainKey",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var actual = Assert.IsType<string>(method.Invoke(null, [domainKey]));
        Assert.Equal(expected, actual);
    }
}
