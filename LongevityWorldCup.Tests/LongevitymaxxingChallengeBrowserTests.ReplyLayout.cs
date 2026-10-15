using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
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
                    if (!meta || !meta.contains(actions)) { issues.push(`${id}: actions are not on the name row`); continue; }
                    if (context && !meta.contains(context)) issues.push(`${id}: reply context is not on the name row`);
                    if (link.right > box.right + 1) issues.push(`${id}: copy link overflows the reply`);
                    const byline = reply.querySelector('.lmx-discussion-reply-byline').getBoundingClientRect();
                    if (body.top - byline.bottom > 16) issues.push(`${id}: the text does not follow the name (${Math.round(body.top - byline.bottom)}px gap)`);
                    // Phones put several actions under the text; a lone link stays on the name row.
                    const actionsBelow = innerWidth <= 480 && actions.children.length > 1;
                    if (actionsBelow) {
                        if (link.top < body.bottom - 1) issues.push(`${id}: phone actions should follow the text`);
                    } else {
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
