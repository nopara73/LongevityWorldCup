using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class TelegramIntegrationTests
{
    private const long ChannelId = -1001234567890;
    private const string Title = "Follow LWC by [RSS](https://longevityworldcup.com/feeds/events.rss)";
    private const string Post = "Follow LWC by RSS\n\nhttps://longevityworldcup.com/feeds/events.rss";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RoutineAwardsAreSkippedIncludingPreviouslyPreparedPosts(bool prepared)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram);
        var id = fixture.InsertEvent(EventType.BadgeAward,
            "slug[majoros_gabor] badge[Pheno Age best improvement] cat[Global] val[] place[1]", clock.Now.UtcDateTime);
        var store = new SocialDeliveryStore(fixture.Database);
        if (prepared) store.Prepare(SocialDeliveryStore.Telegram, id, JsonSerializer.Serialize("An old spotlight"), "majoros_gabor", clock.Now);
        var requests = 0;
        var service = CreateService(fixture, clock, _ => { requests++; throw new InvalidOperationException("Unexpected Telegram send"); });

        await service.DispatchAsync(customOnly: prepared);
        await service.DispatchAsync(customOnly: false);

        Assert.Equal(0, requests);
        Assert.Equal("skipped", Delivery(fixture, id).Status);
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT LastErrorCode FROM SocialDeliveries WHERE EventId=@id AND Platform='telegram';";
            cmd.Parameters.AddWithValue("@id", id);
            Assert.Equal(nameof(SocialEventSkipReason.RoutineConversationUpdate), cmd.ExecuteScalar());
        });
    }

    [Fact]
    public async Task DesignerQueuesOnlyTelegramAndRequiresAnActiveCredential()
    {
        var config = NewConfig();
        config.CustomEventDesignerSecretHash = SecretHashVerifier.CreateHash("test-designer-secret");
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Config>();
            services.AddSingleton(config);
        }));
        using var client = factory.CreateClient();
        var request = new { secret = "test-designer-secret", title = Title, sendToTelegram = true };
        using var response = await client.PostAsJsonAsync("/api/custom-events", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("telegram", Assert.Single(result.RootElement.GetProperty("queuedTargets").EnumerateArray()).GetString());
        var store = factory.Services.GetRequiredService<SocialDeliveryStore>();
        var now = DateTimeOffset.UtcNow.AddMinutes(1);
        var queued = Assert.Single(store.GetPending(SocialDeliveryStore.Telegram, true, now));
        Assert.False(queued.Event.VisibleOnWebsite);
        Assert.Equal(result.RootElement.GetProperty("eventId").GetString(), queued.Event.Id);
        Assert.Empty(store.GetPending(SocialDeliveryStore.Mastodon, true, now));

        config.Telegram = null;
        using var inactive = await client.PostAsJsonAsync("/api/custom-events", request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, inactive.StatusCode);
        Assert.Single(store.GetPending(SocialDeliveryStore.Telegram, true, now));
    }

    [Fact]
    public async Task DispatchBaselinesHistoryRetriesRateLimitsAndRecordsOneReceipt()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        var old = Queue(fixture, "Pre-activation announcement");
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram, baselineSelectedTargets: true);
        var id = Queue(fixture, Title);
        var requests = 0;
        var service = CreateService(fixture, clock, async request =>
        {
            Assert.Equal("api.telegram.org", request.RequestUri!.Host);
            Assert.EndsWith("/sendMessage", request.RequestUri.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Assert.Equal(ChannelId, body.RootElement.GetProperty("chat_id").GetInt64());
            Assert.Equal(Post, body.RootElement.GetProperty("text").GetString());
            Assert.True(body.RootElement.GetProperty("link_preview_options").GetProperty("is_disabled").GetBoolean());
            return ++requests == 1
                ? Json(HttpStatusCode.TooManyRequests, """{"ok":false,"error_code":429,"parameters":{"retry_after":120}}""")
                : Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":42,"chat":{"id":-1001234567890,"type":"channel","username":"longevityworldcup"}}}""");
        });

        await service.DispatchAsync(customOnly: false);
        Assert.Equal(0, requests);
        await service.DispatchAsync(customOnly: true);
        clock.Now = clock.Now.AddMinutes(1);
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(1, requests);
        clock.Now = clock.Now.AddMinutes(1);
        await service.DispatchAsync(customOnly: true);
        await service.DispatchAsync(customOnly: true);

        Assert.Equal(2, requests);
        Assert.Equal(("sent", "42", "https://t.me/longevityworldcup/42"), Delivery(fixture, id));
        Assert.Equal(("skipped", null, null), Delivery(fixture, old));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnconfirmedAttemptIsNeverResubmitted(bool interrupted)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram);
        var id = Queue(fixture, Title);
        var store = new SocialDeliveryStore(fixture.Database);
        if (interrupted)
        {
            store.Prepare(SocialDeliveryStore.Telegram, id, JsonSerializer.Serialize(Post), null, clock.Now);
            Assert.True(store.BeginAttempt(SocialDeliveryStore.Telegram, id, clock.Now, onlyIfUnattempted: true));
            Assert.False(store.BeginAttempt(SocialDeliveryStore.Telegram, id, clock.Now, onlyIfUnattempted: true));
            clock.Now = clock.Now.AddMinutes(2);
        }
        var requests = 0;
        var service = CreateService(fixture, clock, _ =>
        {
            requests++;
            throw new HttpRequestException("Unconfirmed response");
        });

        await service.DispatchAsync(customOnly: true);
        clock.Now = clock.Now.AddHours(1);
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(interrupted ? 0 : 1, requests);
        Assert.Equal(("review", null, null), Delivery(fixture, id));
    }

    [Fact]
    public void LongPostUsesAnImageAndKeepsACompleteLinkAndUnicodeCaption()
    {
        var item = new EventItem("123", EventType.CustomEvent, string.Concat(Enumerable.Repeat("👩‍🔬", 300)) + " " + Title + "\n\n" + new string('x', 5000),
            DateTime.UtcNow, 15, false);
        var plan = TelegramAnnouncementService.BuildCustomPlan(item, slug => slug);
        Assert.Equal(CustomEventPostMode.Image, plan.Mode);
        Assert.InRange(plan.PostText.Length, 1, TelegramAnnouncementService.CaptionLength);
        Assert.EndsWith("…\n\nhttps://longevityworldcup.com/feeds/events.rss", plan.PostText);
        _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(plan.PostText);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PhotosPreserveArtworkOrMilestoneAndRetryTheSamePreparedRequest(bool milestone)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create(seedLeaderboardAssets: true);
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram, baselineSelectedTargets: true);
        var asset = milestone ? "assets/social/memes/athletes-404-not-found.jpg" : "assets/social/rejuvenation-olympics.png";
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "LongevityWorldCup.sln"))) repository = repository.Parent;
        Assert.NotNull(repository);
        var destination = Path.Combine(fixture.Env.WebRootPath, asset);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(Path.Combine(repository.FullName, "LongevityWorldCup.Website", "wwwroot", asset), destination);
        var id = milestone
            ? fixture.InsertEvent(EventType.AthleteCountMilestone, "athletes[404]", clock.Now.UtcDateTime)
            : Queue(fixture, "[Coming from the Rejuvenation Olympics?](https://longevityworldcup.com/rejuvenation-olympics)");
        var bodies = new List<string>();
        var service = CreateService(fixture, clock, async request =>
        {
            Assert.EndsWith("/sendPhoto", request.RequestUri!.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            bodies.Add(body);
            using var doc = JsonDocument.Parse(body);
            var photo = doc.RootElement.GetProperty("photo").GetString()!;
            Assert.StartsWith("https://longevityworldcup.com/", photo);
            Assert.InRange(doc.RootElement.GetProperty("caption").GetString()!.Length, 1, 1024);
            Assert.True(doc.RootElement.GetProperty("show_caption_above_media").GetBoolean());
            if (milestone) Assert.EndsWith(asset, photo);
            else Assert.True(File.Exists(Path.Combine(fixture.Env.WebRootPath, "generated", "custom-events", id + "-telegram.png")));
            return bodies.Count == 1
                ? Json(HttpStatusCode.TooManyRequests, """{"ok":false,"error_code":429,"parameters":{"retry_after":172800}}""")
                : Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":43,"chat":{"id":-1001234567890,"type":"channel","username":"longevityworldcup"}}}""");
        });
        await service.DispatchAsync(customOnly: !milestone);
        Assert.Single(bodies);
        clock.Now = clock.Now.AddDays(1);
        await service.DispatchAsync(customOnly: true);
        Assert.Single(bodies); // Retry-After must not be shortened to a day.
        File.Delete(destination); // A safe retry must reuse its prepared image, not render it again.
        clock.Now = clock.Now.AddDays(1);
        await service.DispatchAsync(customOnly: true);
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(2, bodies.Count);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(("sent", "43", "https://t.me/longevityworldcup/43"), Delivery(fixture, id));
    }

    [Fact]
    public async Task PreparationFailureRetriesBeforeStartingAnyTelegramRequest()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram);
        var id = Queue(fixture, "[Keep competing](https://longevityworldcup.com/rejuvenation-olympics)");
        var requests = 0;
        var service = CreateService(fixture, clock, _ => { requests++; throw new InvalidOperationException(); });
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(0, requests);
        var pending = Assert.Single(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Telegram, true, clock.Now.AddMinutes(10)));
        Assert.Equal(id, pending.Event.Id);
        Assert.Null(pending.FirstAttemptAtUtc);
        Assert.Equal(1, pending.Attempts);
    }

    [Theory]
    [InlineData(403, "failed")]
    [InlineData(500, "review")]
    [InlineData(200, "review")]
    public async Task RejectionOrMismatchedReceiptDoesNotRetryBlindly(int status, string expected)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram);
        var id = Queue(fixture, Title);
        var calls = 0;
        var service = CreateService(fixture, clock, _ =>
        {
            calls++;
            return Task.FromResult(Json((HttpStatusCode)status, status == 200
                ? """{"ok":true,"result":{"message_id":99,"chat":{"id":-1009999999999,"type":"channel"}}}"""
                : "{\"ok\":false,\"error_code\":" + status + "}"));
        });
        await service.DispatchAsync(customOnly: true);
        clock.Now = clock.Now.AddHours(1);
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(1, calls);
        Assert.Equal((expected, null, null), Delivery(fixture, id));
    }

    [Fact]
    public async Task ConcurrentDispatchKeepsAnExistingPreparedTextRequestAndSendsOnce()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var clock = new TestClock(DateTimeOffset.UtcNow);
        SocialDeliveryStore.InitializeChannel(fixture.Database, SocialDeliveryStore.Telegram);
        var id = Queue(fixture, Title);
        new SocialDeliveryStore(fixture.Database).Prepare(SocialDeliveryStore.Telegram, id, JsonSerializer.Serialize(Post), null, clock.Now);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var service = CreateService(fixture, clock, async request =>
        {
            calls++;
            Assert.Contains("Follow LWC by RSS", await request.Content!.ReadAsStringAsync());
            entered.SetResult();
            await finish.Task;
            return Json(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":42,"chat":{"id":-1001234567890,"type":"channel","username":"longevityworldcup"}}}""");
        });
        var first = service.DispatchAsync(customOnly: true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.DispatchAsync(customOnly: true);
        finish.SetResult();
        await first;
        await service.DispatchAsync(customOnly: true);
        Assert.Equal(1, calls);
        Assert.Equal("sent", Delivery(fixture, id).Status);
    }

    private static Config NewConfig() => new()
    {
        Telegram = new() { BotToken = "123456:test-bot-token-with-colon", ChannelId = ChannelId }
    };

    private static string Queue(SocialJobIntegrationTests.SocialJobFixture fixture, string title) =>
        fixture.Events.CreateCustomEvent(title, "", deliveryTargets: new(false, false, false, false, false, SendToTelegram: true));

    private static TelegramAnnouncementService CreateService(SocialJobIntegrationTests.SocialJobFixture fixture, TestClock clock,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var config = NewConfig();
        return new(config, fixture.Events, new SocialDeliveryStore(fixture.Database),
            new TelegramApiClient(new ClientFactory(new Handler(send)), config), fixture.ThreadsEvents, fixture.Athletes,
            new CustomEventImageService(fixture.Env, NullLogger<CustomEventImageService>.Instance), fixture.MilestoneMemes,
            clock, NullLogger<TelegramAnnouncementService>.Instance);
    }

    private static (string Status, string? RemoteId, string? Url) Delivery(SocialJobIntegrationTests.SocialJobFixture fixture, string id) =>
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Status, RemoteCid, RemoteUri FROM SocialDeliveries WHERE EventId=@id AND Platform='telegram';";
            cmd.Parameters.AddWithValue("@id", id);
            using var rows = cmd.ExecuteReader();
            Assert.True(rows.Read());
            return (rows.GetString(0), rows.IsDBNull(1) ? null : rows.GetString(1), rows.IsDBNull(2) ? null : rows.GetString(2));
        });

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }

    private sealed class ClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.telegram.org/") };
    }

    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
