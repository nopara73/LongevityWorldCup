using Microsoft.Playwright;
using System.Globalization;
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

    [Fact]
    public async Task DiscussionFeed_LoadsBeyondTheOriginalHundredAndRetriesMalformedHistoryWithoutLosingContent()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = LargeDiscussionFeedState(100);
        var history = LargeDiscussionFeedState(102)["notes"]!.AsArray().Skip(100).ToArray();
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
        await older.ClickAsync();
        await Assertions.Expect(older).ToHaveTextAsync("Retry loading posts");
        await Assertions.Expect(articles).ToHaveCountAsync(100);
        Assert.True(await articles.First.EvaluateAsync<bool>("e => e === window.firstFeedArticle"));
        await older.ClickAsync();
        await Assertions.Expect(articles).ToHaveCountAsync(102);
        await Assertions.Expect(page.Locator("#lmxFeedCaughtUp")).ToBeVisibleAsync();
        Assert.Equal(2, requests);
        Assert.Equal("feed-099", (string?)cursor!["participantId"]);
        Assert.Equal(102, (await articles.EvaluateAllAsync<string[]>("items => items.map(item => item.dataset.feedKey)")).Distinct().Count());
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
