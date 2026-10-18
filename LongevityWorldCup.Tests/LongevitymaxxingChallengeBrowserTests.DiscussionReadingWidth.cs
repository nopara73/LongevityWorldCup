using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, false)]
    [InlineData(360, true)]
    [InlineData(390, false)]
    [InlineData(481, false)]
    [InlineData(600, true)]
    public async Task DiscussionReplies_ReadUnderTheirAuthorRowOnPhones(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true,
            includeJoinDiscussionPost: true, joinDiscussionHasReply: true))!.AsObject();
        await context.RouteAsync("**/api/longevitymaxxing/state", r => FulfillJsonAsync(r, state["public"]!.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/participant", r => FulfillJsonAsync(r, state.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        var reply = page.Locator("#lmxNotes .lmx-discussion-reply-item").First;
        await Assertions.Expect(reply).ToBeVisibleAsync();

        var layout = await reply.EvaluateAsync<double[]>(
            """
            item => {
                const avatar = item.querySelector('.lmx-discussion-reply-meta .lmx-discussion-author-avatar').getBoundingClientRect();
                const text = item.querySelector(':scope > p, p').getBoundingClientRect();
                return [avatar.left, text.left, text.width, document.documentElement.scrollWidth, innerWidth];
            }
            """);
        Assert.True(Math.Abs(layout[1] - layout[0]) <= 2,
            $"{width}px: a reply's text should start under its author's avatar ({layout[1]:F0}px vs {layout[0]:F0}px), not hang beside it.");
        Assert.True(layout[3] <= layout[4] + 1, $"{width}px: the page scrolls sideways.");
        if (width > 480)
        {
            var rail = await reply.Locator("..").Locator("..").EvaluateAsync<double[]>("""
                list => {
                    const style = getComputedStyle(list);
                    return [parseFloat(style.marginLeft), parseFloat(style.paddingLeft)];
                }
                """);
            Assert.Equal(8, rail[0]);
            Assert.Equal(12, rail[1]);
        }
    }

    [Theory]
    [InlineData(320, 200)]
    [InlineData(390, 100)]
    [InlineData(390, 200)]
    [InlineData(600, 200)]
    [InlineData(601, 100)]
    [InlineData(844, 100)]
    [InlineData(1440, 100)]
    public async Task DiscussionBodies_PhotosEditorsAndErrorsFollowTheirOwnAuthor(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = width == 844 ? 390 : 900 },
            ColorScheme = ColorScheme.Dark, HasTouch = width <= 600,
            ReducedMotion = ReducedMotion.Reduce
        });
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var post = notes.AsArray()[0]!;
            post["note"] = "A longer post with https://example.com/" + new string('a', 100);
            post["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/assets/content-images/play-athlete-placeholder.jpg") });
            var owned = post["replies"]!.AsArray().Single(reply => (string?)reply!["id"] == "r6")!;
            owned["images"] = post["images"]!.DeepClone();
            owned["replyToId"] = "deleted-parent";
            owned["replyTo"] = null;
        }
        await context.RouteAsync("**/api/longevitymaxxing/discussion/replies**", route =>
            route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{}" }));
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.EvaluateAsync("document.fonts.ready");
        var thread = DiscussionThread(page, "p7", 5);
        var ownedReply = thread.Locator("[data-discussion-reply-id='r6']");
        await ownedReply.Locator("[data-discussion-reply-edit]").ClickAsync();
        var editor = ownedReply.Locator("textarea");
        await editor.FillAsync("Keep this unpublished edit.");
        await ownedReply.Locator("[data-reply-edit-submit]").ClickAsync();
        await Assertions.Expect(ownedReply.Locator("[data-reply-edit-submit]")).ToHaveTextAsync("Retry");
        await Assertions.Expect(editor).ToHaveValueAsync("Keep this unpublished edit.");
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        var composer = thread.Locator(":scope > .lmx-discussion-reply-slot textarea");
        await composer.FillAsync("Keep my separate reply draft.");
        await thread.Locator("[data-reply-submit]").ClickAsync();
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToHaveTextAsync("Retry");

        var issues = await thread.EvaluateAsync<string[]>("""
            thread => {
                const issues = [];
                const root = parseFloat(getComputedStyle(document.documentElement).fontSize);
                const check = (item, header, selector, desktopIndent) => {
                    const avatar = item.querySelector(header + ' .lmx-discussion-author-avatar').getBoundingClientRect();
                    const expectedLeft = avatar.left + (innerWidth <= 600 ? 0 : desktopIndent * root);
                    for (const body of item.querySelectorAll(selector)) {
                        if (!body.getClientRects().length) continue;
                        const rect = body.getBoundingClientRect();
                        if (!rect.width || !rect.height) continue;
                        if (Math.abs(rect.left - expectedLeft) > 2) issues.push(`${body.className}: body left ${rect.left}, author edge ${expectedLeft}`);
                        if (body.scrollWidth > body.clientWidth + 1) issues.push(`${body.className}: content overflow`);
                    }
                };
                check(thread, '.lmx-discussion-post-header', ':scope > p, :scope > .lmx-note-photo-grid, :scope > .lmx-discussion-reply-slot', 3);
                for (const item of thread.querySelectorAll('[data-discussion-reply-id]'))
                {
                    check(item, '.lmx-discussion-reply-meta', ':scope > p, :scope > .lmx-reply-photos, :scope > .lmx-discussion-reply-editor-slot, :scope > .lmx-discussion-reply-status', 2.5);
                    if (item.scrollWidth > item.clientWidth + 1) issues.push('reply content overflow');
                }
                if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflow');
                return issues;
            }
            """);
        Assert.True(issues.Length == 0, $"{width}px at {textPercent}% text: {string.Join("; ", issues)}");
        await thread.Locator("[data-reply-action='close']").Last.ClickAsync();
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        await Assertions.Expect(composer).ToHaveValueAsync("Keep my separate reply draft.");
        await Assertions.Expect(composer).ToBeFocusedAsync();
        await Assertions.Expect(composer).ToBeInViewportAsync();
    }
}
