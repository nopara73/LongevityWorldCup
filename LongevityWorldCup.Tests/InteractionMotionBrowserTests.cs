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

    // CLS excludes expected shifts within 500ms of a discrete user input.
    // The in-flow Order strip deliberately makes room beneath the toolbar.
    private const string WatchLayoutShiftScript = """
        () => {
            window.__lwcShift = 0;
            new PerformanceObserver(list => {
                for (const entry of list.getEntries()) {
                    if (!entry.hadRecentInput) window.__lwcShift += entry.value;
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

        await page.EvaluateAsync("() => { customAlert('Saved'); }");
        var opening = await page.EvaluateAsync<string[]>(RunningMotionScript, "#custom-alert, #custom-alert *");
        Assert.Contains("opacity", opening);

        await page.WaitForTimeoutAsync(400);
        await page.Locator("#custom-alert-close").ClickAsync();
        var closing = await page.EvaluateAsync<string[]>("""
            selector => {
                const alert = document.getElementById('custom-alert');
                const panel = alert.querySelector('.custom-alert-panel').getBoundingClientRect();
                const hit = document.elementFromPoint(panel.left + panel.width / 2, panel.top + panel.height / 2);
                return [
                    String(alert.hidden),
                    getComputedStyle(alert).display,
                    getComputedStyle(alert).pointerEvents,
                    String(alert.contains(hit))
                ];
            }
            """, "#custom-alert");
        Assert.Equal("true", closing[0]);
        Assert.NotEqual("none", closing[1]);
        Assert.Equal("none", closing[2]);
        Assert.Equal("false", closing[3]);
        var exiting = await page.EvaluateAsync<string[]>(RunningMotionScript, "#custom-alert, #custom-alert *");
        Assert.Contains("opacity", exiting);
        Assert.Contains(exiting, motion => motion is "translate" or "scale");

        await Assertions.Expect(page.Locator("#custom-alert")).ToHaveCSSAsync("display", "none");
        Assert.True(await page.EvaluateAsync<double>("() => window.__lwcShift") < 0.001);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task LoadingDialog_FadesOutInsteadOfVanishing()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.Mouse.ClickAsync(4, 400);

        await page.EvaluateAsync("() => showLoading()");
        Assert.Contains("opacity", await page.EvaluateAsync<string[]>(RunningMotionScript, "#loading-dialog"));
        await page.WaitForTimeoutAsync(300);
        await page.EvaluateAsync("() => hideLoading()");

        var exiting = await page.EvaluateAsync<string[]>(RunningMotionScript, "#loading-dialog, #loading-dialog *");
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

        await toggle.ClickAsync();
        var opening = await page.EvaluateAsync<string[]>(RunningMotionScript, "#leaderboardOrderPanel");
        Assert.Contains("opacity", opening);
        Assert.Contains("scale", opening);

        await page.WaitForTimeoutAsync(400);
        await toggle.ClickAsync();
        Assert.Equal("none", await page.Locator("#leaderboardOrderPanel").EvaluateAsync<string>("panel => getComputedStyle(panel).pointerEvents"));
        Assert.Contains("opacity", await page.EvaluateAsync<string[]>(RunningMotionScript, "#leaderboardOrderPanel"));
        await Assertions.Expect(page.Locator("#leaderboardOrderPanel")).ToHaveCSSAsync("display", "none");
        Assert.True(await page.EvaluateAsync<double>("() => window.__lwcShift") < 0.001);
        var toolbarAfter = await toolbar.BoundingBoxAsync();
        Assert.NotNull(toolbarBefore);
        Assert.NotNull(toolbarAfter);
        Assert.InRange(Math.Abs(toolbarAfter.X - toolbarBefore.X), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Y - toolbarBefore.Y), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Width - toolbarBefore.Width), 0, 0.5);
        Assert.InRange(Math.Abs(toolbarAfter.Height - toolbarBefore.Height), 0, 0.5);
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
    public async Task AthleteProfile_BackdropFadesInAsWellAsOut(int width, int height)
    {
        await using var context = await NewMotionContextAsync(width, height);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/leaderboard");
        // Observe the browser's transition lifecycle before the click. The 200ms
        // entrance may finish before ClickAsync returns on a busy runner.
        await page.EvaluateAsync("""
            () => {
                window.__backdropMotion = [];
                const backdrop = document.getElementById('detailsModal');
                for (const type of ['transitionrun', 'transitionend']) {
                    backdrop.addEventListener(type, event => {
                        if (event.target !== backdrop || event.propertyName !== 'opacity') return;
                        window.__backdropMotion.push((backdrop.classList.contains('fade-out') ? 'exit:' : 'enter:') + type);
                    });
                }
            }
            """);
        await page.Locator("#athleteLeaderboard tbody tr[data-athlete-name]").First.Locator(".athlete-name").ClickAsync();
        await page.WaitForFunctionAsync("() => window.__backdropMotion.includes('enter:transitionend')");
        await Assertions.Expect(page.Locator("#detailsModal")).ToHaveCSSAsync("opacity", "1");
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        var transitions = await page.EvaluateAsync<string[]>("() => window.__backdropMotion");
        Assert.Contains("enter:transitionrun", transitions);
        Assert.Contains("enter:transitionend", transitions);
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

        await primary.SelectOptionAsync(option);
        var moving = await page.EvaluateAsync<int>("""
            () => document.getAnimations().filter(animation =>
                animation.playState === 'running'
                && animation.effect?.target?.matches?.('#athleteLeaderboard tbody tr')).length
            """);
        Assert.True(moving > 0, "Reordered rows should animate from their previous position.");
    }

    // Heights of the visible accordion on every frame for 400ms after a click on its summary.
    private const string SampleDetailsHeightsScript = """
        () => new Promise(resolve => {
            const details = [...document.querySelectorAll('.play-join-biomarkers details')].find(d => d.getClientRects().length);
            const heights = [];
            const start = performance.now();
            details.querySelector('summary').click();
            const frame = () => {
                heights.push(Math.round(details.getBoundingClientRect().height));
                if (performance.now() - start < 400) requestAnimationFrame(frame);
                else resolve(heights);
            };
            requestAnimationFrame(frame);
        })
        """;

    [Fact]
    public async Task Accordion_GrowsOpenAndFoldsShut()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");
        await page.Locator("main button:visible").First.ClickAsync();
        await page.Locator(".play-join-biomarkers summary:visible").First.ScrollIntoViewIfNeededAsync();

        var opening = await page.EvaluateAsync<int[]>(SampleDetailsHeightsScript);
        var closing = await page.EvaluateAsync<int[]>(SampleDetailsHeightsScript);

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
        await page.EvaluateAsync("() => { document.getElementById('copiedText').hidden = true; }");

        var exiting = await page.EvaluateAsync<string[]>(RunningMotionScript, "#copiedText");
        Assert.Contains("opacity", exiting);
        await Assertions.Expect(page.Locator("#copiedText")).ToHaveCSSAsync("display", "none");
    }

    [Fact]
    public async Task Buttons_GiveWayWhilePressed()
    {
        await using var context = await NewMotionContextAsync(1280, 800);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");
        var button = page.Locator("main button:visible").First;
        await button.HoverAsync();
        await page.Mouse.DownAsync();
        await page.WaitForTimeoutAsync(200);
        var pressed = await button.EvaluateAsync<string>("element => getComputedStyle(element).scale");
        await page.Mouse.MoveAsync(1, 1);
        await page.Mouse.UpAsync();

        Assert.Equal("0.97", pressed);
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
