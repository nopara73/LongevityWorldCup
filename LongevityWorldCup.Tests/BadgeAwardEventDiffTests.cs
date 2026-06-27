using System.Collections;
using System.Reflection;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeAwardEventDiffTests
{
    [Fact]
    public void AmateurAgeReductionReplacementKeepsPreviousHolderForNormalHandoff()
    {
        var rowType = GetAwardRowType();
        var before = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 1, "wen_z"),
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 2, "philipp_schmeing"));
        var after = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 1, "philipp_schmeing"));

        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(before, after);
        var item = Assert.Single(events);

        Assert.Equal("philipp_schmeing", GetStringProperty(item, "AthleteSlug"));
        Assert.Equal("wen_z", GetStringProperty(item, "ReplacedSlug"));
    }

    [Fact]
    public void AmateurAgeReductionGraduationDoesNotFrameCurrentProAsReplaced()
    {
        var rowType = GetAwardRowType();
        var before = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 1, "wen_z"),
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 2, "philipp_schmeing"));
        var after = CreateAwardRowList(
            CreateAwardRow(rowType, "Age reduction", "Amateur", "Amateur", 1, "philipp_schmeing"));

        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(before, after, "wen_z");
        var item = Assert.Single(events);

        Assert.Equal("philipp_schmeing", GetStringProperty(item, "AthleteSlug"));
        Assert.Null(GetStringProperty(item, "ReplacedSlug"));
        Assert.Null(GetProperty(item, "ReplacedSlugs"));
    }

    private static List<object> BuildBadgeAwardEventsForCurrentSnapshotChange(
        object before,
        object after,
        params string[] currentProSlugs)
    {
        var method = typeof(BadgeDataService).GetMethod(
            "BuildBadgeAwardEventsForCurrentSnapshotChange",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var proSlugs = new HashSet<string>(currentProSlugs, StringComparer.OrdinalIgnoreCase);
        var result = method.Invoke(null, [before, after, DateTime.UtcNow, proSlugs]);
        var events = Assert.IsAssignableFrom<IEnumerable>(result);

        return events.Cast<object>().ToList();
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

    private static string? GetStringProperty(object item, string propertyName)
        => GetProperty(item, propertyName) as string;

    private static object? GetProperty(object item, string propertyName)
        => item.GetType().GetProperty(propertyName)!.GetValue(item);
}
