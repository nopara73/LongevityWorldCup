using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class GuessMyAgeBrowserTests
{
    [Fact]
    public async Task PatrickRoute_CompletesAFilteredGuessWithoutAddingItToCrowdAge()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var guessResponseBody = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = await context.NewPageAsync();
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
            "guess => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['patrick-ruff']?.value === guess",
            filteredGuess);
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'entrance-history-test';
                modalContent.classList.remove('gma-fast');
                modalContent.classList.add('guess-mode');
            }
            """);

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
        await page.WaitForFunctionAsync("() => document.activeElement?.id === 'gmaRange'");
        await page.WaitForTimeoutAsync(900);
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

        await page.WaitForTimeoutAsync(350);
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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

        var page = await context.NewPageAsync();
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'rickroll-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        var pauseAt = await page.EvaluateAsync<string>("() => new Date(Date.now() + 5000).toISOString()");
        await page.Clock.PauseAtAsync(pauseAt);

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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-rickroll-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        var pauseAt = await page.EvaluateAsync<string>("() => new Date(Date.now() + 5000).toISOString()");
        await page.Clock.PauseAtAsync(pauseAt);

        var range = page.Locator("#gmaRange");
        var submit = page.Locator("#guessAgeContainer .gma-btn--primary");
        await range.EvaluateAsync(
            "range => { range.value = range.min; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await submit.EvaluateAsync("button => button.click()");
        await page.Locator("#gmaTrollfaceContainer").WaitForAsync();

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'reduced-rickroll-history-test-reopened';
                modalContent.classList.remove('guess-mode');
                modalContent.classList.add('guess-mode');
            }
            """);
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var guessRequests = 0;
        var releaseFirstGuess = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            guessRequests++;
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
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'history-test';
                modalContent.classList.add('guess-mode');
            }
            """);

        var range = page.Locator("#gmaRange");
        var submit = page.Locator("#guessAgeContainer .gma-btn--primary");
        var status = page.Locator("#gmaStatus");
        Assert.Null(await page.Locator("#guessAgeContainer").GetAttributeAsync("aria-live"));

        await range.EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.EvaluateAsync(
            """
            () => {
                window.__gmaFadeSamples = [];
                const actions = document.querySelector('#guessAgeContainer .gma-actions');
                const observer = new MutationObserver(() => {
                    if (!actions.classList.contains('gma-actions-hide')) return;
                    observer.disconnect();
                    let frames = 0;
                    const sample = () => {
                        window.__gmaFadeSamples.push(Number.parseFloat(getComputedStyle(actions).opacity));
                        frames += 1;
                        if (frames < 12) requestAnimationFrame(sample);
                    };
                    requestAnimationFrame(sample);
                });
                observer.observe(actions, { attributes: true, attributeFilter: ['class'] });
            }
            """);
        try
        {
            await submit.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('#guessAgeContainer .gma-btn--primary')?.disabled === true");
            Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
            Assert.Equal("true", await range.GetAttributeAsync("aria-hidden"));
            Assert.True(await range.EvaluateAsync<bool>("element => element.inert"));
            Assert.True(await status.EvaluateAsync<bool>("element => element === document.activeElement"));
            Assert.Equal("Submitting your guess…", await status.InnerTextAsync());
            await page.WaitForFunctionAsync("() => window.__gmaFadeSamples?.length === 12");
            var fadeSamples = await page.EvaluateAsync<double[]>("() => window.__gmaFadeSamples");
            Assert.True(fadeSamples[0] > 0.1);
            Assert.Contains(fadeSamples, opacity => opacity > 0.05 && opacity < fadeSamples[0]);
            Assert.True(fadeSamples[^1] <= 0.01);
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForTimeoutAsync(100);
            Assert.Equal(1, guessRequests);
        }
        finally
        {
            releaseFirstGuess.TrySetResult(true);
        }
        await page.WaitForFunctionAsync("() => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')");
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRealBubble')?.dataset.revealPhase === 'detour'");
        Assert.Equal("true", await range.GetAttributeAsync("aria-hidden"));
        Assert.True(await range.EvaluateAsync<bool>("element => element.inert"));
        await page.WaitForFunctionAsync("() => document.querySelector('.gma-reaction') !== null");
        await page.WaitForTimeoutAsync(400);
        var phoneReaction = await page.Locator(".gma-reaction").EvaluateAsync<double[]>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                return [
                    Number.parseFloat(getComputedStyle(element).fontSize),
                    rect.left,
                    rect.right,
                    rect.top,
                    rect.bottom,
                    window.innerWidth,
                    window.innerHeight
                ];
            }
            """);
        Assert.InRange(phoneReaction[0], 50, 70);
        Assert.True(phoneReaction[1] >= -0.5);
        Assert.True(phoneReaction[2] <= phoneReaction[5] + 0.5);
        Assert.True(phoneReaction[3] >= -0.5);
        Assert.True(phoneReaction[4] <= phoneReaction[6] + 0.5);
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
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test']?.value === 54");
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test'];
                return guess?.value === 54 && guess.first === false && guess.exact === false;
            }
            """));

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
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var compactDesktopResultLayout = await page.EvaluateAsync<double[]>(compactResultLayoutScript);
        Assert.InRange(compactDesktopResultLayout[0], 0, 0.5);
        Assert.Equal(0, compactDesktopResultLayout[1]);
        Assert.InRange(compactDesktopResultLayout[2], 0, 0.5);
        Assert.True(compactDesktopResultLayout[3] <= compactDesktopResultLayout[4] + 1);
        Assert.InRange(compactDesktopResultLayout[5], -0.5, 0.5);
        Assert.InRange(compactDesktopResultLayout[6], 0, 80);

        await page.SetViewportSizeAsync(390, 844);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'accepted-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        Assert.Null(await range.GetAttributeAsync("aria-hidden"));
        Assert.False(await range.EvaluateAsync<bool>("element => element.inert"));
        await range.EvaluateAsync(
            "range => { range.value = '50'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        await submit.ClickAsync();
        await page.GetByText("First accepted guess!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        await page.WaitForFunctionAsync(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['accepted-history-test']?.first === true");

        Assert.Equal(2, guessRequests);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['accepted-history-test'];
                return guess?.value === 50 && guess.first === true && guess.exact === false;
            }
            """));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_HeldResponseCannotRevealUntilBothGatesOpen()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'held-response-gate-test';
                modalContent.classList.add('guess-mode');
            }
            """);
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

        var pauseAt = await page.EvaluateAsync<string>("() => new Date(Date.now() + 100).toISOString()");
        await page.Clock.PauseAtAsync(pauseAt);
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

    [Fact]
    public async Task GuessSubmission_AnimatesThroughTheRevealAndBoundsCelebrationWork()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'animated-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);

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
        var pauseAt = await page.EvaluateAsync<string>("() => new Date(Date.now() + 100).toISOString()");
        await page.Clock.PauseAtAsync(pauseAt);
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
        Assert.Equal(1, await page.Locator(".gma-reaction").CountAsync());
        Assert.Equal("beside-portrait", await page.Locator(".gma-reaction").GetAttributeAsync("data-placement"));

        await page.Clock.RunForAsync(1979);
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        await page.Clock.RunForAsync(1);
        var firstRevealAt = await page.EvaluateAsync<double>("() => window.__gmaFirstRevealPromise");
        var submitAt = await page.EvaluateAsync<double>("() => window.__gmaSubmitAt");
        Assert.InRange(firstRevealAt - submitAt, 1979, 1981);
        await page.Clock.RunForAsync(250);
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        await page.Clock.ResumeAsync();

        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-travelling') === true && document.getElementById('gmaRealBubble').textContent !== '41'");
        Assert.NotEqual("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.Equal("true", await page.Locator("#gmaRealBubble").GetAttributeAsync("aria-hidden"));
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.False(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>("element => element === document.activeElement"));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['animated-history-test']?.value === 54"));
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true");
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

        var sparkCount = await page.Locator(".gma-celebration-spark").CountAsync();
        Assert.Equal(48, sparkCount);
        Assert.InRange(sparkCount, 1, 64);
        Assert.True((await page.Locator(".gma-celebration-spark").EvaluateAllAsync<string[]>(
            "elements => [...new Set(elements.map(element => element.dataset.quadrant))]" )).Length >= 3);
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
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.gma-celebration-spark').length === 0");
        await page.WaitForFunctionAsync(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['animated-history-test']?.value === 54");

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'exact-animation-test';
                modalContent.classList.add('guess-mode');
                document.getElementById('site-sticky-header')?.classList.add('visible');
                const scrolledPlayButton = document.querySelector('.scrolled-button');
                if (scrolledPlayButton) scrolledPlayButton.style.display = 'block';
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
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
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const bubble = document.getElementById('gmaRealBubble');
                return bubble?.dataset.revealPhase === 'detour'
                    && bubble.textContent !== '41';
            }
            """);
        await page.GetByText("Bullseye!", new PageGetByTextOptions { Exact = true }).WaitForAsync();

        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.classList.contains('celebrate-exact')"));
        Assert.Equal(0, await page.Locator(".gma-reaction--exact").CountAsync());
        Assert.Equal(0, await page.Locator(".gma-celebration-spark").CountAsync());
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('.gma-exact-confetti-canvas')?.dataset.burstsFired || 0) >= 4");

        var jackpot = page.Locator(".gma-exact-jackpot");
        var canvas = page.Locator(".gma-exact-confetti-canvas");
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
        Assert.Equal("18", await jackpot.GetAttributeAsync("data-wave-count"));
        Assert.Equal("9000", await jackpot.GetAttributeAsync("data-duration"));
        Assert.Equal("420", await canvas.GetAttributeAsync("data-particle-cap"));
        Assert.Equal("auto", await jackpot.EvaluateAsync<string>(
            "element => getComputedStyle(element).pointerEvents"));
        Assert.True(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        Assert.Equal(160, await page.Locator(".gma-exact-time-icon").CountAsync());
        Assert.Equal(32, await page.Locator(".gma-exact-target").CountAsync());
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
                Number(element.dataset.backingPixels)
            ]
            """);
        Assert.InRange(earlyCanvasWork[0], 1, 420);
        Assert.InRange(earlyCanvasWork[1], 256, 1_620);
        Assert.InRange(earlyCanvasWork[2], 1, 3_000_000);

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
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
        await page.WaitForFunctionAsync(
            "() => Number(document.querySelector('.gma-exact-confetti-canvas')?.dataset.burstsFired || 0) === 18");
        var completedWaves = await canvas.EvaluateAsync<double[]>(
            "element => [Number(element.dataset.activeParticles), Number(element.dataset.totalSpawned)]");
        Assert.InRange(completedWaves[0], 1, 420);
        Assert.InRange(completedWaves[1], 421, 1_620);

        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.gma-exact-jackpot') && !document.querySelector('#detailsModal .modal-content')?.classList.contains('gma-exact-takeover') && !document.getElementById('detailsModal')?.classList.contains('gma-exact-takeover-active')");
        await page.WaitForTimeoutAsync(500);
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'celebration-renderer-failure-test';
                modalContent.classList.add('guess-mode', 'gma-fast');
            }
            """);
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
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['celebration-renderer-failure-test']?.exact === true"));
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuessSubmission_UsesExistingDismissalWithoutAddingAResultAction(bool useEscape)
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            requestStarted.TrySetResult(true);
            await releaseResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"crowdAge":40,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'dismissal-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

        try
        {
            await page.Keyboard.PressAsync("Escape");
            await page.WaitForTimeoutAsync(100);
            Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>(
                "element => getComputedStyle(element).display"));
            Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
                "element => element.classList.contains('guess-mode') && !element.classList.contains('gma-result-ready')"));
            Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        }
        finally
        {
            releaseResponse.TrySetResult(true);
        }

        await page.WaitForFunctionAsync(
            "() => document.querySelector('#detailsModal .modal-content')?.classList.contains('gma-result-ready') === true");
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(0, await page.Locator("#gmaContinueBtn, .gma-result-actions").CountAsync());
        await page.EvaluateAsync("() => document.getElementById('detailsModal').click()");
        await page.WaitForTimeoutAsync(100);
        Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>(
            "element => getComputedStyle(element).display"));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode') && element.classList.contains('gma-result-ready')"));
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['dismissal-history-test'];
                return guess?.value === 54 && guess.skipped === false;
            }
            """));

        if (useEscape)
        {
            await page.Keyboard.PressAsync("Escape");
        }
        else
        {
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        }
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");

        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['dismissal-history-test'];
                return guess?.value === 54 && guess.skipped === false;
            }
            """));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => !element.classList.contains('guess-mode') && !element.classList.contains('gma-result-ready')"));
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-pop-banner, .gma-exact-jackpot").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_StaleResponseCannotMutateOrCloseAnotherAthletesGame()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/Guess/athlete-age**", async route =>
        {
            requestStarted.TrySetResult(true);
            await releaseResponse.Task;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"crowdAge":39,"crowdCount":12,"actualAge":41,"guessAccepted":true}"""
            });
        });

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'stale-athlete-a';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.classList.remove('guess-mode');
                modalContent.dataset.athleteSlug = 'current-athlete-b';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRange')?.disabled === false && document.getElementById('gmaRange').value === '33'");
        Assert.True(await page.Locator("#gmaRange").EvaluateAsync<bool>(
            "element => element === document.activeElement"));

        releaseResponse.TrySetResult(true);
        await page.WaitForFunctionAsync(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['stale-athlete-a']?.value === 54");
        await page.WaitForTimeoutAsync(250);

        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
                const modalContent = document.querySelector('#detailsModal .modal-content');
                return guesses['stale-athlete-a']?.value === 54
                    && !guesses['current-athlete-b']
                    && modalContent.dataset.athleteSlug === 'current-athlete-b'
                    && modalContent.classList.contains('guess-mode')
                    && !modalContent.classList.contains('gma-result-ready')
                    && document.getElementById('gmaRange').value === '33'
                    && document.getElementById('gmaRange').disabled === false
                    && !document.getElementById('gmaRealBubble')
                    && document.getElementById('gmaStatus').hidden;
            }
            """));
        Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_SameAthleteReopenCancelsThePreviousExitTimer()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'same-athlete-reopen-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '41'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.GetByText("Bullseye!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        Assert.Equal(1, await page.Locator(".gma-exact-jackpot").CountAsync());
        Assert.Equal(112, await page.Locator(".gma-exact-time-icon").CountAsync());
        await page.WaitForTimeoutAsync(420);
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                return rect.left >= -1 && rect.right <= window.innerWidth + 1;
            }
            """));

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.classList.remove('guess-mode');
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRange')?.disabled === false && document.getElementById('gmaRange').value === '33' && !document.getElementById('gmaRealBubble')");
        Assert.Equal(
            0,
            await page.Locator(".gma-exact-jackpot, .gma-exact-confetti-canvas, .gma-exact-time-icon, .gma-exact-target").CountAsync());
        Assert.False(await page.Locator("#modalProfilePic").EvaluateAsync<bool>("element => element.inert"));
        // Exact outcomes dwell for 9600ms, then take 260ms to exit at normal motion.
        // Waiting beyond that full threshold proves the stale exit cannot close the reopen.
        await page.WaitForTimeoutAsync(10200);

        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode')"));
        Assert.True(await page.Locator("#gmaRange").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Null(await page.Locator("#gmaRange").GetAttributeAsync("aria-hidden"));
        Assert.False(await page.Locator("#gmaRange").EvaluateAsync<bool>("element => element.inert"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_StorageFailureStillRevealsAndAllowsEveryExit()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const originalSetItem = Storage.prototype.setItem;
                Storage.prototype.setItem = function (key, value) {
                    if (key === 'gmaAllGuesses') throw new DOMException('Storage unavailable', 'QuotaExceededError');
                    return originalSetItem.call(this, key, value);
                };
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'storage-failure-submit-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')");
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        Assert.Null(await page.EvaluateAsync<string?>(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['storage-failure-submit-test']?.value ?? null"));

        await page.EvaluateAsync(
            """
            () => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'storage-failure-skip-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Skip", Exact = true }).ClickAsync();
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");

        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_ReducedMotionRevealsImmediatelyWithoutReactionParticles()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-motion-history-test';
                modalContent.classList.add('guess-mode');
                const range = document.getElementById('gmaRange');
                range.value = '54';
                range.dispatchEvent(new Event('input', { bubbles: true }));
            }
            """);

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
        await page.WaitForTimeoutAsync(1800);
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode')"));
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'reduced-exact-history-test';
                modalContent.classList.add('guess-mode');
                window.proDiscounts = {
                    setPerfectGuessMarker: () => localStorage.setItem('gmaHasPerfectGuess', '1')
                };
            }
            """);
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
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => element.getAnimations({ subtree: true }).length === 0"));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['reduced-exact-history-test']?.exact === true"));
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));

        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => getComputedStyle(document.getElementById('detailsModal')).display === 'none'");
        await page.WaitForTimeoutAsync(500);
        Assert.Equal(0, await page.Locator(".gma-exact-jackpot, .gma-exact-confetti-canvas").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_SettlesAnActiveRevealWhenReducedMotionTurnsOn()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'motion-change-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
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
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['motion-change-history-test']?.value === 54"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_RequestFailureRestoresRetryableControls()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
            () => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'failed-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);

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

}
