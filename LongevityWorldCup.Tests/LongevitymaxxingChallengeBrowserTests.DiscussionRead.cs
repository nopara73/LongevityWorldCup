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
    public async Task DiscussionRead_OpeningConversationClearsUnreadBackgroundWithoutLeavingAFrame(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 1000 }, ColorScheme = theme, Locale = "en-US"
        });
        await RouteUnreadBaselineAsync(context);
        var state = UnreadConversationBrowserState();
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls++;
            await FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await CaptureAdviceAsync(page, $"unread-conversation-before-{width}-{theme}.png", post);
        Assert.Equal(0, calls);
        await post.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(post.Locator(".lmx-unread-message")).ToHaveCountAsync(0);
        await Assertions.Expect(post.Locator(".lmx-discussion-count")).ToHaveTextAsync("4 replies");
        await CaptureAdviceAsync(page, $"unread-conversation-after-{width}-{theme}.png", post);
        if (Environment.GetEnvironmentVariable("LWC_UNREAD_BASELINE") == "1") return;
        var target = post.Locator("[data-discussion-reply-id='old-answer']");
        await Assertions.Expect(target).ToBeFocusedAsync();
        await Assertions.Expect(target).ToHaveCSSAsync("outline-style", "none");
        await Assertions.Expect(target).ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");
        await Assertions.Expect(post.Locator("[data-unread-marker]")).ToHaveCountAsync(0);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task DiscussionRead_ClickingOneMessageKeepsOtherRepliesUnreadAndWaitsForReceipt()
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = 390, Height = 1000 }, ColorScheme = ColorScheme.Light, Locale = "en-US"
        });
        await ObserveDiscussionReadSettlementAsync(context);
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'clipboard', { configurable: true, value: { writeText: async () => {} } });");
        var state = UnreadConversationBrowserState();
        var calls = new List<JsonObject>();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            if (calls.Count == 1) { await route.FulfillAsync(new() { Status = 503 }); return; }
            started.TrySetResult(true);
            await release.Task;
            await FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString());
        });
        try {
            var page = await OpenDiscussionWorkspaceAsync(context, state);
            var post = page.Locator("#lmxNotes > .lmx-advice-post");
            var first = post.Locator("[data-discussion-reply-id='old-answer']");
            var second = post.Locator("[data-discussion-reply-id='new-followup']");
            await Assertions.Expect(post.Locator(".lmx-unread-message")).ToHaveCountAsync(2);
            await Assertions.Expect(first).Not.ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");
            await Assertions.Expect(first).ToHaveCSSAsync("outline-style", "none");
            await first.Locator("[data-discussion-copy-link]").ClickAsync();
            await Assertions.Expect(first.Locator(".lmx-discussion-copy-feedback")).ToHaveTextAsync("Link copied.");
            await page.Clock.FastForwardAsync(2_001);
            await Assertions.Expect(first.Locator("[data-discussion-copy-link] i")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("fa-link"));
            Assert.Empty(calls);
            var body = first.Locator("[data-discussion-reply-body]");
            await body.EvaluateAsync("e => { const range = document.createRange(); range.selectNodeContents(e); const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); }");
            await body.DispatchEventAsync("click");
            Assert.Empty(calls);
            await page.EvaluateAsync("getSelection().removeAllRanges()");
            await body.ClickAsync();
            await Assertions.Expect(first).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            await page.WaitForFunctionAsync("() => window.pendingDiscussionReadsForTest() === 0");
            Assert.Single(calls);
            await body.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assertions.Expect(first).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            release.TrySetResult(true);
            await Assertions.Expect(post.Locator(".lmx-discussion-count")).ToHaveTextAsync("1 new reply");
            await Assertions.Expect(first).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            await Assertions.Expect(first).ToHaveCSSAsync("background-color", "rgba(0, 0, 0, 0)");
            await Assertions.Expect(second).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            Assert.All(calls, payload => Assert.Equal(new[] { "00000000000000000000000000000081" },
                payload["notificationIds"]!.AsArray().Select(id => id!.GetValue<string>())));
            await CaptureAdviceAsync(page, "unread-one-left-390-Light.png", post);
            await second.Locator("[data-discussion-reply-body]").ClickAsync();
            await Assertions.Expect(post.Locator(".lmx-unread-message")).ToHaveCountAsync(0);
            await Assertions.Expect(post.Locator(".lmx-discussion-count")).ToHaveTextAsync("4 replies");
            await CaptureAdviceAsync(page, "unread-all-clicked-390-Light.png", post);
        } finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task DiscussionRead_EnterOnAnUnreadMessageAcknowledgesOnlyThatMessage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = UnreadConversationBrowserState();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", route =>
            FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString()));
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var first = page.Locator("[data-discussion-reply-id='old-answer']");
        await Assertions.Expect(first).ToHaveAttributeAsync("tabindex", "0");
        await first.FocusAsync();
        await Assertions.Expect(page.Locator(".lmx-discussion-count")).ToHaveTextAsync("2 new replies");
        await first.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".lmx-discussion-count")).ToHaveTextAsync("1 new reply");
        await Assertions.Expect(first).ToHaveAttributeAsync("tabindex", "-1");
        await Assertions.Expect(first).ToBeFocusedAsync();
        await Assertions.Expect(first).Not.ToHaveCSSAsync("outline-style", "none");
    }

    [Fact]
    public async Task DiscussionRead_ClickingAnotherMessageWhileAReceiptIsPendingStillMarksItRead()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = UnreadConversationBrowserState();
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            if (++calls == 1) { started.TrySetResult(true); await release.Task; }
            await FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString());
        });
        try {
            var page = await OpenDiscussionWorkspaceAsync(context, state);
            var first = page.Locator("[data-discussion-reply-id='old-answer']");
            var second = page.Locator("[data-discussion-reply-id='new-followup']");
            await first.Locator("[data-discussion-reply-body]").ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await second.Locator("[data-discussion-reply-body]").ClickAsync();
            await Assertions.Expect(second).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            await Assertions.Expect(first).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-unread-message"));
            Assert.Equal(2, calls);
            release.TrySetResult(true);
            await Assertions.Expect(page.Locator(".lmx-unread-message")).ToHaveCountAsync(0);
        } finally { release.TrySetResult(true); }
    }

    [Fact]
    public async Task DiscussionRead_OpeningManyRevealedMessagesUsesBoundedReceiptsFromTheOriginalObservation()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        var ids = Enumerable.Range(0, 103).Select(index => $"new-message-{index:D3}").ToArray();
        foreach (var note in new[] { state["notes"]![0]!, state["public"]!["notes"]![0]! }) {
            foreach (var id in ids) note["advice"]!["replies"]!.AsArray().Add(AdviceBrowserReply(id, "p2", "Ari Able",
                "A new reply in this conversation.", "2026-07-01T08:12:00Z", FirstAdviceId));
            note["replyCount"] = 106;
        }
        state["readContext"] = UnreadAdviceContext(10, ids);
        var calls = new List<JsonObject>();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            await FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await Assertions.Expect(page.Locator(".lmx-unread-message")).ToHaveCountAsync(103);
        await page.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-unread-message")).ToHaveCountAsync(0);
        Assert.Equal(new[] { 100, 3 }, calls.Select(payload => payload["notificationIds"]!.AsArray().Count));
        Assert.All(calls, payload => Assert.Equal(10, payload["observedRevision"]!.GetValue<int>()));
    }

    [Fact]
    public async Task DiscussionRead_PointerOpeningArchiveClearsTheRevealedMessagesWithoutAFrame()
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = 390, Height = 1000 }, ColorScheme = ColorScheme.Light, Locale = "en-US"
        });
        await RouteUnreadBaselineAsync(context);
        var state = AdviceBrowserState(2);
        state["readContext"] = UnreadAdviceContext(10, "old-answer", "higher-advice");
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", route =>
            FulfillJsonAsync(route, AcknowledgeBrowserUnread(state, route.Request.PostData!).ToJsonString()));
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await CaptureAdviceAsync(page, "unread-archive-before-390-Light.png", post);
        await post.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(post.Locator(".lmx-discussion-count")).ToHaveTextAsync("4 replies");
        await Assertions.Expect(post.Locator(".lmx-advice-archive").First).ToHaveAttributeAsync("open", "");
        await CaptureAdviceAsync(page, "unread-archive-after-390-Light.png", post);
        if (Environment.GetEnvironmentVariable("LWC_UNREAD_BASELINE") == "1") return;
        var target = post.Locator("[data-discussion-reply-id='old-answer']");
        await Assertions.Expect(target).ToBeFocusedAsync();
        await Assertions.Expect(target).ToHaveCSSAsync("outline-style", "none");
        await Assertions.Expect(post.Locator(".lmx-unread-message")).ToHaveCountAsync(0);
    }

    private static JsonObject UnreadConversationBrowserState()
    {
        var state = AdviceBrowserState(1);
        foreach (var note in new[] { state["notes"]![0]!, state["public"]!["notes"]![0]! }) {
            note["advice"]!["replies"]!.AsArray().Add(AdviceBrowserReply("new-followup", "p2", "Ari Able",
                "Try keeping the walk at the same time tomorrow.", "2026-07-01T08:12:00Z", FirstAdviceId));
            note["replyCount"] = 4;
            note["lastActivityAtUtc"] = "2026-07-01T08:12:00Z";
        }
        state["readContext"] = UnreadAdviceContext(10, "old-answer", "new-followup");
        return state;
    }

    private static JsonObject AcknowledgeBrowserUnread(JsonObject state, string requestBody)
    {
        var payload = JsonNode.Parse(requestBody)!;
        var ids = payload["notificationIds"]!.AsArray().Select(id => id!.GetValue<string>()).ToHashSet();
        var context = state["readContext"]!.DeepClone();
        context["activityRevision"] = context["activityRevision"]!.GetValue<int>() + 1;
        var thread = context["threads"]![0]!;
        var remaining = thread["notifications"]!.AsArray().Where(item => !ids.Contains(item!["id"]!.GetValue<string>())).ToList();
        thread["notifications"] = new JsonArray(remaining.Select(item => item!.DeepClone()).ToArray());
        thread["unreadCount"] = remaining.Count;
        thread["firstUnreadReplyId"] = remaining.FirstOrDefault()?["sourceReplyId"]!.DeepClone();
        state["readContext"] = context.DeepClone();
        return new JsonObject { ["acknowledgedIds"] = payload["notificationIds"]!.DeepClone(), ["readContext"] = context };
    }

    private static async Task RouteUnreadBaselineAsync(IBrowserContext context)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_UNREAD_BASELINE_ASSETS");
        if (string.IsNullOrEmpty(directory)) return;
        await context.RouteAsync("**/js/longevitymaxxing.js*", route => route.FulfillAsync(new() {
            Path = Path.Combine(directory, "longevitymaxxing.js"), ContentType = "text/javascript"
        }));
        await context.RouteAsync("**/css/longevitymaxxing.css*", route => route.FulfillAsync(new() {
            Path = Path.Combine(directory, "longevitymaxxing.css"), ContentType = "text/css"
        }));
    }

    private static async Task ObserveDiscussionReadSettlementAsync(IBrowserContext context)
    {
        // Observe completion in this intercepted test response. An unchanged
        // unread label alone cannot tell us whether a failed request has settled.
        await context.RouteAsync("**/js/longevitymaxxing.js*", async route => {
            var response = await route.FetchAsync();
            var script = await response.TextAsync();
            var closureEnd = script.LastIndexOf("})();", StringComparison.Ordinal);
            Assert.True(closureEnd > 0);
            await route.FulfillAsync(new() { Response = response, Body = script.Insert(closureEnd,
                "window.pendingDiscussionReadsForTest = () => discussionReadPending.size;\n") });
        });
    }

    [Theory]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task DiscussionRead_ExplicitOpeningPreservesDraftAndDoesNotReadNewerActivity(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 1000 }, ColorScheme = theme });
        await ObserveDiscussionReadSettlementAsync(context);
        var state = AdviceBrowserState(1);
        state["readContext"] = UnreadAdviceContext(10, FirstAdviceId, "old-answer");
        var calls = new List<JsonObject>();
        var firstStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failedResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", async route => {
            calls.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            if (calls.Count == 1) {
                firstStarted.TrySetResult(true);
                await failedResponse.Task;
                await route.FulfillAsync(new() { Status = 503 });
                return;
            }
            retryStarted.TrySetResult(true);
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
            await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Assertions.Expect(link).ToHaveTextAsync("2 new replies");
            await Assertions.Expect(post.Locator("[data-discussion-reply-id='" + FirstAdviceId + "']")).ToBeFocusedAsync();
            Assert.Single(calls);
            await link.FocusAsync();
            await link.PressAsync("Enter");
            await Assertions.Expect(post.Locator("[data-discussion-reply-id='" + FirstAdviceId + "']")).ToBeFocusedAsync();
            Assert.Single(calls);
            failedResponse.TrySetResult(true);
            await page.WaitForFunctionAsync("() => window.pendingDiscussionReadsForTest() === 0");
            await link.FocusAsync();
            await link.PressAsync("Enter");
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        } finally { failedResponse.TrySetResult(true); release.TrySetResult(true); }
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

    [Fact]
    public async Task DiscussionRead_ReloadUsesFreshUnreadOrderAndMessagesAboveRestoredHistory()
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = 1280, Height = 1000 }, ColorScheme = ColorScheme.Light, Locale = "en-US"
        });
        var state = LargeDiscussionFeedState(60);
        static JsonObject Snapshot(JsonArray notes) => new() {
            ["keys"] = new JsonArray(notes.Select(note => (JsonNode)new JsonObject {
                ["participantId"] = note!["participantId"]!.DeepClone(), ["challengeDay"] = note["challengeDay"]!.DeepClone(), ["systemPostId"] = null
            }).ToArray()), ["nextCursor"] = null
        };
        state["discussionSnapshot"] = Snapshot(state["notes"]!.AsArray());
        state["readContext"] = new JsonObject { ["activityRevision"] = 10, ["threads"] = new JsonArray() };
        var reads = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/read", route => { reads++; return route.FulfillAsync(new() { Status = 503 }); });
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        await Assertions.Expect(page.Locator("#lmxNotes > article").First).ToHaveAttributeAsync("data-discussion-post-participant-id", "p2");
        for (var count = 20; count <= 40; count += 10) {
            await page.Locator("#lmxFeedOlder").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(count);
        }
        await page.EvaluateAsync("window.dispatchEvent(new Event('pagehide'))");
        var saved = JsonNode.Parse(await page.EvaluateAsync<string>("sessionStorage.getItem('lmxDiscussionFeedAdviceV2')"))!;
        Assert.Equal(state["participant"]!["id"]!.GetValue<string>(), saved["owner"]!.GetValue<string>());
        var notes = state["notes"]!.AsArray();
        var promoted = notes[50]!.DeepClone(); notes.RemoveAt(50);
        promoted["note"] = "An updated question for @Alex.";
        notes.Insert(0, promoted);
        state["public"]!["notes"] = notes.DeepClone();
        state["discussionSnapshot"] = Snapshot(notes);
        state["readContext"] = ReadPostContext(11, "feed-050", promoted["challengeDay"]!.GetValue<int>(), null);
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(40);
        var first = page.Locator("#lmxNotes > article").First;
        await page.Locator("#lmxNotesTitle").EvaluateAsync("element => window.scrollBy({top: element.getBoundingClientRect().top - 80, behavior: 'instant'})");
        await CaptureAdviceAsync(page, "unread-restored-feed-1280-Light.png");
        await Assertions.Expect(first).ToHaveAttributeAsync("data-discussion-post-participant-id", "feed-050");
        await Assertions.Expect(first).ToContainTextAsync("An updated question for @Alex.");
        await Assertions.Expect(first.Locator(".lmx-discussion-count")).ToHaveTextAsync("New mention");
        Assert.Equal(0, reads);
    }
}
