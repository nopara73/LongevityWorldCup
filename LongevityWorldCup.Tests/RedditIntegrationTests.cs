using System.Security.Cryptography;
using System.Text;
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
    public async Task DailyQuota_SurvivesRestartAndDonationFreshnessDoesNotExpireAcknowledgments()
    {
        using var fixture = Fixture.Create();
        var clock = new Clock(DateTimeOffset.UtcNow);
        var service = Service(fixture, clock);
        await service.GetNextAsync();
        fixture.Events.CreateDonationReceivedEvents([("reddit-late-donation", clock.GetUtcNow().UtcDateTime.AddDays(-30), 8455L)]);
        await service.PrepareDailyAsync();
        var first = (await service.GetNextAsync())!;
        Assert.Contains("0.00008455 BTC", first.Text);
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, store.Begin(first.DeliveryId, clock.GetUtcNow()));
        Assert.True(store.Complete(first.DeliveryId, "t3_first", clock.GetUtcNow()));
        fixture.Events.CreateDonationReceivedEvents([("reddit-second-donation", clock.GetUtcNow().UtcDateTime, 12000L)]);
        await Service(fixture, clock).PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        clock.Current = clock.Current.AddDays(1);
        await service.PrepareDailyAsync();
        var second = (await service.GetNextAsync())!;
        Assert.NotEqual(first.EventId, second.EventId);
        Assert.Equal(RedditBeginResult.Started, store.Begin(second.DeliveryId, clock.GetUtcNow()));
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
        fixture.InsertEvent(EventType.NewRank, "slug[benjamin_garden] rank[1]", clock.GetUtcNow().UtcDateTime.AddDays(-8));
        await service.PrepareDailyAsync();
        Assert.Null(await service.GetNextAsync());
        var id = fixture.InsertEvent(EventType.NewRank, "slug[benjamin_garden] rank[1]", clock.GetUtcNow().UtcDateTime);
        await service.PrepareDailyAsync();
        var first = Assert.IsType<RedditDeliveryPayload>(await service.GetNextAsync());
        Assert.Equal(id, first.EventId);
        var store = new RedditDeliveryStore(fixture.Database);
        Assert.Equal(RedditBeginResult.Started, store.Begin(first.DeliveryId, clock.GetUtcNow()));
        Assert.True(store.Complete(first.DeliveryId, "t3_benjamin", clock.GetUtcNow()));
        clock.Current = clock.Current.AddDays(1);
        // The small-field social policy only announces first place; keep the next
        // highlight eligible so this assertion measures the subject cooldown.
        var nextId = fixture.InsertEvent(EventType.NewRank, "slug[benjamin_garden] rank[1]", clock.GetUtcNow().UtcDateTime);
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

    private static RedditAnnouncementService Service(Fixture fixture, TimeProvider clock, Config? config = null) => new(
        config ?? new Config { RedditEnabled = true }, fixture.Events, new SocialDeliveryStore(fixture.Database),
        new RedditDeliveryStore(fixture.Database), fixture.ThreadsEvents, fixture.Athletes, clock);

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
