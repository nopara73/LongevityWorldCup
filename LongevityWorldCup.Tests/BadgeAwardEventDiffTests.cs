using System.Collections;
using System.Reflection;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BadgeAwardEventDiffTests
{
    private static readonly DateTime SnapshotTimeUtc = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void InitialCurrentSnapshotDoesNotAnnounceExistingAwards()
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(),
            CreateAwardRowList(Award("alice"), Award("bob", place: 1)));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void UnchangedOwnershipIgnoresRowOrderDuplicatesAndAthleteSlugCasing(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("alice", place), Award("bob", place)),
            CreateAwardRowList(Award("BOB", place), Award("alice", place), Award("alice", place)));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void NewlyAwardedBadgeAnnouncesEachRecipientOnce(int? place)
    {
        var existingAward = Award("carol", label: "Podcast");
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(existingAward),
            CreateAwardRowList(existingAward, Award("bob", place), Award("alice", place), Award("bob", place)));

        Assert.Collection(events,
            item => AssertAwardEvent(item, "alice", place),
            item => AssertAwardEvent(item, "bob", place));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void LosingAllOwnersDoesNotInventAWinner(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("alice", place), Award("bob", place)),
            CreateAwardRowList());

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void SharedAwardRemainingSharedDoesNotReannounceExistingOwners(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("alice", place), Award("bob", place), Award("carol", place)),
            CreateAwardRowList(Award("alice", place), Award("bob", place)));

        Assert.Empty(events);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void JoiningSharedOwnershipDoesNotClaimToReplaceExistingOwners(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("alice", place)),
            CreateAwardRowList(Award("alice", place), Award("carol", place), Award("bob", place)));

        Assert.Collection(events,
            item => AssertAwardEvent(item, "bob", place),
            item => AssertAwardEvent(item, "carol", place));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void SharedAwardBecomingSoloNamesTheDepartingOwners(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("carol", place), Award("alice", place), Award("bob", place)),
            CreateAwardRowList(Award("alice", place)));

        AssertAwardEvent(Assert.Single(events), "alice", place, solo: true, previousOwners: ["bob", "carol"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void NewOwnerTakingOverAnEntireGroupNamesAllPreviousOwners(int? place)
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("carol", place), Award("bob", place)),
            CreateAwardRowList(Award("alice", place)));

        AssertAwardEvent(Assert.Single(events), "alice", place, previousOwners: ["bob", "carol"]);
    }

    [Fact]
    public void UnrankedAwardTakeoverNamesTheSinglePreviousOwner()
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("bob")),
            CreateAwardRowList(Award("alice")));

        AssertAwardEvent(Assert.Single(events), "alice", previousOwner: "bob");
    }

    [Fact]
    public void RankedPromotionsDoNotAnnounceDemotionsOrDescribeAPromotedAthleteAsReplaced()
    {
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("alice", 1), Award("bob", 2), Award("carol", 3)),
            CreateAwardRowList(Award("bob", 1), Award("carol", 2), Award("alice", 3)));

        Assert.Collection(events.OrderBy(item => GetProperty(item, "Place")),
            item => AssertAwardEvent(item, "bob", 1, previousOwner: "alice"),
            item => AssertAwardEvent(item, "carol", 2));
    }

    [Theory]
    [InlineData("Pheno Age best improvement", "Global", null)]
    [InlineData("Age reduction", "Generation", "Gen X")]
    [InlineData("Age reduction", "Generation", "Millennials")]
    public void AwardsInOtherBadgesOrLeaguesDoNotSuppressANewPlacement(
        string label, string category, string? value)
    {
        var existingAward = Award("alice", 1, category: "Generation", value: "Gen Z");
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(existingAward, Award("bob", 1, label, category, value)),
            CreateAwardRowList(existingAward, Award("alice", 1, label, category, value)));

        AssertAwardEvent(Assert.Single(events), "alice", 1, previousOwner: "bob",
            label: label, category: category, value: value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1)]
    public void AwardResultDateTakesPriorityOverSnapshotRecalculationTime(int? place)
    {
        var resultDate = SnapshotTimeUtc.AddDays(-7);
        var events = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(Award("bob", place)),
            CreateAwardRowList(Award("alice", place, occurredAtUtc: resultDate)));

        AssertAwardEvent(Assert.Single(events), "alice", place, previousOwner: "bob", occurredAtUtc: resultDate);
    }

    [Theory]
    [InlineData(true, "slug[alice] badge[Age reduction] cat[Global] val[] place[1] solo[1] prevs[bob,carol]")]
    [InlineData(false, "slug[alice] badge[Age reduction] cat[Global] val[] place[1] prevs[bob,carol]")]
    public void GroupTransitionsPersistAndReloadWithoutDuplicatingReplay(bool wasSharedOwner, string expectedText)
    {
        using var factory = new TestWebApplicationFactory();
        var service = factory.Services.GetRequiredService<EventDataService>();
        var db = factory.Services.GetRequiredService<DatabaseManager>();
        var before = new List<object> { Award("carol", 1), Award("bob", 1) };
        if (wasSharedOwner)
            before.Add(Award("alice", 1));

        var changes = BuildBadgeAwardEventsForCurrentSnapshotChange(
            CreateAwardRowList(before.ToArray()),
            CreateAwardRowList(Award("alice", 1)));
        var payload = changes.Select(item => (
            AthleteSlug: GetStringProperty(item, "AthleteSlug")!,
            OccurredAtUtc: Assert.IsType<DateTime>(GetProperty(item, "OccurredAtUtc")),
            BadgeLabel: GetStringProperty(item, "BadgeLabel")!,
            LeagueCategory: GetStringProperty(item, "LeagueCategory")!,
            LeagueValue: GetStringProperty(item, "LeagueValue"),
            Place: (int?)GetProperty(item, "Place"),
            BecameSoloOwner: Assert.IsType<bool>(GetProperty(item, "BecameSoloOwner")),
            ReplacedSlug: GetStringProperty(item, "ReplacedSlug"),
            ReplacedSlugs: (IReadOnlyList<string>?)GetProperty(item, "ReplacedSlugs"))).ToArray();

        service.CreateBadgeAwardEvents(payload);
        service.CreateBadgeAwardEvents(payload);

        var savedCount = db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Events WHERE Type=@type AND Text=@text;";
            command.Parameters.AddWithValue("@type", (int)EventType.BadgeAward);
            command.Parameters.AddWithValue("@text", expectedText);
            return Convert.ToInt32(command.ExecuteScalar());
        });
        Assert.Equal(1, savedCount);
        var published = Assert.Single(service.GetEvents(), item => item.Text == expectedText);
        Assert.Equal(EventType.BadgeAward, published.Type);
        Assert.Equal(SnapshotTimeUtc, published.OccurredAtUtc);
        Assert.True(published.VisibleOnWebsite);
    }

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
        var result = method.Invoke(null, [before, after, SnapshotTimeUtc, proSlugs]);
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
        string athleteSlug,
        DateTime? occurredAtUtc = null)
    {
        var row = Activator.CreateInstance(rowType, nonPublic: true);
        Assert.NotNull(row);

        rowType.GetProperty("BadgeLabel")!.SetValue(row, badgeLabel);
        rowType.GetProperty("LeagueCategory")!.SetValue(row, leagueCategory);
        rowType.GetProperty("LeagueValue")!.SetValue(row, leagueValue);
        rowType.GetProperty("Place")!.SetValue(row, place);
        rowType.GetProperty("AthleteSlug")!.SetValue(row, athleteSlug);
        rowType.GetProperty("DefinitionHash")!.SetValue(row, null);
        rowType.GetProperty("OccurredAtUtc")!.SetValue(row, occurredAtUtc);

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

    private static object Award(
        string slug,
        int? place = null,
        string label = "Age reduction",
        string category = "Global",
        string? value = null,
        DateTime? occurredAtUtc = null)
        => CreateAwardRow(GetAwardRowType(), label, category, value, place, slug, occurredAtUtc);

    private static void AssertAwardEvent(
        object item,
        string slug,
        int? place = null,
        bool solo = false,
        string? previousOwner = null,
        string[]? previousOwners = null,
        string label = "Age reduction",
        string category = "Global",
        string? value = null,
        DateTime? occurredAtUtc = null)
    {
        Assert.Equal(slug, GetStringProperty(item, "AthleteSlug"));
        Assert.Equal(label, GetStringProperty(item, "BadgeLabel"));
        Assert.Equal(category, GetStringProperty(item, "LeagueCategory"));
        Assert.Equal(value, GetStringProperty(item, "LeagueValue"));
        Assert.Equal(place, (int?)GetProperty(item, "Place"));
        Assert.Equal(solo, GetProperty(item, "BecameSoloOwner"));
        Assert.Equal(previousOwner, GetStringProperty(item, "ReplacedSlug"));
        Assert.Equal(previousOwners, (IReadOnlyList<string>?)GetProperty(item, "ReplacedSlugs"));
        Assert.Equal(occurredAtUtc ?? SnapshotTimeUtc, GetProperty(item, "OccurredAtUtc"));
    }

    private static string? GetStringProperty(object item, string propertyName)
        => GetProperty(item, propertyName) as string;

    private static object? GetProperty(object item, string propertyName)
        => item.GetType().GetProperty(propertyName)!.GetValue(item);
}
