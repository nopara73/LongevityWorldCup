using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AcceptedResultEventTests
{
    private const string Slug = "result_history";
    private static readonly DateTime AcceptedAt = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PublishedNonImprovementCreatesOneHighlightOnReloadAndSurvivesRestart()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteAthlete(Result("2025-01-01", crp: 1));
        string eventId;
        using (var factory = workspace.CreateFactory())
        {
            var athletes = factory.Services.GetRequiredService<AthleteDataService>();
            var events = factory.Services.GetRequiredService<EventDataService>();
            Assert.Empty(events.GetEvents(EventType.TestResultAccepted));

            var reloadCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void ObserveReload()
            {
                if (events.GetEvents(EventType.TestResultAccepted).Count == 1)
                    reloadCompleted.TrySetResult();
            }
            athletes.AthletesChanged += ObserveReload;
            var before = DateTime.UtcNow;
            workspace.WriteAthlete(Result("2025-01-01", crp: 1), Result("2026-08-31", crp: 20));
            try
            {
                await reloadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            }
            finally
            {
                athletes.AthletesChanged -= ObserveReload;
            }

            var highlight = Assert.Single(events.GetEvents(EventType.TestResultAccepted));
            eventId = highlight.Id;
            Assert.Equal($"slug[{Slug}] date[2026-08-31]", highlight.Text);
            Assert.InRange(highlight.OccurredAtUtc, before, DateTime.UtcNow);
            Assert.Empty(events.GetEvents(EventType.BiologicalAgeImproved));
            Assert.Equal(0, events.SyncAcceptedResultEvents(athletes.GetAthletesSnapshot(), AcceptedAt));

            using var client = factory.CreateClient();
            var response = JsonNode.Parse(await client.GetStringAsync("/api/events"))!.AsArray();
            Assert.Single(response.OfType<JsonObject>(), e => e["Type"]!.GetValue<int>() == 13);
            AssertNoSocialDelivery(factory, highlight.Id);
        }

        // Corrections and reordering between deployments are the same two tests.
        workspace.WriteAthlete(Result("2026-08-31", crp: 19), Result("2025-01-01", crp: 1));
        using var restarted = workspace.CreateFactory();
        var restartedEvents = restarted.Services.GetRequiredService<EventDataService>();
        Assert.Equal(eventId, Assert.Single(restartedEvents.GetEvents(EventType.TestResultAccepted)).Id);
    }

    [Fact]
    public void StartupRecognizesEveryNewDateIncludingBackfillsAndNewAthletes()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteAthlete(Result("2025-01-01", crp: 20));
        using (var baseline = workspace.CreateFactory())
            Assert.Empty(baseline.Services.GetRequiredService<EventDataService>().GetEvents(EventType.TestResultAccepted));

        workspace.WriteAthlete(Result("2024-03-06", crp: 20), Result("2025-01-01", crp: 20), Result("2026-08-31", crp: 0.6));
        workspace.WriteAthleteFor("new_athlete", Result("2026-08-31"));
        using (var restarted = workspace.CreateFactory())
        {
            var events = restarted.Services.GetRequiredService<EventDataService>();
            Assert.Equal(3, events.GetEvents(EventType.TestResultAccepted).Count);
            Assert.Contains(events.GetEvents(EventType.TestResultAccepted), e => e.Text == $"slug[{Slug}] date[2024-03-06]");
            Assert.Contains(events.GetEvents(EventType.TestResultAccepted), e => e.Text == "slug[new_athlete] date[2026-08-31]");
            Assert.Contains(events.GetEvents(EventType.BiologicalAgeImproved), e => e.Text.StartsWith($"slug[{Slug}] clock[pheno]", StringComparison.Ordinal));
        }

        using var again = workspace.CreateFactory();
        Assert.Equal(3, again.Services.GetRequiredService<EventDataService>().GetEvents(EventType.TestResultAccepted).Count);
    }

    [Fact]
    public void SameDatePanelsAndCorrectionsDoNotRepeatAnEventEvenAfterRemovalAndRestoration()
    {
        using var workspace = new TestWorkspace();
        workspace.WriteAthlete(Result("2025-01-01"));
        using var factory = workspace.CreateFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();

        var partial = JsonNode.Parse("""{"Date":"2026-8-31","GluMmolL":4.7}""")!.AsObject();
        var snapshot = Snapshot(partial, partial.DeepClone().AsObject());
        Assert.Equal(1, events.SyncAcceptedResultEvents(snapshot, AcceptedAt));
        var highlight = Assert.Single(events.GetEvents(EventType.TestResultAccepted));
        Assert.Equal(AcceptedAt, highlight.OccurredAtUtc);

        snapshot = Snapshot(Result("2026-08-31", crp: 5), Result("2026-08-31", crp: 2));
        Assert.Equal(0, events.SyncAcceptedResultEvents(snapshot, AcceptedAt.AddDays(1)));
        Assert.Equal(0, events.SyncAcceptedResultEvents(Snapshot(), AcceptedAt.AddDays(2)));
        Assert.Equal(0, events.SyncAcceptedResultEvents(snapshot, AcceptedAt.AddDays(3)));
        Assert.Equal(highlight, Assert.Single(events.GetEvents(EventType.TestResultAccepted)));
    }

    [Fact]
    public void MissingInvalidAndEmptyTestRecordsDoNotCreateHighlights()
    {
        using var workspace = new TestWorkspace();
        using var factory = workspace.CreateFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var invalid = new[]
        {
            "{}", "{\"Date\":\"2026-08-31\"}", "{\"GluMmolL\":4.7}",
            "{\"Date\":\"not a date\",\"GluMmolL\":4.7}",
            "{\"Date\":42,\"GluMmolL\":4.7}", "{\"Date\":\"2026-08-31\",\"GluMmolL\":null}"
        }.Select(json => JsonNode.Parse(json)!.AsObject()).ToArray();
        Assert.Equal(0, events.SyncAcceptedResultEvents(Snapshot(invalid), AcceptedAt));
        Assert.Empty(events.GetEvents(EventType.TestResultAccepted));
    }

    [Fact]
    public void FailedEventInsertRollsBackTheAcceptedResultSoRetryCanFinish()
    {
        using var workspace = new TestWorkspace();
        using var factory = workspace.CreateFactory();
        var events = factory.Services.GetRequiredService<EventDataService>();
        var database = factory.Services.GetRequiredService<DatabaseManager>();
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER FailAcceptedEvent BEFORE INSERT ON Events WHEN NEW.Type = 13
                BEGIN SELECT RAISE(ABORT, 'Simulated event write failure'); END;
                """;
            command.ExecuteNonQuery();
        });

        var snapshot = Snapshot(Result("2026-08-31"));
        Assert.Throws<SqliteException>(() => events.SyncAcceptedResultEvents(snapshot, AcceptedAt));
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM AcceptedAthleteResults;";
            Assert.Equal(0L, command.ExecuteScalar());
            command.CommandText = "DROP TRIGGER FailAcceptedEvent;";
            command.ExecuteNonQuery();
        });
        Assert.Equal(1, events.SyncAcceptedResultEvents(snapshot, AcceptedAt));
        Assert.Single(events.GetEvents(EventType.TestResultAccepted));
    }

    private static void AssertNoSocialDelivery(TestWebApplicationFactory factory, string eventId)
    {
        factory.Services.GetRequiredService<DatabaseManager>().Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT VisibleOnWebsite, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed FROM Events WHERE Id=@id;";
            command.Parameters.AddWithValue("@id", eventId);
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            for (var i = 0; i < 5; i++)
                Assert.Equal(1, reader.GetInt32(i));
        });
    }

    private static JsonArray Snapshot(params JsonObject[] results) => new(new JsonObject
    {
        ["AthleteSlug"] = Slug,
        ["Biomarkers"] = new JsonArray(results.Select(result => result.DeepClone()).ToArray())
    });

    private static JsonObject Result(string date, double crp = 2) => JsonNode.Parse(
        $$"""
        {"Date":"{{date}}","AlbGL":45,"CreatUmolL":95,"GluMmolL":4,"CrpMgL":{{crp.ToString(CultureInfo.InvariantCulture)}},
         "LymPc":35.2,"McvFL":86.5,"RdwPc":12.5,"AlpUL":50,"Wbc1000cellsuL":5.48}
        """)!.AsObject();

    private sealed class TestWorkspace : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lwc-accepted-results-" + Guid.NewGuid().ToString("N"));
        private string WebRoot => Path.Combine(_root, "wwwroot");
        private string DatabasePath => Path.Combine(_root, "test.db");

        public TestWorkspace() => WriteAthlete();

        public void WriteAthlete(params JsonObject[] results) => WriteAthleteFor(Slug, results);

        public void WriteAthleteFor(string slug, params JsonObject[] results)
        {
            var directory = Path.Combine(WebRoot, "athletes", slug);
            Directory.CreateDirectory(directory);
            var athlete = Snapshot(results)[0]!.DeepClone().AsObject();
            athlete["Name"] = slug;
            athlete["DateOfBirth"] = new JsonObject { ["Year"] = 1980, ["Month"] = 1, ["Day"] = 2 };
            athlete["Division"] = "Open";
            athlete["Flag"] = "Earth";
            File.WriteAllText(Path.Combine(directory, "athlete.json"), athlete.ToJsonString());
        }

        public TestWebApplicationFactory CreateFactory() => new(builder =>
        {
            builder.UseWebRoot(WebRoot);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DatabaseManager>();
                services.AddSingleton(_ => new DatabaseManager(dbPath: DatabasePath));
            });
        });

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
