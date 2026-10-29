using Microsoft.Playwright;
using System.Text.Json.Nodes;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class TouchInteractionBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync(bool reduced = false)
    {
        var context = await Browser.NewContextAsync(new() {
            ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true,
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/*", route => route.Request.Method is "GET" or "HEAD"
            ? route.FallbackAsync() : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        return context;
    }

    private async Task<IPage> FixtureAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(App.BaseAddress, "/health").ToString());
        await page.SetContentAsync("""
            <style>
            body{margin:0}main{height:1600px}#nested{height:180px;overflow-y:auto;margin:80px 20px}
            #nested div{height:800px;background:linear-gradient(white,gray)}
            #board{height:200px;background:#ddd} #hover:hover{color:rgb(255,0,0)}
            #overlay{position:fixed;inset:0;background:#0004}#panel{height:400px;background:white}
            </style>
            <main id="main"><div id="board">Swipe</div><div id="nested"><div>Scroll</div></div>
            <button id="hover">Hover</button><input id="range" type="range"><canvas></canvas></main>
            <div id="overlay" hidden><div id="panel">Sheet</div></div>
            """);
        await page.AddStyleTagAsync(new() { Url = new Uri(App.BaseAddress, "/css/touch.css").ToString() });
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/touch.js").ToString() });
        await page.EvaluateAsync("""
            () => {
                window.refreshes = 0; window.steps = 0; window.dismisses = 0;
                const main = document.getElementById('main');
                main.addEventListener('lwc:pull-refresh', event => { window.refreshes++; event.detail.waitUntil(Promise.resolve()); });
                LwcTouch.pull(main);
                LwcTouch.views(document.getElementById('board'), {
                    items: () => [document.createElement('button'), document.createElement('button')],
                    current: () => 0, activate: () => window.steps++
                });
                LwcTouch.sheet(document.getElementById('overlay'), {
                    panel: () => document.getElementById('panel'), isOpen: () => true,
                    dismiss: () => window.dismisses++
                });
            }
            """);
        return page;
    }

    private static async Task DragAsync(IPage page, int x, int y, int dx, int dy, string ending = "touchEnd")
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchStart", ["touchPoints"] = new[] { new { x, y, id = 1 } } });
        for (var i = 1; i <= 8; i++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchMove", ["touchPoints"] = new[] { new { x = x + dx * i / 8, y = y + dy * i / 8, id = 1 } } });
            await page.WaitForTimeoutAsync(20);
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = ending, ["touchPoints"] = Array.Empty<object>() });
        await cdp.DetachAsync();
        await page.WaitForTimeoutAsync(350);
    }

    [Fact]
    public async Task DialogUpdates_DoNotMeasureTheInertBackgroundForTouchTargets()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.EvaluateAsync("""
            async () => {
                const background = document.getElementById('main');
                for (let i = 0; i < 120; i++) {
                    const button = document.createElement('button');
                    button.textContent = `Background action ${i}`;
                    background.append(button);
                }
                background.inert = true;
                document.getElementById('overlay').hidden = false;
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                window.inertGeometryReads = 0;
                const original = Element.prototype.getBoundingClientRect;
                Element.prototype.getBoundingClientRect = function (...args) {
                    if (this.closest('[inert]')) window.inertGeometryReads++;
                    return original.apply(this, args);
                };
                document.getElementById('panel').innerHTML = '<button id="dialogAction">Dialog action</button>';
                window.dialogActions = 0;
                document.getElementById('dialogAction').onclick = () => window.dialogActions++;
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                Element.prototype.getBoundingClientRect = original;
            }
            """);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.inertGeometryReads"));
        await page.Locator("#dialogAction").TapAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.dialogActions"));
    }

    [Fact]
    public async Task Pull_RejectsControlsAndDialogContentBeforeMeasuringScrollContainers()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        var reads = await page.EvaluateAsync<int[]>("""
            () => {
                const modal = document.createElement('div');
                modal.setAttribute('role', 'dialog'); modal.setAttribute('aria-modal', 'true');
                modal.innerHTML = '<span id="dialogText">Dialog content</span>';
                document.getElementById('main').append(modal);
                let reads = 0;
                const originals = new Map();
                for (const name of ['scrollHeight', 'clientHeight', 'scrollWidth', 'clientWidth']) {
                    const descriptor = Object.getOwnPropertyDescriptor(Element.prototype, name);
                    originals.set(name, descriptor);
                    Object.defineProperty(Element.prototype, name, {configurable:true, get() { reads++; return descriptor.get.call(this); }});
                }
                try {
                    return ['hover', 'dialogText'].map(id => {
                        reads = 0;
                        const target = document.getElementById(id);
                        target.dispatchEvent(new TouchEvent('touchstart', {
                            bubbles: true, touches: [new Touch({identifier:1, target, clientX:20, clientY:20})]
                        }));
                        return reads;
                    });
                } finally {
                    for (const [name, descriptor] of originals) Object.defineProperty(Element.prototype, name, descriptor);
                }
            }
            """);
        Assert.Equal(new[] { 0, 0 }, reads);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.refreshes"));
    }

    [Fact]
    public async Task Desktop_DefersAutomaticTouchWorkUntilTouchBecomesAvailable()
    {
        await using var context = await Browser.NewContextAsync(new() { ViewportSize = new() { Width = 1440, Height = 900 }, HasTouch = false });
        // Chromium can still report a physical Windows touchscreen when touch
        // emulation is disabled. Make this fixture explicitly mouse-only.
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'maxTouchPoints', { configurable: true, value: 0 });");
        var page = await FixtureAsync(context);
        await page.EvaluateAsync("""
            async () => {
                const strip = document.createElement('div');
                strip.className = 'view-switcher-chips';
                strip.style.cssText = 'width:120px;overflow:auto';
                strip.innerHTML = '<div style="width:400px;height:40px">Choices</div>';
                document.body.append(strip);
                LwcTouch.refresh();
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);
        var strip = page.Locator(".view-switcher-chips");
        Assert.Null(await strip.GetAttributeAsync("data-lwc-scroller"));
        await page.EvaluateAsync("() => delete navigator.maxTouchPoints");
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { ["enabled"] = true, ["maxTouchPoints"] = 1 });
        await Expect(strip).ToHaveAttributeAsync("data-lwc-scroller", "");
        await Expect(strip).ToHaveAttributeAsync("data-lwc-fade", "end");
        await cdp.DetachAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(180)]
    public async Task NestedScroll_AlwaysKeepsItsGesture(int scrollTop)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.Locator("#nested").EvaluateAsync("(e, top) => e.scrollTop = top", scrollTop);
        await DragAsync(page, 180, 335, 0, 130);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.refreshes"));
        if (scrollTop > 0) Assert.True(await page.Locator("#nested").EvaluateAsync<int>("e => e.scrollTop") < scrollTop);
    }

    [Theory]
    [InlineData("touchCancel")]
    [InlineData("touchEnd")]
    public async Task Pull_OnlyCompletedSingleTouchRefreshes(string ending)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await DragAsync(page, 180, 60, 0, 160, ending);
        Assert.Equal(ending == "touchEnd" ? 1 : 0, await page.EvaluateAsync<int>("() => window.refreshes"));
    }

    [Fact]
    public async Task Hover_RestoresOnHybridInput()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        Assert.Equal("touch", await page.Locator("html").GetAttributeAsync("data-lwc-touch"));
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Emulation.setTouchEmulationEnabled", new Dictionary<string, object> { ["enabled"] = false });
        await Expect(page.Locator("html")).Not.ToHaveAttributeAsync("data-lwc-touch", "touch");
        await page.Locator("#hover").HoverAsync();
        await Expect(page.Locator("#hover")).ToHaveCSSAsync("color", "rgb(255, 0, 0)");
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task Hover_ProcessesLateRulesOnTouch()
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.EvaluateAsync("() => { document.styleSheets[0].insertRule('#hover:hover { background: rgb(255,0,0) }'); LwcTouch.refresh(); }");
        await page.Locator("#hover").TapAsync();
        await Expect(page.Locator("html")).ToHaveAttributeAsync("data-lwc-touch", "touch");
        await Expect(page.Locator("#hover")).Not.ToHaveCSSAsync("background-color", "rgb(255, 0, 0)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Views_SwipeWithoutVerticalTravelOrCompetingPull(bool reduced)
    {
        await using var context = await ContextAsync(reduced);
        var page = await FixtureAsync(context);
        await DragAsync(page, 300, 100, -180, 8);
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.steps"));
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.refreshes"));
        Assert.Equal("", await page.Locator("#board").EvaluateAsync<string>("e => e.style.translate"));
    }

    [Theory]
    [InlineData("second-touch")]
    [InlineData("pointercancel")]
    [InlineData("lostpointercapture")]
    [InlineData("blur")]
    public async Task InterruptedSheet_NeverDismissesOrStaysDisplaced(string interruption)
    {
        await using var context = await ContextAsync();
        var page = await FixtureAsync(context);
        await page.EvaluateAsync("""
            interruption => {
                const panel = document.getElementById('panel'); document.getElementById('overlay').hidden = false;
                const send = (type, extra = {}) => panel.dispatchEvent(new PointerEvent(type, {
                    bubbles:true, pointerType:'touch', pointerId:17, isPrimary:true, clientX:180, clientY:100, ...extra
                }));
                send('pointerdown'); send('pointermove', {clientY:300});
                if (interruption === 'blur') window.dispatchEvent(new Event('blur'));
                else if (interruption === 'second-touch') document.dispatchEvent(new TouchEvent('touchstart', {touches:[new Touch({identifier:1,target:panel}),new Touch({identifier:2,target:panel})]}));
                else send(interruption);
                send('pointerup', {clientY:300});
            }
            """, interruption);
        Assert.Equal(0, await page.EvaluateAsync<int>("() => window.dismisses"));
        Assert.Equal("", await page.Locator("#panel").EvaluateAsync<string>("e => e.style.translate"));
    }

    [Fact]
    public async Task BoardRefresh_FetchesFreshDataAndPreservesCurrentSelection()
    {
        await using var context = await ContextAsync(true);
        var requests = 0;
        using var client = App.CreateClient();
        var data = await client.GetStringAsync("/api/data/athletes");
        await context.RouteAsync("**/api/data/athletes", route => {
            requests++;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = data });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(App.BaseAddress, "/leaderboard?view=pheno").ToString());
        await Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.Locator(".sidebar input[name=division]").First.EvaluateAsync("e => e.click()");
        var state = "() => JSON.stringify({view:document.querySelector('input[name=leaderboardView]:checked').value, filters:[...document.querySelectorAll('.sidebar input:checked')].map(e=>[e.name,e.value]), rows:[...document.querySelectorAll('.leaderboard tr[data-athlete-name]')].filter(e=>e.getClientRects().length).map(e=>[e.dataset.athleteName,e.querySelector('.rank')?.textContent]),url:location.href})";
        var before = await page.EvaluateAsync<string>(state);
        Assert.True(await page.Locator(".sidebar input[name=division]:checked").CountAsync() > 0);
        var initialRequests = requests;
        await page.EvaluateAsync("""
            async () => {
                const work=[];
                document.querySelector('main[data-leaderboard-page=full]').dispatchEvent(new CustomEvent('lwc:pull-refresh', {
                    detail:{waitUntil:p=>work.push(p)}
                }));
                await Promise.all(work);
            }
            """);
        Assert.Equal(initialRequests + 1, requests);
        Assert.Equal(before, await page.EvaluateAsync<string>(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoardRefresh_IgnoresLateInitialSuccessOrFailure(bool oldFails)
    {
        await using var context = await ContextAsync(true);
        using var client = App.CreateClient();
        var original = await client.GetStringAsync("/api/data/athletes");
        var fresh = JsonNode.Parse(original)!.AsArray();
        fresh[0]!["Name"] = "Refresh Race Marker";
        if (fresh[0]!["DisplayName"] is not null) fresh[0]!["DisplayName"] = "Refresh Race Marker";
        var pending = new TaskCompletionSource<IRoute>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await context.RouteAsync("**/api/data/athletes", async route => {
            if (Interlocked.Increment(ref requests) == 1) pending.SetResult(route);
            else await route.FulfillAsync(new() { ContentType = "application/json", Body = fresh.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(App.BaseAddress, "/leaderboard").ToString(), new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("main[data-lwc-refresh-ready]").WaitForAsync();
        await page.EvaluateAsync("""
            async () => {
                window.originalAthleteRequest = window.__sharedAthletesRequest;
                const work=[];
                document.querySelector('main[data-lwc-refresh-ready]').dispatchEvent(new CustomEvent('lwc:pull-refresh', {
                    detail:{waitUntil:p=>work.push(p)}
                }));
                await Promise.all(work);
            }
            """);
        var marker = page.Locator(".leaderboard tr[data-athlete-name='Refresh Race Marker']");
        await Expect(marker).ToHaveCountAsync(1);
        await (await pending.Task).FulfillAsync(new() { Status = oldFails ? 400 : 200, ContentType = "application/json", Body = oldFails ? "{}" : original });
        await page.EvaluateAsync("async () => { await originalAthleteRequest.catch(() => {}); await new Promise(requestAnimationFrame); }");
        await Expect(marker).ToHaveCountAsync(1);
        await Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
    }
}
