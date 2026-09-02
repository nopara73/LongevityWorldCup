using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class GuessMyAgeBrowserTests
{
    [Fact]
    public async Task GuessSubmission_AnimatesThroughTheRevealAndBoundsCelebrationWork()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 1000 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
        }));

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'animated-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const range = document.getElementById('gmaRange');
                return modalContent?.dataset.athleteSlug === 'animated-history-test'
                    && modalContent.dataset.profileImageId === imageId
                    && modalContent.classList.contains('guess-mode')
                    && !modalContent.classList.contains('gma-result-ready')
                    && range?.disabled === false
                    && range.value === '33'
                    && range === document.activeElement
                    && !document.getElementById('gmaRealBubble');
            }
            """,
            ProfileImageA);

        var range = page.Locator("#gmaRange");
        await range.EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.EvaluateAsync(
            """
            () => {
                window.__gmaRevealAges = [];
                window.__gmaSubmitAt = Number.NaN;
                let resolveFirstReveal;
                window.__gmaFirstRevealPromise = new Promise(resolve => {
                    resolveFirstReveal = resolve;
                });
                window.__gmaResultAppliedPromise = new Promise(resolve => {
                    const status = document.getElementById('gmaStatus');
                    const resolveWhenApplied = () => {
                        if (!status.textContent.includes('Actual age: 41')) return false;
                        observer.disconnect();
                        resolve(true);
                        return true;
                    };
                    const observer = new MutationObserver(resolveWhenApplied);
                    observer.observe(status, { childList: true, characterData: true, subtree: true });
                    resolveWhenApplied();
                });
                window.__gmaReactionPromise = new Promise(resolve => {
                    const resolveWhenPresent = () => {
                        if (document.querySelectorAll('.gma-reaction').length !== 1) return false;
                        const reaction = document.querySelector('.gma-reaction');
                        observer.disconnect();
                        resolve({ count: 1, placement: reaction.dataset.placement });
                        return true;
                    };
                    const observer = new MutationObserver(resolveWhenPresent);
                    observer.observe(document.body, { childList: true, subtree: true });
                    resolveWhenPresent();
                });
                window.__gmaCelebrationSparkPromise = new Promise(resolve => {
                    const observedSparks = new Set();
                    const captureSparks = () => {
                        document.querySelectorAll('.gma-celebration-spark')
                            .forEach(spark => observedSparks.add(spark));
                        if (observedSparks.size < 48) return;
                        observer.disconnect();
                        resolve({
                            count: observedSparks.size,
                            quadrants: [...new Set([...observedSparks]
                                .map(spark => spark.dataset.quadrant)
                                .filter(Boolean))]
                        });
                    };
                    const observer = new MutationObserver(captureSparks);
                    observer.observe(document.body, { childList: true, subtree: true });
                    captureSparks();
                });
                document.querySelector('#guessAgeContainer .gma-btn--primary')
                    .addEventListener('click', () => {
                        window.__gmaSubmitAt = performance.now();
                    }, { capture: true, once: true });
                const original = window.positionGmaRevealBubble;
                window.positionGmaRevealBubble = (bubble, age) => {
                    if (bubble?.id === 'gmaRealBubble') {
                        window.__gmaRevealAges.push(Math.round(age));
                        if (resolveFirstReveal) {
                            resolveFirstReveal(performance.now());
                            resolveFirstReveal = null;
                        }
                    }
                    return original(bubble, age);
                };
            }
            """);
        await PauseClockAsync(page);
        await page.Locator("#guessAgeContainer .gma-btn--primary").EvaluateAsync("button => button.click()");

        Assert.True(await page.EvaluateAsync<bool>("() => window.__gmaResultAppliedPromise"));
        Assert.Equal(
            "1.98s",
            await page.Locator(".chrono-age-heading").EvaluateAsync<string>(
                "element => getComputedStyle(element).animationDuration"));
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.False(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        var reactionSnapshot = await page.EvaluateAsync<JsonElement>("() => window.__gmaReactionPromise");
        Assert.Equal(1, reactionSnapshot.GetProperty("count").GetInt32());
        Assert.Equal("beside-portrait", reactionSnapshot.GetProperty("placement").GetString());

        await page.Clock.RunForAsync(1979);
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        await page.Clock.RunForAsync(1);
        var firstRevealAt = await page.EvaluateAsync<double>("() => window.__gmaFirstRevealPromise");
        var submitAt = await page.EvaluateAsync<double>("() => window.__gmaSubmitAt");
        Assert.InRange(firstRevealAt - submitAt, 1979, 1981);
        await page.Clock.RunForAsync(250);
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());

        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-travelling') === true && document.getElementById('gmaRealBubble').textContent !== '41'"));
        Assert.NotEqual("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.Equal("true", await page.Locator("#gmaRealBubble").GetAttributeAsync("aria-hidden"));
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.False(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.True(await page.EvaluateAsync<bool>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['animated-history-test']?.byImage?.[imageId]?.value === 54",
            ProfileImageA));
        await page.Clock.RunForAsync(7000);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true"));
        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        var revealAges = await page.EvaluateAsync<int[]>("() => window.__gmaRevealAges");
        var changedAges = revealAges
            .Where((age, index) => index == 0 || age != revealAges[index - 1])
            .ToArray();
        Assert.True(changedAges.Length >= 55);
        Assert.All(
            changedAges.Zip(changedAges.Skip(1)),
            pair => Assert.Equal(1, Math.Abs(pair.Second - pair.First)));
        var directions = changedAges
            .Zip(changedAges.Skip(1), (first, second) => Math.Sign(second - first))
            .ToArray();
        Assert.Equal(1, directions.Zip(directions.Skip(1)).Count(pair => pair.First != pair.Second));
        Assert.InRange(changedAges.Min(), 16, 18);
        var travelBudget = await page.EvaluateAsync<double>(
            "() => Number(document.getElementById('gmaRealBubble').dataset.travelBudget)");
        Assert.InRange(travelBudget, 3200, 7000);
        Assert.Equal(0, await page.Locator(".gma-result-actions").CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        Assert.True(await page.Locator("#gmaBubble").EvaluateAsync<bool>(
            "element => Number.parseFloat(getComputedStyle(element).opacity) <= 0.45"));
        var settledBubbleThumbDelta = await page.EvaluateAsync<double>(
            """
            () => {
                const range = document.getElementById('gmaRange');
                const bubble = document.getElementById('gmaRealBubble');
                const rangeRect = range.getBoundingClientRect();
                const bubbleRect = bubble.getBoundingClientRect();
                const thumbSize = Number.parseFloat(
                    getComputedStyle(range).getPropertyValue('--gma-thumb-size'));
                const ratio = (Number(range.value) - Number(range.min))
                    / (Number(range.max) - Number(range.min));
                const expectedCenter = rangeRect.left + (thumbSize / 2)
                    + (ratio * (rangeRect.width - thumbSize));
                return Math.abs(expectedCenter - (bubbleRect.left + (bubbleRect.width / 2)));
            }
            """);
        Assert.InRange(settledBubbleThumbDelta, 0, 2.5);
        await page.GetByText("You beat the crowd!", new PageGetByTextOptions { Exact = true }).WaitForAsync();

        var sparkSnapshot = await page.EvaluateAsync<JsonElement>("() => window.__gmaCelebrationSparkPromise");
        var sparkCount = sparkSnapshot.GetProperty("count").GetInt32();
        Assert.Equal(48, sparkCount);
        Assert.InRange(sparkCount, 1, 64);
        Assert.True(sparkSnapshot.GetProperty("quadrants").GetArrayLength() >= 3);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const banner = document.querySelector('.gma-pop-banner');
                const region = document.getElementById('gmaPayoffRegion');
                const portrait = document.getElementById('modalProfilePic').getBoundingClientRect();
                const bannerRect = banner.getBoundingClientRect();
                return region.contains(banner) && bannerRect.top >= portrait.bottom;
            }
            """));
        await page.SetViewportSizeAsync(844, 390);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const selectors = [
                    '#guessAgeContainer',
                    '#gmaRealBubble',
                    '#gmaBubble',
                    '#closeAthleteDetailsModal',
                    '.gma-pop-banner'
                ];
                const insideViewport = selectors.every(selector => {
                    const element = document.querySelector(selector);
                    if (!element) return false;
                    const rect = element.getBoundingClientRect();
                    return rect.left >= -1 && rect.right <= window.innerWidth + 1;
                });
                return insideViewport
                    && document.documentElement.scrollWidth <= document.documentElement.clientWidth;
            }
            """));
        await page.Clock.RunForAsync(3400);
        Assert.Equal(0, await page.Locator(".gma-celebration-spark").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['animated-history-test']?.byImage?.[imageId]?.value === 54",
            ProfileImageA));

        await page.EvaluateAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'exact-animation-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
                document.getElementById('site-sticky-header')?.classList.add('visible');
                const scrolledPlayButton = document.querySelector('.scrolled-button');
                if (scrolledPlayButton) scrolledPlayButton.style.display = 'block';
            }
            """,
            ProfileImageB);
        await page.WaitForFunctionAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                const range = document.getElementById('gmaRange');
                return modalContent?.dataset.athleteSlug === 'exact-animation-test'
                    && modalContent.dataset.profileImageId === imageId
                    && modalContent.classList.contains('guess-mode')
                    && !modalContent.classList.contains('gma-result-ready')
                    && range?.disabled === false
                    && range.value === '33'
                    && range === document.activeElement
                    && !document.getElementById('gmaRealBubble');
            }
            """,
            ProfileImageB);
        await page.EvaluateAsync(
            """
            () => {
                window.proDiscounts = {
                    setPerfectGuessMarker: () => localStorage.setItem('gmaHasPerfectGuess', '1')
                };
            }
            """);
        await range.EvaluateAsync(
            "range => { range.value = '41'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        var exactResponse = page.WaitForResponseAsync(response =>
            response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && new Uri(response.Url).AbsolutePath.Equals(
                "/api/Guess/athlete-age",
                StringComparison.OrdinalIgnoreCase));
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        var exactResultResponse = await exactResponse;
        await exactResultResponse.FinishedAsync();
        var exactDetourReady = false;
        for (var elapsed = 0; elapsed < 2500 && !exactDetourReady; elapsed += 100)
        {
            await page.Clock.RunForAsync(100);
            exactDetourReady = await page.EvaluateAsync<bool>(
                """
                () => {
                    const bubble = document.getElementById('gmaRealBubble');
                    return bubble?.dataset.revealPhase === 'detour'
                        && bubble.textContent !== '41';
                }
                """);
        }
        Assert.True(exactDetourReady, "The exact reveal did not enter its detour phase.");
        Assert.Contains("Bullseye!", await page.Locator("#gmaStatus").InnerTextAsync(), StringComparison.Ordinal);

        var canvas = page.Locator(".gma-exact-confetti-canvas");
        for (var elapsed = 0; elapsed < 10000 && await canvas.CountAsync() == 0; elapsed += 250)
            await page.Clock.RunForAsync(250);
        Assert.Equal(1, await canvas.CountAsync());

        await page.Clock.RunForAsync(50);
        await page.Clock.RunForAsync(3550);
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.classList.contains('celebrate-exact')"));
        Assert.Equal(0, await page.Locator(".gma-reaction--exact").CountAsync());
        Assert.Equal(0, await page.Locator(".gma-celebration-spark").CountAsync());
        Assert.True(
            await page.Locator(".gma-exact-confetti-canvas").EvaluateAsync<int>(
                "element => Number(element.dataset.burstsFired || 0)") >= 4,
            "The exact jackpot did not fire its opening waves.");

        var jackpot = page.Locator(".gma-exact-jackpot");
        Assert.Equal(1, await jackpot.CountAsync());
        Assert.Equal(1, await canvas.CountAsync());
        Assert.Equal("true", await jackpot.GetAttributeAsync("aria-hidden"));
        Assert.Equal("true", await canvas.GetAttributeAsync("aria-hidden"));
        Assert.True(await page.Locator("#gmaStatus").EvaluateAsync<bool>(
            """
            element => !element.hidden
                && !element.closest('[aria-hidden="true"], [inert]')
                && element.textContent.includes('Actual age: 41')
                && element.textContent.includes('Bullseye!')
            """));
        Assert.Equal("exact-jackpot", await jackpot.GetAttributeAsync("data-gma-kind"));
        Assert.Equal("30", await jackpot.GetAttributeAsync("data-wave-count"));
        Assert.Equal("15000", await jackpot.GetAttributeAsync("data-duration"));
        Assert.Equal("2200", await canvas.GetAttributeAsync("data-particle-cap"));
        Assert.Equal("auto", await jackpot.EvaluateAsync<string>(
            "element => getComputedStyle(element).pointerEvents"));
        Assert.True(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal(420, await page.Locator(".gma-exact-time-icon").CountAsync());
        Assert.Equal(60, await page.Locator(".gma-exact-target").CountAsync());
        Assert.True((await page.Locator("[data-gma-motif]").EvaluateAllAsync<string[]>(
            "elements => [...new Set(elements.map(element => element.dataset.gmaMotif))]" )).Length >= 5);
        Assert.Equal(
            "BULLSEYE!!!",
            await page.Locator(".gma-exact-jackpot-title").InnerTextAsync());
        Assert.True(await page.Locator(".gma-exact-jackpot-title").EvaluateAsync<bool>(
            "element => Number.parseFloat(getComputedStyle(element).fontSize) >= 48"));
        var spectacleGeometry = await page.EvaluateAsync<bool[]>(
            """
            () => {
                const overlay = document.querySelector('.gma-exact-jackpot');
                const close = document.getElementById('closeAthleteDetailsModal');
                const overlayRect = overlay.getBoundingClientRect();
                const closeRect = close.getBoundingClientRect();
                const portrait = document.getElementById('modalProfilePic');
                const portraitRect = portrait.getBoundingClientRect();
                const modalLayer = Number.parseFloat(getComputedStyle(document.getElementById('detailsModal')).zIndex);
                const stickyHeader = document.getElementById('site-sticky-header');
                const scrolledPlayButton = document.querySelector('.scrolled-button');
                const timeXs = [...document.querySelectorAll('.gma-exact-time-icon')]
                    .map(element => Number.parseFloat(element.style.getPropertyValue('--gma-time-x')));
                return [
                    overlayRect.width >= window.innerWidth * 0.98,
                    overlayRect.height >= window.innerHeight * 0.98,
                    overlayRect.left <= 1 && overlayRect.top <= 1,
                    Math.max(...timeXs) - Math.min(...timeXs) >= 90,
                    modalLayer > Number.parseFloat(getComputedStyle(stickyHeader).zIndex)
                        && modalLayer > Number.parseFloat(getComputedStyle(scrolledPlayButton).zIndex),
                    close.contains(document.elementFromPoint(
                        closeRect.left + closeRect.width / 2,
                        closeRect.top + closeRect.height / 2)),
                    !portrait.contains(document.elementFromPoint(
                        portraitRect.left + portraitRect.width / 2,
                        portraitRect.top + portraitRect.height / 2)),
                    overlay.getAnimations({ subtree: true }).every(
                        animation => animation.effect.getTiming().iterations !== Infinity),
                    document.documentElement.scrollWidth <= document.documentElement.clientWidth
                ];
            }
            """);
        Assert.True(spectacleGeometry[0], "Jackpot width does not cover the viewport.");
        Assert.True(spectacleGeometry[1], "Jackpot height does not cover the viewport.");
        Assert.True(spectacleGeometry[2], "Jackpot is not anchored to the viewport origin.");
        Assert.True(spectacleGeometry[3], "Clock rain does not span the screen.");
        Assert.True(spectacleGeometry[4], "Athlete modal does not outrank visible site chrome.");
        Assert.True(spectacleGeometry[5], "Close control is not hit-testable above the jackpot.");
        Assert.True(spectacleGeometry[6], "Jackpot does not shield the interactive portrait.");
        Assert.True(spectacleGeometry[7], "Jackpot contains an unbounded animation.");
        Assert.True(spectacleGeometry[8], "Jackpot introduces horizontal document overflow.");

        var earlyCanvasWork = await canvas.EvaluateAsync<double[]>(
            """
            element => [
                Number(element.dataset.activeParticles),
                Number(element.dataset.totalSpawned),
                Number(element.dataset.backingPixels),
                Number(element.dataset.burstsFired)
            ]
            """);
        Assert.InRange(earlyCanvasWork[0], 1, 2_200);
        Assert.InRange(earlyCanvasWork[2], 1, 3_000_000);
        var observedWaveCount = (int)earlyCanvasWork[3];
        Assert.InRange(observedWaveCount, 4, 30);
        var expectedDesktopParticles = observedWaveCount switch
        {
            <= 5 => observedWaveCount * 110,
            <= 13 => 550 + ((observedWaveCount - 5) * 190),
            <= 24 => 2_070 + ((observedWaveCount - 13) * 290),
            _ => 5_260 + ((observedWaveCount - 24) * 460)
        };
        Assert.Equal(expectedDesktopParticles, earlyCanvasWork[1]);

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await page.Clock.RunForAsync(17);
            await page.WaitForFunctionAsync(
                """
                () => {
                    const canvas = document.querySelector('.gma-exact-confetti-canvas');
                    return canvas
                        && Number.parseFloat(canvas.style.width) === window.innerWidth
                        && Number.parseFloat(canvas.style.height) === window.innerHeight;
                }
                """);
            Assert.True(await page.EvaluateAsync<bool>(
                """
                () => {
                    const overlay = document.querySelector('.gma-exact-jackpot').getBoundingClientRect();
                    const canvas = document.querySelector('.gma-exact-confetti-canvas');
                    const title = document.querySelector('.gma-exact-jackpot-title').getBoundingClientRect();
                    return overlay.width >= window.innerWidth * 0.98
                        && overlay.height >= window.innerHeight * 0.98
                        && title.left >= -1
                        && title.right <= window.innerWidth + 1
                        && document.querySelector('.gma-exact-jackpot-title').scrollWidth
                            <= document.querySelector('.gma-exact-jackpot-title').clientWidth + 1
                        && Number(canvas.dataset.backingPixels) <= 3000000
                        && document.documentElement.scrollWidth <= document.documentElement.clientWidth;
                }
                """));
        }

        // The exact result is a sustained jackpot, not a single decorative pop.
        for (var elapsed = 0;
             elapsed < 10000 && await canvas.EvaluateAsync<int>("element => Number(element.dataset.burstsFired || 0)") < 30;
             elapsed += 500)
        {
            await page.Clock.RunForAsync(500);
        }
        var completedWaves = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const canvas = document.querySelector('.gma-exact-confetti-canvas');
                const jackpot = canvas?.closest('.gma-exact-jackpot');
                return {
                    activeParticles: Number(canvas?.dataset.activeParticles),
                    totalSpawned: Number(canvas?.dataset.totalSpawned),
                    burstsFired: Number(canvas?.dataset.burstsFired),
                    phase: jackpot?.dataset.phase
                };
            }
            """);
        Assert.Equal(30, completedWaves.GetProperty("burstsFired").GetInt32());
        Assert.InRange(completedWaves.GetProperty("activeParticles").GetInt32(), 1, 2_200);
        Assert.InRange(completedWaves.GetProperty("totalSpawned").GetInt32(), 5_000, 8_500);
        Assert.Equal("finale", completedWaves.GetProperty("phase").GetString());

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.Clock.RunForAsync(1000);
        Assert.Equal(
            0,
            await page.Locator(".gma-exact-jackpot, .gma-exact-confetti-canvas, .gma-exact-time-icon, .gma-exact-target").CountAsync());
        Assert.False(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_AcceptedExactResultSurvivesCelebrationRendererFailure()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"crowdAge":40,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
        }));

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'celebration-renderer-failure-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode', 'gma-fast');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.EvaluateAsync(
            """
            () => {
                window.proDiscounts = {
                    setPerfectGuessMarker: () => localStorage.setItem('gmaHasPerfectGuess', '1')
                };
                window.animateActualAgeReveal = async (bubble, _startAge, actualAge) => {
                    bubble.textContent = String(actualAge);
                    bubble.dataset.revealPhase = 'settled';
                    bubble.classList.remove('is-travelling');
                    bubble.classList.add('is-settled');
                    window.positionGmaRevealBubble(bubble, actualAge);
                    return true;
                };
                HTMLCanvasElement.prototype.getContext = () => {
                    throw new Error('Synthetic jackpot renderer failure');
                };
                const range = document.getElementById('gmaRange');
                range.value = '41';
                range.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.GetByText("Bullseye!", new PageGetByTextOptions { Exact = true }).WaitForAsync();

        var statusCopy = await page.Locator("#gmaStatus").InnerTextAsync();
        Assert.Contains("Actual age: 41", statusCopy);
        Assert.Contains("Bullseye!", statusCopy);
        Assert.DoesNotContain("could not submit", statusCopy.ToLowerInvariant());
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode') && element.classList.contains('gma-result-ready')"));
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.classList.contains('celebrate-exact') && !element.classList.contains('is-submitting')"));
        Assert.True(await page.Locator("#gmaRange").IsDisabledAsync());
        Assert.Equal(0, await page.Locator(".gma-exact-jackpot, .gma-exact-confetti-canvas").CountAsync());
        Assert.False(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.EvaluateAsync<bool>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['celebration-renderer-failure-test']?.byImage?.[imageId]?.exact === true",
            ProfileImageA));
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
        Assert.Empty(errors);
    }

}
