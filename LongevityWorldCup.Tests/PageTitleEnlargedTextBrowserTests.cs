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
    [InlineData("/about", "#about-longevity-world-cup", 320, 36.8)]
    [InlineData("/about", "#about-longevity-world-cup", 1440, 53.6)]
    [InlineData("/history", "#history-of-longevity-as-a-sport", 390, 36.8)]
    [InlineData("/history", "#history-of-longevity-as-a-sport", 1440, 53.6)]
    [InlineData("/rejuvenation-olympics", "#ro-title", 320, 36)]
    [InlineData("/rejuvenation-olympics", "#ro-title", 1440, 56)]
    [InlineData("/longevitymaxxing", "#lmx-title", 320, 28.8)]
    [InlineData("/longevitymaxxing", "#lmx-title", 360, 29.6)]
    [InlineData("/longevitymaxxing", "#lmx-title", 390, 32)]
    [InlineData("/longevitymaxxing", "#lmx-title", 600, 32)]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", 559, 36)]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", 560, 48)]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", 600, 48)]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", 601, 48)]
    public async Task NormalText_PreservesTheExistingTitleSizeAtPhoneBreakpoints(string path, string titleSelector, int width, double expectedSize)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var size = await page.Locator(titleSelector).EvaluateAsync<double>("title => parseFloat(getComputedStyle(title).fontSize)");
        Assert.InRange(Math.Abs(size - expectedSize), 0, 0.1);
    }

    [Theory]
    [InlineData("/about", "#about-longevity-world-cup", "About Longevity World Cup")]
    [InlineData("/history", "#history-of-longevity-as-a-sport", "History of longevity as a sport")]
    [InlineData("/rejuvenation-olympics", "#ro-title", "Coming from the Rejuvenation Olympics?")]
    [InlineData("/longevitymaxxing", "#lmx-title", "Longevitymaxxing")]
    [InlineData("/unsubscribe", ".unsubscribe-panel h1", "Unsubscribe")]
    public async Task Titles_EnlargeAndReflowWithoutLosingWordingOrClippingGlyphs(string path, string titleSelector, string wording)
    {
        var widths = new[] { 160, 195, 320, 360, 390, 559, 560, 600, 601, 700, 701, 768, 820, 900, 901, 1024, 1440 };
        // Reserve additional inline space for the native scrollbar in the
        // narrow history-title case. Actual 200% zoom is verified separately.
        if (path == "/history") widths = [150, .. widths];
        var normalSizes = new Dictionary<int, double>();
        // Keep each text setting fixed while resizing. Resetting the root after every
        // resize can expose a stale enlarged clamp() value as the "normal" reference.
        // A separate 100% document provides the baseline without weakening growth checks.
        foreach (var percent in new[] { 100, 125, 150, 175, 200 })
        {
            await using var context = await NewContextAsync(Browser, App, new()
            {
                ViewportSize = new() { Width = 390, Height = 844 },
                ReducedMotion = ReducedMotion.Reduce,
                HasTouch = true
            });
            var page = await context.NewPageAsync();
            await NavigateAndSettleAsync(page, path);
            await SetTextSizeAsync(page, percent);
            await Assertions.Expect(page.Locator(titleSelector)).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(titleSelector)).ToHaveAccessibleNameAsync(wording);
            // 160/195 CSS pixels model 320/390px windows at 200% full browser zoom.
            var enlargedWidths = widths.Where(width => width >= 320).ToArray();
            var scenarioWidths = percent == 100 ? widths
                : percent == 200 ? enlargedWidths.Concat(enlargedWidths.Reverse().Skip(1)).ToArray()
                : new[] { 320, 390 };
            foreach (var width in scenarioWidths)
            {
                await page.SetViewportSizeAsync(width, width == 820 ? 390 : 844);
                await page.WaitForFunctionAsync("width => innerWidth === width", width);
                await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
                var scenario = $"{path} at {width}px / {percent}% text";
                var size = await page.Locator(titleSelector).EvaluateAsync<double>("title => parseFloat(getComputedStyle(title).fontSize)");
                if (percent == 100) normalSizes[width] = size;
                var normalSize = normalSizes[width];
                // The existing fluid 320-360px LMX size can resolve to its rem minimum.
                // It must nearly double; the other phone titles double exactly.
                if (width <= 600)
                {
                    var fluidTolerance = path == "/longevitymaxxing" && width <= 360 ? 0.97 : 1;
                    Assert.True(size >= normalSize * percent / 100 * fluidTolerance - 0.1, $"{scenario}: {normalSize}px only became {size}px.");
                }
                else if (percent == 200)
                    Assert.True(size >= normalSize * 1.25, $"{scenario}: desktop title did not meaningfully enlarge ({normalSize}px to {size}px).");

                Assert.Equal(wording, (await page.Locator(titleSelector).TextContentAsync())!.Replace("\u00ad", ""));
                var problems = await page.EvaluateAsync<string[]>(
                    """
                    selector => {
                        const title = document.querySelector(selector), issues = [];
                        if (![...document.fonts].some(font => font.family.replaceAll('"', '') === 'Roboto' && font.status === 'loaded'))
                            issues.push('Roboto is not loaded');
                        const titleBox = title.getBoundingClientRect();
                        if (title.scrollWidth > title.clientWidth + 1) issues.push('title overflows its column');
                        // Keep the compound suffix whole on supported phone widths;
                        // enlarged padding must not needlessly split "maxxing" again.
                        const suffix = title.querySelector('wbr')?.nextSibling;
                        if (suffix && innerWidth >= 320) {
                            const range = document.createRange(); range.selectNodeContents(suffix);
                            const lines = new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top)));
                            if (lines.size > 1) issues.push('compound suffix wraps despite the available phone width');
                        }
                        const walker = document.createTreeWalker(title, NodeFilter.SHOW_TEXT);
                        for (let node; (node = walker.nextNode());) {
                            // Soft hyphens are the only permitted internal word breaks.
                            for (const segment of node.textContent.matchAll(/[^\s\u00ad]+/g)) {
                                const lines = new Set();
                                for (let i = segment.index; i < segment.index + segment[0].length; i++) {
                                    const range = document.createRange(); range.setStart(node, i); range.setEnd(node, i + 1);
                                    // Chromium includes the preceding rendered soft hyphen in the
                                    // first letter's rects. The last rect belongs to the letter itself.
                                    const glyph = [...range.getClientRects()].filter(r => r.width > 0).at(-1);
                                    if (glyph) lines.add(Math.round(glyph.top));
                                }
                                if (lines.size > 1) issues.push(`arbitrary break inside ${segment[0]}`);
                            }
                            // A fitting block alone cannot prove that its glyphs are visible.
                            for (let i = 0; i < node.length; i++) {
                                if (/\s/.test(node.data[i])) continue;
                                const range = document.createRange(); range.setStart(node, i); range.setEnd(node, i + 1);
                                for (const r of range.getClientRects()) {
                                    if (r.width === 0) continue;
                                    if (r.left < -1 || r.right > innerWidth + 1 || r.left < titleBox.left - 1 || r.right > titleBox.right + 1)
                                        issues.push(`glyph ${node.data[i]} escapes the title/viewport`);
                                    for (let ancestor = node.parentElement; ancestor; ancestor = ancestor.parentElement) {
                                        const style = getComputedStyle(ancestor), box = ancestor.getBoundingClientRect();
                                        if (/hidden|clip/.test(style.overflowX) && (r.left < box.left - 1 || r.right > box.right + 1))
                                            issues.push(`glyph clipped horizontally by ${ancestor.className}`);
                                        if (/hidden|clip/.test(style.overflowY) && (r.top < box.top - 1 || r.bottom > box.bottom + 1))
                                            issues.push(`glyph clipped vertically by ${ancestor.className}`);
                                    }
                                }
                            }
                        }
                        return [...new Set(issues)];
                    }
                    """, titleSelector);
                Assert.True(problems.Length == 0, $"{scenario}: {string.Join("; ", problems)}.");
            }
        }
    }

    private static async Task SetTextSizeAsync(IPage page, int percent)
    {
        await page.EvaluateAsync("percent => { const style = document.getElementById('title-text-size') || document.head.appendChild(Object.assign(document.createElement('style'), { id: 'title-text-size' })); style.textContent = `html { font-size: ${percent}% !important; }`; }", percent);
        await page.WaitForFunctionAsync("percent => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - 16 * percent / 100) < 0.1", percent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; document.body.getBoundingClientRect(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
    }
}
