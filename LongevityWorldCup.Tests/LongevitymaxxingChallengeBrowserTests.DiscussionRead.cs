using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task DiscussionRead_ExplicitOpeningPreservesDraftAndDoesNotReadNewerActivity(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 1000 }, ColorScheme = theme });
        var state = AdviceBrowserState(1);
        state["readContext"] = UnreadAdviceContext(10, FirstAdviceId, "old-answer");
        var calls = new List<JsonObject>();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            if (calls.Count == 1) { await route.FulfillAsync(new() { Status = 503 }); return; }
            await release.Task;
            await FulfillJsonAsync(route, new JsonObject {
                ["acknowledgedIds"] = calls[^1]["notificationIds"]!.DeepClone(),
                ["readContext"] = UnreadAdviceContext(12, "future-message")
            }.ToJsonString());
        });
        try {
            var page = await OpenDiscussionWorkspaceAsync(context, state);
            var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
            var post = page.Locator("#lmxNotes > .lmx-advice-post");
            await CaptureAdviceAsync(page, $"unread-advice-{width}-{theme}.png", post);
            if (Environment.GetEnvironmentVariable("LWC_UNREAD_BASELINE") == "1") return;
            var link = post.Locator(".lmx-discussion-count");
            await Assertions.Expect(link).ToHaveTextAsync("2 new replies");
            await Assertions.Expect(post.Locator("[data-unread-marker]")).ToHaveCountAsync(2);
            await post.Locator(".lmx-advice-story p").EvaluateAsync("e => { const range = document.createRange(); range.selectNodeContents(e); const s = getSelection(); s.removeAllRanges(); s.addRange(range); }");
            await page.Clock.FastForwardAsync(61_000);
            Assert.Empty(calls);
            await link.ClickAsync();
            await Assertions.Expect(link).ToHaveTextAsync("2 new replies");
            await Assertions.Expect(post.Locator("[data-discussion-reply-id='" + FirstAdviceId + "']")).ToBeFocusedAsync();
            Assert.Single(calls);
            await link.FocusAsync();
            await link.PressAsync("Enter");
            await post.Locator($"[data-discussion-reply-id='{FirstAdviceId}'] [data-discussion-reply]").ClickAsync();
            var draft = post.Locator("textarea");
            await draft.FillAsync("Keep my reply while the read receipt returns.");
            await draft.EvaluateAsync("e => { window.unreadDraft = e; e.setSelectionRange(5, 12); }");
            release.TrySetResult(true);
            await Assertions.Expect(link).ToHaveTextAsync("1 new reply");
            Assert.Equal(2, calls.Count);
            Assert.All(calls, payload => {
                Assert.Equal(10, payload["observedRevision"]!.GetValue<int>());
                Assert.Equal(new[] { "00000000000000000000000000000081", "00000000000000000000000000000082" },
                    payload["notificationIds"]!.AsArray().Select(id => id!.GetValue<string>()));
            });
            Assert.True(await draft.EvaluateAsync<bool>("e => e === window.unreadDraft && document.activeElement === e && e.selectionStart === 5 && e.selectionEnd === 12"));
            await Assertions.Expect(draft).ToHaveValueAsync("Keep my reply while the read receipt returns.");
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            Assert.Empty(errors);
        } finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task DiscussionRead_KeyboardOpeningRevealsArchivedTargetBeforeAcknowledgingIt()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(2);
        state["readContext"] = UnreadAdviceContext(10, "old-answer");
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls++;
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            await FulfillJsonAsync(route, new JsonObject { ["acknowledgedIds"] = payload["notificationIds"]!.DeepClone(), ["readContext"] = UnreadAdviceContext(11) }.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        Assert.Equal(0, calls);
        await Assertions.Expect(post.Locator(".lmx-advice-archive").First).Not.ToHaveAttributeAsync("open", "");
        var link = post.Locator(".lmx-discussion-count");
        await link.FocusAsync(); await link.PressAsync("Enter");
        await Assertions.Expect(post.Locator("[data-discussion-reply-id='old-answer']")).ToBeFocusedAsync();
        await Assertions.Expect(link).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-link"));
        await Assertions.Expect(post.Locator(".lmx-advice-archive").First).ToHaveAttributeAsync("open", "");
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionRead_StaleFeedQuietlyDrainsDuplicatePagesAndPreservesDraftNodes(bool newUnread)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(100);
        var all = LargeDiscussionFeedState(130)["notes"]!.AsArray();
        if (newUnread) {
            var promoted = all[110]!.DeepClone(); all.RemoveAt(110); all.Insert(0, promoted);
        }
        static JsonObject Cursor(int index) => new() { ["rankedAtUnixMs"] = 1, ["score"] = 0, ["activityAtUnixMs"] = 1,
            ["challengeDay"] = index, ["participantId"] = "p1", ["systemPostId"] = null, ["rankingRevision"] = 1, ["readSnapshotRevision"] = 1 };
        state["discussionSnapshot"] = new JsonObject {
            ["keys"] = new JsonArray(state["notes"]!.AsArray().Select(note => (JsonNode)new JsonObject {
                ["participantId"] = note!["participantId"]!.DeepClone(), ["challengeDay"] = note["challengeDay"]!.DeepClone(), ["systemPostId"] = null }).ToArray()),
            ["nextCursor"] = Cursor(100)
        };
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/feed", async route => {
            if (++calls == 1) { await route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "{\"code\":\"discussion_feed_changed\",\"message\":\"Feed changed.\"}" }); return; }
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            var start = payload["before"] is JsonObject cursor ? cursor["challengeDay"]!.GetValue<int>() : 0;
            var readContext = newUnread && start == 0 ? new JsonObject { ["activityRevision"] = 2,
                ["threads"] = new JsonArray(all.Take(2).Select((note, index) => (JsonNode)new JsonObject {
                    ["participantId"] = note!["participantId"]!.DeepClone(), ["challengeDay"] = note["challengeDay"]!.DeepClone(), ["systemPostId"] = null,
                    ["unreadCount"] = 1, ["hasOpeningMention"] = true, ["firstUnreadReplyId"] = null, ["latestRelevantAtUnixMs"] = 20 - index,
                    ["notifications"] = new JsonArray(new JsonObject { ["id"] = (83 + index).ToString("D32"), ["sourceReplyId"] = null, ["createdRevision"] = 2 })
                }).ToArray()) } : null;
            await FulfillJsonAsync(route, new JsonObject { ["readContext"] = readContext,
                ["threads"] = new JsonArray(all.Skip(start).Take(10).Select(note => (JsonNode)new JsonObject { ["note"] = note!.DeepClone(), ["systemPost"] = null }).ToArray()),
                ["nextCursor"] = start + 10 < all.Count ? Cursor(start + 10) : null }.ToJsonString());
        });
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var first = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p2']");
        await first.Locator("[data-discussion-quick-reply]").ClickAsync();
        var draft = first.Locator("textarea");
        await draft.FillAsync("Keep this draft through a feed restart.");
        await draft.EvaluateAsync("e => { window.restartDraft = e; e.setSelectionRange(5, 9); }");
        var older = page.Locator("#lmxFeedOlder");
        for (var count = 20; count <= 100; count += 10) {
            await older.ClickAsync(); await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(count);
        }
        await older.ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(110);
        Assert.Equal(newUnread ? 13 : 12, calls);
        Assert.True(await draft.EvaluateAsync<bool>("e => e === window.restartDraft && e.selectionStart === 5 && e.selectionEnd === 9"));
        Assert.Equal(110, (await page.Locator("#lmxNotes > article").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.feedKey)")).Distinct().Count());
        await older.ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(120);
        Assert.Equal(newUnread ? 14 : 13, calls);
        if (newUnread) {
            await Assertions.Expect(page.Locator("#lmxNotes > article[data-discussion-post-participant-id='feed-110']")).ToHaveCountAsync(0);
            await Assertions.Expect(page.Locator("#lmxFeedNew")).ToBeVisibleAsync();
            await page.Locator("#lmxFeedNew").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxNotes > article").First).ToHaveAttributeAsync("data-discussion-post-participant-id", "feed-110");
            Assert.True(await draft.EvaluateAsync<bool>("e => e === window.restartDraft && e.selectionStart === 5 && e.selectionEnd === 9"));
        }
    }

    private static JsonObject UnreadAdviceContext(int revision, params string[] replyIds)
        => new() { ["activityRevision"] = revision, ["threads"] = new JsonArray(new JsonObject {
            ["participantId"] = "p1", ["challengeDay"] = 22, ["systemPostId"] = null, ["unreadCount"] = replyIds.Length,
            ["firstUnreadReplyId"] = replyIds.FirstOrDefault(), ["hasOpeningMention"] = false, ["latestRelevantAtUnixMs"] = replyIds.Length > 0 ? 1782893400000L : 0,
            ["notifications"] = new JsonArray(replyIds.Select((id, index) => (JsonNode)new JsonObject {
                ["id"] = (81 + index).ToString("D32"), ["sourceReplyId"] = id, ["createdRevision"] = revision }).ToArray())
        }) };

    [Fact]
    public async Task DiscussionRead_PartialContextsMergePerThreadAndFinalEarlierPageIsAcknowledged()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        state["readContext"] = ReadPostContext(10, "p2", 22, "r5");
        state["readContext"]!["threads"]!.AsArray().Add(ReadPostContext(10, "p7", 5, null, false)["threads"]![0]!.DeepClone());
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", async route => {
            await release.Task;
            var page = JsonNode.Parse(EarlierPolishReplies())!.AsObject();
            page["readContext"] = ReadPostContext(11, "p7", 5, "r1", notification: 82);
            await FulfillJsonAsync(route, page.ToJsonString());
        });
        var receipts = new List<JsonObject>();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!.AsObject(); receipts.Add(payload);
            var post = (string)payload["postParticipantId"]!;
            await FulfillJsonAsync(route, new JsonObject { ["acknowledgedIds"] = payload["notificationIds"]!.DeepClone(),
                ["readContext"] = ReadPostContext(post == "p2" ? 12 : 13, post, post == "p2" ? 22 : 5, null, false) }.ToJsonString());
        });
        try {
            var page = await OpenDiscussionPolishAsync(context, state);
            var older = DiscussionThread(page, "p7", 5).Locator("[data-discussion-replies-page]");
            await older.ClickAsync();
            await Assertions.Expect(older).ToBeDisabledAsync();
            var first = DiscussionThread(page, "p2", 22);
            await first.Locator(".lmx-discussion-count").ClickAsync();
            await Assertions.Expect(first.Locator(".lmx-discussion-count")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-link"));
            release.TrySetResult(true);
            await Assertions.Expect(older).ToHaveCountAsync(0);
            await Assertions.Expect(DiscussionThread(page, "p7", 5).Locator(".lmx-discussion-count")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-link"));
            await Assertions.Expect(DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r1']")).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => !document.querySelector('.lmx-unread-link')");
            Assert.Equal(2, receipts.Count);
            Assert.Equal("p7", (string?)receipts[1]["postParticipantId"]);
            Assert.Equal(12, (int?)receipts[1]["observedRevision"]);
        } finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task DiscussionRead_OpeningMentionRevealsCheckInRemarksBeforeReceipt()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1, advisor: true);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
            notes[0]!["note"] = "A question for @Ari Able.";
        state["readContext"] = ReadPostContext(10, "p1", 22, null);
        var calls = 0;
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls++;
            await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-context")).ToHaveAttributeAsync("open", "");
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            await FulfillJsonAsync(route, new JsonObject { ["acknowledgedIds"] = payload["notificationIds"]!.DeepClone(), ["readContext"] = ReadPostContext(11, "p1", 22, null, false) }.ToJsonString());
        });
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await post.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        await Assertions.Expect(post.Locator("textarea")).ToBeVisibleAsync();
        Assert.Equal(0, calls);
        await Assertions.Expect(post.Locator(".lmx-discussion-count")).ToHaveTextAsync("New mention");
        await post.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(post.Locator(".lmx-discussion-count")).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-link"));
        Assert.Equal(1, calls);
    }

    private static JsonObject ReadPostContext(int revision, string participant, int day, string? reply, bool unread = true, int notification = 81)
        => new() { ["activityRevision"] = revision, ["threads"] = new JsonArray(new JsonObject {
            ["participantId"] = participant, ["challengeDay"] = day, ["systemPostId"] = null, ["unreadCount"] = unread ? 1 : 0,
            ["firstUnreadReplyId"] = reply, ["hasOpeningMention"] = unread && reply is null, ["latestRelevantAtUnixMs"] = unread ? 1782893400000L : 0,
            ["notifications"] = unread ? new JsonArray(new JsonObject { ["id"] = notification.ToString("D32"), ["sourceReplyId"] = reply, ["createdRevision"] = revision }) : new JsonArray()
        }) };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionRead_UnavailableOpeningRetiresPriorityAndPreservesAnyDraft(bool writing)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(0, advisor: true);
        state["readContext"] = ReadPostContext(10, "p1", 22, null);
        var lookups = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/thread", async route => {
            lookups++; await route.FulfillAsync(new() { Status = 404, ContentType = "application/json", Body = "{\"message\":\"Unavailable.\"}" });
        });
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", route => route.FulfillAsync(new() { Status = 503 }));
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        if (writing) {
            await post.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
            await post.Locator("textarea").FillAsync("Keep this advice even though the post disappeared.");
        }
        var current = LargeDiscussionFeedState(5);
        state["notes"] = current["notes"]!.DeepClone(); state["public"]!["notes"] = current["public"]!["notes"]!.DeepClone();
        state["readContext"] = new JsonObject { ["activityRevision"] = 11, ["threads"] = new JsonArray() };
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-unread-link")).ToHaveCountAsync(0);
        if (writing) {
            await Assertions.Expect(post.Locator("textarea")).ToHaveValueAsync("Keep this advice even though the post disappeared.");
            await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeDisabledAsync();
            await Assertions.Expect(post.Locator(".lmx-status")).ToContainTextAsync("no longer available");
            await Assertions.Expect(post).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-needs-advice"));
        } else await Assertions.Expect(post).ToHaveCountAsync(0);
        Assert.Equal(1, lookups);
    }
}
