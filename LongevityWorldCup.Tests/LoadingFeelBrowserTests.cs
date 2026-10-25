using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// How pages arrive and change: no layout shift while data arrives, placeholders
/// that hand over to content, no wrong-theme first frames, a sticky bar that
/// stays stable during scroll handoffs, same-site view transitions, and instant states
/// under reduced motion.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LoadingFeelBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const double ShiftBudget = 0.05;

    private const string MeasureEmphasis = """
        () => [...document.querySelectorAll('main strong')].map(node => {
            const bounds = node.getBoundingClientRect();
            return {
                visible: bounds.bottom > 0 && bounds.top < innerHeight,
                text: node.textContent,
                rects: [...node.getClientRects()].map(rect => ({ x: rect.x, y: rect.y, width: rect.width, height: rect.height }))
            };
        })
        """;

    private const string RecordShifts = """
        window.__lwcShifts = [];
        new PerformanceObserver(list => {
            for (const entry of list.getEntries()) {
                if (entry.hadRecentInput) continue;
                const sources = (entry.sources || []).map(source => {
                    const node = source.node?.nodeType === 3 ? source.node.parentElement : source.node;
                    const name = !node || node.nodeType !== 1 ? '#text'
                        : node.id ? '#' + node.id : node.tagName.toLowerCase() + (typeof node.className === 'string' && node.className ? '.' + node.className.trim().split(/\s+/)[0] : '');
                    const box = rect => [rect.x, rect.y, rect.width, rect.height].map(Math.round).join(',');
                    return `${name} (${node?.textContent?.slice(0, 80)}) [${box(source.previousRect)}]->[${box(source.currentRect)}]@${Math.round(entry.startTime)}`;
                });
                window.__lwcShifts.push({ value: entry.value, sources: sources.join(' ') });
            }
        }).observe({ type: 'layout-shift', buffered: true });
        """;

    // Samples the canvas the visitor actually sees on every rendered frame,
    // from the first one until the test stops it.
    private const string RecordCanvas = """
        window.__lwcCanvas = [];
        const luminance = color => {
            const match = /rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?/.exec(color || '');
            if (!match || (match[4] !== undefined && Number(match[4]) < 0.5)) return null;
            return (0.2126 * match[1] + 0.7152 * match[2] + 0.0722 * match[3]) / 255;
        };
        const sample = () => {
            if (window.__lwcStopCanvas) return;
            const root = document.documentElement;
            if (root) {
                const body = document.body ? luminance(getComputedStyle(document.body).backgroundColor) : null;
                const canvas = body ?? luminance(getComputedStyle(root).backgroundColor);
                if (canvas !== null) window.__lwcCanvas.push(Math.round(canvas * 100) / 100);
            }
            requestAnimationFrame(sample);
        };
        requestAnimationFrame(sample);
        """;

    [Theory]
    [InlineData("/events", "**/api/events*", 1280, 800)]
    [InlineData("/events", "**/api/events*", 390, 844)]
    [InlineData("/join", "**/js/play-menu.js*", 1280, 800)]
    [InlineData("/join", "**/js/play-menu.js*", 390, 844)]
    [InlineData("/ruleset", "**/*.woff2*", 1280, 800)]
    [InlineData("/about", "**/*.woff2*", 390, 844)]
    public async Task LateData_DoesNotShiftThePageBeyondTheBudget(string path, string late, int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = height } });
        await context.AddInitScriptAsync(RecordShifts);
        var release = await HoldAsync(context, late);
        var page = await context.NewPageAsync();
        JsonElement? paintedCopy = null;
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await NextPaintAsync(page);
            // Late fonts arrive well after the first paint, as on a slow connection.
            await page.WaitForTimeoutAsync(late.Contains("woff2") ? 1500 : 400);
            if (late.Contains("woff2"))
                paintedCopy = await page.EvaluateAsync<JsonElement>(MeasureEmphasis);
        }
        finally { release.TrySetResult(); }

        if (late.Contains("woff2"))
            await page.EvaluateAsync("() => document.fonts.ready");
        else if (path == "/events")
            await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "false", new() { Timeout = 20_000 });
        else
            await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('play-route-ready')", null, new() { Timeout = 20_000 });
        await page.WaitForTimeoutAsync(600);

        if (paintedCopy is { } before)
            await AssertPaintedEmphasisDidNotReflowAsync(page, before, path);

        var shifts = await page.EvaluateAsync<Shift[]>("window.__lwcShifts");
        var total = shifts.Sum(shift => shift.Value);
        Assert.True(total < ShiftBudget,
            $"{path} at {width}px shifted {total:0.000} (budget {ShiftBudget}): " + string.Join("; ", shifts.Select(s => $"{s.Value:0.000} {s.Sources}")));
    }

    private static async Task AssertPaintedEmphasisDidNotReflowAsync(IPage page, JsonElement before, string path)
    {
        var after = await page.EvaluateAsync<JsonElement>(MeasureEmphasis);
        Assert.Equal(before.GetArrayLength(), after.GetArrayLength());
        for (var index = 0; index < before.GetArrayLength(); index++)
        {
            if (!before[index].GetProperty("visible").GetBoolean()) continue;
            Assert.Equal(before[index].GetProperty("text").GetString(), after[index].GetProperty("text").GetString());
            var oldRects = before[index].GetProperty("rects");
            var newRects = after[index].GetProperty("rects");
            Assert.True(oldRects.GetArrayLength() == newRects.GetArrayLength(),
                $"Late fonts changed painted emphasis from {oldRects.GetArrayLength()} to {newRects.GetArrayLength()} fragments on {path}: {before[index].GetProperty("text").GetString()}. Before: {oldRects}; after: {newRects}");
            for (var fragment = 0; fragment < oldRects.GetArrayLength(); fragment++)
                foreach (var dimension in new[] { "y", "height" })
                    Assert.True(Math.Abs(oldRects[fragment].GetProperty(dimension).GetDouble()
                        - newRects[fragment].GetProperty(dimension).GetDouble()) <= 1,
                        $"Late fonts reflowed painted emphasis on {path}: {before[index].GetProperty("text").GetString()}");
        }
    }

    [Theory]
    [InlineData("Regular", "700")]
    [InlineData("Bold", "400")]
    public async Task StaggeredFontWeights_KeepTheDocumentStable(string lastFace, string firstWeight)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        await context.AddInitScriptAsync(RecordShifts);
        var release = await HoldAsync(context, "**/*.woff2*");
        var releaseLast = await HoldAsync(context, $"**/Roboto-{lastFace}.woff2*");
        var page = await context.NewPageAsync();
        JsonElement paintedCopy = default;
        try
        {
            await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForTimeoutAsync(1500);
            paintedCopy = await page.EvaluateAsync<JsonElement>(MeasureEmphasis);
            release.TrySetResult();
            await page.EvaluateAsync("weight => document.fonts.load(`${weight} 17px Roboto`)", firstWeight);
            await NextPaintAsync(page);
            // Give the mixed network/fallback state a real painted frame.
            await page.WaitForTimeoutAsync(150);
            await AssertPaintedEmphasisDidNotReflowAsync(page, paintedCopy, $"/ruleset with Roboto {lastFace} pending");
        }
        finally
        {
            release.TrySetResult();
            releaseLast.TrySetResult();
        }
        await page.EvaluateAsync("() => document.fonts.ready");
        await NextPaintAsync(page);
        await AssertPaintedEmphasisDidNotReflowAsync(page, paintedCopy, "/ruleset");
        var shifts = await page.EvaluateAsync<Shift[]>("window.__lwcShifts");
        Assert.True(shifts.Sum(shift => shift.Value) < ShiftBudget,
            $"Roboto {lastFace} arriving last shifted {shifts.Sum(shift => shift.Value):0.000}: " +
            string.Join("; ", shifts.Select(shift => $"{shift.Value:0.000} {shift.Sources}")));
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task HighlightsPlaceholder_HoldsTheScreen_ThenFadesIntoTheDays(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = height } });
        var release = await HoldAsync(context, "**/api/events*");
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator("#events-root .events-skeleton-row").First).ToBeVisibleAsync();
            await NextPaintAsync(page);
            // The footer stays below the fold while the days are pending.
            var footerTop = await page.EvaluateAsync<double>("() => document.querySelector('.footer')?.getBoundingClientRect().top ?? Infinity");
            Assert.True(footerTop >= height, $"The footer showed at {footerTop:0}px under a {height}px viewport while highlights loaded.");
        }
        finally { release.TrySetResult(); }

        await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "false", new() { Timeout = 20_000 });
        await Assertions.Expect(page.Locator("#events-root tbody:not(.events-more) > .events-skeleton-row")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#events-root tr.main-row").First).ToBeVisibleAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.querySelector('#eventsTable tbody').getAnimations()
                .some(animation => animation.animationName === 'lwc-content-arrive')
            """), "The arriving days must fade up from the placeholder.");
    }

    [Theory]
    [InlineData("/proofs", 1280)]
    [InlineData("/edit-profile", 390)]
    public async Task TaskPlaceholder_IsShapedLikeATaskCard_AndHandsOverWithAFade(string path, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 844 } });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Loading Feel Athlete', DisplayName: 'Loading Feel Athlete',
                AccountEmail: 'loading@example.test', ProfilePic: '/assets/content-images/play-athlete-placeholder.jpg',
                Division: "Women's", Flag: 'United Kingdom', Why: 'More healthy years.',
                PersonalLink: 'https://example.test/athlete', MediaContact: 'loading@example.test',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: []
            }));
            sessionStorage.setItem('contactEmail', 'loading@example.test');
            localStorage.setItem('hasApplication', 'true');
            window.__lwcPanelFades = 0;
            const animate = Element.prototype.animate;
            Element.prototype.animate = function (...args) {
                if (this.parentElement?.classList.contains('initial-view-main')) window.__lwcPanelFades++;
                return animate.apply(this, args);
            };
            """);
        var release = await HoldAsync(context, "**/js/site-footer.js*");
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.Commit });
            await page.WaitForSelectorAsync(".initial-view-main", new() { State = WaitForSelectorState.Attached });
            await NextPaintAsync(page);
            Assert.Equal("pending", await page.EvaluateAsync<string>("document.documentElement.dataset.initialView"));
            var placeholder = await page.EvaluateAsync<double[]>("""
                () => {
                    const style = getComputedStyle(document.querySelector('.initial-view-main'), '::before');
                    return [parseFloat(style.width), parseFloat(style.height), parseFloat(style.borderTopLeftRadius)];
                }
                """);
            Assert.True(placeholder[0] >= 240 && placeholder[1] >= 120 && placeholder[2] < 40,
                $"The pending task must show a card-shaped placeholder, not a spinner ({string.Join(" x ", placeholder)}).");
        }
        finally { release.TrySetResult(); }

        await page.WaitForFunctionAsync("() => document.documentElement.dataset.lwcRevealed !== undefined || window.__lwcPanelFades > 0",
            null, new() { Timeout = 20_000 });
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        Assert.True(await page.EvaluateAsync<int>("window.__lwcPanelFades") > 0, "The resolved task must fade in over its placeholder.");
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.lwcRevealed === undefined", null, new() { Timeout = 5_000 });
    }

    [Theory]
    [InlineData("/join", "light", false)]
    [InlineData("/select-athlete", "light", false)]
    [InlineData("/", "dark", true)]
    [InlineData("/events", "dark", true)]
    [InlineData("/about", "light", false)]
    public async Task FirstFrames_UseTheFinalTheme(string path, string scheme, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light
        });
        await context.AddInitScriptAsync(RecordCanvas);
        // The hub resolves its route in a module; hold it so its first frames are observable.
        var release = await HoldAsync(context, "**/js/play-menu.js*");
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForTimeoutAsync(500);
        }
        finally { release.TrySetResult(); }
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.WaitForTimeoutAsync(500);
        var frames = await page.EvaluateAsync<double[]>("() => { window.__lwcStopCanvas = true; return window.__lwcCanvas; }");
        Assert.NotEmpty(frames);
        var wrong = frames.Where(luminance => dark ? luminance > 0.5 : luminance < 0.5).ToArray();
        Assert.True(wrong.Length == 0,
            $"{path} in {scheme} mode painted {wrong.Length} of {frames.Length} frames on the other theme's canvas: {string.Join(", ", frames.Take(24))}");
        Assert.Equal(1, await page.Locator("style#lwc-first-paint").CountAsync());
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task StickyBar_RevealsAndHidesWithoutMovingItsMeasuredFrame(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = 800 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var sticky = page.Locator("#site-sticky-header");
        await sticky.Locator(".site-menu-toggle").WaitForAsync(new() { State = WaitForSelectorState.Attached });

        Assert.Empty(await CaptureStickyHandoffAsync(page, 900));
        await Assertions.Expect(sticky).ToBeVisibleAsync();
        await Assertions.Expect(sticky).ToHaveAttributeAsync("aria-hidden", "false");

        await sticky.Locator(".scrolled-button").FocusAsync();
        Assert.Empty(await CaptureStickyHandoffAsync(page, 0));
        await Assertions.Expect(page.Locator("header[role='banner'] .join-game")).ToBeFocusedAsync();
        await Assertions.Expect(sticky).ToHaveAttributeAsync("aria-hidden", "true");
        await Assertions.Expect(sticky).ToBeHiddenAsync();
    }

    private static Task<string[]> CaptureStickyHandoffAsync(IPage page, int scrollY)
        => page.EvaluateAsync<string[]>("""
            async y => {
                const content = document.querySelector('.documentation-document');
                const contentTop = content.getBoundingClientRect().top + scrollY;
                scrollTo(0, y);
                const element = document.getElementById('site-sticky-header');
                const deadline = performance.now() + 5000;
                while (element.classList.contains('visible') !== (y > 0)) {
                    if (performance.now() > deadline) throw new Error('The sticky state did not change.');
                    await new Promise(requestAnimationFrame);
                }
                // Navigation now changes state without an entrance/exit animation.
                // Check the first painted state and subsequent frames, not a retired CSS effect.
                const first = element.getBoundingClientRect();
                const errors = new Set();
                for (let frame = 0; frame < 20; frame++) {
                    const rect = element.getBoundingClientRect();
                    if (y > 0) {
                        if (element.inert || rect.top !== 0 || rect.height <= 0 || Math.abs(rect.height - first.height) > 1)
                            errors.add('The visible header moved or became inert.');
                        for (const control of element.querySelectorAll('.site-sticky-header-link, .scrolled-button, .site-menu-toggle')) {
                            const r = control.getBoundingClientRect();
                            if (!control.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)))
                                errors.add(`Header control is not hittable: ${control.className}`);
                        }
                    } else if (!element.inert || rect.height !== 0) {
                        errors.add('The hidden header still occupies space or accepts focus.');
                    }
                    if (Math.abs(content.getBoundingClientRect().top + scrollY - contentTop) > 1)
                        errors.add('The header handoff shifted the document.');
                    await new Promise(requestAnimationFrame);
                }
                return [...errors];
            }
            """, scrollY);

    [Fact]
    public async Task SameSiteNavigation_CrossFadesWithAViewTransition()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        await context.AddInitScriptAsync("""
            addEventListener('pagereveal', event => {
                window.__lwcReveal = { transition: Boolean(event.viewTransition) };
            });
            """);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/about");
        await page.Locator("header[role='banner'] a.header-link").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/");
        await page.WaitForFunctionAsync("() => window.__lwcReveal !== undefined");
        Assert.True(await page.EvaluateAsync<bool>("window.__lwcReveal.transition"), "A same-site link must cross-fade into the next page.");
    }

    [Theory]
    [InlineData("pageswap")]
    [InlineData("pagereveal")]
    public async Task SkippedPageTransition_DoesNotRaiseAnUnhandledRejection(string eventName)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("""
            async name => {
                // Use a real browser transition and its rejected ready promise,
                // then deliver it through each cross-document lifecycle event.
                const transition = document.startViewTransition(() => {});
                const ready = transition.ready;
                const event = new Event(name);
                Object.defineProperty(event, 'viewTransition', { value: transition });
                dispatchEvent(event);
                transition.skipTransition();
                await transition.finished;
                await new Promise(resolve => setTimeout(resolve, 0));
            }
            """, eventName);
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public async Task HomepagePodium_LoadsTheChampionFirst()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 }, JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var portraits = page.Locator(".podium .podium-portrait");
        await Assertions.Expect(portraits).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".podium-item.first .podium-portrait")).ToHaveAttributeAsync("fetchpriority", "high");
        Assert.Equal(0, await page.Locator(".podium .podium-portrait[loading='lazy']").CountAsync());
    }

    [Fact]
    public async Task ReducedMotion_KeepsEveryStateWithoutMovement()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }, ReducedMotion = ReducedMotion.Reduce
        });
        var release = await HoldAsync(context, "**/api/events*");
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator("#events-root .events-skeleton-row").First).ToBeVisibleAsync();
        }
        finally { release.TrySetResult(); }
        await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "false", new() { Timeout = 20_000 });

        var state = await page.EvaluateAsync<string[]>("""
            () => {
                const rules = [];
                const walk = list => { for (const rule of list) { rules.push(rule); if (rule.cssRules) walk(rule.cssRules); } };
                for (const sheet of document.styleSheets) { try { walk(sheet.cssRules); } catch (_) { } }
                const transitions = rules.filter(rule => rule.constructor.name === 'CSSViewTransitionRule');
                return [
                    String(transitions.some(rule => rule.navigation === 'auto')),
                    String(transitions.some(rule => rule.navigation === 'none' && rule.parentRule?.media?.mediaText.includes('reduce'))),
                    String(document.documentElement.classList.contains('lwc-media-fade')),
                    String(document.querySelector('#eventsTable tbody').getAnimations().filter(a => a.playState === 'running' && a.animationName === 'lwc-content-arrive').length)
                ];
            }
            """);
        Assert.Equal("true", state[0]);
        Assert.Equal("true", state[1]);
        Assert.Equal("false", state[2]);
        Assert.Equal("0", state[3]);

        // The sticky bar still appears and leaves; it just does so in one frame.
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("() => window.scrollTo(0, 900)");
        var sticky = page.Locator("#site-sticky-header");
        await Assertions.Expect(sticky).ToBeVisibleAsync();
        Assert.Equal("none", await sticky.EvaluateAsync<string>("element => getComputedStyle(element).transform"));
        Assert.Equal(0, await sticky.EvaluateAsync<int>("element => element.getAnimations().length"));
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Assertions.Expect(sticky).ToBeHiddenAsync();
    }

    private static async Task<TaskCompletionSource> HoldAsync(IBrowserContext context, string pattern)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync(pattern, async route =>
        {
            await release.Task;
            await route.ContinueAsync();
        });
        return release;
    }

    private static Task NextPaintAsync(IPage page)
        => page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

    private sealed class Shift
    {
        public double Value { get; set; }
        public string Sources { get; set; } = "";
    }
}
