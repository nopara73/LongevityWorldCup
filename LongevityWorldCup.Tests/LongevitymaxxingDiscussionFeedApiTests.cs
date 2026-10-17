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
        using var expired = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new { accessToken = "invalid-token" });
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/api/longevitymaxxing/discussion/feed", new {
            accessToken = (string?)null,
            before = new { rankedAtUnixMs = -1, score = 0, activityAtUnixMs = 0, challengeDay = 1, participantId = "invalid", systemPostId = (string?)null }
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }
}
