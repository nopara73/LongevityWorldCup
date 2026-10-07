using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BlueskyIntegrationTests
{
    private const string Did = "did:plc:announcement-test";
    private const string Key = "3m4testpost222";
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonObject Record = BlueskyPost.CreateRecord("A fresh announcement 🦋", Now);
    private static string PostUri => $"at://{Did}/app.bsky.feed.post/{Key}";

    [Fact]
    public void PostLimitAndFacets_CountGraphemesAndUtf8Bytes()
    {
        Assert.True(BlueskyPost.Fits(string.Concat(Enumerable.Repeat("🦋", 300))));
        Assert.False(BlueskyPost.Fits(string.Concat(Enumerable.Repeat("🦋", 301))));
        Assert.False(BlueskyPost.Fits("a" + new string('\u0301', 1600)));
        var prefix = "🏆 Winners: ";
        var link = "https://longevityworldcup.com/events?event=abc";
        var record = BlueskyPost.CreateRecord(prefix + link + ".", Now);
        var facet = record["facets"]![0]!;
        Assert.Equal(Encoding.UTF8.GetByteCount(prefix), facet["index"]!["byteStart"]!.GetValue<int>());
        Assert.Equal(Encoding.UTF8.GetByteCount(prefix + link), facet["index"]!["byteEnd"]!.GetValue<int>());
        Assert.Equal(link, facet["features"]![0]!["uri"]!.GetValue<string>());
        Assert.Matches("^[234567abcdefghij][234567abcdefghijklmnopqrstuvwxyz]{12}$", BlueskyPost.NewRecordKey(Now));
    }

    [Fact]
    public void CustomPost_UsesEmojiCapacityAndClampsLongImageTitles()
    {
        var title = string.Concat(Enumerable.Repeat("🦋", 300));
        var plain = CustomEventSocialComposer.BuildPlan("e", title, 300, null, false,
            text => new System.Globalization.StringInfo(text).LengthInTextElements, BlueskyPost.Truncate);
        Assert.Equal(CustomEventPostMode.Text, plain.Mode);
        Assert.Equal(title, plain.PostText);
        var longTitle = CustomEventSocialComposer.BuildPlan("e", new string('x', 500), 300, null, false,
            text => new System.Globalization.StringInfo(text).LengthInTextElements, BlueskyPost.Truncate);
        Assert.Equal(CustomEventPostMode.Image, longTitle.Mode);
        Assert.True(BlueskyPost.Fits(longTitle.PostText));
        Assert.EndsWith("…", longTitle.PostText);
    }

    [Fact]
    public void CombiningMarks_UseAnImageAndKeepTheCaptionWithinBothLimits()
    {
        var text = "a" + new string('\u0301', 1600);
        var plan = CustomEventSocialComposer.BuildPlan("e", text, 300, null, false,
            BlueskyPost.Count, BlueskyPost.Truncate);
        Assert.Equal(CustomEventPostMode.Image, plan.Mode);
        Assert.True(BlueskyPost.Fits(plan.PostText));
        Assert.Equal("…", plan.PostText);
        Assert.Equal("", BlueskyPost.Truncate(text, 0));
    }

    [Fact]
    public async Task StableDidIdentifier_RejectsAnUnexpectedAccountBeforeWriting()
    {
        var requests = 0;
        var client = Client(request =>
        {
            requests++;
            return Task.FromResult(Session());
        }, "did:plc:expected-account");
        var error = await Assert.ThrowsAsync<BlueskyApiException>(() => client.SendRecordAsync(Key, Record));
        Assert.Equal("AccountChanged", error.Code);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task Send_FollowsAccountPdsAndRefreshesExpiredAccessToken()
    {
        var requests = new List<(string Host, string Endpoint, string? Token)>();
        var expired = true;
        var client = Client(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            requests.Add((request.RequestUri.Host, path, request.Headers.Authorization?.Parameter));
            if (path.EndsWith("createSession"))
            {
                Assert.Null(request.Headers.Authorization);
                var body = await request.Content!.ReadAsStringAsync();
                Assert.Contains("app-password", body);
                return Session("old", "refresh", "https://account-pds.example");
            }
            if (path.EndsWith("refreshSession"))
            {
                Assert.Equal("refresh", request.Headers.Authorization?.Parameter);
                expired = false;
                return Session("fresh", "fresh-refresh", "https://account-pds.example");
            }
            if (path.EndsWith("getRecord")) return expired ? Error("ExpiredToken", HttpStatusCode.Unauthorized) : Error("RecordNotFound");
            Assert.EndsWith("createRecord", path);
            return Json(new JsonObject { ["uri"] = PostUri, ["cid"] = "bafy-receipt" });
        });

        var receipt = await client.SendRecordAsync(Key, Record);

        Assert.Equal(PostUri, receipt.Uri);
        Assert.Equal("bafy-receipt", receipt.Cid);
        Assert.Equal("bsky.social", requests[0].Host);
        Assert.All(requests.Skip(1), request => Assert.Equal("account-pds.example", request.Host));
        Assert.Equal("fresh", requests.Last().Token);
        Assert.Single(requests, x => x.Endpoint.EndsWith("createSession"));
        Assert.Single(requests, x => x.Endpoint.EndsWith("refreshSession"));
    }

    [Fact]
    public async Task LostPublishResponse_RecoversReceiptWithoutAnotherWrite()
    {
        var posted = false;
        var writes = 0;
        var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("createSession")) return Task.FromResult(Session());
            if (request.Method == HttpMethod.Get)
                return Task.FromResult(posted ? Existing(Record) : Error("RecordNotFound"));
            writes++;
            posted = true;
            throw new HttpRequestException("Simulated response lost after successful remote write.");
        });

        Assert.Equal(PostUri, (await client.SendRecordAsync(Key, Record)).Uri);
        Assert.Equal(PostUri, (await client.SendRecordAsync(Key, Record)).Uri);
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task ExistingDifferentRecord_IsNeverOverwritten()
    {
        var writes = 0;
        var client = Client(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("createSession")) return Task.FromResult(Session());
            if (request.Method == HttpMethod.Post) writes++;
            return Task.FromResult(Existing(BlueskyPost.CreateRecord("Different post", Now)));
        });
        var error = await Assert.ThrowsAsync<BlueskyApiException>(() => client.SendRecordAsync(Key, Record));
        Assert.Equal("RecordKeyConflict", error.Code);
        Assert.Equal(0, writes);
    }

    [Theory]
    [InlineData("", "bafy-cid")]
    [InlineData("at://somebody-else/app.bsky.feed.post/key", "bafy-cid")]
    [InlineData(null, null)]
    public async Task MissingOrWrongReceipt_DoesNotCountAsDelivered(string? uri, string? cid)
    {
        var client = Client(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("createSession")
            ? Session() : request.Method == HttpMethod.Get ? Error("RecordNotFound")
            : Json(new JsonObject { ["uri"] = uri, ["cid"] = cid })));
        await Assert.ThrowsAsync<BlueskyApiException>(() => client.SendRecordAsync(Key, Record));
    }

    [Fact]
    public async Task ImageUpload_RequiresARealBlobReceipt()
    {
        var client = Client(request => Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("createSession")
            ? Session() : Json(new JsonObject())));
        var error = await Assert.ThrowsAsync<BlueskyApiException>(() => client.UploadImageAsync([1, 2, 3], "image/png", default));
        Assert.Equal("MissingBlob", error.Code);
    }

    [Fact]
    public void DeliveryLedger_ExcludesHistoryAndPreservesPreparedRetriesAcrossRestart()
    {
        var directory = Path.Combine(Path.GetTempPath(), "lwc-bluesky-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "test.db");
        using (var db = new DatabaseManager(dbPath: path))
        {
            CreateEvents(db);
            InsertEvent(db, "historical");
            SocialDeliveryStore.InitializeChannel(db, SocialDeliveryStore.Bluesky);
            var store = new SocialDeliveryStore(db);
            Assert.Empty(store.GetPending(SocialDeliveryStore.Bluesky, false, Now));
            InsertEvent(db, "new-event");
            Assert.Equal("new-event", Assert.Single(store.GetPending(SocialDeliveryStore.Bluesky, false, Now)).Event.Id);
            store.Prepare(SocialDeliveryStore.Bluesky, "new-event", Record.ToJsonString(), "athlete", Now, Key);
            var competing = store.Prepare(SocialDeliveryStore.Bluesky, "new-event", "{}", "other-athlete", Now, "3otherkey22222");
            Assert.Equal(Key, competing.Key);
            Assert.Equal(Record.ToJsonString(), competing.Json);
            store.Fail(SocialDeliveryStore.Bluesky, "new-event", "HttpError", TimeSpan.FromMinutes(2), false, Now);
            Assert.Empty(store.GetPending(SocialDeliveryStore.Bluesky, false, Now));
        }
        using (var db = new DatabaseManager(dbPath: path))
        {
            SocialDeliveryStore.InitializeChannel(db, SocialDeliveryStore.Bluesky);
            var store = new SocialDeliveryStore(db);
            var pending = Assert.Single(store.GetPending(SocialDeliveryStore.Bluesky, false, Now.AddMinutes(3)));
            Assert.Equal(Key, pending.RecordKey);
            Assert.Equal(Record.ToJsonString(), pending.RecordJson);
            Assert.Equal(1, pending.Attempts);
            store.Complete(SocialDeliveryStore.Bluesky, pending.Event.Id, new("bafy-cid", PostUri), Now.AddMinutes(3));
            Assert.Empty(store.GetPending(SocialDeliveryStore.Bluesky, false, Now.AddMinutes(4)));
            Assert.True(store.IsSubjectOnCooldown(SocialDeliveryStore.Bluesky, "athlete", Now.AddDays(1)));
            Assert.False(store.IsSubjectOnCooldown(SocialDeliveryStore.Bluesky, "athlete", Now.AddDays(3)));
        }
        Directory.Delete(directory, true);
    }

    [Fact]
    public void CustomDestinationSelection_QueuesOnlyExplicitlySelectedEvents()
    {
        var path = Path.Combine(Path.GetTempPath(), "lwc-bluesky-" + Guid.NewGuid().ToString("N") + ".db");
        using (var db = new DatabaseManager(dbPath: path))
        {
            CreateEvents(db);
            SocialDeliveryStore.InitializeChannel(db, SocialDeliveryStore.Bluesky);
            InsertEvent(db, "selected", EventType.CustomEvent);
            InsertEvent(db, "not-selected", EventType.CustomEvent);
            db.Run(sqlite =>
            {
                using var tx = sqlite.BeginTransaction();
                SocialDeliveryStore.SelectCustomEventTarget(sqlite, tx, "selected", true, SocialDeliveryStore.Bluesky);
                SocialDeliveryStore.SelectCustomEventTarget(sqlite, tx, "not-selected", false, SocialDeliveryStore.Bluesky);
                tx.Commit();
            });
            Assert.Equal("selected", Assert.Single(new SocialDeliveryStore(db).GetPending(SocialDeliveryStore.Bluesky, true, Now)).Event.Id);
        }
        File.Delete(path);
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

    private static BlueskyApiClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle, string identifier = "test.bsky.social") => new(
        new HttpFactory(new HttpClient(new Handler(handle))),
        new Config { BlueskyIdentifier = identifier, BlueskyAppPassword = "app-password" });

    private static HttpResponseMessage Session(string access = "access", string refresh = "refresh", string pds = "https://bsky.social") => Json(new JsonObject
    {
        ["accessJwt"] = access, ["refreshJwt"] = refresh, ["did"] = Did,
        ["didDoc"] = new JsonObject { ["service"] = new JsonArray(new JsonObject { ["type"] = "AtprotoPersonalDataServer", ["serviceEndpoint"] = pds }) }
    });
    private static HttpResponseMessage Existing(JsonObject record) => Json(new JsonObject { ["uri"] = PostUri, ["cid"] = "bafy-cid", ["value"] = record.DeepClone() });
    private static HttpResponseMessage Error(string code, HttpStatusCode status = HttpStatusCode.BadRequest) => Json(new JsonObject { ["error"] = code }, status);
    private static HttpResponseMessage Json(JsonObject value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(value.ToJsonString(), Encoding.UTF8, "application/json")
    };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handle(request);
    }
    private sealed class HttpFactory(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => http;
    }
}
