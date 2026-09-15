using LongevityWorldCup.Website.Business;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CrowdAgeTop10EventTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly EventDataService _events;
    private readonly DatabaseManager _database;
    private readonly string _slug = $"crowd_spacing_{Guid.NewGuid():N}";
    private readonly DateTime _now = new(2030, 1, 1, 5, 47, 0, DateTimeKind.Utc);

    public CrowdAgeTop10EventTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        // These synthetic athletes are not part of the host's file-backed directory.
        // Its background reload must not reconcile their image IDs out from under a test.
        _database = new DatabaseManager(dbPath: Path.Combine(
            factory.WorkingDirectory, $"crowd-announcements-{Guid.NewGuid():N}.db"));
        Execute("""
            CREATE TABLE Athletes (Key TEXT PRIMARY KEY, AgeGuesses TEXT NOT NULL, CrowdAgeProfileImageId TEXT);
            INSERT INTO Athletes (Key, AgeGuesses, CrowdAgeProfileImageId) VALUES (@slug, '[]', 'original-image');
            """);
        _events = ActivatorUtilities.CreateInstance<EventDataService>(factory.Services, _database);
    }

    [Fact]
    public void EighthToSixthIsAnnouncedAfterTwentyFourHoursInsteadOfLaterTheSameDay()
    {
        Queue(8, null, _now, age: 41, count: 100);
        var first = Assert.Single(Published());
        Assert.Contains("place[8]", first.Text);
        Assert.Equal(0, PendingCount());

        Queue(6, 8, _now.AddHours(10).AddMinutes(51), age: 40.5, count: 102);
        Assert.Single(Published());
        Assert.Equal(1, PendingCount());
        Assert.DoesNotContain(_events.GetPendingXEvents(), e => e.Text.Contains(_slug) && e.Text.Contains("place[6]"));
        Assert.DoesNotContain(_events.GetPendingThreadsEvents(), e => e.Text.Contains(_slug) && e.Text.Contains("place[6]"));
        Assert.Equal(0, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24).AddTicks(-1)));
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));

        var climb = Assert.Single(Published(), e => e.Id != first.Id);
        Assert.Contains("place[6] prevPlace[8]", climb.Text);
        Assert.Contains("crowdAge[40.5] crowdCount[102]", climb.Text);
        Assert.Contains(_events.GetPendingXEvents(), e => e.Id == climb.Id);
        Assert.Contains(_events.GetPendingThreadsEvents(), e => e.Id == climb.Id);
        Assert.Equal(0, PendingCount());
    }

    [Fact]
    public void CrossingMidnightDoesNotBypassTheCooldown()
    {
        var late = new DateTime(2030, 1, 1, 23, 55, 0, DateTimeKind.Utc);
        Queue(8, null, late);
        Queue(6, 8, late.AddMinutes(10));
        Assert.Equal(0, _events.PublishPendingCrowdAgeAnnouncements(late.AddHours(12)));
        Assert.Single(Published());
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(late.AddHours(24)));
    }

    [Fact]
    public void InterveningGainsBecomeOneStrongestClimbWithTheOriginalMovementContext()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        Queue(4, 6, _now.AddHours(12), age: 39, count: 130);
        Queue(5, 7, _now.AddHours(20)); // A later weaker gain must not replace the stronger draft.
        Assert.Equal(1, PendingCount());
        Assert.Single(Published());
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
        Assert.Equal(2, Published().Count);
        Assert.Contains(Published(), e => e.Text.Contains("place[4] prevPlace[8]") && e.Text.Contains("crowdCount[130]"));
        Assert.Equal(0, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(25)));
    }

    [Fact]
    public void ADelayedPublicationStartsAFreshTwentyFourHourCooldown()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(36)));
        Queue(4, 6, _now.AddHours(37));
        Assert.Equal(0, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(48)));
        Assert.Equal(2, Published().Count);
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(60)));
        Assert.Equal(3, Published().Count);
    }

    [Fact]
    public void PreviouslyUnannouncedPlacesRemainEligibleEvenBelowAPastBest()
    {
        SeedHistoricalEvent(4, _now.AddDays(-2));
        Queue(6, 8, _now);
        Assert.Contains(Published(), e => e.Text.Contains("place[6] prevPlace[8]"));
        Assert.Equal(2, Published().Count);
        Queue(6, 7, _now.AddDays(2));
        Assert.Equal(2, Published().Count);
        Assert.Equal(0, PendingCount());
    }

    [Fact]
    public void AthletesHaveIndependentCooldowns()
    {
        var other = $"{_slug}_other";
        Execute("INSERT INTO Athletes (Key, AgeGuesses, CrowdAgeProfileImageId) VALUES (@slug, '[]', 'other-image');", other);
        try
        {
            Queue(8, null, _now);
            Queue(6, 8, _now.AddHours(1));
            _events.CreateCrowdAgeTop10ChangeEvents(
                new[] { (other, _now.AddHours(2), 7, (int?)null, (string?)null, 40d, 100) }, nowUtc: _now.AddHours(2));
            Assert.Single(_events.GetEvents(EventType.CrowdAgeTop10Change), e => e.Text.Contains($"slug[{other}]"));
            Assert.Single(Published());
            Assert.Equal(1, PendingCount());
        }
        finally
        {
            Cleanup(other);
        }
    }

    [Fact]
    public void PendingClimbsAndPublicationTimesSurviveANewServiceAndDatabaseConnection()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        using var reopenedDatabase = new DatabaseManager(dbPath: _database.DbPath);
        using var restarted = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, reopenedDatabase);
        restarted.CreateCrowdAgeTop10ChangeEvents(
            new[] { (_slug, _now.AddHours(12), 4, (int?)6, (string?)"previous", 39d, 125) }, nowUtc: _now.AddHours(12));
        Assert.Equal(0, restarted.PublishPendingCrowdAgeAnnouncements(_now.AddHours(23)));
        Assert.Equal(1, restarted.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
        Assert.Contains(Published(), e => e.Text.Contains("place[4] prevPlace[8]"));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void ExistingOneHourDraftsMigrateToTheSpacingRuleWithoutBeingLost()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        Execute($"UPDATE PendingCrowdAgeMilestones SET PublishAfterUtc='{_now.AddHours(12):o}' WHERE AthleteSlug=@slug;");
        Execute("DROP TABLE CrowdAgeAnnouncementState;");
        using var restarted = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, _database);
        Assert.Equal(0, restarted.PublishPendingCrowdAgeAnnouncements(_now.AddHours(12)));
        Assert.Equal(1, PendingCount());
        Assert.Equal(1, restarted.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void OldPodiumOnlySkipReasonsAreReopenedWithoutReplayingOtherProcessedPosts()
    {
        SeedHistoricalEvent(6, _now.AddDays(-2));
        Execute("UPDATE Events SET XSkipReason='NonMilestoneCrowdAgeChange', ThreadsSkipReason='NonMilestoneCrowdAgeChange' WHERE Id=@slug;");
        using var restarted = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, _database);
        Assert.Contains(restarted.GetPendingXEvents(), e => e.Id == _slug);
        Assert.Contains(restarted.GetPendingThreadsEvents(), e => e.Id == _slug);
        restarted.MarkEventsXProcessed(new[] { _slug });
        restarted.MarkEventsThreadsProcessed(new[] { _slug });
        using var again = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, _database);
        Assert.DoesNotContain(again.GetPendingXEvents(), e => e.Id == _slug);
        Assert.DoesNotContain(again.GetPendingThreadsEvents(), e => e.Id == _slug);
    }

    [Fact]
    public void ImageChangesDiscardPendingClimbsButKeepPublishedHistory()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        Execute("UPDATE Athletes SET CrowdAgeProfileImageId='new-image' WHERE Key=@slug;");
        Assert.Equal(0, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
        Assert.Equal(0, PendingCount());
        Assert.Single(Published());
        Queue(5, null, _now.AddHours(25));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void FailedPublicationKeepsTheDraftAndDoesNotAdvanceTheCooldown()
    {
        Queue(8, null, _now);
        Queue(6, 8, _now.AddHours(11));
        Execute("CREATE TRIGGER FailCrowdAnnouncement BEFORE INSERT ON Events WHEN NEW.Type=11 BEGIN SELECT RAISE(ABORT, 'Simulated publication failure'); END;");
        try
        {
            Assert.Throws<SqliteException>(() => _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
            Assert.Equal(1, PendingCount());
            Assert.Single(Published());
        }
        finally
        {
            Execute("DROP TRIGGER FailCrowdAnnouncement;");
        }
        Assert.Equal(1, _events.PublishPendingCrowdAgeAnnouncements(_now.AddHours(24)));
        Assert.Equal(2, Published().Count);
        Assert.Equal(0, PendingCount());
    }

    [Theory]
    [InlineData(3, 2, 150)]
    [InlineData(1, 1, 150)]
    [InlineData(11, null, 150)]
    [InlineData(0, null, 150)]
    [InlineData(8, null, 99)]
    public void RegressionsRepeatedPositionsAndUnqualifiedResultsAreNotQueued(int place, int? previousPlace, int count)
    {
        Queue(place, previousPlace, _now, count: count);
        Assert.Equal(0, PendingCount());
        Assert.Empty(Published());
    }

    private void Queue(int place, int? previousPlace, DateTime at, double age = 40, int count = 110) =>
        _events.CreateCrowdAgeTop10ChangeEvents(new[] { (_slug, at, place, previousPlace, (string?)null, age, count) }, nowUtc: at);

    private void SeedHistoricalEvent(int place, DateTime at) => Execute(
        $"INSERT INTO Events (Id, Type, Text, OccurredAt, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed) " +
        $"VALUES (@slug, 11, 'slug[{_slug}] place[{place}] crowdAge[41] crowdCount[100]', '{at:o}', 1, 1, 1, 1);");

    private List<EventItem> Published() => _events.GetEvents(EventType.CrowdAgeTop10Change)
        .Where(e => e.Text.Contains($"slug[{_slug}]", StringComparison.Ordinal)).ToList();

    private long PendingCount() => _database.Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug;";
        command.Parameters.AddWithValue("@slug", _slug);
        return (long)command.ExecuteScalar()!;
    });

    private void Execute(string sql, string? slug = null) => _database.Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@slug", slug ?? _slug);
        command.ExecuteNonQuery();
    });

    private void Cleanup(string slug) => Execute("""
        DELETE FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug;
        DELETE FROM CrowdAgeAnnouncementState WHERE AthleteSlug=@slug;
        DELETE FROM Events WHERE instr(Text, @slug) > 0;
        DELETE FROM Athletes WHERE Key=@slug;
        """, slug);

    public void Dispose()
    {
        _events.Dispose();
        _database.Dispose();
    }
}
