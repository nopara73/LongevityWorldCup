using System.Net;
using System.Reflection;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialJobIntegrationTests
{
    [Fact]
    public async Task FacebookJob_MarksNonCustomRowsSkippedBeforePostingCustomEvent()
    {
        using var fixture = SocialJobFixture.Create(facebookSendSucceeds: true);
        var rankId = fixture.InsertEvent(EventType.NewRank, "slug[alice] rank[1]", DateTime.UtcNow, facebookProcessed: 0);
        var badgeId = fixture.InsertEvent(EventType.BadgeAward, "slug[alice] badge[Crowd Age - lowest] cat[Global] val[] place[1]", DateTime.UtcNow.AddMinutes(-1), facebookProcessed: 0);
        var customId = fixture.InsertEvent(
            EventType.CustomEvent,
            "Announcement\n\nThis should still publish after unsupported Facebook rows are cleared.",
            DateTime.UtcNow.AddMinutes(-2),
            xProcessed: 1,
            threadsProcessed: 1,
            facebookProcessed: 0,
            facebookSkipReason: SocialEventSkipReason.EmptyMessage.ToString());

        await fixture.CreateFacebookJob().Execute(TestJobExecutionContext.Now());

        Assert.Equal((1, SocialEventSkipReason.FacebookSupportsCustomEventsOnly.ToString()), fixture.ReadPlatformState(rankId, "Facebook"));
        Assert.Equal((1, SocialEventSkipReason.FacebookSupportsCustomEventsOnly.ToString()), fixture.ReadPlatformState(badgeId, "Facebook"));
        Assert.Equal((1, null), fixture.ReadPlatformState(customId, "Facebook"));
        Assert.Single(fixture.FacebookRequests);
    }

    [Fact]
    public async Task XJob_MarksStalePrimaryEventSkipped()
    {
        using var fixture = SocialJobFixture.Create(xSendSucceeds: true);
        var eventId = fixture.InsertEvent(
            EventType.NewRank,
            "slug[alice] rank[1]",
            DateTime.UtcNow.AddDays(-8),
            xProcessed: 0);

        await fixture.CreateXJob().Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal((1, SocialEventSkipReason.StalePrimaryEvent.ToString()), fixture.ReadPlatformState(eventId, "X"));
    }

    [Fact]
    public async Task ThreadsJob_MarksUnsupportedBadgeSkipped()
    {
        using var fixture = SocialJobFixture.Create();
        var eventId = fixture.InsertEvent(
            EventType.BadgeAward,
            "slug[alice] badge[Crowd Age - lowest] cat[Global] val[] place[1]",
            DateTime.UtcNow,
            threadsProcessed: 0);

        await fixture.CreateThreadsJob().Execute(TestJobExecutionContext.Now());

        Assert.Equal((1, SocialEventSkipReason.UnsupportedBadgeAward.ToString()), fixture.ReadPlatformState(eventId, "Threads"));
        Assert.Empty(fixture.ThreadsRequests);
    }

    [Fact]
    public async Task XJob_SubjectCooldownLeavesEventUnprocessed()
    {
        using var fixture = SocialJobFixture.Create(xSendSucceeds: true);
        fixture.XFillerLog.LogSubjectPost(DateTime.UtcNow.AddHours(-1), "event[seed]", "alice");
        var eventId = fixture.InsertEvent(
            EventType.NewRank,
            "slug[alice] rank[1]",
            DateTime.UtcNow,
            xProcessed: 0);

        await fixture.CreateXJob().Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal((0, null), fixture.ReadPlatformState(eventId, "X"));
    }

    [Fact]
    public async Task FacebookJob_SendFailureLeavesCustomEventRetryable()
    {
        using var fixture = SocialJobFixture.Create(facebookSendSucceeds: false);
        var eventId = fixture.InsertEvent(
            EventType.CustomEvent,
            "Retry me\n\nFacebook should leave this pending when the API send fails.",
            DateTime.UtcNow,
            xProcessed: 1,
            threadsProcessed: 1,
            facebookProcessed: 0);

        await fixture.CreateFacebookJob().Execute(TestJobExecutionContext.Now());

        Assert.Equal((0, null), fixture.ReadPlatformState(eventId, "Facebook"));
        Assert.Equal(2, fixture.FacebookRequests.Count);
    }

    [Fact]
    public async Task FacebookJob_CustomEventClaimPreventsImmediateDispatchDuplicate()
    {
        SocialJobFixture? callbackFixture = null;
        var immediateDispatchTriggered = false;
        using var fixture = SocialJobFixture.Create(
            facebookSendSucceeds: true,
            onFacebookRequest: () =>
            {
                if (immediateDispatchTriggered)
                    return;

                immediateDispatchTriggered = true;
                callbackFixture!.ProcessPendingImmediateCustomEvents();
            });
        callbackFixture = fixture;
        var eventId = fixture.InsertEvent(
            EventType.CustomEvent,
            "Race test\n\nOnly one Facebook request should be made.",
            DateTime.UtcNow,
            slackProcessed: 1,
            xProcessed: 1,
            threadsProcessed: 1,
            facebookProcessed: 0);

        await fixture.CreateFacebookJob().Execute(TestJobExecutionContext.Now());

        Assert.True(immediateDispatchTriggered);
        Assert.Equal((1, null), fixture.ReadPlatformState(eventId, "Facebook"));
        Assert.Single(fixture.FacebookRequests);
    }

    [Fact]
    public void ImmediateCustomDispatch_SuccessClearsStaleSkipReason()
    {
        using var fixture = SocialJobFixture.Create(facebookSendSucceeds: true);
        var eventId = fixture.InsertEvent(
            EventType.CustomEvent,
            "Immediate custom event\n\nThis should clear a previous skip reason on success.",
            DateTime.UtcNow,
            slackProcessed: 1,
            xProcessed: 1,
            threadsProcessed: 1,
            facebookProcessed: 0,
            facebookSkipReason: SocialEventSkipReason.EmptyMessage.ToString());

        fixture.ProcessPendingImmediateCustomEvents();

        Assert.Equal((1, null), fixture.ReadPlatformState(eventId, "Facebook"));
        Assert.Single(fixture.FacebookRequests);
    }

    private static DateTimeOffset XDailyPostSlot()
    {
        var slots = new[] { 8, 12, 16, 20 };
        var day = new DateOnly(2026, 6, 6);
        return new DateTimeOffset(day.Year, day.Month, day.Day, slots[Math.Abs(day.DayNumber) % slots.Length], 0, 0, TimeSpan.Zero);
    }

    private sealed class SocialJobFixture : IDisposable
    {
        private readonly string _root;

        private SocialJobFixture(
            string root,
            TestWebHostEnvironment env,
            DatabaseManager database,
            EventDataService events,
            AthleteDataService athletes,
            XEventService xEvents,
            ThreadsEventService threadsEvents,
            FacebookEventService facebookEvents,
            XFillerPostLogService xFillerLog,
            ThreadsFillerPostLogService threadsFillerLog,
            FacebookFillerPostLogService facebookFillerLog,
            XApiClient xApiClient,
            XImageService xImageService,
            AthleteCountMilestoneMemeService milestoneMemes,
            List<HttpRequestMessage> xRequests,
            List<HttpRequestMessage> threadsRequests,
            List<HttpRequestMessage> facebookRequests)
        {
            _root = root;
            Env = env;
            Database = database;
            Events = events;
            Athletes = athletes;
            XEvents = xEvents;
            ThreadsEvents = threadsEvents;
            FacebookEvents = facebookEvents;
            XFillerLog = xFillerLog;
            ThreadsFillerLog = threadsFillerLog;
            FacebookFillerLog = facebookFillerLog;
            XApiClient = xApiClient;
            XImageService = xImageService;
            MilestoneMemes = milestoneMemes;
            XRequests = xRequests;
            ThreadsRequests = threadsRequests;
            FacebookRequests = facebookRequests;
        }

        public TestWebHostEnvironment Env { get; }
        public DatabaseManager Database { get; }
        public EventDataService Events { get; }
        public AthleteDataService Athletes { get; }
        public XEventService XEvents { get; }
        public ThreadsEventService ThreadsEvents { get; }
        public FacebookEventService FacebookEvents { get; }
        public XFillerPostLogService XFillerLog { get; }
        public ThreadsFillerPostLogService ThreadsFillerLog { get; }
        public FacebookFillerPostLogService FacebookFillerLog { get; }
        public XApiClient XApiClient { get; }
        public XImageService XImageService { get; }
        public AthleteCountMilestoneMemeService MilestoneMemes { get; }
        public List<HttpRequestMessage> XRequests { get; }
        public List<HttpRequestMessage> ThreadsRequests { get; }
        public List<HttpRequestMessage> FacebookRequests { get; }

        public static SocialJobFixture Create(bool xSendSucceeds = true, bool facebookSendSucceeds = true, Action? onFacebookRequest = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-social-job-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "athletes"));
            Directory.CreateDirectory(Path.Combine(root, "generated", "thumbs", "athletes"));

            var env = new TestWebHostEnvironment(root);
            var database = new DatabaseManager(dbPath: Path.Combine(root, "test.db"));
            var config = new Config
            {
                XAccessToken = "x-token",
                ThreadsAccessToken = null,
                ThreadsAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(60).ToString("o"),
                FacebookPageId = "page-id",
                FacebookPageAccessToken = "facebook-token"
            }.UseFilePathsForTesting(Path.Combine(root, "config.json"), Path.Combine(root, "runtime-config.json"));
            var appConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["EnableEventDispatch"] = "false",
                    ["EnableXDevPreviewBrowser"] = "false"
                })
                .Build();

            var xRequests = new List<HttpRequestMessage>();
            var threadsRequests = new List<HttpRequestMessage>();
            var facebookRequests = new List<HttpRequestMessage>();
            var serviceProvider = new TestServiceProvider();
            var httpFactory = new TestHttpClientFactory(new HttpClient(new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), [])));

            var xClient = new XApiClient(
                new HttpClient(new RecordingHttpHandler(
                    _ => xSendSucceeds
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"id":"tweet-1"}}""") }
                        : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"boom"}""") },
                    xRequests)),
                config,
                env,
                NullLogger<XApiClient>.Instance,
                new XDevPreviewService(NullLogger<XDevPreviewService>.Instance, httpFactory, appConfig));
            var threadsClient = new ThreadsApiClient(
                new HttpClient(new RecordingHttpHandler(
                    _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"threads-1","status":"FINISHED"}""") },
                    threadsRequests)),
                config,
                NullLogger<ThreadsApiClient>.Instance);
            var facebookClient = new FacebookApiClient(
                new HttpClient(new RecordingHttpHandler(
                    _ =>
                    {
                        onFacebookRequest?.Invoke();
                        return facebookSendSucceeds
                            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"facebook-1"}""") }
                            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"boom"}""") };
                    },
                    facebookRequests)),
                config,
                NullLogger<FacebookApiClient>.Instance);
            var customImages = new CustomEventImageService(env, NullLogger<CustomEventImageService>.Instance);
            var xEvents = new XEventService(xClient, NullLogger<XEventService>.Instance, serviceProvider, customImages);
            var threadsEvents = new ThreadsEventService(threadsClient, NullLogger<ThreadsEventService>.Instance, serviceProvider, customImages);
            var facebookEvents = new FacebookEventService(facebookClient, NullLogger<FacebookEventService>.Instance, customImages);
            var slackEvents = new SlackEventService(
                new SlackWebhookClient(new HttpClient(new RecordingHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK), [])), config, NullLogger<SlackWebhookClient>.Instance),
                NullLogger<SlackEventService>.Instance);
            var events = new EventDataService(env, slackEvents, xEvents, threadsEvents, facebookEvents, database, NullLogger<EventDataService>.Instance, appConfig);
            var athletes = new AthleteDataService(env, events, database);
            serviceProvider.Athletes = athletes;

            var xFillerLog = new XFillerPostLogService(database);
            var threadsFillerLog = new ThreadsFillerPostLogService(database);
            var facebookFillerLog = new FacebookFillerPostLogService(database);
            var xImages = new XImageService(env, athletes, NullLogger<XImageService>.Instance);
            var milestoneMemes = new AthleteCountMilestoneMemeService(env);

            database.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "DELETE FROM Events;";
                cmd.ExecuteNonQuery();
            });

            return new SocialJobFixture(
                root,
                env,
                database,
                events,
                athletes,
                xEvents,
                threadsEvents,
                facebookEvents,
                xFillerLog,
                threadsFillerLog,
                facebookFillerLog,
                xClient,
                xImages,
                milestoneMemes,
                xRequests,
                threadsRequests,
                facebookRequests);
        }

        public XDailyPostJob CreateXJob() => new(NullLogger<XDailyPostJob>.Instance, Events, XEvents, Athletes, XFillerLog, XImageService, XApiClient, MilestoneMemes);

        public ThreadsDailyPostJob CreateThreadsJob() => new(NullLogger<ThreadsDailyPostJob>.Instance, Events, ThreadsEvents, Athletes, ThreadsFillerLog, MilestoneMemes);

        public FacebookDailyPostJob CreateFacebookJob() => new(NullLogger<FacebookDailyPostJob>.Instance, Events, Athletes, FacebookEvents, FacebookFillerLog);

        public string InsertEvent(
            EventType type,
            string text,
            DateTime occurredAtUtc,
            double relevance = 10d,
            int visibleOnWebsite = 1,
            int slackProcessed = 1,
            int xProcessed = 1,
            int threadsProcessed = 1,
            int facebookProcessed = 1,
            string? xSkipReason = null,
            string? threadsSkipReason = null,
            string? facebookSkipReason = null)
        {
            var id = Guid.NewGuid().ToString("N");
            Database.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO Events (
                        Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite,
                        SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed,
                        XSkipReason, ThreadsSkipReason, FacebookSkipReason)
                    VALUES (
                        @id, @type, @text, @occurredAt, @relevance, @visibleOnWebsite,
                        @slackProcessed, @xProcessed, @threadsProcessed, @facebookProcessed,
                        @xSkipReason, @threadsSkipReason, @facebookSkipReason);
                    """;
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@type", (int)type);
                cmd.Parameters.AddWithValue("@text", text);
                cmd.Parameters.AddWithValue("@occurredAt", occurredAtUtc.ToUniversalTime().ToString("o"));
                cmd.Parameters.AddWithValue("@relevance", relevance);
                cmd.Parameters.AddWithValue("@visibleOnWebsite", visibleOnWebsite);
                cmd.Parameters.AddWithValue("@slackProcessed", slackProcessed);
                cmd.Parameters.AddWithValue("@xProcessed", xProcessed);
                cmd.Parameters.AddWithValue("@threadsProcessed", threadsProcessed);
                cmd.Parameters.AddWithValue("@facebookProcessed", facebookProcessed);
                cmd.Parameters.AddWithValue("@xSkipReason", (object?)xSkipReason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@threadsSkipReason", (object?)threadsSkipReason ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@facebookSkipReason", (object?)facebookSkipReason ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            });
            Events.ReloadIntoCache();
            return id;
        }

        public (int Processed, string? SkipReason) ReadPlatformState(string id, string platform)
        {
            return Database.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = $"SELECT {platform}Processed, {platform}SkipReason FROM Events WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using var reader = cmd.ExecuteReader();
                Assert.True(reader.Read());
                return (reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1));
            });
        }

        public void ProcessPendingImmediateCustomEvents()
        {
            var method = typeof(EventDataService).GetMethod("ProcessPendingImmediateCustomEvents", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            method!.Invoke(Events, null);
        }

        public void Dispose()
        {
            Athletes.Dispose();
            Events.Dispose();
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

    private sealed class RecordingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, List<HttpRequestMessage> requests) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Add(new HttpRequestMessage(request.Method, request.RequestUri));
            return Task.FromResult(respond(request));
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

    private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new PhysicalFileProvider(root);
        public string WebRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Production";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new PhysicalFileProvider(root);
    }

    private sealed class TestJobExecutionContext(DateTimeOffset fireTimeUtc) : IJobExecutionContext
    {
        private readonly Dictionary<object, object> _values = new();

        public static TestJobExecutionContext Now() => new(DateTimeOffset.UtcNow);
        public static TestJobExecutionContext At(DateTimeOffset fireTimeUtc) => new(fireTimeUtc);

        public IScheduler Scheduler => null!;
        public ITrigger Trigger => null!;
        public ICalendar Calendar => null!;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => null!;
        public int RefireCount => 0;
        public JobDataMap MergedJobDataMap { get; } = new();
        public IJobDetail JobDetail => null!;
        public IJob JobInstance => null!;
        public DateTimeOffset FireTimeUtc { get; } = fireTimeUtc;
        public DateTimeOffset? ScheduledFireTimeUtc { get; } = fireTimeUtc;
        public DateTimeOffset? PreviousFireTimeUtc => null;
        public DateTimeOffset? NextFireTimeUtc => null;
        public string FireInstanceId { get; } = Guid.NewGuid().ToString("N");
        public object? Result { get; set; }
        public TimeSpan JobRunTime => TimeSpan.Zero;
        public CancellationToken CancellationToken => CancellationToken.None;

        public void Put(object key, object objectValue) => _values[key] = objectValue;
        public object? Get(object key) => _values.TryGetValue(key, out var value) ? value : null;
    }
}
