using System.Net;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class MastodonIntegrationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CharacterBudget_CountsEmojiAndLinksAndClampsImageCaptions()
    {
        var emoji = string.Concat(Enumerable.Repeat("👩‍🔬", 500));
        Assert.Equal(500, MastodonPost.Count(emoji));
        Assert.Equal(501, MastodonPost.Count(emoji + "!"));
        var link = "https://longevityworldcup.com/events?event=" + new string('x', 300);
        Assert.Equal(26, MastodonPost.Count("🏆 " + link + "."));
        Assert.Equal(500, MastodonPost.Count(new string('x', 476) + " " + link));
        var plan = CustomEventSocialComposer.BuildPlan("e", emoji, 500, null, false, MastodonPost.Count, MastodonPost.Truncate);
        Assert.Equal(CustomEventPostMode.Text, plan.Mode);
        var longTitle = CustomEventSocialComposer.BuildPlan("e", new string('x', 501), 500, null, false, MastodonPost.Count, MastodonPost.Truncate);
        Assert.Equal(CustomEventPostMode.Image, longTitle.Mode);
        Assert.Equal(500, MastodonPost.Count(longTitle.PostText));
        Assert.EndsWith("…", longTitle.PostText);
    }

    [Fact]
    public async Task WrongAccountToken_IsRejectedBeforePublishing()
    {
        var posts = 0;
        var client = Client(request =>
        {
            if (request.Method == HttpMethod.Post) posts++;
            return Task.FromResult(Json("""{"id":"somebody-else"}"""));
        });
        var error = await Assert.ThrowsAsync<MastodonApiException>(() => client.PublishAsync("key", new("Hello"), default));
        Assert.Equal("AccountMismatchOrSuspended", error.Code);
        Assert.Equal(0, posts);
    }

    [Theory]
    [InlineData(401, false)]
    [InlineData(429, false)]
    [InlineData(500, true)]
    public async Task PostingErrors_DistinguishRejectedAndUnconfirmedWrites(int status, bool unconfirmed)
    {
        var client = Client(request => Task.FromResult(request.Method == HttpMethod.Get
            ? Json("""{"id":"account-1"}""")
            : Json("""{"error":"Never log this response or token"}""", (HttpStatusCode)status)));
        var error = await Assert.ThrowsAsync<MastodonApiException>(() => client.PublishAsync("key", new("Hello"), default));
        Assert.Equal("Http" + status, error.Code);
        Assert.Equal(unconfirmed, error.OutcomeUnknown);
        Assert.DoesNotContain("Never log", error.Message);
    }

    [Fact]
    public async Task MissingReceipt_IsUnconfirmedAndRetriesKeepTheExactKeyAndMedia()
    {
        var keys = new List<string>();
        var bodies = new List<string>();
        var client = Client(async request =>
        {
            if (request.Method == HttpMethod.Get) return Json("""{"id":"account-1"}""");
            keys.Add(Assert.Single(request.Headers.GetValues("Idempotency-Key")));
            bodies.Add(await request.Content!.ReadAsStringAsync());
            return keys.Count == 1 ? Json("{}") : Receipt();
        });
        var error = await Assert.ThrowsAsync<MastodonApiException>(() => client.PublishAsync("stable-key", new("Hello", "media-1"), default));
        Assert.True(error.OutcomeUnknown);
        Assert.Equal("post-1", (await client.PublishAsync("stable-key", new("Hello", "media-1"), default)).Id);
        Assert.Equal(new[] { "stable-key", "stable-key" }, keys);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Contains("media_ids%5B%5D=media-1", bodies[0]);
    }

    [Theory]
    [InlineData("https://mastodon.social/@user/post-1?token=secret")]
    [InlineData("https://elsewhere.example/post-1")]
    public async Task UnexpectedReceiptUrl_DoesNotFinishDelivery(string url)
    {
        var client = Client(request => Task.FromResult(request.Method == HttpMethod.Get
            ? Json("""{"id":"account-1"}""")
            : Json(JsonSerializer.Serialize(new { id = "post-1", url, account = new { id = "account-1" } }))));
        Assert.True((await Assert.ThrowsAsync<MastodonApiException>(() => client.PublishAsync("key", new("Hello"), default))).OutcomeUnknown);
    }

    [Fact]
    public async Task ImageUpload_SendsAltTextAndPollsProcessingBeforeReturningMedia()
    {
        var polls = 0;
        var client = Client(async request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("verify_credentials")) return Json("""{"id":"account-1"}""");
            if (request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("Complete announcement text", body);
                return Json("""{"id":"media-1","url":null}""", HttpStatusCode.Accepted);
            }
            polls++;
            return Json("""{"id":"media-1","url":"https://files.mastodon.social/image.png"}""");
        });
        Assert.Equal("media-1", await client.UploadImageAsync([1, 2, 3], "image/png", "Complete announcement text", default));
        Assert.Equal(1, polls);
    }

    [Fact]
    public void Ledger_BaselinesHistoryOnceAndRetainsPreparedRetriesAcrossRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-mastodon-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "test.db");
        string key;
        using (var db = new DatabaseManager(dbPath: path))
        {
            CreateEvents(db);
            InsertEvent(db, "old");
            SocialDeliveryStore.InitializeMastodon(db);
            var store = new SocialDeliveryStore(db);
            Assert.Empty(store.GetPending(false, Now));
            InsertEvent(db, "new");
            var prepared = store.Prepare("new", "{\"Text\":\"hello\"}", "athlete", Now);
            key = prepared.Key;
            Assert.Equal(prepared, store.Prepare("new", "changed", "other-athlete", Now));
            store.BeginAttempt("new", Now);
            store.Fail("new", "NetworkError", TimeSpan.FromMinutes(2), false, Now);
            Assert.Empty(store.GetPending(false, Now));
        }
        using (var db = new DatabaseManager(dbPath: path))
        {
            SocialDeliveryStore.InitializeMastodon(db);
            var store = new SocialDeliveryStore(db);
            var pending = Assert.Single(store.GetPending(true, Now.AddMinutes(3)));
            Assert.Equal(key, pending.RecordKey);
            Assert.Equal(Now, pending.FirstAttemptAtUtc);
            Assert.Equal(1, pending.Attempts);
            store.Complete("new", new("post-1", "https://mastodon.social/@user/post-1"), Now.AddMinutes(3));
            Assert.Empty(store.GetPending(false, Now.AddDays(1)));
            Assert.True(store.IsSubjectOnCooldown("athlete", Now.AddDays(1)));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public void CustomSelectionAndReviewState_DoNotLeakIntoAnotherDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-mastodon-" + Guid.NewGuid().ToString("N"));
        using (var db = new DatabaseManager(dbPath: Path.Combine(root, "test.db")))
        {
            CreateEvents(db);
            SocialDeliveryStore.InitializeMastodon(db);
            InsertEvent(db, "selected", EventType.CustomEvent);
            InsertEvent(db, "excluded", EventType.CustomEvent);
            db.Run(sqlite =>
            {
                using var tx = sqlite.BeginTransaction();
                SocialDeliveryStore.SelectCustomEventTarget(sqlite, tx, "selected", true);
                SocialDeliveryStore.SelectCustomEventTarget(sqlite, tx, "excluded", false);
                tx.Commit();
            });
            var store = new SocialDeliveryStore(db);
            Assert.Equal("selected", Assert.Single(store.GetPending(true, Now)).Event.Id);
            store.RequireReview("selected", Now);
            Assert.Empty(store.GetPending(true, Now.AddDays(1)));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public void ExistingChannelLedger_UpgradesWithoutChangingReceiptsOrExplicitlyQueuedMastodonEvents()
    {
        var root = Path.Combine(Path.GetTempPath(), "lwc-mastodon-" + Guid.NewGuid().ToString("N"));
        using (var db = new DatabaseManager(dbPath: Path.Combine(root, "test.db")))
        {
            CreateEvents(db);
            InsertEvent(db, "old");
            InsertEvent(db, "explicit", EventType.CustomEvent);
            db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE SocialDeliveryChannels (Platform TEXT PRIMARY KEY, IntroducedAtUtc TEXT NOT NULL);
                    CREATE TABLE SocialDeliveries (
                        EventId TEXT NOT NULL, Platform TEXT NOT NULL, Status TEXT NOT NULL,
                        RecordKey TEXT, RecordJson TEXT, ContentHash TEXT, RemoteUri TEXT, RemoteCid TEXT,
                        SubjectSlug TEXT, AttemptCount INTEGER NOT NULL DEFAULT 0,
                        NextAttemptAtUtc TEXT, LastErrorCode TEXT, UpdatedAtUtc TEXT NOT NULL,
                        PRIMARY KEY (EventId, Platform));
                    INSERT INTO SocialDeliveryChannels VALUES ('previous-channel', '2026-10-01');
                    INSERT INTO SocialDeliveries (EventId, Platform, Status, RemoteUri, UpdatedAtUtc)
                        VALUES ('old', 'previous-channel', 'sent', 'original-receipt', '2026-10-01');
                    INSERT INTO SocialDeliveries (EventId, Platform, Status, UpdatedAtUtc)
                        VALUES ('explicit', 'mastodon', 'pending', '2026-10-01');
                    """;
                cmd.ExecuteNonQuery();
            });
            SocialDeliveryStore.InitializeMastodon(db);
            Assert.Equal("explicit", Assert.Single(new SocialDeliveryStore(db).GetPending(false, Now)).Event.Id);
            Assert.Equal("original-receipt", db.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText = "SELECT RemoteUri FROM SocialDeliveries WHERE Platform = 'previous-channel'";
                return (string)cmd.ExecuteScalar()!;
            }));
        }
        Directory.Delete(root, true);
    }

    private static void CreateEvents(DatabaseManager db) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "CREATE TABLE Events (Id TEXT PRIMARY KEY, Type INTEGER, Text TEXT, OccurredAt TEXT, Relevance REAL, VisibleOnWebsite INTEGER, XSkipReason TEXT, ThreadsSkipReason TEXT, FacebookSkipReason TEXT);";
        cmd.ExecuteNonQuery();
    });

    private static void InsertEvent(DatabaseManager db, string id, EventType type = EventType.DonationReceived) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance, VisibleOnWebsite) VALUES (@id, @type, 'tx[test] sats[100]', @now, 9, 1);";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@type", (int)type);
        cmd.Parameters.AddWithValue("@now", Now.UtcDateTime.ToString("o"));
        cmd.ExecuteNonQuery();
    });

    internal static MastodonApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, TimeProvider? time = null) => new(
        new Config { MastodonAccountId = "account-1", MastodonAccessToken = "test-token" }, new Factory(new Handler(handle)), time ?? TimeProvider.System);
    internal static HttpResponseMessage Receipt() => Json("""{"id":"post-1","url":"https://mastodon.social/@user/post-1","account":{"id":"account-1"}}""");
    internal static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
