using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task Typing_LeavesOtherThreadsStillAndKeepsTheDraft(int width, bool legacySizing)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        if (legacySizing) await context.AddInitScriptAsync("""
            const supports = CSS.supports.bind(CSS);
            CSS.supports = (...args) => args[0] === 'field-sizing' ? false : supports(...args);
            """);
        var page = await OpenDiscussionWorkspaceAsync(context, ConversationWorkspaceState());
        if (legacySizing) await page.AddStyleTagAsync(new() { Content = ".lmx-discussion-reply-composer textarea { field-sizing: fixed; min-height: 5rem; }" });
        var thread = DiscussionThread(page, "p7", 5);
        await thread.Locator("[data-discussion-quick-reply]").ClickAsync();
        var input = thread.Locator("textarea");
        await input.FillAsync("A draft");
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        await thread.EvaluateAsync("""
            active => {
                window.typingOtherThreadReads = 0;
                window.typingOtherThreadMutations = 0;
                window.typingComposerReplacements = 0;
                const otherThread = element => {
                    const post = element.closest('#lmxNotes > article');
                    return post && post !== active;
                };
                for (const method of ['getBoundingClientRect', 'getClientRects']) {
                    const original = Element.prototype[method];
                    Element.prototype[method] = function(...args) {
                        if (otherThread(this)) window.typingOtherThreadReads++;
                        return original.apply(this, args);
                    };
                }
                new MutationObserver(records => {
                    for (const record of records) {
                        const element = record.target instanceof Element ? record.target : record.target.parentElement;
                        if (element && otherThread(element)) window.typingOtherThreadMutations++;
                        if (record.type === 'childList' && element?.closest('[data-discussion-draft]')) window.typingComposerReplacements++;
                    }
                }).observe(document.getElementById('lmxNotes'), {subtree:true, childList:true, attributes:true});
            }
            """);
        const string body = "A draft with responsive typing.";
        await input.PressSequentiallyAsync(" with responsive typing.");
        await page.EvaluateAsync("() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
        Assert.Equal(new[] { 0, 0, 0 }, await page.EvaluateAsync<int[]>("[typingOtherThreadReads, typingOtherThreadMutations, typingComposerReplacements]"));
        await Assertions.Expect(input).ToHaveValueAsync(body);
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(thread.Locator("[data-reply-count]")).ToHaveTextAsync($"{body.Length}/240");
        await Assertions.Expect(thread.Locator("[data-reply-submit]")).ToBeEnabledAsync();

        // Growing and shrinking the composer still moves its own rail correctly.
        await input.FillAsync(string.Join('\n', Enumerable.Repeat("A new line", 12)));
        await AssertThreadRailAsync(thread);
        await input.FillAsync(body);
        await AssertThreadRailAsync(thread);
        await page.ReloadAsync();
        await Assertions.Expect(thread.Locator("textarea")).ToHaveValueAsync(body);
    }

}
