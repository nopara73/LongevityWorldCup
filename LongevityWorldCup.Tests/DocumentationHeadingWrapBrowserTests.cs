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
    [InlineData("/about")]
    [InlineData("/history")]
    [InlineData("/ruleset")]
    public async Task SectionHeadings_KeepWordsAndGlyphsReadableAcrossTextSizesAndResizing(string path)
    {
        var normalSizes = new Dictionary<int, double>();
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
            var headings = await page.Locator(".documentation-document :is(h2,h3,h4)")
                .EvaluateAllAsync<string[]>("headings => headings.map(h => `${h.id}:${h.textContent}`)");
            Assert.NotEmpty(headings);
            await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {percent}% !important; }}" });
            var widths = percent is 100 or 200
                ? new[] { 320, 390, 600, 601, 768, 844, 900, 901, 1024, 1440, 901, 600, 390, 320 }
                : new[] { 320, 390, 601, 901 };
            foreach (var width in widths)
            {
                await page.SetViewportSizeAndWaitForLayoutAsync(width, width == 844 ? 390 : 844);
                await SettleFontsAsync(page);
                var scenario = $"{path} {width}px / {percent}% text";
                var size = await page.Locator(".documentation-document h2").First
                    .EvaluateAsync<double>("h => parseFloat(getComputedStyle(h).fontSize)");
                if (percent == 100) normalSizes[width] = size;
                if (width <= 600)
                    Assert.InRange(Math.Abs(size - normalSizes[width] * percent / 100), 0, 0.1);
                Assert.Equal(headings, await page.Locator(".documentation-document :is(h2,h3,h4)")
                    .EvaluateAllAsync<string[]>("headings => headings.map(h => `${h.id}:${h.textContent}`)"));
                var problems = await page.EvaluateAsync<string[]>(HeadingProblems);
                Assert.True(problems.Length == 0, $"{scenario}: {string.Join("; ", problems)}.");
            }
        }
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task NumberedTitles_UseAvailableSpaceAndReturnInlineAfterResizing(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        foreach (var percent in new[] { 100, 125, 150, 175, 200, 100 })
        {
            await page.EvaluateAsync("p => document.documentElement.style.fontSize = `${p}%`", percent);
            foreach (var resizedWidth in new[] { width, 1440, width })
            {
                await page.SetViewportSizeAndWaitForLayoutAsync(resizedWidth, 844);
                await SettleFontsAsync(page);
                var problems = await page.EvaluateAsync<string[]>(
                    """
                    () => {
                        const issues = [];
                        for (const h of document.querySelectorAll('.ruleset-rule > h2')) {
                            const title = h.querySelector('.ruleset-rule-title');
                            if (!title) { issues.push(`${h.id}: title missing`); continue; }
                            const style = getComputedStyle(h), tile = getComputedStyle(h, '::before');
                            const hBox = h.getBoundingClientRect(), box = title.getBoundingClientRect();
                            const probe = title.cloneNode(true);
                            probe.style.cssText = 'position:fixed; visibility:hidden; width:min-content; max-width:none; flex:none';
                            h.append(probe);
                            const longest = probe.getBoundingClientRect().width;
                            probe.remove();
                            const tileWidth = parseFloat(tile.width), gap = parseFloat(style.columnGap);
                            const room = h.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                            const inline = box.left >= hBox.left + tileWidth + gap - 1;
                            if (inline !== (longest + tileWidth + gap <= room + 0.5))
                                issues.push(`${h.id}: ${inline ? 'squeezed beside' : 'needlessly below'} number (${longest}/${room})`);
                            if (!inline && (Math.abs(box.left - hBox.left) > 1
                                || box.top < hBox.top + parseFloat(style.paddingTop) + parseFloat(tile.height) + parseFloat(style.rowGap) - 1))
                                issues.push(`${h.id}: stacked title overlaps or is misaligned`);
                            if (style.counterIncrement !== 'ruleset-rule 1' || !/^counter\(ruleset-rule[,)]/.test(tile.content))
                                issues.push(`${h.id}: lost numbering`);
                            if (parseFloat(getComputedStyle(document.documentElement).fontSize) === 16 && innerWidth >= 320 && !inline)
                                issues.push(`${h.id}: normal-text title must stay beside its number`);
                        }
                        return issues;
                    }
                    """);
                Assert.True(problems.Length == 0, $"{resizedWidth}px / {percent}%: {string.Join("; ", problems)}.");
            }
        }
    }

    private static Task SettleFontsAsync(IPage page) => page.EvaluateAsync(
        "async () => { await document.fonts.ready; document.body.getBoundingClientRect(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

    // Emergency wrapping is allowed only when an unbroken word cannot fit its
    // actual text column. Every glyph, including those words, must stay visible.
    private const string HeadingProblems = """
        () => {
            const issues = [];
            const measure = document.createElement('canvas').getContext('2d');
            if (![...document.fonts].some(font => font.family.replaceAll('"', '') === 'Roboto' && font.status === 'loaded'))
                issues.push('Roboto is not loaded');
            for (const h of document.querySelectorAll('.documentation-document :is(h2,h3,h4)')) {
                const walker = document.createTreeWalker(h, NodeFilter.SHOW_TEXT);
                for (let node; (node = walker.nextNode());) {
                    if (node.parentElement.closest('.documentation-timeline-separator')) continue;
                    const column = node.parentElement.closest('.ruleset-rule-title,.documentation-timeline-title,.documentation-timeline-date,h2,h3,h4');
                    const box = column.getBoundingClientRect(), style = getComputedStyle(column);
                    const room = column.clientWidth - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                    const font = getComputedStyle(node.parentElement);
                    measure.font = `${font.fontStyle} ${font.fontWeight} ${font.fontSize} ${font.fontFamily}`;
                    const spacing = parseFloat(font.letterSpacing) || 0;
                    for (const word of node.textContent.matchAll(/[^\s\u00ad-]+-?/g)) {
                        const unbrokenWidth = measure.measureText(word[0]).width + spacing * word[0].length;
                        const range = document.createRange();
                        range.setStart(node, word.index); range.setEnd(node, word.index + word[0].length);
                        const lines = new Set([...range.getClientRects()].filter(r => r.width > 0).map(r => Math.round(r.top)));
                        if (lines.size > 1 && unbrokenWidth <= room - 1)
                            issues.push(`${h.id}: arbitrary break in ${word[0]}`);
                    }
                    for (let i = 0; i < node.length; i++) {
                        if (/\s|\u00ad/.test(node.data[i])) continue;
                        const range = document.createRange(); range.setStart(node, i); range.setEnd(node, i + 1);
                        for (const r of range.getClientRects()) {
                            if (!r.width) continue;
                            if (r.left < -1 || r.right > innerWidth + 1 || r.left < box.left - 1 || r.right > box.right + 1)
                                issues.push(`${h.id}: glyph escapes its column/viewport`);
                            for (let ancestor = column; ancestor; ancestor = ancestor.parentElement) {
                                const s = getComputedStyle(ancestor), b = ancestor.getBoundingClientRect();
                                if (/hidden|clip/.test(s.overflowX) && (r.left < b.left - 1 || r.right > b.right + 1))
                                    issues.push(`${h.id}: glyph clipped horizontally`);
                                if (/hidden|clip/.test(s.overflowY) && (r.top < b.top - 1 || r.bottom > b.bottom + 1))
                                    issues.push(`${h.id}: glyph clipped vertically`);
                            }
                        }
                    }
                }
            }
            if (document.documentElement.scrollWidth > innerWidth + 1) issues.push('page overflows');
            return [...new Set(issues)];
        }
        """;
}
