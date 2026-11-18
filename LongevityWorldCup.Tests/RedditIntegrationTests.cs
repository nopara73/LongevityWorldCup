using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Controllers;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Fixture = LongevityWorldCup.Tests.SocialJobIntegrationTests.SocialJobFixture;

namespace LongevityWorldCup.Tests;

public sealed class RedditIntegrationTests
{
    private static readonly CustomEventDeliveryTargets RedditOnly = new(false, false, false, false, false, false, false, true);

    [Fact]
    public async Task Activation_BaselinesHistoricalEventsIncludingPreviouslySelectedCustomEvents()
    {
        using var fixture = Fixture.Create();
        fixture.Events.CreateCustomEvent("Before activation", "Old content", deliveryTargets: RedditOnly);
        var service = Service(fixture, new Clock(DateTimeOffset.UtcNow));
        Assert.Null(await service.GetNextAsync());
        Assert.True(new RedditDeliveryStore(fixture.Database).IsActive);
        var id = fixture.Events.CreateCustomEvent("After activation", "New content", deliveryTargets: RedditOnly);
        Assert.Equal(id, (await service.GetNextAsync())!.EventId);
    }

    [Fact]
    public async Task ExplicitCustomDestination_IsIndependentAndPreparedCopySurvivesRestartAndEdits()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        Assert.Null(await service.GetNextAsync());
        fixture.Events.CreateCustomEvent("Excluded", "", deliveryTargets: new(false, false, true, false, false));
        var id = fixture.Events.CreateCustomEvent("Keep this title!", "Keep this full body", deliveryTargets: RedditOnly);
        var first = (await service.GetNextAsync())!;
        Assert.Equal(id, first.EventId);
        Assert.Contains("Keep this full body", first.Text);
        Assert.DoesNotContain("/events?", first.Text);
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "UPDATE Events SET Text = 'Edited afterward' WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        });
        Assert.Equal(first, await Service(fixture, clock).GetNextAsync());
        Assert.Empty(fixture.XRequests);
        Assert.Empty(fixture.ThreadsRequests);
        Assert.Empty(fixture.FacebookRequests);
    }

    [Fact]
    public async Task Bridge_StartIsDurableAndReceiptIsIdempotentWithoutReopeningCompletedDelivery()
    {
        using var fixture = Fixture.Create();
        var now = DateTimeOffset.UtcNow;
        var service = Service(fixture, new Clock(now));
        await service.GetNextAsync();
        fixture.Events.CreateCustomEvent("One post", "", deliveryTargets: RedditOnly);
        var post = (await service.GetNextAsync())!;
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.False(store.Complete(post.DeliveryId, "t3_abc123", now));
        Assert.Equal(RedditBeginResult.Started, store.Begin(post.DeliveryId, now));
        Assert.Equal(RedditBeginResult.AlreadyStarted, new RedditDeliveryStore(fixture.Database).Begin(post.DeliveryId, now.AddMinutes(1)));
        Assert.True(store.RequireReview(post.DeliveryId, now));
        Assert.Null(await service.GetNextAsync());
        // A late known receipt can resolve an uncertain outcome; it cannot change a sent receipt.
        Assert.True(store.Complete(post.DeliveryId, "t3_abc123", now));
        Assert.True(store.Complete(post.DeliveryId, "t3_abc123", now.AddMinutes(1)));
        Assert.False(store.Complete(post.DeliveryId, "t3_different", now));
        Assert.False(store.RequireReview(post.DeliveryId, now));
        Assert.Equal("sent", store.Find(post.DeliveryId)!.Status);
        Assert.Null(await Service(fixture, new Clock(now.AddDays(3))).GetNextAsync());
    }

    [Fact]
    public async Task LostBeginBeforeServerMarker_RequiresReviewWithoutBlockingTheQueue()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        fixture.Events.CreateCustomEvent("Unconfirmed start", "", deliveryTargets: RedditOnly);
        var first = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        var store = new RedditDeliveryStore(fixture.Database);
        // The app persisted its own start marker, but its begin request never
        // committed on the server. Retain the delivery for review and move on.
        Assert.True(store.RequireReview(first.DeliveryId, clock.GetUtcNow().AddMinutes(46)));
        Assert.Equal(RedditBeginResult.NotPending, store.Begin(first.DeliveryId, clock.GetUtcNow()));
        Assert.False(store.Complete(first.DeliveryId, "t3_unstarted", clock.GetUtcNow()));
        Assert.Equal("review", store.Find(first.DeliveryId)!.Status);
        var nextId = fixture.Events.CreateCustomEvent("Next announcement", "", deliveryTargets: RedditOnly);
        Assert.Equal(nextId, Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync()).EventId);
    }

    [Fact]
    public async Task DailyQuota_SurvivesRestartAndDoesNotLimitSelectedCustomEvents()
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        var leader = fixture.Athletes.GetAthletesForX().Single(a => a.CurrentRank == 1);
        fixture.InsertEvent(EventType.NewRank, $"slug[{leader.Slug}] rank[1]", clock.GetUtcNow().UtcDateTime);
        await service.PrepareDailyAsync();
        var first = (await service.GetNextAsync())!;
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, store.Begin(first.DeliveryId, clock.GetUtcNow()));
        Assert.True(store.Complete(first.DeliveryId, "t3_first", clock.GetUtcNow()));
        Assert.False(new RedditDeliveryStore(fixture.Database).ReserveDailyDelivery("another-event", clock.GetUtcNow()));
        var customId = fixture.Events.CreateCustomEvent("Competition announcement", "This is deliberately selected.", deliveryTargets: RedditOnly);
        var custom = Assert.IsType<RedditDeliveryPayload>(await Service(fixture, clock).GetNextAsync());
        Assert.Equal(customId, custom.EventId);
        Assert.Equal(RedditBeginResult.Started, store.Begin(custom.DeliveryId, clock.GetUtcNow()));
        Assert.True(store.ReserveDailyDelivery("tomorrows-event", clock.GetUtcNow().AddDays(1)));
    }

    [Fact]
    public async Task ProfileOnlyEvents_AreTerminallyExcludedFromReddit()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        fixture.Events.CreateBecameProEvents([("alice", clock.GetUtcNow().UtcDateTime)]);
        fixture.Events.CreateBiologicalAgeImprovementEvents([("bob", clock.GetUtcNow().UtcDateTime, "pheno", 44.2, 41.8)]);
        fixture.InsertEvent(EventType.TestResultAccepted, "Profile-only accepted result", clock.GetUtcNow().UtcDateTime);
        await service.PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Reddit, false, clock.GetUtcNow()));
    }

    [Fact]
    public async Task AthleteHighlights_RespectFreshnessAndTwoDaySubjectCooldown()
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        var leader = fixture.Athletes.GetAthletesForX().Single(a => a.CurrentRank == 1);
        var text = $"slug[{leader.Slug}] rank[1]";
        fixture.InsertEvent(EventType.NewRank, text, clock.GetUtcNow().UtcDateTime.AddDays(-8));
        await service.PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        var id = fixture.InsertEvent(EventType.NewRank, text, clock.GetUtcNow().UtcDateTime);
        await service.PrepareDailyAsync();
        var first = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        Assert.Equal(id, first.EventId);
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, store.Begin(first.DeliveryId, clock.GetUtcNow()));
        Assert.True(store.Complete(first.DeliveryId, "t3_benjamin", clock.GetUtcNow()));
        clock.Current = clock.Current.AddDays(1);
        var nextId = fixture.InsertEvent(EventType.NewRank, text, clock.GetUtcNow().UtcDateTime);
        await service.PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        clock.Current = clock.Current.AddDays(1).AddMinutes(1);
        await service.PrepareDailyAsync();
        Assert.Equal(nextId, Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync()).EventId);
    }

    [Fact]
    public async Task InvalidBridgeAuthentication_CannotActivateOrConsumeTheQueue()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = new Config { RedditEnabled = true, RedditBridgeSecretHash = TestHash() };
        var controller = Controller(fixture, clock, config);
        controller.Request.Headers.Authorization = "Bearer incorrect-test-key";
        Assert.IsType<UnauthorizedResult>(await controller.Next(default));
        Assert.False(new RedditDeliveryStore(fixture.Database).IsActive);
        controller.Request.Headers.Authorization = "Bearer test-only-reddit-key";
        config.RedditBridgeSecretHash = "";
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Next(default)).StatusCode);
        Assert.False(new RedditDeliveryStore(fixture.Database).IsActive);
    }

    [Fact]
    public async Task Controller_ValidatesReceiptIdentityAndConflictingDuplicates()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var controller = Controller(fixture, clock, new Config { RedditEnabled = true, RedditBridgeSecretHash = TestHash() });
        controller.Request.Headers.Authorization = "Bearer test-only-reddit-key";
        Assert.IsType<NoContentResult>(await controller.Next(default));
        fixture.Events.CreateCustomEvent("New post", "", deliveryTargets: RedditOnly);
        var delivery = Assert.IsType<RedditDeliveryPayload>(Assert.IsType<OkObjectResult>(await controller.Next(default)).Value);
        Assert.IsType<OkObjectResult>(controller.Begin(new(delivery.DeliveryId)));
        Assert.IsType<ConflictObjectResult>(controller.Begin(new(delivery.DeliveryId)));
        Assert.IsType<BadRequestResult>(controller.Receipt(new(delivery.DeliveryId, "https://example.com")));
        Assert.IsType<OkObjectResult>(controller.Receipt(new(delivery.DeliveryId, "t3_abc123")));
        Assert.IsType<OkObjectResult>(controller.Receipt(new(delivery.DeliveryId, "t3_abc123")));
        Assert.IsType<ConflictResult>(controller.Receipt(new(delivery.DeliveryId, "t3_other")));
        Assert.IsType<NoContentResult>(await controller.Next(default));
    }

    [Fact]
    public async Task CustomEventApi_DoesNotPromiseRedditDeliveryBeforeActivation()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = new Config { RedditEnabled = true, CustomEventDesignerSecretHash = TestHash() };
        var store = new RedditDeliveryStore(fixture.Database);
        var controller = new CustomEventsController(fixture.Events, config, NullLogger<CustomEventsController>.Instance, store);
        var request = new CustomEventsController.CreateCustomEventRequest("test-only-reddit-key", "A new announcement", "",
            false, false, false, false, false, false, false, true);
        Assert.Equal(503, Assert.IsType<ObjectResult>(controller.Create(request)).StatusCode);
        Assert.Empty(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Reddit, true, clock.GetUtcNow()));
        await Service(fixture, clock, config).GetNextAsync();
        Assert.IsType<OkObjectResult>(controller.Create(request));
        Assert.Equal(EventType.CustomEvent, Assert.Single(new SocialDeliveryStore(fixture.Database).GetPending(SocialDeliveryStore.Reddit, true, clock.GetUtcNow())).Event.Type);
    }

    [Fact]
    public async Task CustomEventApi_SkipsOnlyRedditAndReportsTheBlockedLink()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = new Config { RedditEnabled = true, CustomEventDesignerSecretHash = TestHash() };
        var store = new RedditDeliveryStore(fixture.Database);
        await Service(fixture, clock, config).GetNextAsync();
        var controller = new CustomEventsController(fixture.Events, config, NullLogger<CustomEventsController>.Instance, store);
        const string link = "https://IFT.TT./article";
        var request = new CustomEventsController.CreateCustomEventRequest("test-only-reddit-key", "A new announcement",
            $"[Source]({link})", true, false, false, false, false, SendToReddit: true);

        var queued = JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(controller.Create(request)).Value);
        Assert.Equal(link, queued.GetProperty("blockedRedditLink").GetString());
        Assert.Equal("webpage", Assert.Single(queued.GetProperty("queuedTargets").EnumerateArray()).GetString());
        Assert.True(fixture.Events.HasCustomEventWithTitle(request.Title!));
        Assert.Null(await Service(fixture, clock, config).GetNextAsync());

        var redditOnly = request with { Title = "Reddit only", SendToWebpage = false };
        var rejected = JsonSerializer.SerializeToElement(Assert.IsType<BadRequestObjectResult>(controller.Create(redditOnly)).Value);
        Assert.Equal(link, rejected.GetProperty("blockedRedditLink").GetString());
        Assert.False(fixture.Events.HasCustomEventWithTitle(redditOnly.Title!));
    }

    [Fact]
    public async Task LearnedBanStopsAnAlreadyPreparedPostWithoutResubmittingAnyAttempt()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var service = Service(fixture, clock, config);
        await service.GetNextAsync();
        const string link = "https://publisher.example/rejected-article";
        fixture.Events.CreateCustomEvent("An announcement", $"[Source]({link})", deliveryTargets: RedditOnly);
        var prepared = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = "CREATE TABLE RedditBlockedLinks(Link TEXT PRIMARY KEY); INSERT INTO RedditBlockedLinks VALUES (@link);";
            cmd.Parameters.AddWithValue("@link", link);
            cmd.ExecuteNonQuery();
        });
        Assert.Null(await service.ClaimNextIftttAsync(default));
        Assert.Equal(("skipped", "BlockedLink", (string?)null), DeliveryState(fixture, prepared.EventId));
        Assert.Equal(new[] { "existing.example", link }, new RedditDeliveryStore(fixture.Database).GetBlockedLinks(["existing.example"]));
        Assert.Null(RedditLinkPolicy.FindBlockedLink("https://publisher.example/another-article", [link]));
    }

    [Fact]
    public void OversizedCustomText_IsRejectedInsteadOfDroppingTheBodyForAnImageCaption()
    {
        var item = new EventItem("event-oversized", EventType.CustomEvent,
            "Keep the whole announcement\n\n" + new string('x', RedditPost.MaxTextLength), DateTime.UtcNow, 10, true);
        Assert.Null(RedditPost.Build(item, "", slug => slug));
    }

    [Fact]
    public void LongUnicodeTitle_IsShortenedWithoutLosingBodyOrSplittingTextElements()
    {
        var item = new EventItem("event-1", EventType.CustomEvent, string.Concat(Enumerable.Repeat("😀", 200)) + "\n\nFull body", DateTime.UtcNow, 10, true);
        var post = RedditPost.Build(item, "", slug => slug)!;
        Assert.InRange(post.Title.Length, 1, RedditPost.MaxTitleLength);
        Assert.EndsWith("…", post.Title);
        Assert.Contains("Full body", post.Text);
        Assert.Contains("https://longevityworldcup.com/events?event=event-1", post.Text);
        Assert.False(char.IsHighSurrogate(post.Title[^2]));
    }

    [Fact]
    public async Task IftttDispatch_BaselinesHistoryAndWaitsForTheMatchingRedditPost()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var store = new RedditDeliveryStore(fixture.Database);
        var announcements = Service(fixture, clock, config);
        using var ifttt = new RecordingIftttClient();
        var publisher = new RedditIftttPublisher(config, announcements, store, ifttt, clock, NullLogger<RedditIftttPublisher>.Instance);

        fixture.Events.CreateCustomEvent("Historical", "Keep this out of Reddit", deliveryTargets: RedditOnly);
        await publisher.DispatchAsync(default);
        Assert.Empty(ifttt.Requests);
        Assert.True(store.IsActive);

        fixture.Events.CreateCustomEvent("IFTTT announcement", "One & two\n\nAnother paragraph", deliveryTargets: RedditOnly);
        var delivery = Assert.IsType<RedditDeliveryPayload>(await announcements.GetNextAsync());
        await publisher.DispatchAsync(default);
        await publisher.DispatchAsync(default);
        var sent = Assert.Single(ifttt.Requests);
        Assert.Equal("/trigger/lwc_reddit_announcement/with/key/test-webhooks-key", sent.Path);
        using var payload = JsonDocument.Parse(sent.Body);
        Assert.Equal(delivery.Title, payload.RootElement.GetProperty("value1").GetString());
        var outgoingBody = Assert.IsType<string>(payload.RootElement.GetProperty("value2").GetString());
        Assert.Contains("One &amp; two<br><br>Another paragraph", outgoingBody);
        Assert.Equal("pending", store.Find(delivery.DeliveryId)!.Status);

        var controller = IftttController(store, clock, config);
        var receipt = new RedditIftttReceiptsController.ReceiptRequest("LongevityWorldCup", "nopara73",
            "http://www.reddit.com/r/LongevityWorldCup/comments/abc123/a_post/", delivery.Title,
            outgoingBody, clock.Current.ToUnixTimeSeconds());
        controller.Request.Headers.Authorization = "Bearer incorrect";
        Assert.IsType<UnauthorizedResult>(controller.Receive(receipt));
        controller.Request.Headers.Authorization = "Bearer test-only-reddit-key";
        Assert.IsType<BadRequestResult>(controller.Receive(receipt with { Author = "someone-else" }));
        Assert.IsType<BadRequestResult>(controller.Receive(receipt with { PostUrl = "https://www.reddit.com/r/elsewhere/comments/abc123/" }));
        Assert.False(Matched(controller.Receive(receipt with { Text = "Different content" })));
        Assert.False(Matched(controller.Receive(receipt with { PostedAtUnixSeconds = receipt.PostedAtUnixSeconds - 1 })));

        clock.Current = clock.Current.AddHours(3);
        await publisher.DispatchAsync(default);
        Assert.Equal("review", store.Find(delivery.DeliveryId)!.Status);
        Assert.Single(ifttt.Requests);
        config.RedditEnabled = false;
        Assert.True(Matched(controller.Receive(receipt)));
        Assert.True(Matched(controller.Receive(receipt)));
        Assert.False(Matched(controller.Receive(receipt with { PostUrl = "https://www.reddit.com/r/LongevityWorldCup/comments/duplicate/" })));
        Assert.Equal("t3_abc123", store.Find(delivery.DeliveryId)!.PostId);
        var canonicalUrl = fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT RemoteUri FROM SocialDeliveries WHERE Platform = 'reddit' AND RecordKey = @key;";
            command.Parameters.AddWithValue("@key", delivery.DeliveryId);
            return (string?)command.ExecuteScalar();
        });
        Assert.Equal("https://www.reddit.com/r/LongevityWorldCup/comments/abc123/", canonicalUrl);
    }

    [Fact]
    public async Task IftttDispatch_SkipsPreviouslyPreparedBannedLinkAndSendsTheNextAllowedLink()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var announcements = Service(fixture, clock, config);
        await announcements.GetNextAsync();
        const string banned = "https://news.example/blocked?article=1";
        fixture.Events.CreateCustomEvent("Prepared announcement", $"[Source]({banned})", deliveryTargets: RedditOnly);
        var prepared = Assert.IsType<RedditDeliveryPayload>(await announcements.GetNextAsync());
        config.RedditBlockedLinks = [banned, "blocked.example"];
        fixture.Events.CreateCustomEvent("Blocked domain", "See https://www.blocked.example/article.", deliveryTargets: RedditOnly);
        const string allowed = "https://news.example/blocked?article=2";
        fixture.Events.CreateCustomEvent("Next announcement", $"See {allowed} or https://ift.tt.example/article.",
            deliveryTargets: RedditOnly);
        var store = new RedditDeliveryStore(fixture.Database);
        using var ifttt = new RecordingIftttClient();
        var publisher = new RedditIftttPublisher(config, announcements, store, ifttt, clock, NullLogger<RedditIftttPublisher>.Instance);

        await publisher.DispatchAsync(default);
        await publisher.DispatchAsync(default);

        var sent = Assert.Single(ifttt.Requests);
        using var payload = JsonDocument.Parse(sent.Body);
        Assert.Equal("Next announcement", payload.RootElement.GetProperty("value1").GetString());
        Assert.Contains(allowed, payload.RootElement.GetProperty("value2").GetString());
        Assert.Equal("skipped", store.Find(prepared.DeliveryId)!.Status);
        Assert.False(fixture.Database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM RedditIftttAttempts WHERE RecordKey = @key);";
            command.Parameters.AddWithValue("@key", prepared.DeliveryId);
            return (long)command.ExecuteScalar()! != 0;
        }));
    }

    [Fact]
    public async Task Ifttt_IdenticalCopyWaitsForANewReceiptWindowAndPostIdsAreNotReassigned()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var store = new RedditDeliveryStore(fixture.Database);
        var announcements = Service(fixture, clock, config);
        using var ifttt = new RecordingIftttClient();
        var publisher = new RedditIftttPublisher(config, announcements, store, ifttt, clock, NullLogger<RedditIftttPublisher>.Instance);
        var controller = IftttController(store, clock, config);
        await publisher.DispatchAsync(default);

        fixture.Events.CreateCustomEvent("Repeated announcement", "Same copy", deliveryTargets: RedditOnly);
        var first = Assert.IsType<RedditDeliveryPayload>(await announcements.GetNextAsync());
        await publisher.DispatchAsync(default);
        var receipt = new RedditIftttReceiptsController.ReceiptRequest("LongevityWorldCup", "nopara73",
            "https://www.reddit.com/r/LongevityWorldCup/comments/abc123/", first.Title, first.Text, clock.Current.ToUnixTimeSeconds());
        Assert.True(Matched(controller.Receive(receipt)));

        clock.Current = clock.Current.AddMinutes(1);
        fixture.Events.CreateCustomEvent("Repeated announcement", "Same copy", deliveryTargets: RedditOnly);
        var second = Assert.IsType<RedditDeliveryPayload>(await announcements.GetNextAsync());
        await publisher.DispatchAsync(default);
        Assert.Single(ifttt.Requests);

        clock.Current = clock.Current.AddHours(2);
        await publisher.DispatchAsync(default);
        Assert.Equal(2, ifttt.Requests.Count);
        var nextReceipt = receipt with { PostedAtUnixSeconds = clock.Current.ToUnixTimeSeconds() };
        Assert.False(Matched(controller.Receive(nextReceipt)));
        Assert.True(Matched(controller.Receive(nextReceipt with { PostUrl = "https://www.reddit.com/r/LongevityWorldCup/comments/abc124/" })));
        Assert.Equal("t3_abc124", store.Find(second.DeliveryId)!.PostId);
    }

    [Fact]
    public async Task EditorialSelection_SkipsRoutineUpdatesOnlyOnRedditAndKeepsTheCurrentOverallLead()
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        var roster = fixture.Athletes.GetAthletesForX();
        var leader = roster.Single(a => a.CurrentRank == 1);
        var previous = roster.Single(a => a.CurrentRank == 2);
        var excluded = new List<string>();
        foreach (var type in Enum.GetValues<EventType>().Where(t => t is not EventType.NewRank and not EventType.CustomEvent))
            excluded.Add(fixture.InsertEvent(type, "Routine update", clock.Current.UtcDateTime));
        foreach (var rank in new[] { 2, 3, 10 })
            excluded.Add(fixture.InsertEvent(EventType.NewRank, $"slug[{leader.Slug}] rank[{rank}]", clock.Current.UtcDateTime));
        excluded.Add(fixture.InsertEvent(EventType.NewRank, $"slug[{previous.Slug}] rank[1]", clock.Current.UtcDateTime.AddMinutes(-2)));
        var leadId = fixture.InsertEvent(EventType.NewRank, $"slug[{leader.Slug}] rank[1]", clock.Current.UtcDateTime);

        await service.PrepareDailyAsync();
        var post = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        Assert.Equal(leadId, post.EventId);
        Assert.Contains(leader.Name, post.Title);
        Assert.Contains("lowest submitted", post.Text);
        Assert.Contains("biological age minus chronological age", post.Text);
        Assert.Contains(previous.Name, post.Text);
        // A second selection drains lower-priority routine Events only after this
        // prepared post is resolved, so exercise each through the dispatch gate too.
        foreach (var id in excluded)
        {
            var prepared = new SocialDeliveryStore(fixture.Database);
            var pending = prepared.GetPending(SocialDeliveryStore.Reddit, false, clock.Current).FirstOrDefault(x => x.Event.Id == id);
            if (pending is not null)
                prepared.Prepare(SocialDeliveryStore.Reddit, id, JsonSerializer.Serialize(new RedditPostRequest("Old caption", "Old body")), null, clock.Current);
        }
        var reddit = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, reddit.Begin(post.DeliveryId, clock.Current));
        Assert.True(reddit.Complete(post.DeliveryId, "t3_leader", clock.Current));
        Assert.Null(await service.GetNextAsync());
        Assert.All(excluded, id => Assert.Equal("skipped", DeliveryState(fixture, id).Status));
        Assert.All(excluded, id => Assert.Equal((1, null), fixture.ReadPlatformState(id, "X")));
        Assert.Empty(fixture.ThreadsRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EditorialSelection_RechecksLegacyPreparedPostsAndReleasesOnlyUnstartedReservations(bool beginDirectly)
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var service = Service(fixture, clock, config);
        await service.GetNextAsync();
        var oldId = fixture.InsertEvent(EventType.AthleteCountMilestone, "athletes[250]", clock.Current.UtcDateTime);
        var store = new RedditDeliveryStore(fixture.Database);
        var old = new SocialDeliveryStore(fixture.Database).Prepare(SocialDeliveryStore.Reddit, oldId,
            JsonSerializer.Serialize(new RedditPostRequest("250 athletes", "Routine milestone")), null, clock.Current);
        Assert.True(store.ReserveDailyDelivery(oldId, clock.Current));
        if (beginDirectly)
            Assert.Equal(RedditBeginResult.NotPending, store.Begin(old.Key, clock.Current, "unused-fingerprint"));
        else
            Assert.Null(await service.ClaimNextIftttAsync(default));
        Assert.Equal(("skipped", RedditEventPolicy.RoutineUpdate, (string?)null), DeliveryState(fixture, oldId));
        var leader = fixture.Athletes.GetAthletesForX().Single(a => a.CurrentRank == 1);
        var newId = fixture.InsertEvent(EventType.NewRank, $"slug[{leader.Slug}] rank[1]", clock.Current.UtcDateTime);
        await Service(fixture, clock, config).PrepareDailyAsync();
        var next = Assert.IsType<RedditDeliveryPayload>(await service.ClaimNextIftttAsync(default));
        Assert.Equal(newId, next.EventId);
        Assert.NotNull(DeliveryState(fixture, newId).FirstAttempt);
    }

    [Fact]
    public async Task EditorialSelection_DoesNotRewriteOrRetryAnOldAttemptAndItsAutomaticReceiptStillWorks()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var config = IftttConfig();
        var service = Service(fixture, clock, config);
        await service.GetNextAsync();
        var oldId = fixture.InsertEvent(EventType.DonationReceived, "sats[8455]", clock.Current.UtcDateTime);
        var post = new RedditPostRequest("Existing post", "Already submitted before the policy change");
        var prepared = new SocialDeliveryStore(fixture.Database).Prepare(SocialDeliveryStore.Reddit, oldId,
            JsonSerializer.Serialize(post), null, clock.Current);
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.True(store.ReserveDailyDelivery(oldId, clock.Current));
        fixture.Database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = """
                UPDATE SocialDeliveries SET FirstAttemptAtUtc = @now WHERE RecordKey = @key AND Platform = 'reddit';
                INSERT INTO RedditIftttAttempts (RecordKey, Fingerprint) VALUES (@key, @fingerprint);
                """;
            cmd.Parameters.AddWithValue("@now", clock.Current.UtcDateTime.ToString("o"));
            cmd.Parameters.AddWithValue("@key", prepared.Key);
            cmd.Parameters.AddWithValue("@fingerprint", RedditIftttPost.Fingerprint(post.Title, post.Text));
            cmd.ExecuteNonQuery();
        });
        store.SkipUnstarted(oldId, RedditEventPolicy.RoutineUpdate, clock.Current);
        await service.PrepareDailyAsync();
        Assert.Null(await service.ClaimNextIftttAsync(default));
        Assert.Equal("pending", DeliveryState(fixture, oldId).Status);
        Assert.False(store.ReserveDailyDelivery("another", clock.Current));
        Assert.Equal(prepared.Json, store.Find(prepared.Key)!.Json);
        var receipt = new RedditIftttReceiptsController.ReceiptRequest("LongevityWorldCup", "nopara73",
            "https://www.reddit.com/r/LongevityWorldCup/comments/existing/", post.Title, post.Text, clock.Current.ToUnixTimeSeconds());
        Assert.True(Matched(IftttController(store, clock, config).Receive(receipt)));
        store.SkipUnstarted(oldId, RedditEventPolicy.RoutineUpdate, clock.Current);
        Assert.Equal("sent", DeliveryState(fixture, oldId).Status);
        Assert.Equal("t3_existing", store.Find(prepared.Key)!.PostId);
    }

    [Fact]
    public async Task SupersededLeadDoesNotResurfaceAfterTheAthleteCooldown()
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        var leader = fixture.Athletes.GetAthletesForX().Single(a => a.CurrentRank == 1);
        var text = $"slug[{leader.Slug}] rank[1]";
        var oldId = fixture.InsertEvent(EventType.NewRank, text, clock.Current.UtcDateTime.AddHours(-1));
        var latestId = fixture.InsertEvent(EventType.NewRank, text, clock.Current.UtcDateTime);
        await service.PrepareDailyAsync();
        var latest = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        Assert.Equal(latestId, latest.EventId);
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, store.Begin(latest.DeliveryId, clock.Current));
        Assert.True(store.Complete(latest.DeliveryId, "t3_latest", clock.Current));
        clock.Current = clock.Current.AddDays(3);
        await Service(fixture, clock).PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        Assert.Equal(("skipped", "SupersededLeader", (string?)null), DeliveryState(fixture, oldId));
    }

    [Theory]
    [InlineData(true, "StalePrimaryEvent")]
    [InlineData(false, "SupersededLeader")]
    public async Task BeginRechecksALeadThatChangedSincePreparation(bool expired, string reason)
    {
        using var fixture = Fixture.Create(seedLeaderboardAssets: true);
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        var leader = fixture.Athletes.GetAthletesForX().Single(a => a.CurrentRank == 1);
        var text = $"slug[{leader.Slug}] rank[1]";
        var id = fixture.InsertEvent(EventType.NewRank, text, clock.Current.UtcDateTime);
        await service.PrepareDailyAsync();
        var prepared = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        if (expired) clock.Current = clock.Current.AddDays(8);
        else fixture.InsertEvent(EventType.NewRank, text, clock.Current.UtcDateTime.AddSeconds(1));
        Assert.Equal(RedditBeginResult.NotPending, new RedditDeliveryStore(fixture.Database).Begin(prepared.DeliveryId, clock.Current));
        Assert.Equal(("skipped", reason, (string?)null), DeliveryState(fixture, id));
    }

    private static (string Status, string? Reason, string? FirstAttempt) DeliveryState(Fixture fixture, string id) => fixture.Database.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT Status, LastErrorCode, FirstAttemptAtUtc FROM SocialDeliveries WHERE Platform = 'reddit' AND EventId = @id;";
        cmd.Parameters.AddWithValue("@id", id);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
    });

    private static Config IftttConfig() => new()
    {
        RedditEnabled = true,
        RedditUseIfttt = true,
        RedditIftttWebhookKey = "test-webhooks-key",
        RedditIftttReceiptSecretHash = TestHash()
    };

    private static RedditIftttReceiptsController IftttController(RedditDeliveryStore store, TimeProvider clock, Config config)
    {
        var controller = new RedditIftttReceiptsController(config, store, clock)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.Request.Headers.Authorization = "Bearer test-only-reddit-key";
        return controller;
    }

    private static bool Matched(IActionResult response) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(response).Value).GetProperty("matched").GetBoolean();

    private sealed class RecordingIftttClient : HttpMessageHandler, IHttpClientFactory
    {
        internal List<(string Path, string Body)> Requests { get; } = [];

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false)
        {
            BaseAddress = new Uri("https://maker.ifttt.com/")
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.PathAndQuery, await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private static RedditAnnouncementService Service(Fixture fixture, TimeProvider clock, Config? config = null) => new(
        config ?? new Config { RedditEnabled = true }, fixture.Events, new SocialDeliveryStore(fixture.Database),
        new RedditDeliveryStore(fixture.Database), fixture.Athletes, clock);

    private static RedditBridgeController Controller(Fixture fixture, TimeProvider clock, Config config) => new(
        config, Service(fixture, clock, config), new RedditDeliveryStore(fixture.Database), clock)
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

    private static string TestHash()
    {
        var salt = new byte[16];
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("test-only-reddit-key"), salt, 210000, HashAlgorithmName.SHA256, 32);
        return $"pbkdf2-sha256:210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Current;
    }
}
