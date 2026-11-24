using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task SavedPhotos_EditRemarksAndRemoveAnAttachmentOnThePost(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 840 }, IsMobile = width < 500, HasTouch = width < 500
        });
        await RouteChallengeResourcesAsync(context);
        var state = DiscussionWorkspaceState();
        var photo = CheckInImage("/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari");
        var own = JsonSerializer.SerializeToNode(Note("p1", "Browser Tester", 12, "2026-06-19", "Original remarks.", [photo],
            replies: [Reply("r1", "p2", "Ari", "Thanks for sharing.", "2026-06-19T09:00:00Z")]))!;
        var other = JsonSerializer.SerializeToNode(Note("p2", "Ari", 12, "2026-06-19", "Other remarks."))!;
        var calls = 0;
        void UpdateNotes()
        {
            state["notes"] = new JsonArray(own.DeepClone(), other.DeepClone());
            state["public"]!["notes"] = state["notes"]!.DeepClone();
        }
        UpdateNotes();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/post/edit", async route =>
        {
            calls++;
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal("browser-token", (string?)payload["accessToken"]);
            Assert.Equal(12, (int?)payload["challengeDay"]);
            Assert.Equal("Original remarks.", (string?)payload["originalNote"]);
            Assert.Equal("Corrected remarks.", (string?)payload["note"]);
            Assert.Equal("ari.webp", (string?)Assert.Single(payload["removedPhotoNames"]!.AsArray()));
            if (calls == 1) { await route.FulfillAsync(new() { Status = 503 }); return; }
            own["note"] = "Corrected remarks.";
            own["images"] = new JsonArray();
            UpdateNotes();
            state["updatedDiscussionThread"] = own.DeepClone();
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionPolishAsync(context, state);
        var thread = DiscussionThread(page, "p1", 12);
        await Assertions.Expect(DiscussionThread(page, "p2", 12).Locator("[data-edit-post]")).ToHaveCountAsync(0);
        var edit = thread.GetByRole(AriaRole.Button, new() { Name = "Edit remarks and photos" });
        var remarksLeft = await thread.Locator(":scope > p").EvaluateAsync<double>("p => p.getBoundingClientRect().left");
        await edit.ClickAsync();
        var editor = thread.Locator(".lmx-post-editor");
        await Assertions.Expect(editor.Locator("textarea")).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator("[data-edit-post]")).ToBeHiddenAsync();
        await AssertThreadRailAsync(thread);
        Assert.InRange(await editor.EvaluateAsync<double>("form => form.getBoundingClientRect().left") - remarksLeft, -1, 1);
        Assert.True(await editor.EvaluateAsync<bool>("""
            form => {
                const replies = form.parentElement.querySelector(':scope > [data-lmx-thread]');
                const rail = getComputedStyle(replies, '::before');
                const bounds = replies.getBoundingClientRect();
                const right = bounds.left + replies.clientLeft + parseFloat(rail.left) + parseFloat(rail.width);
                const top = bounds.top + replies.clientTop + parseFloat(rail.top);
                const editor = form.getBoundingClientRect();
                return right <= editor.left || top >= editor.bottom;
            }
            """), "The reply connector must clear the entire post editor.");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Remove photo 1" }).ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(edit).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator(".lmx-note-photo img")).ToHaveCountAsync(1);
        await edit.ClickAsync();
        await editor.Locator("textarea").FillAsync("Corrected remarks.");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Remove photo 1" }).ClickAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(editor.Locator("[role=status]")).Not.ToBeEmptyAsync();
        await Assertions.Expect(editor.Locator("textarea")).ToHaveValueAsync("Corrected remarks.");
        await Assertions.Expect(editor.GetByRole(AriaRole.Button, new() { Name = "Undo removal of photo 1" })).ToBeEnabledAsync();
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(editor).ToHaveCountAsync(0);
        await Assertions.Expect(thread.Locator(":scope > p")).ToHaveTextAsync("Corrected remarks.");
        await Assertions.Expect(thread.Locator(".lmx-note-photo")).ToHaveCountAsync(0);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SavedPhotos_ReplyEditKeepsRemovedPhotosInTheDraftUntilSaved()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteChallengeResourcesAsync(context);
        var state = DiscussionWorkspaceState();
        JsonNode? editable = null;
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var thread = notes.AsArray().Single(note => (string?)note!["participantId"] == "p7")!;
            editable = thread["replies"]!.AsArray().Single(reply => (string?)reply!["id"] == "r6")!;
            editable["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari") });
        }
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal("ari.webp", (string?)Assert.Single(payload["removedPhotoNames"]!.AsArray()));
            var updated = editable!.DeepClone();
            updated["images"] = new JsonArray();
            await FulfillJsonAsync(route, updated.ToJsonString());
        });
        var page = await OpenDiscussionPolishAsync(context, state);
        var reply = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r6']");
        var edit = reply.Locator("[data-discussion-reply-edit]");
        await edit.ClickAsync();
        var editor = reply.Locator("[data-discussion-draft]");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Remove photo 1" }).ClickAsync();
        await editor.Locator("[data-reply-action='close']").ClickAsync();
        await Assertions.Expect(reply.Locator(".lmx-reply-photos img")).ToBeVisibleAsync();
        await edit.ClickAsync();
        await Assertions.Expect(editor.GetByRole(AriaRole.Button, new() { Name = "Undo removal of photo 1" })).ToBeVisibleAsync();
        await editor.Locator("[data-reply-edit-submit]").ClickAsync();
        await Assertions.Expect(editor).ToHaveCountAsync(0);
        await Assertions.Expect(reply.Locator(".lmx-reply-photos img")).ToHaveCountAsync(0);
        await Assertions.Expect(reply.Locator("[data-discussion-reply-body]")).ToContainTextAsync("An actual child reply");
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1280)]
    public async Task SavedPhotos_RemoveUndoAndReplaceInAFullCheckIn(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 840 }, IsMobile = width < 500, HasTouch = width < 500
        });
        await RouteChallengeResourcesAsync(context);
        var state = CheckInWorkspaceState();
        var day = state["eligibleDays"]!.AsArray().Last()!.DeepClone();
        state["eligibleDays"] = new JsonArray(day);
        day["existing"] = SavedCheckIn("Keep my remarks.");
        var images = new JsonArray("ari bea cam dee".Split().Select(name => JsonSerializer.SerializeToNode(
            CheckInImage($"/generated/longevitymaxxing/check-in-photos/{name}.webp?v=1"))).ToArray());
        day["existing"]!["images"] = images;
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        var picker = form.GetByRole(AriaRole.Button, new() { Name = "Add photos", Exact = true });
        var save = form.Locator("button[type=submit]");
        var remove = form.GetByRole(AriaRole.Button, new() { Name = "Remove photo 2", Exact = true });
        var undo = form.GetByRole(AriaRole.Button, new() { Name = "Undo removal of photo 2" });
        await Assertions.Expect(picker).ToBeDisabledAsync();
        if (width < 500) await remove.TapAsync(); else await remove.ClickAsync();
        await Assertions.Expect(picker).ToBeEnabledAsync();
        await undo.ClickAsync();
        await Assertions.Expect(save).ToBeDisabledAsync();
        await remove.ClickAsync();
        await form.Locator("[data-checkin-reset]").ClickAsync();
        await Assertions.Expect(save).ToBeDisabledAsync();
        await form.Locator("[data-checkin-reset]").ClickAsync();
        await Assertions.Expect(undo).ToBeEnabledAsync();
        await AddCheckInPhotosAsync(form, "replacement.png");
        await Assertions.Expect(picker).ToBeDisabledAsync();
        await Assertions.Expect(undo).ToBeDisabledAsync();
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            var body = Encoding.UTF8.GetString(route.Request.PostDataBuffer!);
            Assert.Contains("name=\"removedPhotoNames\"\r\n\r\nbea.webp", body);
            Assert.Contains("replacement.webp", body);
            images.RemoveAt(1);
            images.Add(JsonSerializer.SerializeToNode(CheckInImage("/generated/longevitymaxxing/check-in-photos/new.webp")));
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await save.ClickAsync();
        await Assertions.Expect(form.Locator("[data-saved-photo='new.webp']")).ToBeVisibleAsync();
        await Assertions.Expect(form.Locator("[data-saved-photo='bea.webp']")).ToHaveCountAsync(0);
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync("Keep my remarks.");
        await Assertions.Expect(save).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(320, ColorScheme.Light)]
    public async Task DiscussionPhotos_PasteDropPreviewAndDraftContinuity(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            Permissions = ["clipboard-read", "clipboard-write"]
        });
        var page = await OpenDiscussionPolishAsync(context);
        // Other browser collections can open tabs while this test uses the real clipboard.
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setFocusEmulationEnabled", new() { ["enabled"] = true });
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        var composer = thread.Locator("[data-discussion-draft]");
        var textarea = composer.Locator("textarea");
        await textarea.FillAsync("Keep my caption.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 7); window.photoDraftTextarea = e; }");
        await page.EvaluateAsync("""
            async () => {
                const canvas = document.createElement('canvas'); canvas.width = 120; canvas.height = 80;
                const ctx = canvas.getContext('2d'); ctx.fillStyle = '#168b80'; ctx.fillRect(0, 0, 120, 80);
                ctx.fillStyle = '#e7f5e9'; ctx.fillRect(20, 20, 80, 40);
                const blob = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
                await navigator.clipboard.write([new ClipboardItem({'image/png': blob})]);
            }
            """);
        await page.Keyboard.PressAsync("Control+V");
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(1);
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep my caption.");
        Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.photoDraftTextarea && document.activeElement === e && e.selectionStart === 5 && e.selectionEnd === 7"));
        await page.EvaluateAsync("navigator.clipboard.writeText('our')");
        await page.Keyboard.PressAsync("Control+V");
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep our caption.");
        await TransferDiscussionPhotosAsync(composer, "drop", "walk.png", "lunch.png", "fourth.png", "fifth.png", "notes.txt");
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(4);
        await Assertions.Expect(composer.Locator("[data-reply-photo-feedback]")).ToContainTextAsync("4 photos maximum");
        await Assertions.Expect(composer.Locator("[data-reply-photo-feedback]")).ToContainTextAsync("unsupported");
        await Assertions.Expect(composer.Locator("[data-reply-photo-button]")).ToBeDisabledAsync();
        await composer.Locator("[data-remove-reply-photo]").Last.ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-photo-button]")).ToBeEnabledAsync();
        await composer.Locator("[data-reply-photo-previews] .lmx-note-photo").First.ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-viewer")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".lwc-viewer")).ToBeHiddenAsync();
        await composer.Locator("[data-reply-action='close']").ClickAsync();
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(3);
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep our caption.");
        await DiscussionThread(page, "p3", 21).Locator("[data-discussion-reply]").ClickAsync();
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(3);
        await Assertions.Expect(textarea).ToBeFocusedAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var controls = await composer.Locator("[data-reply-photo-button]").BoundingBoxAsync();
        Assert.NotNull(controls);
        Assert.True(controls.Width >= 44 && controls.Height >= 44);
        var captureDirectory = Environment.GetEnvironmentVariable("LWC_DISCUSSION_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            await composer.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, $"photos-{width}-{theme}.png") });
        }
    }

    [Fact]
    public async Task DiscussionPhotos_ImageOnlyRepliesKeepRetryIdentityAndRenderInTheViewer()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await RouteChallengeResourcesAsync(context);
        var state = DiscussionWorkspaceState();
        var requests = new List<string>();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            Assert.Contains("multipart/form-data", route.Request.Headers["content-type"]);
            var body = Encoding.UTF8.GetString(route.Request.PostDataBuffer!);
            Assert.Contains("name=\"photos\"", body);
            var id = Regex.Match(body, "name=\"replyId\"\\r\\n\\r\\n([^\\r]+)").Groups[1].Value;
            Assert.NotEmpty(id);
            requests.Add(id);
            if (requests.Count <= 2) { await route.FulfillAsync(new() { Status = 503 }); return; }
            var reply = JsonSerializer.SerializeToNode(new
            {
                id, participantId = "p1", displayName = "Browser Tester", body = "", createdAtUtc = "2026-07-01T10:00:00Z",
                images = new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/ari.webp?v=ari") }
            });
            foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
            {
                var note = notes.AsArray().Single(note => note!["participantId"]!.GetValue<string>() == "p7")!;
                note["replies"]!.AsArray().Add(reply!.DeepClone());
                note["replyCount"] = note["replyCount"]!.GetValue<int>() + 1;
            }
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionPolishAsync(context, state);
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        var composer = thread.Locator("[data-discussion-draft]");
        await TransferDiscussionPhotosAsync(composer, "paste", "first.png");
        var submit = composer.Locator("[data-reply-action='submit']");
        await Assertions.Expect(submit).ToBeEnabledAsync();
        await submit.ClickAsync();
        await Assertions.Expect(submit).ToHaveTextAsync("Retry");
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(1);
        await submit.ClickAsync();
        await Assertions.Expect(submit).ToHaveTextAsync("Retry");
        Assert.Equal(requests[0], requests[1]);
        await TransferDiscussionPhotosAsync(composer, "drop", "second.png");
        await submit.ClickAsync();
        await Assertions.Expect(composer).ToHaveCountAsync(0);
        Assert.NotEqual(requests[1], requests[2]);
        var posted = thread.Locator($"[data-discussion-reply-id='{requests[2]}']");
        await Assertions.Expect(posted.Locator(".lmx-note-photo")).ToHaveCountAsync(1);
        await posted.Locator(".lmx-note-photo").ScrollIntoViewIfNeededAsync();
        await posted.Locator(".lmx-note-photo img").EvaluateAsync("async image => { await image.decode(); }");
        var imageHit = await posted.Locator(".lmx-note-photo img").EvaluateAsync<string>("""
            image => { const r = image.getBoundingClientRect(); const hit = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2);
                return JSON.stringify({matches:hit === image, tag:hit?.tagName, className:hit?.className, pointer:getComputedStyle(image).pointerEvents,
                    visibility:getComputedStyle(image).visibility, rect:r.toJSON(), loaded:image.complete && image.naturalWidth > 0}); }
            """);
        Assert.Contains("\"matches\":true", imageHit);
        await posted.Locator(".lmx-note-photo").ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-viewer")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DiscussionPhotos_ReloadRequiresReattachmentAndDiscardUndoKeepsPhotos()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenDiscussionPolishAsync(context);
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-reply]").ClickAsync();
        var composer = thread.Locator("[data-discussion-draft]");
        await composer.Locator("textarea").FillAsync("Caption saved across reloads.");
        await TransferDiscussionPhotosAsync(composer, "paste", "photo.png");
        await composer.Locator("[data-reply-action='discard']").ClickAsync();
        await thread.Locator("[data-discussion-discard-undo] button").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-photo-previews] img")).ToHaveCountAsync(1);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", route => route.FulfillAsync(new() { Status = 503 }));
        await composer.Locator("[data-reply-action='submit']").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-reply-action='submit']")).ToHaveTextAsync("Retry");
        var guarded = false;
        page.Dialog += async (_, dialog) => { guarded |= dialog.Type == "beforeunload"; await dialog.AcceptAsync(); };
        await page.ReloadAsync();
        Assert.True(guarded);
        await Assertions.Expect(composer.Locator("textarea")).ToHaveValueAsync("Caption saved across reloads.");
        await Assertions.Expect(composer.Locator(".lmx-status")).ToContainTextAsync("Add them again");
        await Assertions.Expect(composer.Locator("[data-reply-action='submit']")).ToBeDisabledAsync();
        await TransferDiscussionPhotosAsync(composer, "paste", "photo.png");
        await Assertions.Expect(composer.Locator("[data-reply-action='submit']")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task DiscussionPhotos_CheckInSupportsTheSamePasteAndDropGestures()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        await form.Locator("textarea").FillAsync("Check-in caption.");
        await TransferDiscussionPhotosAsync(form, "paste", "checkin.png");
        await TransferDiscussionPhotosAsync(form, "drop", "walk.png");
        await Assertions.Expect(form.Locator("[data-photo-previews] img")).ToHaveCountAsync(2);
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync("Check-in caption.");
        await form.Locator("[data-photo-previews] .lmx-note-photo").First.ClickAsync();
        await Assertions.Expect(page.Locator(".lwc-viewer")).ToBeVisibleAsync();
    }

    private static Task TransferDiscussionPhotosAsync(ILocator target, string kind, params string[] names) => target.EvaluateAsync("""
        (target, {kind, names}) => {
            const bytes = Uint8Array.from(atob('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aWQAAAABJRU5ErkJggg=='), c => c.charCodeAt(0));
            const transfer = new DataTransfer();
            for (const name of names) transfer.items.add(new File([bytes], name, {type: name.endsWith('.png') ? 'image/png' : 'text/plain', lastModified: 123}));
            if (kind === 'drop') {
                target.dispatchEvent(new DragEvent('dragenter', {dataTransfer:transfer, bubbles:true, cancelable:true}));
                if (!target.classList.contains('is-photo-drop-target')) throw new Error('No drop feedback');
                target.dispatchEvent(new DragEvent('drop', {dataTransfer:transfer, bubbles:true, cancelable:true}));
                if (target.classList.contains('is-photo-drop-target')) throw new Error('Drop feedback was not cleared');
            } else target.querySelector('textarea').dispatchEvent(new ClipboardEvent('paste', {clipboardData:transfer, bubbles:true, cancelable:true}));
        }
        """, new { kind, names });
}
