using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task DiscussionDrafts_SurviveThreadsPagingAndParticipantViews(ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ColorScheme = theme });
        var page = await OpenDiscussionWorkspaceAsync(context);
        var ari = DiscussionThread(page, "p2", 22);
        var bea = DiscussionThread(page, "p3", 21);
        await ari.Locator("[data-discussion-reply]").ClickAsync();
        const string first = "  Keep this reply to @Ari Able.  ";
        await ari.Locator("textarea").FillAsync(first);
        await ari.Locator("textarea").EvaluateAsync("e => e.setSelectionRange(5, 10)");
        await bea.Locator("[data-discussion-reply]").ClickAsync();
        await bea.Locator("textarea").FillAsync("A different reply.");
        await Assertions.Expect(ari.Locator("[data-discussion-reply]")).ToHaveTextAsync("Resume reply");
        await ari.Locator("[data-discussion-reply]").ClickAsync();
        Assert.Equal(first, await ari.Locator("textarea").InputValueAsync());
        Assert.Equal(new[] { 5, 10 }, await ari.Locator("textarea").EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
        await page.Locator("#lmxNotesOlder").ClickAsync();
        Assert.Equal(0, await ari.CountAsync());
        await page.Locator("#lmxNotesNewer").ClickAsync();
        Assert.Equal(first, await ari.Locator("textarea").InputValueAsync());
        await page.Locator("#lmxProfileTab").ClickAsync();
        await page.Locator("#lmxHomeTab").ClickAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        Assert.Equal(first, await ari.Locator("textarea").InputValueAsync());
        await bea.Locator("[data-discussion-reply]").ClickAsync();
        Assert.Equal("A different reply.", await bea.Locator("textarea").InputValueAsync());
        Assert.Equal(1, await page.Locator("[data-discussion-draft]").CountAsync());
    }

    [Fact]
    public async Task DiscussionEditDraft_IsSeparateFromANewReplyAndDiscardRestoresPublishedText()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context);
        var owned = page.Locator("#lmxNotes [data-discussion-reply-id='r6']");
        var fox = DiscussionThread(page, "p7", 5);
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        await owned.Locator("textarea").FillAsync("An unpublished correction.");
        await fox.Locator("[data-discussion-reply]").ClickAsync();
        await fox.Locator(":scope > .lmx-discussion-reply-slot textarea").FillAsync("A separate new reply.");
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        Assert.Equal("An unpublished correction.", await owned.Locator("textarea").InputValueAsync());
        await owned.Locator("[data-reply-action='discard']").ClickAsync();
        await Assertions.Expect(owned.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("An actual child reply for @Ari Able.");
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        await Assertions.Expect(owned.Locator("[data-reply-edit-submit]")).ToBeDisabledAsync();
        await fox.Locator("[data-discussion-reply]").ClickAsync();
        Assert.Equal("A separate new reply.", await fox.Locator(":scope > .lmx-discussion-reply-slot textarea").InputValueAsync());
    }

    [Fact]
    public async Task DiscussionRetry_KeepsIdentityWhenReopenedAndRotatesOnlyForChangedContent()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context);
        var requests = new List<JsonObject>();
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            requests.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Please try again.\"}" });
        });
        var full = DiscussionThread(page, "p2", 22);
        await full.Locator("[data-discussion-reply]").ClickAsync();
        await full.Locator("textarea").FillAsync("Keep the same reply.");
        await full.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(full.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");
        await full.Locator("[data-reply-action='close']").ClickAsync();
        await full.Locator("[data-discussion-reply]").ClickAsync();
        await Assertions.Expect(full.Locator(".lmx-status.error")).ToContainTextAsync("Couldn’t confirm your reply.");
        Assert.Equal("Keep the same reply.", await full.Locator("textarea").InputValueAsync());
        await full.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(full.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");
        await full.Locator("textarea").FillAsync("A changed reply.");
        await Assertions.Expect(full.Locator(".lmx-status.error")).ToHaveCountAsync(0);
        await full.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(full.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");
        Assert.Equal(3, requests.Count);
        Assert.Equal(requests[0]["replyId"]!.GetValue<string>(), requests[1]["replyId"]!.GetValue<string>());
        Assert.NotEqual(requests[1]["replyId"]!.GetValue<string>(), requests[2]["replyId"]!.GetValue<string>());
        Assert.Equal("p2", requests[2]["postParticipantId"]!.GetValue<string>());
        Assert.Equal(22, requests[2]["challengeDay"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionPendingReply_LocksItsDraftAndPreservesAnotherComposerOnCompletion(bool editingOtherReply)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            requests++;
            requested.TrySetResult();
            await release.Task;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        try {
            var ari = DiscussionThread(page, "p2", 22);
            await ari.Locator("[data-discussion-reply]").ClickAsync();
            await ari.Locator("textarea").FillAsync("The reply being posted.");
            await ari.Locator("[data-reply-submit]").ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ari.Locator("[data-reply-action='close']").ClickAsync();
            await ari.Locator("[data-discussion-reply]").ClickAsync();
            Assert.True(await ari.Locator("textarea").EvaluateAsync<bool>("e => e.readOnly"));
            await ari.Locator("textarea").PressAsync("Control+Enter");
            await Assertions.Expect(ari.Locator("[data-reply-action='discard']")).ToBeDisabledAsync();
            var other = editingOtherReply ? page.Locator("#lmxNotes [data-discussion-reply-id='r6']")
                : DiscussionThread(page, "p3", 21);
            await other.Locator(editingOtherReply ? "[data-discussion-reply-edit]" : "[data-discussion-reply]").ClickAsync();
            await other.Locator("textarea").FillAsync("Continue writing while the first reply finishes.");
            await other.Locator("textarea").EvaluateAsync("e => e.setSelectionRange(9, 16)");
            await Assertions.Expect(other.Locator("[data-reply-action='submit']")).ToBeDisabledAsync();
            var position = await other.Locator("textarea").EvaluateAsync<double>("e => e.getBoundingClientRect().top");
            release.TrySetResult();
            await Assertions.Expect(other.Locator("[data-reply-action='submit']")).ToBeEnabledAsync();
            Assert.Equal("Continue writing while the first reply finishes.", await other.Locator("textarea").InputValueAsync());
            await Assertions.Expect(other.Locator("textarea")).ToBeFocusedAsync();
            Assert.Equal(new[] { 9, 16 }, await other.Locator("textarea").EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
            Assert.InRange(Math.Abs(position - await other.Locator("textarea").EvaluateAsync<double>("e => e.getBoundingClientRect().top")), 0, 2);
            Assert.Equal(1, requests);
            await Assertions.Expect(ari.Locator("[data-discussion-reply]")).ToHaveTextAsync("Reply");
        } finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task DiscussionEditFailure_CanResumeAndSaveWithoutLosingTheCheckIn()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context);
        var note = page.Locator(".lmx-checkin-entry textarea");
        await note.FillAsync("An unfinished daily remark.");
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", async route => {
            requests++;
            if (requests == 1) await route.FulfillAsync(new() { Status = 503 });
            else await FulfillJsonAsync(route, JsonSerializer.Serialize(Reply("r6", "p1", "Browser Tester", "My corrected reply.", "2026-06-30T10:00:00Z", "2026-06-30T11:00:00Z")));
        });
        var owned = page.Locator("#lmxNotes [data-discussion-reply-id='r6']");
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        await owned.Locator("textarea").FillAsync("My corrected reply.");
        await owned.Locator("[data-reply-edit-submit]").ClickAsync();
        await Assertions.Expect(owned.Locator("[data-reply-edit-submit]")).ToHaveTextAsync("Retry");
        await owned.Locator("[data-reply-action='close']").ClickAsync();
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        Assert.Equal("My corrected reply.", await owned.Locator("textarea").InputValueAsync());
        await owned.Locator("textarea").PressAsync("Control+Enter");
        await Assertions.Expect(owned.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("My corrected reply.");
        await Assertions.Expect(owned.Locator("[data-discussion-reply-edit]")).ToBeFocusedAsync();
        await Assertions.Expect(owned.Locator(".lmx-discussion-feedback")).ToHaveTextAsync("Reply saved.");
        Assert.Equal("An unfinished daily remark.", await note.InputValueAsync());
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionUnpublishedWork_WarnsOnLeavingUntilExplicitlyDiscarded(bool editing)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context);
        var target = editing ? page.Locator("#lmxNotes [data-discussion-reply-id='r6']") : DiscussionThread(page, "p2", 22);
        var toggle = target.Locator(editing ? "[data-discussion-reply-edit]" : "[data-discussion-reply]");
        await toggle.ClickAsync();
        await target.Locator("textarea").FillAsync("Don't silently lose this.");
        await target.Locator("[data-reply-action='close']").ClickAsync();
        var dialogs = 0;
        page.Dialog += async (_, dialog) => { dialogs++; Assert.Equal("beforeunload", dialog.Type); await dialog.DismissAsync(); };
        await page.Locator("#lmxProfileTab").EvaluateAsync("e => { const a = document.createElement('a'); a.id = 'leave-discussion'; a.href = '/about'; a.textContent = 'Leave'; e.parentElement.append(a); }");
        await page.Locator("#leave-discussion").ClickAsync();
        Assert.Equal(1, dialogs);
        Assert.Contains("/longevitymaxxing", page.Url);
        await toggle.ClickAsync();
        await target.Locator("[data-reply-action='discard']").ClickAsync();
        await page.Locator("#leave-discussion").ClickAsync();
        await page.WaitForURLAsync("**/about");
        Assert.Equal(1, dialogs);
    }

    [Theory]
    [InlineData(320, ColorScheme.Light, false)]
    [InlineData(390, ColorScheme.Dark, true)]
    [InlineData(1280, ColorScheme.Light, false)]
    public async Task DiscussionComposer_KeepsCapacityMentionsAndActionsUsable(int width, ColorScheme theme, bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme });
        var page = await OpenDiscussionWorkspaceAsync(context, dialog: dialog);
        var root = "#lmxNotes";
        var owned = page.Locator($"{root} [data-discussion-reply-id='r6']");
        await owned.Locator("[data-discussion-reply-edit]").ClickAsync();
        await owned.Locator("textarea").FillAsync(new string('x', 240));
        await Assertions.Expect(owned.Locator("[data-reply-count]")).ToHaveTextAsync("240/240");
        await owned.Locator("textarea").PressAsync("y");
        Assert.Equal(241, (await owned.Locator("textarea").InputValueAsync()).Length);
        await Assertions.Expect(owned.Locator("[data-reply-count]")).ToHaveTextAsync("1 over");
        await Assertions.Expect(owned.Locator("[data-reply-edit-submit]")).ToBeDisabledAsync();
        var controls = await owned.Locator("[data-reply-action]").EvaluateAllAsync<double[][]>("buttons => buttons.map(b => { const r=b.getBoundingClientRect();return [r.width,r.height,r.left,r.right,r.top] })");
        Assert.All(controls, box => { Assert.True(box[0] >= 44); Assert.True(box[1] >= 44); Assert.InRange(box[2], 0, width); Assert.InRange(box[3], 0, width); });
        Assert.Equal(controls[1][4], controls[2][4]);
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        await owned.Locator("textarea").FillAsync("Thanks @Ar");
        await owned.Locator(".lmx-mention-options:not([hidden])").WaitForAsync();
        await owned.Locator("textarea").PressAsync("Control+Enter");
        Assert.Equal("Thanks @Ari Able ", await owned.Locator("textarea").InputValueAsync());
        await Assertions.Expect(owned.Locator("[data-reply-edit-submit]")).ToHaveTextAsync("Save reply");
        await Assertions.Expect(owned.Locator("[data-reply-count]")).ToHaveTextAsync("17/240");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionPostedReply_ClearsItsDraftAndRestoresFocusWithoutALeaveWarning(bool fromOlderPage)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var participantId = fromOlderPage ? "p6" : "p2";
        var day = fromOlderPage ? 19 : 22;
        string? postedReplyId = null;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            postedReplyId = (string)payload["replyId"]!;
            foreach (var collection in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
                var thread = collection.Single(n => (string?)n!["participantId"] == participantId && (int?)n["challengeDay"] == day)!;
                thread["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
                    (string)payload["replyId"]!, "p1", "Browser Tester", (string)payload["body"]!, "2026-07-01T10:00:00Z")));
                thread["replyCount"] = thread["replies"]!.AsArray().Count;
                thread["lastActivityAtUtc"] = "2026-07-01T10:00:00Z";
            }
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        if (fromOlderPage) await page.Locator("#lmxNotesOlder").ClickAsync();
        var thread = DiscussionThread(page, participantId, day);
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("  My published reply.  ");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body]").Last).ToHaveTextAsync("My published reply.");
        Assert.NotNull(postedReplyId);
        await Assertions.Expect(thread.Locator($"[data-discussion-reply-id='{postedReplyId}']")).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator(".lmx-discussion-feedback")).ToHaveTextAsync("Reply posted.");
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        Assert.Equal("", await thread.Locator("textarea").InputValueAsync());
        var dialogs = 0;
        page.Dialog += async (_, dialog) => { dialogs++; await dialog.DismissAsync(); };
        await page.EvaluateAsync("location.href = '/about'");
        await page.WaitForURLAsync("**/about", new() { Timeout = 5000 });
        Assert.Equal(0, dialogs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionBackgroundReordering_KeepsTheActiveThreadOnScreen(bool editing)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        var targetId = "p5";
        var day = 19;
        if (editing) {
            foreach (var collection in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
                var thread = collection.Single(n => (string?)n!["participantId"] == targetId)!;
                thread["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply("active-edit", "p1", "Browser Tester", "Published reply.", (string)thread["updatedAtUtc"]!)));
                thread["replyCount"] = thread["replies"]!.AsArray().Count;
            }
        }
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-07-02T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
        var response = state.DeepClone().AsObject();
        foreach (var collection in new[] { response["notes"]!.AsArray(), response["public"]!["notes"]!.AsArray() }) {
            var thread = collection.Single(n => (string?)n!["participantId"] == "p6")!;
            thread["lastActivityAtUtc"] = "2026-07-02T10:00:00Z";
            thread["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply("just-posted", "p1", "Browser Tester", "New activity.", "2026-07-02T10:00:00Z")));
            thread["replyCount"] = thread["replies"]!.AsArray().Count;
        }
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            requested.TrySetResult();
            await release.Task;
            await FulfillJsonAsync(route, response.ToJsonString());
        });
        try {
            await page.Locator("#lmxNotesOlder").ClickAsync();
            var eli = DiscussionThread(page, "p6", 19);
            await eli.Locator("[data-discussion-reply]").ClickAsync();
            await eli.Locator("textarea").FillAsync("New activity.");
            await eli.Locator("[data-reply-submit]").ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await page.Locator("#lmxNotesNewer").ClickAsync();
            var target = DiscussionThread(page, targetId, day, "#lmxNotes");
            await target.Locator(editing ? "[data-discussion-reply-edit]" : "[data-discussion-reply]").ClickAsync();
            await target.Locator("textarea").FillAsync("Still writing when the order changes.");
            await target.Locator("textarea").EvaluateAsync("e => e.setSelectionRange(3, 7)");
            var top = await target.Locator("textarea").EvaluateAsync<double>("e => e.getBoundingClientRect().top");
            release.TrySetResult();
            await Assertions.Expect(target.Locator("[data-reply-action='submit']")).ToBeEnabledAsync();
            await Assertions.Expect(target.Locator("textarea")).ToBeFocusedAsync();
            Assert.Equal("Still writing when the order changes.", await target.Locator("textarea").InputValueAsync());
            Assert.Equal(new[] { 3, 7 }, await target.Locator("textarea").EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
            Assert.InRange(Math.Abs(top - await target.Locator("textarea").EvaluateAsync<double>("e => e.getBoundingClientRect().top")), 0, 2);
            await Assertions.Expect(page.Locator("#lmxNotesNewer")).ToBeEnabledAsync();
            await page.Locator("#lmxNotesNewer").ClickAsync();
            await Assertions.Expect(target).ToHaveCountAsync(0);
            await page.Locator("#lmxProfileTab").ClickAsync();
            await Assertions.Expect(target).ToHaveCountAsync(0);
        } finally { release.TrySetResult(); }
    }

    [Fact]
    public async Task DiscussionEditDraft_RemainsReachableWhenItsReplyLeavesTheVisibleWindow()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var response = JsonSerializer.Serialize(BuildParticipantState(includeMentionParticipants: true,
            includeDiscussionNotesWithMentionParticipants: true, discussionReplySnapshot: DiscussionReplySnapshot.DisjointAfterFourMoreReplies));
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            requested.TrySetResult();
            await release.Task;
            await FulfillJsonAsync(route, response);
        });
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", route => FulfillJsonAsync(route,
            JsonSerializer.Serialize(Reply("r6", "p1", "Browser Tester", "Keep this correction.", "2026-06-30T10:00:00Z", "2026-07-01T10:00:00Z"))));
        try {
            var ari = DiscussionThread(page, "p2", 22);
            await ari.Locator("[data-discussion-reply]").ClickAsync();
            await ari.Locator("textarea").FillAsync("A new reply while others are joining in.");
            await ari.Locator("[data-reply-submit]").ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var fox = DiscussionThread(page, "p7", 5, "#lmxNotes");
            await fox.Locator("[data-discussion-reply-edit]").ClickAsync();
            await fox.Locator("textarea").FillAsync("Keep this correction.");
            release.TrySetResult();
            await Assertions.Expect(fox.Locator("[data-reply-edit-submit]")).ToBeEnabledAsync();
            await Assertions.Expect(fox.Locator("textarea")).ToBeFocusedAsync();
            Assert.Equal("Keep this correction.", await fox.Locator("textarea").InputValueAsync());
            await Assertions.Expect(page.Locator("[data-discussion-reply-id='r6']")).ToHaveCountAsync(0);
            await fox.Locator("[data-reply-action='close']").ClickAsync();
            await fox.Locator("[data-discussion-reply-edit]").ClickAsync();
            Assert.Equal("Keep this correction.", await fox.Locator("textarea").InputValueAsync());
            await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", route => FulfillJsonAsync(route, JsonSerializer.Serialize(new {
                replies = new[] {
                    Reply("r6", "p1", "Browser Tester", "An actual child reply for @Ari Able.", "2026-06-30T10:00:00Z"),
                    Reply("r7", "p7", "Fox", "Reply seven.", "2026-06-30T11:00:00Z"),
                    Reply("r8", "p3", "Bea", "Reply eight.", "2026-06-30T12:00:00Z"),
                    Reply("r9", "p4", "Cam", "Reply nine.", "2026-06-30T13:00:00Z")
                },
                totalCount = 12, latestReplyIds = new[] { "r10", "r11", "r12" }, remainingEarlierReplyCount = 5,
                hasEarlier = true, nextBeforeCreatedAtUtc = "2026-06-30T10:00:00Z", nextBeforeReplyId = "r6"
            })));
            await fox.Locator("[data-discussion-replies-page]").ClickAsync();
            await Assertions.Expect(fox.Locator("[data-discussion-reply-id='r6']")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator("[data-discussion-draft]")).ToHaveCountAsync(1);
            await fox.Locator("[data-reply-action='close']").ClickAsync();
            await Assertions.Expect(fox.Locator("[data-discussion-reply-edit]")).ToBeFocusedAsync();
            await fox.Locator("[data-discussion-reply-edit]").ClickAsync();
            Assert.Equal("Keep this correction.", await fox.Locator("textarea").InputValueAsync());
            await fox.Locator("[data-reply-edit-submit]").ClickAsync();
            await Assertions.Expect(fox.Locator(".lmx-discussion-feedback")).ToHaveTextAsync("Reply saved.");
            await Assertions.Expect(fox.Locator("[data-discussion-reply-edit]")).ToBeFocusedAsync();
            await Assertions.Expect(fox.Locator("[data-discussion-edit-id]")).ToHaveCountAsync(0);
        } finally { release.TrySetResult(); }
    }

    private static ILocator DiscussionThread(IPage page, string participantId, int day, string root = "#lmxNotes") =>
        page.Locator($"{root} article[data-discussion-post-participant-id='{participantId}'][data-discussion-post-challenge-day='{day}']");

    private static JsonObject DiscussionWorkspaceState() => JsonSerializer.SerializeToNode(BuildParticipantState(
        includeMentionParticipants: true, includeDiscussionNotesWithMentionParticipants: true,
        discussionReplySnapshot: DiscussionReplySnapshot.ContinuousAfterReply))!.AsObject();

    private static async Task<IPage> OpenDiscussionWorkspaceAsync(IBrowserContext context, JsonObject? state = null, bool dialog = false)
    {
        state ??= DiscussionWorkspaceState();
        if (!dialog) foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync(dialog ? "/longevitymaxxing?token=browser-token&checkin=1" : "/longevitymaxxing");
        if (dialog) {
            await page.Locator(".lmx-checkin-dialog-panel").WaitForAsync();
            await Assertions.Expect(page.Locator("#lmxCheckinList [data-discussion-post-participant-id]")).ToHaveCountAsync(0);
            await page.Locator("#lmxCheckinDialogClose").ClickAsync();
        }
        await page.Locator("#lmxParticipantTabs").WaitForAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator("#lmxNotes article").First.WaitForAsync();
        return page;
    }
}
