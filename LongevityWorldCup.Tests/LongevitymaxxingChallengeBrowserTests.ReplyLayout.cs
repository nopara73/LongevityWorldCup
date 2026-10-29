using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, ColorScheme.Light)]
    [InlineData(320, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(414, ColorScheme.Light)]
    [InlineData(414, ColorScheme.Dark)]
    public async Task DiscussionReplies_EnlargedTextKeepsNamesAndReplyPromptReadable(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme
        });
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
            notes.AsArray()[0]!["displayName"] = "Klaus Townsend";
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("document.fonts.ready");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        var thread = DiscussionThread(page, "p7", 5);
        const string measureBrokenWords = """
            thread => {
                const broken = [];
                const labels = thread.querySelectorAll('.lmx-discussion-author-identity strong, [data-discussion-quick-label], .lmx-discussion-reply-owner-actions button');
                for (const label of labels) {
                    if (!label.getClientRects().length) continue;
                    const text = label.firstChild;
                    if (text?.nodeType !== Node.TEXT_NODE) continue;
                    for (const word of text.textContent.matchAll(/\S+/g)) {
                        const range = document.createRange();
                        range.setStart(text, word.index);
                        range.setEnd(text, word.index + word[0].length);
                        if (range.getClientRects().length > 1) broken.push(word[0]);
                    }
                }
                return broken;
            }
            """;
        var fragments = await thread.EvaluateAsync<string[]>(measureBrokenWords);
        Assert.Empty(fragments);
        if (width >= 390)
        {
            foreach (var contentWidth in new[] { 279, 280, 281, 300, 281, 280, 279 })
            {
                await thread.EvaluateAsync("(post, width) => { post.style.boxSizing = 'content-box'; post.style.width = `${width}px`; post.style.maxWidth = 'none'; }", contentWidth);
                await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
                fragments = await thread.EvaluateAsync<string[]>(measureBrokenWords);
                Assert.True(fragments.Length == 0, $"{width}px viewport / {contentWidth}px post: {string.Join(", ", fragments)}");
            }
            await thread.EvaluateAsync("post => { post.style.removeProperty('box-sizing'); post.style.removeProperty('width'); post.style.removeProperty('max-width'); }");
        }
        Assert.True(await thread.EvaluateAsync<bool>("thread => thread.scrollWidth <= thread.clientWidth"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
    }

    [Theory]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task DiscussionReplies_CopyFallbackFollowsResponsiveActions(int initialWidth, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = initialWidth, Height = 900 }, ColorScheme = theme
        });
        await context.AddInitScriptAsync("""
            Object.defineProperty(navigator, 'clipboard', { value: { writeText: async () => { throw new Error('Denied'); } } });
            """);
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var reply = notes.AsArray()[0]!["replies"]!.AsArray().Single(reply => reply!["id"]!.GetValue<string>() == "r6")!;
            reply["body"] = string.Join(" ", Enumerable.Repeat("A longer reply about training and recovery.", 40));
        }
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var item = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r6']");
        await item.Locator("[data-discussion-copy-link]").ClickAsync();
        var fallback = item.Locator(":scope > .lmx-discussion-copy-feedback input");
        await Assertions.Expect(fallback).ToBeFocusedAsync();
        foreach (var width in new[] { initialWidth, initialWidth <= 480 ? 1280 : 390, initialWidth })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.WaitForFunctionAsync("""
                () => {
                    const item = document.querySelector('#lmxNotes [data-discussion-reply-id="r6"]');
                    const actions = item?.querySelector('.lmx-discussion-reply-footer');
                    return actions && (actions.parentElement === item) === (innerWidth <= 480);
                }
                """);
            Assert.True(await fallback.EvaluateAsync<bool>("""
                input => {
                    const feedback = input.parentElement;
                    const anchor = feedback.previousElementSibling;
                    return anchor.matches(innerWidth <= 480 ? '.lmx-discussion-reply-footer' : '.lmx-discussion-reply-meta');
                }
                """), $"{width}px: the selectable link must follow the copy action's row.");
            await Assertions.Expect(fallback).ToBeFocusedAsync();
            await Assertions.Expect(fallback).ToBeInViewportAsync();
            Assert.True(await fallback.EvaluateAsync<bool>("input => input.selectionStart === 0 && input.selectionEnd === input.value.length"));
        }
    }

    [Fact]
    public async Task DiscussionReplies_ResizePreservesTheFocusedActionAndUnpublishedEdit()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 900 }
        });
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState());
        var reply = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r6']");
        await reply.Locator("[data-discussion-reply-edit]").ClickAsync();
        var draft = reply.Locator("textarea");
        await draft.FillAsync("Keep this unfinished reply edit.");
        var copy = reply.Locator("[data-discussion-copy-link]");
        await copy.FocusAsync();
        await copy.EvaluateAsync("button => window.responsiveReplyCopy = button");
        foreach (var width in new[] { 480, 481, 320, 1280 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await page.WaitForFunctionAsync("""
                () => {
                    const actions = window.responsiveReplyCopy?.parentElement;
                    return actions && (actions.parentElement.matches('[data-discussion-reply-id]') === (innerWidth <= 480));
                }
                """);
            await Assertions.Expect(copy).ToBeFocusedAsync();
            Assert.True(await copy.EvaluateAsync<bool>("button => button === window.responsiveReplyCopy"));
            await Assertions.Expect(draft).ToHaveValueAsync("Keep this unfinished reply edit.");
        }
    }

    [Theory]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(1280, ColorScheme.Light)]
    public async Task DiscussionReplies_KeyboardOrderFollowsTheVisibleContentAcrossResize(int initialWidth, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = initialWidth, Height = 900 }, ColorScheme = theme
        });
        var state = ConversationWorkspaceState();
        foreach (var notes in new[] { state["notes"]!, state["public"]!["notes"]! })
        {
            var replies = notes.AsArray()[0]!["replies"]!.AsArray();
            var owned = replies.Single(reply => reply!["id"]!.GetValue<string>() == "r6")!;
            owned["body"] = "@Ari Able This photo helped.";
            owned["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/assets/content-images/play-athlete-placeholder.jpg") });
        }
        var page = await OpenDiscussionWorkspaceAsync(context, state);
        var reply = DiscussionThread(page, "p7", 5).Locator("[data-discussion-reply-id='r6']");
        var mention = reply.Locator("[data-discussion-reply-body] a");
        var photo = reply.Locator(".lmx-reply-photos button");
        var actions = reply.Locator(".lmx-discussion-reply-footer");
        await Assertions.Expect(mention).ToBeVisibleAsync();
        await Assertions.Expect(photo).ToBeVisibleAsync();

        foreach (var width in new[] { initialWidth, initialWidth <= 480 ? 1280 : 390, initialWidth })
        {
            await page.SetViewportSizeAsync(width, 900);
            await Assertions.Expect(actions).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("""
                () => {
                    const reply = document.querySelector('#lmxNotes [data-discussion-reply-id="r6"]');
                    const actions = reply?.querySelector('.lmx-discussion-reply-footer');
                    const photos = reply?.querySelector('.lmx-reply-photos');
                    return actions && photos && !!(photos.compareDocumentPosition(actions) & Node.DOCUMENT_POSITION_FOLLOWING) === (innerWidth <= 480);
                }
                """);
            Assert.True(await reply.EvaluateAsync<bool>("""
                reply => {
                    const actions = reply.querySelector('.lmx-discussion-reply-footer');
                    const photos = reply.querySelector('.lmx-reply-photos');
                    const followsPhotos = !!(photos.compareDocumentPosition(actions) & Node.DOCUMENT_POSITION_FOLLOWING);
                    return followsPhotos === (innerWidth <= 480);
                }
                """), $"{width}px: action reading order must match their visible placement.");
            if (width <= 480)
            {
                Assert.True(await reply.EvaluateAsync<bool>("reply => reply.querySelector('.lmx-discussion-reply-footer').getBoundingClientRect().top >= reply.querySelector('.lmx-reply-photos').getBoundingClientRect().bottom - 1"));
                await mention.FocusAsync();
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(photo).ToBeFocusedAsync();
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(reply.Locator("[data-discussion-reply-to]")).ToBeFocusedAsync();
            }
            else
            {
                await reply.Locator("[data-discussion-reply-delete]").FocusAsync();
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(mention).ToBeFocusedAsync();
                await page.Keyboard.PressAsync("Tab");
                await Assertions.Expect(photo).ToBeFocusedAsync();
            }
            var capture = Environment.GetEnvironmentVariable("LWC_CONVERSATION_CAPTURE_DIR");
            if (!string.IsNullOrEmpty(capture))
            {
                Directory.CreateDirectory(capture);
                await reply.ScreenshotAsync(new() { Path = Path.Combine(capture, $"reply-focus-{initialWidth}-{width}.png") });
            }
        }
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    [InlineData(320, ColorScheme.Light)]
    public async Task DiscussionReplies_KeepContextAndActionsOnTheNameRow(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ColorScheme = theme,
            IsMobile = width < 600, HasTouch = width < 600
        });
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState());
        var thread = DiscussionThread(page, "p7", 5);
        await Assertions.Expect(thread.Locator("[data-discussion-reply-id]").First).ToBeVisibleAsync();

        var problems = await thread.EvaluateAsync<string[]>(
            """
            (thread, wide) => {
                const issues = [];
                for (const reply of thread.querySelectorAll('[data-discussion-reply-id]:not([hidden])')) {
                    const id = reply.dataset.discussionReplyId;
                    const meta = reply.querySelector(':scope > .lmx-discussion-reply-meta');
                    const name = meta?.querySelector('strong, .lmx-discussion-author-name')?.getBoundingClientRect();
                    const actions = reply.querySelector('.lmx-discussion-reply-footer');
                    const link = reply.querySelector('[data-discussion-copy-link]').getBoundingClientRect();
                    const context = reply.querySelector('.lmx-discussion-context-link');
                    const body = reply.querySelector('[data-discussion-reply-body]').getBoundingClientRect();
                    const box = reply.getBoundingClientRect();
                    if (!meta) { issues.push(`${id}: name row is missing`); continue; }
                    if (context && !meta.contains(context)) issues.push(`${id}: reply context is not on the name row`);
                    if (link.right > box.right + 1) issues.push(`${id}: copy link overflows the reply`);
                    const byline = reply.querySelector('.lmx-discussion-reply-byline').getBoundingClientRect();
                    if (body.top - byline.bottom > 16) issues.push(`${id}: the text does not follow the name (${Math.round(body.top - byline.bottom)}px gap)`);
                    // Phones put several actions under the text; a lone link stays on the name row.
                    const actionsBelow = innerWidth <= 480 && actions.children.length > 1;
                    if (actionsBelow) {
                        if (actions.parentElement !== reply || !(reply.querySelector('[data-discussion-reply-body]').compareDocumentPosition(actions) & Node.DOCUMENT_POSITION_FOLLOWING))
                            issues.push(`${id}: phone action reading order should follow the text`);
                        if (link.top < body.bottom - 1) issues.push(`${id}: phone actions should follow the text`);
                    } else {
                        if (!meta.contains(actions)) issues.push(`${id}: actions are not on the name row`);
                        if (wide && link.top > name.bottom) issues.push(`${id}: copy link starts below the name`);
                        if (link.top >= body.top) issues.push(`${id}: actions sit below the text`);
                    }
                }
                return issues;
            }
            """, width >= 960);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
    }
}
