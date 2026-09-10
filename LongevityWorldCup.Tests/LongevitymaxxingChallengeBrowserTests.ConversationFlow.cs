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
    public async Task ConversationFlow_QuickEntryResumesDraftAndEscapeDismissesOneLayer(bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState(), dialog);
        var thread = DiscussionThread(page, "p7", 5, dialog ? "#lmxCheckinList" : "#lmxNotes");
        var quick = thread.Locator("[data-discussion-quick-reply]");
        await quick.ClickAsync();
        var textarea = thread.Locator("[data-discussion-reply-composer] textarea");
        await Assertions.Expect(textarea).ToBeFocusedAsync();
        await Assertions.Expect(quick).ToBeHiddenAsync();
        await textarea.FillAsync("Try this with @Ar");
        await Assertions.Expect(thread.Locator(".lmx-mention-options")).ToBeVisibleAsync();
        await textarea.PressAsync("Escape");
        await Assertions.Expect(thread.Locator(".lmx-mention-options")).ToBeHiddenAsync();
        await Assertions.Expect(textarea).ToBeFocusedAsync();
        await textarea.PressAsync("Escape");
        await Assertions.Expect(textarea).ToHaveCountAsync(0);
        await Assertions.Expect(quick).ToBeFocusedAsync();
        await Assertions.Expect(quick).ToContainTextAsync("Resume your reply…");
        if (dialog) await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToBeVisibleAsync();
        await quick.ClickAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("Try this with @Ar");
        await thread.Locator("[data-reply-action='submit']").PressAsync("Escape");
        await Assertions.Expect(quick).ToBeFocusedAsync();
        await quick.ClickAsync();
        await thread.Locator("[data-reply-action='discard']").ClickAsync();
        await Assertions.Expect(quick).ToBeFocusedAsync();
        await Assertions.Expect(quick).ToContainTextAsync("Write a reply…");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConversationFlow_HistoryFoldsWithoutLosingDraftsCachedRepliesOrSharedLinks(bool dialog)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var requests = 0;
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", route => {
            Interlocked.Increment(ref requests);
            return FulfillJsonAsync(route, EarlierPolishReplies());
        });
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState(), dialog);
        var root = dialog ? "#lmxCheckinList" : "#lmxNotes";
        var thread = DiscussionThread(page, "p7", 5, root);
        var earlier = thread.Locator("[data-discussion-replies-page]");
        await earlier.ScrollIntoViewIfNeededAsync();
        var anchor = thread.Locator("[data-discussion-reply-id='r3']");
        var anchorTop = (await anchor.BoundingBoxAsync())!.Y;
        await earlier.ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]:not([hidden])")).ToHaveCountAsync(5);
        Assert.InRange((await anchor.BoundingBoxAsync())!.Y, anchorTop - 3, anchorTop + 3);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        var textarea = thread.Locator("textarea");
        await textarea.FillAsync("Keep my place and draft.");
        await textarea.EvaluateAsync("e => { e.setSelectionRange(5, 13); window.flowDraft = e; }");
        await thread.Locator("[data-discussion-replies-collapse]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]:not([hidden])")).ToHaveCountAsync(3);
        await Assertions.Expect(thread.Locator("[data-discussion-replies-expand]")).ToBeFocusedAsync();
        await Assertions.Expect(textarea).ToHaveValueAsync("Keep my place and draft.");
        Assert.True(await textarea.EvaluateAsync<bool>("e => e === window.flowDraft && e.selectionStart === 5 && e.selectionEnd === 13"));
        await thread.Locator("[data-discussion-replies-expand]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]:not([hidden])")).ToHaveCountAsync(5);
        Assert.Equal(1, requests);
        await thread.Locator("[data-discussion-replies-collapse]").ClickAsync();
        await textarea.PressAsync("Escape");
        // Rendering another page must keep the collapsed choice and cached history.
        if (!dialog)
        {
            await page.Locator("#lmxNotesOlder").ClickAsync();
            await page.Locator("#lmxNotesNewer").ClickAsync();
            await Assertions.Expect(thread.Locator("[data-discussion-reply-id]:not([hidden])")).ToHaveCountAsync(3);
        }
        await page.EvaluateAsync("location.hash = '#discussion/post/p7/5/reply/r1'");
        var linked = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r1']");
        await Assertions.Expect(linked).ToBeFocusedAsync();
        await Assertions.Expect(linked).ToBeVisibleAsync();
        Assert.Equal(1, requests);
        await DiscussionThread(page, "p7", 5).Locator("[data-discussion-quick-reply]").ClickAsync();
        await Assertions.Expect(DiscussionThread(page, "p7", 5).Locator("textarea")).ToHaveValueAsync("Keep my place and draft.");
    }

    [Fact]
    public async Task ConversationFlow_HistoryCannotHideAnActiveEarlierEdit()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var earlier = JsonNode.Parse(EarlierPolishReplies())!;
        earlier["replies"]![0]!["participantId"] = "p1";
        earlier["replies"]![0]!["displayName"] = "Browser Tester";
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies/page", route => FulfillJsonAsync(route, earlier.ToJsonString()));
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState());
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-replies-page]").ClickAsync();
        var reply = thread.Locator("[data-discussion-reply-id='r1']");
        await reply.Locator("[data-discussion-reply-edit]").ClickAsync();
        await reply.Locator("textarea").FillAsync("An unfinished earlier edit.");
        await Assertions.Expect(thread.Locator("[data-discussion-replies-collapse]")).ToBeDisabledAsync();
        await reply.Locator("textarea").PressAsync("Escape");
        await thread.Locator("[data-discussion-replies-collapse]").ClickAsync();
        await Assertions.Expect(reply).ToBeHiddenAsync();
        await thread.Locator("[data-discussion-replies-expand]").ClickAsync();
        await reply.Locator("[data-discussion-reply-edit]").ClickAsync();
        await Assertions.Expect(reply.Locator("textarea")).ToHaveValueAsync("An unfinished earlier edit.");
        await reply.Locator("textarea").PressAsync("Escape");
        await reply.Locator("[data-discussion-reply-to]").ClickAsync();
        await thread.Locator("[data-discussion-replies-collapse]").ClickAsync();
        await thread.Locator("textarea").PressAsync("Escape");
        await Assertions.Expect(thread.Locator("[data-discussion-quick-reply]")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task ConversationFlow_PostLinksAlignTheOpeningOfTallExpandedThreads(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 740 } });
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = notes.AsArray()[0]!;
            post["replies"] = JsonSerializer.SerializeToNode(Enumerable.Range(1, 12).Select(index =>
                Reply($"tall-{index}", "p3", "Bea Builder", "Sharing a little more context about the workout and recovery.", $"2026-07-01T08:{index:D2}:00Z")));
            post["replyCount"] = 12;
        }
        var page = await OpenDiscussionPolishAsync(context, state, "#discussion/post/p7/5/reply/tall-12", signedIn: false);
        var thread = DiscussionThread(page, "p7", 5);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id='tall-12']")).ToBeFocusedAsync();
        await thread.Locator(".lmx-discussion-post-header [data-discussion-link]").First.ClickAsync();
        await Assertions.Expect(thread).ToBeFocusedAsync();
        var bounds = await thread.BoundingBoxAsync();
        Assert.True(bounds!.Height > 740);
        var inset = await thread.EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).scrollMarginTop) + parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop)");
        Assert.InRange(bounds.Y, inset - 1, inset + 1);
        Assert.InRange(bounds.Y, 0, 200);
        await Assertions.Expect(thread.Locator("[data-discussion-quick-reply]")).ToHaveCountAsync(0);
    }
}
