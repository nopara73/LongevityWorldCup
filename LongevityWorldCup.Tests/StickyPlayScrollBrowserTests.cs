using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed partial class StickyPlayScrollBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Snapshot = """
        () => {
            const usable = element => {
                if (!element) return false;
                const r = element.getBoundingClientRect(), s = getComputedStyle(element);
                let opacity = 1;
                for (let p = element; p; p = p.parentElement) opacity *= Number(getComputedStyle(p).opacity);
                return !element.inert && s.display !== 'none' && s.visibility === 'visible' && opacity >= .98
                    && s.pointerEvents !== 'none' && r.width >= 44 && r.height >= 44
                    && r.left >= 0 && r.right <= innerWidth && r.top >= 0 && r.bottom <= innerHeight
                    && [[.5,.5],[.15,.25],[.85,.25],[.15,.75],[.85,.75]].every(([x,y]) =>
                        element.contains(document.elementFromPoint(r.left + r.width*x, r.top + r.height*y)));
            };
            const bar = document.querySelector('#site-sticky-header'), b = bar.getBoundingClientRect(), s = getComputedStyle(bar);
            const painted = s.display !== 'none' && Number(s.opacity) >= .98 && b.height > 0
                && bar.contains(document.elementFromPoint(b.left + b.width/2, b.top + b.height/2));
            const play = document.querySelector('.scrolled-button'), f = play.getBoundingClientRect();
            const menu = bar.querySelector('.site-menu-toggle'), m = menu.getBoundingClientRect();
            const progress = document.querySelector('.documentation-progress'), ps = progress && getComputedStyle(progress);
            const progressAligned = !progress || ps.display === 'none' || Number(ps.opacity) < .01
                || Math.abs(progress.getBoundingClientRect().top - (painted ? b.bottom : 0)) <= 1;
            return {
                Y: scrollY,
                PastBanner: scrollY > document.querySelector('header[role=banner]').offsetHeight + 1,
                AnyPlay: [...document.querySelectorAll('.join-game')].some(usable),
                StickyPlay: usable(play),
                InsideBar: bar.contains(play) && f.top >= b.top && f.bottom <= b.bottom
                    && f.left >= b.left && f.right <= m.left - 4 && m.right <= b.right,
                BarPainted: painted,
                ProgressAligned: progressAligned
            };
        }
        """;

    [Theory]
    [InlineData("/", 1440, false, false)]
    [InlineData("/", 1440, true, true)]
    [InlineData("/", 390, false, true)]
    [InlineData("/", 390, true, false)]
    [InlineData("/ruleset", 1440, false, true)]
    [InlineData("/ruleset", 1440, true, false)]
    [InlineData("/ruleset", 390, false, false)]
    [InlineData("/ruleset", 390, true, true)]
    public async Task IncrementalWheelScroll_KeepsPlayOperableAndNavigationStableInEveryFrame(
        string route, int width, bool dark, bool reduced)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        // Native transition snapshots own hit testing until the incoming
        // transition ends, even after the restored document has parsed.
        await context.AddInitScriptAsync("""
            (() => {
                const supportsReveal = 'onpagereveal' in window;
                let navigation = 0;
                window.__stickyPageShown = false;
                window.__stickyTransitionSettled = !supportsReveal;
                window.addEventListener('pagehide', () => {
                    navigation++;
                    window.__stickyPageShown = false;
                    window.__stickyTransitionSettled = !supportsReveal;
                });
                window.addEventListener('pageshow', () => window.__stickyPageShown = true);
                window.addEventListener('pagereveal', event => {
                    const current = ++navigation;
                    window.__stickyTransitionSettled = !event.viewTransition;
                    const settled = () => {
                        if (current === navigation) window.__stickyTransitionSettled = true;
                    };
                    event.viewTransition?.finished.then(settled, settled);
                });
            })();
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(route, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
        await page.Locator("#site-sticky-header .site-menu-toggle").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.Mouse.MoveAsync(width / 2, 500);
        await page.EvaluateAsync($$"""
            () => {
                const read = {{Snapshot}};
                window.playScrollFrames = [];
                window.recordPlayScroll = true;
                const tick = () => {
                    if (!window.recordPlayScroll) return;
                    // Sample after all animation-frame handlers for this paint.
                    setTimeout(() => { if (window.recordPlayScroll) window.playScrollFrames.push(read()); }, 0);
                    requestAnimationFrame(tick);
                };
                requestAnimationFrame(tick);
            }
            """);
        // Ordinary input crosses the threshold and continues down. No large
        // programmatic jump, focus reveal, or scroll-direction exception.
        for (var i = 0; i < 24; i++)
        {
            var before = await page.EvaluateAsync<double>("scrollY");
            await page.Mouse.WheelAsync(0, 30);
            await page.WaitForFunctionAsync("y => scrollY > y", before);
        }
        await page.EvaluateAsync("async () => { await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame); window.recordPlayScroll = false; }");
        var frames = await page.EvaluateAsync<ScrollState[]>("() => window.playScrollFrames");
        Assert.True(frames.Length >= 20, $"Only {frames.Length} scroll frames were sampled.");
        Assert.All(frames, frame => Assert.True(frame.AnyPlay, $"PLAY was not painted and hit-testable at y={frame.Y}."));
        var scrolled = frames.Where(frame => frame.PastBanner && frame.Y > 200).ToArray();
        Assert.NotEmpty(scrolled);
        Assert.All(scrolled, AssertScrolledState);
        Assert.All(frames, frame => Assert.True(frame.ProgressAligned, $"Reading progress floated over content at y={frame.Y}."));

        foreach (var delta in new[] { -120, 180, -60, 240, -360, 300 })
        {
            var before = await page.EvaluateAsync<double>("scrollY");
            await page.Mouse.WheelAsync(0, delta);
            await page.WaitForFunctionAsync("y => scrollY !== y", before);
            await AssertControlsAsync(page);
        }

        await page.Locator(".site-sticky-menu-toggle").ClickAsync();
        await Assertions.Expect(page.Locator("#site-menu")).ToBeVisibleAsync();
        Assert.False((await page.EvaluateAsync<ScrollState>(Snapshot)).StickyPlay, "The modal must own hit testing while open.");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".site-sticky-menu-toggle")).ToBeFocusedAsync();
        await AssertControlsAsync(page);
        await page.Locator(".scrolled-button").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.WaitForDomContentLoadedUrlAsync("**/play");
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        // History restoration can replace the document after GoBack returns.
        // Wait across that navigation before reading the returned controls.
        await using var restored = await page.WaitForFunctionAsync($$"""
            route => {
                // Playwright polls truthiness synchronously: an async predicate's
                // Promise succeeds even when it will resolve to false.
                if (location.pathname !== route || document.readyState === 'loading'
                    || !window.__stickyPageShown || !window.__stickyTransitionSettled
                    || document.fonts.status !== 'loaded'
                    || [...document.querySelectorAll('link[rel=stylesheet]')].some(link => !link.sheet)
                    || !document.querySelector('.scrolled-button')
                    || !document.querySelector('#site-sticky-header .site-menu-toggle')) return false;
                // Read in the same document/turn as readiness. A later protocol
                // command can race the document replacement during restoration.
                const read = {{Snapshot}};
                return read();
            }
            """, route);
        // The leaderboard and play flow deliberately use manual restoration.
        // Both a restored scroll position and a return to the banner need PLAY.
        var returned = await restored.JsonValueAsync<ScrollState>();
        Assert.True(returned.AnyPlay, "Back navigation lost the Play action.");
        if (returned.PastBanner) AssertScrolledState(returned);
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.EndsWith("/play", new Uri(page.Url).AbsolutePath);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/ruleset")]
    public async Task TouchScrolling_ResizeAndEnlargedText_PreserveTheHeaderAction(string route)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true,
            IsMobile = true, ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(route, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => document.fonts.ready");
        var session = await context.NewCDPSessionAsync(page);
        for (var gesture = 0; gesture < 2; gesture++)
        {
            await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x = 195, y = 660, id = 1 } } });
            for (var step = 1; step <= 8; step++)
            {
                await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = 195, y = 660 - step * 40, id = 1 } } });
                await page.WaitForTimeoutAsync(20);
            }
            // End with the finger at rest, so the later tap activates PLAY
            // instead of stopping a still-active native fling.
            await page.WaitForTimeoutAsync(150);
            await session.SendAsync("Input.dispatchTouchEvent", new() { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        }
        await session.DetachAsync();
        await page.WaitForFunctionAsync("() => scrollY > 400");
        await AssertControlsAsync(page);
        await page.SetViewportSizeAsync(844, 390);
        await page.EvaluateAsync("() => { document.documentElement.style.fontSize = '200%'; }");
        await AssertControlsAsync(page);
        await page.SetViewportSizeAsync(320, 640);
        await AssertControlsAsync(page);
        await page.Locator(".scrolled-button").TapAsync();
        await page.WaitForDomContentLoadedUrlAsync("**/play");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task SafeAreas_KeepNavigationAndPlayOutsideScreenObstructions(int width, int height)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var session = await context.NewCDPSessionAsync(page);
        await session.SendAsync("Emulation.setSafeAreaInsetsOverride", new()
        {
            ["insets"] = new { top = 44, right = 44, bottom = 34, left = 44 }
        });
        await page.Mouse.WheelAsync(0, 600);
        await page.WaitForFunctionAsync("() => scrollY > 400");
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame); }");
        var state = await page.EvaluateAsync<ScrollState>(Snapshot);
        AssertScrolledState(state);
        var rightGap = await page.Locator(".site-sticky-menu-toggle").EvaluateAsync<double>("e => innerWidth - e.getBoundingClientRect().right");
        Assert.InRange(rightGap, 43, 45);
        var tops = await page.Locator(".site-sticky-header-link, .scrolled-button, .site-sticky-menu-toggle")
            .EvaluateAllAsync<double[]>("elements => elements.map(e => e.getBoundingClientRect().top)");
        Assert.All(tops, top => Assert.True(top >= 44, $"Navigation overlaps the screen inset at {top}px."));
        await session.DetachAsync();
    }

    internal static async Task AssertControlsAsync(IPage page)
    {
        await page.EvaluateAsync("async () => { await new Promise(requestAnimationFrame); await new Promise(requestAnimationFrame); }");
        AssertScrolledState(await page.EvaluateAsync<ScrollState>(Snapshot));
    }

    private static void AssertScrolledState(ScrollState state)
    {
        Assert.True(state.StickyPlay, $"Header PLAY is not painted and hit-testable at y={state.Y}.");
        Assert.True(state.BarPainted, $"Navigation flashed away at y={state.Y}.");
        Assert.True(state.ProgressAligned, $"Reading progress is detached at y={state.Y}.");
        Assert.True(state.InsideBar, $"PLAY must stay inside the header, clear of Menu at y={state.Y}.");
    }

    private sealed class ScrollState
    {
        public double Y { get; set; }
        public bool PastBanner { get; set; }
        public bool AnyPlay { get; set; }
        public bool StickyPlay { get; set; }
        public bool InsideBar { get; set; }
        public bool BarPainted { get; set; }
        public bool ProgressAligned { get; set; }
    }
}
