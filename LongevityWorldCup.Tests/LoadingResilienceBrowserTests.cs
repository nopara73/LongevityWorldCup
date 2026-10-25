using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class LoadingResilienceBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task StickyRemeasurement_PreservesTheEntranceAndItsFrame()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        var result = await page.EvaluateAsync<bool[]>("""
            async () => {
                const bar = document.getElementById('site-sticky-header');
                handleScroll(0, true);
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                // A font/resize can invalidate the cached height on the scroll
                // that first reveals the bar.
                handleScroll(900, true);
                const frame = bar.getBoundingClientRect();
                return [bar.classList.contains('visible'), !bar.inert,
                    bar.getAnimations().some(a => a.transitionProperty === 'clip-path'),
                    frame.top === 0 && frame.height > 0,
                    !bar.classList.contains('is-measuring')];
            }
            """);
        Assert.All(result, Assert.True);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DocumentationContents_IsCompactAndUsableBeforeItsScriptLoads(bool failScript)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/documentation.js*", async route =>
        {
            await release.Task;
            if (failScript) await route.AbortAsync();
            else await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/about", new() { WaitUntil = WaitUntilState.Commit });
            var toggle = page.Locator(".documentation-nav-toggle");
            await Assertions.Expect(toggle).ToBeVisibleAsync();
            await Assertions.Expect(page.Locator(".documentation-nav-links")).ToBeHiddenAsync();
            await page.EvaluateAsync("() => document.fonts.ready");
            var top = await page.Locator(".documentation-document").EvaluateAsync<double>("element => element.getBoundingClientRect().top");
            await toggle.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator(".documentation-nav-links")).ToBeVisibleAsync();
            release.TrySetResult();
            await page.WaitForLoadStateAsync(LoadState.Load);
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
            Assert.InRange(Math.Abs(await page.Locator(".documentation-document").EvaluateAsync<double>("element => element.getBoundingClientRect().top") - top), 0, 1);
            // Both the fallback and the enhanced binding toggle only once.
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page.Locator(".documentation-nav-links")).ToBeHiddenAsync();
        }
        finally
        {
            release.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
        }
    }

    [Fact]
    public async Task DocumentationWithoutJavaScript_LeavesTheContentsLinksVisible()
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false, ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/about");
        await Assertions.Expect(page.Locator(".documentation-nav-links")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".documentation-nav-links a").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DocumentationContents_WorksWhilePageModulesAreStillLoading()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // A synthetic deferred dependency keeps DCL pending without preventing
        // the real document and its parser-blocking contents script from running.
        await context.RouteAsync("**/about", async route =>
        {
            var response = await route.FetchAsync();
            await route.FulfillAsync(new() { Response = response, Body = (await response.TextAsync()).Replace("</head>", "<script defer src='/review-delayed.js'></script></head>") });
        });
        await context.RouteAsync("**/review-delayed.js", async route => { await release.Task; await route.FulfillAsync(new() { ContentType = "application/javascript", Body = "" }); });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/about", new() { WaitUntil = WaitUntilState.Commit });
            await Assertions.Expect(page.Locator(".documentation-nav.is-enhanced")).ToBeVisibleAsync();
            Assert.NotEqual("complete", await page.EvaluateAsync<string>("document.readyState"));
            var toggle = page.Locator(".documentation-nav-toggle");
            await toggle.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true", new() { Timeout = 2000 });
            await page.Keyboard.PressAsync("Escape");
            await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
            await page.Keyboard.PressAsync("Enter");
            var link = page.Locator(".documentation-nav-links a").First;
            var href = await link.GetAttributeAsync("href");
            await link.FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            Assert.Equal(href, await page.EvaluateAsync<string>("location.hash"));
            await page.WaitForFunctionAsync(
                "href => document.activeElement === document.getElementById(decodeURIComponent(href.slice(1)))",
                href, new() { Timeout = 2000 });
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement.matches('.documentation-document h1, .documentation-document h2, .documentation-document h3')"));
        }
        finally
        {
            release.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
        }
    }

    [Fact]
    public async Task LeavingCompactAction_IsImmediatelyInertAndReturnsFocus()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("() => scrollTo(0, 900)");
        var action = page.Locator(".scrolled-button");
        await Assertions.Expect(action).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
        await action.FocusAsync();
        var result = await page.EvaluateAsync<bool[]>("""
            async () => {
                scrollTo(0, 0);
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const action = document.querySelector('.scrolled-button');
                const restored = document.activeElement === document.querySelector('.join-game:not(.scrolled-button)');
                action.focus({ preventScroll: true });
                return [restored, !!action.closest('[inert]'), document.activeElement !== action];
            }
            """);
        Assert.All(result, Assert.True);
        await page.EvaluateAsync("() => scrollTo(0, 900)");
        await Assertions.Expect(action).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#site-sticky-header")).ToHaveAttributeAsync("aria-hidden", "false");
        await action.FocusAsync();
        Assert.True(await action.EvaluateAsync<bool>("element => !element.closest('[inert]') && document.activeElement === element"));
    }

    [Fact]
    public async Task PendingPortrait_SourceRemovalAndReducedMotionCannotLeaveItTransparent()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/review-stalled.png*", async route => { await release.Task; await route.AbortAsync(); });
        var page = await context.NewPageAsync();
        try
        {
            await NavigateAndSettleAsync(page, "/history");
            await page.EvaluateAsync("""
                () => {
                    const image = new Image();
                    image.id = 'review-portrait'; image.className = 'portrait';
                    image.alt = 'Portrait unavailable'; image.src = '/review-stalled.png';
                    document.querySelector('main').prepend(image);
                }
                """);
            var portrait = page.Locator("#review-portrait");
            await Assertions.Expect(portrait).ToHaveAttributeAsync("data-lwc-media", "pending");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            await Assertions.Expect(portrait).ToHaveCSSAsync("opacity", "1");
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
            await portrait.EvaluateAsync("image => image.removeAttribute('src')");
            await Assertions.Expect(portrait).ToHaveCSSAsync("opacity", "1");
            await Assertions.Expect(portrait).ToHaveAttributeAsync("data-lwc-media", "instant");
        }
        finally
        {
            release.TrySetResult();
            await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
        }
    }

    [Fact]
    public async Task LateFonts_ActuallyRenderRobotoAfterTheyLoad()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 1280, Height = 800 } });
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/*.woff2*", async route => { await release.Task; await route.ContinueAsync(); });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/ruleset", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForTimeoutAsync(1500);
            await Assertions.Expect(page.Locator(".documentation-document p").First).ToBeVisibleAsync();
        }
        finally { release.TrySetResult(); }
        await page.EvaluateAsync("() => document.fonts.ready");
        // FontFace.status and computed font-family alone do not establish which
        // font supplied the glyphs: optional faces can be loaded but never used.
        var session = await context.NewCDPSessionAsync(page);
        await session.SendAsync("DOM.enable");
        await session.SendAsync("CSS.enable");
        var document = await session.SendAsync("DOM.getDocument");
        var node = await session.SendAsync("DOM.querySelector", new() { ["nodeId"] = document!.Value.GetProperty("root").GetProperty("nodeId").GetInt32(), ["selector"] = ".documentation-document p" });
        var fonts = await session.SendAsync("CSS.getPlatformFontsForNode", new() { ["nodeId"] = node!.Value.GetProperty("nodeId").GetInt32() });
        Assert.Contains(fonts!.Value.GetProperty("fonts").EnumerateArray(), font =>
            font.GetProperty("isCustomFont").GetBoolean() && font.GetProperty("familyName").GetString() == "Roboto" && font.GetProperty("glyphCount").GetInt32() > 0);
        await session.DetachAsync();
        await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait });
    }
}
