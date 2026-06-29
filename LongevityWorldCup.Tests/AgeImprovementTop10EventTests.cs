using System.Net;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AgeImprovementTop10EventTests
{
    [Fact]
    public void StartupEmitsPhenoImprovementTop10ChangeWhenChangedAthleteEntersAtFirstPlace()
    {
        using var fixture = StartupFixture.Create();

        var texts = fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Text FROM Events WHERE Type=@type ORDER BY OccurredAt ASC;";
            cmd.Parameters.AddWithValue("@type", (int)EventType.AgeImprovementTop10Change);

            var result = new List<string>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(reader.GetString(0));
            return result;
        });

        var text = Assert.Single(texts);
        Assert.Contains("slug[majoros_gabor]", text);
        Assert.Contains("clock[pheno]", text);
        Assert.Contains("place[1]", text);
        Assert.Contains("prev[nopara73]", text);
        Assert.DoesNotContain("prevPlace[", text);
    }

    private sealed class StartupFixture : IDisposable
    {
        private readonly string _root;
        private readonly SlackEventService _slackEvents;

        private StartupFixture(string root, DatabaseManager database, EventDataService events, AthleteDataService athletes, SlackEventService slackEvents)
        {
            _root = root;
            Database = database;
            Events = events;
            Athletes = athletes;
            _slackEvents = slackEvents;
        }

        public DatabaseManager Database { get; }
        public EventDataService Events { get; }
        public AthleteDataService Athletes { get; }

        public static StartupFixture Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-age-improvement-events-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "athletes"));
            Directory.CreateDirectory(Path.Combine(root, "generated", "thumbs", "athletes"));

            WriteAthleteJson(root, "majoros_gabor", MajorosGaborJson);
            WriteAthleteJson(root, "nopara73", Nopara73Json);

            var env = new TestWebHostEnvironment(root);
            var database = new DatabaseManager(dbPath: Path.Combine(root, "test.db"));
            SeedStoredPlacements(database);

            var config = new Config().UseFilePathsForTesting(
                Path.Combine(root, "config.json"),
                Path.Combine(root, "runtime-config.json"));
            var appConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EnableEventDispatch"] = "false",
                    ["EnableXDevPreviewBrowser"] = "false"
                })
                .Build();

            var customImages = new CustomEventImageService(env, NullLogger<CustomEventImageService>.Instance);
            var httpFactory = new TestHttpClientFactory(new HttpClient(new StaticHttpHandler()));
            var services = new TestServiceProvider();
            var xClient = new XApiClient(
                new HttpClient(new StaticHttpHandler()),
                config,
                env,
                NullLogger<XApiClient>.Instance,
                new XDevPreviewService(NullLogger<XDevPreviewService>.Instance, httpFactory, appConfig));
            var threadsClient = new ThreadsApiClient(
                new HttpClient(new StaticHttpHandler()),
                config,
                NullLogger<ThreadsApiClient>.Instance);
            var facebookClient = new FacebookApiClient(
                new HttpClient(new StaticHttpHandler()),
                config,
                NullLogger<FacebookApiClient>.Instance);
            var slackEvents = new SlackEventService(
                new SlackWebhookClient(new HttpClient(new StaticHttpHandler()), config, NullLogger<SlackWebhookClient>.Instance),
                NullLogger<SlackEventService>.Instance);
            var events = new EventDataService(
                env,
                slackEvents,
                new XEventService(xClient, NullLogger<XEventService>.Instance, services, customImages),
                new ThreadsEventService(threadsClient, NullLogger<ThreadsEventService>.Instance, services, customImages),
                new FacebookEventService(facebookClient, NullLogger<FacebookEventService>.Instance, customImages),
                database,
                NullLogger<EventDataService>.Instance,
                appConfig);
            var athletes = new AthleteDataService(env, events, database);
            services.Athletes = athletes;

            return new StartupFixture(root, database, events, athletes, slackEvents);
        }

        private static void SeedStoredPlacements(DatabaseManager database)
        {
            var joinedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc).ToString("o");
            database.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    CREATE TABLE Athletes (
                        Key TEXT PRIMARY KEY,
                        AgeGuesses TEXT NOT NULL,
                        JoinedAt TEXT,
                        Placements TEXT NOT NULL DEFAULT '[]',
                        CurrentPlacement INTEGER NULL,
                        LastAgeDiff REAL NULL,
                        TestSig TEXT NULL,
                        HadBortz INTEGER NULL,
                        LastLowestPhenoAge REAL NULL,
                        LastLowestBortzAge REAL NULL,
                        CrowdAgeTop10Placement INTEGER NULL,
                        PhenoImprovementTop10Placement INTEGER NULL,
                        BortzImprovementTop10Placement INTEGER NULL
                    );

                    INSERT INTO Athletes (Key, AgeGuesses, JoinedAt, Placements, TestSig, PhenoImprovementTop10Placement)
                    VALUES
                        ('nopara73', '[]', @joinedAt, '[]', 'old-nopara73-signature', 1),
                        ('majoros_gabor', '[]', @joinedAt, '[]', 'old-majoros-gabor-signature', NULL);
                    """;
                cmd.Parameters.AddWithValue("@joinedAt", joinedAt);
                cmd.ExecuteNonQuery();
            });
        }

        private static void WriteAthleteJson(string root, string slug, string json)
        {
            var athleteDir = Path.Combine(root, "athletes", slug);
            Directory.CreateDirectory(athleteDir);
            File.WriteAllText(Path.Combine(athleteDir, "athlete.json"), json);
        }

        public void Dispose()
        {
            Athletes.Dispose();
            Events.Dispose();
            _slackEvents.Dispose();
            Database.Dispose();
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public AthleteDataService? Athletes { get; set; }
        public object? GetService(Type serviceType) => serviceType == typeof(AthleteDataService) ? Athletes : null;
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StaticHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
        }
    }

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(root);
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }

    private const string MajorosGaborJson =
        """
        {
          "Name": "Majoros Gabor",
          "DateOfBirth": {
            "Year": 1980,
            "Month": 5,
            "Day": 11
          },
          "Biomarkers": [
            {
              "Date": "2025-05-05",
              "AlbGL": 44,
              "CreatUmolL": 101,
              "GluMmolL": 4.6,
              "CrpMgL": 18,
              "LymPc": 20.8,
              "McvFL": 90,
              "RdwPc": 13,
              "AlpUL": 49,
              "Wbc1000cellsuL": 6.2
            },
            {
              "Date": "2026-06-25",
              "AlbGL": 45,
              "CreatUmolL": 95,
              "GluMmolL": 4,
              "CrpMgL": 0.6,
              "LymPc": 35.2,
              "McvFL": 86.5,
              "RdwPc": 12.5,
              "AlpUL": 50,
              "Wbc1000cellsuL": 5.48
            }
          ]
        }
        """;

    private const string Nopara73Json =
        """
        {
          "Name": "nopara73",
          "DateOfBirth": {
            "Year": 1991,
            "Month": 12,
            "Day": 31
          },
          "Biomarkers": [
            {
              "Date": "2025-03-06",
              "AlbGL": 40.99999999999999,
              "CreatUmolL": 88.49557522123894,
              "GluMmolL": 4.906749555950268,
              "CrpMgL": 13.2,
              "LymPc": 26.6,
              "McvFL": 93.9,
              "RdwPc": 13.1,
              "AlpUL": 70.3,
              "Wbc1000cellsuL": 8.8
            },
            {
              "Date": "2025-03-13",
              "AlbGL": 50,
              "CreatUmolL": 88.4,
              "GluMmolL": 5.3,
              "CrpMgL": 3,
              "LymPc": 48,
              "McvFL": 94,
              "RdwPc": 14.3,
              "AlpUL": 60,
              "Wbc1000cellsuL": 5.2
            },
            {
              "Date": "2025-11-04",
              "AlbGL": 45,
              "CreatUmolL": 91,
              "GluMmolL": 4.75,
              "CrpMgL": 1.6,
              "LymPc": 42,
              "McvFL": 87.5,
              "RdwPc": 13.2,
              "AlpUL": 61,
              "Wbc1000cellsuL": 5.78
            }
          ]
        }
        """;
}
