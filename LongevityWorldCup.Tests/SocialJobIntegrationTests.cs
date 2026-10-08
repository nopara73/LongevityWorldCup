using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Jobs;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;
using SixLabors.ImageSharp;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialJobIntegrationTests
{
    [Fact]
    public async Task BlueskyDonation_FailedSendRetriesAndCompletesExactlyOnce()
    {
        using var fixture = SocialJobFixture.Create();
        var time = new AnnouncementTimeProvider(DateTimeOffset.UtcNow);
        var succeeds = false;
        var writes = new List<JsonObject>();
        var service = fixture.CreateBlueskyService(time, request =>
        {
            if (!request.RequestUri!.AbsolutePath.EndsWith("createRecord")) return BlueskyReadResponse(request);
            var body = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
            writes.Add(body);
            return succeeds ? BlueskyWriteResponse(body) : new(HttpStatusCode.ServiceUnavailable)
                { Content = new StringContent("""{"error":"UpstreamError"}""") };
        });
        var donation = ("bluesky-donation", DateTime.UtcNow, 8455L);
        fixture.Events.CreateDonationReceivedEvents([donation]);

        await service.DispatchAsync(false);
        var pending = Assert.Single(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Bluesky, false, time.GetUtcNow().AddMinutes(3)));
        Assert.Equal(1, pending.Attempts);
        Assert.Contains("0.00008455 BTC", writes[0]["record"]!["text"]!.GetValue<string>());
        Assert.Contains("contribute", writes[0]["record"]!["embed"]!["external"]!["uri"]!.GetValue<string>());
        succeeds = true;
        time.Advance(TimeSpan.FromMinutes(3));
        await service.DispatchAsync(false);
        Assert.Equal(writes[0]["rkey"]!.GetValue<string>(), writes[1]["rkey"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(writes[0]["record"], writes[1]["record"]));
        fixture.Events.CreateDonationReceivedEvents([donation]);
        await service.DispatchAsync(false);
        Assert.Equal(2, writes.Count);
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Bluesky, false, time.GetUtcNow()));
    }

    [Fact]
    public async Task BlueskyCustomImage_PostsSelectedDestinationWithAltTextAndSkipsAcceptedResults()
    {
        using var fixture = SocialJobFixture.Create(seedLeaderboardAssets: true);
        var records = new List<JsonObject>();
        var uploads = 0;
        var service = fixture.CreateBlueskyService(TimeProvider.System, request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("uploadBlob"))
            {
                uploads++;
                Assert.Equal("image/png", request.Content!.Headers.ContentType!.MediaType);
                return new(HttpStatusCode.OK) { Content = new StringContent("""{"blob":{"$type":"blob","ref":{"$link":"bafy-image"},"mimeType":"image/png","size":100}}""") };
            }
            if (!request.RequestUri.AbsolutePath.EndsWith("createRecord")) return BlueskyReadResponse(request);
            var body = JsonNode.Parse(request.Content!.ReadAsStringAsync().GetAwaiter().GetResult())!.AsObject();
            records.Add(body["record"]!.AsObject());
            return BlueskyWriteResponse(body);
        });
        fixture.Events.CreateCustomEvent("Not selected", "No Bluesky post", deliveryTargets: new(false, false, false, false, false));
        var content = string.Join(" ", Enumerable.Repeat("Still getting younger.", 30));
        fixture.Events.CreateCustomEvent("A community update", content, deliveryTargets: new(false, false, false, false, false, SendToBluesky: true));
        var accepted = fixture.InsertEvent(EventType.TestResultAccepted, "slug[nopara73] testDate[2026-10-06]", DateTime.UtcNow, 1, 1, 1);

        await service.DispatchAsync(true);
        await service.DispatchAsync(false);

        Assert.Equal(1, uploads);
        var record = Assert.Single(records);
        Assert.Equal("app.bsky.embed.images", record["embed"]!["$type"]!.GetValue<string>());
        Assert.Contains(content, record["embed"]!["images"]![0]!["alt"]!.GetValue<string>());
        Assert.DoesNotContain("events?event=", record["text"]!.GetValue<string>());
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Bluesky, false, DateTimeOffset.UtcNow));
        Assert.Equal((1, null), fixture.ReadPlatformState(accepted, "X"));
    }

    private static HttpResponseMessage BlueskyReadResponse(HttpRequestMessage request) => new(
        request.RequestUri!.AbsolutePath.EndsWith("createSession") ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
    {
        Content = new StringContent(request.RequestUri!.AbsolutePath.EndsWith("createSession")
            ? """{"accessJwt":"test-access","refreshJwt":"test-refresh","did":"did:plc:lwc-test"}"""
            : """{"error":"RecordNotFound"}""")
    };

    private static HttpResponseMessage BlueskyWriteResponse(JsonObject body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JsonObject
        {
            ["uri"] = $"at://did:plc:lwc-test/app.bsky.feed.post/{body["rkey"]!.GetValue<string>()}", ["cid"] = "bafy-post"
        }.ToJsonString())
    };

    private sealed class AnnouncementTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    [Fact]
    public async Task NostrCustomOnly_PreservesLongTextHumanNamesAndIndependentDestinations()
    {
        using var fixture = SocialJobFixture.Create(seedLeaderboardAssets: true);
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var transport = new NostrRelayTests.RecordingRelays();
        var client = new NostrRelayClient(NostrRelayTests.Configured(), transport);
        var id = fixture.Events.CreateCustomEvent("A sport for time 🏆", "[mention](benjamin_garden) " + new string('x', 600),
            deliveryTargets: new(false, false, false, false, false, false, true));
        await NostrService(fixture, client, clock).DispatchAsync(true);
        Assert.Equal(2, transport.Writes.Count);
        Assert.All(transport.Writes, write =>
        {
            Assert.Equal(1, write.Item.Kind);
            Assert.Contains(fixture.Athletes.GetAthletesForX().Single(a => a.Slug == "benjamin_garden").Name, write.Item.Content);
            Assert.Contains(new string('x', 600), write.Item.Content);
            Assert.DoesNotContain("[mention]", write.Item.Content);
            Assert.DoesNotContain("@", write.Item.Content);
        });
        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
        var store = new SocialDeliveryStore(fixture.Database);
        Assert.Empty(store.GetPending(SocialDeliveryStore.Nostr, true, clock.Current));
        Assert.Empty(store.GetPending(SocialDeliveryStore.Mastodon, true, clock.Current));
        Assert.Equal((1, null), fixture.ReadPlatformState(id, "X"));
    }

    [Fact]
    public async Task NostrLostRelayReply_RestartAndLaterRetryReuseTheExactSignedEvent()
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var transport = new NostrRelayTests.RecordingRelays { FailSecond = true };
        var client = new NostrRelayClient(NostrRelayTests.Configured(), transport);
        var id = fixture.Events.CreateCustomEvent("Original announcement", "Keep this content", deliveryTargets: new(false, false, false, false, false, true, true));
        await NostrService(fixture, client, clock).DispatchAsync(true);
        var original = JsonSerializer.Serialize(transport.Writes[0].Item);
        Assert.Equal(2, transport.Writes.Count);
        fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "UPDATE Events SET Text = 'Changed after publishing' WHERE Id = @id";
            command.Parameters.AddWithValue("@id", id);
            command.ExecuteNonQuery();
        });
        clock.Current = clock.Current.AddDays(2);
        transport.FailSecond = false;
        await NostrService(fixture, client, clock).DispatchAsync(true);
        Assert.Equal(4, transport.Writes.Count);
        Assert.All(transport.Writes, write => Assert.Equal(original, JsonSerializer.Serialize(write.Item)));
        var store = new SocialDeliveryStore(fixture.Database);
        Assert.Empty(store.GetPending(SocialDeliveryStore.Nostr, true, clock.Current));
        Assert.Equal(id, Assert.Single(store.GetPending(SocialDeliveryStore.Mastodon, true, clock.Current)).Event.Id);
        Assert.DoesNotContain(NostrProtocolTests.TestPrivateKey, original);
    }

    [Fact]
    public async Task NostrDailyAndCustomJobs_ShareTheirDispatchGate()
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new NostrRelayTests.RecordingRelays
        {
            BeforeReceipt = async ct => { started.TrySetResult(); await release.Task.WaitAsync(ct); }
        };
        fixture.Events.CreateCustomEvent("One announcement", "", deliveryTargets: new(false, false, false, false, false, false, true));
        var service = NostrService(fixture, new NostrRelayClient(NostrRelayTests.Configured(), transport), clock);
        var first = service.DispatchAsync(false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.DispatchAsync(true);
        release.SetResult();
        await first;
        Assert.Equal(2, transport.Writes.Count);
    }

    [Fact]
    public async Task NostrDelayedDonation_IsAcknowledgedOnceWithoutCompletingOtherChannels()
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var transport = new NostrRelayTests.RecordingRelays();
        var service = NostrService(fixture, new NostrRelayClient(NostrRelayTests.Configured(), transport), clock);
        var donation = ("nostr-donation", DateTime.UtcNow.AddDays(-30), 8455L);
        fixture.Events.CreateDonationReceivedEvents([donation]);
        var receipt = Assert.Single(fixture.Events.GetPendingXEvents());
        await service.DispatchAsync(false);
        fixture.Events.CreateDonationReceivedEvents([donation]);
        await service.DispatchAsync(false);
        Assert.Equal(2, transport.Writes.Count);
        Assert.All(transport.Writes, write => Assert.Contains("0.00008455 BTC", write.Item.Content));
        Assert.Equal((0, null), fixture.ReadPlatformState(receipt.Id, "X"));
    }

    private static NostrAnnouncementService NostrService(SocialJobFixture fixture, NostrRelayClient client, TimeProvider clock) => new(
        fixture.Events, new SocialDeliveryStore(fixture.Database), client, fixture.ThreadsEvents, fixture.Athletes,
        new CustomEventImageService(fixture.Env, NullLogger<CustomEventImageService>.Instance), fixture.MilestoneMemes,
        clock, NullLogger<NostrAnnouncementService>.Instance);

    [Fact]
    public async Task MastodonDonation_DelayedReceiptPublishesOnceAndOtherChannelsRemainPending()
    {
        using var fixture = SocialJobFixture.Create();
        var writes = new List<string>();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var api = MastodonIntegrationTests.Client(async request =>
        {
            if (request.Method == HttpMethod.Get) return MastodonIntegrationTests.Json("""{"id":"account-1"}""");
            writes.Add(WebUtility.UrlDecode(await request.Content!.ReadAsStringAsync()));
            return MastodonIntegrationTests.Receipt();
        }, clock);
        var service = MastodonService(fixture, api, clock);
        var donation = ("mastodon-donation", DateTime.UtcNow.AddDays(-30), 8455L);
        fixture.Events.CreateDonationReceivedEvents([donation]);
        var receipt = Assert.Single(fixture.Events.GetPendingXEvents());
        await service.DispatchAsync(false);
        fixture.Events.CreateDonationReceivedEvents([donation]);
        await service.DispatchAsync(false);
        var text = Assert.Single(writes);
        Assert.Contains("0.00008455 BTC", text);
        Assert.Contains("utm_content=donation-" + receipt.Id, text);
        Assert.Equal((0, null), fixture.ReadPlatformState(receipt.Id, "X"));
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Mastodon, false, clock.GetUtcNow()));
    }

    [Fact]
    public async Task MastodonCustomImage_UsesHumanNamesAndAltTextForTheSelectedDestination()
    {
        using var fixture = SocialJobFixture.Create(seedLeaderboardAssets: true);
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        string? alt = null;
        string? posted = null;
        var api = MastodonIntegrationTests.Client(async request =>
        {
            if (request.Method == HttpMethod.Get) return MastodonIntegrationTests.Json("""{"id":"account-1"}""");
            if (request.RequestUri!.AbsolutePath == "/api/v2/media")
            {
                var parts = Assert.IsType<MultipartFormDataContent>(request.Content);
                alt = await parts.Single(part => part.Headers.ContentDisposition!.Name!.Trim('"') == "description").ReadAsStringAsync();
                return MastodonIntegrationTests.Json("""{"id":"media-1","url":"https://files.mastodon.social/image.png"}""");
            }
            posted = WebUtility.UrlDecode(await request.Content!.ReadAsStringAsync());
            return MastodonIntegrationTests.Receipt();
        }, clock);
        fixture.Events.CreateCustomEvent("A long announcement", "[mention](benjamin_garden) " + new string('x', 550),
            deliveryTargets: new(false, false, false, false, false, true));
        await MastodonService(fixture, api, clock).DispatchAsync(true);
        Assert.Contains(fixture.Athletes.GetAthletesForX().Single(a => a.Slug == "benjamin_garden").Name, alt);
        Assert.Contains("media_ids[]=media-1", posted);
        Assert.Contains("status=A long announcement", posted);
        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(46, true)]
    public async Task MastodonLostReply_ReusesThePreparedRequestOrRequiresReviewAfterTheRetryWindow(int elapsedMinutes, bool review)
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var writes = new List<(string Key, string Body)>();
        var api = MastodonIntegrationTests.Client(async request =>
        {
            if (request.Method == HttpMethod.Get) return MastodonIntegrationTests.Json("""{"id":"account-1"}""");
            writes.Add((Assert.Single(request.Headers.GetValues("Idempotency-Key")), await request.Content!.ReadAsStringAsync()));
            if (writes.Count == 1) throw new HttpRequestException("Response lost after the server created the post");
            return MastodonIntegrationTests.Receipt();
        }, clock);
        var id = fixture.Events.CreateCustomEvent("Original announcement", "Keep this content", deliveryTargets: new(false, false, false, false, false, true));
        await MastodonService(fixture, api, clock).DispatchAsync(true);
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "UPDATE Events SET Text = 'Changed after publishing' WHERE Id = @id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        });
        clock.Current = clock.Current.AddMinutes(elapsedMinutes);
        await MastodonService(fixture, api, clock).DispatchAsync(true);
        if (review) Assert.Single(writes);
        else
        {
            Assert.Equal(2, writes.Count);
            Assert.Equal(writes[0], writes[1]);
        }
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Mastodon, true, clock.Current));
        Assert.Equal(review ? "review" : "sent", fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "SELECT Status FROM SocialDeliveries WHERE Platform = 'mastodon' AND EventId = @id";
            cmd.Parameters.AddWithValue("@id", id);
            return (string)cmd.ExecuteScalar()!;
        }));
    }

    [Fact]
    public async Task MastodonDailyAndCustomJobs_CannotPublishTheSameEventConcurrently()
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = 0;
        var api = MastodonIntegrationTests.Client(async request =>
        {
            if (request.Method == HttpMethod.Get) return MastodonIntegrationTests.Json("""{"id":"account-1"}""");
            writes++;
            started.SetResult();
            await release.Task;
            return MastodonIntegrationTests.Receipt();
        }, clock);
        fixture.Events.CreateCustomEvent("One announcement", "", deliveryTargets: new(false, false, false, false, false, true));
        var service = MastodonService(fixture, api, clock);
        var first = service.DispatchAsync(false);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await service.DispatchAsync(true);
        release.SetResult();
        await first;
        Assert.Equal(1, writes);
    }

    [Fact]
    public async Task UnconfiguredMastodon_LeavesSelectedAnnouncementsPending()
    {
        using var fixture = SocialJobFixture.Create();
        var clock = new MastodonClock(DateTimeOffset.UtcNow);
        var requests = new List<HttpRequestMessage>();
        var api = new MastodonApiClient(new Config(), new TestHttpClientFactory(new HttpClient(new RecordingHttpHandler(
            _ => throw new InvalidOperationException("An unconfigured channel must not make requests"), requests))), clock);
        var id = fixture.Events.CreateCustomEvent("Ready when configured", "", deliveryTargets: new(false, false, false, false, false, true));
        await MastodonService(fixture, api, clock).DispatchAsync(true);
        Assert.Empty(requests);
        Assert.Equal(id, Assert.Single(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Mastodon, true, clock.Current)).Event.Id);
    }

    private static MastodonAnnouncementService MastodonService(SocialJobFixture fixture, MastodonApiClient api, TimeProvider clock) => new(
        fixture.Events, new SocialDeliveryStore(fixture.Database), api, fixture.ThreadsEvents, fixture.Athletes,
        new CustomEventImageService(fixture.Env, NullLogger<CustomEventImageService>.Instance), fixture.MilestoneMemes,
        clock, NullLogger<MastodonAnnouncementService>.Instance);

    private sealed class MastodonClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public async Task DonationReceivedEvent_PublishesOnceEvenAfterDelayedDeliveryAndRepeatedDetection(string platform)
    {
        using var fixture = SocialJobFixture.Create(enableThreads: true);
        BlockPeriodicReminders(fixture);
        var donation = ("donation-transaction", DateTime.UtcNow.AddDays(-30), 8455L);
        fixture.Events.CreateDonationReceivedEvents([donation]);
        var receipt = Assert.Single(fixture.Events.GetPendingXEvents());
        Assert.Equal(8, receipt.XPriority);
        Assert.Equal(
            $"Someone has donated 0.00008455 BTC 🎉\n\nThank you for helping fund the prize pool!\n\nhttps://longevityworldcup.com/contribute?utm_content=donation-{receipt.Id}#contribute",
            fixture.FacebookEvents.TryBuildMessage(receipt.Type, receipt.Text, receipt.Id));

        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal((1, null), fixture.ReadPlatformState(receipt.Id, platform));
        var requests = DonationRequests(fixture, platform);
        Assert.NotEmpty(requests);
        var bodies = await Task.WhenAll(requests
            .Where(request => request.Content is not null)
            .Select(request => request.Content!.ReadAsStringAsync()));
        Assert.Contains(bodies, body => WebUtility.UrlDecode(body)
            .Contains("Someone has donated 0.00008455 BTC", StringComparison.Ordinal));
        Assert.Contains(bodies, body => WebUtility.UrlDecode(body)
            .Contains($"https://longevityworldcup.com/contribute?utm_content=donation-{receipt.Id}#contribute", StringComparison.Ordinal));
        var requestCount = requests.Count;

        fixture.Events.CreateDonationReceivedEvents([donation]);
        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal(requestCount, requests.Count);
        Assert.Equal((1, null), fixture.ReadPlatformState(receipt.Id, platform));

        fixture.Events.CreateDonationReceivedEvents([("second-donation", donation.Item2, 8455L)]);
        var secondReceipt = Assert.Single(fixture.Events.GetPendingXEvents(), item => item.Text.Contains("tx[second-donation]", StringComparison.Ordinal));
        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal((1, null), fixture.ReadPlatformState(secondReceipt.Id, platform));
        var secondBodies = await Task.WhenAll(requests.Skip(requestCount)
            .Where(request => request.Content is not null)
            .Select(request => request.Content!.ReadAsStringAsync()));
        Assert.Contains(secondBodies, body => WebUtility.UrlDecode(body)
            .Contains($"https://longevityworldcup.com/contribute?utm_content=donation-{secondReceipt.Id}#contribute", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public async Task DonationReceivedEvent_FailedSendRemainsPendingUntilSuccessfulRetry(string platform)
    {
        var sendSucceeds = false;
        using var fixture = SocialJobFixture.Create(
            enableThreads: true,
            responseOverride: _ => sendSucceeds
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":{"id":"tweet-1"},"id":"post-1","status":"FINISHED"}""")
                }
                : new HttpResponseMessage(HttpStatusCode.BadRequest)
                {
                    Content = new StringContent("""{"error":{"message":"Invalid parameter","code":100}}""")
                });
        fixture.Events.CreateDonationReceivedEvents([("donation-transaction", DateTime.UtcNow, 8455L)]);
        var receipt = Assert.Single(fixture.Events.GetPendingXEvents());

        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.NotEmpty(DonationRequests(fixture, platform));
        Assert.Equal((0, null), fixture.ReadPlatformState(receipt.Id, platform));

        sendSucceeds = true;
        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Equal((1, null), fixture.ReadPlatformState(receipt.Id, platform));
    }

    [Theory]
    [InlineData("X", SocialEventSkipReason.UnsupportedEventPayload)]
    [InlineData("Threads", SocialEventSkipReason.UnsupportedEventPayload)]
    [InlineData("Facebook", SocialEventSkipReason.EmptyMessage)]
    public async Task DonationReceivedEvent_InvalidReceiptDoesNotSend(string platform, SocialEventSkipReason expectedReason)
    {
        using var fixture = SocialJobFixture.Create(enableThreads: true);
        BlockPeriodicReminders(fixture);
        var eventId = fixture.InsertEvent(
            EventType.DonationReceived,
            "tx[donation-transaction] sats[0]",
            DateTime.UtcNow,
            xProcessed: 0,
            threadsProcessed: 0,
            facebookProcessed: 0);

        await DonationJob(fixture, platform).Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Empty(DonationRequests(fixture, platform));
        Assert.Equal((1, expectedReason.ToString()), fixture.ReadPlatformState(eventId, platform));
    }

    [Fact]
    public async Task CancelledSocialJobs_DoNotSendRequests()
    {
        using var fixture = SocialJobFixture.Create();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        IJob[] jobs = [fixture.CreateXJob(), fixture.CreateThreadsJob(), fixture.CreateFacebookJob()];

        foreach (var job in jobs)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await job.Execute(TestJobExecutionContext.Now(), cancellation.Token));
        }

        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
    }

    [Fact]
    public void BecameProAndBiologicalAgeImprovedEvents_AreWebsiteOnly()
    {
        using var fixture = SocialJobFixture.Create();
        var occurredAtUtc = new DateTime(2026, 6, 7, 12, 0, 0, DateTimeKind.Utc);

        fixture.Events.CreateBecameProEvents(new[] { ("alice", occurredAtUtc) });
        fixture.Events.CreateBiologicalAgeImprovementEvents(new[] { ("bob", occurredAtUtc, "pheno", 44.2, 41.8) });

        var rows = fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                SELECT Type, VisibleOnWebsite, SlackProcessed, XProcessed, ThreadsProcessed, FacebookProcessed
                FROM Events
                WHERE Type IN (@becamePro, @bioImproved)
                ORDER BY Type;
                """;
            cmd.Parameters.AddWithValue("@becamePro", (int)EventType.BecamePro);
            cmd.Parameters.AddWithValue("@bioImproved", (int)EventType.BiologicalAgeImproved);

            var result = new List<(EventType Type, int VisibleOnWebsite, int SlackProcessed, int XProcessed, int ThreadsProcessed, int FacebookProcessed)>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                result.Add((
                    (EventType)reader.GetInt32(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5)));
            }

            return result;
        });

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Equal(EventType.BecamePro, row.Type);
                Assert.Equal(1, row.VisibleOnWebsite);
                Assert.Equal(1, row.SlackProcessed);
                Assert.Equal(1, row.XProcessed);
                Assert.Equal(1, row.ThreadsProcessed);
                Assert.Equal(1, row.FacebookProcessed);
            },
            row =>
            {
                Assert.Equal(EventType.BiologicalAgeImproved, row.Type);
                Assert.Equal(1, row.VisibleOnWebsite);
                Assert.Equal(1, row.SlackProcessed);
                Assert.Equal(1, row.XProcessed);
                Assert.Equal(1, row.ThreadsProcessed);
                Assert.Equal(1, row.FacebookProcessed);
            });
    }

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

        Assert.Equal((1, SocialEventSkipReason.YoungestLookingBadge.ToString()), fixture.ReadPlatformState(eventId, "Threads"));
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
    public async Task XJob_Top3FillerUploadsCurrentLeagueImageAndKeepsLeagueLink()
    {
        using var fixture = SocialJobFixture.Create(xSendSucceeds: true, seedLeaderboardAssets: true);
        var nowUtc = DateTime.UtcNow;
        foreach (var fillerType in new[]
                 {
                     FillerType.HistoryDocument,
                     FillerType.Ruleset,
                     FillerType.GitHubRepository,
                     FillerType.Donation
                 })
        {
            fixture.XFillerLog.LogPost(nowUtc, fillerType, "test cooldown");
        }

        Assert.True(fixture.LeagueImages.TryGetCurrentPayload("ultimate", out var expectedPayload));
        Assert.Equal(3, expectedPayload.Top3Slugs.Count);

        await fixture.CreateXJob().Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        Assert.Collection(
            fixture.XRequests,
            request => Assert.Equal("/1.1/media/upload.json", request.RequestUri?.AbsolutePath),
            request => Assert.Equal("/2/tweets", request.RequestUri?.AbsolutePath));

        var tweetJson = await fixture.XRequests[1].Content!.ReadAsStringAsync();
        Assert.Contains("https://longevityworldcup.com/leaderboard", tweetJson, StringComparison.Ordinal);
        Assert.Contains("\"media_ids\":[\"media-1\"]", tweetJson, StringComparison.Ordinal);

        var renderDirectory = Path.Combine(fixture.Env.WebRootPath, "generated", "og", "league");
        var renderedImagePath = Assert.Single(Directory.GetFiles(renderDirectory, "ultimate-*.png"));
        using var image = await Image.LoadAsync(renderedImagePath);
        Assert.Equal(1200, image.Width);
        Assert.Equal(630, image.Height);
        Assert.True(fixture.XFillerLog.IsOnCooldownForType(FillerType.Top3Leaderboard, TimeSpan.FromDays(7), nowUtc.AddMinutes(1)));
    }

    [Fact]
    public async Task XJob_Top3FillerUploadFailureLeavesPostRetryable()
    {
        using var fixture = SocialJobFixture.Create(
            xSendSucceeds: true,
            seedLeaderboardAssets: true,
            xMediaUploadSucceeds: false);
        var nowUtc = DateTime.UtcNow;
        foreach (var fillerType in new[]
                 {
                     FillerType.HistoryDocument,
                     FillerType.Ruleset,
                     FillerType.GitHubRepository,
                     FillerType.Donation
                 })
        {
            fixture.XFillerLog.LogPost(nowUtc, fillerType, "test cooldown");
        }

        await fixture.CreateXJob().Execute(TestJobExecutionContext.At(XDailyPostSlot()));

        var uploadRequest = Assert.Single(fixture.XRequests);
        Assert.Equal("/1.1/media/upload.json", uploadRequest.RequestUri?.AbsolutePath);
        Assert.False(fixture.XFillerLog.IsOnCooldownForType(FillerType.Top3Leaderboard, TimeSpan.FromDays(7), nowUtc.AddMinutes(1)));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task XSend_RetriesMissingPostIdWithTheSameTextAndMedia(bool retrySucceeds)
    {
        var attempts = 0;
        using var fixture = SocialJobFixture.Create(responseOverride: _ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++attempts == 2 && retrySucceeds
                    ? """{"data":{"id":"tweet-1"}}"""
                    : "{}")
            });

        var sent = await fixture.XEvents.TrySendAsync("Keep the original caption", ["media-1"]);

        Assert.Equal(retrySucceeds, sent);
        Assert.Equal(2, attempts);
        foreach (var request in fixture.XRequests)
        {
            Assert.Equal("/2/tweets", request.RequestUri?.AbsolutePath);
            var body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("\"text\":\"Keep the original caption\"", body, StringComparison.Ordinal);
            Assert.Contains("\"media_ids\":[\"media-1\"]", body, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ThreadsSend_DoesNotRestartAfterPermanentClientFailure(bool imagePost)
    {
        using var fixture = SocialJobFixture.Create(
            enableThreads: true,
            responseOverride: _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("""{"error":{"message":"Invalid parameter","code":100}}""")
            });

        var sent = imagePost
            ? await fixture.ThreadsEvents.TrySendImageAsync("Keep the original caption", " https://example.test/image.png ")
            : await fixture.ThreadsEvents.TrySendAsync("Keep the original caption");

        Assert.False(sent);
        var request = Assert.Single(fixture.ThreadsRequests);
        Assert.Equal("/me/threads", request.RequestUri?.AbsolutePath);
        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains(imagePost ? "media_type=IMAGE" : "media_type=TEXT", body, StringComparison.Ordinal);
        if (imagePost)
            Assert.Contains("image_url=https%3A%2F%2Fexample.test%2Fimage.png", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task FacebookSend_RetriesMissingPostIdWithTheSamePayload(bool imagePost, bool retrySucceeds)
    {
        var attempts = 0;
        using var fixture = SocialJobFixture.Create(
            seedLeaderboardAssets: imagePost,
            responseOverride: _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(++attempts == 2 && retrySucceeds
                    ? """{"id":"facebook-1"}"""
                    : "{}")
            });

        // A long title selects image mode while keeping the rendered body small.
        var sent = imagePost
            ? await fixture.FacebookEvents.TrySendEventAsync(
                EventType.CustomEvent,
                new string('a', 63207) + "\n\nRead [details](https://example.test).",
                "retry-image")
            : await fixture.FacebookEvents.TrySendAsync("Keep the original caption");

        Assert.Equal(retrySucceeds, sent);
        Assert.Equal(2, attempts);
        Assert.All(fixture.FacebookRequests, request =>
            Assert.Equal(imagePost ? "/v23.0/page-id/photos" : "/v23.0/page-id/feed", request.RequestUri?.AbsolutePath));
        var firstBody = await fixture.FacebookRequests[0].Content!.ReadAsStringAsync();
        Assert.Equal(firstBody, await fixture.FacebookRequests[1].Content!.ReadAsStringAsync());
        Assert.Contains(imagePost ? "retry-image.png" : "message=Keep+the+original+caption", firstBody, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("X")]
    [InlineData("Threads")]
    [InlineData("Facebook")]
    public void ImmediateCustomDispatch_ExhaustedDeliveryIsHeldUntilThisDestinationIsReset(string platform)
    {
        var repaired = false;
        using var fixture = SocialJobFixture.Create(enableThreads: true, responseOverride: request =>
            new HttpResponseMessage(repaired ? HttpStatusCode.OK : HttpStatusCode.BadRequest)
            {
                Content = new StringContent(!repaired
                    ? """{"error":{"message":"Rejected"}}"""
                    : request.RequestUri?.AbsolutePath == "/2/tweets"
                        ? """{"data":{"id":"tweet-1"}}"""
                        : """{"id":"post-1","status":"FINISHED"}""")
            });
        var eventId = fixture.InsertEvent(EventType.CustomEvent, "Original announcement\n\nKeep this exact copy.", DateTime.UtcNow,
            xProcessed: platform == "X" ? 0 : 1,
            threadsProcessed: platform == "Threads" ? 0 : 1,
            facebookProcessed: platform == "Facebook" ? 0 : 1);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            fixture.ProcessPendingImmediateCustomEvents();
            Assert.Equal(attempt < 3 ? (0, null) : (3, SocialEventSkipReason.DeliveryRetriesExhausted.ToString()),
                fixture.ReadPlatformState(eventId, platform));
        }
        var failedRequestCount = DonationRequests(fixture, platform).Count;
        fixture.ProcessPendingImmediateCustomEvents();
        Assert.Equal(failedRequestCount, DonationRequests(fixture, platform).Count);
        foreach (var other in new[] { "X", "Threads", "Facebook" }.Where(other => other != platform))
            Assert.Empty(DonationRequests(fixture, other));

        repaired = true;
        fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = $"UPDATE Events SET {platform}Processed = 0 WHERE Id = @id;";
            command.Parameters.AddWithValue("@id", eventId);
            Assert.Equal(1, command.ExecuteNonQuery());
        });
        fixture.ProcessPendingImmediateCustomEvents();
        Assert.Equal((1, null), fixture.ReadPlatformState(eventId, platform));
        var sentRequestCount = DonationRequests(fixture, platform).Count;
        Assert.True(sentRequestCount > failedRequestCount);
        fixture.ProcessPendingImmediateCustomEvents();
        Assert.Equal(sentRequestCount, DonationRequests(fixture, platform).Count);
        foreach (var other in new[] { "X", "Threads", "Facebook" }.Where(other => other != platform))
            Assert.Empty(DonationRequests(fixture, other));
    }

    [Fact]
    public void ImmediateCustomDispatch_PermanentMediaFailureIsHeldWithoutClaimingSuccess()
    {
        using var fixture = SocialJobFixture.Create(seedLeaderboardAssets: true, responseOverride: _ =>
            new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("""{"error":"Media permission rejected"}""")
            });
        var eventId = fixture.InsertEvent(EventType.CustomEvent, "Image announcement\n\n" + new string('a', 300), DateTime.UtcNow,
            xProcessed: 0);

        fixture.ProcessPendingImmediateCustomEvents();
        Assert.Equal((3, SocialEventSkipReason.PermanentDeliveryFailure.ToString()), fixture.ReadPlatformState(eventId, "X"));
        Assert.Equal("/1.1/media/upload.json", Assert.Single(fixture.XRequests).RequestUri?.AbsolutePath);
        fixture.ProcessPendingImmediateCustomEvents();
        Assert.Single(fixture.XRequests);
    }

    private static IJob DonationJob(SocialJobFixture fixture, string platform) => platform switch
    {
        "X" => fixture.CreateXJob(),
        "Threads" => fixture.CreateThreadsJob(),
        "Facebook" => fixture.CreateFacebookJob(),
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    private static List<HttpRequestMessage> DonationRequests(SocialJobFixture fixture, string platform) => platform switch
    {
        "X" => fixture.XRequests,
        "Threads" => fixture.ThreadsRequests,
        "Facebook" => fixture.FacebookRequests,
        _ => throw new ArgumentOutOfRangeException(nameof(platform))
    };

    private static void BlockPeriodicReminders(SocialJobFixture fixture)
    {
        foreach (var type in new[] { FillerType.HistoryDocument, FillerType.Ruleset, FillerType.GitHubRepository, FillerType.Donation })
        {
            fixture.XFillerLog.LogPost(DateTime.UtcNow, type, "test cooldown");
            fixture.ThreadsFillerLog.LogPost(DateTime.UtcNow, type, "test cooldown");
            fixture.FacebookFillerLog.LogPost(DateTime.UtcNow, type, "test cooldown");
        }
    }

    private static DateTimeOffset XDailyPostSlot()
    {
        var slots = new[] { 8, 12, 16, 20 };
        var day = new DateOnly(2026, 6, 6);
        return new DateTimeOffset(day.Year, day.Month, day.Day, slots[Math.Abs(day.DayNumber) % slots.Length], 0, 0, TimeSpan.Zero);
    }

    internal sealed class SocialJobFixture : IDisposable
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
            LeagueOgImageService leagueImages,
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
            LeagueImages = leagueImages;
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
        public LeagueOgImageService LeagueImages { get; }
        public AthleteCountMilestoneMemeService MilestoneMemes { get; }
        public List<HttpRequestMessage> XRequests { get; }
        public List<HttpRequestMessage> ThreadsRequests { get; }
        public List<HttpRequestMessage> FacebookRequests { get; }

        public static SocialJobFixture Create(
            bool xSendSucceeds = true,
            bool facebookSendSucceeds = true,
            Action? onFacebookRequest = null,
            bool seedLeaderboardAssets = false,
            bool xMediaUploadSucceeds = true,
            bool enableThreads = false,
            Func<HttpRequestMessage, HttpResponseMessage>? responseOverride = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "lwc-social-job-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "athletes"));
            Directory.CreateDirectory(Path.Combine(root, "generated", "thumbs", "athletes"));
            if (seedLeaderboardAssets)
                SeedLeaderboardFiles(root);

            var env = new TestWebHostEnvironment(root);
            var database = new DatabaseManager(dbPath: Path.Combine(root, "test.db"));
            var config = new Config
            {
                XAccessToken = "x-token",
                XConsumerKey = "x-consumer-key",
                XConsumerSecret = "x-consumer-secret",
                XUserAccessToken = "x-user-token",
                XUserAccessTokenSecret = "x-user-token-secret",
                ThreadsAccessToken = enableThreads ? "threads-token" : null,
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
                    request => responseOverride?.Invoke(request) ?? (request.RequestUri?.AbsolutePath == "/1.1/media/upload.json"
                        ? xMediaUploadSucceeds
                            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"media_id_string":"media-1"}""") }
                            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"media boom"}""") }
                        : xSendSucceeds
                            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"data":{"id":"tweet-1"}}""") }
                            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"boom"}""") }),
                    xRequests)),
                config,
                env,
                NullLogger<XApiClient>.Instance,
                new XDevPreviewService(NullLogger<XDevPreviewService>.Instance, httpFactory, appConfig));
            var threadsClient = new ThreadsApiClient(
                new HttpClient(new RecordingHttpHandler(
                    request => responseOverride?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"threads-1","status":"FINISHED"}""") },
                    threadsRequests)),
                config,
                NullLogger<ThreadsApiClient>.Instance);
            var facebookClient = new FacebookApiClient(
                new HttpClient(new RecordingHttpHandler(
                    request =>
                    {
                        onFacebookRequest?.Invoke();
                        return responseOverride?.Invoke(request) ?? (facebookSendSucceeds
                            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"id":"facebook-1"}""") }
                            : new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("""{"error":"boom"}""") });
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
            var leagueImages = new LeagueOgImageService(env, athletes, NullLogger<LeagueOgImageService>.Instance);
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
                leagueImages,
                milestoneMemes,
                xRequests,
                threadsRequests,
                facebookRequests);
        }

        public XDailyPostJob CreateXJob() => new(NullLogger<XDailyPostJob>.Instance, Events, XEvents, Athletes, XFillerLog, XImageService, LeagueImages, XApiClient, MilestoneMemes);

        public ThreadsDailyPostJob CreateThreadsJob() => new(NullLogger<ThreadsDailyPostJob>.Instance, Events, ThreadsEvents, Athletes, ThreadsFillerLog, MilestoneMemes);

        public FacebookDailyPostJob CreateFacebookJob() => new(NullLogger<FacebookDailyPostJob>.Instance, Events, Athletes, FacebookEvents, FacebookFillerLog);

        public BlueskyAnnouncementService CreateBlueskyService(TimeProvider time, Func<HttpRequestMessage, HttpResponseMessage> response)
        {
            var factory = new TestHttpClientFactory(new HttpClient(new RecordingHttpHandler(response, [])));
            var api = new BlueskyApiClient(factory, new Config { BlueskyIdentifier = "lwc-test.bsky.social", BlueskyAppPassword = "test-password" });
            var images = new CustomEventImageService(Env, NullLogger<CustomEventImageService>.Instance);
            var youtube = new YouTubePreviewService(factory, NullLogger<YouTubePreviewService>.Instance);
            var links = new CustomEventLinkPreviewService(factory, NullLogger<CustomEventLinkPreviewService>.Instance, youtube);
            return new BlueskyAnnouncementService(Events, new SocialDeliveryStore(Database), api, ThreadsEvents, Athletes,
                images, links, MilestoneMemes, time, NullLogger<BlueskyAnnouncementService>.Instance);
        }

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

        private static void SeedLeaderboardFiles(string destinationRoot)
        {
            var repositoryRoot = FindRepositoryRoot();
            var sourceWebRoot = Path.Combine(repositoryRoot, "LongevityWorldCup.Website", "wwwroot");
            foreach (var relativePath in new[]
                     {
                         Path.Combine("assets", "HdLogo.png"),
                         Path.Combine("assets", "custom_event.png"),
                         Path.Combine("assets", "fonts", "Poppins-Bold.ttf"),
                         Path.Combine("assets", "fonts", "Poppins-Regular.ttf")
                     })
            {
                var destinationPath = Path.Combine(destinationRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(Path.Combine(sourceWebRoot, relativePath), destinationPath);
            }

            foreach (var slug in new[] { "benjamin_garden", "siim_land", "tiat_lim", "max", "nopara73" })
            {
                var sourceAthleteDirectory = Path.Combine(sourceWebRoot, "athletes", slug);
                var destinationAthleteDirectory = Path.Combine(destinationRoot, "athletes", slug);
                Directory.CreateDirectory(destinationAthleteDirectory);
                File.Copy(
                    Path.Combine(sourceAthleteDirectory, "athlete.json"),
                    Path.Combine(destinationAthleteDirectory, "athlete.json"));

                var profilePath = Directory.EnumerateFiles(sourceAthleteDirectory, $"{slug}.*")
                    .Single(path => Path.GetExtension(path) is ".webp" or ".png" or ".jpg" or ".jpeg");
                File.Copy(profilePath, Path.Combine(destinationAthleteDirectory, Path.GetFileName(profilePath)));
            }
        }

        private static string FindRepositoryRoot()
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
                 directory is not null;
                 directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.sln")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException($"Could not find repository root from {AppContext.BaseDirectory}.");
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
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var recorded = new HttpRequestMessage(request.Method, request.RequestUri);
            if (request.Content?.Headers.ContentType?.MediaType is "application/json" or "application/x-www-form-urlencoded")
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                recorded.Content = new StringContent(body, System.Text.Encoding.UTF8, request.Content.Headers.ContentType.MediaType);
            }
            else if (request.Content?.Headers.ContentType is { } contentType)
            {
                recorded.Content = new ByteArrayContent([]);
                recorded.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType.ToString());
            }

            requests.Add(recorded);
            return respond(request);
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

    internal sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
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
        public static TestJobExecutionContext Now() => new(DateTimeOffset.UtcNow);
        public static TestJobExecutionContext At(DateTimeOffset fireTimeUtc) => new(fireTimeUtc);

        public IScheduler Scheduler => null!;
        public ITrigger Trigger => null!;
        public ICalendar Calendar => null!;
        public bool Recovering => false;
        public TriggerKey RecoveringTriggerKey => null!;
        public int RefireCount => 0;
        public int RetryAttempt => 0;
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

    }
}
