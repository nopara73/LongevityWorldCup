using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;


public sealed partial class GuessMyAgeBrowserTests
{
    [Fact]
    public async Task GuessSubmission_ReducedMotionRevealsImmediatelyWithoutReactionParticles()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/Guess/athlete-age**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "application/json",
            Body = """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
        }));

        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-motion-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
                const range = document.getElementById('gmaRange');
                range.value = '54';
                range.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """,
            ProfileImageA);

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true");

        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>(
            "element => getComputedStyle(element).animationName === 'none' && element.getAnimations().length === 0"));
        var reducedMotionOldActions = await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<double[]>(
            "element => [element.getBoundingClientRect().height, Number.parseFloat(getComputedStyle(element).opacity)]");
        Assert.InRange(reducedMotionOldActions[0], 0, 0.5);
        Assert.Equal(0, reducedMotionOldActions[1]);
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        Assert.Equal(0, await page.Locator(".gma-celebration-spark").CountAsync());
        await page.GetByText("You beat the crowd!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        await PauseClockAsync(page);
        await page.Clock.RunForAsync(1800);
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode')"));
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        await page.Clock.RunForAsync(4000);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        await page.Clock.RunForAsync(50);
        Assert.Equal(
            0,
            await page.Locator(".gma-pop-banner, .gma-reaction, .gma-celebration-spark, .gma-exact-jackpot").CountAsync());
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => getComputedStyle(element).animationName === 'none' && element.getAnimations().length === 0"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_ExactResultSuppressesTheJackpotOverlayUnderReducedMotion()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce,
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
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                const originalSetTimeout = window.setTimeout.bind(window);
                window.__gmaScheduledDelays = [];
                window.setTimeout = (callback, delay, ...args) => {
                    window.__gmaScheduledDelays.push(Number(delay));
                    return originalSetTimeout(callback, delay, ...args);
                };
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-exact-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
                window.proDiscounts = {
                    setPerfectGuessMarker: () => localStorage.setItem('gmaHasPerfectGuess', '1')
                };
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '41'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.GetByText("Bullseye!", new PageGetByTextOptions { Exact = true }).WaitForAsync();

        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.classList.contains('celebrate-exact')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Equal(
            0,
            await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-exact-jackpot, .gma-exact-confetti-canvas, .gma-exact-time-icon, .gma-exact-target").CountAsync());
        Assert.False(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal("none", await page.EvaluateAsync<string>(
            """
            () => {
                const probe = document.createElement('div');
                probe.className = 'gma-exact-jackpot';
                document.querySelector('#detailsModal .modal-content').appendChild(probe);
                const display = getComputedStyle(probe).display;
                probe.remove();
                return display;
            }
            """));
        await page.WaitForFunctionAsync(
            "() => document.getElementById('guessAgeContainer')?.getAnimations({ subtree: true }).length === 0");
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.getAnimations({ subtree: true }).length === 0"));
        var reducedExactDelays = await page.EvaluateAsync<double[]>(
            "() => window.__gmaScheduledDelays");
        Assert.Contains(5000, reducedExactDelays);
        Assert.DoesNotContain(16000, reducedExactDelays);
        Assert.True(await page.EvaluateAsync<bool>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['reduced-exact-history-test']?.byImage?.[imageId]?.exact === true",
            ProfileImageA));
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.gma-exact-jackpot, .gma-exact-confetti-canvas')");
        Assert.Equal(0, await page.Locator(".gma-exact-jackpot, .gma-exact-confetti-canvas").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_SettlesAnActiveRevealWhenReducedMotionTurnsOn()
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
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'motion-change-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-travelling') === true");

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true");

        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-exact-jackpot").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['motion-change-history-test']?.byImage?.[imageId]?.value === 54",
            ProfileImageA));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_RequestFailureRestoresRetryableControls()
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
            Status = 503,
            ContentType = "application/json",
            Body = "{}"
        }));

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'failed-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);

        var range = page.Locator("#gmaRange");
        var submit = page.Locator("#guessAgeContainer .gma-btn--primary");
        await range.EvaluateAsync(
            "range => { range.value = '50'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await submit.ClickAsync();
        await page.GetByText("We could not submit your guess. Please try again.", new PageGetByTextOptions { Exact = true }).WaitForAsync();

        Assert.True(await range.IsEnabledAsync());
        Assert.True(await submit.IsEnabledAsync());
        Assert.False(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
        Assert.Null(await range.GetAttributeAsync("aria-hidden"));
        Assert.False(await range.EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => !JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['failed-history-test']"));
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessState_MigratesLegacyStateAndSeparatesImagesAndAthletes()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.LwcGuessState?.getAll === 'function'");

        var diagnostics = await page.EvaluateAsync<GuessStateHistoryDiagnostics>(
            """
            args => {
                const exact = { value: 42, skipped: false, first: true, exact: true };
                localStorage.setItem('gmaAllGuesses', JSON.stringify({
                    'history-athlete': exact,
                    'uppercase-history': {
                        byImage: {
                            [args.imageA.toUpperCase()]: {
                                value: 31,
                                skipped: false,
                                first: false,
                                exact: false
                            }
                        }
                    }
                }));

                const identity = (slug, imageId) => ({ AthleteSlug: slug, ProfileImageId: imageId });
                const legacy = window.LwcGuessState.get(
                    identity('history_athlete', args.imageA.toUpperCase()));
                const imageBInitially = window.LwcGuessState.get(
                    identity('history_athlete', args.imageB));
                window.LwcGuessState.set(identity('history_athlete', args.imageB), {
                    value: null,
                    skipped: true,
                    first: false,
                    exact: false
                });

                const restored = window.LwcGuessState.get(
                    identity('history_athlete', args.imageA));
                const otherInitially = window.LwcGuessState.get(
                    identity('other-athlete', args.imageA));
                const normalizedNested = window.LwcGuessState.get(
                    identity('uppercase-history', args.imageA));
                window.LwcGuessState.set(identity('other-athlete', args.imageA), {
                    value: 33,
                    skipped: false,
                    first: false,
                    exact: false
                });

                const stored = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
                return {
                    LegacyValue: Number(legacy?.value),
                    ImageBInitiallyNull: imageBInitially === null,
                    RestoredValue: Number(restored?.value),
                    OtherInitiallyNull: otherInitially === null,
                    NormalizedNestedValue: Number(normalizedNested?.value),
                    OtherValue: Number(window.LwcGuessState.get(
                        identity('other-athlete', args.imageA))?.value),
                    PrimaryValueAfterOtherWrite: Number(window.LwcGuessState.get(
                        identity('history-athlete', args.imageA))?.value),
                    LegacyFlatStateRemoved: !Object.prototype.hasOwnProperty.call(
                        stored['history-athlete'] || {}, 'value'),
                    PrimaryImageCount: Object.keys(stored['history-athlete']?.byImage || {}).length,
                    OtherImageCount: Object.keys(stored['other-athlete']?.byImage || {}).length,
                    LowercaseImageKeyExists: Object.prototype.hasOwnProperty.call(
                        stored['history-athlete']?.byImage || {}, args.imageA),
                    UppercaseNestedKeyNormalized: Object.prototype.hasOwnProperty.call(
                        stored['uppercase-history']?.byImage || {}, args.imageA)
                        && !Object.prototype.hasOwnProperty.call(
                            stored['uppercase-history']?.byImage || {}, args.imageA.toUpperCase())
                };
            }
            """,
            new { imageA = ProfileImageA, imageB = ProfileImageB });

        Assert.Equal(42, diagnostics.LegacyValue);
        Assert.True(diagnostics.ImageBInitiallyNull);
        Assert.Equal(42, diagnostics.RestoredValue);
        Assert.True(diagnostics.OtherInitiallyNull);
        Assert.Equal(31, diagnostics.NormalizedNestedValue);
        Assert.Equal(33, diagnostics.OtherValue);
        Assert.Equal(42, diagnostics.PrimaryValueAfterOtherWrite);
        Assert.True(diagnostics.LegacyFlatStateRemoved);
        Assert.Equal(2, diagnostics.PrimaryImageCount);
        Assert.Equal(1, diagnostics.OtherImageCount);
        Assert.True(diagnostics.LowercaseImageKeyExists);
        Assert.True(diagnostics.UppercaseNestedKeyNormalized);
    }

    [Fact]
    public async Task StaleProfileImageConflict_RefetchesPortraitWithoutRevealingOrSavingGuess()
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

        var athleteSnapshotRequests = 0;
        await context.RouteAsync("**/api/data/athletes*", async route =>
        {
            athleteSnapshotRequests++;
            var response = await route.FetchAsync();
            var athletes = JsonNode.Parse(await response.TextAsync())!.AsArray();
            var patrick = athletes
                .OfType<JsonObject>()
                .Single(athlete => athlete["Name"]?.GetValue<string>() == "Patrick Ruff");
            var isInitialSnapshot = athleteSnapshotRequests == 1;
            var imageId = isInitialSnapshot ? ProfileImageA : ProfileImageB;
            var imageUrl = isInitialSnapshot
                ? "/assets/content-images/trollface.png?v=image-a"
                : "/assets/favicon-512x512.png?v=image-b";
            patrick["ProfileImageId"] = imageId;
            patrick["ProfilePic"] = imageUrl;
            patrick["ProfilePicThumb"] = imageUrl;
            patrick["ProfilePicLeaderboardThumb"] = imageUrl;

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = response.Status,
                ContentType = "application/json",
                Body = athletes.ToJsonString()
            });
        });

        var guessRequestUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            guessRequestUrl.TrySetResult(route.Request.Url);
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 409,
                ContentType = "application/json",
                Body = """{"code":"profile_image_changed","message":"Profile picture changed."}"""
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(
            "/athlete/patrick-ruff?guessmyage=1",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "imageId => document.querySelector('#detailsModal .modal-content')?.dataset.profileImageId === imageId && document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')",
            ProfileImageA);

        var range = page.Locator("#gmaRange");
        await range.EvaluateAsync(
            "range => { range.value = '50'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        var submittedUrl = await guessRequestUrl.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Contains($"profileImageId={ProfileImageA}", submittedUrl, StringComparison.Ordinal);
        await page.WaitForFunctionAsync(
            """
            imageId => {
                const modal = document.querySelector('#detailsModal .modal-content');
                return modal?.dataset.profileImageId === imageId
                    && !modal.classList.contains('is-loading')
                    && modal.classList.contains('guess-mode')
                    && document.getElementById('gmaRange')?.disabled === false;
            }
            """,
            ProfileImageB);

        Assert.True(athleteSnapshotRequests >= 2);
        Assert.DoesNotContain("Actual age:", await page.Locator("#gmaStatus").InnerTextAsync(), StringComparison.Ordinal);
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const history = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['patrick-ruff'];
                return !history || Object.keys(history.byImage || {}).length === 0;
            }
            """));
        Assert.Contains("favicon-512x512.png", await page.Locator("#modalProfilePic").GetAttributeAsync("src"));
        Assert.Empty(errors);
    }

}
