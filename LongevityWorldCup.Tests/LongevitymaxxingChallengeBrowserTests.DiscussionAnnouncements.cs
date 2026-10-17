using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    private const string DiscussionAnnouncementId = "93679f8f8cd44a46a1533db881fe68ef";

    [Theory]
    [InlineData(390, false)]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    [InlineData(1440, true)]
    public async Task DiscussionAnnouncements_RenderConciseCardsAndKeepWelcomeRepliesSeparate(int width, bool signedIn)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 } });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true,
            includeJoinDiscussionPost: true, joinDiscussionHasReply: true))!.AsObject();
        state["notes"] = new JsonArray();
        state["public"]!["notes"] = new JsonArray();
        var posts = state["public"]!["systemDiscussionPosts"]!.AsArray();
        posts.Add(new JsonObject
        {
            ["id"] = DiscussionAnnouncementId, ["kind"] = "announcement", ["participantId"] = "p2",
            ["displayName"] = "New Nina", ["date"] = "2026-10-17",
            ["occurredAtUtc"] = DateTimeOffset.UtcNow.ToString("o"),
            ["lastActivityAtUtc"] = DateTimeOffset.UtcNow.ToString("o"), ["replyCount"] = 0,
            ["replies"] = new JsonArray(),
            ["announcement"] = new JsonObject
            {
                ["eventId"] = DiscussionAnnouncementId, ["title"] = "Earn your words.",
                ["content"] = "Longevitymaxxing notes and replies now start at 146 characters. Each current Challenge point adds one (up to 300), plus one for every athlete you outrank in the Ultimate League.\n\n[Join the Challenge](https://longevityworldcup.com/longevitymaxxing)"
            }
        });
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        if (signedIn) await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/longevitymaxxing#discussion/system/{DiscussionAnnouncementId}");
        var announcement = page.Locator($"#lmxNotes [data-discussion-system-post-id='{DiscussionAnnouncementId}']");
        await Assertions.Expect(announcement).ToBeVisibleAsync();
        await Assertions.Expect(announcement).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-announcement"));
        await Assertions.Expect(announcement.GetByRole(AriaRole.Heading, new() { Name = "Earn your words." })).ToBeVisibleAsync();
        await Assertions.Expect(announcement.Locator(".lmx-discussion-post-author strong")).ToHaveTextAsync("Earn your words.");
        await Assertions.Expect(announcement.GetByText("Announcement", new() { Exact = true })).ToHaveCountAsync(0);
        await Assertions.Expect(announcement.GetByRole(AriaRole.Link, new() { Name = "Join the Challenge", Exact = true }))
            .ToHaveCountAsync(0);
        await Assertions.Expect(announcement).Not.ToContainTextAsync("Day 0");
        Assert.True(await announcement.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var screenshots = Path.Combine(FindRepositoryRoot(), ".artifacts", "character-allowance");
        Directory.CreateDirectory(screenshots);
        await announcement.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"announcement-{width}-{signedIn}.png") });

        if (!signedIn)
        {
            await Assertions.Expect(announcement.Locator("[data-discussion-reply]")).ToHaveCountAsync(0);
            return;
        }
        var welcome = page.Locator("#lmxNotes [data-discussion-system-post-id='11111111111111111111111111111111']");
        await announcement.Locator("[data-discussion-reply]").ClickAsync();
        await announcement.Locator("textarea").FillAsync("Earned words!");
        await announcement.Locator("textarea").PressAsync("Escape");
        await welcome.Locator("[data-discussion-reply]").ClickAsync();
        await welcome.Locator("textarea").FillAsync("A welcome draft.");
        await welcome.Locator("textarea").PressAsync("Escape");
        await announcement.Locator("[data-discussion-reply]").ClickAsync();
        await Assertions.Expect(announcement.Locator("textarea")).ToHaveValueAsync("Earned words!");
        string? replyPayload = null;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            replyPayload = route.Request.PostData;
            var post = posts.Last()!;
            post["replyCount"] = 1;
            post["replies"] = JsonSerializer.SerializeToNode(new[] { Reply("announcement-reply", "p1", "Browser Tester", "Earned words!", DateTimeOffset.UtcNow.ToString("o")) });
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await announcement.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(announcement.Locator(".lmx-discussion-reply-item")).ToContainTextAsync("Earned words!");
        await Assertions.Expect(welcome.Locator(".lmx-discussion-reply-item")).ToContainTextAsync("Welcome aboard!");
        using var payload = JsonDocument.Parse(replyPayload!);
        Assert.Equal(DiscussionAnnouncementId, payload.RootElement.GetProperty("systemPostId").GetString());
        Assert.Equal(0, payload.RootElement.GetProperty("challengeDay").GetInt32());

        // Both system posts have the same participant and day zero. Their loaded history must stay separate.
        foreach (var post in posts)
        {
            var prefix = (string?)post!["kind"] == "announcement" ? "announcement" : "welcome";
            post["replyCount"] = 4;
            post["replies"] = JsonSerializer.SerializeToNode(Enumerable.Range(2, 3).Select(index =>
                Reply($"{prefix}-{index}", "p1", "Browser Tester", $"{prefix} feedback {index}", $"2026-07-05T09:0{index}:00Z")));
        }
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", async route =>
        {
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var prefix = request.RootElement.GetProperty("systemPostId").GetString() == DiscussionAnnouncementId ? "announcement" : "welcome";
            await FulfillJsonAsync(route, JsonSerializer.Serialize(new
            {
                replies = new[] { Reply($"{prefix}-1", "p1", "Browser Tester", $"{prefix} feedback 1", "2026-07-05T09:01:00Z") },
                totalCount = 4, latestReplyIds = Enumerable.Range(2, 3).Select(index => $"{prefix}-{index}").ToArray(),
                remainingEarlierReplyCount = 0, hasEarlier = false,
                nextBeforeCreatedAtUtc = (string?)null, nextBeforeReplyId = (string?)null
            }));
        });
        await page.ReloadAsync();
        // The feed restores the reader's previous snapshot until they accept newer activity.
        await page.Locator("#lmxFeedNew").ClickAsync();
        await announcement.Locator("[data-discussion-replies-page]").ClickAsync();
        await Assertions.Expect(announcement.Locator(".lmx-discussion-reply-item")).ToHaveCountAsync(4);
        await welcome.Locator("[data-discussion-replies-page]").ClickAsync();
        await Assertions.Expect(welcome.Locator(".lmx-discussion-reply-item")).ToHaveCountAsync(4);
        await Assertions.Expect(announcement).Not.ToContainTextAsync("welcome feedback");
        await Assertions.Expect(welcome).Not.ToContainTextAsync("announcement feedback");
    }
}
