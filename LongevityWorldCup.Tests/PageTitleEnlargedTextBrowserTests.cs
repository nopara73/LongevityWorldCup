using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PageTitleEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about", "#about-longevity-world-cup", 390, 844, 200)]
    [InlineData("/about", "#about-longevity-world-cup", 320, 640, 200)]
    [InlineData("/history", "#history-of-longevity-as-a-sport", 320, 640, 200)]
    [InlineData("/rejuvenation-olympics", "#ro-title", 390, 844, 200)]
    [InlineData("/rejuvenation-olympics", "#ro-title", 320, 640, 200)]
    [InlineData("/longevitymaxxing", "#lmx-title", 390, 844, 200)]
    [InlineData("/longevitymaxxing", "#lmx-title", 320, 640, 200)]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", 320, 640, 200)]
    [InlineData("/about", "#about-longevity-world-cup", 390, 844, 100)]
    [InlineData("/longevitymaxxing", "#lmx-title", 1440, 900, 100)]
    public async Task Title_KeepsEveryWordWholeAtEnlargedText(string path, string titleSelector, int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.Locator(titleSelector).WaitForAsync();
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textPercent}% !important; }}" });
        await page.EvaluateAsync("() => document.fonts.ready");

        var split = await page.EvaluateAsync<string[]>(
            """
            selector => {
                const title = document.querySelector(selector);
                const broken = [];
                const walker = document.createTreeWalker(title, NodeFilter.SHOW_TEXT);
                for (let node; (node = walker.nextNode());) {
                    for (const word of node.textContent.matchAll(/[^\s ]+/g)) {
                        const range = document.createRange();
                        range.setStart(node, word.index);
                        range.setEnd(node, word.index + word[0].length);
                        const lines = new Set(Array.from(range.getClientRects()).filter(rect => rect.width > 0).map(rect => Math.round(rect.top)));
                        if (lines.size > 1) broken.push(word[0]);
                    }
                }
                return broken;
            }
            """, titleSelector);
        Assert.True(split.Length == 0, $"{path} {width}x{height} at {textPercent}%: title breaks inside {string.Join(", ", split)}.");

        var fit = await page.EvaluateAsync<double[]>(
            "selector => { const title = document.querySelector(selector); return [title.scrollWidth, title.clientWidth, title.getBoundingClientRect().right, document.documentElement.clientWidth]; }",
            titleSelector);
        Assert.True(fit[0] <= fit[1] + 1, $"{path} {width}x{height}: the title overflows its column ({fit[0]}/{fit[1]}px).");
        Assert.True(fit[2] <= fit[3], $"{path} {width}x{height}: the title runs past the viewport ({fit[2]}/{fit[3]}px).");
    }
}
