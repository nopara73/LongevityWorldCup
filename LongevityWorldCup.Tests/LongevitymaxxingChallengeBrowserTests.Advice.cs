using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    private const string AdviceStory = "Travel knocked out sleep and exercise. Once one thing slipped, I wrote the whole day off. How do you stop that spiral?";
    private const string AdviceCheckInNote = "Travel knocked out sleep and exercise.";
    private const string FirstAdviceId = "00000000000000000000000000000007";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdviceComposer_RestoresLegacyPhotoDraftsWithoutBlockingOrDuplicatingAdvice(bool submitted)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1, advisor: true);
        const string body = "Keep one small daily anchor.";
        const string id = "11111111111111111111111111111111";
        var active = JsonSerializer.Serialize(new[] { "p2", "reply", "p1", "22", "", "advice", "" });
        var stored = JsonSerializer.Serialize(new { version = 1, active, drafts = new[] { new {
            participantId = "p2", postParticipantId = "p1", challengeDay = 22, systemPostId = (string?)null,
            displayName = "Alex", editReplyId = (string?)null, originalBody = "", body, selectionStart = 5, selectionEnd = 9,
            replyId = id, submittedBody = submitted ? body : null, submittedReplyToId = (string?)null,
            isAdvice = true, hadPhotos = true, submittedPhotoSignature = "old-photo"
        } } });
        await context.AddInitScriptAsync($"sessionStorage.setItem('lmx-discussion-drafts:p2', {JsonSerializer.Serialize(stored)})");
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            calls++;
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal(id, (string?)payload["replyId"]);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await Assertions.Expect(post.Locator("textarea")).ToHaveValueAsync(body);
        if (submitted)
        {
            await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeDisabledAsync();
            await post.Locator("[data-reply-photos]").SetInputFilesAsync(ProfilePhotoFile("original.png"));
            await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeEnabledAsync();
            await post.Locator("textarea").FillAsync("Changed text must not create another advice.");
            await post.Locator("[data-reply-submit]").ClickAsync();
            await Assertions.Expect(post.Locator(".lmx-status")).ToContainTextAsync("Keep the original text");
            await post.Locator("textarea").FillAsync(body);
            await post.Locator("[data-reply-submit]").ClickAsync();
            await Assertions.Expect(post.Locator(".lmx-status")).ToContainTextAsync("Reattach the same photos");
            Assert.Equal(0, calls);
            var draft = JsonNode.Parse((await page.EvaluateAsync<string>("sessionStorage.getItem('lmx-discussion-drafts:p2')")))!["drafts"]![0]!;
            Assert.Equal(id, (string?)draft["replyId"]);
            Assert.Equal("old-photo", (string?)draft["submittedPhotoSignature"]);
        }
        else
        {
            await Assertions.Expect(post.Locator("[data-reply-photo-button]")).ToHaveCountAsync(0);
            await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeEnabledAsync();
            await post.Locator("[data-reply-submit]").ClickAsync();
            await Assertions.Expect(post.Locator("textarea")).ToHaveCountAsync(0);
            Assert.Equal(1, calls);
        }
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task Advice_UnansweredRequestStaysAboveNewerDiscussionsUntilAdviceArrives(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme, HasTouch = width < 500
        });
        await RouteUnreadBaselineAsync(context);
        var state = AdviceFeedBrowserState();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.True(payload["isAdvice"]!.GetValue<bool>());
            var answered = AdviceFeedBrowserState(true, replyId: payload["replyId"]!.GetValue<string>());
            state["notes"] = answered["notes"]!.DeepClone();
            state["public"] = answered["public"]!.DeepClone();
            state["updatedDiscussionThread"] = state["notes"]!.AsArray().Single(note => note!["advice"] != null)!.DeepClone();
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var posts = page.Locator("#lmxNotes > article");
        var request = page.Locator("#lmxNotes > .lmx-advice-post");
        await Assertions.Expect(posts.First).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("lmx-needs-advice"));
        await Assertions.Expect(request.Locator(".lmx-advice-badge")).ToHaveTextAsync("Needs advice");
        Assert.NotEqual(await posts.Nth(1).EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"),
            await request.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        await CaptureAdviceAsync(page, $"unanswered-request-{width}-{theme}.png", request);
        var header = await request.Locator(".lmx-discussion-post-header").BoundingBoxAsync();
        Assert.True(header!.Height <= 96, "The request metadata must not create a tall empty header.");
        await page.Locator("#lmxNotesTitle").EvaluateAsync("element => window.scrollBy({top: element.getBoundingClientRect().top - 80, behavior: 'instant'})");
        await CaptureAdviceAsync(page, $"unanswered-feed-{width}-{theme}.png");
        await request.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        await request.Locator("[data-discussion-draft] textarea").FillAsync("Keep one daily anchor. A short walk after lunch is enough to start.");
        await request.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes > .lmx-needs-advice")).ToHaveCountAsync(0);
        await Assertions.Expect(request.Locator(".lmx-advice-badge")).ToHaveCountAsync(0);
        await Assertions.Expect(posts.First).ToHaveAttributeAsync("data-discussion-post-participant-id", "p2");
        Assert.Equal(await posts.First.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"),
            await request.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor"));
        await CaptureAdviceAsync(page, $"answered-request-{width}-{theme}.png", request);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Advice_RefreshRevalidatesAnOldPinnedRequestMissingFromTheBoundedState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceFeedBrowserState(normalCount: 99);
        var withdrawn = state["notes"]!.AsArray().Single(note => note!["advice"] != null)!.DeepClone();
        withdrawn["advice"]!["withdrawn"] = true;
        withdrawn["advice"]!["story"] = "";
        withdrawn["advice"]!["version"] = 3;
        var lookups = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/thread?**", async route =>
        {
            lookups++;
            await FulfillJsonAsync(route, new JsonObject { ["note"] = withdrawn.DeepClone(), ["systemPost"] = null }.ToJsonString());
        });
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var request = page.Locator("#lmxNotes > .lmx-advice-post");
        var ordinary = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p2']");
        await ordinary.Locator("[data-discussion-quick-reply]").ClickAsync();
        var draft = ordinary.Locator("textarea");
        await draft.FillAsync("Keep this reply while the request changes.");
        await draft.EvaluateAsync("e => { e.setSelectionRange(5, 15); window.adviceRefreshDraft = e; }");
        var newer = LargeDiscussionFeedState(100);
        state["notes"] = newer["notes"]!.DeepClone();
        state["public"]!["notes"] = newer["public"]!["notes"]!.DeepClone();
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(request.Locator(".lmx-advice-badge")).ToHaveTextAsync("Request withdrawn");
        await Assertions.Expect(page.Locator("#lmxNotes > .lmx-needs-advice")).ToHaveCountAsync(0);
        await Assertions.Expect(request).Not.ToContainTextAsync(AdviceStory);
        Assert.Equal(1, lookups);
        Assert.True(await draft.EvaluateAsync<bool>("e => e === window.adviceRefreshDraft && document.activeElement === e && e.selectionStart === 5 && e.selectionEnd === 15"));
        await Assertions.Expect(draft).ToHaveValueAsync("Keep this reply while the request changes.");
    }

    [Fact]
    public async Task Advice_AnAnswerElsewherePreservesTheFocusedDraftWhenThereAreMoreThanTenPins()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceFeedBrowserState();
        for (var i = 0; i < 12; i++)
        {
            var extra = state["notes"]!.AsArray().Single(note => (string?)note!["participantId"] == "p1")!.DeepClone();
            extra["participantId"] = $"pin-{i:00}";
            extra["displayName"] = $"Walker {i}";
            extra["advice"]!["id"] = $"case-{i}";
            state["notes"]!.AsArray().Add(extra);
        }
        state["public"]!["notes"] = state["notes"]!.DeepClone();
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var request = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p1']");
        await request.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        var draft = request.Locator("textarea");
        await draft.FillAsync("My advice is still being written.");
        await draft.EvaluateAsync("e => { e.setSelectionRange(3, 9); window.manyPinsDraft = e; }");
        var top = await draft.EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        var answered = AdviceFeedBrowserState(true)["notes"]!.AsArray().Single(note => note!["advice"] != null)!;
        answered["advice"]!["betterAdviceParticipantIds"] = new JsonArray("p2", "p4");
        foreach (var replies in new[] { answered["replies"]!.AsArray(), answered["advice"]!["replies"]!.AsArray() })
            foreach (var reply in replies)
            {
                reply!["participantId"] = "p3";
                reply["displayName"] = "Bea Builder";
            }
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
        {
            var index = notes.Select((note, index) => (note, index)).Single(pair => (string?)pair.note!["participantId"] == "p1").index;
            notes[index] = answered.DeepClone();
        }
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(request.Locator(".lmx-advice-badge")).ToHaveCountAsync(0);
        Assert.True(await draft.EvaluateAsync<bool>("e => e === window.manyPinsDraft && document.activeElement === e && e.selectionStart === 3 && e.selectionEnd === 9"));
        Assert.InRange(Math.Abs(top - await draft.EvaluateAsync<double>("e => e.getBoundingClientRect().top")), 0, 2);
        await Assertions.Expect(draft).ToHaveValueAsync("My advice is still being written.");
        await draft.PressAsync("Escape");
        await Assertions.Expect(request.Locator("textarea")).ToHaveCountAsync(0);
        await request.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        await Assertions.Expect(draft).ToHaveValueAsync("My advice is still being written.");
    }

    [Fact]
    public async Task Advice_AnOlderHistoryResponseCannotRestoreThePinAfterFirstAdvice()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceFeedBrowserState(normalCount: 99);
        var oldRequest = state["notes"]!.AsArray().Single(note => note!["advice"] != null)!.DeepClone();
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var historyCalls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/feed", async route =>
        {
            historyCalls++;
            await release.Task;
            await FulfillJsonAsync(route, new JsonObject
            {
                ["threads"] = new JsonArray(new JsonObject { ["note"] = oldRequest.DeepClone(), ["systemPost"] = null }),
                ["nextCursor"] = null
            }.ToJsonString());
        });
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            var answered = AdviceFeedBrowserState(true, 99, payload["replyId"]!.GetValue<string>());
            state["notes"] = answered["notes"]!.DeepClone();
            state["public"] = answered["public"]!.DeepClone();
            state["updatedDiscussionThread"] = state["notes"]!.AsArray().Single(note => note!["advice"] != null)!.DeepClone();
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        try
        {
            var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
            var older = page.Locator("#lmxFeedOlder");
            for (var count = 20; count <= 100; count += 10)
            {
                await older.ClickAsync();
                await Assertions.Expect(page.Locator("#lmxNotes > article")).ToHaveCountAsync(count);
            }
            await older.ClickAsync();
            await Assertions.Expect(older).ToBeDisabledAsync();
            var request = page.Locator("#lmxNotes > .lmx-advice-post");
            await request.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
            await request.Locator("textarea").FillAsync("Keep one daily anchor. A short walk after lunch is enough to start.");
            await request.Locator("[data-reply-submit]").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxNotes > .lmx-needs-advice")).ToHaveCountAsync(0);
            release.SetResult(true);
            await Assertions.Expect(older).ToBeEnabledAsync();
            await Assertions.Expect(page.Locator("#lmxNotes > .lmx-needs-advice")).ToHaveCountAsync(0);
            await Assertions.Expect(request.Locator(".is-active .lmx-advice-rank")).ToHaveTextAsync("#3");
            Assert.Equal(1, historyCalls);
        }
        finally { release.TrySetResult(true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Advice_PromotionKeepsAnOpenEditVisibleAndAppliesTheArchiveRules(bool editingAdvice)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        if (editingAdvice)
        {
            state["participant"]!["id"] = "p3";
            state["participant"]!["displayName"] = "Bea Builder";
        }
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var comment = page.Locator($"#lmxNotes [data-discussion-reply-id='{(editingAdvice ? FirstAdviceId : "old-comment")}']");
        await comment.Locator("[data-discussion-reply-edit]").ClickAsync();
        var draft = comment.Locator("textarea");
        await draft.FillAsync("My edit should stay visible after promotion.");
        await draft.EvaluateAsync("e => { e.setSelectionRange(3, 9); window.archivedEditDraft = e; }");
        var promoted = AdviceBrowserState(2);
        state["notes"] = promoted["notes"]!.DeepClone();
        state["public"]!["notes"] = promoted["public"]!["notes"]!.DeepClone();
        await page.Clock.FastForwardAsync(61_000);
        await page.Locator("#lmxFeedNew").ClickAsync();
        await Assertions.Expect(page.Locator($"#lmxNotes [data-advice-root-id='{FirstAdviceId}'] > .lmx-advice-archive")).ToHaveAttributeAsync("open", "");
        await Assertions.Expect(draft).ToBeVisibleAsync();
        Assert.True(await draft.EvaluateAsync<bool>("e => e === window.archivedEditDraft && e.selectionStart === 3 && e.selectionEnd === 9"));
        await Assertions.Expect(draft).ToHaveValueAsync("My edit should stay visible after promotion.");
        if (editingAdvice) await Assertions.Expect(comment.Locator("[data-reply-edit-submit]")).ToBeEnabledAsync();
        else await Assertions.Expect(comment.Locator("[data-reply-edit-submit]")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task AdviceStory_SavedCheckInPrefillsStoryAndPreservesClearedAndFailedDrafts(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme, Locale = "en-US"
        });
        var state = AdviceBrowserState(0, due: true);
        var publishCalls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal(0, payload["exercise"]!.GetValue<int>());
            Assert.Equal(AdviceCheckInNote, payload["note"]!.GetValue<string>());
            state["eligibleDays"]![0]!["existing"] = JsonSerializer.SerializeToNode(new { sleep = 2, exercise = 0, nutrition = 0, vices = 2, note = AdviceCheckInNote, images = Array.Empty<object>() });
            state["pendingAdvicePrompt"] = JsonSerializer.SerializeToNode(new { id = "case-22", challengeDay = 22, date = "2026-06-29", rawScore = 4, baselineRawScore = 8, characterLimit = 240, version = 1, checkInNote = AdviceCheckInNote });
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.RouteAsync("**/api/longevitymaxxing/advice/publish", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal(AdviceStory, payload["story"]!.GetValue<string>());
            if (++publishCalls == 1) { await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Temporarily unavailable.\"}" }); return; }
            state = AdviceBrowserState(0);
            state["notes"]![0]!["note"] = AdviceCheckInNote;
            state["public"]!["notes"]![0]!["note"] = AdviceCheckInNote;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        var form = page.Locator(".lmx-checkin-card");
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
            await form.Locator($"[data-key='{key}'] .lmx-answer-option:has(input[value='{(key is "sleep" or "vices" ? "2" : "0")}'])").ClickAsync();
        await form.Locator("textarea").FillAsync(AdviceCheckInNote);
        await form.Locator("button[type=submit]").ClickAsync();
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog.Locator("h2")).ToHaveTextAsync("This is unusual for you. What’s the story?");
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("button")).ToHaveCountAsync(2);
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceCheckInNote);
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true })).ToBeEnabledAsync();
        await CaptureAdviceAsync(page, $"story-prefilled-{width}-{theme}.png");
        await CaptureAdviceAsync(page, $"story-prefilled-panel-{width}-{theme}.png", dialog);
        await dialog.Locator("textarea").FillAsync("");
        await page.ReloadAsync();
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync("");
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Post", Exact = true })).ToBeDisabledAsync();
        await dialog.Locator("textarea").FillAsync(AdviceStory);
        await CaptureAdviceAsync(page, $"story-{width}-{theme}.png");
        await CaptureAdviceAsync(page, $"story-panel-{width}-{theme}.png", dialog);
        await page.ReloadAsync();
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceStory);
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog.Locator(".lmx-status")).ToContainTextAsync("Your story is kept");
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceStory);
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync(AdviceStory);
        await page.Locator("#lmxNotes .lmx-advice-context > summary").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-context")).ToContainTextAsync(AdviceCheckInNote);
        await CaptureAdviceAsync(page, $"request-{width}-{theme}.png", page.Locator("#lmxNotes > article").First);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task AdviceStory_ChangeScoresPreservesDraftAndRechecksTheCorrectedCheckIn(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme, Locale = "en-US", TimezoneId = "UTC"
        });
        var state = AdviceBrowserState(0, due: true);
        state["eligibleDays"]![0]!["existing"] = JsonSerializer.SerializeToNode(new
        {
            sleep = 2, exercise = 0, nutrition = 0, vices = 2, note = AdviceCheckInNote, images = Array.Empty<object>()
        });
        state["pendingAdvicePrompt"] = JsonSerializer.SerializeToNode(new
        {
            id = "case-22", challengeDay = 22, date = "2026-06-29", rawScore = 4,
            baselineRawScore = 8, characterLimit = 240, version = 1, checkInNote = AdviceCheckInNote
        });
        var checkInCalls = 0;
        var adviceMutationCalls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal(22, payload["challengeDay"]!.GetValue<int>());
            Assert.Equal(AdviceCheckInNote, payload["note"]!.GetValue<string>());
            if (++checkInCalls == 1)
            {
                await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Temporarily unavailable.\"}" });
                return;
            }
            var corrected = checkInCalls == 3;
            Assert.Equal(corrected ? 2 : 1, payload["exercise"]!.GetValue<int>());
            Assert.Equal(corrected ? 2 : 0, payload["nutrition"]!.GetValue<int>());
            state["eligibleDays"]![0]!["existing"] = JsonSerializer.SerializeToNode(new
            {
                sleep = 2, exercise = corrected ? 2 : 1, nutrition = corrected ? 2 : 0,
                vices = 2, note = AdviceCheckInNote, images = Array.Empty<object>()
            });
            if (corrected) state["pendingAdvicePrompt"] = null;
            else state["pendingAdvicePrompt"]!["rawScore"] = 5;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.RouteAsync("**/api/longevitymaxxing/advice/*", async route =>
        {
            adviceMutationCalls++;
            await route.FulfillAsync(new() { Status = 400, Body = "Unexpected advice mutation." });
        });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceCheckInNote);
        await CaptureAdviceAsync(page, $"story-change-scores-{width}-{theme}.png", dialog);
        const string draft = "I may have selected the wrong scores. Keep this draft while I check.";
        await dialog.Locator("textarea").FillAsync(draft);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        var form = page.Locator(".lmx-checkin-card");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToBeVisibleAsync();
        await Assertions.Expect(form).ToHaveAttributeAsync("data-day", "22");
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
            await Assertions.Expect(form.Locator($"[data-key='{key}'] input[value='{(key is "sleep" or "vices" ? "2" : "0")}']")).ToBeCheckedAsync();
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync(AdviceCheckInNote);
        await Assertions.Expect(form.Locator("button[type=submit]")).ToBeDisabledAsync();
        await CaptureAdviceAsync(page, $"story-edit-scores-{width}-{theme}.png", form);
        await page.Locator("#lmxCheckinDialogClose").ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(draft);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        await form.Locator("[data-key='exercise'] .lmx-answer-option:has(input[value='1'])").ClickAsync();
        await form.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(form).ToContainTextAsync("Temporarily unavailable.");
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(form.Locator("[data-key='exercise'] input[value='1']")).ToBeCheckedAsync();
        await form.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(draft);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true }).ClickAsync();
        foreach (var key in new[] { "exercise", "nutrition" })
            await form.Locator($"[data-key='{key}'] .lmx-answer-option:has(input[value='2'])").ClickAsync();
        await CaptureAdviceAsync(page, $"story-corrected-scores-{width}-{theme}.png", form);
        await form.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#lmxHomePanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToHaveCountAsync(0);
        Assert.Null(await page.EvaluateAsync<string?>("() => sessionStorage.getItem('lmx-advice-story:p1:case-22')"));
        Assert.Equal(3, checkInCalls);
        Assert.Equal(0, adviceMutationCalls);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task AdviceStory_ClosedCheckInDoesNotOfferAnUnavailableCorrection()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdvicePromptBrowserState("case-22", 22);
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Back", Exact = true })).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(640, ColorScheme.Light)]
    public async Task Advice_HigherRankTakesConversationAndArchiveRemainsReadable(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 1000 }, ColorScheme = theme, Locale = "en-US"
        });
        var state = AdviceBrowserState(1, advisor: true);
        if (width == 640) await context.AddInitScriptAsync("document.addEventListener('DOMContentLoaded',()=>document.documentElement.style.fontSize='200%')");
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.True(payload["isAdvice"]!.GetValue<bool>());
            Assert.Null(payload["adviceRootId"]);
            state = AdviceBrowserState(2, advisor: true, higherId: payload["replyId"]!.GetValue<string>());
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await Assertions.Expect(post.Locator(".is-active .lmx-advice-rank")).ToHaveTextAsync("#7");
        await Assertions.Expect(post.Locator(".lmx-discussion-post-actions [data-is-advice='true']")).ToHaveTextAsync("Give better advice");
        await Assertions.Expect(post.GetByRole(AriaRole.Button, new() { Name = "Join conversation", Exact = true })).ToHaveCountAsync(0);
        await CaptureAdviceAsync(page, $"first-advice-{width}-{theme}.png", post);
        await post.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        await CaptureAdviceAsync(page, $"advice-composer-empty-{width}-{theme}.png", post);
        await post.Locator("[data-discussion-draft] textarea").FillAsync("Keep one anchor even on travel days. Choose a ten-minute walk after lunch, then rebuild from there.");
        await post.EvaluateAsync("element => window.scrollBy({top: element.getBoundingClientRect().top - 80, behavior: 'instant'})");
        await CaptureAdviceAsync(page, $"advice-composer-page-{width}-{theme}.png");
        await CaptureAdviceAsync(page, $"advice-composer-{width}-{theme}.png", post);
        await post.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(post.Locator(".is-active .lmx-advice-rank")).ToHaveTextAsync("#3");
        await Assertions.Expect(post.Locator(".lmx-discussion-post-actions [data-is-advice='true']")).ToHaveCountAsync(0);
        var replyAction = post.Locator(".is-active > .lmx-discussion-reply-item [data-discussion-reply]");
        await Assertions.Expect(replyAction).ToHaveCountAsync(1);
        await Assertions.Expect(post.Locator(".is-active > .lmx-discussion-reply-item [data-discussion-reply-to]")).ToHaveCountAsync(0);
        await replyAction.ClickAsync();
        await Assertions.Expect(post.Locator("[data-discussion-draft]")).ToBeVisibleAsync();
        await replyAction.ClickAsync();
        await Assertions.Expect(post.Locator("[data-discussion-draft]")).ToHaveCountAsync(0);
        var old = post.Locator($".lmx-advice-branch[data-advice-root-id='{FirstAdviceId}']");
        var archive = old.Locator("details");
        await Assertions.Expect(archive.Locator("summary")).ToHaveTextAsync("Earlier advice");
        await Assertions.Expect(archive).Not.ToHaveAttributeAsync("open", "");
        var oldAdvice = archive.Locator($"[data-discussion-reply-id='{FirstAdviceId}']");
        await Assertions.Expect(oldAdvice).ToBeHiddenAsync();
        await Assertions.Expect(old.Locator("[data-discussion-reply-to]")).ToHaveCountAsync(0);
        await CaptureAdviceAsync(page, $"promoted-{width}-{theme}.png", post);
        await archive.Locator("summary").ClickAsync();
        await Assertions.Expect(oldAdvice).ToBeVisibleAsync();
        await Assertions.Expect(oldAdvice).ToContainTextAsync("Keep the restart small. One walk is a perfectly good first step.");
        await Assertions.Expect(archive).ToContainTextAsync("I can make a short walk happen after lunch.");
        await Assertions.Expect(archive.Locator("[data-discussion-reply-to], [data-discussion-reply-edit]")).ToHaveCountAsync(0);
        await CaptureAdviceAsync(page, $"archive-{width}-{theme}.png", post);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));

        await using var visitorContext = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 1000 }, ColorScheme = theme, Locale = "en-US"
        });
        if (width == 640) await visitorContext.AddInitScriptAsync("document.addEventListener('DOMContentLoaded',()=>document.documentElement.style.fontSize='200%')");
        var visitorState = AdviceBrowserState(2, advisor: true);
        visitorState["participant"]!["id"] = "p4";
        visitorState["participant"]!["displayName"] = "Dee";
        var visitorPage = await OpenDiscussionWorkspaceAsync(visitorContext, visitorState);
        visitorPage.PageError += (_, error) => errors.Add(error);
        var visitorPost = visitorPage.Locator("#lmxNotes > .lmx-advice-post");
        var giveAdvice = visitorPost.Locator(".lmx-discussion-post-actions [data-is-advice='true']");
        await Assertions.Expect(giveAdvice).ToBeVisibleAsync();
        await Assertions.Expect(giveAdvice).ToHaveTextAsync("Give better advice");
        Assert.False(await visitorPost.Locator(".lmx-discussion-post-header").EvaluateAsync<bool>("""
            header => {
                const author = header.querySelector('.lmx-discussion-post-author');
                const actions = [...header.querySelectorAll('.lmx-discussion-post-actions button')].map(button => button.getBoundingClientRect());
                const text = document.createTreeWalker(author, NodeFilter.SHOW_TEXT);
                while (text.nextNode()) {
                    if (!text.currentNode.textContent.trim()) continue;
                    const range = document.createRange(); range.selectNodeContents(text.currentNode);
                    if ([...range.getClientRects()].some(glyph => actions.some(action =>
                        glyph.left < action.right - 1 && glyph.right > action.left + 1 &&
                        glyph.top < action.bottom - 1 && glyph.bottom > action.top + 1))) return true;
                }
                return false;
            }
            """), "Advice actions must not overlap the author's text.");
        await CaptureAdviceAsync(visitorPage, $"give-advice-collapsed-{width}-{theme}.png", visitorPost);
        await visitorPost.Locator(".lmx-advice-archive > summary").ClickAsync();
        await CaptureAdviceAsync(visitorPage, $"give-advice-expanded-{width}-{theme}.png", visitorPost);
        await giveAdvice.ClickAsync();
        var visitorDraft = visitorPost.Locator("[data-discussion-draft] textarea");
        await Assertions.Expect(visitorDraft).ToBeVisibleAsync();
        await Assertions.Expect(giveAdvice).ToBeHiddenAsync();
        await visitorDraft.PressAsync("Escape");
        await Assertions.Expect(giveAdvice).ToBeVisibleAsync();
        await Assertions.Expect(giveAdvice).ToHaveTextAsync("Give better advice");
        Assert.True(await visitorPage.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Advice_AViewerOutsideTheCapturedEligibilityCanReplyButCannotGiveNewAdvice()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 1000 }, ColorScheme = ColorScheme.Light, Locale = "en-US"
        });
        var state = AdviceBrowserState(2, advisor: true);
        state["participant"]!["id"] = "p5";
        state["participant"]!["displayName"] = "Cam";
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await Assertions.Expect(post.Locator(".lmx-discussion-post-actions [data-is-advice='true']")).ToHaveCountAsync(0);
        await Assertions.Expect(post.Locator(".is-active [data-discussion-reply]")).ToBeVisibleAsync();
        await CaptureAdviceAsync(page, "better-advice-ineligible-390-Light.png", post);
    }

    [Fact]
    public async Task Advice_LosingBetterAdviceEligibilityKeepsTheDraftCopyableAcrossReload()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1, advisor: true);
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var post = page.Locator("#lmxNotes > .lmx-advice-post");
        await post.Locator(".lmx-discussion-post-actions [data-is-advice='true']").ClickAsync();
        const string body = "Keep this alternative so I can copy it later.";
        await post.Locator("textarea").FillAsync(body);
        var promoted = AdviceBrowserState(2, advisor: true);
        foreach (var note in new[] { promoted["notes"]![0]!, promoted["public"]!["notes"]![0]! })
        {
            note["advice"]!["betterAdviceParticipantIds"] = new JsonArray();
            foreach (var replies in new[] { note["replies"]!.AsArray(), note["advice"]!["replies"]!.AsArray() })
                foreach (var reply in replies.Where(reply => (string?)reply!["participantId"] == "p2"))
                {
                    reply!["participantId"] = "p4";
                    reply["displayName"] = "Dee";
                    reply["adviceRank"] = 1;
                }
        }
        state["notes"] = promoted["notes"]!.DeepClone();
        state["public"]!["notes"] = promoted["public"]!["notes"]!.DeepClone();
        await page.ReloadAsync();
        await Assertions.Expect(post.Locator("textarea")).ToHaveValueAsync(body);
        await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeDisabledAsync();
        await Assertions.Expect(post.Locator("[data-discussion-draft] .lmx-status")).ToContainTextAsync("Your draft is kept");
        await post.Locator("textarea").PressAsync("Escape");
        await post.Locator("[data-advice-draft-resume]").ClickAsync();
        await Assertions.Expect(post.Locator("textarea")).ToHaveValueAsync(body);
        await Assertions.Expect(post.Locator("[data-reply-submit]")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task Advice_ArchivedReplyPermalinkOpensHistoryAndActiveCommentsCarryTheirRoot()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(2, advisor: true);
        JsonNode? submitted = null;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            submitted = JsonNode.Parse(route.Request.PostData!);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.GotoAsync("/longevitymaxxing#discussion/post/p1/22/reply/old-comment");
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-reply-id='old-comment']")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-archive")).ToHaveAttributeAsync("open", "");
        await page.Locator("#lmxNotes .is-active [data-discussion-reply][data-advice-root-id]").ClickAsync();
        await page.Locator("#lmxNotes [data-discussion-draft] textarea").FillAsync("A useful next step.");
        await page.Locator("#lmxNotes [data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-draft]")).ToHaveCountAsync(0);
        Assert.NotNull(submitted);
        Assert.False(submitted!["isAdvice"]!.GetValue<bool>());
        Assert.Equal("higher-advice", submitted["adviceRootId"]!.GetValue<string>());
        Assert.Equal("higher-advice", submitted["replyToId"]!.GetValue<string>());
    }

    [Fact]
    public async Task AdviceStory_AnotherTabsQueuedPromptCannotReceiveThePreviousStory()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdvicePromptBrowserState("case-22", 22);
        JsonNode? lastPublication = null;
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/advice/publish", async route =>
        {
            if (++calls == 1)
            {
                state = AdvicePromptBrowserState("case-21", 21);
                await route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "{\"message\":\"Handled elsewhere.\",\"code\":\"advice_changed\"}" });
                return;
            }
            lastPublication = JsonNode.Parse(route.Request.PostData!);
            state["pendingAdvicePrompt"] = null;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        await dialog.Locator("textarea").FillAsync("This explanation belongs to day 22.");
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog.Locator(".lmx-status")).ToContainTextAsync("another tab");
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync("This explanation belongs to day 22.");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Continue", Exact = true }).ClickAsync();
        await Assertions.Expect(dialog).ToHaveAttributeAsync("aria-description", "Day 21 · Monday, Jun 29");
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync("");
        await dialog.Locator("textarea").FillAsync("This explanation belongs to day 21.");
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        Assert.Equal("case-21", lastPublication!["adviceId"]!.GetValue<string>());
        Assert.Equal("This explanation belongs to day 21.", lastPublication["story"]!.GetValue<string>());
    }

    [Fact]
    public async Task Advice_PromotionDuringSubmissionKeepsDraftAndRequiresExplicitRetargeting()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        var calls = 0;
        JsonNode? submitted = null;
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            if (++calls == 1)
            {
                state = AdviceBrowserState(2);
                await route.FulfillAsync(new() { Status = 409, ContentType = "application/json", Body = "{\"message\":\"Conversation moved.\",\"code\":\"advice_changed\"}" });
                return;
            }
            submitted = JsonNode.Parse(route.Request.PostData!);
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator("#lmxNotes .is-active [data-discussion-reply][data-advice-root-id]").ClickAsync();
        var composer = page.Locator("#lmxNotes [data-discussion-draft]");
        await composer.Locator("textarea").FillAsync("Keep this thought while the lead changes.");
        await composer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(composer.Locator("[data-advice-retarget]")).ToBeVisibleAsync();
        await Assertions.Expect(composer.Locator("textarea")).ToHaveValueAsync("Keep this thought while the lead changes.");
        await Assertions.Expect(composer.Locator("[data-reply-submit]")).ToBeDisabledAsync();
        await composer.Locator("[data-advice-retarget]").ClickAsync();
        await composer.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(composer).ToHaveCountAsync(0);
        Assert.Equal("higher-advice", submitted!["adviceRootId"]!.GetValue<string>());
        Assert.Equal("Keep this thought while the lead changes.", submitted["body"]!.GetValue<string>());
    }

    [Fact]
    public async Task Advice_WithdrawalReplacesAnOlderThreadOmittedFromTheMutationState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(2);
        var withdrawals = 0;
        await context.RouteAsync("**/api/longevitymaxxing/advice/withdraw", async route =>
        {
            withdrawals++;
            var updated = state["notes"]![0]!.DeepClone();
            updated["advice"]!["withdrawn"] = true;
            updated["advice"]!["story"] = "";
            updated["advice"]!["activeAdviceReplyId"] = null;
            var response = state.DeepClone();
            response["notes"] = new JsonArray();
            response["public"]!["notes"] = new JsonArray();
            response["updatedDiscussionThread"] = updated;
            await FulfillJsonAsync(route, response.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var confirmWithdrawal = false;
        page.Dialog += async (_, dialog) =>
        {
            if (confirmWithdrawal) await dialog.AcceptAsync();
            else await dialog.DismissAsync();
        };
        var withdraw = page.Locator("#lmxNotes [data-advice-withdraw]");
        await withdraw.ClickAsync();
        Assert.Equal(0, withdrawals);
        await Assertions.Expect(withdraw).ToBeFocusedAsync();
        await Assertions.Expect(withdraw).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync(AdviceStory);
        confirmWithdrawal = true;
        await page.Locator("#lmxNotes [data-advice-withdraw]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync("Advice request withdrawn");
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).Not.ToContainTextAsync(AdviceStory);
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-reply][data-advice-root-id]")).ToHaveCountAsync(0);
        Assert.Equal(1, withdrawals);
    }

    [Fact]
    public async Task AdviceStory_EditSurvivesAConversationRefreshAndSavesAgainstTheCurrentVersion()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        var refreshed = AdviceBrowserState(2);
        JsonNode? edited = null;
        var saveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", route => FulfillJsonAsync(route, refreshed.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/advice/edit", async route =>
        {
            edited = JsonNode.Parse(route.Request.PostData!);
            saveStarted.TrySetResult();
            await releaseSave.Task;
            foreach (var collection in new[] { refreshed["notes"]!, refreshed["public"]!["notes"]! })
                collection[0]!["advice"]!["story"] = edited!["story"]!.GetValue<string>();
            await FulfillJsonAsync(route, refreshed.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.Locator("#lmxNotes [data-advice-edit]").ClickAsync();
        var storyEditor = page.Locator("#lmxNotes .lmx-advice-story-editor");
        await storyEditor.Locator("textarea").FillAsync("An unfinished clarification that must survive the handoff.");
        await page.Locator("#lmxNotes .is-active [data-discussion-reply][data-advice-root-id]").ClickAsync();
        await page.Locator("#lmxNotes [data-discussion-draft] textarea").FillAsync("One more detail for the conversation.");
        await page.Locator("#lmxNotes [data-reply-submit]").ClickAsync();
        await Assertions.Expect(storyEditor.Locator("textarea")).ToHaveValueAsync("An unfinished clarification that must survive the handoff.");
        await storyEditor.Locator("[type=submit]").ClickAsync();
        await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            var save = storyEditor.Locator("[type=submit]");
            await Assertions.Expect(save).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(save).ToHaveAttributeAsync("data-lwc-wait", new System.Text.RegularExpressions.Regex("^(pending|shown)$"));
            await Assertions.Expect(save).ToBeDisabledAsync();
            await Assertions.Expect(save).ToHaveTextAsync("Save story");
            await Assertions.Expect(storyEditor.Locator("textarea")).ToBeEditableAsync();
            await Assertions.Expect(storyEditor.Locator("button[type=button]")).ToBeEnabledAsync();
        }
        finally { releaseSave.TrySetResult(); }
        await Assertions.Expect(storyEditor).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".lwc-wait-status")).ToHaveCountAsync(0);
        Assert.Equal(4, edited!["version"]!.GetValue<int>());
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync("An unfinished clarification that must survive the handoff.");
    }

    [Fact]
    public async Task Advice_PagingKeepsTheActiveDraftAndGroupsAllOlderRepliesUnderTheirAdvice()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        var replies = new List<JsonObject> { state["notes"]![0]!["advice"]!["replies"]![0]!.DeepClone().AsObject() };
        for (var i = 1; i <= 10; i++) replies.Add(AdviceBrowserReply($"paged-{i}", "p1", "Alex", $"Conversation update {i}.", $"2026-07-01T08:{i:00}:00Z", FirstAdviceId));
        foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            collection[0]!["replyCount"] = replies.Count;
            collection[0]!["replies"] = new JsonArray(replies.TakeLast(3).Select(reply => (JsonNode)reply.DeepClone()).ToArray());
            collection[0]!["advice"]!["replies"] = new JsonArray(new[] { replies[0] }.Concat(replies.TakeLast(3)).Select(reply => (JsonNode)reply.DeepClone()).ToArray());
        }
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", route => FulfillJsonAsync(route, JsonSerializer.Serialize(new
        {
            replies = replies.Take(8), totalCount = replies.Count, latestReplyIds = replies.TakeLast(3).Select(reply => reply["id"]!.GetValue<string>()),
            remainingEarlierReplyCount = 0, hasEarlier = false, nextBeforeCreatedAtUtc = (string?)null, nextBeforeReplyId = (string?)null
        })));
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.Locator("#lmxNotes [data-discussion-reply][data-advice-root-id]").ClickAsync();
        var textarea = page.Locator("#lmxNotes [data-discussion-draft] textarea");
        await textarea.FillAsync("Keep this draft while reading the older context.");
        await page.Locator("#lmxNotes [data-discussion-replies-page]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .is-active [data-discussion-reply-id]")).ToHaveCountAsync(11);
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep this draft while reading the older context.");
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-replies-page]")).ToHaveCountAsync(0);
    }

    private static JsonObject AdvicePromptBrowserState(string id, int day)
    {
        var state = AdviceBrowserState(0);
        state["notes"] = new JsonArray();
        state["public"]!["notes"] = new JsonArray();
        state["pendingAdvicePrompt"] = JsonSerializer.SerializeToNode(new { id, challengeDay = day, date = "2026-06-29", rawScore = 4, baselineRawScore = 8, characterLimit = 240, version = 1 });
        return state;
    }

    private static JsonObject AdviceFeedBrowserState(bool answered = false, int normalCount = 5, string replyId = FirstAdviceId)
    {
        var state = AdviceBrowserState(0, advisor: true);
        var request = state["notes"]![0]!.DeepClone();
        if (answered)
        {
            var reply = AdviceBrowserReply(replyId, "p2", "Ari Able",
                "Keep one daily anchor. A short walk after lunch is enough to start.", "2026-07-01T09:00:00Z", replyId, true, 3);
            request["advice"]!["activeAdviceReplyId"] = replyId;
            request["advice"]!["betterAdviceParticipantIds"] = new JsonArray("p4");
            request["advice"]!["version"] = 3;
            request["advice"]!["replies"] = new JsonArray(reply.DeepClone());
            request["replyCount"] = 1;
            request["replies"] = new JsonArray(reply.DeepClone());
            request["lastActivityAtUtc"] = "2026-07-01T09:00:00Z";
        }
        var notes = LargeDiscussionFeedState(normalCount)["notes"]!.AsArray().DeepClone().AsArray();
        notes.Add(request);
        state["notes"] = notes.DeepClone();
        state["public"]!["notes"] = notes;
        state["public"]!["systemDiscussionPosts"] = new JsonArray();
        return state;
    }

    private static JsonObject AdviceBrowserState(int stage, bool due = false, bool advisor = false, string higherId = "higher-advice")
    {
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: !due, includeMentionParticipants: true))!.AsObject();
        state["participant"]!["displayName"] = advisor ? "Ari Able" : "Alex";
        if (advisor) state["participant"]!["id"] = "p2";
        state["public"]!["leaderboard"]![0]!["displayName"] = "Alex";
        var replies = new List<JsonObject>();
        if (stage >= 1)
        {
            replies.Add(AdviceBrowserReply(FirstAdviceId, "p3", "Bea Builder", "Keep the restart small. One walk is a perfectly good first step.", "2026-07-01T08:00:00Z", FirstAdviceId, true, 7, stage >= 2));
            replies.Add(AdviceBrowserReply("old-comment", "p1", "Alex", "I can make a short walk happen after lunch.", "2026-07-01T08:05:00Z", FirstAdviceId, archived: stage >= 2));
            replies.Add(AdviceBrowserReply("old-answer", "p3", "Bea Builder", "That sounds doable. Let us know how it goes.", "2026-07-01T08:10:00Z", FirstAdviceId, archived: stage >= 2));
        }
        if (stage >= 2) replies.Add(AdviceBrowserReply(higherId, "p2", "Ari Able", "Keep one anchor even on travel days. Choose a ten-minute walk after lunch, then rebuild from there.", "2026-07-01T09:00:00Z", higherId, true, 3));
        var note = JsonSerializer.SerializeToNode(Note("p1", "Alex", 22, "2026-06-29", "", updatedAtUtc: "2026-07-01T07:30:00Z", replyCount: replies.Count,
            lastActivityAtUtc: stage >= 2 ? "2026-07-01T09:00:00Z" : "2026-07-01T08:10:00Z"))!.AsObject();
        note["note"] = null;
        note["replies"] = new JsonArray(replies.TakeLast(3).Select(reply => (JsonNode)reply.DeepClone()).ToArray());
        note["advice"] = new JsonObject
        {
            ["id"] = "case-22", ["story"] = AdviceStory, ["publishedAtUtc"] = "2026-07-01T07:30:00Z",
            ["activeAdviceReplyId"] = stage >= 2 ? higherId : stage == 1 ? FirstAdviceId : null,
            ["replies"] = new JsonArray(replies.Select(reply => (JsonNode)reply.DeepClone()).ToArray()), ["version"] = stage + 2, ["withdrawn"] = false,
            ["betterAdviceParticipantIds"] = stage == 1 ? new JsonArray("p2", "p4") : stage >= 2 ? new JsonArray("p4") : new JsonArray()
        };
        state["notes"] = due ? new JsonArray() : new JsonArray(note.DeepClone());
        state["public"]!["notes"] = due ? new JsonArray() : new JsonArray(note.DeepClone());
        state["pendingAdvicePrompt"] = null;
        return state;
    }

    private static JsonObject AdviceBrowserReply(string id, string owner, string name, string body, string time, string root,
        bool advice = false, int? rank = null, bool archived = false)
    {
        var reply = JsonSerializer.SerializeToNode(Reply(id, owner, name, body, time))!.AsObject();
        reply["isAdvice"] = advice;
        reply["adviceRootId"] = root;
        reply["adviceRank"] = rank;
        reply["isArchived"] = archived;
        return reply;
    }

    private static async Task CaptureAdviceAsync(IPage page, string name, ILocator? element = null)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_ADVICE_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        await page.EvaluateAsync("() => document.fonts.ready");
        await Assertions.Expect(page.Locator(".lmx-discussion-just-saved")).ToHaveCountAsync(0);
        if (element == null) { await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name) }); return; }
        var viewport = page.ViewportSize;
        var bounds = await element.BoundingBoxAsync();
        var expanded = viewport != null && bounds != null && bounds.Height > viewport.Height - 240;
        // Keep fixed page chrome outside a tall component capture at enlarged text sizes.
        if (expanded)
        {
            await page.SetViewportSizeAsync(viewport!.Width, (int)Math.Ceiling(bounds!.Height) + 480);
            await element.EvaluateAsync("element => window.scrollBy({top: element.getBoundingClientRect().top - 240, behavior: 'instant'})");
        }
        try { await element.ScreenshotAsync(new() { Path = Path.Combine(directory, name) }); }
        finally { if (expanded) await page.SetViewportSizeAsync(viewport!.Width, viewport.Height); }
    }
}
