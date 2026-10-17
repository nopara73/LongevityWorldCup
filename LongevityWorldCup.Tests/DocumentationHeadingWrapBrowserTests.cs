using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class DocumentationHeadingWrapBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/ruleset", 320, 640, 200)]
    [InlineData("/ruleset", 390, 844, 200)]
    [InlineData("/history", 320, 640, 200)]
    [InlineData("/ruleset", 320, 640, 100)]
    [InlineData("/ruleset", 1440, 900, 100)]
    public async Task SectionHeadings_WrapBetweenWords(string path, int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.WaitForFunctionAsync(
            "percent => parseFloat(getComputedStyle(document.documentElement).fontSize) >= 16 * percent / 100 - 0.5",
            textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");

        var broken = await page.EvaluateAsync<string[]>(
            """
            () => {
                const broken = [];
                const headings = [...document.querySelectorAll('.documentation-document :is(h2, h3, h4)')]
                    .filter(heading => heading.getClientRects().length);
                for (const heading of headings) {
                    const walker = document.createTreeWalker(heading, NodeFilter.SHOW_TEXT);
                    for (let node; (node = walker.nextNode());) {
                        for (const word of node.textContent.matchAll(/[^\s -]+-?/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const fragments = [...range.getClientRects()].filter(rect => rect.width > 0);
                            const style = getComputedStyle(heading);
                            const room = heading.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                            // A word wider than the heading itself may still need a last-resort break.
                            if (new Set(fragments.map(rect => Math.round(rect.top))).size > 1
                                && fragments.reduce((sum, rect) => sum + rect.width, 0) < room - 1)
                                broken.push(word[0]);
                        }
                    }
                }
                return headings.length ? broken : ['no headings rendered'];
            }
            """);
        Assert.True(broken.Length == 0, $"{path} {width}x{height} at {textPercent}%: headings break inside {string.Join(", ", broken.Distinct().Take(10))}.");
    }
}
