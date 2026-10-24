using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class SiteNavigationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync(int width, int height, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = true
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task OpenAsync(IPage page, int textPercent = 100)
    {
        await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("async p => { await document.fonts.ready; document.documentElement.style.fontSize = `${p}%`; }", textPercent);
        await page.Locator("header .site-menu-toggle").ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Dialog, new() { Name = "Site menu" })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(1440, 900, 100, false)]
    [InlineData(390, 844, 100, true)]
    [InlineData(320, 640, 100, false)]
    [InlineData(768, 1024, 100, true)]
    [InlineData(844, 390, 100, false)]
    [InlineData(640, 320, 100, true)]
    [InlineData(320, 640, 200, true)]
    [InlineData(1440, 900, 200, true)]
    [InlineData(160, 422, 100, false)]
    public async Task Menu_FitsShortContentAndKeepsConstrainedContentReachable(int width, int height, int textPercent, bool dark)
    {
        await using var context = await ContextAsync(width, height, dark);
        var page = await context.NewPageAsync();
        await OpenAsync(page, textPercent);
        var issues = await page.EvaluateAsync<string[]>("""
            () => {
                const issues = [], panel = document.querySelector('.site-menu-panel').getBoundingClientRect();
                const body = document.querySelector('.site-menu-body');
                if (panel.left < -1 || panel.right > innerWidth + 1 || panel.top < -1 || panel.bottom > innerHeight + 1) issues.push('panel clipped');
                if (body.scrollWidth > body.clientWidth + 1) issues.push('horizontal scroller');
                const close = document.querySelector('.site-menu-close').getBoundingClientRect();
                const grip = document.querySelector('.site-menu-grip').getBoundingClientRect();
                if (grip.width && grip.right > close.left && grip.left < close.right && grip.bottom > close.top && grip.top < close.bottom) issues.push('grip overlaps close control');
                for (const link of body.querySelectorAll('a')) {
                    const box = link.getBoundingClientRect();
                    if (box.width < 44 || box.height < 44) issues.push('small target: ' + link.textContent.trim());
                    if (!link.closest('.site-menu-pages')) continue;
                    const range = document.createRange(); range.selectNodeContents(link.querySelector('.footer-link-label'));
                    for (const glyph of range.getClientRects()) {
                        if (glyph.width && (glyph.left < box.left - 1 || glyph.right > box.right + 1 || glyph.top < box.top - 1 || glyph.bottom > box.bottom + 1)) issues.push('clipped text: ' + link.textContent.trim());
                    }
                }
                return issues;
            }
            """);
        Assert.Empty(issues);
        Assert.Equal(15, await page.Locator("#site-menu .footer-link").CountAsync());
        if (textPercent == 100 && height >= 640 && width >= 320)
            Assert.True(await page.Locator(".site-menu-body").EvaluateAsync<bool>("b => b.scrollHeight <= b.clientHeight + 1"), "Ordinary short navigation should not need an internal scrollbar.");

        var last = page.Locator("#site-menu .footer-link").Last;
        await last.FocusAsync();
        Assert.True(await last.EvaluateAsync<bool>("e => { const r = e.getBoundingClientRect(), b = e.closest('.site-menu-body').getBoundingClientRect(); return r.top >= b.top - 1 && r.bottom <= b.bottom + 1; }"));
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Close menu" })).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(last).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task Menu_IsModalAndRestoresFocusOnEscapeBackdropAndBreakpoint(int width)
    {
        await using var context = await ContextAsync(width, 844);
        var page = await context.NewPageAsync();
        await OpenAsync(page);
        var toggle = page.Locator("header .site-menu-toggle");
        await Assertions.Expect(page.Locator("#site-menu .site-menu-pages [aria-current=page]")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#site-menu").EvaluateAsync<bool>("e => e.matches(':modal')"));
        await page.Locator("header .header-link").EvaluateAsync("e => e.focus()");
        Assert.True(await page.EvaluateAsync<bool>("document.querySelector('#site-menu').contains(document.activeElement)"));
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('site-menu-open')"));

        await toggle.ClickAsync();
        await page.Mouse.ClickAsync(2, 2);
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await toggle.ClickAsync();
        await page.SetViewportSizeAsync(width == 390 ? 900 : 390, 844);
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#site-menu")).ToBeHiddenAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('site-menu-open')"));
    }

    [Fact]
    public async Task Sheet_NativeTouchScrollingReachesLastLinkAndCancelledGripDoesNotDismiss()
    {
        await using var context = await ContextAsync(320, 480);
        var page = await context.NewPageAsync();
        await OpenAsync(page, 200);
        var body = page.Locator(".site-menu-body");
        await body.EvaluateAsync("e => e.scrollTop = 0");
        var box = (await body.BoundingBoxAsync())!;
        var cdp = await context.NewCDPSessionAsync(page);
        var x = box.X + box.Width / 2;
        var start = box.Y + box.Height - 20;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y = start } } });
        for (var i = 1; i <= 8; i++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x, y = start - i * 20 } } });
            await page.WaitForTimeoutAsync(25);
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
        await page.WaitForFunctionAsync("() => document.querySelector('.site-menu-body').scrollTop > 30");
        Assert.True(await page.Locator("#site-menu").EvaluateAsync<bool>("e => e.open"));

        var grip = (await page.Locator(".site-menu-grip").BoundingBoxAsync())!;
        x = grip.X + grip.Width / 2;
        start = grip.Y + grip.Height / 2;
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y = start } } });
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x, y = start + 100 } } });
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchCancel", ["touchPoints"] = Array.Empty<object>() });
        Assert.True(await page.Locator("#site-menu").EvaluateAsync<bool>("e => e.open"));
        Assert.Equal("", await page.Locator(".site-menu-panel").EvaluateAsync<string>("e => e.style.translate"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Menu_YieldsToAsyncOverlaysAndPreservesTheirReturnFocus(bool alert)
    {
        await using var context = await ContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await OpenAsync(page);
        await page.EvaluateAsync(alert ? "() => { void customAlert('Navigation test'); }" : "() => showLoading()");
        await Assertions.Expect(page.Locator("#site-menu")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(alert ? "#custom-alert-close" : ".loading-dialog-panel")).ToBeFocusedAsync();
        await page.Locator("header .site-menu-toggle").EvaluateAsync("e => e.click()");
        Assert.False(await page.Locator("#site-menu").EvaluateAsync<bool>("e => e.open"));
        await page.EvaluateAsync(alert ? "() => closeCustomAlert()" : "() => hideLoading()");
        await Assertions.Expect(page.Locator("header .site-menu-toggle")).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.body.matches('.site-menu-open,.no-scroll')"));
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(844)]
    public async Task StickyArrival_KeepsTheAnimatedBrandClearOfBothControls(int width)
    {
        await using var context = await ContextAsync(width, 720);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.Load });
        await page.EvaluateAsync("() => document.fonts.ready");
        var overlaps = await page.EvaluateAsync<int>("""
            async () => {
                const sticky = document.querySelector('#site-sticky-header');
                const brand = sticky.querySelector('.site-sticky-header-link');
                const controls = [document.querySelector('.scrolled-button'), sticky.querySelector('.site-menu-toggle')];
                scrollTo(0, document.querySelector('header[role=banner]').offsetHeight + 1);
                let overlaps = 0;
                for (let frame = 0; frame < 32; frame++) {
                    await new Promise(requestAnimationFrame);
                    const b = brand.getBoundingClientRect();
                    for (const control of controls) {
                        const a = control.getBoundingClientRect();
                        if (a.width && b.width && a.left < b.right && a.right > b.left && a.top < b.bottom && a.bottom > b.top) overlaps++;
                    }
                }
                return overlaps;
            }
            """);
        Assert.Equal(0, overlaps);
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
    }

    [Fact]
    public async Task LayoutWait_SettlesClockDrivenMotionWhileScrollMotionRemainsActive()
    {
        await using var context = await ContextAsync(1440, 900);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinStartAmateurBtn')?.disabled");
        await page.EvaluateAsync("""
            () => {
                window.testClockAnimation = document.querySelector('.play-join-card').animate(
                    [{ opacity: 0.5 }, { opacity: 1 }], { duration: 200, fill: 'forwards' });
            }
            """);
        await WaitForFiniteAnimationsAsync(page);
        Assert.Equal("finished", await page.EvaluateAsync<string>("window.testClockAnimation.playState"));
        Assert.True(await page.EvaluateAsync<bool>("""
            () => document.getAnimations().some(animation =>
                animation.effect?.target?.closest?.('header[role="banner"]')
                && animation.timeline !== document.timeline && animation.playState === 'running')
            """));
    }

    [Fact]
    public async Task Headroom_RevealsFocusedControlsAndRestoredScroll()
    {
        await using var context = await ContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("scrollTo(0, 800)");
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
        await page.EvaluateAsync("scrollBy(0, 100)");
        await page.WaitForFunctionAsync("() => document.querySelector('#site-sticky-header').classList.contains('is-tucked')");
        await page.Locator(".site-sticky-menu-toggle").FocusAsync();
        Assert.False(await page.Locator("#site-sticky-header").EvaluateAsync<bool>("e => e.classList.contains('is-tucked')"));
        await page.EvaluateAsync("scrollBy(0, 150)");
        Assert.False(await page.Locator("#site-sticky-header").EvaluateAsync<bool>("e => e.classList.contains('is-tucked')"));
        await page.Locator(".site-sticky-menu-toggle").ClickAsync();
        await page.EvaluateAsync("dispatchEvent(new Event('pagehide'))");
        await Assertions.Expect(page.Locator("#site-menu")).ToBeHiddenAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('site-menu-open')"));
        await page.EvaluateAsync("dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }))");
        Assert.False(await page.Locator("#site-sticky-header").EvaluateAsync<bool>("e => e.classList.contains('is-tucked')"));
    }

    [Theory]
    [InlineData(160, 100)]
    [InlineData(320, 200)]
    [InlineData(390, 100)]
    [InlineData(1440, 100)]
    public async Task PlatformLabels_FitTheirScrollportWithoutAddingShortMenuScrollbars(int width, int textPercent)
    {
        await using var context = await ContextAsync(width, 844);
        var page = await context.NewPageAsync();
        await OpenAsync(page, textPercent);
        await page.Keyboard.PressAsync("Tab");
        foreach (var link in await page.Locator("#site-menu .site-menu-follow a:has(.footer-link-label)").AllAsync())
        {
            await link.FocusAsync();
            await page.EvaluateAsync("async () => { for (let i = 0; i < 3; i++) await new Promise(requestAnimationFrame); }");
            Assert.True(await link.EvaluateAsync<bool>("""
                a => {
                    const label = a.querySelector('.footer-link-label'), box = label.getBoundingClientRect();
                    const body = a.closest('.site-menu-body'), viewport = body.getBoundingClientRect();
                    return getComputedStyle(label).clipPath === 'none'
                        && box.left >= viewport.left - 1 && box.right <= viewport.right + 1
                        && box.top >= viewport.top - 1 && box.bottom <= viewport.bottom + 1
                        && body.scrollWidth <= body.clientWidth + 1;
                }
                """), $"Platform label is clipped at {width}px / {textPercent}%.");
            if (width >= 320 && textPercent == 100)
                Assert.True(await page.Locator(".site-menu-body").EvaluateAsync<bool>("b => b.scrollHeight <= b.clientHeight + 1"));
        }
    }
}
