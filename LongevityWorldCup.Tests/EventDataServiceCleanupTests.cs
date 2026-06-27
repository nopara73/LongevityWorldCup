using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventDataServiceCleanupTests
{
    [Fact]
    public void CleanupEventsForMissingAthletes_HidesOnlyAthleteEventsWithMissingSlugReferences()
    {
        using var factory = new TestWebApplicationFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var db = factory.Services.GetRequiredService<DatabaseManager>();

        db.Run(sqlite =>
        {
            using var clear = sqlite.CreateCommand();
            clear.CommandText = "DELETE FROM Events;";
            clear.ExecuteNonQuery();

            InsertEvent(sqlite, "valid-rank", EventType.NewRank, "slug[alice] rank[1] prev[bob]");
            InsertEvent(sqlite, "missing-primary", EventType.NewRank, "slug[ghost] rank[1]");
            InsertEvent(sqlite, "missing-prev", EventType.BadgeAward, "slug[alice] badge[Test] cat[Global] val[] place[1] prev[ghost]");
            InsertEvent(sqlite, "custom", EventType.CustomEvent, "slug[ghost]\n\nEditorial note");
            InsertEvent(sqlite, "milestone", EventType.AthleteCountMilestone, "athletes[100]");
            InsertEvent(sqlite, "challenge", EventType.LongevitymaxxingChallengeResult, "challenge[longevitymaxxing] pid[p1] name[Former athlete] place[1] checkedIn[14] points[120] days[14]");
        });

        var hidden = events.CleanupEventsForMissingAthletes(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alice",
            "bob",
            "charlie",
            "dana",
            "eve",
            "frank",
            "grace",
            "heidi",
            "ivan",
            "judy"
        });

        Assert.Equal(2, hidden);
        var visibleIds = events.GetEvents(visibleOnWebsite: true).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("valid-rank", visibleIds);
        Assert.Contains("custom", visibleIds);
        Assert.Contains("milestone", visibleIds);
        Assert.Contains("challenge", visibleIds);
        Assert.DoesNotContain("missing-primary", visibleIds);
        Assert.DoesNotContain("missing-prev", visibleIds);

        var allIds = events.GetEvents().Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("missing-primary", allIds);
        Assert.Contains("missing-prev", allIds);
    }

    [Fact]
    public void CleanupEventsForMissingAthletes_SkipsWhenActiveSlugSetLooksInvalid()
    {
        using var factory = new TestWebApplicationFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var db = factory.Services.GetRequiredService<DatabaseManager>();

        db.Run(sqlite =>
        {
            using var clear = sqlite.CreateCommand();
            clear.CommandText = "DELETE FROM Events;";
            clear.ExecuteNonQuery();

            InsertEvent(sqlite, "would-be-missing", EventType.NewRank, "slug[alice] rank[1]");
        });

        var hidden = events.CleanupEventsForMissingAthletes(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(0, hidden);
        Assert.Contains("would-be-missing", events.GetEvents(visibleOnWebsite: true).Select(e => e.Id));
    }

    [Fact]
    public void CleanupAmateurAgeReductionGraduationLinks_RemovesOnlyCurrentProPreviousLinks()
    {
        using var factory = new TestWebApplicationFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var db = factory.Services.GetRequiredService<DatabaseManager>();

        db.Run(sqlite =>
        {
            using var clear = sqlite.CreateCommand();
            clear.CommandText = "DELETE FROM Events;";
            clear.ExecuteNonQuery();

            InsertEvent(sqlite, "wen-pro", EventType.BecamePro, "slug[wen_z]");
            InsertEvent(sqlite, "graduation", EventType.BadgeAward, "slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prev[wen_z]");
            InsertEvent(sqlite, "normal", EventType.BadgeAward, "slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prev[alice]");
            InsertEvent(sqlite, "global", EventType.BadgeAward, "slug[philipp_schmeing] badge[Age reduction] cat[Global] val[] place[1] prev[wen_z]");
            InsertEvent(sqlite, "multi", EventType.BadgeAward, "slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prevs[wen_z,alice]");
            InsertEvent(sqlite, "historical", EventType.BadgeAward, "slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prev[wen_z]", DateTime.UtcNow.AddDays(-1));
        });

        var updated = events.CleanupAmateurAgeReductionGraduationLinks(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "wen-z"
        });

        Assert.Equal(2, updated);
        Assert.Equal("slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1]", ReadEventText(db, "graduation"));
        Assert.Equal("slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prev[alice]", ReadEventText(db, "normal"));
        Assert.Equal("slug[philipp_schmeing] badge[Age reduction] cat[Global] val[] place[1] prev[wen_z]", ReadEventText(db, "global"));
        Assert.Equal("slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prevs[alice]", ReadEventText(db, "multi"));
        Assert.Equal("slug[philipp_schmeing] badge[Age reduction] cat[Amateur] val[Amateur] place[1] prev[wen_z]", ReadEventText(db, "historical"));
    }

    [Fact]
    public void ExtractReferencedAthleteSlugs_NormalizesPrimaryAndPreviousSlugTokens()
    {
        var slugs = EventDataService.ExtractReferencedAthleteSlugs("slug[alice-smith] rank[1] prev[bob] prevs[charlie-one,dana]")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "alice_smith",
            "bob",
            "charlie_one",
            "dana"
        }, slugs);
    }

    private static string ReadEventText(DatabaseManager db, string id)
    {
        return db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Text FROM Events WHERE Id=@id;";
            cmd.Parameters.AddWithValue("@id", id);
            return Assert.IsType<string>(cmd.ExecuteScalar());
        });
    }

    private static void InsertEvent(Microsoft.Data.Sqlite.SqliteConnection sqlite, string id, EventType type, string text, DateTime? occurredAtUtc = null)
    {
        using var insert = sqlite.CreateCommand();
        insert.CommandText =
            """
            INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance)
            VALUES (@id, @type, @text, @occurredAt, @relevance);
            """;
        insert.Parameters.AddWithValue("@id", id);
        insert.Parameters.AddWithValue("@type", (int)type);
        insert.Parameters.AddWithValue("@text", text);
        insert.Parameters.AddWithValue("@occurredAt", (occurredAtUtc ?? DateTime.UtcNow).ToString("o"));
        insert.Parameters.AddWithValue("@relevance", 5d);
        insert.ExecuteNonQuery();
    }
}
