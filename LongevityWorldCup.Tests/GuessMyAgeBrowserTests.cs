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
        try
        {
            await submit.ClickAsync();
            await page.WaitForFunctionAsync("() => document.querySelector('#guessAgeContainer .gma-btn--primary')?.disabled === true");
            Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
            await page.Keyboard.PressAsync("Enter");
            await page.WaitForTimeoutAsync(100);
            Assert.Equal(1, guessRequests);
        }
        finally
        {
            releaseFirstGuess.TrySetResult(true);
        }
        await page.WaitForFunctionAsync("() => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')");

        Assert.Equal(1, guessRequests);
        Assert.False(await trollNote.IsVisibleAsync());
        Assert.DoesNotContain("not accepted", await status.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        Assert.False(await range.IsEnabledAsync());
        Assert.True(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
        Assert.False(await page.Locator("#guessAgeContainer .gma-actions").IsVisibleAsync());
        Assert.Equal("41", await page.Locator("#gmaRealBubble").InnerTextAsync());
        Assert.Equal("0", await page.Locator("#gmaBubble").EvaluateAsync<string>("element => getComputedStyle(element).opacity"));
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const heading = document.querySelector('#guessAgeContainer .chrono-age-heading').getBoundingClientRect();
                const status = document.getElementById('gmaStatus').getBoundingClientRect();
                return status.top >= heading.bottom - 0.5;
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
