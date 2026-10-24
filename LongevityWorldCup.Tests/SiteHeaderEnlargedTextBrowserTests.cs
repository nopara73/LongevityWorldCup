using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class SiteHeaderEnlargedTextBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/", 320, 640)]
    [InlineData("/", 390, 844)]
    [InlineData("/", 639, 844)]
    [InlineData("/", 640, 844)]
    [InlineData("/", 641, 844)]
    [InlineData("/", 768, 1024)]
    [InlineData("/", 844, 390)]
    [InlineData("/", 1440, 900)]
    [InlineData("/about", 320, 640)]
    [InlineData("/about", 390, 844)]
    [InlineData("/about", 639, 844)]
    [InlineData("/about", 640, 844)]
    [InlineData("/about", 641, 844)]
    [InlineData("/about", 768, 1024)]
    [InlineData("/about", 844, 390)]
    [InlineData("/about", 1440, 900)]
    [InlineData("/ruleset", 320, 640)]
    [InlineData("/ruleset", 390, 844)]
    [InlineData("/events", 390, 844)]
    [InlineData("/events", 768, 1024)]
    public async Task Header_KeepsCompleteTextAndSeparateTargetsAtNormalAndDoubleTextSize(
        string path, int width, int height)
    {
        foreach (var theme in new[] { ColorScheme.Light, ColorScheme.Dark })
        {
            await using var context = await NewHeaderContextAsync(width, height, theme);
            var page = await context.NewPageAsync();
            await NavigateHeaderAsync(page, path);
            Assert.True(await page.EvaluateAsync<bool>("document.fonts.check('700 20px Roboto')"));
            await AssertHeaderFitsAsync(page, sticky: false);

            await DoubleTextAsync(page);
            await ScrollToTopAsync(page);
            await AssertHeaderFitsAsync(page, sticky: false);
            await page.Mouse.WheelAsync(0, 600);
            await page.WaitForFunctionAsync("() => document.querySelector('header[role=banner]').getBoundingClientRect().bottom < 0");
            await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
            await AssertHeaderFitsAsync(page, sticky: true);
            Assert.True(await page.EvaluateAsync<bool>(
                "parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop) >= document.querySelector('#site-sticky-header').getBoundingClientRect().height - 1"));
        }
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    public async Task Header_AlsoReflowsWithDoubleRootSizeAndKeepsAnchorDestinationsVisible(int width)
    {
        await using var context = await NewHeaderContextAsync(width, 844, ColorScheme.Light);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await WaitForLayoutAsync(page);
        await AssertHeaderFitsAsync(page, sticky: false);
        await page.Locator(".documentation-nav-toggle").ClickAsync();
        var link = page.Locator(".documentation-nav a[href^='#']").Last;
        var hash = await link.GetAttributeAsync("href");
        await link.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($"{System.Text.RegularExpressions.Regex.Escape(hash!)}$"));
        await WaitForVisibleStickyHeaderAsync(page);
        await AssertHeaderFitsAsync(page, sticky: true);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.getElementById(decodeURIComponent(location.hash.slice(1))).getBoundingClientRect().top >= document.querySelector('#site-sticky-header').getBoundingClientRect().bottom - 1"));
    }

    [Fact]
    public async Task Header_EnlargedPrimaryPlayTargetCanBeTappedWithoutStickyInterception()
    {
        await using var context = await NewHeaderContextAsync(390, 844, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await DoubleTextAsync(page);
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "true");
        await page.Locator("header .join-game:not(.scrolled-button)").TapAsync();
        await page.WaitForURLAsync("**/play", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator(".play-menu-wordmark")).ToHaveTextAsync("JUST TRACK IT");
    }

    [Fact]
    public async Task Header_ResizingEnlargedTextRefitsTheVisibleStickyBarWithoutAnotherScroll()
    {
        await using var context = await NewHeaderContextAsync(390, 844, ColorScheme.Dark);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await DoubleTextAsync(page);
        await page.Mouse.WheelAsync(0, 1000);
        await WaitForVisibleStickyHeaderAsync(page);
        foreach (var size in new[] { (320, 844), (844, 390), (390, 844) })
        {
            await page.SetViewportSizeAsync(size.Item1, size.Item2);
            await AssertHeaderFitsAsync(page, sticky: true);
            Assert.True(await page.EvaluateAsync<bool>(
                "parseFloat(getComputedStyle(document.documentElement).scrollPaddingTop) >= document.querySelector('#site-sticky-header').getBoundingClientRect().height - 1"));
        }
    }

    [Fact]
    public async Task Header_GlyphProbeDetectsClippingWithoutDocumentOverflow()
    {
        await using var context = await NewHeaderContextAsync(390, 844, ColorScheme.Light);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await AssertHeaderFitsAsync(page, sticky: false);
        await page.Locator("header .bannertext").EvaluateAsync(
            "element => { element.style.setProperty('min-width', '0'); element.style.maxWidth = '20px'; element.style.overflow = 'hidden'; element.style.whiteSpace = 'nowrap'; }");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var problems = await HeaderProblemsAsync(page, sticky: false);
        Assert.Contains(problems, problem => problem.Contains("clipped Longevity", StringComparison.Ordinal));
    }

    private Task<IBrowserContext> NewHeaderContextAsync(int width, int height, ColorScheme theme)
        => NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 933,
            HasTouch = width < 933
        });

    private static async Task DoubleTextAsync(IPage page)
    {
        // Root percentages alone leave vw/px/clamp text short of 200%. Freeze
        // computed sizes before changing any inheritance, then double every
        // body element's text while preserving the CSS viewport and root size.
        var ratios = await page.EvaluateAsync<double[]>(
            """
            async () => {
                const elements = [document.body, ...document.body.querySelectorAll('*')]
                    .filter(element => element instanceof HTMLElement);
                const sizes = elements.map(element => [element, parseFloat(getComputedStyle(element).fontSize)]);
                const targets = [...document.querySelectorAll('header .bannertext, header .join-game strong, header .join-game-middle, .site-sticky-header-title')];
                const before = targets.map(element => parseFloat(getComputedStyle(element).fontSize));
                for (const [element, size] of sizes) element.style.setProperty('font-size', `${size * 2}px`, 'important');
                for (let i = 0; i < 4; i++) await new Promise(requestAnimationFrame);
                return targets.map((element, index) => parseFloat(getComputedStyle(element).fontSize) / before[index]);
            }
            """);
        Assert.NotEmpty(ratios);
        Assert.All(ratios, ratio => Assert.InRange(ratio, 1.999, 2.001));
        await WaitForLayoutAsync(page);
    }

    private static Task WaitForLayoutAsync(IPage page)
        => page.EvaluateAsync("async () => { await document.fonts.ready; for (let i = 0; i < 4; i++) await new Promise(requestAnimationFrame); }");

    private static Task WaitForVisibleStickyHeaderAsync(IPage page)
        => page.WaitForFunctionAsync("""
            () => document.querySelector('header[role=banner]').getBoundingClientRect().bottom < 0
                && document.getElementById('site-sticky-header').getAttribute('aria-hidden') === 'false'
            """);

    private static async Task NavigateHeaderAsync(IPage page, string path)
    {
        await NavigateAndSettleAsync(page, path);
        await page.EvaluateAsync("async () => { await window.modulesReady; await Promise.allSettled([window.getSharedAthletes?.(), window.getSharedEvents?.()]); }");
        if (path == "/events")
            await page.WaitForFunctionAsync("() => document.getElementById('events-root')?.getAttribute('aria-busy') === 'false'");
        await WaitForLayoutAsync(page);
        await ScrollToTopAsync(page);
    }

    private static async Task ScrollToTopAsync(IPage page)
    {
        // Real wheel input cancels Highlights' optional opening scroll even when
        // it has not started, and avoids the homepage's programmatic guard.
        // scrollY may already be zero before Chromium delivers the wheel event.
        // Observe that input before measuring, so the opening timer is cancelled.
        await page.WheelAndWaitForInputAsync(0, -10000);
        await page.WaitForFunctionAsync("() => window.scrollY === 0");
    }

    private static async Task AssertHeaderFitsAsync(IPage page, bool sticky)
    {
        await WaitForLayoutAsync(page);
        var problems = await HeaderProblemsAsync(page, sticky);
        Assert.True(problems.Length == 0, $"{page.Url} {(sticky ? "sticky" : "main")} header: {string.Join("; ", problems)}");
    }

    private static Task<string[]> HeaderProblemsAsync(IPage page, bool sticky)
        => page.EvaluateAsync<string[]>(
            """
            sticky => {
                const issues = [];
                const brand = document.querySelector(sticky ? '.site-sticky-header-title' : 'header[role=banner] .bannertext');
                const action = document.querySelector(sticky ? '.scrolled-button' : 'header[role=banner] .join-game:not(.scrolled-button)');
                const container = document.querySelector(sticky ? '#site-sticky-header' : 'header[role=banner]');
                const visible = element => element && element.getBoundingClientRect().width > 0 && getComputedStyle(element).display !== 'none';
                if (brand.textContent.replace(/\s+/g, ' ').trim() !== 'Longevity World Cup') issues.push('brand wording changed');
                for (const root of [brand, action].filter(visible)) {
                    const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT);
                    let node;
                    while (node = walker.nextNode()) {
                        if (!visible(node.parentElement)) continue;
                        for (const word of node.textContent.matchAll(/[^\s\u00a0]+/g)) {
                            const range = document.createRange();
                            range.setStart(node, word.index);
                            range.setEnd(node, word.index + word[0].length);
                            const rects = [...range.getClientRects()].filter(rect => rect.width > 0);
                            if (new Set(rects.map(rect => Math.round(rect.top))).size > 1) issues.push(`split word ${word[0]}`);
                            for (const rect of rects) {
                                const outer = container.getBoundingClientRect();
                                if (rect.left < -1 || rect.right > innerWidth + 1 || rect.top < outer.top - 2 || rect.bottom > outer.bottom + 2)
                                    issues.push(`uncontained ${word[0]}`);
                                for (let ancestor = node.parentElement; ancestor && ancestor !== container.parentElement; ancestor = ancestor.parentElement) {
                                    // A fixed action escapes the main banner's overflow clip.
                                    if (sticky && ancestor.matches('header[role=banner]')) break;
                                    const style = getComputedStyle(ancestor), bounds = ancestor.getBoundingClientRect();
                                    if (/hidden|clip|auto|scroll/.test(style.overflowX) && (rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                                        issues.push(`clipped ${word[0]} by ${ancestor.className || ancestor.tagName}`);
                                    if (/hidden|clip|auto|scroll/.test(style.overflowY) && (rect.top < bounds.top - 2 || rect.bottom > bounds.bottom + 2))
                                        issues.push(`clipped ${word[0]} vertically`);
                                }
                            }
                        }
                    }
                }
                if (visible(action)) {
                    const a = action.getBoundingClientRect(), b = brand.getBoundingClientRect();
                    if (a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top) issues.push('brand/Play overlap');
                    if (a.width < 44 || a.height < 44) issues.push('Play target below 44px');
                    if (action.getAttribute('href') !== '/play') issues.push('Play destination changed');
                    const style = getComputedStyle(action), halo = getComputedStyle(action, '::before');
                    if (halo.content !== 'none' && halo.position === 'absolute') {
                        const containingBox = style.position === 'static' ? container.getBoundingClientRect() : a;
                        if (containingBox.left + parseFloat(halo.left) < a.left - 8 || containingBox.right - parseFloat(halo.right) > a.right + 8)
                            issues.push('Play halo detached from its target');
                    }
                }
                const link = brand.closest('a'), bounds = link.getBoundingClientRect();
                if (bounds.width < 44 || bounds.height < 44 || link.getAttribute('href') !== '/') issues.push('brand target changed');
                for (const target of [link, action].filter(visible)) {
                    const rect = target.getBoundingClientRect();
                    const hit = document.elementFromPoint((rect.left + rect.right) / 2, (rect.top + rect.bottom) / 2);
                    if (!hit || !target.contains(hit)) issues.push(`header target obscured: ${target.className}, scrollY=${scrollY}, center=${(rect.left + rect.right) / 2},${(rect.top + rect.bottom) / 2}, hit=${hit?.tagName}.${hit?.className}`);
                }
                return [...new Set(issues)];
            }
            """, sticky);
}
