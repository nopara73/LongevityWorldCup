using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.GuessMyAgeBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class GuessMyAgePayoffBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string OpenGuessModeScript =
        """
        ({ imageId, slug }) => {
            const modal = document.getElementById('detailsModal');
            const modalContent = modal.querySelector('.modal-content');
            modal.style.display = 'block';
            modalContent.classList.remove('is-loading');
            modalContent.dataset.athleteSlug = slug;
            modalContent.dataset.profileImageId = imageId;
            document.getElementById('gmaSkipAll').style.display = 'none';
            modalContent.classList.add('guess-mode');
        }
        """;

    private async Task<IPage> OpenStageAsync(IBrowserContext context, string slug, int guess)
    {
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "(range, value) => { range.value = String(value); range.dispatchEvent(new Event('input', { bubbles: true })); }",
            guess);
        return page;
    }

    private static async Task<IBrowserContext> NewStageContextAsync(
        IBrowser browser, Uri baseAddress, string resultBody, int width = 1440, int height = 900)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = baseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = width, Height = height }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = resultBody
        }));
        return context;
    }

    private static async Task SubmitAndSettleAsync(IPage page)
    {
        await page.Locator("#guessAgeContainer .gma-btn--primary").EvaluateAsync("button => button.click()");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaStatus')?.textContent?.includes('Actual age:') === true");
        await PauseClockAsync(page);
        for (var elapsed = 0; elapsed < 12000
             && !await page.EvaluateAsync<bool>("() => !!document.querySelector('#gmaRealBubble.is-settled')");
             elapsed += 250)
        {
            await page.Clock.RunForAsync(250);
        }
        Assert.True(await page.EvaluateAsync<bool>("() => !!document.querySelector('#gmaRealBubble.is-settled')"));
    }

    [Fact]
    public async Task CrowdWin_DrawsTheMissOnTheRulerAndFansSparksAwayFromTheFace()
    {
        await using var context = await NewStageContextAsync(
            Browser, App.BaseAddress,
            """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-crowd-test", 54);
        await page.EvaluateAsync(
            """
            () => {
                window.__gmaSparks = [];
                new MutationObserver(records => records.forEach(record => record.addedNodes.forEach(node => {
                    if (node.classList?.contains('gma-celebration-spark')) window.__gmaSparks.push(node);
                }))).observe(document.body, { childList: true, subtree: true });
            }
            """);
        await SubmitAndSettleAsync(page);
        await page.Clock.RunForAsync(100);
        // Playwright's clock controls rAF but CSS animations still use compositor
        // time. pause() schedules a compositor task; ready must resolve before
        // the mocked rAF samples the pose or it can retain a mask for the old pose.
        await page.EvaluateAsync("""
            async () => {
                const animations = document.getAnimations();
                animations.forEach(animation => animation.pause());
                await Promise.all(animations.map(animation => animation.ready));
            }
            """);
        await page.Clock.RunForAsync(32);

        var payoff = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const range = document.getElementById('gmaRange');
                const gap = document.getElementById('gmaGap');
                const center = age => {
                    const rect = range.getBoundingClientRect();
                    const thumb = Number.parseFloat(getComputedStyle(range).getPropertyValue('--gma-thumb-size'));
                    const ratio = (age - Number(range.min)) / (Number(range.max) - Number(range.min));
                    return rect.left + (thumb / 2) + (ratio * (rect.width - thumb));
                };
                const gapAnimation = gap?.getAnimations().map(animation => animation.animationName).join(',') ?? '';
                gap?.getAnimations().forEach(animation => animation.finish());
                const gapRect = gap?.getBoundingClientRect();
                const style = gap ? getComputedStyle(gap) : null;
                const layer = document.querySelector('.gma-celebration');
                const layerRect = layer.getBoundingClientRect();
                const clip = getComputedStyle(layer).clipPath;
                const mask = new Path2D(clip.match(/"(.*)"/)[1]);
                const maskContext = document.createElement('canvas').getContext('2d');
                const protectedCenters = ['#modalProfilePic', '.chrono-age-heading', '#gmaRealBubble', '#gmaPayoffRegion', '#closeAthleteDetailsModal']
                    .map(selector => document.querySelector(selector).getBoundingClientRect())
                    .filter(rect => rect.width && rect.height)
                    .every(rect => !maskContext.isPointInPath(mask,
                        rect.left + rect.width / 2 - layerRect.left,
                        rect.top + rect.height / 2 - layerRect.top, 'evenodd'));
                const trackBottom = range.getBoundingClientRect().bottom;
                const sparks = window.__gmaSparks.map(spark => ({
                    dx: Number.parseFloat(spark.style.getPropertyValue('--gma-spark-dx')),
                    dy: Number.parseFloat(spark.style.getPropertyValue('--gma-spark-dy')),
                    background: getComputedStyle(spark).backgroundColor,
                    quadrant: spark.dataset.quadrant
                }));
                return {
                    poseFrozen: document.getAnimations().every(animation => !animation.pending && animation.playState !== 'running'),
                    hasGap: Boolean(gap),
                    tone: gap?.dataset.tone ?? '',
                    gapLeftError: gap ? Math.abs(gapRect.left - center(41)) : -1,
                    gapWidthError: gap ? Math.abs(gapRect.width - (center(54) - center(41))) : -1,
                    gapBelowTrack: gap ? gapRect.top >= trackBottom - 1 : false,
                    gapAnimation,
                    gapHidden: gap?.getAttribute('aria-hidden') ?? '',
                    protectedCenters,
                    sparkCount: sparks.length,
                    sparksAimedAtFace: sparks.filter(spark => spark.dy < 0 && Math.abs(spark.dx) < Math.abs(spark.dy) * 1.2).length,
                    sparkDiscs: sparks.filter(spark => spark.background !== 'rgba(0, 0, 0, 0)').length,
                    quadrants: new Set(sparks.map(spark => spark.quadrant)).size,
                    bannerMarks: document.querySelectorAll('.gma-pop-banner .gma-pop-banner-mark').length
                };
            }
            """);
        Assert.True(payoff.GetProperty("poseFrozen").GetBoolean(), "The mask and protected geometry must be sampled after the compositor pause completes.");
        Assert.True(payoff.GetProperty("hasGap").GetBoolean(), "The miss is not drawn on the ruler.");
        Assert.Equal("crowd", payoff.GetProperty("tone").GetString());
        Assert.InRange(payoff.GetProperty("gapLeftError").GetDouble(), 0, 2);
        Assert.InRange(payoff.GetProperty("gapWidthError").GetDouble(), 0, 2);
        Assert.True(payoff.GetProperty("gapBelowTrack").GetBoolean());
        Assert.Equal("gma-gap-draw", payoff.GetProperty("gapAnimation").GetString());
        Assert.Equal("true", payoff.GetProperty("gapHidden").GetString());
        Assert.True(payoff.GetProperty("protectedCenters").GetBoolean(), "Sparks can paint over the face, labels or controls.");
        Assert.Equal(48, payoff.GetProperty("sparkCount").GetInt32());
        Assert.Equal(0, payoff.GetProperty("sparksAimedAtFace").GetInt32());
        Assert.Equal(0, payoff.GetProperty("sparkDiscs").GetInt32());
        Assert.True(payoff.GetProperty("quadrants").GetInt32() >= 3);
        Assert.Equal(2, payoff.GetProperty("bannerMarks").GetInt32());

        // Replaying another athlete starts from a clean ruler.
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageB, slug = "payoff-crowd-next" });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        Assert.Equal(0, await page.Locator("#gmaGap").CountAsync());
        Assert.Null(await page.Locator("#guessAgeContainer").GetAttributeAsync("data-gma-reaction"));
    }

    [Fact]
    public async Task ExactJackpot_LandsInBeatsAroundTheAnswerAndFadesOutInsteadOfCutting()
    {
        await using var context = await NewStageContextAsync(
            Browser, App.BaseAddress,
            """{"crowdAge":49,"crowdCount":12,"actualAge":41,"guessAccepted":true}""");
        var page = await OpenStageAsync(context, "payoff-exact-test", 41);
        await SubmitAndSettleAsync(page);
        for (var elapsed = 0; elapsed < 3000 && await page.Locator(".gma-exact-jackpot").CountAsync() == 0; elapsed += 50)
            await page.Clock.RunForAsync(50);
        await page.Clock.RunForAsync(400);

        // JS timers are paused, but CSS animations are instantiated by the
        // browser's rendering timeline. Wait for both ribbons before sampling.
        await page.WaitForFunctionAsync("""
            () => {
                const tracks = [...document.querySelectorAll('.gma-exact-jackpot-marquee-track')];
                return tracks.length === 2 && tracks.every(track => track.getAnimations()
                    .some(animation => animation.animationName === 'gma-exact-marquee-run'));
            }
            """);

        var opening = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const overlay = document.querySelector('.gma-exact-jackpot');
                const answer = document.getElementById('gmaRealBubble').getBoundingClientRect();
                const copy = overlay.querySelector('.gma-exact-jackpot-copy');
                const scrim = overlay.querySelector('.gma-exact-jackpot-scrim');
                const decoration = overlay.querySelector('.gma-exact-jackpot-decoration');
                const decorationRect = decoration.getBoundingClientRect();
                const protection = new Path2D(getComputedStyle(decoration).clipPath.match(/"(.*)"/)[1]);
                const protectionContext = document.createElement('canvas').getContext('2d');
                const protectedStage = ['#modalProfilePic', '#gmaRealBubble', '#gmaPayoffRegion']
                    .map(selector => document.querySelector(selector).getBoundingClientRect())
                    .filter(rect => rect.width && rect.height)
                    .every(rect => !protectionContext.isPointInPath(protection,
                        rect.left + rect.width / 2 - decorationRect.left,
                        rect.top + rect.height / 2 - decorationRect.top, 'evenodd'));
                const beat = copy.getAnimations().find(animation => animation.animationName === 'gma-exact-copy-beat');
                const scrimBeat = scrim?.getAnimations().find(animation => animation.animationName === 'gma-exact-scrim-beat');
                const finite = value => Number.isFinite(value) ? value : -1;
                const sampleOpacity = (animation, element, time) => {
                    animation.pause();
                    animation.currentTime = time;
                    return Number(getComputedStyle(element).opacity);
                };
                const duration = beat ? beat.effect.getTiming().duration : 0;
                const marquees = [...overlay.querySelectorAll('.gma-exact-jackpot-marquee')].map(marquee => {
                    const track = marquee.querySelector('.gma-exact-jackpot-marquee-track');
                    const segments = track ? [...track.children].map(child => child.textContent) : [];
                    const run = track?.getAnimations().find(animation => animation.animationName === 'gma-exact-marquee-run');
                    const frames = run?.effect.getKeyframes() ?? [];
                    return {
                        segments: segments.length,
                        same: segments.length === 2 && segments[0] === segments[1],
                        fillsRibbon: track?.firstElementChild.offsetWidth >= marquee.clientWidth,
                        finite: run ? run.effect.getTiming().iterations !== Infinity : false,
                        endsAtHalf: String(frames[frames.length - 1]?.transform || '').includes('-50%')
                    };
                });
                return {
                    originXError: finite(Math.abs(Number.parseFloat(overlay.style.getPropertyValue('--gma-jackpot-x')) - (answer.left + answer.width / 2))),
                    originYError: finite(Math.abs(Number.parseFloat(overlay.style.getPropertyValue('--gma-jackpot-y')) - (answer.top + answer.height / 2))),
                    protectedStage,
                    beating: copy.classList.contains('is-beat'),
                    held: beat ? sampleOpacity(beat, copy, duration * 0.5) : -1,
                    cleared: beat ? sampleOpacity(beat, copy, duration * 0.999) : -1,
                    scrimHeld: scrimBeat ? sampleOpacity(scrimBeat, scrim, duration * 0.4) : -1,
                    scrimCleared: scrimBeat ? sampleOpacity(scrimBeat, scrim, duration * 0.999) : -1,
                    marquees
                };
            }
            """);
        // Measured once at launch while the answer is still bouncing into place.
        Assert.InRange(opening.GetProperty("originXError").GetDouble(), 0, 12);
        Assert.InRange(opening.GetProperty("originYError").GetDouble(), 0, 12);
        Assert.True(opening.GetProperty("protectedStage").GetBoolean(), "Jackpot marks can obscure the athlete or result.");
        Assert.True(opening.GetProperty("beating").GetBoolean(), "The jackpot copy does not land as a beat.");
        Assert.InRange(opening.GetProperty("held").GetDouble(), 0.99, 1);
        Assert.InRange(opening.GetProperty("cleared").GetDouble(), 0, 0.05);
        Assert.InRange(opening.GetProperty("scrimHeld").GetDouble(), 0.99, 1);
        Assert.InRange(opening.GetProperty("scrimCleared").GetDouble(), 0, 0.05);
        Assert.Equal(2, opening.GetProperty("marquees").GetArrayLength());
        foreach (var marquee in opening.GetProperty("marquees").EnumerateArray())
        {
            Assert.Equal(2, marquee.GetProperty("segments").GetInt32());
            Assert.True(marquee.GetProperty("same").GetBoolean());
            Assert.True(marquee.GetProperty("fillsRibbon").GetBoolean(), "A marquee segment is shorter than its ribbon.");
            Assert.True(marquee.GetProperty("finite").GetBoolean(), "A ribbon is missing its finite marquee animation: " + opening.GetRawText());
            Assert.True(marquee.GetProperty("endsAtHalf").GetBoolean());
        }

        // The escalation phase lands its own beat with its own copy.
        await page.EvaluateAsync("""
            () => {
                const copy = document.querySelector('.gma-exact-jackpot-copy');
                const openingBeat = copy.getAnimations().find(animation => animation.animationName === 'gma-exact-copy-beat');
                window.__gmaEscalationBeat = null;
                const capture = event => {
                    if (event.detail.phase !== 'escalation') return;
                    const beat = copy.getAnimations().find(animation => animation.animationName === 'gma-exact-copy-beat');
                    window.__gmaEscalationBeat = copy.classList.contains('is-beat') && Boolean(beat)
                        && beat !== openingBeat && (beat.currentTime === null || beat.currentTime < 1500);
                    window.removeEventListener('gma:exact-phase', capture);
                };
                window.addEventListener('gma:exact-phase', capture);
            }
            """);
        await page.Clock.RunForAsync(4800);
        Assert.Equal("escalation", await page.Locator(".gma-exact-jackpot").GetAttributeAsync("data-phase"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.__gmaEscalationBeat === true"),
            "The escalation phase must start a new copy beat before the driver observes it.");

        // The finale holds, and the takeover dissolves before it is removed.
        await page.Clock.RunForAsync(9100);
        var ending = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const overlay = document.querySelector('.gma-exact-jackpot');
                return {
                    present: Boolean(overlay),
                    ending: overlay?.dataset.ending ?? '',
                    final: overlay?.querySelector('.gma-exact-jackpot-copy').classList.contains('is-final') ?? false,
                    fading: overlay?.getAnimations().some(animation => animation.transitionProperty === 'opacity') ?? false
                };
            }
            """);
        Assert.True(ending.GetProperty("present").GetBoolean());
        Assert.Equal("true", ending.GetProperty("ending").GetString());
        Assert.True(ending.GetProperty("final").GetBoolean());
        Assert.True(ending.GetProperty("fading").GetBoolean(), "The jackpot cuts out instead of fading.");
        await page.Clock.RunForAsync(1200);
        Assert.Equal(0, await page.Locator(".gma-exact-jackpot").CountAsync());
    }

    [Fact]
    public async Task HandOff_SettlesTheProfileInBehindTheGlidingPortrait()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(OpenGuessModeScript, new { imageId = ProfileImageA, slug = "payoff-handoff-test" });
        await page.WaitForFunctionAsync(
            "() => getComputedStyle(document.querySelector('#guessAgeContainer .gma-actions')).opacity === '1'");
        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                window.__gmaHandoff = new Promise(resolve => {
                    new MutationObserver((_, observer) => {
                        if (modalContent.classList.contains('guess-mode')) return;
                        observer.disconnect();
                        requestAnimationFrame(() => {
                            const settling = [...document.querySelectorAll('#athlete-profile > *')]
                                .filter(element => element.getAnimations().some(animation => animation.animationName === 'gma-profile-settle'));
                            const delays = settling.map(element => element.getAnimations()
                                .find(animation => animation.animationName === 'gma-profile-settle').effect.getTiming().delay);
                            resolve({
                                handoff: modalContent.classList.contains('gma-handoff'),
                                settling: settling.length,
                                portraitSettles: settling.some(element => element.classList.contains('portrait-wrapper')),
                                staggered: new Set(delays).size,
                                latest: Math.max(0, ...delays)
                            });
                        });
                    }).observe(modalContent, { attributes: true, attributeFilter: ['class'] });
                });
            }
            """);
        await page.Locator("#guessAgeContainer .gma-btn--ghost:not(#gmaSkipAll)").ClickAsync();
        var handoff = await page.EvaluateAsync<JsonElement>("() => window.__gmaHandoff");
        Assert.True(handoff.GetProperty("handoff").GetBoolean(), $"No hand-off reveal: {handoff}");
        Assert.True(handoff.GetProperty("settling").GetInt32() >= 3, $"Too few profile parts settle in: {handoff}");
        Assert.False(handoff.GetProperty("portraitSettles").GetBoolean());
        Assert.True(handoff.GetProperty("staggered").GetInt32() >= 3, $"The profile appears all at once: {handoff}");
        Assert.InRange(handoff.GetProperty("latest").GetDouble(), 100, 400);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content').classList.contains('gma-handoff')",
            null, new PageWaitForFunctionOptions { Timeout = 5000 });
    }
}
