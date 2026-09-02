using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(BrowserTestCollections.Integration)]
public sealed partial class GuessMyAgeBrowserTests(PlaywrightBrowserFixture browserFixture)
    : IsolatedBrowserIntegrationTest(browserFixture)
{
    internal const string ProfileImageA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    internal const string ProfileImageB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    internal static async Task PauseClockAsync(IPage page)
    {
        // Fix the wall clock first so PauseAt cannot lose a race to its own Playwright round trip.
        var pauseAt = DateTime.UtcNow;
        await page.Clock.SetFixedTimeAsync(pauseAt);
        await page.Clock.PauseAtAsync(pauseAt);
    }

    [Fact]
    public async Task PatrickRoute_CompletesAFilteredGuessWithoutAddingItToCrowdAge()
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

        var guessResponseBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        page.Response += async (_, response) =>
        {
            if (!response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
                || !new Uri(response.Url).AbsolutePath.Equals("/api/Guess/athlete-age", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            try
            {
                guessResponseBody.TrySetResult(await response.TextAsync());
            }
            catch (Exception exception)
            {
                guessResponseBody.TrySetException(exception);
            }
        };

        await page.GotoAsync(
            "/athlete/patrick-ruff?guessmyage=1",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode') === true");
        var profileImageId = await page.Locator("#detailsModal .modal-content")
            .GetAttributeAsync("data-profile-image-id");
        Assert.NotNull(profileImageId);
        Assert.Equal(64, profileImageId!.Length);

        var today = DateTime.UtcNow.Date;
        var birthday = new DateTime(today.Year, 3, 8, 0, 0, 0, DateTimeKind.Utc);
        var actualAge = today.Year - 1985 - (today < birthday ? 1 : 0);
        var filteredGuess = (int)Math.Floor(actualAge * 1.30) + 1;
        Assert.InRange(filteredGuess, 10, 110);

        var crowdCount = page.Locator("#crowdCount");
        var crowdCountBefore = int.Parse(await crowdCount.InnerTextAsync());
        var range = page.Locator("#gmaRange");
        await range.EvaluateAsync(
            "(element, value) => { element.value = String(value); element.dispatchEvent(new Event('input', { bubbles: true })); }",
            filteredGuess);
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        var responseBody = await guessResponseBody.Task.WaitAsync(TimeSpan.FromSeconds(15));
        using var responseJson = JsonDocument.Parse(responseBody);
        var responseRoot = responseJson.RootElement;
        Assert.False(responseRoot.GetProperty("guessAccepted").GetBoolean());
        Assert.Equal(actualAge, responseRoot.GetProperty("actualAge").GetInt32());
        Assert.Equal(crowdCountBefore, responseRoot.GetProperty("crowdCount").GetInt32());

        var status = page.Locator("#gmaStatus");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaStatus')?.textContent?.includes('Actual age:') === true");
        Assert.DoesNotContain("not accepted", await status.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(crowdCountBefore.ToString(), await crowdCount.InnerTextAsync());

        await page.WaitForFunctionAsync(
            "args => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['patrick-ruff']?.byImage?.[args.imageId]?.value === args.guess",
            new { imageId = profileImageId, guess = filteredGuess });
        await PauseClockAsync(page);
        await page.Clock.RunForAsync(15000);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode') === false");
        Assert.DoesNotContain("guessmyage", page.Url, StringComparison.OrdinalIgnoreCase);

        await page.GotoAsync(
            "/athlete/patrick-ruff",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('detailsModal')?.style.display === 'block' && !document.querySelector('#detailsModal .modal-content')?.classList.contains('is-loading')");
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode')"));
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-done')"));
    }

    [Fact]
    public async Task GuessMode_RestoresTheDesktopPromptCadenceAndKeepsFastReplayShort()
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

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'entrance-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.remove('gma-fast');
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);

        var freshTiming = await page.EvaluateAsync<string[]>(
            """
            () => {
                const heading = getComputedStyle(document.querySelector('#guessAgeContainer .gma-heading'));
                const slider = getComputedStyle(document.querySelector('#guessAgeContainer .gma-slider-wrap'));
                const actions = getComputedStyle(document.querySelector('#guessAgeContainer .gma-actions'));
                const portrait = getComputedStyle(document.querySelector('#athlete-profile .portrait-wrapper'));
                return [
                    heading.animationName,
                    heading.animationDuration,
                    heading.animationTimingFunction,
                    slider.animationName,
                    slider.animationDuration,
                    slider.animationDelay,
                    actions.animationName,
                    actions.animationDuration,
                    actions.animationDelay,
                    portrait.animationName
                ];
            }
            """);

        Assert.Equal("gma-prompt-type", freshTiming[0]);
        Assert.Equal("1.98s", freshTiming[1]);
        Assert.Contains("steps(15", freshTiming[2]);
        Assert.Equal("gma-control-fade", freshTiming[3]);
        Assert.Equal("0.35s", freshTiming[4]);
        Assert.Equal("0.84s", freshTiming[5]);
        Assert.Equal("gma-control-fade", freshTiming[6]);
        Assert.Equal("0.35s", freshTiming[7]);
        Assert.Equal("0.84s", freshTiming[8]);
        Assert.Equal("gma-portrait-enter", freshTiming[9]);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#guessAgeContainer .gma-heading')?.getBoundingClientRect().width > 0");
        Assert.True(await page.Locator("#guessAgeContainer .gma-heading").IsVisibleAsync());
        await page.WaitForFunctionAsync(
            """
            () => document.activeElement?.id === 'gmaRange'
                && Number.parseFloat(getComputedStyle(
                    document.querySelector('#guessAgeContainer .gma-slider-wrap')).opacity) > 0.05
            """);
        var focusedInstrument = await page.EvaluateAsync<string[]>(
            """
            () => {
                const range = document.getElementById('gmaRange');
                const slider = range.closest('.gma-slider-wrap');
                const rangeStyle = getComputedStyle(range);
                return [
                    String(range === document.activeElement),
                    rangeStyle.outlineStyle,
                    rangeStyle.outlineWidth,
                    getComputedStyle(slider).opacity
                ];
            }
            """);
        Assert.Equal("true", focusedInstrument[0]);
        Assert.NotEqual("none", focusedInstrument[1]);
        Assert.NotEqual("0px", focusedInstrument[2]);
        Assert.True(double.Parse(focusedInstrument[3], System.Globalization.CultureInfo.InvariantCulture) > 0.05);

        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('#guessAgeContainer')
                ?.getAnimations({ subtree: true })
                .every(animation => animation.playState === 'finished') === true
            """);
        var maximumBubbleThumbDelta = await page.EvaluateAsync<double>(
            """
            () => {
                const range = document.getElementById('gmaRange');
                const bubble = document.getElementById('gmaBubble');
                const thumbSize = Number.parseFloat(
                    getComputedStyle(range).getPropertyValue('--gma-thumb-size'));
                const min = Number(range.min);
                const max = Number(range.max);
                let maximumDelta = 0;
                for (const value of [1, 20, 41, 65, 100, 130]) {
                    range.value = String(value);
                    range.dispatchEvent(new Event('input', { bubbles: true }));
                    const rangeRect = range.getBoundingClientRect();
                    const bubbleRect = bubble.getBoundingClientRect();
                    const ratio = (value - min) / (max - min);
                    const expectedCenter = rangeRect.left + (thumbSize / 2)
                        + (ratio * (rangeRect.width - thumbSize));
                    const bubbleCenter = bubbleRect.left + (bubbleRect.width / 2);
                    maximumDelta = Math.max(maximumDelta, Math.abs(expectedCenter - bubbleCenter));
                }
                return maximumDelta;
            }
            """);
        Assert.InRange(maximumBubbleThumbDelta, 0, 4);

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.classList.remove('guess-mode');
                modalContent.classList.add('gma-fast', 'guess-mode');
            }
            """);
        var fastTiming = await page.EvaluateAsync<string[]>(
            """
            () => {
                const heading = getComputedStyle(document.querySelector('#guessAgeContainer .gma-heading'));
                const slider = getComputedStyle(document.querySelector('#guessAgeContainer .gma-slider-wrap'));
                const actions = getComputedStyle(document.querySelector('#guessAgeContainer .gma-actions'));
                return [heading.animationDuration, slider.animationDuration, slider.animationDelay, actions.animationDelay];
            }
            """);
        Assert.Equal(new[] { "0.22s", "0.22s", "0.14s", "0.28s" }, fastTiming);
    }

    [Theory]
    [InlineData("min")]
    [InlineData("max")]
    public async Task GuessSubmission_EndpointRestoresTheOriginalTrollfaceAndForcedRickroll(string endpoint)
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

        var guessRequests = 0;
        await context.RouteAsync("**/api/Guess/athlete-age**", route =>
        {
            guessRequests++;
            return route.AbortAsync();
        });

        await using var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);

        var rickrollAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rickrollRequests = 0;
        string? rickrollMethod = null;
        var rickrollWasMainFrameNavigation = false;
        await page.RouteAsync(
            new Regex("^https://www\\.youtube\\.com/watch\\?v=dQw4w9WgXcQ$", RegexOptions.IgnoreCase),
            async route =>
            {
                rickrollRequests++;
                rickrollMethod = route.Request.Method;
                rickrollWasMainFrameNavigation = route.Request.IsNavigationRequest
                    && route.Request.Frame == page.MainFrame;
                rickrollAttempt.TrySetResult(true);
                await route.FulfillAsync(new RouteFulfillOptions { Status = 204 });
            });

        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'rickroll-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await PauseClockAsync(page);

        await page.Locator("#gmaRange").EvaluateAsync(
            "(range, endpoint) => { range.value = range[endpoint]; range.dispatchEvent(new Event('input', { bubbles: true })); }",
            endpoint);
        await page.Locator("#guessAgeContainer .gma-btn--primary").EvaluateAsync("button => button.click()");

        var takeover = page.Locator("#gmaTrollfaceContainer.gma-trollface-container");
        await takeover.WaitForAsync();
        Assert.Equal(1, await takeover.CountAsync());
        Assert.Equal(0, guessRequests);
        Assert.Equal("status", await takeover.GetAttributeAsync("role"));
        Assert.Equal("Trollface. Redirecting to the Rickroll.", await takeover.GetAttributeAsync("aria-label"));
        Assert.True(await takeover.EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.True(await page.Locator("#athlete-profile").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await takeover.EvaluateAsync<bool>("element => element === document.activeElement"));
        await page.Keyboard.PressAsync("Shift+Tab");
        Assert.True(await takeover.EvaluateAsync<bool>("element => element === document.activeElement"));

        var trollImage = takeover.Locator("img");
        Assert.Contains("/assets/content-images/trollface.png?v=", await trollImage.GetAttributeAsync("src"));
        Assert.Equal("Trollface", await trollImage.GetAttributeAsync("alt"));
        Assert.Equal("true", await trollImage.GetAttributeAsync("aria-hidden"));
        Assert.True(await trollImage.EvaluateAsync<bool>(
            """
            image => image.complete
                ? image.naturalWidth > 0
                : new Promise(resolve => {
                    image.addEventListener('load', () => resolve(image.naturalWidth > 0), { once: true });
                    image.addEventListener('error', () => resolve(false), { once: true });
                })
            """));
        var style = await takeover.EvaluateAsync<string[]>(
            """
            element => {
                const overlay = getComputedStyle(element);
                const image = getComputedStyle(element.querySelector('img'));
                return [
                    overlay.position,
                    overlay.animationName,
                    overlay.animationDuration,
                    overlay.animationTimingFunction,
                    overlay.animationFillMode,
                    overlay.zIndex,
                    image.objectFit
                ];
            }
            """);
        Assert.Equal(
            new[] { "absolute", "gmaTrollSlideUpOverlay", "2s", "ease-out", "forwards", "10000", "cover" },
            style);

        var geometry = await takeover.EvaluateAsync<double[]>(
            """
            element => {
                const animation = element.getAnimations()[0];
                const modal = element.parentElement.getBoundingClientRect();
                animation.pause();
                const sample = time => {
                    animation.currentTime = time;
                    return element.getBoundingClientRect().top;
                };
                const startTop = sample(0);
                const middleTop = sample(1000);
                const endTop = sample(2000);
                const overlay = element.getBoundingClientRect();
                const image = element.querySelector('img').getBoundingClientRect();
                return [
                    startTop,
                    middleTop,
                    endTop,
                    modal.top,
                    modal.bottom,
                    modal.width,
                    modal.height,
                    overlay.width,
                    overlay.height,
                    image.width,
                    image.height
                ];
            }
            """);
        Assert.InRange(Math.Abs(geometry[0] - geometry[4]), 0, 2);
        Assert.True(geometry[1] < geometry[0] - (geometry[6] * 0.35));
        Assert.True(geometry[1] > geometry[2] + (geometry[6] * 0.15));
        Assert.InRange(Math.Abs(geometry[2] - geometry[3]), 0, 2);
        Assert.InRange(Math.Abs(geometry[7] - geometry[5]), 0, 3);
        Assert.InRange(Math.Abs(geometry[8] - geometry[6]), 0, 3);
        Assert.InRange(Math.Abs(geometry[9] - geometry[7]), 0, 1);
        Assert.InRange(Math.Abs(geometry[10] - geometry[8]), 0, 1);
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.elementFromPoint(innerWidth / 2, innerHeight / 2)?.closest('#gmaTrollfaceContainer') !== null"));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => !sessionStorage.getItem('gmaSubmittedOnce') && !JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['rickroll-history-test']"));

        await page.Clock.RunForAsync(1990);
        Assert.False(rickrollAttempt.Task.IsCompleted);
        await page.Clock.RunForAsync(20);
        await rickrollAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, rickrollRequests);
        Assert.Equal("GET", rickrollMethod);
        Assert.True(rickrollWasMainFrameNavigation);
        Assert.Equal(0, guessRequests);
        await page.Clock.RunForAsync(2000);
        Assert.Equal(0, await page.Locator(".gma-trollface-container").CountAsync());
        Assert.False(await page.Locator("#athlete-profile").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.Locator("#gmaRange").EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.Equal(1, rickrollRequests);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_EndpointCancelsOnReopenAndIsStaticUnderReducedMotion()
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

        var rickrollAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var rickrollRequests = 0;
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync(
            new Regex("^https://www\\.youtube\\.com/watch\\?v=dQw4w9WgXcQ$", RegexOptions.IgnoreCase),
            async route =>
            {
                rickrollRequests++;
                rickrollAttempt.TrySetResult(true);
                await route.FulfillAsync(new RouteFulfillOptions { Status = 204 });
            });

        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-rickroll-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await PauseClockAsync(page);

        var range = page.Locator("#gmaRange");
        var submit = page.Locator("#guessAgeContainer .gma-btn--primary");
        await range.EvaluateAsync(
            "range => { range.value = range.min; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await submit.EvaluateAsync("button => button.click()");
        await page.Locator("#gmaTrollfaceContainer").WaitForAsync();

        await page.EvaluateAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'reduced-rickroll-history-test-reopened';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.remove('guess-mode');
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageB);
        await page.EvaluateAsync("() => new Promise(resolve => queueMicrotask(resolve))");
        Assert.True(await page.EvaluateAsync<bool>(
            "() => !document.querySelector('.gma-trollface-container') && !document.getElementById('athlete-profile').inert && !document.getElementById('guessAgeContainer').inert"));
        await page.Clock.RunForAsync(2100);
        Assert.Equal(0, rickrollRequests);

        await range.EvaluateAsync(
            "range => { range.value = range.max; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await submit.EvaluateAsync("button => button.click()");
        var takeover = page.Locator("#gmaTrollfaceContainer");
        await takeover.WaitForAsync();

        Assert.Equal("none", await takeover.EvaluateAsync<string>(
            "element => getComputedStyle(element).animationName"));
        Assert.Equal("0px", await takeover.EvaluateAsync<string>(
            "element => getComputedStyle(element).bottom"));
        Assert.Equal(0, await takeover.EvaluateAsync<int>(
            "element => element.getAnimations({ subtree: true }).length"));
        var settledGeometry = await takeover.EvaluateAsync<double[]>(
            """
            element => {
                const overlay = element.getBoundingClientRect();
                const modal = element.parentElement.getBoundingClientRect();
                return [overlay.top, modal.top, overlay.width, modal.width, overlay.height, modal.height];
            }
            """);
        Assert.InRange(Math.Abs(settledGeometry[0] - settledGeometry[1]), 0, 1);
        Assert.InRange(Math.Abs(settledGeometry[2] - settledGeometry[3]), 0, 3);
        Assert.InRange(Math.Abs(settledGeometry[4] - settledGeometry[5]), 0, 3);

        await page.Clock.RunForAsync(1990);
        Assert.False(rickrollAttempt.Task.IsCompleted);
        await page.Clock.RunForAsync(20);
        await rickrollAttempt.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, rickrollRequests);

        await page.Clock.RunForAsync(2000);
        Assert.Equal(0, await page.Locator(".gma-trollface-container").CountAsync());
        Assert.False(await page.Locator("#athlete-profile").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>("element => element.inert"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_CompletesFilteredAndAcceptedGuesses()
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

        var guessRequests = 0;
        var requestedUrls = new List<string>();
        var releaseFirstGuess = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            guessRequests++;
            requestedUrls.Add(route.Request.Url);
            if (guessRequests == 1)
            {
                await releaseFirstGuess.Task;
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = """{"crowdAge":39,"crowdCount":12,"actualAge":41,"guessAccepted":false}"""
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"crowdAge":40,"crowdCount":1,"actualAge":40,"guessAccepted":true}"""
            });
        });

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
                modalContent.dataset.athleteSlug = 'history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);

        var range = page.Locator("#gmaRange");
        var submit = page.Locator("#guessAgeContainer .gma-btn--primary");
        var status = page.Locator("#gmaStatus");
        Assert.Null(await page.Locator("#guessAgeContainer").GetAttributeAsync("aria-live"));
        await page.EvaluateAsync(
            """
            () => {
                window.__gmaAcceptedResultPromise = new Promise(resolve => {
                    const resolveWhenReady = () => {
                        const status = document.getElementById('gmaStatus');
                        if (!status?.textContent.includes('Actual age: 41')
                            || document.querySelectorAll('.gma-reaction').length !== 1) return;
                        const reaction = document.querySelector('.gma-reaction');
                        const rect = reaction.getBoundingClientRect();
                        observer.disconnect();
                        resolve({
                            count: 1,
                            placement: reaction.dataset.placement,
                            fontSize: Number.parseFloat(getComputedStyle(reaction).fontSize),
                            left: rect.left,
                            right: rect.right,
                            top: rect.top,
                            bottom: rect.bottom,
                            viewportWidth: window.innerWidth,
                            viewportHeight: window.innerHeight
                        });
                    };
                    const observer = new MutationObserver(resolveWhenReady);
                    observer.observe(document.getElementById('detailsModal'), {
                        childList: true,
                        characterData: true,
                        subtree: true
                    });
                    resolveWhenReady();
                });
            }
            """);
        await PauseClockAsync(page);

        await range.EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        try
        {
            await submit.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('#guessAgeContainer .gma-btn--primary')?.disabled === true");
            Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
            Assert.Equal("true", await range.GetAttributeAsync("aria-hidden"));
            Assert.True(await range.EvaluateAsync<bool>("element => element.inert"));
            Assert.True(await status.EvaluateAsync<bool>("element => element === document.activeElement"));
            Assert.Equal("Submitting your guess…", await status.InnerTextAsync());
            var fade = await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<double[]>(
                """
                element => {
                    const animation = element.getAnimations()
                        .find(candidate => candidate.animationName === 'fadeOutButtons');
                    if (!animation) return [];
                    const duration = Number(animation.effect.getTiming().duration);
                    animation.pause();
                    const readAt = time => {
                        animation.currentTime = time;
                        return Number.parseFloat(getComputedStyle(element).opacity);
                    };
                    const result = [duration, readAt(0), readAt(duration / 2), readAt(duration)];
                    animation.finish();
                    return result;
                }
                """);
            Assert.Equal(4, fade.Length);
            Assert.InRange(fade[0], 120, 160);
            Assert.True(fade[1] > 0.9);
            Assert.InRange(fade[2], 0.01, fade[1] - 0.01);
            Assert.True(fade[3] <= 0.01);
            await page.Keyboard.PressAsync("Enter");
            await page.EvaluateAsync("() => new Promise(resolve => queueMicrotask(resolve))");
            Assert.Equal(1, guessRequests);
        }
        finally
        {
            releaseFirstGuess.TrySetResult(true);
        }
        var reactionSnapshot = await page.EvaluateAsync<JsonElement>("() => window.__gmaAcceptedResultPromise");
        Assert.Equal(1, reactionSnapshot.GetProperty("count").GetInt32());
        Assert.Equal("beside-portrait", reactionSnapshot.GetProperty("placement").GetString());
        await page.Clock.RunForAsync(1980);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRealBubble')?.dataset.revealPhase === 'detour'");
        Assert.Equal("true", await range.GetAttributeAsync("aria-hidden"));
        Assert.True(await range.EvaluateAsync<bool>("element => element.inert"));
        var phoneReaction = new[]
        {
            reactionSnapshot.GetProperty("fontSize").GetDouble(),
            reactionSnapshot.GetProperty("left").GetDouble(),
            reactionSnapshot.GetProperty("right").GetDouble(),
            reactionSnapshot.GetProperty("top").GetDouble(),
            reactionSnapshot.GetProperty("bottom").GetDouble(),
            reactionSnapshot.GetProperty("viewportWidth").GetDouble(),
            reactionSnapshot.GetProperty("viewportHeight").GetDouble()
        };
        Assert.InRange(phoneReaction[0], 50, 70);
        Assert.True(phoneReaction[1] >= -0.5);
        Assert.True(phoneReaction[2] <= phoneReaction[5] + 0.5);
        Assert.True(phoneReaction[3] >= -0.5);
        Assert.True(phoneReaction[4] <= phoneReaction[6] + 0.5);
        await page.Clock.RunForAsync(7000);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true");

        Assert.Equal(1, guessRequests);
        Assert.DoesNotContain("not accepted", await status.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        Assert.False(await range.IsEnabledAsync());
        Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#guessAgeContainer .gma-actions").IsVisibleAsync());
        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.True(await page.Locator("#gmaBubble").EvaluateAsync<bool>(
            "element => { const opacity = Number.parseFloat(getComputedStyle(element).opacity); return opacity >= 0.35 && opacity <= 0.5; }"));
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const range = document.getElementById('gmaRange').getBoundingClientRect();
                const submittedGuess = document.getElementById('gmaBubble').getBoundingClientRect();
                const status = getComputedStyle(document.getElementById('gmaStatus'));
                return submittedGuess.bottom <= range.top + 2
                    && status.position === 'absolute'
                    && status.width === '1px';
            }
            """));
        await page.WaitForFunctionAsync(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test']?.byImage?.[imageId]?.value === 54",
            ProfileImageA);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            imageId => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test']?.byImage?.[imageId];
                return guess?.value === 54 && guess.first === false && guess.exact === false;
            }
            """,
            ProfileImageA));

        const string compactResultLayoutScript =
            """
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const slider = rect('.gma-slider-wrap');
                const payoff = rect('#gmaPayoffRegion');
                const oldActions = rect('#guessAgeContainer > .gma-actions');
                const card = rect('#guessAgeContainer');
                return [
                    payoff.height,
                    payoff.childElementCount,
                    oldActions.height,
                    card.bottom,
                    window.innerHeight,
                    document.documentElement.scrollWidth - document.documentElement.clientWidth,
                    card.bottom - slider.bottom
                ];
            }
            """;

        var phoneResultLayout = await page.EvaluateAsync<double[]>(compactResultLayoutScript);
        Assert.InRange(phoneResultLayout[0], 0, 0.5);
        Assert.Equal(0, phoneResultLayout[1]);
        Assert.InRange(phoneResultLayout[2], 0, 0.5);
        Assert.True(phoneResultLayout[3] <= phoneResultLayout[4] + 1);
        Assert.InRange(phoneResultLayout[5], -0.5, 0.5);
        Assert.InRange(phoneResultLayout[6], 0, 80);

        await page.SetViewportSizeAsync(1280, 720);
        await page.Clock.RunForAsync(50);
        var compactDesktopResultLayout = await page.EvaluateAsync<double[]>(compactResultLayoutScript);
        Assert.InRange(compactDesktopResultLayout[0], 0, 0.5);
        Assert.Equal(0, compactDesktopResultLayout[1]);
        Assert.InRange(compactDesktopResultLayout[2], 0, 0.5);
        Assert.True(compactDesktopResultLayout[3] <= compactDesktopResultLayout[4] + 1);
        Assert.InRange(compactDesktopResultLayout[5], -0.5, 0.5);
        Assert.InRange(compactDesktopResultLayout[6], 0, 80);

        await page.SetViewportSizeAsync(390, 844);
        await page.Clock.RunForAsync(50);
        await page.Clock.RunForAsync(6000);
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");

        await page.EvaluateAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'accepted-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageB);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        Assert.Null(await range.GetAttributeAsync("aria-hidden"));
        Assert.False(await range.EvaluateAsync<bool>("element => element.inert"));
        await range.EvaluateAsync(
            "range => { range.value = '50'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        var acceptedResponse = page.WaitForResponseAsync(response =>
            response.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && new Uri(response.Url).AbsolutePath.Equals(
                "/api/Guess/athlete-age",
                StringComparison.OrdinalIgnoreCase));
        await submit.ClickAsync();
        var acceptedResultResponse = await acceptedResponse;
        await acceptedResultResponse.FinishedAsync();
        await page.Clock.RunForAsync(100);
        Assert.Contains("First accepted guess!", await status.InnerTextAsync(), StringComparison.Ordinal);
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        await page.WaitForFunctionAsync(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['accepted-history-test']?.byImage?.[imageId]?.first === true",
            ProfileImageB);

        Assert.Equal(2, guessRequests);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            imageId => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['accepted-history-test']?.byImage?.[imageId];
                return guess?.value === 50 && guess.first === true && guess.exact === false;
            }
            """,
            ProfileImageB));
        Assert.Contains($"profileImageId={ProfileImageA}", requestedUrls[0], StringComparison.Ordinal);
        Assert.Contains($"profileImageId={ProfileImageB}", requestedUrls[1], StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_HeldResponseCannotRevealUntilBothGatesOpen()
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

        var guessRequests = 0;
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            guessRequests++;
            requestStarted.TrySetResult(true);
            await releaseResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"crowdAge":100,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
            });
        });

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
                modalContent.dataset.athleteSlug = 'held-response-gate-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.EvaluateAsync(
            """
            () => {
                const slider = document.querySelector('#guessAgeContainer .gma-slider-wrap');
                window.__gmaHeldFirstRevealPromise = new Promise(resolve => {
                    const resolveWhenPresent = () => {
                        const bubble = document.getElementById('gmaRealBubble');
                        if (!bubble) return false;
                        observer.disconnect();
                        resolve(performance.now());
                        return true;
                    };
                    const observer = new MutationObserver(resolveWhenPresent);
                    observer.observe(slider, { childList: true });
                    resolveWhenPresent();
                });
                window.__gmaHeldClickAt = Number.NaN;
                document.querySelector('#guessAgeContainer .gma-btn--primary')
                    .addEventListener('click', () => {
                        window.__gmaHeldClickAt = performance.now();
                    }, { capture: true, once: true });
            }
            """);

        await PauseClockAsync(page);
        await page.Locator("#guessAgeContainer .gma-btn--primary").EvaluateAsync("button => button.click()");
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await page.Clock.RunForAsync(2500);
            Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
            Assert.Equal("Submitting your guess…", await page.Locator("#gmaStatus").InnerTextAsync());
            Assert.InRange(
                await page.EvaluateAsync<double>("() => performance.now() - window.__gmaHeldClickAt"),
                2499,
                2501);

            var releaseAt = await page.EvaluateAsync<double>("() => performance.now()");
            releaseResponse.TrySetResult(true);
            var firstRevealAt = await page.EvaluateAsync<double>("() => window.__gmaHeldFirstRevealPromise");

            Assert.InRange(firstRevealAt - releaseAt, 0, 1);
            Assert.Equal(1, guessRequests);
            Assert.True(await page.Locator("#gmaRealBubble").EvaluateAsync<bool>(
                "element => element.classList.contains('is-travelling') && element.textContent !== '41'"));
            Assert.InRange(
                await page.Locator("#gmaRealBubble").EvaluateAsync<double>(
                    "element => Number(element.dataset.travelBudget)"),
                3200,
                7000);
            Assert.Empty(errors);
        }
        finally
        {
            releaseResponse.TrySetResult(true);
        }
    }

    internal sealed class GuessStateHistoryDiagnostics
    {
        public int LegacyValue { get; set; }
        public bool ImageBInitiallyNull { get; set; }
        public int RestoredValue { get; set; }
        public bool OtherInitiallyNull { get; set; }
        public int NormalizedNestedValue { get; set; }
        public int OtherValue { get; set; }
        public int PrimaryValueAfterOtherWrite { get; set; }
        public bool LegacyFlatStateRemoved { get; set; }
        public int PrimaryImageCount { get; set; }
        public int OtherImageCount { get; set; }
        public bool LowercaseImageKeyExists { get; set; }
        public bool UppercaseNestedKeyNormalized { get; set; }
    }
}
