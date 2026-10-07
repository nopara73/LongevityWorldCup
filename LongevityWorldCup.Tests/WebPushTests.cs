using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class WebPushTests
{
    [Fact]
    public void Encryption_MatchesRfc8291PublishedVector()
    {
        // Public RFC 8291 Appendix A test material, never operational credentials.
        var senderKey = WebPushTransport.PublicParameters(D("BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"));
        senderKey.D = D("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw");
        using var sender = ECDiffieHellman.Create(senderKey);
        var result = WebPushTransport.Encrypt(Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"),
            D("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            D("BTBZMqHH6r4Tts7J_aSIgg"), sender, D("DGv6ra1nlYgDCS1FRnbzlw"));
        byte[] expected = [.. D("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8"),
            .. D("8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEsbI_0LpXMuGvnzQ")];
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Vapid_BindsSignedJwtToProviderOriginAndExpiresWithin24Hours()
    {
        var config = Configured();
        var sender = Transport(config);
        var now = DateTimeOffset.UtcNow;
        var auth = sender.Authorization(new("https://fcm.googleapis.com/fcm/send/opaque"), now);
        var token = auth.Split("t=")[1].Split(',')[0];
        var parts = token.Split('.');
        using var claims = JsonDocument.Parse(D(parts[1]));
        Assert.Equal("https://fcm.googleapis.com", claims.RootElement.GetProperty("aud").GetString());
        Assert.Equal(now.AddHours(12).ToUnixTimeSeconds(), claims.RootElement.GetProperty("exp").GetInt64());
        using var publicKey = ECDsa.Create(WebPushTransport.PublicParameters(D(config.WebPushVapidPublicKey!)));
        Assert.True(publicKey.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), D(parts[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        Assert.True(sender.IsConfigured);
        config.WebPushVapidPrivateKey = Configured().WebPushVapidPrivateKey;
        Assert.False(sender.IsConfigured);
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/send/x")]
    [InlineData("https://127.0.0.1/send/x")]
    [InlineData("https://fcm.googleapis.com.evil.example/send/x")]
    [InlineData("https://evil.example@fcm.googleapis.com/send/x")]
    [InlineData("https://fcm.googleapis.com:444/send/x")]
    [InlineData("https://fcm.googleapis.com/send/x#fragment")]
    public void Subscriptions_RejectArbitraryServersAndMalformedKeys(string endpoint)
    {
        Assert.False(WebPushTransport.IsValid(Subscription(endpoint)));
        Assert.False(WebPushTransport.IsValid(new("https://fcm.googleapis.com/send/x", new("broken", "broken"))));
    }

    [Fact]
    public async Task SubscriberApi_RequiresSameOriginIntentAndKeepsKeysPrivate()
    {
        var config = Configured();
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Config>(); services.AddSingleton(config);
        }));
        using var client = factory.CreateClient();
        var subscription = Subscription();
        using var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/web-push/subscribe") { Content = JsonContent.Create(subscription) };
        crossSite.Headers.Add("Origin", "https://unrelated.example");
        crossSite.Headers.Add("X-LWC-Push", "1");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(crossSite)).StatusCode);
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        client.DefaultRequestHeaders.Add("X-LWC-Push", "1");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/web-push/subscribe", subscription)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/web-push/subscribe", subscription)).StatusCode);
        var publicConfig = await client.GetStringAsync("/api/web-push/configuration");
        Assert.Contains(config.WebPushVapidPublicKey!, publicConfig);
        Assert.DoesNotContain(config.WebPushVapidPrivateKey!, publicConfig);
        Assert.DoesNotContain(subscription.Endpoint, publicConfig);
        Assert.DoesNotContain(subscription.Keys.Auth, publicConfig);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/web-push/subscribe", Subscription(subscription.Endpoint))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/web-push/unsubscribe", subscription)).StatusCode);
        Assert.Equal(0L, Count(factory.Services.GetRequiredService<DatabaseManager>(), "WebPushSubscriptions"));
    }

    [Fact]
    public async Task AdminQueue_PushCanBeSelectedIndependentlyAndRequiresConfiguredSigningKeys()
    {
        var config = Configured();
        config.CustomEventDesignerSecretHash = SecretHashVerifier.CreateHash("test-designer-secret");
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Config>(); services.AddSingleton(config);
        }));
        using var client = factory.CreateClient();
        var request = new { secret = "test-designer-secret", title = "A selected notification", content = "Just website push", sendToWebPush = true };
        using var response = await client.PostAsJsonAsync("/api/custom-events", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("webpush", Assert.Single(result.RootElement.GetProperty("queuedTargets").EnumerateArray()).GetString());
        var store = factory.Services.GetRequiredService<WebPushStore>();
        var queued = Assert.Single(store.Pending());
        Assert.False(queued.VisibleOnWebsite);
        Assert.Equal(result.RootElement.GetProperty("eventId").GetString(), queued.Id);
        config.WebPushVapidPrivateKey = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/custom-events", request)).StatusCode);
        Assert.Single(store.Pending());
    }

    [Fact]
    public async Task Dispatch_FreezeRecipientsAndRetryOnlyFailedDevicesAcrossServiceRestart()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var now = new Clock(DateTimeOffset.UtcNow);
        var first = Subscription("https://fcm.googleapis.com/send/first");
        var second = Subscription("https://fcm.googleapis.com/send/second");
        store.Subscribe(first, now.GetUtcNow().AddMinutes(-1));
        store.Subscribe(second, now.GetUtcNow().AddMinutes(-1));
        fixture.Events.CreateCustomEvent("Website only", "No push", deliveryTargets: new(true, false, false, false, false));
        fixture.Events.CreateCustomEvent("Legacy defaults", "No push");
        var id = fixture.Events.CreateCustomEvent("[bold](A sport for time) 🏆", "Selected announcement", deliveryTargets: new(false, false, false, false, false, SendToWebPush: true));
        var requests = new List<string>();
        var succeeds = false;
        var transport = Transport(Configured(), request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);
            Assert.Equal("aes128gcm", Assert.Single(request.Content!.Headers.ContentEncoding));
            Assert.Equal("86400", Assert.Single(request.Headers.GetValues("TTL")));
            return new(request.RequestUri.AbsolutePath.EndsWith("second") && !succeeds ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Created);
        });
        WebPushAnnouncementService Service() => new(fixture.Events, new WebPushStore(fixture.Database), transport, new(fixture.Database), new(fixture.Env), NullLogger<WebPushAnnouncementService>.Instance, now);
        await Service().DispatchAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal((1, 1, 2), store.Progress(id));
        store.Subscribe(Subscription("https://fcm.googleapis.com/send/late"), now.GetUtcNow().AddMinutes(1));
        succeeds = true;
        now.Advance(TimeSpan.FromMinutes(3));
        await Service().DispatchAsync();
        await Service().DispatchAsync();
        Assert.Equal(3, requests.Count);
        Assert.Equal(1, requests.Count(path => path.EndsWith("first")));
        Assert.Equal(2, requests.Count(path => path.EndsWith("second")));
        Assert.DoesNotContain(requests, path => path.EndsWith("late"));
        Assert.Empty(store.Pending());
        Assert.DoesNotContain("[bold]", fixture.Database.Run(db =>
        {
            using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT RecordJson FROM SocialDeliveries WHERE EventId = @id AND Platform = 'webpush';";
            cmd.Parameters.AddWithValue("@id", id); return (string)cmd.ExecuteScalar()!;
        }));
    }

    [Fact]
    public async Task ExpiredSubscriptions_AreRemovedAndNoSubscribersDoesNotBecomeABacklog()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var clock = new Clock(DateTimeOffset.UtcNow);
        WebPushAnnouncementService Service() => new(fixture.Events, store, Transport(Configured(), _ => new(HttpStatusCode.Gone)), new(fixture.Database), new(fixture.Env), NullLogger<WebPushAnnouncementService>.Instance, clock);
        fixture.Events.CreateCustomEvent("Nobody subscribed", "Skip forever", deliveryTargets: new(false, false, false, false, false, SendToWebPush: true));
        await Service().DispatchAsync();
        var subscription = Subscription();
        store.Subscribe(subscription, clock.GetUtcNow().AddMinutes(-1));
        fixture.Events.CreateCustomEvent("Expired browser", "Remove stale endpoint", deliveryTargets: new(false, false, false, false, false, SendToWebPush: true));
        await Service().DispatchAsync();
        Assert.Equal(0L, Count(fixture.Database, "WebPushSubscriptions"));
        Assert.Empty(store.Pending());
    }

    [Fact]
    public void Payload_RespectsGraphemesUtf8BudgetAndUsesVersionedIcon()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var assetFolder = Path.Combine(fixture.Env.WebRootPath, "assets");
        Directory.CreateDirectory(assetFolder);
        File.WriteAllBytes(Path.Combine(assetFolder, "favicon-192x192.png"), [1, 2, 3]);
        var raw = "🏆" + new string('\u0301', 1600) + "\n\n" + string.Concat(Enumerable.Repeat("🏃🏽‍♀️", 500));
        var payload = WebPushAnnouncementService.BuildPayload(new("abc", raw, true, "", null), new(fixture.Env));
        Assert.True(Encoding.UTF8.GetByteCount(payload) <= 3000);
        using var parsed = JsonDocument.Parse(payload);
        Assert.Equal("…", parsed.RootElement.GetProperty("title").GetString());
        Assert.Equal("/events?event=abc", parsed.RootElement.GetProperty("url").GetString());
        Assert.Contains("?v=", parsed.RootElement.GetProperty("icon").GetString());
        Assert.DoesNotContain("\ufffd", payload);
    }

    internal static Config Configured()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        return new() { WebPushVapidSubject = "mailto:testing@longevityworldcup.com", WebPushVapidPublicKey = WebPushTransport.Encode(WebPushTransport.PublicBytes(p)), WebPushVapidPrivateKey = WebPushTransport.Encode(p.D!) };
    }
    internal static WebPushSubscription Subscription(string endpoint = "https://fcm.googleapis.com/send/test")
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new(endpoint, new(WebPushTransport.Encode(WebPushTransport.PublicBytes(key.ExportParameters(false))), WebPushTransport.Encode(RandomNumberGenerator.GetBytes(16))));
    }
    private static WebPushTransport Transport(Config config, Func<HttpRequestMessage, HttpResponseMessage>? handle = null) => new(config, new Factory(new(new Handler(handle ?? (_ => new(HttpStatusCode.Created))))));
    private static byte[] D(string value) => WebPushTransport.Decode(value);
    private static long Count(DatabaseManager db, string table) => db.Run(sqlite => { using var cmd = sqlite.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM " + table; return (long)cmd.ExecuteScalar()!; });
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(handle(request));
    }
    private sealed class Factory(HttpClient http) : IHttpClientFactory { public HttpClient CreateClient(string name) => http; }
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan value) => _now += value;
    }
}
