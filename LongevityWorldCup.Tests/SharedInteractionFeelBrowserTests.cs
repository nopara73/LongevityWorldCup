using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class SharedInteractionFeelBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> ContextAsync(int width = 1280, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            HasTouch = width < 600,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.NoPreference
        });
        await context.RouteAsync("**/*", route => route.Request.Method is "GET" or "HEAD"
            ? route.FallbackAsync()
            : route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" }));
        return context;
    }

    private static async Task ReadyAsync(IPage page)
    {
        await page.WaitForFunctionAsync("""
            () => window.playAthleteFlow && !document.documentElement.hasAttribute('data-initial-view')
                && !document.body.classList.contains('play-route-hydrating')
            """);
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);
    }

    [Theory]
    [InlineData(1280, false)]
    [InlineData(1280, true)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    public async Task PlaySelection_ControlsRemainReadableFromFirstArrivalFrame(int width, bool dark)
    {
        await using var context = await ContextAsync(width, dark);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.selectionFrames = [];
                document.getElementById('continueGameBtn').addEventListener('click', () => {
                    const start = performance.now();
                    const sample = () => {
                        const input = document.getElementById('playAthleteInput');
                        let opacity = 1;
                        for (let node = input; node; node = node.parentElement) opacity *= Number(getComputedStyle(node).opacity);
                        window.selectionFrames.push([opacity, input.getBoundingClientRect().width]);
                        if (performance.now() - start < 350) requestAnimationFrame(sample);
                        else window.selectionDone = true;
                    };
                    requestAnimationFrame(sample);
                }, {once:true});
            }
            """);
        await page.Locator("#continueGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.selectionDone");
        var frames = await page.EvaluateAsync<double[][]>("() => window.selectionFrames");
        Assert.True(frames.Length > 1);
        Assert.All(frames, frame =>
        {
            Assert.True(frame[0] >= .99, $"The live selection input faded to {frame[0]:F3} during arrival.");
            Assert.True(frame[1] > 0);
        });
        await page.Locator("#playAthleteInput").FillAsync("a");
        await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayReducedMotionChange_ReleasesInterruptedMotionAndDockWithoutReopeningOldPanel()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await ReadyAsync(page);
        // Pause in the real event task to make the interruption deterministic,
        // including a backgrounded tab whose animation clock has stopped.
        await page.EvaluateAsync("""
            () => document.getElementById('newGameBtn').addEventListener('click', () => {
                for (const animation of document.getElementById('joinTrackPanel').getAnimations({subtree:true})) animation.pause();
            }, {once:true})
            """);
        await page.Locator("#newGameBtn").ClickAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.True(await page.EvaluateAsync<bool>("""
            () => !document.documentElement.hasAttribute('data-flow-dock-hold')
                && !document.documentElement.classList.contains('play-panel-transitioning')
                && !document.querySelector('.play-hub-panel--ghost')
                && document.getElementById('joinTrackPanel').getAnimations({subtree:true})
                    .every(animation => ['finished','idle'].includes(animation.playState))
            """));
        await page.Locator("#joinTrackBackBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeVisibleAsync();
        await page.WaitForTimeoutAsync(450);
        await Assertions.Expect(page.Locator("#joinTrackPanel")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(390)]
    public async Task PlayJoin_ChoicesAndBackRemainReadableDuringArrival(int width)
    {
        await using var context = await ContextAsync(width);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await ReadyAsync(page);
        await page.EvaluateAsync("""
            () => document.getElementById('newGameBtn').addEventListener('click', () => {
                window.joinFrames = [];
                const start = performance.now();
                const sample = () => {
                    joinFrames.push([...document.querySelectorAll('.play-join-card, .play-join-foot')].map(element => {
                        let opacity = 1;
                        for (let node = element; node; node = node.parentElement) opacity *= Number(getComputedStyle(node).opacity);
                        return opacity;
                    }));
                    if (performance.now() - start < 500) requestAnimationFrame(sample);
                    else window.joinDone = true;
                };
                requestAnimationFrame(sample);
            }, {once:true})
            """);
        await page.Locator("#newGameBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.joinDone");
        var frames = await page.EvaluateAsync<double[][]>("() => window.joinFrames");
        Assert.True(frames.Length > 1);
        Assert.All(frames.SelectMany(frame => frame), opacity => Assert.True(opacity >= .99,
            $"A live Join choice or Back action was faded to {opacity:F3} during arrival."));
        await page.Locator("#joinTrackBackBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#playStartPanel")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayPanelArrival_NeverSuperimposesTwoIdentityHeaders()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        await ReadyAsync(page);
        await page.Locator("#playAthleteInput").FillAsync("Ron Lugbill");
        await Assertions.Expect(page.Locator("#playAthleteInput-autocomplete-list [role=option]").First).ToBeVisibleAsync();
        await page.Locator("#playAthleteInput").PressAsync("Enter");
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.identityFrames = [];
                document.getElementById('playConfirmAthleteBtn').addEventListener('click', () => {
                    const start = performance.now();
                    const sample = () => {
                        const titles = [...document.querySelectorAll('.play-hub-main h1')].filter(title => {
                            if (!title.getClientRects().length) return false;
                            let opacity = 1;
                            for (let node = title; node; node = node.parentElement) opacity *= Number(getComputedStyle(node).opacity);
                            return opacity > .05;
                        });
                        identityFrames.push(titles.map(title => title.textContent));
                        if (performance.now() - start < 500) requestAnimationFrame(sample);
                        else window.identityDone = true;
                    };
                    requestAnimationFrame(sample);
                }, {once:true});
            }
            """);
        await page.Locator("#playConfirmAthleteBtn").ClickAsync();
        await page.WaitForFunctionAsync("() => window.identityDone");
        var frames = await page.EvaluateAsync<string[][]>("() => window.identityFrames");
        Assert.True(frames.Length > 1);
        Assert.All(frames, titles => Assert.Single(titles));
    }

    [Fact]
    public async Task DecodedPortrait_ReadinessDoesNotWaitForHiddenPanelPaints()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/play");
        await ReadyAsync(page);
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const image = new Image();
                image.src = '/assets/favicon-512x512.png';
                await image.decode();
                const frame = document.createElement('div');
                frame.hidden = true;
                document.body.append(frame);
                const original = window.requestAnimationFrame;
                const pending = [];
                window.requestAnimationFrame = callback => { pending.push(callback); return pending.length; };
                try {
                    return await Promise.race([
                        playAthleteFlow.replaceAthletePictureImmediately(frame, image, image.src)
                            .then(() => frame.firstElementChild === image && image.complete && image.naturalWidth > 0),
                        new Promise(resolve => setTimeout(() => resolve(false), 150))
                    ]);
                } finally {
                    window.requestAnimationFrame = original;
                    for (const callback of pending) original(callback);
                    frame.remove();
                }
            }
            """), "An already decoded portrait must make its hidden panel ready without waiting for animation frames.");
    }

    [Fact]
    public async Task SharedFlip_BatchesLayoutForMovedRows()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/health");
        await page.SetContentAsync("<style>body{margin:0}#rows>div{height:18px}</style><div id=rows>"
            + string.Concat(Enumerable.Range(0, 40).Select(i => $"<div>Row {i}</div>")) + "</div>");
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/motion.js").ToString() });
        await page.Mouse.ClickAsync(1000, 800);
        var cdp = await context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Performance.enable");
        static double Metric(JsonElement? value, string name) => value!.Value.GetProperty("metrics")
            .EnumerateArray().Single(metric => metric.GetProperty("name").GetString() == name).GetProperty("value").GetDouble();
        var before = await cdp.SendAsync("Performance.getMetrics");
        var animated = await page.EvaluateAsync<bool>("""
            () => {
                const list = document.getElementById('rows');
                LwcMotion.flip([...list.children], () => list.replaceChildren(...[...list.children].reverse()));
                return document.getAnimations().length > 0;
            }
            """);
        var after = await cdp.SendAsync("Performance.getMetrics");
        var layouts = Metric(after, "LayoutCount") - Metric(before, "LayoutCount");
        Assert.True(layouts <= 3, $"One row reorder forced {layouts} layouts instead of measuring the final positions together.");
        Assert.Equal("Row 39", await page.Locator("#rows > div").First.TextContentAsync());
        Assert.True(animated);
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task FirstMotionPress_DoesNotRestyleUnrelatedDocumentContent()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/health");
        await page.SetContentAsync("<button id=press>Enable motion</button><details><summary>Archive</summary><p>Contents</p></details><main>"
            + string.Concat(Enumerable.Range(0, 3000).Select(i => $"<div><span>Unrelated content {i}</span></div>")) + "</main>");
        await page.AddStyleTagAsync(new() { Url = new Uri(App.BaseAddress, "/css/motion.css").ToString() });
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/motion.js").ToString() });
        await page.EvaluateAsync("() => document.body.getBoundingClientRect().height");
        var cdp = await context.NewCDPSessionAsync(page);
        var restyledCounts = new List<int>();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        cdp.Event("Tracing.dataCollected").OnEvent += (_, value) =>
        {
            foreach (var entry in value!.Value.GetProperty("value").EnumerateArray())
                if (entry.GetProperty("name").GetString() == "UpdateLayoutTree"
                    && entry.GetProperty("args").TryGetProperty("elementCount", out var count))
                    restyledCounts.Add(count.GetInt32());
        };
        cdp.Event("Tracing.tracingComplete").OnEvent += (_, _) => complete.TrySetResult();
        await cdp.SendAsync("Tracing.start", new() { ["categories"] = "devtools.timeline", ["transferMode"] = "ReportEvents" });
        await page.Locator("#press").ClickAsync();
        await page.EvaluateAsync("() => document.body.getBoundingClientRect().height");
        await cdp.SendAsync("Tracing.end");
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await page.EvaluateAsync<bool>("() => LwcMotion.enabled()"));
        Assert.NotEmpty(restyledCounts);
        Assert.True(restyledCounts.Max() < 100,
            $"Enabling motion restyled {restyledCounts.Max()} elements, including the unrelated 6,000-node content.");
        await cdp.DetachAsync();
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task CalculatorDock_DoesNotHideOrBlockCommandsWhenItArrives(string route)
    {
        await using var context = await ContextAsync(390);
        await context.AddInitScriptAsync("""
            (() => {
                const observer = new MutationObserver(() => {
                    const stack = document.querySelector('.flow-action-stack--dock-entering');
                    if (!stack) return;
                    const style = getComputedStyle(stack);
                    window.firstDockState = [Number(style.opacity), style.pointerEvents === 'none' ? 0 : 1];
                    observer.disconnect();
                });
                observer.observe(document, {subtree:true, childList:true, attributes:true, attributeFilter:['class']});
            })();
            """);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1280, 1600);
        await page.GotoAsync(route);
        await page.WaitForFunctionAsync("""
            () => document.documentElement.classList.contains('flow-action-dock-ready')
                && !document.documentElement.hasAttribute('data-initial-view')
                && document.getElementById('lwcStepOneActions')
                && !document.getElementById('lwcStepOneActions').classList.contains('flow-action-stack--docked')
            """);
        // Initial layout intentionally does not animate. A real input followed
        // by a smaller viewport exercises the live docking transition.
        await page.Keyboard.PressAsync("Escape");
        await page.SetViewportSizeAsync(390, 844);
        await page.WaitForFunctionAsync("() => window.firstDockState");
        Assert.Equal(new double[] { 1, 1 }, await page.EvaluateAsync<double[]>("() => window.firstDockState"));
        // The visible command still runs the existing validation gate.
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await Assertions.Expect(page.Locator("#lwc-step-1")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lwc-step-2")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task SheetReturn_IsCancelledByReducedMotionAndRegrabbing()
    {
        await using var context = await ContextAsync(390);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/health");
        await page.SetContentAsync("<style>body{margin:0}#overlay{position:fixed;inset:0}#panel{height:600px;background:#eee}</style><div id=overlay><div id=panel>Sheet</div></div>");
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/touch.js").ToString() });
        await page.EvaluateAsync("""
            () => {
                const overlay = document.getElementById('overlay'), panel = document.getElementById('panel');
                LwcTouch.sheet(overlay, {panel:()=>panel, isOpen:()=>true, dismiss:()=>{}});
                window.dragSheet = (type, y) => panel.dispatchEvent(new PointerEvent(type, {
                    bubbles:true, pointerType:'touch', isPrimary:true, pointerId:22, clientX:150, clientY:y
                }));
                dragSheet('pointerdown',100); dragSheet('pointermove',140); dragSheet('pointerup',140);
                for (const animation of panel.getAnimations()) animation.pause();
            }
            """);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.True(await page.Locator("#panel").EvaluateAsync<bool>("panel => panel.getAnimations().every(a => ['idle','finished'].includes(a.playState))"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                const panel = document.getElementById('panel');
                dragSheet('pointerdown',100); dragSheet('pointermove',140); dragSheet('pointerup',140);
                const before = parseFloat(getComputedStyle(panel).translate.split(/\s+/)[1]) || 0;
                dragSheet('pointerdown',100); dragSheet('pointermove',130);
                const after = parseFloat(getComputedStyle(panel).translate.split(/\s+/)[1]) || 0;
                const directlyTracksFinger = panel.getAnimations().length === 0 && Math.abs(after - before - 30) < 1;
                dragSheet('pointercancel',130);
                return directlyTracksFinger;
            }
            """));
    }
}
