using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HighlightsRocketBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/events", 360, false)]
    [InlineData("/events", 340, true)]
    [InlineData("/events", 390, false)]
    [InlineData("/events", 320, true)]
    public async Task RelativeDates_KeepTheirRocketOnTheSameLineAsTheLastWord(string path, int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.events-board .fa-rocket').length > 0");
        await page.EvaluateAsync("() => document.fonts.ready");

        var orphans = await page.EvaluateAsync<string[]>(
            """
            () => {
                const orphans = [];
                for (const rocket of document.querySelectorAll('.events-board .fa-rocket')) {
                    const icon = rocket.getBoundingClientRect();
                    if (!icon.width) continue;
                    // Find the text right before the rocket in reading order.
                    const walker = document.createTreeWalker(rocket.closest('td, li, .event-message-cell') || document.body, NodeFilter.SHOW_TEXT);
                    let previous = null, node;
                    while ((node = walker.nextNode())) {
                        if (rocket.compareDocumentPosition(node) & Node.DOCUMENT_POSITION_FOLLOWING) break;
                        if (node.textContent.trim()) previous = node;
                    }
                    if (!previous) continue;
                    const words = [...previous.textContent.matchAll(/\S+/g)];
                    const last = words[words.length - 1];
                    const range = document.createRange();
                    range.setStart(previous, last.index);
                    range.setEnd(previous, last.index + last[0].length);
                    const word = [...range.getClientRects()].pop();
                    if (Math.abs((word.top + word.bottom) / 2 - (icon.top + icon.bottom) / 2) > word.height / 2)
                        orphans.push(`"${last[0]}" and its rocket split across lines`);
                }
                return orphans;
            }
            """);
        Assert.True(orphans.Length == 0, $"{path} {width}px: {string.Join("; ", orphans)}");
    }
}
