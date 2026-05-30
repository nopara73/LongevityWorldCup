using System.Reflection;
using System.Collections;
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

    [Fact]
    public void ComputedAwardRowsAreCanonicalizedBeforePersistence()
    {
        var rowType = GetAwardRowType();
        var rows = CreateAwardRowList(
            CreateAwardRow(rowType, "Age Reduction", "Global", null, 1, "alice"),
            CreateAwardRow(rowType, "≥2 Submissions", "Global", null, null, "bob"));

        var method = typeof(BadgeDataService).GetMethod(
            "CanonicalizeAwardRows",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var canonicalRows = Assert.IsAssignableFrom<IEnumerable>(method.Invoke(null, [rows]));
        var labels = canonicalRows.Cast<object>()
            .Select(row => Assert.IsType<string>(rowType.GetProperty("BadgeLabel")!.GetValue(row)))
            .ToList();

        Assert.Equal(["Age reduction", "≥2 submissions"], labels);
    }

    [Fact]
    public void BadgeAwardEventDiffIgnoresLegacyStoredLabelCasing()
    {
        var rowType = GetAwardRowType();
        var before = CreateAwardRowList(
            CreateAwardRow(rowType, "Age Reduction", "Global", null, 1, "alice"),
            CreateAwardRow(rowType, "≥2 Submissions", "Global", null, null, "bob"));
        var after = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Global", null, 1, "alice"),
            CreateAwardRow(rowType, "≥2 submissions", "Global", null, null, "bob"));

        var canonicalize = typeof(BadgeDataService).GetMethod(
            "CanonicalizeAwardRows",
            BindingFlags.NonPublic | BindingFlags.Static);
        var diff = typeof(BadgeDataService).GetMethod(
            "BuildBadgeAwardEvents",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(canonicalize);
        Assert.NotNull(diff);

        var canonicalBefore = canonicalize.Invoke(null, [before]);
        var canonicalAfter = canonicalize.Invoke(null, [after]);
        var events = Assert.IsAssignableFrom<ICollection>(diff.Invoke(null, [canonicalBefore, canonicalAfter, DateTime.UtcNow]));

        Assert.Empty(events);
    }

    [Fact]
    public void EmptyPreviousAwardSnapshotDoesNotCreateBaselineBadgeEvents()
    {
        var rowType = GetAwardRowType();
        var before = CreateAwardRowList();
        var after = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Global", null, 1, "alice"));

        var method = typeof(BadgeDataService).GetMethod(
            "BuildBadgeAwardEventsForSnapshotChange",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        var events = Assert.IsAssignableFrom<ICollection>(method.Invoke(null, [before, after, DateTime.UtcNow]));

        Assert.Empty(events);
    }

    private static Type GetAwardRowType()
    {
        var type = typeof(BadgeDataService).GetNestedType("AwardRow", BindingFlags.NonPublic);
        Assert.NotNull(type);
        return type;
    }

    private static object CreateAwardRow(
        Type rowType,
        string badgeLabel,
        string leagueCategory,
        string? leagueValue,
        int? place,
        string athleteSlug)
    {
        var row = Activator.CreateInstance(rowType, nonPublic: true);
        Assert.NotNull(row);

        rowType.GetProperty("BadgeLabel")!.SetValue(row, badgeLabel);
        rowType.GetProperty("LeagueCategory")!.SetValue(row, leagueCategory);
        rowType.GetProperty("LeagueValue")!.SetValue(row, leagueValue);
        rowType.GetProperty("Place")!.SetValue(row, place);
        rowType.GetProperty("AthleteSlug")!.SetValue(row, athleteSlug);
        rowType.GetProperty("DefinitionHash")!.SetValue(row, null);
        rowType.GetProperty("OccurredAtUtc")!.SetValue(row, null);

        return row;
    }

    private static object CreateAwardRowList(params object[] rows)
    {
        var rowType = GetAwardRowType();
        var listType = typeof(List<>).MakeGenericType(rowType);
        var list = Assert.IsAssignableFrom<IList>(Activator.CreateInstance(listType));
        foreach (var row in rows)
            list.Add(row);

        return list;
    }
}
