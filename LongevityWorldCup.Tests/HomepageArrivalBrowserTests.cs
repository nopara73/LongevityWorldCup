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
            const animate = Element.prototype.animate;
            Element.prototype.animate = function(...args) {
                const animation = animate.apply(this, args);
                queueMicrotask(() => {
                    if (animation.id !== 'homepage-arrival') return;
                    const timing = animation.effect.getTiming();
                    window.arrivalStarts.push({
                        podium: this.classList.contains('podium-item'),
                        startTime: animation.startTime,
                        end: timing.duration + timing.delay,
                        opacity: Number(getComputedStyle(this).opacity)
                    });
                    if (window.arrivalStarts.length === 1) requestAnimationFrame(() => {
                        window.arrivalReachedPaint = document.getAnimations()
                            .some(a => a.id === 'homepage-arrival' && a.playState === 'running');
                    });
                });
                return animation;
            };
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
            Assert.Equal(3, await page.EvaluateAsync<int>("arrivalStarts.filter(a => a.podium).length"));
            Assert.Equal(8, await page.EvaluateAsync<int>("arrivalStarts.length"));
            Assert.True(await page.EvaluateAsync<bool>("arrivalStarts.every(a => a.opacity >= 0.2 && a.end <= 1000)"));
            var startTimes = await page.EvaluateAsync<double[]>("arrivalStarts.map(a => a.startTime)");
            // Browser timestamp conversion can round fractional milliseconds.
            Assert.InRange(startTimes.Max() - startTimes.Min(), 0, 1);
            Assert.True(await page.EvaluateAsync<bool>("window.arrivalReachedPaint"));
            await CaptureAsync(page, $"{width}-{visits}-server");
        }
        finally
        {
            releaseAthletes.TrySetResult();
        }

        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        Assert.Equal(0, await page.EvaluateAsync<int>("hiddenPodiumFrames"));
        Assert.Equal(3, await page.EvaluateAsync<int>("arrivalStarts.filter(a => a.podium).length"));
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
            const animate = Element.prototype.animate;
            Element.prototype.animate = function(...args) {
                const animation = animate.apply(this, args);
                if (this.matches('.podium-item.first')) queueMicrotask(() => {
                    if (animation.id !== 'homepage-arrival') return;
                    window.interactedDuringArrival = document.documentElement.classList.contains('homepage-arriving');
                    if (INTERACTION === 'focusin') this.querySelector('.athlete-name').focus();
                    else window.dispatchEvent(new Event(INTERACTION));
                    window.arrivalStoppedOnInteraction = !document.documentElement.classList.contains('homepage-arriving');
                });
                return animation;
            };
            """.Replace("INTERACTION", System.Text.Json.JsonSerializer.Serialize(interaction)));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.arrivalStoppedOnInteraction");
        Assert.True(await page.EvaluateAsync<bool>("interactedDuringArrival && arrivalStoppedOnInteraction"));
        // The existing filter feedback can still run as data arrives; the
        // stronger opening choreography must stop without disabling that feedback.
        Assert.InRange(await page.Locator(".podium-item.first").EvaluateAsync<double>("el => Number(getComputedStyle(el).opacity)"), .8, 1);
        Assert.Equal(0, await page.EvaluateAsync<int>("document.getAnimations().filter(a => a.id === 'homepage-arrival').length"));
    }

    [Fact]
    public async Task ReplacingAnArrivingCard_PreservesTheOriginalAnimationDeadline()
    {
        await using var context = await NewContextAsync(1440);
        await context.AddInitScriptAsync("""
            window.podiumStartTimes = [];
            const animate = Element.prototype.animate;
            Element.prototype.animate = function(...args) {
                const animation = animate.apply(this, args);
                if (this.matches('.podium-item.first')) queueMicrotask(() => {
                    if (animation.id !== 'homepage-arrival') return;
                    window.podiumStartTimes.push(animation.startTime);
                    // Force the same node replacement hydration performs, while
                    // the entrance is active, without a wall-clock race in CI.
                    if (window.podiumStartTimes.length === 1) this.replaceWith(this.cloneNode(true));
                });
                return animation;
            };
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.podiumStartTimes.length >= 2");
        var startTimes = await page.EvaluateAsync<double[]>("podiumStartTimes");
        Assert.InRange(startTimes.Max() - startTimes.Min(), 0, 1);
        await page.WaitForFunctionAsync("() => !document.documentElement.classList.contains('homepage-arriving')");
        Assert.Equal(0, await page.EvaluateAsync<int>("document.getAnimations().filter(a => a.id === 'homepage-arrival').length"));
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
