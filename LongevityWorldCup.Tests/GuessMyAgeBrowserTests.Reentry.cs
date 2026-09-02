using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;


public sealed partial class GuessMyAgeBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GuessSubmission_UsesExistingDismissalWithoutAddingAResultAction(bool useEscape)
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
        var requestStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/Guess/athlete-age**", async route =>
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
                modalContent.dataset.athleteSlug = 'dismissal-history-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
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
            await page.EvaluateAsync(
                "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>(
                "element => getComputedStyle(element).display"));
            Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
                "element => element.classList.contains('guess-mode') && !element.classList.contains('gma-result-ready')"));
            Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        }
        finally
        {
            await page.EvaluateAsync(
                """
                () => {
                    window.__gmaDismissalResultReadyPromise = new Promise(resolve => {
                        const modalContent = document.querySelector('#detailsModal .modal-content');
                        const resolveWhenReady = () => {
                            if (!modalContent?.classList.contains('gma-result-ready')) return false;
                            observer.disconnect();
                            resolve(true);
                            return true;
                        };
                        const observer = new MutationObserver(resolveWhenReady);
                        observer.observe(modalContent, { attributes: true, attributeFilter: ['class'] });
                        resolveWhenReady();
                    });
                }
                """);
            await PauseClockAsync(page);
            releaseResponse.TrySetResult(true);
        }

        Assert.True(await page.EvaluateAsync<bool>("() => window.__gmaDismissalResultReadyPromise"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(0, await page.Locator("#gmaContinueBtn, .gma-result-actions").CountAsync());
        await page.EvaluateAsync("() => document.getElementById('detailsModal').click()");
        Assert.Equal("block", await page.Locator("#detailsModal").EvaluateAsync<string>(
            "element => getComputedStyle(element).display"));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('guess-mode') && element.classList.contains('gma-result-ready')"));
        Assert.True(await page.EvaluateAsync<bool>(
            """
            imageId => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['dismissal-history-test']?.byImage?.[imageId];
                return guess?.value === 54 && guess.skipped === false;
            }
            """,
            ProfileImageA));

        if (useEscape)
        {
            await page.Keyboard.PressAsync("Escape");
        }
        else
        {
            await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        }
        await page.Clock.RunForAsync(400);
        Assert.Equal("none", await page.Locator("#detailsModal").EvaluateAsync<string>(
            "element => getComputedStyle(element).display"));

        Assert.True(await page.EvaluateAsync<bool>(
            """
            imageId => {
                const guess = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['dismissal-history-test']?.byImage?.[imageId];
                return guess?.value === 54 && guess.skipped === false;
            }
            """,
            ProfileImageA));
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => !element.classList.contains('guess-mode') && !element.classList.contains('gma-result-ready')"));
        Assert.Equal(0, await page.Locator(".gma-reaction, .gma-celebration-spark, .gma-pop-banner, .gma-exact-jackpot").CountAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_StaleResponseCannotMutateOrCloseAnotherAthletesGame()
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
            imageId => {
                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'stale-athlete-a';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '54'; range.dispatchEvent(new Event('input', { bubbles: true })); }");

        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await page.EvaluateAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.classList.remove('guess-mode');
                modalContent.dataset.athleteSlug = 'current-athlete-b';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageB);
        await page.WaitForFunctionAsync(
            "() => document.getElementById('gmaRange')?.disabled === false && document.getElementById('gmaRange').value === '33'");
        Assert.True(await page.Locator("#gmaRange").EvaluateAsync<bool>(
            "element => element === document.activeElement"));

        releaseResponse.TrySetResult(true);
        await page.WaitForFunctionAsync(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['stale-athlete-a']?.byImage?.[imageId]?.value === 54",
            ProfileImageA);

        Assert.True(await page.EvaluateAsync<bool>(
            """
            args => {
                const guesses = JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}');
                const modalContent = document.querySelector('#detailsModal .modal-content');
                return guesses['stale-athlete-a']?.byImage?.[args.imageA]?.value === 54
                    && !guesses['current-athlete-b']?.byImage?.[args.imageB]
                    && modalContent.dataset.athleteSlug === 'current-athlete-b'
                    && modalContent.dataset.profileImageId === args.imageB
                    && modalContent.classList.contains('guess-mode')
                    && !modalContent.classList.contains('gma-result-ready')
                    && document.getElementById('gmaRange').value === '33'
                    && document.getElementById('gmaRange').disabled === false
                    && !document.getElementById('gmaRealBubble')
                    && document.getElementById('gmaStatus').hidden;
            }
            """,
            new { imageA = ProfileImageA, imageB = ProfileImageB }));
        Assert.False(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GuessSubmission_SameAthleteReopenCancelsThePreviousExitTimer()
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
                modalContent.dataset.athleteSlug = 'same-athlete-reopen-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageA);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.Locator("#gmaRange").EvaluateAsync(
            "range => { range.value = '41'; range.dispatchEvent(new Event('input', { bubbles: true })); }");
        await page.Locator("#guessAgeContainer .gma-btn--primary").ClickAsync();
        await page.GetByText("Bullseye!", new PageGetByTextOptions { Exact = true }).WaitForAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('.gma-exact-jackpot').length === 1 && document.querySelectorAll('.gma-exact-time-icon').length === 240");
        Assert.Equal(1, await page.Locator(".gma-exact-jackpot").CountAsync());
        Assert.Equal(240, await page.Locator(".gma-exact-time-icon").CountAsync());
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
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
        // Exact outcomes dwell for 16000ms, then take 260ms to exit at normal motion.
        // Advancing beyond that full threshold proves the stale exit cannot close the reopen.
        await PauseClockAsync(page);
        await page.Clock.RunForAsync(16600);

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
        await page.Clock.InstallAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange') && typeof updateYourGuess === 'function'");
        await page.EvaluateAsync(
            """
            imageId => {
                localStorage.removeItem('gmaAllGuesses');
                let acceptedLength = 0;
                let rejectedLength = 16 * 1024 * 1024;
                while (acceptedLength + 1 < rejectedLength) {
                    const candidateLength = Math.floor((acceptedLength + rejectedLength) / 2);
                    try {
                        localStorage.setItem('gma-quota-filler', 'x'.repeat(candidateLength));
                        acceptedLength = candidateLength;
                    } catch {
                        rejectedLength = candidateLength;
                    }
                }

                let quotaReached = false;
                try {
                    localStorage.setItem('gma-quota-probe', 'x');
                } catch (error) {
                    quotaReached = error instanceof DOMException
                        && (error.name === 'QuotaExceededError' || error.name === 'NS_ERROR_DOM_QUOTA_REACHED');
                }
                if (!quotaReached) throw new Error('Could not exhaust the real localStorage quota.');

                const modal = document.getElementById('detailsModal');
                const modalContent = modal.querySelector('.modal-content');
                modal.style.display = 'block';
                modalContent.dataset.athleteSlug = 'storage-failure-submit-test';
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
            """
            () => document.getElementById('gmaStatus')?.textContent.includes('Actual age: 41')
                && document.getElementById('gmaRealBubble')?.classList.contains('is-settled') === true
            """);
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<bool>(
            "element => element.classList.contains('gma-result-ready')"));
        Assert.True(await page.Locator("#closeAthleteDetailsModal").IsVisibleAsync());
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "See profile", Exact = true }).CountAsync());
        Assert.DoesNotContain(
            "could not submit",
            (await page.Locator("#gmaStatus").InnerTextAsync()).ToLowerInvariant());
        await PauseClockAsync(page);
        await page.Clock.RunForAsync(5100);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");
        Assert.Null(await page.EvaluateAsync<string?>(
            "imageId => JSON.parse(localStorage.getItem('gmaAllGuesses') || '{}')['storage-failure-submit-test']?.byImage?.[imageId]?.value ?? null",
            ProfileImageA));

        await page.EvaluateAsync(
            """
            imageId => {
                const modalContent = document.querySelector('#detailsModal .modal-content');
                modalContent.dataset.athleteSlug = 'storage-failure-skip-test';
                modalContent.dataset.profileImageId = imageId;
                modalContent.classList.add('guess-mode');
            }
            """,
            ProfileImageB);
        await page.WaitForFunctionAsync("() => document.getElementById('gmaRange')?.disabled === false");
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Skip", Exact = true }).ClickAsync();
        await page.Clock.RunForAsync(1);
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('#detailsModal .modal-content')?.classList.contains('guess-mode')");

        Assert.True(await page.Locator("#closeAthleteDetailsModal").EvaluateAsync<bool>(
            "element => element === document.activeElement"));
        Assert.Empty(errors);
    }

}
