using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class InPageWaitBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync(ReducedMotion motion = ReducedMotion.NoPreference)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ReducedMotion = motion,
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        await context.RouteAsync("**/wait-regression", route => route.FulfillAsync(new()
        {
            ContentType = "text/html", Body = """
                <!doctype html><html><head>
                <link rel="stylesheet" href="/css/motion.css">
                <link rel="stylesheet" href="/css/flow-controls.css">
                <style>:root{--lwc-motion-base:120ms;--lwc-ease-enter:ease;--lwc-ease-exit:ease;--lwc-ease-standard:ease}
                body{margin:20px}button{position:relative;padding:12px;background:green;color:white}
                #board{position:relative;min-height:100px}#hidden{opacity:0;visibility:hidden}</style>
                </head><body><main id="form"><input id="draft" value="Kept">
                <div class="flow-action-stack flow-action-stack--hide-disabled-primary">
                <button id="save" class="flow-action">Submit change request</button><button id="back">Back</button>
                </div></main><section id="board"><p>Existing rows stay readable.</p><div id="hidden">Hidden actions</div></section>
                <script src="/js/motion.js"></script></body></html>
                """
        }));
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/wait-regression");
        await page.WaitForFunctionAsync("() => !!window.LwcWait");
        return page;
    }

    [Fact]
    public async Task PendingLabel_RetainsItsApprovedPaintedBackground()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        var action = page.Locator("#save");
        using var before = Image.Load<Rgba32>(await action.ScreenshotAsync());
        // The clear band above the label shares its background. Sampling real
        // pixels catches overlays that computed background-color cannot see.
        var approved = before[before.Width / 2, 8];
        await page.EvaluateAsync("() => { LwcWait.start(save); save.disabled=true; }");
        await page.WaitForTimeoutAsync(180);
        await page.EvaluateAsync("""
            () => { for(const animation of document.querySelector('.lwc-wait-fill').getAnimations()) {
                animation.pause(); animation.currentTime=3000;
            }}
            """);
        using var during = Image.Load<Rgba32>(await action.ScreenshotAsync());
        Assert.Equal(approved, during[during.Width / 2, 8]);
    }

    [Fact]
    public async Task Submission_RemainsVisibleWithoutMakingItsDraftOrBackInert()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        var width = await page.Locator("#save").EvaluateAsync<double>("el => el.getBoundingClientRect().width");
        await page.EvaluateAsync("() => { LwcWait.start(save, {region: form}); save.disabled = true; }");
        await Assertions.Expect(page.Locator("#save")).ToBeVisibleAsync();
        Assert.True(await page.Locator("#save").IsDisabledAsync());
        Assert.InRange(await page.Locator("#save").EvaluateAsync<double>("el => el.getBoundingClientRect().width") - width, -1, 1);
        await page.Locator("#draft").FillAsync("Edited during submission");
        await page.Locator("#back").FocusAsync();
        await Assertions.Expect(page.Locator("#back")).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("() => form.inert"));
        await page.EvaluateAsync("() => LwcWait.finish(save, 'idle')");
        Assert.False(await page.EvaluateAsync<bool>("() => save.hasAttribute('data-lwc-wait')"));
    }

    [Fact]
    public async Task Newsletter_DeadlineIncludesAStalledResponseBody()
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !!window.LwcWait && typeof fetchWithTimeout==='function'");
        await page.Clock.InstallAsync();
        await page.EvaluateAsync("""
            () => window.fetchWithTimeout = async (url, options) => ({
                ok:true, status:200,
                text:()=>new Promise((resolve,reject)=>{
                    window.newsletterBodyStarted=true;
                    options.signal?.addEventListener('abort',()=>{
                        window.newsletterTransferAborted=true;
                        reject(new DOMException('Aborted','AbortError'));
                    },{once:true});
                })
            })
            """);
        await page.Locator("#emailInput").FillAsync("slow@example.test");
        await page.Locator("#newsletter-form button[type=submit]").ClickAsync();
        await page.WaitForFunctionAsync("() => window.newsletterBodyStarted");
        await Assertions.Expect(page.Locator("#newsletter-form")).ToHaveAttributeAsync("aria-busy", "true");
        await page.Clock.RunForAsync(10001);
        await Assertions.Expect(page.Locator("#newsletter-form")).ToHaveAttributeAsync("aria-busy", "false");
        Assert.True(await page.EvaluateAsync<bool>("() => window.newsletterTransferAborted===true"));
        await Assertions.Expect(page.Locator("#newsletter-form button[type=submit]")).ToHaveTextAsync("Retry");
        await Assertions.Expect(page.Locator("#emailInput")).ToHaveValueAsync("slow@example.test");
    }

    [Theory]
    [InlineData(170)]
    [InlineData(600)]
    public async Task Restart_CannotBeClearedByAnEarlierEnding(int restartAt)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        var state = await page.EvaluateAsync<bool>("""
            async restartAt => {
                LwcWait.start(save);
                await new Promise(r => setTimeout(r, 160));
                window.firstEnding = LwcWait.finish(save, 'success');
                await new Promise(r => setTimeout(r, restartAt - 160));
                LwcWait.start(save);
                await new Promise(r => setTimeout(r, 1100));
                return save.hasAttribute('data-lwc-wait') && save.querySelectorAll('.lwc-wait-fill').length === 1;
            }
            """, restartAt);
        Assert.True(state, "The old completion must not restore styles or remove the new wait.");
        Assert.True(await page.EvaluateAsync<bool>("() => Promise.race([firstEnding.then(() => true), new Promise(r => setTimeout(() => r(false), 200))])"));
        await page.EvaluateAsync("() => LwcWait.finish(save, 'idle')");
    }

    [Theory]
    [InlineData(40)]
    [InlineData(470)]
    public async Task RefreshRestart_RetiresEveryOldLine(int restartAt)
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            async restartAt => {
                LwcWait.region(board, true); LwcWait.region(board, false);
                await new Promise(r => setTimeout(r, restartAt));
                LwcWait.region(board, true);
                await new Promise(r => setTimeout(r, 60));
                LwcWait.region(board, false);
                await new Promise(r => setTimeout(r, 1200));
            }
            """, restartAt);
        Assert.Equal(0, await page.Locator(".lwc-wait-line").CountAsync());
        Assert.Null(await page.Locator("#board").GetAttributeAsync("data-lwc-wait-region"));
    }

    [Fact]
    public async Task Refresh_PreservesHiddenChildStateAndUnrelatedArrivals()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("() => LwcWait.region(board, true)");
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(page.Locator("#hidden")).ToHaveCSSAsync("opacity", "0");
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const list = document.createElement('ul');
                document.body.append(list);
                for(let i=0;i<8;i++) { const row=document.createElement('li'); row.textContent='Ready '+i; list.append(row); }
                await new Promise(requestAnimationFrame);
                await new Promise(requestAnimationFrame);
                return [...list.children].every(row => Number(getComputedStyle(row).opacity) === 1 && !row.getAnimations().length);
            }
            """), "An unrelated wait must not hide ready content or replay its entrance.");
    }

    [Fact]
    public async Task RemovedControl_CleansUpBeforeReinsertion()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const control = save;
                LwcWait.start(control, {region: form});
                await new Promise(r => setTimeout(r, 160));
                control.remove();
                await new Promise(r => setTimeout(r, 40));
                form.append(control);
                return !control.hasAttribute('data-lwc-wait') && !control.querySelector('.lwc-wait-fill') && !form.inert;
            }
            """));
    }

    [Fact]
    public async Task BusyRemoval_DoesNotAdoptAStillDisabledControlAgain()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("() => { LwcWait.start(save); save.disabled=true; void LwcWait.finish(save, 'idle'); }");
        await page.WaitForTimeoutAsync(30);
        Assert.Null(await page.Locator("#save").GetAttributeAsync("data-lwc-wait"));
        Assert.True(await page.Locator("#save").IsDisabledAsync());
    }

    [Fact]
    public async Task FastResult_IsImmediateAndNeverShowsAPendingFill()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("() => { LwcWait.start(save); void LwcWait.finish(save,'success'); draft.value='Ready'; }");
        Assert.Equal("Ready", await page.Locator("#draft").InputValueAsync());
        Assert.Equal(0, await page.Locator(".lwc-wait-fill").CountAsync());
        Assert.Null(await page.Locator("#save").GetAttributeAsync("data-lwc-wait"));
    }

    [Fact]
    public async Task ReducedMotion_HasNoRunningWaitAnimationsAndKeepsFocusOutline()
    {
        await using var context = await ContextAsync(ReducedMotion.Reduce);
        var page = await OpenAsync(context);
        await page.Locator("#save").FocusAsync();
        await page.EvaluateAsync("() => { save.style.outline='3px solid blue'; LwcWait.start(save); LwcWait.region(board,true); }");
        await page.WaitForTimeoutAsync(170);
        await page.EvaluateAsync("() => { void LwcWait.finish(save,'error'); LwcWait.region(board,false); }");
        Assert.Equal("3px", await page.Locator("#save").EvaluateAsync<string>("el => getComputedStyle(el).outlineWidth"));
        Assert.Equal("rgb(0, 0, 255)", await page.Locator("#save").EvaluateAsync<string>("el => getComputedStyle(el).outlineColor"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.getAnimations().filter(a => a.playState==='running').length"));
    }

    [Fact]
    public async Task EarlierResponse_CannotFinishANewerOperation()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const first = LwcWait.start(save);
                const second = LwcWait.start(save);
                await first('error');
                if (!save.hasAttribute('data-lwc-wait') || save.hasAttribute('data-lwc-wait-ring')) return false;
                await second('idle');
                return !save.hasAttribute('data-lwc-wait');
            }
            """));
    }

    [Fact]
    public async Task ReinitializationAndPageDisposal_DoNotDuplicateOrStrandWaits()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.AddScriptTagAsync(new() { Url = "/js/motion.js" });
        await page.EvaluateAsync("() => { save.setAttribute('aria-busy','true'); }");
        await page.WaitForTimeoutAsync(170);
        Assert.Equal(1, await page.Locator(".lwc-wait-fill").CountAsync());
        await page.EvaluateAsync("() => { window.dispatchEvent(new PageTransitionEvent('pagehide')); save.removeAttribute('aria-busy'); window.dispatchEvent(new PageTransitionEvent('pageshow', {persisted:true})); }");
        Assert.Equal(0, await page.Locator(".lwc-wait-fill").CountAsync());
        await page.EvaluateAsync("() => save.setAttribute('aria-busy','true')");
        await page.WaitForTimeoutAsync(170);
        Assert.Equal(1, await page.Locator(".lwc-wait-fill").CountAsync());
        await page.EvaluateAsync("() => save.removeAttribute('aria-busy')");
        await Assertions.Expect(page.Locator(".lwc-wait-fill")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task BackgroundSave_ReducedMotionAndAbortCleanUpWithoutOwningOtherRequests()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.backgroundResult = LwcWait.optimistic(() => () => draft.value='Rolled back',
                    () => new Promise((resolve,reject) => window.abortSave=()=>reject(new DOMException('Canceled','AbortError')))).catch(()=>{});
            }
            """);
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.lwcSync==='on'");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        Assert.Equal(0, await page.EvaluateAsync<int>("() => document.getAnimations().filter(a => a.playState==='running').length"));
        await page.EvaluateAsync("() => { abortSave(); return backgroundResult; }");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-lwc-sync')");
        Assert.Equal("Rolled back", await page.Locator("#draft").InputValueAsync());
    }

    [Fact]
    public async Task PendingSave_UsesTheBackgroundIndicatorWhenItsControlScrollsAway()
    {
        await using var context = await ContextAsync();
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                document.body.style.height='2400px';
                window.pendingSave=LwcWait.optimistic(()=>{},()=>new Promise(resolve=>window.resolveSave=resolve),save);
            }
            """);
        await page.WaitForTimeoutAsync(180);
        Assert.Null(await page.Locator("html").GetAttributeAsync("data-lwc-sync"));
        await page.EvaluateAsync("() => scrollTo(0,1400)");
        await page.WaitForFunctionAsync("() => document.documentElement.dataset.lwcSync==='on'");
        await page.EvaluateAsync("() => { resolveSave(); return pendingSave; }");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-lwc-sync')");
        await Assertions.Expect(page.Locator(".lwc-wait-fill")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(200, "Subscription successful.", "success")]
    [InlineData(400, "This email is already subscribed.", "success")]
    [InlineData(503, "Unavailable.", "error")]
    [InlineData(0, "", "error")]
    public async Task Newsletter_UsesItsOwnApplicationOutcomeAndPreservesNewInput(int status, string body, string outcome)
    {
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.NoPreference
        });
        // Every write is intercepted; the actual newsletter handler is exercised.
        await context.RouteAsync("**/api/**", route => route.Request.Method == "GET"
            ? route.FallbackAsync() : route.FulfillAsync(new() { Status = 503, Body = "Unrelated failure" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !!window.LwcWait && typeof fetchWithTimeout==='function'");
        await page.EvaluateAsync("""
            () => window.fetchWithTimeout = () => new Promise((resolve,reject) => {
                window.releaseNewsletter = ({status,body}) => status
                    ? resolve(new Response(body,{status,headers:{'content-type':'text/plain'}}))
                    : reject(new DOMException('Timed out','AbortError'));
            })
            """);
        var button = page.Locator("#newsletter-form button[type=submit]");
        await page.Locator("#emailInput").FillAsync("first@example.test");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Subscribe");
        await Assertions.Expect(button).ToHaveAttributeAsync("data-lwc-wait", new System.Text.RegularExpressions.Regex("^(pending|shown)$"));
        await page.Locator("#emailInput").FillAsync("newer@example.test");
        await page.EvaluateAsync("() => fetch('/api/unrelated', {method:'POST'}).catch(()=>{})");
        await page.EvaluateAsync("value => releaseNewsletter(value)", new { status, body });
        await Assertions.Expect(button).ToHaveAttributeAsync("data-lwc-wait-end", outcome);
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#emailInput")).ToHaveValueAsync("newer@example.test");
        await Assertions.Expect(page.Locator("#emailInput")).ToBeFocusedAsync();
    }
}
