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
    private readonly string _slug = $"crowd_milestone_{Guid.NewGuid():N}";
    private readonly DateTime _now = DateTime.UtcNow.AddDays(1);

    public CrowdAgeTop10EventTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _events = factory.Services.GetRequiredService<EventDataService>();
        _database = factory.Services.GetRequiredService<DatabaseManager>();
        Execute("INSERT INTO Athletes (Key, AgeGuesses, CrowdAgeProfileImageId) VALUES (@slug, '[]', 'original-image');");
    }

    [Fact]
    public void Top10EntryPublishesOnceAndTwoMoreGuessesDoNotCreateAnotherAnnouncement()
    {
        Queue(8, null, _now, age: 41, count: 100);
        AssertNoPublishedMilestones();
        Assert.Equal(0, _events.PublishPendingCrowdAgeMilestones(_now.AddMinutes(59)));
        Assert.Equal(1, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));

        Queue(6, 8, _now.AddHours(11), age: 40.5, count: 102);
        Queue(4, 6, _now.AddHours(12));
        Queue(7, null, _now.AddDays(1)); // Leaving and re-entering is not another debut.
        Assert.Equal(0, _events.PublishPendingCrowdAgeMilestones(_now.AddDays(2)));
        var milestone = Assert.Single(Published());
        Assert.Contains("place[8]", milestone.Text);
        Assert.Contains("crowdCount[100]", milestone.Text);
        Assert.Equal(0, PendingCount());
    }

    [Fact]
    public void NearbyMilestonesPublishOnlyTheStrongestAndKeepTheOriginalEntryContext()
    {
        Queue(8, null, _now);
        Queue(3, 8, _now.AddMinutes(20));
        Queue(2, 3, _now.AddMinutes(50), age: 38.5, count: 130);
        Assert.Equal(1, PendingCount());
        AssertNoPublishedMilestones();

        Assert.Equal(1, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));
        var milestone = Assert.Single(Published());
        Assert.Contains("place[2]", milestone.Text);
        Assert.DoesNotContain("prevPlace[", milestone.Text);
        Assert.Contains("crowdAge[38.5] crowdCount[130]", milestone.Text);
        Assert.Equal(_now.AddMinutes(50), milestone.OccurredAtUtc);
        Assert.Contains(_events.GetPendingXEvents(), e => e.Id == milestone.Id);
        Assert.Contains(_events.GetPendingThreadsEvents(), e => e.Id == milestone.Id);
        Assert.Equal(0, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(2)));

        Queue(3, 4, _now.AddHours(3)); // A skipped weaker milestone cannot appear later.
        Assert.Equal(0, PendingCount());
    }

    [Fact]
    public void EachNewBestPodiumPositionCanPublishAndRepeatedPositionsCannot()
    {
        SeedHistoricalEvent(8);
        Queue(3, 8, _now);
        Queue(2, 3, _now.AddHours(2)); // Publish the expired window before starting another.
        Queue(1, 2, _now.AddHours(4));
        _events.PublishPendingCrowdAgeMilestones(_now.AddHours(5));
        Assert.Equal(4, Published().Count);
        Assert.Contains(Published(), e => e.Text.Contains("place[3] prevPlace[8]"));
        Assert.Contains(Published(), e => e.Text.Contains("place[2] prevPlace[3]"));
        Assert.Contains(Published(), e => e.Text.Contains("place[1] prevPlace[2]"));

        Queue(3, 4, _now.AddHours(6));
        Queue(2, 3, _now.AddHours(7));
        Queue(1, 2, _now.AddHours(8));
        Assert.Equal(0, PendingCount());
        Assert.Equal(4, Published().Count);
    }

    [Fact]
    public void CombinedPodiumClimbKeepsThePositionBeforeTheWindow()
    {
        SeedHistoricalEvent(8);
        Queue(3, 8, _now);
        Queue(1, 3, _now.AddMinutes(30));
        _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1));
        Assert.Contains(Published(), e => e.Text.Contains("place[1] prevPlace[8]"));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void QueueAndPreviouslyReachedMilestonesSurviveANewServiceAndDatabaseConnection()
    {
        Queue(8, null, _now);
        using var reopenedDatabase = new DatabaseManager(dbPath: _database.DbPath);
        using var restarted = ActivatorUtilities.CreateInstance<EventDataService>(_factory.Services, reopenedDatabase);
        restarted.CreateCrowdAgeTop10ChangeEvents(new[] { (_slug, _now.AddMinutes(30), 3, (int?)8, (string?)"previous", 39d, 125) });
        Assert.Equal(1, restarted.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));
        Assert.Contains("place[3]", Assert.Single(Published()).Text);

        restarted.CreateCrowdAgeTop10ChangeEvents(new[] { (_slug, _now.AddHours(2), 8, (int?)null, (string?)"previous", 40d, 150) });
        Assert.Equal(0, PendingCount());
    }

    [Fact]
    public void ImageChangesDiscardUnpublishedMilestonesButKeepPublishedHistory()
    {
        SeedHistoricalEvent(8);
        Queue(3, 8, _now);
        Execute("UPDATE Athletes SET CrowdAgeProfileImageId='new-image' WHERE Key=@slug;");
        Assert.Equal(0, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));
        Assert.Equal(0, PendingCount());
        Assert.Single(Published());

        Queue(3, null, _now.AddHours(2));
        Assert.Equal(1, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(3)));
        Assert.Equal(2, Published().Count);
    }

    [Fact]
    public void FailedPublicationKeepsThePendingMilestoneForRetry()
    {
        Queue(8, null, _now);
        Execute("CREATE TRIGGER FailCrowdMilestone BEFORE INSERT ON Events WHEN NEW.Type=11 BEGIN SELECT RAISE(ABORT, 'Simulated publication failure'); END;");
        try
        {
            Assert.Throws<SqliteException>(() => _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));
            Assert.Equal(1, PendingCount());
            AssertNoPublishedMilestones();
        }
        finally
        {
            Execute("DROP TRIGGER FailCrowdMilestone;");
        }

        Assert.Equal(1, _events.PublishPendingCrowdAgeMilestones(_now.AddHours(1)));
        Assert.Single(Published());
        Assert.Equal(0, PendingCount());
    }

    [Theory]
    [InlineData(6, 8, 102)]
    [InlineData(4, 5, 120)]
    [InlineData(3, 2, 150)]
    [InlineData(1, 1, 150)]
    [InlineData(11, null, 150)]
    [InlineData(0, null, 150)]
    [InlineData(8, null, 99)]
    public void RoutineMovementRegressionsAndUnqualifiedResultsAreNotQueued(int place, int? previousPlace, int count)
    {
        Queue(place, previousPlace, _now, count: count);
        Assert.Equal(0, PendingCount());
        AssertNoPublishedMilestones();
    }

    private void Queue(int place, int? previousPlace, DateTime at, double age = 40, int count = 110) =>
        _events.CreateCrowdAgeTop10ChangeEvents(new[] { (_slug, at, place, previousPlace, (string?)null, age, count) });

    private void SeedHistoricalEvent(int place) => Execute(
        $"INSERT INTO Events (Id, Type, Text, OccurredAt, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed) " +
        $"VALUES (@slug, 11, 'slug[{_slug}] place[{place}] crowdAge[41] crowdCount[100]', '{_now.AddDays(-1):o}', 1, 1, 1, 1);");

    private List<EventItem> Published() => _events.GetEvents(EventType.CrowdAgeTop10Change)
        .Where(e => e.Text.Contains($"slug[{_slug}]", StringComparison.Ordinal)).ToList();

    private void AssertNoPublishedMilestones()
    {
        Assert.Empty(Published());
        Assert.DoesNotContain(_events.GetPendingXEvents(), e => e.Text.Contains(_slug));
        Assert.DoesNotContain(_events.GetPendingThreadsEvents(), e => e.Text.Contains(_slug));
        Assert.DoesNotContain(_events.Events, e => e!["Text"]!.GetValue<string>().Contains(_slug));
    }

    private long PendingCount() => _database.Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug;";
        command.Parameters.AddWithValue("@slug", _slug);
        return (long)command.ExecuteScalar()!;
    });

    private void Execute(string sql) => _database.Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("@slug", _slug);
        command.ExecuteNonQuery();
    });

    public void Dispose() => Execute("""
        DELETE FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug;
        DELETE FROM Events WHERE instr(Text, @slug) > 0;
        DELETE FROM Athletes WHERE Key=@slug;
        """);
}
