using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

// One interaction motion system (motion.css + motion.ts): overlays, menus,
// toasts and accordions open and close on shared tokens, presses give way,
// reordered rows glide, and reduced motion keeps everything instant.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class InteractionMotionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    // Running animations and transitions on an element (or its pseudo-elements), as "name/property".
    private const string RunningMotionScript = """
        selector => document.getAnimations()
            .filter(animation => animation.playState === 'running')
            .filter(animation => {
                const target = animation.effect?.target;
                return target && target.matches(selector);
            })
            .map(animation => (animation.transitionProperty || animation.animationName || 'script')
                + (animation.effect.pseudoElement || ''))
        """;

    // Capture short transitions in the same browser event task as the application
    // handler. A protocol round trip after ClickAsync can outlast a 140ms exit.
    private static async Task<string[]> CaptureEventMotionAsync(
        ILocator trigger, string eventName, string selector, Func<Task> action)
    {
        await trigger.EvaluateAsync($$"""
            (target, args) => target.addEventListener(args.eventName, () => {
                window.__lwcEventMotion = ({{RunningMotionScript}})(args.selector);
            }, { once: true })
            """, new { eventName, selector });
        await action();
        return await trigger.EvaluateAsync<string[]>("() => window.__lwcEventMotion");
    }

    // CLS excludes expected shifts within 500ms of a discrete user input.
    // The in-flow Order strip deliberately makes room beneath the toolbar.
    private const string WatchLayoutShiftScript = """
        () => {
            window.__lwcShift = 0;
            window.__lwcShiftEntries = [];
            new PerformanceObserver(list => {
                for (const entry of list.getEntries()) {
                    if (!entry.hadRecentInput) {
                        window.__lwcShift += entry.value;
                        window.__lwcShiftEntries.push({ value: entry.value, at: entry.startTime,
                            sources: entry.sources.map(source => ({ node: source.node?.outerHTML?.slice(0, 250),
                                previous: source.previousRect, current: source.currentRect })) });
                    }
                }
            }).observe({ type: 'layout-shift' });
        }
        """;

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/play")]
    [InlineData("/longevitymaxxing")]
    [InlineData("/about")]
    public async Task MotionFoundation_LoadsOnEveryPageWithSharedTokens(string path)
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);

        var foundation = await page.EvaluateAsync<string[]>("""
            () => {
                const links = [...document.querySelectorAll('link[rel=stylesheet]')].map(link => link.href);
                const motion = links.findIndex(href => href.includes('/css/motion.css'));
                const aesthetic = links.findIndex(href => href.includes('/css/aesthetic-system.css'));
                const root = getComputedStyle(document.documentElement);
                return [
                    String(motion >= 0 && motion < aesthetic),
                    typeof window.LwcMotion?.flip,
                    root.getPropertyValue('--lwc-motion-press').trim(),
                    root.getPropertyValue('--lwc-motion-base').trim(),
                    root.getPropertyValue('--lwc-motion-slow').trim(),
                    root.getPropertyValue('--lwc-ease-enter').trim(),
                    root.getPropertyValue('--lwc-ease-exit').trim()
                ];
            }
            """);

        Assert.Equal(["true", "function", "90ms", "200ms", "280ms", "cubic-bezier(0, 0, 0, 1)", "cubic-bezier(0.3, 0, 1, 1)"], foundation);
    }

    [Fact]
    public async Task Motion_StartsOnlyAfterTheFirstPress()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");

        Assert.Null(await page.EvaluateAsync<string?>("() => document.documentElement.dataset.lwcMotion ?? null"));
        await page.Mouse.ClickAsync(4, 400);
        Assert.Equal("on", await page.EvaluateAsync<string>("() => document.documentElement.dataset.lwcMotion"));
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task Alert_FadesInAndSinksAwayWithoutCatchingClicksOrShiftingLayout(int width, int height)
    {
        await using var context = await NewMotionContextAsync(width, height);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync(WatchLayoutShiftScript);

        var opening = await page.EvaluateAsync<string[]>($$"""
            () => { customAlert('Saved'); return ({{RunningMotionScript}})('#custom-alert, #custom-alert *'); }
            """);
        Assert.Contains("opacity", opening);

        await page.WaitForTimeoutAsync(400);
        var close = page.Locator("#custom-alert-close");
        await close.EvaluateAsync("""
            button => button.addEventListener('click', () => {
                const alert = document.getElementById('custom-alert');
                const panel = alert.querySelector('.custom-alert-panel').getBoundingClientRect();
                const hit = document.elementFromPoint(panel.left + panel.width / 2, panel.top + panel.height / 2);
                window.__lwcAlertClosing = [
                    String(alert.hidden),
                    getComputedStyle(alert).display,
                    getComputedStyle(alert).pointerEvents,
                    String(alert.contains(hit))
                ];
            }, { once: true })
            """);
        var exiting = await CaptureEventMotionAsync(close, "click", "#custom-alert, #custom-alert *", () => close.ClickAsync());
        var closing = await page.EvaluateAsync<string[]>("() => window.__lwcAlertClosing");
        Assert.Equal("true", closing[0]);
        Assert.NotEqual("none", closing[1]);
        Assert.Equal("none", closing[2]);
        Assert.Equal("false", closing[3]);
        Assert.Contains("opacity", exiting);
        Assert.Contains(exiting, motion => motion is "translate" or "scale");

        await Assertions.Expect(page.Locator("#custom-alert")).ToHaveCSSAsync("display", "none");
        Assert.True(await page.EvaluateAsync<double>("() => window.__lwcShift") < 0.001,
            await page.EvaluateAsync<string>("() => JSON.stringify(window.__lwcShiftEntries)"));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task LoadingDialog_FadesOutInsteadOfVanishing()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);

        var opening = await page.EvaluateAsync<string[]>($$"""
            () => { showLoading(); return ({{RunningMotionScript}})('#loading-dialog'); }
            """);
        Assert.Contains("opacity", opening);
        await WaitForFiniteAnimationsAsync(page);
        var exiting = await page.EvaluateAsync<string[]>($$"""
            () => { hideLoading(); return ({{RunningMotionScript}})('#loading-dialog, #loading-dialog *'); }
            """);
        Assert.Contains("opacity", exiting);
        Assert.Contains(exiting, motion => motion is "translate" or "scale");
        await Assertions.Expect(page.Locator("#loading-dialog")).ToHaveCSSAsync("display", "none");
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task OrderPanel_GrowsFromItsButtonAndFoldsBack(int width, int height)
    {
        await using var context = await NewMotionContextAsync(width, height);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        var toggle = page.Locator("#leaderboardOrderToggle");
        await Assertions.Expect(toggle).ToBeEnabledAsync();
        await page.EvaluateAsync(WatchLayoutShiftScript);
        var toolbar = page.Locator(".leaderboard-toolbar");
        var toolbarBefore = await toolbar.BoundingBoxAsync();

        var opening = await CaptureEventMotionAsync(toggle, "click", "#leaderboardOrderPanel", () => toggle.ClickAsync());
        Assert.Contains("opacity", opening);
        Assert.Contains("scale", opening);

        await page.WaitForTimeoutAsync(400);
        var exiting = await CaptureEventMotionAsync(toggle, "click", "#leaderboardOrderPanel", () => toggle.ClickAsync());
        Assert.Equal("none", await page.Locator("#leaderboardOrderPanel").EvaluateAsync<string>("panel => getComputedStyle(panel).pointerEvents"));
        Assert.Contains("opacity", exiting);
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToHaveCSSAsync("display", "none");
        Assert.True(await page.EvaluateAsync<double>("() => window.__lwcShift") < 0.001,
            await page.EvaluateAsync<string>("() => JSON.stringify(window.__lwcShiftEntries)"));
        var toolbarAfter = await toolbar.BoundingBoxAsync();
        Assert.NotNull(toolbarBefore);
        Assert.NotNull(toolbarAfter);
        Assert.InRange(Math.Abs(toolbarAfter.X - toolbarBefore.X), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Y - toolbarBefore.Y), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Width - toolbarBefore.Width), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Height - toolbarBefore.Height), 0, 0.5);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task OrderPanel_ClosingReleasesFlowSpaceWhileItsExitStillPaints(int width, int height)
    {
        await using var context = await NewMotionContextAsync(width, height);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var toggle = page.Locator("#leaderboardOrderToggle");
        var board = page.Locator("#athleteLeaderboard");
        var closed = await board.BoundingBoxAsync();
        await toggle.ClickAsync();
        await WaitForFiniteAnimationsAsync(page);
        var open = await board.BoundingBoxAsync();
        Assert.True(open!.Y > closed!.Y + 40);
        await toggle.EvaluateAsync("""
            target => {
                target.addEventListener('click', () => {
                    window.__orderExitBefore = document.getElementById('leaderboardOrderPanel').getBoundingClientRect().toJSON();
                }, {once:true, capture:true});
                target.addEventListener('click', () => {
                    window.__heldOrderExit = document.getElementById('leaderboardOrderPanel').getAnimations();
                    __heldOrderExit.forEach(animation => animation.pause());
                }, {once:true});
            }
            """);
        await toggle.ClickAsync();
        var panel = page.Locator("#leaderboardOrderPanel");
        Assert.NotEqual("none", await panel.EvaluateAsync<string>("e => getComputedStyle(e).display"));
        Assert.Equal("none", await panel.EvaluateAsync<string>("e => getComputedStyle(e).pointerEvents"));
        var frameDelta = await panel.EvaluateAsync<double[]>("e => {const before=__orderExitBefore, after=e.getBoundingClientRect();return ['x','y','width','height'].map(key=>Math.abs(after[key]-before[key]));}");
        Assert.All(frameDelta, difference => Assert.InRange(difference, 0, 1));
        var closing = await board.BoundingBoxAsync();
        Assert.InRange(Math.Abs(closing!.Y - closed.Y), 0, 0.5);
        await page.EvaluateAsync("() => __heldOrderExit.forEach(animation => animation.play())");
        await toggle.ClickAsync();
        await Assertions.Expect(panel).ToBeVisibleAsync();
        Assert.Null(await panel.GetAttributeAsync("data-order-exit"));
        Assert.Equal("static", await panel.EvaluateAsync<string>("e => getComputedStyle(e).position"));
    }

    [Fact]
    public async Task OpeningAnArchive_ImmediatelyAllowsFocusAndHistoryPositioning()
    {
        await using var context = await NewMotionContextAsync(390, 844);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");
        await page.Mouse.ClickAsync(4, 400);
        await page.EvaluateAsync("""
            () => {
                const archive = document.createElement('details');
                archive.id = 'motion-archive-probe';
                archive.innerHTML = '<summary>History</summary><div style="width:200px;overflow:auto"><button style="min-width:800px">Earlier reply</button></div>';
                document.querySelector('main').append(archive);
                getComputedStyle(archive, '::details-content').contentVisibility;
            }
            """);
        var result = await page.EvaluateAsync<bool[]>("""
            () => {
                const archive = document.getElementById('motion-archive-probe');
                archive.open = true;
                const history = archive.querySelector('div');
                const reply = archive.querySelector('button');
                reply.focus({ preventScroll: true });
                history.scrollLeft = history.scrollWidth;
                return [document.activeElement === reply, history.clientWidth > 0, history.scrollLeft > 0];
            }
            """);
        Assert.Equal([true, true, true], result);
    }

    [Theory]
    [InlineData(1280, 800)]
    [InlineData(390, 844)]
    public async Task AthleteProfile_OpensOpaqueAndFadesOut(int width, int height)
    {
        await using var context = await NewMotionContextAsync(width, height);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        // The card stays opaque on entrance so the board cannot show through
        // its identity. Record painted frames and transitions before the click.
        await page.EvaluateAsync("""
            () => {
                window.__backdropMotion = [];
                window.__backdropEntranceOpacity = [];
                window.__backdropPhase = 'enter';
                const backdrop = document.getElementById('detailsModal');
                requestAnimationFrame(function sampleEntrance() {
                    if (window.__backdropPhase !== 'enter') return;
                    if (backdrop.getClientRects().length)
                        window.__backdropEntranceOpacity.push(Number(getComputedStyle(backdrop).opacity));
                    requestAnimationFrame(sampleEntrance);
                });
                // Transition events can be delivered after close cleanup removes
                // fade-out. Keep the direction from the actual close lifecycle.
                backdrop.addEventListener('athlete-dialog-closing', () => window.__backdropPhase = 'exit');
                for (const type of ['transitionrun', 'transitionend']) {
                    backdrop.addEventListener(type, event => {
                        if (event.target !== backdrop || event.propertyName !== 'opacity') return;
                        window.__backdropMotion.push(window.__backdropPhase + ':' + type);
                    });
                }
            }
            """);
        await page.Locator("#athleteLeaderboard tbody tr[data-athlete-name]").First.Locator(".athlete-name").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__backdropEntranceOpacity.length > 0");
        await Assertions.Expect(page.Locator("#detailsModal")).ToHaveCSSAsync("opacity", "1");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var entranceOpacity = await page.EvaluateAsync<double[]>("() => window.__backdropEntranceOpacity");
        Assert.NotEmpty(entranceOpacity);
        Assert.All(entranceOpacity, opacity => Assert.Equal(1, opacity));
        var transitions = await page.EvaluateAsync<string[]>("() => window.__backdropMotion");
        Assert.DoesNotContain("enter:transitionrun", transitions);
        Assert.Contains("exit:transitionrun", transitions);
    }

    [Fact]
    public async Task ReorderedRows_GlideToTheirNewPlace()
    {
        await using var context = await NewMotionContextAsync(1280, 900);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        var toggle = page.Locator("#leaderboardOrderToggle");
        await Assertions.Expect(toggle).ToBeEnabledAsync();
        await toggle.ClickAsync();
        var primary = page.Locator("#leaderboardOrderPrimary");
        var option = await primary.EvaluateAsync<string>("select => [...select.options].map(o => o.value).find(v => v && v !== 'rank')");

        var moving = await CaptureEventMotionAsync(primary, "change", "#athleteLeaderboard tbody tr", async () => { await primary.SelectOptionAsync(option); });
        Assert.NotEmpty(moving);
    }

    // Chromium exposes the native details-content height transition through
    // the inspector, but not getAnimations(). Seek that real effect so a busy
    // renderer cannot skip every intermediate frame in a fixed-time sample.
    private static async Task<int[]> SampleDetailsHeightsAsync(IPage page)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        try
        {
            await cdp.SendAsync("Animation.enable");
            await cdp.SendAsync("Animation.setPlaybackRate", new() { ["playbackRate"] = 0 });
            var started = new TaskCompletionSource<(string Id, double Duration, double Delay)>(TaskCreationOptions.RunContinuationsAsynchronously);
            cdp.Event("Animation.animationStarted").OnEvent += (_, value) =>
            {
                var animation = value!.Value.GetProperty("animation");
                if (animation.GetProperty("name").GetString() is not ("height" or "block-size")) return;
                var source = animation.GetProperty("source");
                started.TrySetResult((animation.GetProperty("id").GetString()!,
                    source.GetProperty("duration").GetDouble(), source.GetProperty("delay").GetDouble()));
            };
            var initial = await page.EvaluateAsync<int>("""
                async () => {
                    const details = [...document.querySelectorAll('.play-join-biomarkers details')].find(d => d.getClientRects().length);
                    window.__lwcAccordion = details;
                    const height = Math.round(details.getBoundingClientRect().height);
                    details.querySelector('summary').click();
                    void details.getBoundingClientRect().height;
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    return height;
                }
                """);
            var effect = await started.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var heights = new List<int> { initial };
            foreach (var progress in new[] { 0, 0.25, 0.5, 0.75, 1 })
            {
                await cdp.SendAsync("Animation.seekAnimations", new()
                {
                    ["animations"] = new[] { effect.Id },
                    ["currentTime"] = effect.Delay + effect.Duration * progress
                });
                heights.Add(await page.EvaluateAsync<int>("Math.round(window.__lwcAccordion.getBoundingClientRect().height)"));
            }
            return heights.ToArray();
        }
        finally
        {
            try { await cdp.SendAsync("Animation.setPlaybackRate", new() { ["playbackRate"] = 1 }); }
            finally { await cdp.DetachAsync(); }
        }
    }

    [Fact]
    public async Task Accordion_GrowsOpenAndFoldsShut()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");
        await page.Locator("main button:visible").First.ClickAsync();
        await page.Locator(".play-join-biomarkers summary:visible").First.ScrollIntoViewIfNeededAsync();

        var opening = await SampleDetailsHeightsAsync(page);
        var closing = await SampleDetailsHeightsAsync(page);

        // Steps between the closed and open heights: the panel grows instead of jumping.
        AssertPassesThrough(opening);
        AssertPassesThrough(closing);
        Assert.True(opening[^1] > opening[0]);
        Assert.True(closing[^1] < closing[0]);

        static void AssertPassesThrough(int[] heights)
        {
            var low = heights.Min();
            var high = heights.Max();
            Assert.True(high - low > 40, $"Heights barely changed: {string.Join(", ", heights)}");
            Assert.Contains(heights, h => h > low + 4 && h < high - 4);
        }
    }

    [Fact]
    public async Task Toast_LiftsAwayBeforeItsPlaceIsReused()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/");
        await page.Mouse.ClickAsync(4, 400);

        await page.EvaluateAsync("() => { const toast = document.getElementById('copiedText'); toast.textContent = 'Copied'; toast.hidden = false; }");
        // A real toast stays two seconds; let its entrance finish first.
        await page.WaitForTimeoutAsync(900);
        var exiting = await page.EvaluateAsync<string[]>($$"""
            () => { document.getElementById('copiedText').hidden = true; return ({{RunningMotionScript}})('#copiedText'); }
            """);
        Assert.Contains("opacity", exiting);
        await Assertions.Expect(page.Locator("#copiedText")).ToHaveCSSAsync("display", "none");
    }

    [Theory]
    [InlineData("/leaderboard", "#leaderboardOrderToggle", "0.97")]
    [InlineData("/play", "#continueGameBtn", "0.965")]
    public async Task Buttons_GiveWayWhilePressed(string route, string selector, string expectedScale)
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, route);
        if (route == "/play")
            await page.WaitForFunctionAsync("""
                () => document.documentElement.classList.contains('play-route-ready')
                    && !document.body.matches('.play-start-preintro, .play-start-intro')
                """);
        // Check both the shared press treatment and the play flow's deliberate
        // override, rather than treating a component as the global default.
        var button = page.Locator(selector);
        await button.HoverAsync();
        await page.Mouse.DownAsync();
        try
        {
            // Observe the held state instead of assuming a busy CI renderer
            // has presented the press transition after a fixed wall-clock delay.
            await Assertions.Expect(button).ToHaveCSSAsync("scale", expectedScale);
        }
        finally
        {
            await page.Mouse.MoveAsync(1, 1);
            await page.Mouse.UpAsync();
        }
    }

    [Fact]
    public async Task ReducedMotion_KeepsEveryChangeInstant()
    {
        await using var context = await NewMotionContextAsync(390, 844, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);
        Assert.Null(await page.EvaluateAsync<string?>("() => document.documentElement.dataset.lwcMotion ?? null"));

        await page.EvaluateAsync("() => { customAlert('Saved'); }");
        await page.Locator("#custom-alert-close").ClickAsync();
        Assert.Equal("none", await page.Locator("#custom-alert").EvaluateAsync<string>("alert => getComputedStyle(alert).display"));

        var toggle = page.Locator("#leaderboardOrderToggle");
        await Assertions.Expect(toggle).ToBeEnabledAsync();
        await toggle.ClickAsync();
        Assert.Empty(await page.EvaluateAsync<string[]>(RunningMotionScript, "#leaderboardOrderPanel"));
        await toggle.ClickAsync();
        Assert.Equal("none", await page.Locator("#leaderboardOrderPanel").EvaluateAsync<string>("panel => getComputedStyle(panel).display"));

        await toggle.HoverAsync();
        await page.Mouse.DownAsync();
        Assert.Equal("none", await toggle.EvaluateAsync<string>("element => getComputedStyle(element).scale"));
        await page.Mouse.UpAsync();
    }

    private Task<IBrowserContext> NewMotionContextAsync(int width, int height, ReducedMotion reducedMotion = ReducedMotion.NoPreference) =>
        NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = reducedMotion
        });
}
