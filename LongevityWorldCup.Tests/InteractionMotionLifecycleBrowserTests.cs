using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class InteractionMotionLifecycleBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private Task<IBrowserContext> NewMotionContextAsync() => NewContextAsync(Browser, App, new()
    {
        ViewportSize = new() { Width = 1440, Height = 900 }, ReducedMotion = ReducedMotion.NoPreference
    });

    [Fact]
    public async Task PreferenceChanges_CancelOwnedMotionAndRequireANewTrustedPress()
    {
        await using var context = await NewMotionContextAsync();
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.EvaluateAsync("() => window.dispatchEvent(new PointerEvent('pointerdown'))");
        Assert.False(await page.EvaluateAsync<bool>("() => LwcMotion.enabled()"));
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync("""
            () => {
                document.documentElement.style.setProperty('--lwc-motion-base', '2000ms');
                const panel = document.querySelector('main');
                LwcMotion.settle(panel);
                window.motionProbe = panel.getAnimations().find(a => !a.animationName && !a.transitionProperty);
            }
            """);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        // matches changes before the browser delivers MediaQueryList's change
        // event. Wait for the handler to close the gate before checking cleanup.
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-lwc-motion')");
        Assert.Equal("idle", await page.EvaluateAsync<string>("() => window.motionProbe.playState"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.False(await page.EvaluateAsync<bool>("() => LwcMotion.enabled()"));
        await page.Keyboard.PressAsync("Shift");
        Assert.True(await page.EvaluateAsync<bool>("() => LwcMotion.enabled()"));
    }

    [Theory]
    [InlineData("animate")]
    [InlineData("getAnimations")]
    [InlineData("CSSTransition")]
    [InlineData("media-listener")]
    public async Task OptionalAnimationApis_DoNotBreakActions(string missing)
    {
        await using var context = await NewMotionContextAsync();
        await context.AddInitScriptAsync("""
            (() => {
                const missing = MISSING;
                if (missing === 'animate' || missing === 'getAnimations') Element.prototype[missing] = undefined;
                if (missing === 'CSSTransition') window.CSSTransition = undefined;
            })();
            """.Replace("MISSING", System.Text.Json.JsonSerializer.Serialize(missing)));
        if (missing == "media-listener")
        {
            // Only this helper's MediaQueryList is legacy; unrelated components
            // have separate media listeners and are outside this fallback test.
            await context.RouteAsync("**/js/motion.js*", async route =>
            {
                var response = await route.FetchAsync();
                var source = await response.TextAsync();
                source = source.Replace("const reduced = window.matchMedia(\"(prefers-reduced-motion: reduce)\");",
                    "const reduced = window.matchMedia(\"(prefers-reduced-motion: reduce)\"); reduced.addEventListener = undefined;");
                await route.FulfillAsync(new() { ContentType = "application/javascript", Body = source });
            });
        }
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                let changed = false;
                const rows = [...document.querySelectorAll('#athleteLeaderboard tbody tr')];
                LwcMotion.flip(rows, () => { changed = true; rows[0].parentNode.append(rows[0]); });
                LwcMotion.settle(rows[0]);
                const toggle = document.createElement('button');
                toggle.setAttribute('aria-pressed', 'true');
                document.body.append(toggle); toggle.click();
                return changed;
            }
            """));
        await page.EvaluateAsync("() => LwcMotion.afterExit(document.createElement('div'), () => window.exitFinished = true)");
        Assert.True(await page.EvaluateAsync<bool>("() => window.exitFinished"));
        await page.EvaluateAsync("() => new Promise(requestAnimationFrame)");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RepeatedMotion_PreservesTransformsAndDoesNotStack()
    {
        await using var context = await NewMotionContextAsync();
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        var result = await page.EvaluateAsync<bool[]>("""
            () => {
                const row = document.querySelector('#athleteLeaderboard tbody tr[data-athlete-name]');
                row.style.transform = 'rotate(2deg)';
                const original = getComputedStyle(row).transform;
                LwcMotion.settle(row);
                LwcMotion.settle(row);
                const owned = row.getAnimations().filter(a => !a.animationName && !a.transitionProperty);
                for (const a of owned) { a.pause(); a.currentTime = 50; }
                const preserved = getComputedStyle(row).transform === original;
                const rows = [...row.parentNode.children];
                LwcMotion.flip(rows, () => row.parentNode.append(row));
                LwcMotion.flip(rows, () => row.parentNode.prepend(row));
                return [preserved, owned.length === 1, rows.every(r => r.getAnimations().filter(a => !a.animationName && !a.transitionProperty).length <= 1)];
            }
            """);
        Assert.Equal([true, true, true], result);
    }

    [Fact]
    public async Task ExitWaiters_ReplaceAndCancelWithoutLateCallbacks()
    {
        await using var context = await NewMotionContextAsync();
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        var result = await page.EvaluateAsync<string[]>("""
            async () => {
                const element = document.createElement('div'); document.body.append(element);
                const animation = element.animate([{opacity:0}, {opacity:1}], {duration:10000});
                const calls = [];
                LwcMotion.afterExit(element, () => calls.push('stale'));
                LwcMotion.afterExit(element, () => calls.push('current'));
                animation.cancel();
                await new Promise(resolve => setTimeout(resolve, 450));
                element.animate([{opacity:0}, {opacity:1}], {duration:10000});
                const cancel = LwcMotion.afterExit(element, () => calls.push('cancelled'));
                cancel();
                await new Promise(resolve => setTimeout(resolve, 450));
                LwcMotion.afterExit(element, () => calls.push('timeout'));
                await new Promise(resolve => setTimeout(resolve, 450));
                for (const a of element.getAnimations()) a.cancel();
                await Promise.resolve();
                element.remove();
                return calls;
            }
            """);
        Assert.Equal(["current", "timeout"], result);
    }

    [Fact]
    public async Task ClosingAlertAndMenu_ImmediatelyExcludeKeyboardFocusDuringPaint()
    {
        await using var context = await NewMotionContextAsync();
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync("() => { customAlert('Saved'); }");
        await page.WaitForTimeoutAsync(300);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                closeCustomAlert();
                const close = document.getElementById('custom-alert-close');
                close.focus();
                return document.activeElement !== close;
            }
            """));
        await page.Locator("#leaderboardOrderToggle").ClickAsync();
        await page.WaitForTimeoutAsync(300);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                document.getElementById('leaderboardOrderToggle').click();
                const select = document.getElementById('leaderboardOrderPrimary');
                select.focus();
                return document.activeElement !== select;
            }
            """));
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task RapidReopen_KeepsTheLatestAlertAndLoadingState(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync("() => { customAlert('First'); }");
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync("() => { closeCustomAlert(); customAlert('Latest'); showLoading(); hideLoading(); showLoading(); }");
        await page.WaitForTimeoutAsync(500);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Latest");
        await Assertions.Expect(page.Locator("#custom-alert-close")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#custom-alert")).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToHaveCSSAsync("opacity", "1");
        await page.EvaluateAsync("() => { closeCustomAlert(); hideLoading(); }");
        Assert.False(await page.EvaluateAsync<bool>("() => document.activeElement.closest('#custom-alert, #loading-dialog') !== null"));
    }

    [Fact]
    public async Task ClosingAccordion_ImmediatelyExcludesItsRetainedContents()
    {
        await using var context = await NewMotionContextAsync();
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync("""
            () => {
                const details = document.createElement('details');
                details.id = 'motion-close-probe';
                details.innerHTML = '<summary>History</summary><div style="height:200px"><button>Earlier reply</button></div>';
                document.querySelector('main').append(details);
                details.open = true;
            }
            """);
        await page.WaitForTimeoutAsync(300);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const details = document.getElementById('motion-close-probe');
                details.querySelector('summary').focus();
                details.open = false;
                const reply = details.querySelector('button');
                reply.focus();
                return document.activeElement !== reply;
            }
            """));
    }
}
