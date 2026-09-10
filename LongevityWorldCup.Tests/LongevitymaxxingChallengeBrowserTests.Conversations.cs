using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationActions_ReplyMentionsPreserveDraftSelectionAndRetryIdentity(bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        var page = await OpenDiscussionWorkspaceAsync(context, state, dialog);
        var thread = DiscussionThread(page, "p7", 5, "#lmxNotes");
        var ari = thread.Locator("[data-discussion-reply-id='r4'] [data-discussion-reply-to]");
        var dee = thread.Locator("[data-discussion-reply-id='r3'] [data-discussion-reply-to]");
        await ari.ClickAsync();
        var textarea = thread.Locator("[data-discussion-reply-composer] textarea");
        await Assertions.Expect(textarea).ToBeFocusedAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("@Ari Able ");
        await textarea.FillAsync("@Ari Able I’ll try that tomorrow.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(10, 14); window.conversationDraft = e; }");
        await dee.ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("@Dee @Ari Able I’ll try that tomorrow.");
        Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.conversationDraft && e.selectionStart === 15 && e.selectionEnd === 19"));
        await ari.ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("@Dee @Ari Able I’ll try that tomorrow.");
        await thread.Locator("[data-reply-action='close']").ClickAsync();
        await Assertions.Expect(ari).ToBeFocusedAsync();
        await ari.ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("@Dee @Ari Able I’ll try that tomorrow.");

        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var payloads = new List<JsonObject>();
        await page.RouteAsync("**/api/longevitymaxxing/discussion/replies", async route =>
        {
            var payload = JsonNode.Parse(route.Request.PostData!)!.AsObject();
            payloads.Add(payload);
            if (payloads.Count == 1)
            {
                requested.TrySetResult();
                await release.Task;
                await route.FulfillAsync(new() { Status = 503 });
            }
            else
            {
                foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
                {
                    var post = notes.AsArray()[0]!;
                    post["replies"]!.AsArray().Add(JsonSerializer.SerializeToNode(Reply(payload["replyId"]!.GetValue<string>(),
                        "p1", "Browser Tester", payload["body"]!.GetValue<string>(), "2026-07-01T10:00:00Z")));
                    post["replyCount"] = 6;
                }
                await FulfillJsonAsync(route, state.ToJsonString());
            }
        });
        try
        {
            await thread.Locator("[data-reply-submit]").ClickAsync();
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await dee.ClickAsync();
            await Assertions.Expect(textarea).ToHaveValueAsync("@Dee @Ari Able I’ll try that tomorrow.");
            release.TrySetResult();
            await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");
            await thread.Locator("[data-reply-submit]").ClickAsync();
            await Assertions.Expect(thread.Locator(".lmx-discussion-feedback")).ToHaveTextAsync("Reply posted.");
            Assert.Equal(2, payloads.Count);
            Assert.Equal(payloads[0]["replyId"]!.GetValue<string>(), payloads[1]["replyId"]!.GetValue<string>());
            Assert.Equal("p7", payloads[1]["postParticipantId"]!.GetValue<string>());
            Assert.Equal(5, payloads[1]["challengeDay"]!.GetValue<int>());
            await Assertions.Expect(thread.Locator("[data-discussion-reply-body]").Last).ToHaveTextAsync("@Dee @Ari Able I’ll try that tomorrow.");
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("capacity")]
    [InlineData("mentions")]
    [InlineData("ambiguous")]
    public async Task ConversationActions_MentionPrefillsRespectExistingLimits(string limit)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var state = ConversationWorkspaceState();
        if (limit == "ambiguous")
        {
            var duplicate = state["public"]!["leaderboard"]!.AsArray()[1]!.DeepClone();
            duplicate["participantId"] = "another-ari";
            state["public"]!["leaderboard"]!.AsArray().Add(duplicate);
        }
        var page = await OpenDiscussionPolishAsync(context, state);
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-reply-id='r6'] [data-discussion-reply-to]").ClickAsync();
        var textarea = thread.Locator("textarea");
        await Assertions.Expect(textarea).ToHaveValueAsync("");
        var body = limit == "capacity" ? new string('a', 240)
            : limit == "mentions" ? "@Bea Builder @Cam @Dee @Eli @Fox Thanks everyone." : "Keep my draft.";
        await textarea.FillAsync(body);
        await thread.Locator("[data-discussion-reply-id='r4'] [data-discussion-reply-to]").ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync(body);
        await Assertions.Expect(textarea).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-draft] .lmx-status")).ToContainTextAsync(
            limit == "capacity" ? "Make room" : limit == "mentions" ? "Five-participant" : "unique @mention");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationActions_CopyLinksHaveFeedbackAndSelectableFallback(bool fail)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync($$"""
            window.copiedDiscussionLinks = [];
            window.failDiscussionCopy = {{fail.ToString().ToLowerInvariant()}};
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async value => {
                window.copiedDiscussionLinks.push(value);
                if (window.failDiscussionCopy) throw new Error('Denied');
            } } });
            """);
        var page = await OpenDiscussionWorkspaceAsync(context, dialog: true);
        var thread = DiscussionThread(page, "p7", 5);
        foreach (var (scope, suffix) in new[] { (thread, ""), (thread.Locator("[data-discussion-reply-id='r4']"), "/reply/r4") })
        {
            var button = scope.Locator("[data-discussion-copy-link]").First;
            await button.ClickAsync();
            var expected = new Uri(new Uri(page.Url), "/longevitymaxxing#discussion/post/p7/5" + suffix).AbsoluteUri;
            Assert.Equal(expected, await page.EvaluateAsync<string>("window.copiedDiscussionLinks.at(-1)"));
            if (fail)
            {
                var input = scope.Locator(":scope > .lmx-discussion-copy-feedback input");
                await Assertions.Expect(input).ToHaveValueAsync(expected);
                await Assertions.Expect(input).ToBeFocusedAsync();
                Assert.True(await input.EvaluateAsync<bool>("e => e.selectionStart === 0 && e.selectionEnd === e.value.length"));
                await page.EvaluateAsync("window.failDiscussionCopy = false");
                await button.ClickAsync();
                await Assertions.Expect(input).ToHaveCountAsync(0);
                await page.EvaluateAsync("window.failDiscussionCopy = true");
            }
            await Assertions.Expect(button.Locator(".fa-check")).ToHaveCountAsync(1);
            await Assertions.Expect(scope.Locator(":scope > .lmx-discussion-copy-feedback")).ToHaveTextAsync("Link copied.");
        }
    }

    [Theory]
    [InlineData(1280, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(320, ColorScheme.Light)]
    public async Task ConversationLayout_FullPostsAndThreadActionsFitTheDiscussionPanel(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600, Locale = "en-US"
        });
        var state = ConversationWorkspaceState();
        const string opening = "Today’s baseline: 500m row, 40 air squats, 30 sit-ups, 20 pushups and 10 pull-ups.\nKeeping the pace comfortable and tracking how recovery feels tomorrow.\nWhat’s your favourite alternative when a rowing machine isn’t available?";
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = notes.AsArray()[0]!;
            post["note"] = opening;
            post["updatedAtUtc"] = "2026-07-01T07:00:00Z";
            post["replies"] = JsonSerializer.SerializeToNode(new[] {
                Reply("r3", "p2", "Ari Able", "I use inverted rows with straps. Easy to adjust the difficulty.", "2026-07-01T08:00:00Z"),
                Reply("r4", "p7", "Fox", "@Ari Able Great idea — I’ll give that a try next session.", "2026-07-01T09:00:00Z"),
                Reply("r6", "p1", "Browser Tester", "A steady pace makes all the difference for me.", "2026-07-01T09:45:00Z")
            });
            post["lastActivityAtUtc"] = "2026-07-01T09:45:00Z";
        }
        var page = await OpenDiscussionPolishAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        foreach (var root in new[] { "#lmxNotes" })
        {
            var thread = DiscussionThread(page, "p7", 5, root);
            var body = thread.Locator(":scope > p");
            await Assertions.Expect(body).ToHaveTextAsync(opening);
            Assert.True(await body.EvaluateAsync<bool>("e => e.scrollHeight <= e.clientHeight + 1 && getComputedStyle(e).webkitLineClamp === 'none'"));
            await Assertions.Expect(thread.Locator("[data-discussion-reply-id='r4'] .lmx-discussion-author-marker")).ToHaveTextAsync("Author");
            Assert.True(await thread.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth"));
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            var capture = Environment.GetEnvironmentVariable("LWC_CONVERSATION_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(capture))
            {
                Directory.CreateDirectory(capture);
                var bounds = await thread.BoundingBoxAsync();
                await page.SetViewportSizeAsync(width, Math.Max(900, (int)Math.Ceiling(bounds!.Height) + 200));
                await thread.EvaluateAsync("e => window.scrollTo({ top: e.getBoundingClientRect().top + window.scrollY - 90, behavior: 'instant' })");
                await thread.ScreenshotAsync(new() { Path = Path.Combine(capture, $"conversation-{width}-{theme}-{root[4..]}.png") });
                await page.SetViewportSizeAsync(width, 900);
            }
            await thread.Locator("[data-discussion-reply-id='r3'] [data-discussion-reply-to]").ClickAsync();
            await Assertions.Expect(thread.Locator("textarea")).ToBeFocusedAsync();
            await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync("@Ari Able ");
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
            var composer = thread.Locator("[data-discussion-draft]");
            await thread.Locator("textarea").FillAsync(new string('x', 240));
            Assert.True(await composer.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth"));
            Assert.True(await composer.EvaluateAsync<bool>("e => e.querySelector('[data-reply-count]').getBoundingClientRect().right + 4 <= e.querySelector('[data-reply-action=discard]').getBoundingClientRect().left"));
            if (!string.IsNullOrEmpty(capture))
            {
                await thread.Locator("textarea").FillAsync("@Ari Able I’ll give that a try next session.");
                await composer.ScrollIntoViewIfNeededAsync();
                await composer.ScreenshotAsync(new() { Path = Path.Combine(capture, $"composer-{width}-{theme}-{root[4..]}.png") });
            }
            await thread.Locator("[data-reply-action='discard']").ClickAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationLinks_DelayedLookupOnlyYieldsFocusAfterUserInput(bool interrupted)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/discussion/thread?*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await FulfillJsonAsync(route, JsonSerializer.Serialize(new {
                note = Note("archived", "Archived Author", 10, "2026-06-20", "An older thread."), systemPost = (object?)null
            }));
        });
        try
        {
            var page = await OpenDiscussionPolishAsync(context, hash: "#discussion/post/archived/10", signedIn: false);
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (interrupted)
                await page.WheelAndWaitForInputAsync(0, 300);
            release.TrySetResult();
            var thread = DiscussionThread(page, "archived", 10);
            await Assertions.Expect(thread).ToBeAttachedAsync();
            await Assertions.Expect(page.Locator("#lmxDiscussionLinkStatus")).ToHaveCountAsync(0);
            if (interrupted)
            {
                await Assertions.Expect(thread).Not.ToBeFocusedAsync();
                Assert.False(await thread.EvaluateAsync<bool>("e => e.classList.contains('lmx-discussion-target')"));
            }
            else
            {
                await Assertions.Expect(thread).ToBeFocusedAsync();
                Assert.True(await thread.EvaluateAsync<bool>("e => e.classList.contains('lmx-discussion-target')"));
            }
        }
        finally { release.TrySetResult(); }
    }

    private static JsonObject ConversationWorkspaceState() => JsonSerializer.SerializeToNode(BuildParticipantState(
        includeMentionParticipants: true, includeDiscussionNotesWithMentionParticipants: true,
        includeDiscussionIdentityParticipants: true, discussionReplySnapshot: DiscussionReplySnapshot.ContinuousAfterReply))!.AsObject();
}
