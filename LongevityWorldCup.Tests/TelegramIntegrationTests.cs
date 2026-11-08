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
    public void LongPostKeepsACompleteLinkAndUnicodeText()
    {
        var item = new EventItem("123", EventType.CustomEvent, Title + "\n\n" + string.Concat(Enumerable.Repeat("👩‍🔬", 1000)),
            DateTime.UtcNow, 15, false);
        var post = TelegramAnnouncementService.BuildCustomPost(item, slug => slug);
        Assert.True(post.Length <= TelegramAnnouncementService.MaxLength);
        Assert.EndsWith("…\n\nhttps://longevityworldcup.com/feeds/events.rss", post);
        _ = new UTF8Encoding(false, throwOnInvalidBytes: true).GetBytes(post);
    }

    private static Config NewConfig() => new()
    {
        Telegram = new() { BotToken = "test-token", ChannelId = ChannelId }
    };

    private static string Queue(SocialJobIntegrationTests.SocialJobFixture fixture, string title) =>
        fixture.Events.CreateCustomEvent(title, "", deliveryTargets: new(false, false, false, false, false, SendToTelegram: true));

    private static TelegramAnnouncementService CreateService(SocialJobIntegrationTests.SocialJobFixture fixture, TestClock clock,
        Func<HttpRequestMessage, Task<HttpResponseMessage>> send)
    {
        var config = NewConfig();
        return new(config, fixture.Events, new SocialDeliveryStore(fixture.Database),
            new TelegramApiClient(new ClientFactory(new Handler(send)), config), fixture.ThreadsEvents, fixture.Athletes,
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
