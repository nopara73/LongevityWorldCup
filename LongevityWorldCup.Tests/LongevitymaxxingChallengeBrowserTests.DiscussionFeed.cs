using Microsoft.Playwright;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, ColorScheme.Light, false)]
    [InlineData(390, ColorScheme.Dark, true)]
    [InlineData(1280, ColorScheme.Light, false)]
    public async Task DiscussionFeed_AppendsThreeBatchesThenOffersKeyboardAccessibleHistory(int width, ColorScheme theme, bool reducedMotion)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            HasTouch = width < 500
        });
        var page = await OpenDiscussionFeedAsync(context, LargeDiscussionFeedState(45));
        var articles = page.Locator("#lmxNotes > article");
        await Assertions.Expect(articles).ToHaveCountAsync(10);
        var first = articles.First;
        await first.Locator("[data-discussion-quick-reply]").ClickAsync();
        var textarea = first.Locator("textarea");
        await textarea.FillAsync("Keep this reply while the feed grows.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 14); window.feedDraft = e; }");
        for (var expected = 20; expected <= 30; expected += 10) {
            await page.Locator("#lmxFeedSentinel").ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(articles).ToHaveCountAsync(expected);
            Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.feedDraft && document.activeElement === e && e.selectionStart === 5 && e.selectionEnd === 14"));
        }
        var older = page.Locator("#lmxFeedOlder");
        await older.ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await Assertions.Expect(articles).ToHaveCountAsync(30);
        var box = (await older.BoundingBoxAsync())!;
        Assert.True(box.Width >= 44 && box.Height >= 44);
        await older.FocusAsync();
        await older.PressAsync("Enter");
        await Assertions.Expect(articles).ToHaveCountAsync(40);
        await Assertions.Expect(articles.Nth(30)).ToBeFocusedAsync();
        await older.FocusAsync();
        await older.PressAsync("Enter");
        await Assertions.Expect(articles).ToHaveCountAsync(45);
        await Assertions.Expect(page.Locator("#lmxFeedCaughtUp")).ToBeVisibleAsync();
        await Assertions.Expect(older).ToBeHiddenAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep this reply while the feed grows.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));

        var capture = Environment.GetEnvironmentVariable("LWC_FEED_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(capture)) {
            Directory.CreateDirectory(capture);
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"feed-history-{width}-{theme}.png") });
            await first.Locator("[data-reply-action='close']").ClickAsync();
            await page.Locator("#lmxNotesTitle").ScrollIntoViewIfNeededAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"feed-{width}-{theme}.png") });
        }
    }

    [Fact]
    public async Task DiscussionFeed_StagesNewActivityWithoutMovingTheReaderOrTheirDraft()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(35);
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var articles = page.Locator("#lmxNotes > article");
        var order = await articles.EvaluateAllAsync<string[]>("items => items.map(item => item.dataset.feedKey)");
        var original = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p2']");
        await original.Locator("[data-discussion-reply]").ClickAsync();
        var textarea = original.Locator("textarea");
        await textarea.FillAsync("My reply stays exactly here.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(3, 8); window.feedDraft = e; }");
        var top = await textarea.EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
            var fresh = notes[0]!.DeepClone().AsObject();
            fresh["participantId"] = "new-runner";
            fresh["displayName"] = "New Runner";
            fresh["lastActivityAtUtc"] = "2026-07-03T12:01:00Z";
            fresh["updatedAtUtc"] = "2026-07-03T12:01:00Z";
            notes[0]!["note"] = "An update received while someone is reading.";
            notes.Insert(0, fresh);
        }
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToHaveTextAsync("New posts · 2");
        await Assertions.Expect(original.Locator(":scope > p").First).ToContainTextAsync("Morning walk");
        Assert.Equal(order, await articles.EvaluateAllAsync<string[]>("items => items.map(item => item.dataset.feedKey)"));
        Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.feedDraft && document.activeElement === e && e.selectionStart === 3 && e.selectionEnd === 8"));
        Assert.InRange(Math.Abs(top - await textarea.EvaluateAsync<double>("e => e.getBoundingClientRect().top")), 0, 2);
        var capture = Environment.GetEnvironmentVariable("LWC_FEED_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(capture)) await page.ScreenshotAsync(new() { Path = Path.Combine(capture, "feed-new-posts.png") });
        await page.Locator("#lmxFeedNew").ClickAsync();
        await Assertions.Expect(articles).ToHaveCountAsync(11);
        await Assertions.Expect(articles.First.Locator(".lmx-discussion-post-author strong")).ToHaveTextAsync("New Runner");
        await Assertions.Expect(original.Locator("textarea")).ToHaveValueAsync("My reply stays exactly here.");
        await Assertions.Expect(original.Locator(":scope > p").First).ToHaveTextAsync("An update received while someone is reading.");
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task DiscussionFeed_LoadsBeyondTheOriginalHundredAndRetriesMalformedHistoryWithoutLosingContent(int olderCount)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(100);
        var history = LargeDiscussionFeedState(100 + olderCount)["notes"]!.AsArray().Skip(100).ToArray();
        var requests = 0;
        JsonNode? cursor = null;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/feed", async route => {
            requests++;
            cursor = JsonNode.Parse(route.Request.PostData!)!["before"]!.DeepClone();
            if (requests == 1) await FulfillJsonAsync(route, "{\"threads\":\"invalid\",\"nextCursor\":null}");
            else {
                var threads = new JsonArray(history.Select(note => (JsonNode)new JsonObject { ["note"] = note!.DeepClone(), ["systemPost"] = null }).ToArray());
                await FulfillJsonAsync(route, new JsonObject { ["threads"] = threads, ["nextCursor"] = null }.ToJsonString());
            }
        });
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var articles = page.Locator("#lmxNotes > article");
        await articles.First.EvaluateAsync("e => window.firstFeedArticle = e");
        var older = page.Locator("#lmxFeedOlder");
        for (var expected = 20; expected <= 100; expected += 10) {
            await older.ClickAsync();
            await Assertions.Expect(articles).ToHaveCountAsync(expected);
        }
        await older.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(older).ToHaveTextAsync("Retry loading posts");
        await Assertions.Expect(articles).ToHaveCountAsync(100);
        await Assertions.Expect(older).ToBeFocusedAsync();
        Assert.True(await articles.First.EvaluateAsync<bool>("e => e === window.firstFeedArticle"));
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(articles).ToHaveCountAsync(100 + olderCount);
        await Assertions.Expect(page.Locator("#lmxFeedCaughtUp")).ToBeVisibleAsync();
        if (olderCount > 0) await Assertions.Expect(articles.Nth(100)).ToBeFocusedAsync();
        else await Assertions.Expect(page.Locator("#lmxFeedCaughtUp")).ToBeFocusedAsync();
        Assert.Equal(2, requests);
        Assert.Equal("feed-099", (string?)cursor!["participantId"]);
        Assert.Equal(100 + olderCount, (await articles.EvaluateAllAsync<string[]>("items => items.map(item => item.dataset.feedKey)")).Distinct().Count());
        Assert.True(await articles.First.EvaluateAsync<bool>("e => e === window.firstFeedArticle"));
    }

    [Fact]
    public async Task DiscussionFeed_BackNavigationRestoresLoadedThreadsAndReadingPosition()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await context.RouteAsync("**/feed-away", route => route.FulfillAsync(new() { ContentType = "text/html", Body = "<main>Another page</main>" }));
        var page = await OpenDiscussionFeedAsync(context, LargeDiscussionFeedState(45), automatic: false);
        var articles = page.Locator("#lmxNotes > article");
        for (var expected = 20; expected <= 40; expected += 10) {
            await page.Locator("#lmxFeedOlder").ClickAsync();
            await Assertions.Expect(articles).ToHaveCountAsync(expected);
        }
        var target = articles.Nth(32);
        await target.EvaluateAsync("e => window.scrollTo({top: e.getBoundingClientRect().top + scrollY - 100, behavior: 'instant'})");
        var key = await target.GetAttributeAsync("data-feed-key");
        var top = await target.EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        await page.GotoAsync("/feed-away");
        await page.GoBackAsync();
        await Assertions.Expect(articles).ToHaveCountAsync(40);
        Assert.Equal(key, await articles.Nth(32).GetAttributeAsync("data-feed-key"));
        await page.WaitForFunctionAsync("expected => Math.abs(document.querySelectorAll('#lmxNotes > article')[32].getBoundingClientRect().top - expected) < 4", top);
        await page.ReloadAsync();
        await Assertions.Expect(articles).ToHaveCountAsync(40);
        await page.WaitForFunctionAsync("expected => Math.abs(document.querySelectorAll('#lmxNotes > article')[32].getBoundingClientRect().top - expected) < 4", top);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionFeed_DelayedRefreshCannotOfferAnOlderVersionOfYourPublishedReply(bool editing)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(35);
        if (editing) {
            foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
                notes[0]!["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
                    "owned-reply", "p1", "Browser Tester", "The original reply.", "2026-07-03T11:50:00Z")));
                notes[0]!["replyCount"] = 1;
            }
        }
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var staleState = state.ToJsonString();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/participant", async route => {
            requested.TrySetResult();
            await release.Task;
            await FulfillJsonAsync(route, staleState);
        });
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
                var thread = notes[0]!;
                thread["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
                    (string)payload["replyId"]!, "p1", "Browser Tester", (string)payload["body"]!, "2026-07-03T12:01:01Z")));
                thread["replyCount"] = 1;
                thread["lastActivityAtUtc"] = "2026-07-03T12:01:01Z";
            }
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", async route => {
            var payload = JsonNode.Parse(route.Request.PostData!)!;
            await FulfillJsonAsync(route, JsonSerializer.Serialize(Reply("owned-reply", "p1", "Browser Tester",
                (string)payload["body"]!, "2026-07-03T11:50:00Z", "2026-07-03T12:01:01Z")));
        });
        try {
            await page.Clock.FastForwardAsync(61_000);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var thread = page.Locator("#lmxNotes > article").First;
            await thread.Locator(editing ? "[data-discussion-reply-edit]" : "[data-discussion-quick-reply]").ClickAsync();
            await thread.Locator("textarea").FillAsync("My reply is already published.");
            await thread.Locator(editing ? "[data-reply-edit-submit]" : "[data-reply-submit]").ClickAsync();
            await Assertions.Expect(thread.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("My reply is already published.");
            var response = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/longevitymaxxing/participant", StringComparison.Ordinal));
            release.TrySetResult();
            await response;
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await Assertions.Expect(page.Locator("#lmxFeedNew")).ToBeHiddenAsync();
            await Assertions.Expect(thread.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("My reply is already published.");
        } finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionFeed_DelayedHistoryOnlyContinuesFocusWhileTheReaderWaits(bool readerMovesAway)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/feed", async route => {
            requested.TrySetResult();
            await release.Task;
            var threads = new JsonArray(LargeDiscussionFeedState(102)["notes"]!.AsArray().Skip(100)
                .Select(note => (JsonNode)new JsonObject { ["note"] = note!.DeepClone(), ["systemPost"] = null }).ToArray());
            await FulfillJsonAsync(route, new JsonObject { ["threads"] = threads, ["nextCursor"] = null }.ToJsonString());
        });
        var page = await OpenDiscussionFeedAsync(context, LargeDiscussionFeedState(100), automatic: false);
        var articles = page.Locator("#lmxNotes > article");
        var older = page.Locator("#lmxFeedOlder");
        try {
            for (var expected = 20; expected <= 100; expected += 10) {
                await older.ClickAsync();
                await Assertions.Expect(articles).ToHaveCountAsync(expected);
            }
            await older.ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (readerMovesAway) await page.Mouse.WheelAsync(0, -600);
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            var anchor = await articles.EvaluateAllAsync<string>("items => items.find(e => e.getBoundingClientRect().bottom > 60 && e.getBoundingClientRect().top < innerHeight).dataset.feedKey");
            var reading = page.Locator($"#lmxNotes > article[data-feed-key='{anchor}']");
            var top = await reading.EvaluateAsync<double>("e => e.getBoundingClientRect().top");
            release.TrySetResult();
            await Assertions.Expect(articles).ToHaveCountAsync(102);
            if (readerMovesAway) await Assertions.Expect(articles.Nth(100)).Not.ToBeFocusedAsync();
            else await Assertions.Expect(articles.Nth(100)).ToBeFocusedAsync();
            Assert.InRange(Math.Abs(top - await reading.EvaluateAsync<double>("e => e.getBoundingClientRect().top")), 0, 4);
        } finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscussionFeed_EditingReplyPreservesAcceptedAndPendingUpdates(bool acceptBeforeEditing)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(35);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
            notes[0]!["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
                "owned-reply", "p1", "Browser Tester", "The original reply.", "2026-07-03T11:50:00Z")));
            notes[0]!["replyCount"] = 1;
        }
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
            notes[0]!["note"] = "A fresh post update from the feed.";
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToHaveTextAsync("New posts · 1");
        if (acceptBeforeEditing) await page.Locator("#lmxFeedNew").ClickAsync();
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/edit", route => FulfillJsonAsync(route,
            JsonSerializer.Serialize(Reply("owned-reply", "p1", "Browser Tester", "My edited reply.",
                "2026-07-03T11:50:00Z", "2026-07-03T12:01:01Z"))));
        var thread = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p2']");
        await thread.Locator("[data-discussion-reply-edit]").ClickAsync();
        await thread.Locator("textarea").FillAsync("My edited reply.");
        await thread.Locator("[data-reply-edit-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("My edited reply.");
        if (!acceptBeforeEditing) await page.Locator("#lmxFeedNew").ClickAsync();
        await Assertions.Expect(thread.Locator(":scope > p").First).ToHaveTextAsync("A fresh post update from the feed.");
        await Assertions.Expect(thread.Locator("[data-discussion-reply-body]")).ToHaveTextAsync("My edited reply.");
    }

    [Fact]
    public async Task DiscussionFeed_DeletingAnOlderCachedReplyUpdatesTheThreadOutsideTheInitialState()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(100);
        var history = LargeDiscussionFeedState(102)["notes"]!.AsArray().Skip(100).Select(note => note!.DeepClone()).ToArray();
        foreach (var note in history) {
            note["updatedAtUtc"] = "2026-07-01T10:00:00Z";
            note["lastActivityAtUtc"] = "2026-07-01T10:00:00Z";
            note["date"] = "2026-07-01";
            note["challengeDay"] = 20;
        }
        history[0]["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
            "owned-older-reply", "p1", "Browser Tester", "An older reply to delete.", "2026-07-02T10:00:00Z")));
        history[0]["replyCount"] = 1;
        history[0]["lastActivityAtUtc"] = "2026-07-02T10:00:00Z";
        await context.RouteAsync("**/api/longevitymaxxing/discussion/feed", route => FulfillJsonAsync(route,
            new JsonObject { ["threads"] = new JsonArray(history.Select(note => (JsonNode)new JsonObject {
                ["note"] = note.DeepClone(), ["systemPost"] = null }).ToArray()), ["nextCursor"] = null }.ToJsonString()));
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var articles = page.Locator("#lmxNotes > article");
        for (var expected = 20; expected <= 100; expected += 10) {
            await page.Locator("#lmxFeedOlder").ClickAsync();
            await Assertions.Expect(articles).ToHaveCountAsync(expected);
        }
        await page.Locator("#lmxFeedOlder").ClickAsync();
        await Assertions.Expect(articles).ToHaveCountAsync(102);
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/delete", route => FulfillJsonAsync(route, state.ToJsonString()));
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var older = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='feed-100']");
        await older.Locator("[data-discussion-reply-delete]").ClickAsync();
        await Assertions.Expect(older.Locator("[data-discussion-reply-id]")).ToHaveCountAsync(0);
        await Assertions.Expect(older.Locator(".lmx-discussion-count")).ToHaveTextAsync("0 replies");
        await Assertions.Expect(articles).ToHaveCountAsync(102);
    }

    [Fact]
    public async Task DiscussionFeed_DeletingReplyConsumesItsQueuedUpdate()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(35);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
            notes[0]!["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(
                "owned-reply", "p1", "Browser Tester", "The original reply.", "2026-07-03T11:50:00Z")));
            notes[0]!["replyCount"] = 1;
        }
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
            notes[0]!["replies"]![0]!["body"] = "A queued update to the reply.";
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToHaveTextAsync("New posts · 1");
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies/delete", async route => {
            foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
                notes[0]!["replies"] = new JsonArray();
                notes[0]!["replyCount"] = 0;
            }
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var thread = page.Locator("#lmxNotes > article[data-discussion-post-participant-id='p2']");
        await thread.Locator("[data-discussion-reply-delete]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task DiscussionFeed_RemovingOwnPostAlsoRemovesItsQueuedVersion()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(35);
        var template = state["notes"]![0]!.DeepClone();
        foreach (var day in state["eligibleDays"]!.AsArray()) {
            day!["existing"] = SavedCheckIn("My published note.");
            var own = template.DeepClone();
            own["participantId"] = "p1";
            own["displayName"] = "Browser Tester";
            own["challengeDay"] = day["challengeDay"]!.DeepClone();
            own["note"] = "My published note.";
            foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
                notes.Insert(0, own.DeepClone());
        }
        var page = await OpenDiscussionFeedAsync(context, state, automatic: false);
        var form = page.Locator(".lmx-checkin-card");
        var selectedDay = int.Parse((await form.GetAttributeAsync("data-day"))!, CultureInfo.InvariantCulture);
        foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() }) {
            notes.Single(note => (string?)note!["participantId"] == "p1" && (int?)note["challengeDay"] == selectedDay)!["note"] = "A queued version of my published note.";
            var fresh = template.DeepClone();
            fresh["participantId"] = "new-runner";
            fresh["displayName"] = "New Runner";
            fresh["lastActivityAtUtc"] = "2026-07-03T12:01:00Z";
            notes.Insert(0, fresh);
        }
        await page.Clock.FastForwardAsync(61_000);
        await Assertions.Expect(page.Locator("#lmxFeedNew")).ToHaveTextAsync("New posts · 2");
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route => {
            state["eligibleDays"]!.AsArray().Single(day => (int?)day!["challengeDay"] == selectedDay)!["existing"] = SavedCheckIn("");
            foreach (var notes in new[] { state["notes"]!.AsArray(), state["public"]!["notes"]!.AsArray() })
                notes.Remove(notes.Single(note => (string?)note!["participantId"] == "p1" && (int?)note["challengeDay"] == selectedDay));
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var ownPost = page.Locator($"#lmxNotes > article[data-discussion-post-participant-id='p1'][data-discussion-post-challenge-day='{selectedDay}']");
        await form.Locator("textarea").FillAsync("");
        await form.Locator("button[type='submit']").ClickAsync();
        await Assertions.Expect(ownPost).ToHaveCountAsync(0);
        await page.Locator("#lmxQuoteDialogOk").ClickAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator("#lmxFeedNew").ClickAsync();
        await Assertions.Expect(ownPost).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#lmxNotes > article[data-discussion-post-participant-id='new-runner']")).ToBeVisibleAsync();
    }

    private static JsonObject LargeDiscussionFeedState(int count)
    {
        var state = DiscussionWorkspaceState();
        var template = state["notes"]!.AsArray().First(note => (string?)note!["participantId"] == "p2")!;
        var notes = new JsonArray();
        var now = DateTimeOffset.Parse("2026-07-03T12:00:00Z", CultureInfo.InvariantCulture);
        var remarks = new[] {
            "Morning walk, strength training, and an early night. Keeping it simple this week.",
            "A longer ride today. Anyone else finding it easier to stay consistent with the daily check-in?",
            "Sleep finally back on track. Getting outside first thing has helped.",
            "Meal prep done for the next few days. One less decision to make after training."
        };
        for (var i = 0; i < count; i++) {
            var note = template.DeepClone().AsObject();
            note["participantId"] = i == 0 ? "p2" : $"feed-{i:000}";
            note["displayName"] = i == 0 ? "Ari Able" : new[] { "Bea", "Cam", "Dee", "Eli", "Fox", "Jo" }[(i - 1) % 6];
            note["note"] = remarks[i % remarks.Length];
            note["date"] = "2026-07-03";
            note["updatedAtUtc"] = now.AddMinutes(-i).ToString("o");
            note["lastActivityAtUtc"] = now.AddMinutes(-i).ToString("o");
            note["replyCount"] = 0;
            note["replies"] = new JsonArray();
            note["images"] = new JsonArray();
            notes.Add(note);
        }
        state["notes"] = notes.DeepClone();
        state["public"]!["notes"] = notes;
        state["public"]!["systemDiscussionPosts"] = new JsonArray();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        return state;
    }

    private static async Task<IPage> OpenDiscussionFeedAsync(IBrowserContext context, JsonObject state, bool automatic = true)
    {
        await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        if (!automatic) await context.AddInitScriptAsync("delete window.IntersectionObserver");
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync(new() { Time = "2026-07-03T12:00:00Z" });
        await page.GotoAsync("/longevitymaxxing");
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator("#lmxNotes > article").First.WaitForAsync();
        return page;
    }
}
