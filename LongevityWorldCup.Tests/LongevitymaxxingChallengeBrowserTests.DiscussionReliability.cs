using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, ColorScheme.Light, false)]
    [InlineData(390, ColorScheme.Dark, true)]
    public async Task DiscussionReliability_ReloadRestoresContextSelectionAndRetryIdentity(int width, ColorScheme theme, bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme });
        var state = ConversationWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state, dialog);
        var payloads = new List<JsonObject>();
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!.AsObject();
            payloads.Add(payload);
            if (payloads.Count == 1) await route.FulfillAsync(new() { Status = 503 });
            else { AppendReliabilityReply(state, payload); await FulfillJsonAsync(route, state.ToJsonString()); }
        });
        var thread = DiscussionThread(page, "p7", 5, "#lmxNotes");
        await thread.Locator("[data-discussion-reply-id='r3'] [data-discussion-reply-to]").ClickAsync();
        var textarea = thread.Locator("textarea");
        await Assertions.Expect(thread.Locator(".lmx-discussion-composer-heading label")).ToHaveTextAsync("Reply to Dee");
        await Assertions.Expect(thread.Locator("[data-discussion-compose-context]")).ToContainTextAsync("The small version worked for me.");
        await textarea.FillAsync("@Dee I’ll try the smaller version tomorrow.");
        await CaptureReliabilityAsync(thread.Locator("[data-discussion-reply-composer]"), $"composer-{width}-{theme}.png");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 15); e.dispatchEvent(new Event('select')); }");
        var saved = await page.EvaluateAsync<string>("sessionStorage.getItem('lmx-discussion-drafts:p1')");
        Assert.DoesNotContain("browser-token", saved);
        Assert.Matches("^[a-f0-9]{32}$", payloads[0]["replyId"]!.GetValue<string>());
        page.Dialog += (_, modal) => _ = modal.AcceptAsync();
        await page.ReloadAsync();
        thread = DiscussionThread(page, "p7", 5);
        textarea = thread.Locator("textarea");
        await Assertions.Expect(textarea).ToHaveValueAsync("@Dee I’ll try the smaller version tomorrow.");
        Assert.Equal(new[] { 5, 15 }, await textarea.EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
        await Assertions.Expect(thread.Locator(".lmx-discussion-composer-heading label")).ToHaveTextAsync("Reply to Dee");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        var accepted = thread.Locator($"[data-discussion-reply-id='{payloads[0]["replyId"]!.GetValue<string>()}']");
        await Assertions.Expect(accepted).ToBeFocusedAsync();
        await Assertions.Expect(accepted.Locator(".lmx-discussion-context-link")).ToContainTextAsync("Dee");
        Assert.Equal(payloads[0]["replyId"]!.GetValue<string>(), payloads[1]["replyId"]!.GetValue<string>());
        Assert.Equal("r3", payloads[1]["replyToId"]!.GetValue<string>());
        Assert.Null(await page.EvaluateAsync<string?>("sessionStorage.getItem('lmx-discussion-drafts:p1')"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await CaptureReliabilityAsync(thread, $"posted-{width}-{theme}.png");
        await accepted.Locator(".lmx-discussion-context-link").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id='r3']")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task DiscussionReliability_SourceEditsAndDeletionUpdatePublishedAndDraftContext()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = notes.AsArray()[0]!;
            var parent = post["replies"]!.AsArray().Single(reply => (string?)reply!["id"] == "r6")!;
            var child = JsonSerializer.SerializeToNode(Reply("context-child", "p2", "Ari", "Thanks for the detail.", "2026-06-30T13:00:00Z"))!;
            child["replyToId"] = "r6";
            child["replyTo"] = new JsonObject { ["displayName"] = parent["displayName"]!.DeepClone(), ["body"] = parent["body"]!.DeepClone() };
            post["replies"]!.AsArray().Add(child);
            post["replyCount"] = post["replyCount"]!.GetValue<int>() + 1;
        }
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        const string revised = "Updated source wording.";
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", async route => {
            JsonNode? updated = null;
            foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! }) {
                var replies = notes.AsArray()[0]!["replies"]!.AsArray();
                updated = replies.Single(reply => (string?)reply!["id"] == "r6")!;
                updated["body"] = revised;
                updated["editedAtUtc"] = "2026-07-01T10:00:00Z";
                replies.Single(reply => (string?)reply!["id"] == "context-child")!["replyTo"]!["body"] = revised;
            }
            await FulfillJsonAsync(route, updated!.ToJsonString());
        });
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/delete", async route => {
            foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! }) {
                var post = notes.AsArray()[0]!;
                var replies = post["replies"]!.AsArray();
                replies.Remove(replies.Single(reply => (string?)reply!["id"] == "r6"));
                replies.Single(reply => (string?)reply!["id"] == "context-child")!["replyTo"] = null;
                post["replyCount"] = post["replyCount"]!.GetValue<int>() - 1;
            }
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var thread = DiscussionThread(page, "p7", 5);
        var parentItem = thread.Locator("[data-discussion-reply-id='r6']");
        var childContext = thread.Locator("[data-discussion-reply-id='context-child'] .lmx-discussion-context-link");
        await parentItem.Locator("[data-discussion-reply-to]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Keep my response draft.");
        await thread.Locator("[data-reply-action='close']").ClickAsync();
        await parentItem.Locator("[data-discussion-reply-edit]").ClickAsync();
        await parentItem.Locator("textarea").FillAsync(revised);
        await parentItem.Locator("[data-reply-edit-submit]").ClickAsync();
        await Assertions.Expect(childContext).ToHaveAttributeAsync("title", revised);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-compose-context]")).ToContainTextAsync(revised);
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Keep my response draft.");
        page.Dialog += (_, dialog) => _ = dialog.AcceptAsync();
        await parentItem.Locator("[data-discussion-reply-delete]").ClickAsync();
        await Assertions.Expect(childContext).ToHaveTextAsync("Original reply unavailable");
        await Assertions.Expect(thread.Locator("[data-discussion-compose-context]")).ToContainTextAsync("Original reply unavailable");
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Keep my response draft.");
    }

    [Fact]
    public async Task DiscussionReliability_DiscardUndoRestoresSelectionAndProtectsNewerText()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState());
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Keep this draft.");
        await thread.Locator("textarea").EvaluateAsync("e => { e.setSelectionRange(5, 9); e.dispatchEvent(new Event('select')); }");
        await thread.Locator("[data-reply-action='discard']").ClickAsync();
        await thread.Locator("[data-discussion-discard-undo] button").ClickAsync();
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Keep this draft.");
        Assert.Equal(new[] { 5, 9 }, await thread.Locator("textarea").EvaluateAsync<int[]>("e => [e.selectionStart, e.selectionEnd]"));
        await thread.Locator("[data-reply-action='discard']").ClickAsync();
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Newer text must survive.");
        await Assertions.Expect(thread.Locator("[data-discussion-discard-undo] button")).ToBeDisabledAsync();
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Newer text must survive.");
    }

    [Fact]
    public async Task DiscussionReliability_MentionsPreserveExcessTextUndoAndComposition()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        var rows = state["public"]!["leaderboard"]!.AsArray();
        rows.Add(JsonSerializer.SerializeToNode(MentionLeaderboardRow("accent", "José Silva")));
        rows.Add(JsonSerializer.SerializeToNode(MentionLeaderboardRow("duplicate1", "Same Name")));
        rows.Add(JsonSerializer.SerializeToNode(MentionLeaderboardRow("duplicate2", "Same Name")));
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        var textarea = thread.Locator("textarea");
        var original = "@Jose " + new string('x', 225) + " END";
        await textarea.FillAsync(original);
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 5); e.click(); }");
        await thread.Locator(".lmx-mention-option", new() { HasText = "José Silva" }).ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("@José Silva " + new string('x', 225) + " END");
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToContainTextAsync("over");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeDisabledAsync();
        await textarea.PressAsync("Control+z");
        await Assertions.Expect(textarea).ToHaveValueAsync(original);
        await textarea.FillAsync("@Same");
        await Assertions.Expect(thread.Locator(".lmx-mention-option")).ToHaveCountAsync(0);
        await textarea.FillAsync("@Jo");
        await textarea.DispatchEventAsync("compositionstart");
        await textarea.DispatchEventAsync("keydown", new { key = "Enter", isComposing = true });
        await Assertions.Expect(textarea).ToHaveValueAsync("@Jo");
        await textarea.DispatchEventAsync("compositionend");
        await Assertions.Expect(thread.Locator(".lmx-mention-option")).ToHaveCountAsync(1);
        foreach (var command in new[] {
            "() => false",
            "(_command, _showUi, value) => { const input = document.activeElement; input.setRangeText(value, input.selectionStart, input.selectionEnd, 'end'); return false; }"
        }) {
            await page.EvaluateAsync($"() => {{ document.execCommand = {command}; }}");
            await textarea.FillAsync("@Jose keep the rest.");
            await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 5); e.click(); }");
            await thread.Locator(".lmx-mention-option", new() { HasText = "José Silva" }).ClickAsync();
            await Assertions.Expect(textarea).ToHaveValueAsync("@José Silva keep the rest.");
        }
        await textarea.FillAsync(new string('a', 260));
        await Assertions.Expect(textarea).ToHaveValueAsync(new string('a', 260));
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync("20 over");
        var note = page.Locator("#lmxCheckinList textarea[id^='lmx-note-']");
        await note.FillAsync(new string('n', 260));
        await Assertions.Expect(note).ToHaveValueAsync(new string('n', 260));
        await Assertions.Expect(page.Locator("#lmxCheckinList [data-note-character-count]")).ToHaveTextAsync("20 over");
        await Assertions.Expect(page.Locator("#lmxCheckinList button[type='submit']")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task DiscussionReliability_StorageFailureAndDifferentParticipantNeverLoseOrExposeDrafts()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.EvaluateAsync("() => { Storage.prototype.setItem = function(){throw new Error('storage unavailable')}; }");
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Keep this page open.");
        await Assertions.Expect(thread.Locator(".lmx-status")).ToContainTextAsync("Keep this page open.");
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Keep this page open.");
        page.Dialog += (_, modal) => _ = modal.AcceptAsync();
        await page.ReloadAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Private unpublished wording.");
        state["participant"]!["id"] = "different-participant";
        await page.ReloadAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("");
        Assert.DoesNotContain("Private unpublished wording.", await page.Locator("body").InnerTextAsync());
    }

    [Fact]
    public async Task DiscussionReliability_ExpiredSignInReturnsToDraftWithoutPosting()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            Interlocked.Increment(ref requests);
            await route.FulfillAsync(new() { Status = 401, ContentType = "application/json", Body = "{\"message\":\"Expired access\"}" });
        });
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await thread.Locator("textarea").FillAsync("Keep this through sign-in.");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToHaveTextAsync("Sign in");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToBeFocusedAsync();
        await page.EvaluateAsync("window.dispatchEvent(new StorageEvent('storage', { key:'lmxAccessToken',newValue:'fresh-token',storageArea:localStorage }))");
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("Keep this through sign-in.");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToHaveTextAsync("Post reply");
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task DiscussionReliability_PhotosRetryAndIgnoreAnOlderImageFailure()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
            notes.AsArray()[0]!["images"] = JsonSerializer.SerializeToNode(new[] {
                new { url = "/mock/reliability-first.svg", width = 1600, height = 900 },
                new { url = "/mock/reliability-second.svg", width = 1600, height = 900 }
            });
        var row = state["public"]!["leaderboard"]!.AsArray().First(item => item!["participantId"]!.GetValue<string>() == "p5")!;
        row["profileImageUrl"] = "/mock/reliability-avatar.svg";
        var loadFirst = false;
        const string photo = "<svg xmlns='http://www.w3.org/2000/svg' width='1600' height='900'><rect width='1600' height='900' fill='#007d87'/></svg>";
        await context.RouteAsync("**/mock/reliability-*.svg", route => {
            var fail = route.Request.Url.Contains("avatar") || (route.Request.Url.Contains("first") && !loadFirst);
            return route.FulfillAsync(new() { Status = fail ? 503 : 200, ContentType = fail ? "text/plain" : "image/svg+xml", Body = fail ? "Unavailable" : photo });
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator(".lmx-note-photo").First.ClickAsync();
        var viewer = page.Locator("#lmxNotePhotoViewer");
        var retry = viewer.Locator("[data-photo-retry]");
        await Assertions.Expect(retry).ToBeVisibleAsync();
        await Assertions.Expect(viewer.Locator("#lmxNotePhotoViewerClose")).ToBeEnabledAsync();
        await CaptureReliabilityAsync(viewer, "photo-retry-mobile.png");
        loadFirst = true;
        await retry.ClickAsync();
        await Assertions.Expect(viewer.Locator(".lmx-photo-load-status")).ToBeHiddenAsync();
        await viewer.Locator(".lmx-photo-viewer-stage img").EvaluateAsync("e => window.previousDiscussionImage = e");
        await viewer.Locator("#lmxNotePhotoViewerNext").ClickAsync();
        await Assertions.Expect(viewer.Locator(".lmx-photo-load-status")).ToBeHiddenAsync();
        await page.EvaluateAsync("window.previousDiscussionImage.dispatchEvent(new Event('error'))");
        await Assertions.Expect(viewer.Locator(".lmx-photo-load-status")).ToBeHiddenAsync();
        await Assertions.Expect(viewer.Locator(".lmx-photo-viewer-stage img")).ToHaveAttributeAsync("src", "/mock/reliability-second.svg");
        await viewer.Locator("#lmxNotePhotoViewerClose").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id='r3'] .lmx-discussion-author-avatar img")).Not.ToHaveAttributeAsync("src", "/mock/reliability-avatar.svg");
    }

    [Fact]
    public async Task DiscussionReliability_LatePostDoesNotPullBackAReaderWhoScrolledAway()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var replyId = "";
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!.AsObject();
            replyId = payload["replyId"]!.GetValue<string>();
            requested.SetResult();
            await release.Task;
            AppendReliabilityReply(state, payload);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        try
        {
            var thread = DiscussionThread(page, "p7", 5);
            await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
            await thread.Locator("textarea").FillAsync("Keep reading while this saves.");
            await thread.Locator("[data-reply-submit]").ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await page.Mouse.WheelAsync(0, 450);
            await page.WaitForFunctionAsync("scrollY > 100");
            release.SetResult();
            var saved = thread.Locator($"[data-discussion-reply-id='{replyId}']");
            await Assertions.Expect(saved).ToHaveCountAsync(1);
            await Assertions.Expect(saved).Not.ToBeFocusedAsync();
        }
        finally { release.TrySetResult(); }
    }

    private static void AppendReliabilityReply(JsonObject state, JsonObject payload)
    {
        foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = collection.AsArray().First(item => item!["participantId"]!.GetValue<string>() == "p7")!;
            var replies = post["replies"]!.AsArray();
            var reply = JsonSerializer.SerializeToNode(Reply(payload["replyId"]!.GetValue<string>(), "p1", "Browser Tester",
                payload["body"]!.GetValue<string>(), "2026-07-01T10:00:00Z"))!;
            reply["replyToId"] = payload["replyToId"]?.DeepClone();
            if (payload["replyToId"]?.GetValue<string>() is { } id && replies.FirstOrDefault(item => item!["id"]!.GetValue<string>() == id) is { } parent)
                reply["replyTo"] = new JsonObject { ["displayName"] = parent["displayName"]!.DeepClone(), ["body"] = parent["body"]!.DeepClone() };
            replies.Add(reply);
            post["replyCount"] = post["replyCount"]!.GetValue<int>() + 1;
        }
    }

    private static async Task CaptureReliabilityAsync(ILocator element, string name)
    {
        if (Environment.GetEnvironmentVariable("LWC_DISCUSSION_ARTIFACTS") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        await element.ScreenshotAsync(new() { Path = Path.Combine(directory, name) });
    }
}
