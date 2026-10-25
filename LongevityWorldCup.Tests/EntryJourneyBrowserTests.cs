using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

/// <summary>
/// The first-time visitor's entry journey: the /join stage, its motion, the
/// track choice and the hand-over into the apply flow.
/// </summary>
[Collection(BrowserTestCollections.WorkloadB)]
public sealed class EntryJourneyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string RecordPageReveal = """
        window.__lwcReveal = null;
        window.addEventListener('pagereveal', event => {
            window.__lwcReveal = { transition: Boolean(event.viewTransition), recorded: !event.viewTransition };
            if (event.viewTransition) {
                // pagereveal can precede parsing the banner on a streamed document.
                const parsed = document.readyState === 'loading'
                    ? new Promise(resolve => document.addEventListener('DOMContentLoaded', resolve, { once: true }))
                    : Promise.resolve();
                Promise.all([event.viewTransition.ready, parsed]).then(() => {
                    window.__lwcReveal.headerName = getComputedStyle(document.querySelector('header[role=banner]')).viewTransitionName;
                    window.__lwcReveal.animations = document.getAnimations()
                        .filter(a => a.effect && String(a.effect.pseudoElement || '').startsWith('::view-transition'))
                        .map(a => ({ name: a.animationName, pseudo: a.effect.pseudoElement, duration: a.effect.getTiming().duration, delay: a.effect.getTiming().delay }));
                    // Publish readiness only after the complete snapshot is available.
                    window.__lwcReveal.recorded = true;
                }).catch(error => {
                    window.__lwcReveal.skipped = String(error);
                    window.__lwcReveal.animations = [];
                    window.__lwcReveal.recorded = true;
                });
            }
        });
        """;

    private async Task<IBrowserContext> NewJourneyContextAsync(int width, int height, bool dark, bool reducedMotion)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(RecordPageReveal);
        return context;
    }

    private static async Task<IPage> OpenJoinAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinTrackBackBtn')?.disabled && !document.getElementById('joinGoProButton')?.disabled");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow?.(); }");
        return page;
    }

    private static Task SettleAnimationsAsync(IPage page)
        => WaitForFiniteAnimationsAsync(page);

    [Fact]
    public async Task CanceledTrackNavigation_RestoresChoicesAndKeepsTheSavedOffer()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, false, false);
        var page = await OpenJoinAsync(context);
        await SettleAnimationsAsync(page);
        await page.EvaluateAsync("""
            () => navigation.addEventListener('navigate', event => {
                if (!event.destination.sameDocument) event.preventDefault();
            })
            """);
        await page.Locator("#joinStartAmateurBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#joinTrackPanel")).Not.ToHaveAttributeAsync("data-join-choice", "amateur");
        Assert.EndsWith("/join", page.Url);
        Assert.True(await page.EvaluateAsync<bool>("() => !!sessionStorage.getItem('pendingPaymentOffer')"));
        Assert.Equal("auto", await page.Locator(".play-join-card").First.EvaluateAsync<string>("card => getComputedStyle(card).pointerEvents"));
    }

    [Fact]
    public async Task CanceledExit_DoesNotDisableJourneyStyles()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, false, false);
        var page = await OpenJoinAsync(context);
        await page.EvaluateAsync("""
            () => {
                navigation.addEventListener('navigate', event => {
                    if (!event.destination.sameDocument) event.preventDefault();
                });
                location.href = '/ruleset';
            }
            """);
        await page.WaitForTimeoutAsync(100);
        Assert.EndsWith("/join", page.Url);
        Assert.True(await page.EvaluateAsync<bool>("() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => !link.disabled)"));
    }

    [Fact]
    public async Task ReducedMotion_HoverKeepsTheTileAndArrowStill()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, false, true);
        var page = await OpenJoinAsync(context);
        await page.Locator(".play-join-card").First.HoverAsync();
        var transforms = await page.EvaluateAsync<string[]>("""
            () => [document.querySelector('.play-join-card'), document.querySelector('.play-join-card .flow-action > i')]
                .filter(Boolean).map(node => getComputedStyle(node).transform)
            """);
        Assert.All(transforms, transform => Assert.Equal("none", transform));
    }

    [Fact]
    public async Task SlowArrival_PreservesTheCalculatorAndDoesNotRaiseAPageError()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, false, false);
        var page = await OpenJoinAsync(context);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await context.RouteAsync("**/css/bioageform.css*", async route =>
        {
            await Task.Delay(5500);
            await route.ContinueAsync();
        });
        try
        {
            await page.Locator("#joinStartAmateurBtn").ClickAsync();
            await page.WaitForURLAsync("**/pheno-age**");
            await page.WaitForLoadStateAsync(LoadState.Load);
            await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
            Assert.True(errors.Count == 0, string.Join("\n", errors));
        }
        finally { await context.UnrouteAllAsync(new() { Behavior = UnrouteBehavior.Wait }); }
    }

    [Fact]
    public async Task TransitionHandling_DoesNotHideUnrelatedRejections()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, false, false);
        var page = await OpenJoinAsync(context);
        var reported = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        page.PageError += (_, error) => reported.TrySetResult(error);
        await page.EvaluateAsync("() => { void Promise.reject(new Error('Unrelated journey failure')); }");
        Assert.Contains("Unrelated journey failure", await reported.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(1280, 720, true)]
    [InlineData(768, 1024, false)]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task JoinStage_LeadsWithTheStartTitleAndTracksRiseOutOfIt(int width, int height, bool dark)
    {
        await using var context = await NewJourneyContextAsync(width, height, dark, reducedMotion: true);
        var page = await OpenJoinAsync(context);

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const panel = document.getElementById('joinTrackPanel');
                const head = panel.querySelector('.play-join-head');
                const title = panel.querySelector('.play-join-title');
                const ts = getComputedStyle(title);
                if (ts.textTransform !== 'uppercase' || ts.fontStyle !== 'italic') issues.push('title is not the start screen wordmark style');
                const rule = getComputedStyle(title, '::after');
                if (rule.content === 'none' || parseFloat(rule.height) < 3 || parseFloat(rule.width) < 40) issues.push('title has no accent rule');
                if (!rule.backgroundColor.includes('25, 195, 209')) issues.push(`accent rule is ${rule.backgroundColor}`);
                if (!getComputedStyle(head).backgroundImage.includes('radial-gradient')) issues.push('stage has no crest halo');
                const h = head.getBoundingClientRect();
                const intro = panel.querySelector('.play-join-intro').getBoundingClientRect();
                const cards = [...panel.querySelectorAll('.play-join-card')];
                const first = cards[0].getBoundingClientRect();
                if (first.top >= h.bottom - 16) issues.push(`tracks do not rise out of the stage (${Math.round(h.bottom - first.top)}px overlap)`);
                if (first.top < intro.bottom + 8) issues.push('tracks cover the stage lead');
                if (innerWidth <= 760) {
                    const shadow = getComputedStyle(head).boxShadow;
                    if (!/\d{3,}px/.test(shadow)) issues.push('phone stage does not run edge to edge');
                    if (parseFloat(getComputedStyle(head).borderTopLeftRadius) > 0) issues.push('phone stage is still a floating card');
                }
                for (const card of cards) {
                    const name = card.querySelector('.track-name').textContent.trim();
                    const dt = card.querySelector('.play-join-meta dt');
                    if (dt.textContent.trim() !== 'Entry') issues.push(`${name}: entry price does not lead the specs`);
                    const price = card.querySelector('.join-amateur-entry-cell > .pro-new-price, #joinProEntryPrice');
                    const other = card.querySelectorAll('.play-join-meta dd')[1];
                    const ratio = parseFloat(getComputedStyle(price).fontSize) / parseFloat(getComputedStyle(other).fontSize);
                    if (ratio < 1.6) issues.push(`${name}: price is not the headline figure (${ratio.toFixed(2)}x)`);
                    const p = price.getBoundingClientRect(), d = dt.getBoundingClientRect();
                    if (Math.abs((p.top + p.bottom) / 2 - (d.top + d.bottom) / 2) > 6 && getComputedStyle(card.querySelector('.play-join-meta')).gridTemplateColumns.split(' ').length > 1)
                        issues.push(`${name}: Entry label is off the price line`);
                }
                if (innerWidth > 760 && innerHeight >= 600) {
                    const foot = panel.querySelector('.play-join-foot');
                    if (getComputedStyle(foot).backgroundColor === getComputedStyle(panel).backgroundColor) issues.push('foot is not a muted band');
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}x{height}: {string.Join("; ", problems)}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task JoinEntrance_RisesTitleAndTracksWhileTheFooterStaysReadableAndStill(int width, int height)
    {
        await using var context = await NewJourneyContextAsync(width, height, dark: false, reducedMotion: false);
        // Keep every animation that starts, so finished ones can still be read.
        await context.AddInitScriptAsync("""
            window.__lwcAnimations = new Set();
            document.addEventListener('animationstart', () => document.getAnimations().forEach(a => window.__lwcAnimations.add(a)), true);
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/join");
        await page.WaitForFunctionAsync("() => window.__lwcAnimations.size > 0");

        var report = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const panel = document.getElementById('joinTrackPanel');
                const recorded = [...window.__lwcAnimations];
                const of = element => recorded.filter(a => a.effect?.target === element && !a.effect.pseudoElement);
                const timing = a => a.effect.getComputedTiming();
                const title = of(panel.querySelector('.play-join-title'));
                const rule = recorded.filter(a => a.effect?.target === panel.querySelector('.play-join-title') && a.effect.pseudoElement === '::after');
                const cards = [...panel.querySelectorAll('.play-join-card')].map(of);
                const foot = of(panel.querySelector('.play-join-foot'));
                if (!title.length) issues.push('title does not rise');
                if (!rule.length) issues.push('accent rule does not draw');
                if (cards.some(list => !list.length)) issues.push('a track does not rise');
                if (getComputedStyle(panel.querySelector('.play-join-foot')).opacity !== '1') issues.push('foot is not immediately readable');
                const all = [...title, ...rule, ...cards.flat(), ...foot];
                for (const a of all) {
                    const t = timing(a);
                    if (t.duration < 120 || t.duration > 400) issues.push(`${a.animationName} lasts ${t.duration}ms`);
                    if (t.delay + t.duration > 600) issues.push(`${a.animationName} ends after ${t.delay + t.duration}ms`);
                }
                if (cards.every(list => list.length) && !(timing(cards[1][0]).delay > timing(cards[0][0]).delay)) issues.push('tracks do not rise one after the other');
                if (cards.every(list => list.length) && !(timing(cards[0][0]).delay >= timing(title[0] || cards[0][0]).delay)) issues.push('tracks rise before the title');
                for (const a of foot) {
                    if (a.effect.getKeyframes().some(k => k.transform && k.transform !== 'none')) issues.push('foot moves while the dock measures it');
                    if (a.effect.getKeyframes().some(k => k.opacity !== undefined && Number(k.opacity) < 1)) issues.push('foot hides its controls during arrival');
                }
                return issues;
            }
            """);
        Assert.True(report.Length == 0, $"{width}x{height}: {string.Join("; ", report)}");

        await page.WaitForFunctionAsync("() => !document.getElementById('joinGoProButton')?.disabled");
        await SettleAnimationsAsync(page);
        var settled = await page.EvaluateAsync<string[]>(
            """
            () => [...document.querySelectorAll('.play-join-card, .play-join-foot, .play-join-title')]
                .filter(e => getComputedStyle(e).opacity !== '1' || !['none', 'matrix(1, 0, 0, 1, 0, 0)'].includes(getComputedStyle(e).transform))
                .map(e => e.className)
            """);
        Assert.True(settled.Length == 0, $"still moving after the entrance: {string.Join(", ", settled)}");
    }

    [Fact]
    public async Task ReducedMotion_JoinEntersStillAndTheApplyFlowArrivesWithoutATransition()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, dark: true, reducedMotion: true);
        var page = await OpenJoinAsync(context);

        var running = await page.EvaluateAsync<string[]>(
            "() => document.getAnimations().filter(a => a.effect?.target?.closest?.('#joinTrackPanel')).map(a => a.animationName)");
        Assert.Empty(running);
        var transitions = await page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.play-join-card')).transitionDuration");
        Assert.Matches("^0s(, 0s)*$", transitions);

        await page.Locator("#joinStartAmateurBtn").ClickAsync();
        await page.WaitForURLAsync("**/pheno-age**");
        await page.WaitForFunctionAsync("() => window.__lwcReveal !== null");
        Assert.False(await page.EvaluateAsync<bool>("() => window.__lwcReveal.transition"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrackTiles_LiftOnHoverAndTheChosenTrackHoldsTheRing(bool pro)
    {
        await using var context = await NewJourneyContextAsync(1280, 720, dark: pro, reducedMotion: false);
        var page = await OpenJoinAsync(context);
        await SettleAnimationsAsync(page);

        var chosen = pro ? ".play-join-card--pro" : ".play-join-card:not(.play-join-card--pro)";
        var other = pro ? ".play-join-card:not(.play-join-card--pro)" : ".play-join-card--pro";
        Assert.True(await page.EvaluateAsync<bool>(
            $"() => parseFloat(getComputedStyle(document.querySelector('{chosen}')).transitionDuration) > 0"),
            "the track tile does not ease its hover lift");
        await page.Locator(chosen).HoverAsync();
        await page.WaitForTimeoutAsync(400);
        var lift = await page.EvaluateAsync<double>($"() => new DOMMatrix(getComputedStyle(document.querySelector('{chosen}')).transform).m42");
        Assert.True(lift <= -2, $"hovered tile lifts {lift}px");

        // Choose and read the settled state in one task, before the page leaves.
        var state = await page.EvaluateAsync<string[]>(
            $$"""
            () => {
                const issues = [];
                document.getElementById('{{(pro ? "joinGoProButton" : "joinStartAmateurBtn")}}').click();
                const panel = document.getElementById('joinTrackPanel');
                panel.getAnimations({ subtree: true }).forEach(a => a.finish());
                if (panel.dataset.joinChoice !== '{{(pro ? "pro" : "amateur")}}') issues.push(`choice is ${panel.dataset.joinChoice}`);
                const chosen = document.querySelector('{{chosen}}'), other = document.querySelector('{{other}}');
                if (!/0px 0px 0px [23]px/.test(getComputedStyle(chosen).boxShadow)) issues.push(`chosen ring: ${getComputedStyle(chosen).boxShadow}`);
                if (parseFloat(getComputedStyle(other.firstElementChild).opacity) > 0.6) issues.push('the other track does not step back');
                if (getComputedStyle(other).opacity !== '1') issues.push('the other track lets the stage show through');
                if (getComputedStyle(other).pointerEvents !== 'none') issues.push('the other track still takes clicks');
                return issues;
            }
            """);
        Assert.True(state.Length == 0, string.Join("; ", state));
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    public async Task JoinHandsOverToTheApplyFlowThroughAViewTransitionWithTheHeaderHeld(int width, int height, bool pro)
    {
        await using var context = await NewJourneyContextAsync(width, height, dark: false, reducedMotion: false);
        var page = await OpenJoinAsync(context);
        await SettleAnimationsAsync(page);

        // The resting banner must not trap floating PLAY in a stacking context.
        Assert.Equal("none", await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.querySelector('header[role=banner]')).viewTransitionName"));
        await page.Locator(pro
            ? (width < 600 ? "#joinMobileGoProButton" : "#joinGoProButton")
            : (width < 600 ? "#joinMobileStartAmateurBtn" : "#joinStartAmateurBtn")).ClickAsync();
        await page.WaitForURLAsync(pro ? "**/bortz-age**" : "**/pheno-age**");
        await page.WaitForFunctionAsync("() => window.__lwcReveal?.recorded");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__lwcReveal.transition && !window.__lwcReveal.skipped"),
            "Native route transition was unavailable or skipped: " + await page.EvaluateAsync<string>("() => JSON.stringify(window.__lwcReveal)"));

        Assert.Equal("lwc-site-header", await page.EvaluateAsync<string>("window.__lwcReveal.headerName"));

        var animations = await page.EvaluateAsync<string[]>(
            "() => window.__lwcReveal.animations.map(a => `${a.name}|${a.pseudo}|${a.duration}|${a.delay}`)");
        Assert.Contains(animations, a => a.StartsWith("lwc-journey-leave|::view-transition-old(root)"));
        Assert.Contains(animations, a => a.StartsWith("lwc-journey-arrive|::view-transition-new(root)"));
        Assert.DoesNotContain(animations, a => a.Contains("view-transition-new(lwc-site-header)") && !a.StartsWith("none|"));
        foreach (var animation in animations.Where(a => a.StartsWith("lwc-journey")))
        {
            var parts = animation.Split('|');
            var end = double.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture) + double.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture);
            Assert.True(end <= 400, $"{animation} ends after {end}ms");
        }
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/play")]
    [InlineData("/join")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/apply")]
    public async Task EveryJourneyPage_OptsIntoTheSameRouteTransition(string path)
    {
        await using var context = await NewJourneyContextAsync(1280, 720, dark: false, reducedMotion: false);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        var optedIn = await page.EvaluateAsync<bool>(
            """
            () => [...document.styleSheets].some(sheet => {
                const walk = rules => [...rules].some(rule => rule.constructor.name === 'CSSViewTransitionRule'
                    ? rule.navigation === 'auto'
                    : rule.cssRules ? walk(rule.cssRules) : false);
                try { return walk(sheet.cssRules); } catch { return false; }
            })
            """);
        Assert.True(optedIn, $"{path} does not opt into the journey transition");
    }

    [Theory]
    [InlineData("/join", "/leaderboard")]
    [InlineData("/?search=one", "/leaderboard")]
    [InlineData("/#faq", "/leaderboard")]
    [InlineData("/", "/leaderboard?view=pheno")]
    [InlineData("/apply", "/ruleset")]
    [InlineData("/join", "/ruleset")]
    public async Task LeavingTheJourney_OptsOutOfTheTransitionWithoutAPageError(string from, string to)
    {
        await using var context = await NewJourneyContextAsync(1280, 720, dark: false, reducedMotion: false);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(from);
        await page.WaitForLoadStateAsync(LoadState.Load);
        await page.EvaluateAsync("to => { window.location.href = to; }", to);
        await page.WaitForURLAsync("**" + to + "**");
        await page.WaitForFunctionAsync("() => window.__lwcReveal !== null");
        await page.WaitForTimeoutAsync(300);
        Assert.False(await page.EvaluateAsync<bool>("() => window.__lwcReveal.transition"), $"{from} -> {to} started a journey transition");
        Assert.True(errors.Count == 0, $"{from} -> {to}: {string.Join("; ", errors)}");
    }

    [Fact]
    public async Task RedirectLeavingTheJourney_UsesTheFinalDestinationToSkipTheTransition()
    {
        await using var context = await NewJourneyContextAsync(1280, 720, dark: false, reducedMotion: false);
        await context.RouteAsync("**/review?journey-redirect-test", route => route.FulfillAsync(new()
        {
            Status = 302,
            Headers = new Dictionary<string, string> { ["Location"] = "/ruleset" }
        }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/join");
        await page.EvaluateAsync("() => { location.href = '/review?journey-redirect-test'; }");
        await page.WaitForURLAsync("**/ruleset");
        await page.WaitForFunctionAsync("() => window.__lwcReveal !== null");
        Assert.False(await page.EvaluateAsync<bool>("() => window.__lwcReveal.transition"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("pageswap")]
    [InlineData("pagereveal")]
    public async Task SkippedPageTransition_DoesNotRaiseAnUnhandledRejection(string eventName)
    {
        // Keep this context free of the test recorder, which owns a separate
        // ready promise observer. This specifically tests the page's handling.
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 720 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await NavigateAndSettleAsync(page, "/join");
        await page.EvaluateAsync("""
            async name => {
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
    public async Task DiscountedJoin_StillFitsTheStageTracksAndFootIn1440x900()
    {
        await using var context = await NewJourneyContextAsync(1440, 900, dark: false, reducedMotion: true);
        await context.AddInitScriptAsync("""
            const athlete = { Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', PersonalLink: 'https://example.test/a', DateOfBirth: { Year: 1980, Month: 5, Day: 20 }, Biomarkers: [] };
            sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            localStorage.setItem('selectedAthleteName', athlete.Name);
            localStorage.setItem('hasApplication', 'true');
            localStorage.setItem('gmaHasPerfectGuess', '1');
            """);
        var page = await OpenJoinAsync(context);
        await Assertions.Expect(page.Locator("#joinProDiscountNote")).ToBeVisibleAsync();
        await page.EvaluateAsync("async () => { window.LwcFlowActionDock?.refreshNow?.(); await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r))); }");
        Assert.False(await HasDockClassAsync(page, ".play-join-actions"), "Back left the foot for the dock");
        var noteBottom = await page.EvaluateAsync<double>("() => document.querySelector('.play-join-challenge-note').getBoundingClientRect().bottom");
        Assert.True(noteBottom <= 900, $"the lifestyle detour ends at {noteBottom}px, below the first screen");
    }
}
