using System.Text.Json;
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

    [Fact]
    public async Task GuessSubmission_PreservesEasterEggAndCompletesFilteredAndAcceptedGuesses()
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
        var trollNote = page.Locator("#gmaTrollNote");

        foreach (var endpoint in new[] { "min", "max" })
        {
            await range.EvaluateAsync(
                "(range, endpoint) => { range.value = range[endpoint]; range.dispatchEvent(new Event('input', { bubbles: true })); }",
                endpoint);
            await submit.ClickAsync();

            Assert.True(await trollNote.IsVisibleAsync());
            Assert.Equal(0, guessRequests);
            Assert.DoesNotContain("youtube.com", page.Url, StringComparison.OrdinalIgnoreCase);
            Assert.True(await range.EvaluateAsync<bool>("range => range === document.activeElement && !range.disabled"));
            await page.WaitForFunctionAsync(
                "() => document.querySelector('.gma-troll-reaction')?.dataset.animationState === 'running'");
            Assert.Equal(1, await page.Locator(".gma-troll-reaction").CountAsync());
            Assert.Equal(
                "gma-troll-reaction-pop",
                await page.Locator(".gma-troll-reaction").EvaluateAsync<string>(
                    "element => getComputedStyle(element).animationName"));
            Assert.Equal(
                "running",
                await page.Locator(".gma-troll-reaction").EvaluateAsync<string>(
                    "element => getComputedStyle(element).animationPlayState"));

            if (endpoint == "min")
            {
                await page.WaitForFunctionAsync(
                    "() => document.querySelectorAll('.gma-troll-reaction').length === 0");
            }
        }

        Assert.Contains("/assets/content-images/trollface.png?v=", await trollNote.Locator("img").GetAttributeAsync("src"));
        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", await trollNote.Locator("a").GetAttributeAsync("href"));
        Assert.Equal("_blank", await trollNote.Locator("a").GetAttributeAsync("target"));
        Assert.Equal("noopener noreferrer", await trollNote.Locator("a").GetAttributeAsync("rel"));
        Assert.Null(await page.Locator("#guessAgeContainer").GetAttributeAsync("aria-live"));

        var containment = await trollNote.EvaluateAsync<ContainmentDiagnostics>(
            """
            element => {
                const note = element.getBoundingClientRect();
                const card = element.closest('#guessAgeContainer').getBoundingClientRect();
                return {
                    NoteLeft: note.left,
                    NoteRight: note.right,
                    CardLeft: card.left,
                    CardRight: card.right,
                    PageScrollWidth: document.documentElement.scrollWidth,
                    PageClientWidth: document.documentElement.clientWidth
                };
            }
            """);
        Assert.True(containment.NoteLeft >= containment.CardLeft - 0.5);
        Assert.True(containment.NoteRight <= containment.CardRight + 0.5);
        Assert.True(containment.PageScrollWidth <= containment.PageClientWidth);

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
        Assert.False(await trollNote.IsVisibleAsync());
        Assert.DoesNotContain("not accepted", await status.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        Assert.False(await range.IsEnabledAsync());
        Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#guessAgeContainer .gma-actions").IsVisibleAsync());
        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.True(await page.Locator("#gmaBubble").EvaluateAsync<bool>(
            "element => { const opacity = Number.parseFloat(getComputedStyle(element).opacity); return opacity >= 0.35 && opacity <= 0.5; }"));
        Assert.True(await page.Locator("#gmaContinueBtn").IsVisibleAsync());
        Assert.True(await page.Locator("#gmaContinueBtn").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
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
                const resultActions = rect('.gma-result-actions');
                const card = rect('#guessAgeContainer');
                const continueButton = rect('#gmaContinueBtn');
                return [
                    payoff.height,
                    payoff.childElementCount,
                    oldActions.height,
                    resultActions.top - slider.bottom,
                    continueButton.bottom,
                    card.bottom,
                    window.innerHeight,
                    document.documentElement.scrollWidth - document.documentElement.clientWidth
                ];
            }
            """;

        var phoneResultLayout = await page.EvaluateAsync<double[]>(compactResultLayoutScript);
        Assert.InRange(phoneResultLayout[0], 0, 0.5);
        Assert.Equal(0, phoneResultLayout[1]);
        Assert.InRange(phoneResultLayout[2], 0, 0.5);
        Assert.InRange(phoneResultLayout[3], 0, 28);
        Assert.True(phoneResultLayout[4] <= phoneResultLayout[6] + 1);
        Assert.True(phoneResultLayout[5] <= phoneResultLayout[6] + 1);
        Assert.InRange(phoneResultLayout[7], -0.5, 0.5);

        await page.SetViewportSizeAsync(1280, 720);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var compactDesktopResultLayout = await page.EvaluateAsync<double[]>(compactResultLayoutScript);
        Assert.InRange(compactDesktopResultLayout[0], 0, 0.5);
        Assert.Equal(0, compactDesktopResultLayout[1]);
        Assert.InRange(compactDesktopResultLayout[2], 0, 0.5);
        Assert.InRange(compactDesktopResultLayout[3], 0, 28);
        Assert.True(compactDesktopResultLayout[4] <= compactDesktopResultLayout[6] + 1);
        Assert.True(compactDesktopResultLayout[5] <= compactDesktopResultLayout[6] + 1);
        Assert.InRange(compactDesktopResultLayout[7], -0.5, 0.5);

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
                window.__gmaRevealTimes = [];
                window.__gmaSubmitAt = performance.now();
                const original = window.positionGmaRevealBubble;
                window.positionGmaRevealBubble = (bubble, age) => {
                    if (bubble?.id === 'gmaRealBubble') {
                        window.__gmaRevealAges.push(Math.round(age));
                        window.__gmaRevealTimes.push(performance.now());
                    }
                    return original(bubble, age);
                };
            }
            """);
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')");
        Assert.Equal(
            "1.98s",
            await page.Locator(".chrono-age-heading").EvaluateAsync<string>(
                "element => getComputedStyle(element).animationDuration"));
        Assert.True(await page.Locator("#gmaContinueBtn").IsVisibleAsync());
        Assert.True(await page.Locator(".gma-result-actions").EvaluateAsync<bool>(
            "element => element.classList.contains('is-pending') && !element.classList.contains('is-promoted')"));
        Assert.False(await page.Locator("#gmaContinueBtn").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Equal(0, await page.Locator("#gmaRealBubble").CountAsync());
        Assert.Equal(1, await page.Locator(".gma-reaction").CountAsync());
        Assert.Equal("beside-portrait", await page.Locator(".gma-reaction").GetAttributeAsync("data-placement"));

        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-travelling') === true && document.getElementById('gmaRealBubble').textContent !== '41'");
        Assert.NotEqual("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.Equal("true", await page.Locator("#gmaRealBubble").GetAttributeAsync("aria-hidden"));
        Assert.Equal(0, await page.Locator(".gma-reaction").CountAsync());
        Assert.True(await page.Locator("#gmaContinueBtn").IsVisibleAsync());
        Assert.False(await page.Locator("#gmaContinueBtn").EvaluateAsync<bool>("element => element === document.activeElement"));
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
        var timing = await page.EvaluateAsync<double[]>(
            "() => [window.__gmaRevealTimes[0] - window.__gmaSubmitAt, Number(document.getElementById('gmaRealBubble').dataset.travelBudget)]");
        Assert.InRange(timing[0], 1750, 2800);
        Assert.InRange(timing[1], 3200, 7000);
        Assert.True(await page.Locator(".gma-result-actions").EvaluateAsync<bool>(
            "element => !element.classList.contains('is-pending') && element.classList.contains('is-promoted')"));
        Assert.True(await page.Locator("#gmaContinueBtn").EvaluateAsync<bool>(
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
                    '#gmaContinueBtn',
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
        Assert.Equal(64, await page.Locator(".gma-celebration-spark").CountAsync());
        Assert.NotNull(await page.EvaluateAsync<string?>("() => localStorage.getItem('gmaHasPerfectGuess')"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_ContinueIsImmediateAndPreservesTheCompletedGuess()
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
                modalContent.dataset.athleteSlug = 'continue-history-test';
                modalContent.classList.add('guess-mode');
            }
            """);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();

        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRealBubble')?.classList.contains('is-travelling') === true");
        Assert.True(await page.Locator("#gmaContinueBtn").IsVisibleAsync());
        Assert.True(await page.Locator(".gma-result-actions").EvaluateAsync<bool>(
            "element => element.classList.contains('is-pending') && !element.classList.contains('is-promoted')"));
        Assert.False(await page.Locator("#gmaContinueBtn").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['continue-history-test'];
                return guess?.value === 54 && guess.skipped === false;
            }
            """));

        var exitHeights = await page.EvaluateAsync<double[]>(
            """
            () => new Promise(resolve => {
                const card = document.getElementById('guessAgeContainer');
                const heights = [card.getBoundingClientRect().height];
                document.getElementById('gmaContinueBtn').click();
                let frames = 0;
                const sample = () => {
                    heights.push(card.getBoundingClientRect().height);
                    frames += 1;
                    if (frames < 18) {
                        requestAnimationFrame(sample);
                        return;
                    }
                    resolve(heights);
                };
                requestAnimationFrame(sample);
            })
            """);
        Assert.True(exitHeights[0] > 100);
        Assert.True(exitHeights.Count(height => height > 1) >= 4);
        Assert.True(exitHeights.Select(height => Math.Round(height, 1)).Distinct().Count() >= 4);
        Assert.All(
            exitHeights.Zip(exitHeights.Skip(1)),
            pair => Assert.True(pair.Second <= pair.First + 1));
        Assert.InRange(exitHeights[^1], 0, 2);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        await page.WaitForTimeoutAsync(500);

        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['continue-history-test'];
                return guess?.value === 54 && guess.skipped === false;
            }
            """));
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-pop-banner").CountAsync());
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
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
                    && document.getElementById('gmaRange').value === '33'
                    && document.getElementById('gmaRange').disabled === false
                    && !document.getElementById('gmaRealBubble')
                    && document.getElementById('gmaStatus').hidden;
            }
            """));
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
        // Exact outcomes dwell for 6000ms, then take 260ms to exit at normal motion.
        // Waiting beyond that full threshold proves the stale exit cannot close the reopen.
        await page.WaitForTimeoutAsync(6600);

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
        Assert.True(await page.Locator("#gmaContinueBtn").IsVisibleAsync());
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.Equal(
            0,
            await page.Locator(".gma-pop-banner, .gma-reaction, .gma-celebration-spark").CountAsync());
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "element => getComputedStyle(element).animationName === 'none' && element.getAnimations().length === 0"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
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
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark").CountAsync());
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
        Assert.Empty(errors);
    }

    private sealed class ContainmentDiagnostics
    {
        public double NoteLeft { get; set; }
        public double NoteRight { get; set; }
        public double CardLeft { get; set; }
        public double CardRight { get; set; }
        public double PageScrollWidth { get; set; }
        public double PageClientWidth { get; set; }
    }
}
