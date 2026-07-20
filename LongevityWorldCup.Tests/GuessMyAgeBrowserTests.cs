using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class GuessMyAgeBrowserTests
{
    [Fact]
    public async Task GuessSubmission_PreservesEasterEggAndPersistsOnlyAcceptedGuesses()
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
                    Headers = new Dictionary<string, string> { ["Retry-After"] = "30" },
                    Body = """{"crowdAge":0,"crowdCount":0,"actualAge":40,"guessAccepted":false}"""
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
            "range => { range.value = '50'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
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
        await page.WaitForFunctionAsync("() => document.getElementById('gmaStatus')?.textContent.includes('30 seconds')");

        Assert.Equal(1, guessRequests);
        Assert.False(await trollNote.IsVisibleAsync());
        Assert.Contains("not accepted", await status.InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.True(await range.IsEnabledAsync());
        Assert.True(await page.Locator("#guessAgeContainer .gma-actions").IsVisibleAsync());
        Assert.False(await page.Locator("#guessAgeContainer .gma-actions").EvaluateAsync<bool>("element => element.inert"));
        Assert.True(await page.EvaluateAsync<bool>(
            "() => !JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test']"));
        Assert.Equal(0, await page.Locator(".gma-pop-banner").CountAsync());

        await submit.ClickAsync();
        await page.GetByText("First accepted guess!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        Assert.Contains("You guessed older — oof.", await status.InnerTextAsync());
        await page.WaitForFunctionAsync(
            "() => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test']?.first === true");

        Assert.Equal(2, guessRequests);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['history-test'];
                return guess?.value === 50 && guess.first === true && guess.exact === false;
            }
            """));
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
