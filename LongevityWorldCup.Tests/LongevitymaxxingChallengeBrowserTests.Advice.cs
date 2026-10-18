using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    private const string AdviceStory = "Travel knocked out sleep and exercise. Once one thing slipped, I wrote the whole day off. How do you stop that spiral?";
    private const string FirstAdviceId = "00000000000000000000000000000007";

    [Theory]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task AdviceStory_SavedCheckInPausesForExplicitPublicationAndPreservesFailedDraft(int width, ColorScheme theme)
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
            state["eligibleDays"]![0]!["existing"] = JsonSerializer.SerializeToNode(new { sleep = 2, exercise = 0, nutrition = 0, vices = 2, note = (string?)null, images = Array.Empty<object>() });
            state["pendingAdvicePrompt"] = JsonSerializer.SerializeToNode(new { id = "case-22", challengeDay = 22, date = "2026-06-29", rawScore = 4, baselineRawScore = 8, characterLimit = 240, version = 1 });
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await context.RouteAsync("**/api/longevitymaxxing/advice/publish", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            Assert.Equal(AdviceStory, payload["story"]!.GetValue<string>());
            if (++publishCalls == 1) { await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Temporarily unavailable.\"}" }); return; }
            state = AdviceBrowserState(0);
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
        await form.Locator("button[type=submit]").ClickAsync();
        var dialog = page.Locator("#lmxAdviceStoryDialog");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToHaveCountAsync(0);
        await Assertions.Expect(dialog.Locator("h2")).ToHaveTextAsync("What's the story?");
        await dialog.Locator("textarea").FillAsync(AdviceStory);
        await CaptureAdviceAsync(page, $"story-{width}-{theme}.png");
        await page.ReloadAsync();
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceStory);
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog.Locator(".lmx-status")).ToContainTextAsync("Your story is kept");
        await Assertions.Expect(dialog.Locator("textarea")).ToHaveValueAsync(AdviceStory);
        await dialog.Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(dialog).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync(AdviceStory);
        await CaptureAdviceAsync(page, $"request-{width}-{theme}.png", page.Locator("#lmxNotes > article").First);
        Assert.Empty(errors);
    }

    [Theory]
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
        await Assertions.Expect(post.Locator(".is-active .lmx-advice-branch-heading")).ToContainTextAsync("Challenge #7");
        await CaptureAdviceAsync(page, $"first-advice-{width}-{theme}.png", post);
        await post.Locator(".lmx-discussion-post-actions [data-discussion-reply]").ClickAsync();
        await post.Locator("[data-discussion-draft] textarea").FillAsync("Keep one anchor even on travel days. Choose a ten-minute walk after lunch, then rebuild from there.");
        await post.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(post.Locator(".is-active .lmx-advice-branch-heading")).ToContainTextAsync("Challenge #3");
        var old = post.Locator($".lmx-advice-branch[data-advice-root-id='{FirstAdviceId}']");
        var archive = old.Locator("details");
        await Assertions.Expect(archive).Not.ToHaveAttributeAsync("open", "");
        await Assertions.Expect(old.Locator("[data-discussion-reply-to]")).ToHaveCountAsync(0);
        await CaptureAdviceAsync(page, $"promoted-{width}-{theme}.png", post);
        await archive.Locator("summary").ClickAsync();
        await Assertions.Expect(archive).ToContainTextAsync("I can make a short walk happen after lunch.");
        await Assertions.Expect(archive.Locator("[data-discussion-reply-to], [data-discussion-reply-edit]")).ToHaveCountAsync(0);
        await CaptureAdviceAsync(page, $"archive-{width}-{theme}.png", post);
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Empty(errors);
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
        await page.Locator("#lmxNotes .is-active .lmx-advice-join").ClickAsync();
        await page.Locator("#lmxNotes [data-discussion-draft] textarea").FillAsync("A useful next step.");
        await page.Locator("#lmxNotes [data-reply-submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-draft]")).ToHaveCountAsync(0);
        Assert.NotNull(submitted);
        Assert.False(submitted!["isAdvice"]!.GetValue<bool>());
        Assert.Equal("higher-advice", submitted["adviceRootId"]!.GetValue<string>());
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
        await Assertions.Expect(dialog.Locator("button[type=submit]")).ToBeDisabledAsync();
        await dialog.Locator(".lmx-advice-continue").ClickAsync();
        await Assertions.Expect(dialog.Locator("#lmxAdviceStoryDate")).ToContainTextAsync("Day 21");
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
        await page.Locator("#lmxNotes .is-active .lmx-advice-join").ClickAsync();
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
        await context.RouteAsync("**/api/longevitymaxxing/advice/withdraw", async route =>
        {
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
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await page.Locator("#lmxNotes [data-advice-withdraw]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).ToContainTextAsync("Advice request withdrawn");
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-story")).Not.ToContainTextAsync(AdviceStory);
        await Assertions.Expect(page.Locator("#lmxNotes .lmx-advice-join")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task AdviceStory_EditSurvivesAConversationRefreshAndSavesAgainstTheCurrentVersion()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = AdviceBrowserState(1);
        var refreshed = AdviceBrowserState(2);
        JsonNode? edited = null;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies", route => FulfillJsonAsync(route, refreshed.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/advice/edit", async route =>
        {
            edited = JsonNode.Parse(route.Request.PostData!);
            foreach (var collection in new[] { refreshed["notes"]!, refreshed["public"]!["notes"]! })
                collection[0]!["advice"]!["story"] = edited!["story"]!.GetValue<string>();
            await FulfillJsonAsync(route, refreshed.ToJsonString());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.Locator("#lmxNotes [data-advice-edit]").ClickAsync();
        var storyEditor = page.Locator("#lmxNotes .lmx-advice-story-editor");
        await storyEditor.Locator("textarea").FillAsync("An unfinished clarification that must survive the handoff.");
        await page.Locator("#lmxNotes .is-active .lmx-advice-join").ClickAsync();
        await page.Locator("#lmxNotes [data-discussion-draft] textarea").FillAsync("One more detail for the conversation.");
        await page.Locator("#lmxNotes [data-reply-submit]").ClickAsync();
        await Assertions.Expect(storyEditor.Locator("textarea")).ToHaveValueAsync("An unfinished clarification that must survive the handoff.");
        await storyEditor.Locator("[type=submit]").ClickAsync();
        await Assertions.Expect(storyEditor).ToHaveCountAsync(0);
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
        await page.Locator("#lmxNotes .lmx-advice-join").ClickAsync();
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
            ["replies"] = new JsonArray(replies.Select(reply => (JsonNode)reply.DeepClone()).ToArray()), ["version"] = stage + 2, ["withdrawn"] = false
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
        if (element != null) await element.ScreenshotAsync(new() { Path = Path.Combine(directory, name) });
        else await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name) });
    }
}
