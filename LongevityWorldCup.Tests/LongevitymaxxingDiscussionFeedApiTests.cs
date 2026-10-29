using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class LongevitymaxxingDiscussionFeedApiTests(TestWebApplicationFactory factory)
{
    [Fact]
    public async Task DiscussionFeedApi_BoundsPublicPagesAndRejectsInvalidAccessAndCursors()
    {
        using var client = factory.CreateClient();
        using var page = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new { accessToken = (string?)null });
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var body = JsonNode.Parse(await page.Content.ReadAsStringAsync())!;
        Assert.InRange(body["threads"]!.AsArray().Count, 0, 10);
        Assert.True(body.AsObject().ContainsKey("nextCursor"));
        Assert.True(page.Headers.CacheControl?.NoStore is true);
        using var expired = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new { accessToken = "invalid-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new {
            accessToken = (string?)null,
            before = new { rankedAtUnixMs = -1, score = 0, activityAtUnixMs = 0, challengeDay = 1, participantId = "invalid", systemPostId = (string?)null }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task DiscussionReadApi_RequiresAccessAndReturnsAnUncachedTypedRestartForOldCursors()
    {
        using var client = factory.CreateClient();
        var postParticipantId = Guid.NewGuid().ToString("N");
        using var read = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/read", new {
            accessToken = "invalid-token", postParticipantId, challengeDay = 1, systemPostId = (string?)null,
            observedRevision = 0, notificationIds = Array.Empty<string>()
        });
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.True(read.Headers.CacheControl?.NoStore is true);
        using var thread = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/thread", new {
            accessToken = "invalid-token", postParticipantId, challengeDay = 1, systemPostId = (string?)null
        });
        Assert.Equal(HttpStatusCode.Unauthorized, thread.StatusCode);
        Assert.True(thread.Headers.CacheControl?.NoStore is true);
        using var old = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new {
            accessToken = (string?)null, before = new { rankedAtUnixMs = 1, score = 0, activityAtUnixMs = 1,
                participantId = postParticipantId, challengeDay = 1, systemPostId = (string?)null }
        });
        Assert.Equal(HttpStatusCode.Conflict, old.StatusCode);
        Assert.Equal("discussion_feed_changed", (string?)JsonNode.Parse(await old.Content.ReadAsStringAsync())!["code"]);
        using var publicState = await client.GetAsync("/api/longevitymaxxing/state");
        var state = JsonNode.Parse(await publicState.Content.ReadAsStringAsync())!.AsObject();
        Assert.False(state.ContainsKey("readContext"));
    }
}
