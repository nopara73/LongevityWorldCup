using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsFeedWidthBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(1280, 720)]
    [InlineData(1024, 768)]
    [InlineData(844, 390)]
    [InlineData(768, 1024)]
    [InlineData(721, 900)]
    [InlineData(720, 900)]
    public async Task DesktopHighlights_ReadAsAColumnWithRecognisableFaces(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await page.Locator(".event-board-page .events-board tr.main-row").First.WaitForAsync();

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const board = document.querySelector('.event-board-page .events-board').getBoundingClientRect();
                const heading = document.querySelector('.event-board-heading').getBoundingClientRect();
                const avatar = document.querySelector('.event-board-page .events-board tr.main-row .avatar, .event-board-page .events-board tr.main-row .avatar-fallback').getBoundingClientRect();
                return [board.width, (board.left + board.right) / 2, innerWidth / 2, avatar.width, heading.width];
            }
            """);
        Assert.True(layout[0] <= 862, $"{width}px: the feed is {layout[0]}px wide; it should read at a column's width.");
        Assert.InRange(Math.Abs(layout[1] - layout[2]), 0, 2);
        Assert.InRange(layout[3], 31, 33);
        Assert.InRange(Math.Abs(layout[4] - layout[0]), 0, 2);
    }

    [Theory]
    [InlineData(719, 100)]
    [InlineData(719, 200)]
    [InlineData(390, 100)]
    [InlineData(390, 200)]
    [InlineData(720, 100)]
    [InlineData(820, 100)]
    [InlineData(720, 200)]
    [InlineData(1024, 200)]
    [InlineData(1440, 200)]
    public async Task Column_KeepsSubscriptionAlignmentAndBothAvatarKindsAtActualTextSize(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Key":"column-photo","Name":"Nóra Marosvölgyi","CurrentPlacement":232,"ProfilePic":"/assets/favicon-64x64.png"},
                  {"Key":"column-fallback","Name":"Alexandertheexceptionallylongunbrokentokenforreflow","CurrentPlacement":75},
                  {"Key":"column-initials","Name":"William Wilson","CurrentPlacement":3}
                ]
                """
        }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """
                [
                  {"Id":"column-photo","Type":1,"Text":"slug[column-photo]","OccurredAt":"2026-10-16T08:00:00Z","Relevance":10},
                  {"Id":"column-fallback","Type":2,"Text":"slug[column-fallback] rank[3] prev[column-photo]","OccurredAt":"2026-10-15T08:00:00Z","Relevance":10},
                  {"Id":"column-donation","Type":3,"Text":"sats[8455]","OccurredAt":"2026-10-14T08:00:00Z","Relevance":10},
                  {"Id":"column-initials","Type":1,"Text":"slug[column-initials]","OccurredAt":"2026-10-13T08:00:00Z","Relevance":10}
                ]
                """
        }));
        await context.RouteAsync("**/api/web-push/configuration", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = """{"publicKey":"test-public-key"}"""
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await page.Locator("#webPushToggle").WaitForAsync();
        await page.Locator("tr[data-event-id='column-fallback'] .avatar-fallback").WaitForAsync();
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.WaitForFunctionAsync("percent => Math.abs(parseFloat(getComputedStyle(document.querySelector('.event-message')).fontSize) - 16 * percent / 100) < .1", textPercent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('.events-board img.avatar')].every(image => image.complete && image.naturalWidth > 0)");

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const board = document.querySelector('.event-board-page .events-board').getBoundingClientRect();
                const heading = document.querySelector('.event-board-heading').getBoundingClientRect();
                if (Math.abs(heading.left - board.left) > 1 || Math.abs(heading.right - board.right) > 1)
                    issues.push('heading and feed edges do not align');
                const actions = document.querySelector('.event-board-actions').getBoundingClientRect();
                // The actions close the head band, one band inset from the feed's edge.
                const inset = parseFloat(getComputedStyle(document.querySelector('.event-board-heading')).paddingRight);
                if (Math.abs(board.right - inset - actions.right) > 1) issues.push('subscription actions leave the column');
                for (const target of document.querySelectorAll('.event-feed-link, #webPushToggle, .events-board .avatar-link')) {
                    const box = target.getBoundingClientRect();
                    if (box.width < 44 || box.height < 44) issues.push(`${target.className} is smaller than 44px`);
                }
                const avatarSize = 32;
                for (const avatar of document.querySelectorAll('.events-board .avatar, .events-board .avatar-fallback')) {
                    const box = avatar.getBoundingClientRect();
                    if (Math.abs(box.width - avatarSize) > 1 || Math.abs(box.height - avatarSize) > 1)
                        issues.push(`avatar is ${box.width}x${box.height}, expected ${avatarSize}`);
                }
                for (const initials of document.querySelectorAll('.events-board .avatar-fallback')) {
                    if (!initials.textContent.trim()) continue;
                    const box = initials.getBoundingClientRect(), style = getComputedStyle(initials);
                    const range = document.createRange(); range.selectNodeContents(initials);
                    const rects = [...range.getClientRects()].filter(rect => rect.width > 0);
                    if (new Set(rects.map(rect => Math.round(rect.top))).size !== 1
                        || rects.some(rect => rect.left < box.left + parseFloat(style.borderLeftWidth) - 1
                            || rect.right > box.right - parseFloat(style.borderRightWidth) + 1
                            || rect.top < box.top + parseFloat(style.borderTopWidth) - 1
                            || rect.bottom > box.bottom - parseFloat(style.borderBottomWidth) + 1))
                        issues.push(`${initials.textContent} is clipped inside its avatar`);
                }
                for (const cell of document.querySelectorAll('.event-message-cell')) {
                    const bounds = cell.getBoundingClientRect();
                    const walker = document.createTreeWalker(cell, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) {
                        const range = document.createRange(); range.selectNodeContents(node);
                        const rects = [...range.getClientRects()].filter(rect => rect.width > 0);
                        if (rects.some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                            issues.push(`${node.textContent.trim()} escapes its message`);
                        if (node.parentElement.closest('.placement-link') && new Set(rects.map(rect => Math.round(rect.top))).size > 1)
                            issues.push('rank token splits across lines');
                    }
                }
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows horizontally');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px / {textPercent}%: {string.Join("; ", problems)}");
        var rss = page.Locator(".event-feed-link");
        Assert.Equal("/feeds/events.rss", await rss.GetAttributeAsync("href"));
        await rss.FocusAsync();
        await Assertions.Expect(rss).ToBeFocusedAsync();
        Assert.Equal("false", await page.Locator("#webPushToggle").GetAttributeAsync("aria-pressed"));
    }
}
