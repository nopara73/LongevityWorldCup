using System.Net;
using System.Net.Http.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class WebPushRenewalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Replacement_IsAtomicIdempotentAndPreservesFrozenRecipients(bool sameEndpoint)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var now = DateTimeOffset.UtcNow;
        var old = WebPushTests.Subscription();
        var replacement = WebPushTests.Subscription(sameEndpoint ? old.Endpoint : old.Endpoint + "-renewed");
        store.Subscribe(old, now.AddMinutes(-2));
        var id = fixture.Events.CreateCustomEvent("Fictional renewal", "No delivery", deliveryTargets: new(false, false, false, false, false, SendToWebPush: true));
        store.Prepare(Assert.Single(store.Pending()), "{}", now);
        var original = Assert.Single(store.Recipients(id, now));

        store.Subscribe(replacement, now, [old]);
        store.Subscribe(replacement, now.AddMinutes(1), [old]); // Lost success response / restarted worker.
        store.Unsubscribe(old); // Delayed provider expiry or stale cleanup.
        store.Record(id, original, "failed", "410", null, now); // Old in-flight provider response.

        var recipient = Assert.Single(store.Recipients(id, now));
        Assert.Equal(replacement, recipient.Subscription);
        Assert.Equal(0, recipient.Attempts);
        Assert.Equal(1L, Count(fixture.Database));
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(old, now));
    }

    [Fact]
    public void FailedRotation_PreservesOldRegistrationAndPendingDelivery()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var old = WebPushTests.Subscription();
        var replacement = WebPushTests.Subscription(old.Endpoint);
        var now = DateTimeOffset.UtcNow;
        store.Subscribe(old, now.AddMinutes(-1));
        var id = fixture.Events.CreateCustomEvent("Fictional renewal", "No delivery", deliveryTargets: new(false, false, false, false, false, SendToWebPush: true));
        store.Prepare(Assert.Single(store.Pending()), "{}", now);
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(replacement, now, [WebPushTests.Subscription(old.Endpoint)]));
        Assert.Equal(old, Assert.Single(store.Recipients(id, now)).Subscription);
        store.Unsubscribe(replacement); // Opt-out arrived before its delayed save.
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(replacement, now, [old]));
        Assert.Equal(old, Assert.Single(store.Recipients(id, now)).Subscription);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptOut_WinsWhetherSaveArrivesBeforeOrAfterIt(bool saveFirst)
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var candidate = WebPushTests.Subscription();
        var now = DateTimeOffset.UtcNow;
        if (saveFirst) store.Subscribe(candidate, now);
        store.Unsubscribe(candidate);
        store = new WebPushStore(fixture.Database); // Durable across server restart.
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(candidate, now));
        Assert.Equal(0L, Count(fixture.Database));
        var fresh = WebPushTests.Subscription(candidate.Endpoint);
        store.Subscribe(fresh, now); // Explicit new provider identity can opt in again.
        store.Unsubscribe(candidate);
        Assert.Equal(1L, Count(fixture.Database));
    }

    [Fact]
    public void Replacement_StillWorksAtCapacityAndKeepsOriginalEligibility()
    {
        using var fixture = SocialJobIntegrationTests.SocialJobFixture.Create();
        var store = new WebPushStore(fixture.Database);
        var old = WebPushTests.Subscription();
        var next = WebPushTests.Subscription(old.Endpoint + "-renewed");
        var now = DateTimeOffset.UtcNow;
        store.Subscribe(old, now.AddDays(-1));
        fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM numbers WHERE n < 19999)
                INSERT INTO WebPushSubscriptions (Id, Endpoint, P256dh, Auth, CreatedAtUtc, LastSeenAtUtc)
                SELECT 'fixture-' || n, 'https://fcm.googleapis.com/send/fixture-' || n, 'fixture', 'fixture', '2020', '2020' FROM numbers;
                """;
            command.ExecuteNonQuery();
        });
        Assert.Throws<InvalidOperationException>(() => store.Subscribe(next, now));
        store.Subscribe(next, now, [old]);
        Assert.Equal(20000L, Count(fixture.Database));
        var created = fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT CreatedAtUtc FROM WebPushSubscriptions WHERE Endpoint = @endpoint;";
            command.Parameters.AddWithValue("@endpoint", next.Endpoint);
            return DateTimeOffset.Parse((string)command.ExecuteScalar()!);
        });
        Assert.Equal(now.AddDays(-1), created);
    }

    [Fact]
    public async Task Api_ValidatesOldProofAndReturnsFailuresWithoutRemovingTheOldRegistration()
    {
        var config = WebPushTests.Configured();
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<Config>(); services.AddSingleton(config);
        }));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", client.BaseAddress!.GetLeftPart(UriPartial.Authority));
        client.DefaultRequestHeaders.Add("X-LWC-Push", "1");
        var old = WebPushTests.Subscription();
        var next = WebPushTests.Subscription(old.Endpoint);
        var db = factory.Services.GetRequiredService<DatabaseManager>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/web-push/subscribe", old)).StatusCode);
        var malformed = new WebPushRegistration(next.Endpoint, next.Keys, [new(old.Endpoint, new("bad", "bad"))]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/web-push/subscribe", malformed)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/web-push/subscribe", next)).StatusCode);
        var valid = new WebPushRegistration(next.Endpoint, next.Keys, [old]);
        config.WebPushVapidPrivateKey = null;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/web-push/subscribe", valid)).StatusCode);
        Assert.Equal(1L, Count(db));
        // Unsubscribe remains available while configuration is temporarily unavailable.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/web-push/unsubscribe", old)).StatusCode);
        Assert.Equal(0L, Count(db));
    }

    private static long Count(DatabaseManager db) => db.Run(sqlite =>
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM WebPushSubscriptions;";
        return (long)command.ExecuteScalar()!;
    });
}
