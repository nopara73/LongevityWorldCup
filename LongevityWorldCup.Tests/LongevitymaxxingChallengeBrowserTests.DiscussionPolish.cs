using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task DiscussionPolish_TimesLinksAndTooltipsWorkAcrossViews(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600, Locale = "en-US", TimezoneId = "Asia/Singapore"
        });
        var state = DiscussionWorkspaceState();
        foreach (var collection in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var fox = collection.AsArray()[0]!;
            fox["updatedAtUtc"] = "2026-07-01T09:00:00Z";
            fox["note"] = "Try https://example.com/workout_(easy).\nThanks @Ari Able — www.example.org/tips?x=1&y=2!";
            fox["replies"] = JsonSerializer.SerializeToNode(new[]
            {
                Reply("last-year", "p4", "Cam", "An older reply.", "2025-07-01T10:00:00Z"),
                Reply("week", "p2", "Ari Able", "A week ago.", "2026-06-24T10:00:00Z"),
                Reply("days", "p3", "Bea", "Six days ago.", "2026-06-25T10:00:00Z"),
                Reply("hours", "p4", "Cam", "Nearly one day.", "2026-06-30T11:00:00Z"),
                Reply("minutes", "p3", "Bea", "A minute ago.", "2026-07-01T09:59:00Z"),
                Reply("seconds", "p1", "Browser Tester", "See https://example.com/@Ari_Able?a=1&b=2. <script>alert(1)</script>", "2026-07-01T09:59:30Z", "2026-07-01T09:59:45Z")
            });
            fox["replyCount"] = 6;
            fox["lastActivityAtUtc"] = "2026-07-01T09:59:30Z";
        }
        var page = await OpenDiscussionPolishAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        foreach (var root in new[] { "#lmxNotes", "#lmxCheckinList" })
        {
            var thread = DiscussionThread(page, "p7", 5, root);
            await Assertions.Expect(thread.Locator(".lmx-discussion-post-author time")).ToHaveTextAsync("1 hour ago");
            foreach (var (id, label) in new[] { ("last-year", "Jul 1, 2025"), ("week", "Jun 24"), ("days", "6 days ago"), ("hours", "23 hours ago"), ("minutes", "1 minute ago"), ("seconds", "Just now") })
                await Assertions.Expect(thread.Locator($"[data-discussion-reply-id='{id}'] time")).ToHaveTextAsync(label);
            await Assertions.Expect(thread.Locator(":scope > p .lmx-discussion-text-link")).ToHaveCountAsync(2);
            await Assertions.Expect(thread.Locator(":scope > p .lmx-discussion-text-link").First).ToHaveAttributeAsync("href", "https://example.com/workout_(easy)");
            await Assertions.Expect(thread.Locator(":scope > p .lmx-note-mention")).ToHaveTextAsync("@Ari Able");
            Assert.Equal(0, await thread.Locator("script").CountAsync());
        }
        var full = DiscussionThread(page, "p7", 5);
        var timestamp = full.Locator(".lmx-discussion-post-author .lmx-discussion-permalink");
        await timestamp.FocusAsync();
        var tooltip = page.Locator("#lmxDiscussionTimeTooltip");
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await Assertions.Expect(tooltip).ToContainTextAsync("Jul 1, 2026");
        await Assertions.Expect(tooltip).ToContainTextAsync("5:00:00 PM");
        Assert.Contains("GMT+8", await tooltip.InnerTextAsync());
        var tooltipBounds = await tooltip.BoundingBoxAsync();
        Assert.NotNull(tooltipBounds);
        Assert.InRange(tooltipBounds.X, 0, width - tooltipBounds.Width);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tooltip).ToBeHiddenAsync();
        await page.Mouse.MoveAsync(0, 0);
        await timestamp.HoverAsync();
        await Assertions.Expect(tooltip).ToBeVisibleAsync();
        await full.Locator("[data-discussion-reply]").ClickAsync();
        var textarea = full.Locator("textarea");
        await textarea.FillAsync("Keep this draft while timestamps update.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 10); window.discussionDraftNode = e; }");
        await page.Clock.FastForwardAsync(31_000);
        await Assertions.Expect(full.Locator("[data-discussion-reply-id='seconds'] time")).ToHaveTextAsync("1 minute ago");
        Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.discussionDraftNode && document.activeElement === e && e.selectionStart === 5 && e.selectionEnd === 10"));
        await full.Locator("[data-reply-action='close']").ClickAsync();
        await full.EvaluateAsync("e => window.scrollTo({ top: e.getBoundingClientRect().top + window.scrollY - 90, behavior: 'instant' })");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        var captureDirectory = Environment.GetEnvironmentVariable("LWC_DISCUSSION_CAPTURE_DIR");
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            await full.ScreenshotAsync(new() { Path = Path.Combine(captureDirectory, $"discussion-{width}-{theme}.png") });
        }
    }

    [Fact]
    public async Task DiscussionPolish_PermalinksRevealEarlierRepliesAndSurviveReloadAndHistory()
    {
        await using var context = await NewContextAsync(Browser, App, new() { Locale = "en-US" });
        var pages = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", async route =>
        {
            pages++;
            await FulfillJsonAsync(route, EarlierPolishReplies());
        });
        var page = await OpenDiscussionPolishAsync(context, hash: "#discussion/post/p7/5/reply/r2", signedIn: false);
        var reply = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r2']");
        await Assertions.Expect(reply).ToBeFocusedAsync();
        Assert.Equal(1, pages);
        await page.ReloadAsync();
        await Assertions.Expect(reply).ToBeFocusedAsync();
        Assert.Equal(2, pages);
        var newer = DiscussionThread(page, "p2", 22).Locator(".lmx-discussion-count");
        await newer.ClickAsync();
        await Assertions.Expect(page.Locator("#lmxNotes [data-discussion-reply-id='r5']")).ToBeFocusedAsync();
        await page.GoBackAsync();
        await Assertions.Expect(reply).ToBeFocusedAsync();
        Assert.DoesNotContain("token", await reply.Locator(".lmx-discussion-permalink").GetAttributeAsync("href"));
        await page.GotoAsync("/longevitymaxxing#discussion/post/p6/19");
        await Assertions.Expect(DiscussionThread(page, "p6", 19)).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxNotesDayLabel")).ToContainTextAsync("6");
    }

    [Fact]
    public async Task DiscussionPolish_ReplyCountsKeepDraftsAndEarlierPagingOffersRetryWithoutLosingPosition()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var calls = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", async route =>
        {
            if (++calls == 1) await route.FulfillAsync(new() { Status = 503 });
            else await FulfillJsonAsync(route, EarlierPolishReplies());
        });
        var page = await OpenDiscussionPolishAsync(context);
        var fox = DiscussionThread(page, "p7", 5);
        await fox.Locator("[data-discussion-reply]").ClickAsync();
        await fox.Locator("textarea").FillAsync("Keep me through reply navigation.");
        await fox.Locator(".lmx-discussion-count").ClickAsync();
        var earlier = fox.Locator("[data-discussion-replies-page]");
        await Assertions.Expect(earlier).ToBeFocusedAsync();
        Assert.Equal("Keep me through reply navigation.", await fox.Locator("textarea").InputValueAsync());
        await earlier.ClickAsync();
        await Assertions.Expect(earlier).ToContainTextAsync("Retry");
        await Assertions.Expect(fox.Locator(".lmx-discussion-page-status")).ToContainTextAsync("Couldn’t load earlier replies");
        var existing = fox.Locator("[data-discussion-reply-id='r3']");
        var top = await existing.EvaluateAsync<double>("e => e.getBoundingClientRect().top");
        await earlier.ClickAsync();
        await Assertions.Expect(earlier).ToHaveCountAsync(0);
        await Assertions.Expect(existing).ToBeFocusedAsync();
        Assert.InRange(Math.Abs(await existing.EvaluateAsync<double>("e => e.getBoundingClientRect().top") - top), 0, 3);
        Assert.Equal("Keep me through reply navigation.", await fox.Locator("textarea").InputValueAsync());
        var noReplies = DiscussionThread(page, "p3", 21);
        await noReplies.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(noReplies.Locator("textarea")).ToBeFocusedAsync();
        await noReplies.Locator("textarea").FillAsync("Keep the first reply open.");
        await noReplies.Locator(".lmx-discussion-count").ClickAsync();
        await Assertions.Expect(noReplies.Locator("textarea")).ToBeFocusedAsync();
        await Assertions.Expect(noReplies.Locator("textarea")).ToHaveValueAsync("Keep the first reply open.");
    }

    [Fact]
    public async Task DiscussionPolish_LoadsArchivedThreadsAndReportsMissingReplies()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var archived = Note("archived", "Archived Author", 10, "2026-06-20", "An older public thread.",
            replies: [Reply("archived-reply", "p3", "Bea", "Still reachable.", "2026-06-20T12:00:00Z")]);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/thread?*", route =>
            FulfillJsonAsync(route, JsonSerializer.Serialize(new { note = archived, systemPost = (object?)null })));
        var page = await OpenDiscussionPolishAsync(context, hash: "#discussion/post/archived/10/reply/archived-reply", signedIn: false);
        var thread = DiscussionThread(page, "archived", 10);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id='archived-reply']")).ToBeFocusedAsync();
        await page.GotoAsync("/longevitymaxxing#discussion/post/archived/10/reply/deleted");
        await Assertions.Expect(page.Locator("#lmxDiscussionLinkStatus")).ToHaveTextAsync("That reply is no longer available.");
    }

    [Fact]
    public async Task DiscussionPolish_ArchivedReplyOwnershipAndDeletionStayCurrent()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var archived = Note("archived", "Archived Author", 10, "2026-06-20", "An older public thread.",
            replies: [Reply("owned-archived", "p1", "Browser Tester", "My old reply.", "2026-06-20T12:00:00Z")]);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/thread?*", route =>
            FulfillJsonAsync(route, JsonSerializer.Serialize(new { note = archived, systemPost = (object?)null })));
        var state = DiscussionWorkspaceState();
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/delete", route => FulfillJsonAsync(route, state.ToJsonString()));
        var page = await OpenDiscussionPolishAsync(context, state, "#discussion/post/archived/10/reply/owned-archived");
        var thread = DiscussionThread(page, "archived", 10);
        await thread.Locator("[data-discussion-reply-edit]").ClickAsync();
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("My old reply.");
        await thread.Locator("[data-reply-action='close']").ClickAsync();
        page.Dialog += (_, dialog) => dialog.AcceptAsync();
        await thread.Locator("[data-discussion-reply-delete]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]")).ToHaveCountAsync(0);
        await Assertions.Expect(thread.Locator(".lmx-discussion-count")).ToHaveTextAsync("0 replies");
    }

    [Fact]
    public async Task DiscussionPolish_PublicWelcomeAndEmptyReplyLinksOpenTheirPosts()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = DiscussionWorkspaceState();
        state["public"]!["systemDiscussionPosts"] = JsonSerializer.SerializeToNode(new[] { new
        {
            id = "welcome", kind = "participant-joined", participantId = "p8", displayName = "New Member",
            date = "2026-07-01", occurredAtUtc = "2026-07-01T09:00:00Z", lastActivityAtUtc = "2026-07-01T09:00:00Z",
            replyCount = 0, replies = Array.Empty<object>()
        } });
        var page = await OpenDiscussionPolishAsync(context, state, "#discussion/system/welcome/replies", signedIn: false);
        var welcome = page.Locator("#lmxNotes [data-discussion-system-post-id='welcome']");
        await Assertions.Expect(welcome).ToBeFocusedAsync();
        await Assertions.Expect(welcome.Locator("time")).ToHaveTextAsync("1 hour ago");
        await page.GotoAsync("/longevitymaxxing#discussion/post/p3/21/replies");
        await Assertions.Expect(DiscussionThread(page, "p3", 21)).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxDiscussionLinkStatus")).ToHaveCountAsync(0);
    }

    private static string EarlierPolishReplies() => JsonSerializer.Serialize(new
    {
        replies = new[] { Reply("r1", "p3", "Bea", "This helped me rethink breakfast.", "2026-06-29T11:00:00Z"),
            Reply("r2", "p4", "Cam", "Trying the same approach tomorrow.", "2026-06-29T12:00:00Z") },
        totalCount = 5, latestReplyIds = new[] { "r3", "r4", "r6" }, remainingEarlierReplyCount = 0,
        hasEarlier = false, nextBeforeCreatedAtUtc = (string?)null, nextBeforeReplyId = (string?)null
    });

    private static async Task<IPage> OpenDiscussionPolishAsync(IBrowserContext context, JsonObject? state = null, string hash = "", bool signedIn = true)
    {
        state ??= DiscussionWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        if (signedIn) await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync(new() { Time = "2026-07-01T10:00:00Z" });
        await page.GotoAsync("/longevitymaxxing" + hash);
        await page.Locator("#lmxNotes article").First.WaitForAsync();
        return page;
    }
}
