using System.Net;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net.Http.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class InstagramIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly InstagramPostRequest Post = new("A sport for time 🏆", "https://longevityworldcup.com/generated/instagram/card.jpg", "Complete announcement");

    [Fact]
    public void Captions_AlwaysUseAnImageAndRespectEmojiBoundaries()
    {
        var plan = InstagramPost.BuildPlan("event", "A new channel\n\nFollow [mention](alice_smith).", slug => "Alice Smith", false);
        Assert.Equal(CustomEventPostMode.Image, plan.Mode);
        Assert.Equal("A new channel\n\nFollow Alice Smith.", plan.PostText);
        Assert.DoesNotContain("events?", plan.PostText);
        var longTitle = string.Concat(Enumerable.Repeat("👩‍🔬", 500));
        var clipped = InstagramPost.BuildPlan("event", longTitle, null, false).PostText;
        Assert.InRange(clipped.Length, 1, InstagramPost.MaxCaptionLength);
        Assert.EndsWith("…", clipped);
        Assert.Equal(InstagramPost.Truncate(longTitle, 2200), clipped);
        Assert.Equal("…", InstagramPost.Truncate("👩‍🔬", 4));
        Assert.Equal("á…", InstagramPost.Truncate("ábcdef", 3));
        var oversizedUrl = "https://example.com/" + new string('a', 2300);
        var linked = InstagramPost.BuildPlan("event", $"Read the update\n\n[More]({oversizedUrl})", null, false);
        Assert.Equal("Read the update", linked.PostText);
        Assert.Equal(CustomEventPostMode.Image, linked.Mode);
    }

    [Fact]
    public async Task WrongAccountToken_IsRejectedBeforeAnyWrite()
    {
        var writes = 0;
        var api = Client(request =>
        {
            if (request.Method == HttpMethod.Post) writes++;
            return Task.FromResult(Json("""{"user_id":"999","username":"other-account"}"""));
        });
        var error = await Assert.ThrowsAsync<InstagramApiException>(() => api.CreateContainerAsync(Post));
        Assert.Equal("AccountMismatch", error.Code);
        Assert.False(error.OutcomeUnknown);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(403, false)]
    [InlineData(429, false)]
    [InlineData(400, true)]
    [InlineData(500, true)]
    public async Task PublishErrors_PreserveUncertainOutcomesWithoutLeakingTheResponse(int status, bool uncertain)
    {
        var api = Client(request => Task.FromResult(request.Method == HttpMethod.Get ? Identity()
            : Json("""{"error":"private token or response"}""", (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<InstagramApiException>(() => api.PublishContainerAsync("222"));
        Assert.Equal("Http" + status, error.Code);
        Assert.Equal(uncertain, error.OutcomeUnknown);
        Assert.DoesNotContain("private", error.Message);
    }

    [Fact]
    public async Task Publisher_SavesContainerAndMediaBeforeRequiringMatchingPublicReadback()
    {
        using var fixture = new DeliveryFixture();
        var writes = new List<string>();
        var failReadback = true;
        var api = Client(async request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            var path = request.RequestUri!.AbsolutePath;
            Assert.Equal("", request.RequestUri.Query.Contains("access_token", StringComparison.Ordinal) ? "token in URL" : "");
            if (path.EndsWith("/me")) return Identity();
            if (request.Method == HttpMethod.Post)
            {
                writes.Add(path);
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                if (path.EndsWith("/media"))
                {
                    Assert.Equal(Post.ImageUrl, body.RootElement.GetProperty("image_url").GetString());
                    Assert.Equal(Post.Caption, body.RootElement.GetProperty("caption").GetString());
                    Assert.Equal(Post.AltText, body.RootElement.GetProperty("alt_text").GetString());
                    return Json("""{"id":"222"}""");
                }
                Assert.Equal("222", body.RootElement.GetProperty("creation_id").GetString());
                return Json("""{"id":"333"}""");
            }
            if (path.EndsWith("/222")) return Json("""{"id":"222","status_code":"FINISHED"}""");
            if (failReadback) return Json("{}", HttpStatusCode.ServiceUnavailable);
            return Receipt();
        });
        var publisher = fixture.Publisher(api);
        var error = await Assert.ThrowsAsync<InstagramApiException>(() => publisher.PublishAsync(fixture.Pending(), Post, default));
        Assert.False(error.OutcomeUnknown);
        Assert.Equal("333", fixture.Progress.Get("new")?.MediaId);
        Assert.NotNull(fixture.Pending().FirstAttemptAtUtc);
        fixture.Reopen();
        failReadback = false;
        var receipt = await fixture.Publisher(api).PublishAsync(fixture.Pending(), Post, default);
        Assert.Equal("333", receipt.Id);
        Assert.Equal("https://www.instagram.com/p/verified-post/", receipt.Url);
        Assert.Equal(2, writes.Count);
        fixture.Deliveries.Complete(SocialDeliveryStore.Instagram, "new", receipt, Now);
        Assert.Empty(fixture.Deliveries.GetPending(SocialDeliveryStore.Instagram, false, Now.AddDays(1)));
    }

    [Fact]
    public async Task LostPublishResponse_StopsAfterRestartInsteadOfPostingAgain()
    {
        using var fixture = new DeliveryFixture();
        var publicWrites = 0;
        var api = Client(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me")) return Task.FromResult(Identity());
            if (path.EndsWith("/media")) return Task.FromResult(Json("""{"id":"222"}"""));
            if (path.EndsWith("/222")) return Task.FromResult(Json("""{"id":"222","status_code":"FINISHED"}"""));
            publicWrites++;
            throw new HttpRequestException("URL or exception could contain a secret");
        });
        Assert.True((await Assert.ThrowsAsync<InstagramApiException>(() => fixture.Publisher(api).PublishAsync(fixture.Pending(), Post, default))).OutcomeUnknown);
        fixture.Reopen();
        var error = await Assert.ThrowsAsync<InstagramApiException>(() => fixture.Publisher(api).PublishAsync(fixture.Pending(), Post, default));
        Assert.Equal("UnconfirmedPublish", error.Code);
        fixture.Deliveries.RequireReview(SocialDeliveryStore.Instagram, "new", Now);
        Assert.Equal(1, publicWrites);
        Assert.Empty(fixture.Deliveries.GetPending(SocialDeliveryStore.Instagram, true, Now.AddDays(1)));
    }

    [Fact]
    public async Task ProcessingContainer_IsReusedAndTimesOutWithoutAPublicWrite()
    {
        using var fixture = new DeliveryFixture();
        var clock = new FixedTime(Now);
        var creates = 0;
        var api = Client(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me")) return Task.FromResult(Identity());
            if (path.EndsWith("/media")) { creates++; return Task.FromResult(Json("""{"id":"222"}""")); }
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(Json("""{"id":"222","status_code":"IN_PROGRESS"}"""));
        });
        var publisher = fixture.Publisher(api, clock);
        Assert.Equal("ContainerProcessing", (await Assert.ThrowsAsync<InstagramApiException>(() => publisher.PublishAsync(fixture.Pending(), Post, default))).Code);
        clock.Now = Now.AddMinutes(5);
        Assert.Equal("ContainerProcessingTimedOut", (await Assert.ThrowsAsync<InstagramApiException>(() => publisher.PublishAsync(fixture.Pending(), Post, default))).Code);
        Assert.Equal(1, creates);
        Assert.Null(fixture.Pending().FirstAttemptAtUtc);
    }

    [Theory]
    [InlineData("https://elsewhere.example/p/post/")]
    [InlineData("https://www.instagram.com/p/post/?access_token=secret")]
    public async Task PublicReadback_RejectsUnexpectedReceipts(string url)
    {
        var api = Client(_ => Task.FromResult(Json(JsonSerializer.Serialize(new { id = "333", caption = Post.Caption, media_type = "IMAGE", permalink = url }))));
        var error = await Assert.ThrowsAsync<InstagramApiException>(() => api.ReadReceiptAsync("333", Post.Caption));
        Assert.Equal("InvalidPostReadback", error.Code);
    }

    [Fact]
    public async Task TokenRefresh_OnAQuietDayPersistsVerifiedIdentityAndExpiry()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-instagram-token-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config.json");
            var config = new Config { InstagramAccountId = "111", InstagramAccessToken = "test-token",
                InstagramAccessTokenExpiresAtUtc = Now.AddDays(7).ToString("o"), ThreadsAccessToken = "preserve-threads" }
                .UseFilePathsForTesting(path, Path.Combine(root, "runtime.json"));
            var refreshes = 0;
            using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
            var api = Client(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                if (request.RequestUri!.AbsolutePath == "/refresh_access_token")
                {
                    refreshes++;
                    Assert.Contains("grant_type=ig_refresh_token", request.RequestUri.Query);
                    return Task.FromResult(Json("""{"access_token":"renewed-token","expires_in":5184000}"""));
                }
                Assert.Equal("renewed-token", request.Headers.Authorization?.Parameter);
                return Task.FromResult(Identity());
            }, config, new FixedTime(Now));
            var service = Service(fixture, api, new FixedTime(Now));
            await service.DispatchAsync(true);
            await service.DispatchAsync(false);
            var reloaded = await Config.LoadAsync(path, Path.Combine(root, "runtime.json"));
            Assert.Equal(1, refreshes);
            Assert.Equal("renewed-token", reloaded.InstagramAccessToken);
            Assert.Equal(Now.AddDays(60), DateTimeOffset.Parse(reloaded.InstagramAccessTokenExpiresAtUtc!));
            Assert.Equal(Now, DateTimeOffset.Parse(reloaded.InstagramAccessTokenLastRefreshAttemptAtUtc!));
            Assert.Equal("preserve-threads", reloaded.ThreadsAccessToken);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Activation_BaselinesOldEventsAndHonorsIndependentDestinationSelection()
    {
        using var fixture = new DeliveryFixture();
        SocialDeliveryStore.InitializeChannel(fixture.Db, SocialDeliveryStore.Instagram);
        SocialDeliveryStore.InitializeChannel(fixture.Db, SocialDeliveryStore.Nostr);
        Assert.Equal("new", fixture.Pending().Event.Id);
        Assert.Empty(fixture.Deliveries.GetPending(SocialDeliveryStore.Nostr, false, Now));
        fixture.Deliveries.Skip(SocialDeliveryStore.Instagram, "new", "TargetNotSelected", Now);
        Assert.Empty(fixture.Deliveries.GetPending(SocialDeliveryStore.Instagram, true, Now));
    }

    [Fact]
    public async Task AdminQueue_InstagramCanBeSelectedAloneAndRequiresConfiguredCredentials()
    {
        var config = new Config { InstagramAccountId = "111", InstagramAccessToken = "test-token",
            CustomEventDesignerSecretHash = SecretHashVerifier.CreateHash("test-designer-secret") };
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Config>(); services.AddSingleton(config);
        }));
        using var client = factory.CreateClient();
        var request = new { secret = "test-designer-secret", title = "A selected announcement", content = "Instagram only", sendToInstagram = true };
        using var response = await client.PostAsJsonAsync("/api/custom-events", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("instagram", Assert.Single(result.RootElement.GetProperty("queuedTargets").EnumerateArray()).GetString());
        var store = factory.Services.GetRequiredService<SocialDeliveryStore>();
        var queued = Assert.Single(store.GetPending(SocialDeliveryStore.Instagram, true, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.False(queued.Event.VisibleOnWebsite);
        Assert.Equal(result.RootElement.GetProperty("eventId").GetString(), queued.Event.Id);
        Assert.Empty(store.GetPending(SocialDeliveryStore.Mastodon, true, DateTimeOffset.UtcNow.AddMinutes(1)));
        Assert.Empty(store.GetPending(SocialDeliveryStore.Bluesky, true, DateTimeOffset.UtcNow.AddMinutes(1)));
        config.InstagramAccessToken = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/custom-events", request)).StatusCode);
        Assert.Single(store.GetPending(SocialDeliveryStore.Instagram, true, DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task DailyAndCustomDispatch_ShareTheGateAndStopAmbiguousPublication()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create(seedLeaderboardAssets: true);
        var clock = new FixedTime(DateTimeOffset.UtcNow);
        var id = fixture.Events.CreateCustomEvent("A sport for time", "[mention](benjamin_garden) keeps competing.",
            deliveryTargets: new(false, false, false, false, false, SendToInstagram: true));
        fixture.Events.CreateCustomEvent("Website only", "Excluded", deliveryTargets: new(true, false, false, false, false));
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publicWrites = 0;
        var config = new Config { InstagramAccountId = "111", InstagramAccessToken = "test-token", InstagramAccessTokenExpiresAtUtc = clock.Now.AddDays(60).ToString("o") };
        var api = Client(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/me")) return Identity();
            if (path.EndsWith("/media"))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Assert.Contains(fixture.Athletes.GetAthletesForX().Single(x => x.Slug == "benjamin_garden").Name,
                    body.RootElement.GetProperty("caption").GetString());
                Assert.StartsWith("https://longevityworldcup.com/generated/instagram/", body.RootElement.GetProperty("image_url").GetString());
                return Json("""{"id":"222"}""");
            }
            if (path.EndsWith("/222")) return Json("""{"id":"222","status_code":"FINISHED"}""");
            publicWrites++;
            started.SetResult();
            await release.Task;
            throw new HttpRequestException("Lost response after publishing");
        }, config, clock);
        var service = Service(fixture, api, clock);
        var first = service.DispatchAsync(false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await service.DispatchAsync(true);
        release.SetResult();
        await first;
        clock.Now = clock.Now.AddHours(1);
        await Service(fixture, api, clock).DispatchAsync(true);
        Assert.Equal(1, publicWrites);
        Assert.Equal("review", fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Status FROM SocialDeliveries WHERE Platform='instagram' AND EventId=@id";
            cmd.Parameters.AddWithValue("@id", id);
            return (string)cmd.ExecuteScalar()!;
        }));
        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
    }

    private static InstagramAnnouncementService Service(SocialJobIntegrationTests.SocialJobFixture fixture, InstagramApiClient api, TimeProvider clock)
    {
        var deliveries = new SocialDeliveryStore(fixture.Database);
        var images = new InstagramImageService(fixture.Env, new InstagramAnnouncementImageService(fixture.Env, fixture.Athletes,
            new Factory(new Handler(_ => Task.FromResult(Json("{}", HttpStatusCode.NotFound)))), NullLogger<InstagramAnnouncementImageService>.Instance));
        return new(fixture.Events, deliveries, api, new InstagramPublisher(api, new InstagramPublishingStore(fixture.Database), deliveries, clock),
            fixture.ThreadsEvents, fixture.Athletes, images, fixture.MilestoneMemes, clock, NullLogger<InstagramAnnouncementService>.Instance);
    }

    private static InstagramApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, Config? config = null, TimeProvider? clock = null)
        => new(config ?? new Config { InstagramAccountId = "111", InstagramAccessToken = "test-token" },
            new Factory(new Handler(handle)), clock ?? new FixedTime(Now), NullLogger<InstagramApiClient>.Instance);
    private static HttpResponseMessage Identity() => Json("""{"user_id":"111","username":"longevityworldcup","id":"444"}""");
    private static HttpResponseMessage Receipt() => Json(JsonSerializer.Serialize(new { id = "333", caption = Post.Caption,
        media_type = "IMAGE", permalink = "https://www.instagram.com/p/verified-post/" }));
    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class DeliveryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "lwc-instagram-" + Guid.NewGuid().ToString("N"));
        public DatabaseManager Db { get; private set; }
        public SocialDeliveryStore Deliveries { get; private set; }
        public InstagramPublishingStore Progress { get; private set; }
        public DeliveryFixture()
        {
            Db = new DatabaseManager(dbPath: Path.Combine(_root, "test.db"));
            Db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE Events (Id TEXT PRIMARY KEY, Type INTEGER, Text TEXT, OccurredAt TEXT, Relevance REAL,
                        VisibleOnWebsite INTEGER, XSkipReason TEXT, ThreadsSkipReason TEXT, FacebookSkipReason TEXT);
                    INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite)
                        VALUES ('old', 6, 'Old announcement', @now, 15, 0);
                    """;
                cmd.Parameters.AddWithValue("@now", Now.UtcDateTime.ToString("o"));
                cmd.ExecuteNonQuery();
            });
            SocialDeliveryStore.InitializeChannel(Db, SocialDeliveryStore.Instagram);
            Db.Run(sqlite =>
            {
                using var tx = sqlite.BeginTransaction();
                using var cmd = sqlite.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite) VALUES ('new', 6, 'A sport for time', @now, 15, 0);";
                cmd.Parameters.AddWithValue("@now", Now.UtcDateTime.ToString("o"));
                cmd.ExecuteNonQuery();
                SocialDeliveryStore.SelectCustomEventTarget(sqlite, tx, "new", true, SocialDeliveryStore.Instagram);
                tx.Commit();
            });
            Deliveries = new(Db);
            Progress = new(Db);
            Deliveries.Prepare(SocialDeliveryStore.Instagram, "new", JsonSerializer.Serialize(Post), null, Now);
        }
        public PendingSocialDelivery Pending() => Assert.Single(Deliveries.GetPending(SocialDeliveryStore.Instagram, true, Now.AddHours(1)));
        public InstagramPublisher Publisher(InstagramApiClient api, TimeProvider? clock = null) => new(api, Progress, Deliveries, clock ?? new FixedTime(Now));
        public void Reopen()
        {
            Db.Dispose();
            Db = new DatabaseManager(dbPath: Path.Combine(_root, "test.db"));
            Deliveries = new(Db);
            Progress = new(Db);
        }
        public void Dispose() { Db.Dispose(); Directory.Delete(_root, true); }
    }
}
