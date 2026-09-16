using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageArrivalBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 0)]
    [InlineData(390, 0)]
    [InlineData(1440, 3)]
    [InlineData(390, 3)]
    public async Task Arrival_KeepsServerContentVisible_AndDoesNotReplayOnHydration(int width, int visits)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync($"localStorage.setItem('lwcHomepageVisitCount:v1', '{visits}');");
        await context.AddInitScriptAsync("""
            window.arrivalStarts = [];
            window.hiddenPodiumFrames = 0;
            document.addEventListener('animationstart', event => {
                if (event.animationName.startsWith('homepage-')) {
                    window.arrivalStarts.push({
                        name: event.animationName,
                        opacity: Number(getComputedStyle(event.target).opacity)
                    });
                }
            }, true);
            // Observe the synchronous hydration mutation, before its next frame:
            // a frame-delayed reveal must not blank already visible athletes.
            new MutationObserver(() => {
                if ([...document.querySelectorAll('.podium-item:not(.podium-skeleton-item) > *')]
                    .some(child => getComputedStyle(child).opacity === '0')) window.hiddenPodiumFrames++;
            }).observe(document, { childList: true, subtree: true });
            """);
        var releaseAthletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseAthletes.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(page.Locator(".podium[data-server-rendered='true'] .athlete-name")).ToHaveCountAsync(3);
            await Assertions.Expect(page.Locator("header .join-game:not(.scrolled-button)")).ToBeVisibleAsync();
            await page.WaitForFunctionAsync("() => !document.documentElement.classList.contains('homepage-arriving')");
            Assert.Equal(1, await page.EvaluateAsync<int>("arrivalStarts.filter(a => a.name === 'homepage-settle').length"));
            Assert.Equal(3, await page.EvaluateAsync<int>("arrivalStarts.filter(a => a.name === 'homepage-arrival').length"));
            Assert.True(await page.EvaluateAsync<bool>("arrivalStarts.every(a => a.opacity >= 0.4)"));
            await CaptureAsync(page, $"{width}-{visits}-server");
        }
        finally
        {
            releaseAthletes.TrySetResult();
        }

        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        Assert.Equal(0, await page.EvaluateAsync<int>("hiddenPodiumFrames"));
        Assert.Equal(1, await page.EvaluateAsync<int>("arrivalStarts.filter(a => a.name === 'homepage-settle').length"));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var startCount = await page.EvaluateAsync<int>("arrivalStarts.length");
        await page.Locator("#faq").ScrollIntoViewIfNeededAsync();
        await page.Locator(".podium").ScrollIntoViewIfNeededAsync();
        Assert.Equal(startCount, await page.EvaluateAsync<int>("arrivalStarts.length"));
        await CaptureAsync(page, $"{width}-{visits}-hydrated");
    }

    [Theory]
    [InlineData("reduced")]
    [InlineData("no-js")]
    [InlineData("fragment")]
    [InlineData("history")]
    [InlineData("profile")]
    public async Task AccessibleAndReturningVisits_SkipArrival(string mode)
    {
        await using var context = await NewContextAsync(390, mode);
        var page = await context.NewPageAsync();
        var url = mode switch { "fragment" => "/#faq", "profile" => "/athlete/michael-lustgarten", _ => "/" };
        await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (mode == "history")
        {
            await page.GotoAsync("/about", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.GoBackAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        }
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.classList.contains('homepage-arriving')"));
        Assert.Equal("1", await page.Locator(".podium").EvaluateAsync<string>("el => getComputedStyle(el).opacity"));
        await Assertions.Expect(page.Locator(".podium .athlete-name")).ToHaveCountAsync(3);
        if (mode == "fragment") await Assertions.Expect(page.Locator("#faq")).ToBeInViewportAsync();
    }

    [Theory]
    [InlineData("focusin")]
    [InlineData("wheel")]
    [InlineData("pointerdown")]
    public async Task EarlyInteraction_ImmediatelyFinishesArrival(string interaction)
    {
        await using var context = await NewContextAsync(1440);
        await context.AddInitScriptAsync("""
            document.addEventListener('animationstart', event => {
                if (event.animationName !== 'homepage-settle') return;
                window.interactedDuringArrival = document.documentElement.classList.contains('homepage-arriving');
                if (INTERACTION === 'focusin') document.querySelector('.podium .athlete-name').focus();
                else window.dispatchEvent(new Event(INTERACTION));
                window.arrivalStoppedOnInteraction = !document.documentElement.classList.contains('homepage-arriving');
            }, true);
            """.Replace("INTERACTION", System.Text.Json.JsonSerializer.Serialize(interaction)));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.arrivalStoppedOnInteraction");
        Assert.True(await page.EvaluateAsync<bool>("interactedDuringArrival && arrivalStoppedOnInteraction"));
        Assert.Equal("none", await page.Locator(".podium").EvaluateAsync<string>("el => getComputedStyle(el).transform"));
    }

    private async Task<IBrowserContext> NewContextAsync(int width, string mode = "normal")
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = mode == "reduced" ? ReducedMotion.Reduce : ReducedMotion.NoPreference,
            JavaScriptEnabled = mode != "no-js"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_ARRIVAL_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png") });
    }
}
