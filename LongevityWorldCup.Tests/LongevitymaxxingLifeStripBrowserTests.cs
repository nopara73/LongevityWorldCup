using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LongevitymaxxingLifeStripBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 640)]
    [InlineData(359, 844)]
    [InlineData(360, 844)]
    [InlineData(361, 844)]
    [InlineData(390, 844)]
    [InlineData(519, 844)]
    [InlineData(520, 844)]
    [InlineData(521, 844)]
    [InlineData(550, 900)]
    [InlineData(619, 900)]
    [InlineData(620, 900)]
    [InlineData(621, 900)]
    [InlineData(768, 1024)]
    [InlineData(844, 390)]
    [InlineData(1024, 768)]
    [InlineData(1080, 900)]
    [InlineData(1081, 900)]
    [InlineData(1200, 900)]
    [InlineData(1440, 900)]
    public async Task LifeStrip_ContainsTextAndIconsThroughTextResizing(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        await page.WaitForFunctionAsync("() => document.querySelector('main.lmx-page').getAttribute('aria-busy') === 'false'");
        await SettleAsync(page);
        var normal = await MeasureAsync(page);
        AssertLayout(normal, $"{width}x{height}, normal text");
        if (width is 320 or 390) Assert.Equal(2, normal.Columns);
        if (width is 768 or 1080 or 1081 or 1200 or 1440) Assert.Equal(4, normal.Columns);

        foreach (var percent in new[] { 125, 150, 175, 200 })
        {
            // Root percentages use the browser default, which need not equal the
            // page's measured baseline. Enlarge the actual starting size instead.
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", normal.RootFontSize * percent / 100);
            await WaitForRootFontSizeAsync(page, normal.RootFontSize * percent / 100);
            await SettleAsync(page);
            var enlarged = await MeasureAsync(page);
            Assert.InRange(enlarged.RootFontSize, normal.RootFontSize * percent / 100 - 0.1, normal.RootFontSize * percent / 100 + 0.1);
            // The existing phone clamp includes vw, so root enlargement alone need not double every label.
            Assert.True(enlarged.LabelFontSize > normal.LabelFontSize, "The label itself must enlarge.");
            AssertLayout(enlarged, $"{width}x{height}, {percent}% root font");
        }

        // True 200% label size, independent of rem-based gaps, padding and thresholds.
        await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
        await WaitForRootFontSizeAsync(page, normal.RootFontSize);
        await page.Locator("#lmxLifeStrip span").EvaluateAllAsync<object>(
            "(tiles, fontSize) => tiles.forEach(tile => tile.style.fontSize = fontSize + 'px')", normal.LabelFontSize * 2);
        await WaitForLabelFontSizeAsync(page, normal.LabelFontSize * 2);
        await SettleAsync(page);
        var textOnly = await MeasureAsync(page);
        Assert.InRange(textOnly.LabelFontSize, normal.LabelFontSize * 2 - 0.1, normal.LabelFontSize * 2 + 0.1);
        Assert.Equal(normal.RootFontSize, textOnly.RootFontSize);
        AssertLayout(textOnly, $"{width}x{height}, actual 200% text with normal spacing");
    }

    [Theory]
    [InlineData(ColorScheme.Light, "signup")]
    [InlineData(ColorScheme.Dark, "signup")]
    [InlineData(ColorScheme.Light, "active")]
    [InlineData(ColorScheme.Dark, "active")]
    public async Task LifeStrip_ReflowsWithItsContainerAndLoadedFonts(ColorScheme theme, string phase)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 900, Height = 900 },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/longevitymaxxing/state", async route =>
        {
            var response = await route.FetchAsync();
            var state = JsonNode.Parse(await response.TextAsync())!;
            state["phase"] = phase;
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = state.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        await page.WaitForFunctionAsync("() => document.querySelector('main.lmx-page').getAttribute('aria-busy') === 'false'");
        await SettleAsync(page);
        var normal = await MeasureAsync(page);

        foreach (var (containerWidth, columns) in new[] { (220, 1), (320, 2), (460, 2), (680, 4), (320, 2) })
        {
            await page.Locator("#lmxLifeStrip").EvaluateAsync("(strip, width) => strip.style.width = width + 'px'", containerWidth);
            await SettleAsync(page);
            var layout = await MeasureAsync(page);
            AssertLayout(layout, $"900px viewport, {containerWidth}px container");
            Assert.Equal(columns, layout.Columns);
        }

        // Same viewport and container: only real text size changes the two-column decision.
        await page.Locator("#lmxLifeStrip span").EvaluateAllAsync<object>(
            "(tiles, fontSize) => tiles.forEach(tile => tile.style.fontSize = fontSize + 'px')", normal.LabelFontSize * 2);
        await WaitForLabelFontSizeAsync(page, normal.LabelFontSize * 2);
        await SettleAsync(page);
        var enlarged = await MeasureAsync(page);
        AssertLayout(enlarged, "320px container, actual 200% text");
        Assert.Equal(1, enlarged.Columns);

        await page.Locator("#lmxLifeStrip").EvaluateAsync("strip => strip.style.removeProperty('width')");
        // Resize the same loaded document across breakpoints in both directions.
        foreach (var width in new[] { 844, 621, 620, 521, 520, 361, 360, 320, 390, 620, 621, 844, 1440 })
        {
            await page.SetViewportSizeAsync(width, 900);
            await SettleAsync(page);
            AssertLayout(await MeasureAsync(page), $"resized to {width}px with actual 200% text");
        }
    }

    // A fixed number of animation frames can still sample an intermediate font
    // under load. Verify the requested text size before measuring its layout.
    private static Task WaitForRootFontSizeAsync(IPage page, double size)
        => page.WaitForFunctionAsync(
            "size => Math.abs(parseFloat(getComputedStyle(document.documentElement).fontSize) - size) < 0.1", size);

    private static Task WaitForLabelFontSizeAsync(IPage page, double size)
        => page.WaitForFunctionAsync(
            "size => [...document.querySelectorAll('#lmxLifeStrip span')].every(tile => Math.abs(parseFloat(getComputedStyle(tile).fontSize) - size) < 0.1)", size);

    private static async Task SettleAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts.ready");
        // Reduced-motion CSS still gives every element a 1ms transition. Flush
        // the new styles and await the root/strip transitions before measuring
        // text resizing; two animation frames can stop at the transition start.
        await page.EvaluateAsync("""
            async () => {
                getComputedStyle(document.documentElement).fontSize;
                const strip = document.getElementById('lmxLifeStrip');
                await Promise.all(document.getAnimations().filter(animation => {
                    const target = animation.effect?.target;
                    return animation.effect?.getTiming().iterations !== Infinity
                        && (target === document.documentElement || strip.contains(target));
                }).map(animation => animation.finished.catch(() => {})));
            }
            """);
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private static Task<LifeStripLayout> MeasureAsync(IPage page)
        => page.EvaluateAsync<LifeStripLayout>(
            """
            () => {
                const strip = document.getElementById('lmxLifeStrip');
                const tiles = [...strip.querySelectorAll('span')];
                const problems = [];
                const boxes = tiles.map(tile => tile.getBoundingClientRect());
                const contains = (outer, inner) => inner.left >= outer.left - 1 && inner.right <= outer.right + 1
                    && inner.top >= outer.top - 1 && inner.bottom <= outer.bottom + 1;
                const overlaps = (a, b) => a.left < b.right - 1 && b.left < a.right - 1
                    && a.top < b.bottom - 1 && b.top < a.bottom - 1;
                tiles.forEach((tile, index) => {
                    const label = tile.textContent.trim();
                    const box = boxes[index];
                    const icon = tile.querySelector('i');
                    const iconBox = icon.getBoundingClientRect();
                    if (!contains(strip.getBoundingClientRect(), box)) problems.push(label + ': tile outside strip');
                    if (!contains(box, iconBox) || iconBox.width < 1 || iconBox.height < 1)
                        problems.push(label + ': icon outside tile or missing');
                    if (!getComputedStyle(icon, '::before').content.includes('"')) problems.push(label + ': missing icon glyph');
                    if (tile.scrollWidth > tile.clientWidth + 1 || tile.scrollHeight > tile.clientHeight + 1)
                        problems.push(label + ': overflowing tile');
                    // Text-node fragments exclude the icon and the blockified flex item box.
                    const range = document.createRange();
                    const fragments = [];
                    for (const node of tile.childNodes) {
                        if (node.nodeType !== Node.TEXT_NODE || !node.textContent.trim()) continue;
                        range.selectNodeContents(node);
                        fragments.push(...range.getClientRects());
                    }
                    if (!fragments.length) problems.push(label + ': missing text');
                    for (const fragment of fragments) {
                        if (!contains(box, fragment)) problems.push(label + ': text outside tile');
                        if (overlaps(iconBox, fragment)) problems.push(label + ': icon overlaps text');
                    }
                    if (fragments.length) {
                        const textCenter = (Math.min(...fragments.map(r => r.top)) + Math.max(...fragments.map(r => r.bottom))) / 2;
                        if (Math.abs(textCenter - (iconBox.top + iconBox.bottom) / 2) > parseFloat(getComputedStyle(tile).fontSize) / 4 + 1)
                            problems.push(label + ': icon and text misaligned');
                    }
                });
                for (let a = 0; a < boxes.length; a++)
                    for (let b = a + 1; b < boxes.length; b++)
                        if (overlaps(boxes[a], boxes[b])) problems.push('Tiles overlap');
                return {
                    Problems: problems,
                    Labels: tiles.map(tile => tile.textContent.trim()),
                    Columns: boxes.filter(box => Math.abs(box.top - boxes[0].top) < 1).length,
                    RootFontSize: parseFloat(getComputedStyle(document.documentElement).fontSize),
                    LabelFontSize: parseFloat(getComputedStyle(tiles[0]).fontSize),
                    FontsLoaded: document.fonts.check('800 16px Roboto') && document.fonts.check('900 16px "Font Awesome 6 Free"')
                };
            }
            """);

    private static void AssertLayout(LifeStripLayout layout, string scenario)
    {
        Assert.True(layout.FontsLoaded, "Roboto and Font Awesome must be loaded before measuring.");
        Assert.Equal(["Work compatible", "Travel compatible", "Family compatible", "Illness compatible"], layout.Labels);
        Assert.True(layout.Problems.Length == 0, $"{scenario}: {string.Join("; ", layout.Problems)}");
    }

    public sealed class LifeStripLayout
    {
        public string[] Problems { get; set; } = [];
        public string[] Labels { get; set; } = [];
        public int Columns { get; set; }
        public double RootFontSize { get; set; }
        public double LabelFontSize { get; set; }
        public bool FontsLoaded { get; set; }
    }
}
