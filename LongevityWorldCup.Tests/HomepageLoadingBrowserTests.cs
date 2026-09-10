using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageLoadingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task Homepage_FetchesItsDataWhileModulesAreStillLoading()
    {
        await using var context = await NewContextAsync(1440);
        var page = await context.NewPageAsync();
        var releaseModule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var athletesRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventsRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        page.Request += (_, request) =>
        {
            var path = new Uri(request.Url).AbsolutePath;
            if (path == "/api/data/athletes") athletesRequested.TrySetResult();
            if (path == "/api/events") eventsRequested.TrySetResult();
        };
        await page.RouteAsync("**/js/misc.js*", async route =>
        {
            await releaseModule.Task;
            await route.ContinueAsync();
        });

        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Task.WhenAll(athletesRequested.Task, eventsRequested.Task).WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally
        {
            releaseModule.TrySetResult();
        }

        await ExpectHomepageLoadedAsync(page);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/api/data/athletes", 1440)]
    [InlineData("/api/events", 390)]
    public async Task StalledResponseBody_IsAbortedAndRetried_ThenOffersWorkingRecovery(string target, int width)
    {
        await using var context = await NewContextAsync(width);
        await context.AddInitScriptAsync(
            """
            (() => {
                const fetchNormally = window.fetch.bind(window);
                window.publicDataAttempts = {};
                window.publicDataAborts = 0;
                window.holdPublicData = true;
                window.fetch = (input, options) => {
                    const path = new URL(input, location.href).pathname;
                    if (path.startsWith('/api/')) {
                        window.publicDataAttempts[path] = (window.publicDataAttempts[path] || 0) + 1;
                    }
                    if (path === TARGET && window.holdPublicData) {
                        // Headers arrive, but the JSON body never finishes. A timeout
                        // around fetch() alone cannot recover from this failure.
                        return Promise.resolve(new Response(new ReadableStream({
                            start(controller) {
                                options?.signal?.addEventListener('abort', () => {
                                    window.publicDataAborts++;
                                    controller.error(new DOMException('Aborted', 'AbortError'));
                                }, { once: true });
                            }
                        }), { headers: { 'Content-Type': 'application/json' } }));
                    }
                    return fetchNormally(input, options);
                };
            })();
            """.Replace("TARGET", System.Text.Json.JsonSerializer.Serialize(target)));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("() => window.modulesReady");
        await page.WaitForFunctionAsync("path => window.publicDataAttempts[path] === 1", target);
        await page.Clock.FastForwardAsync(10_001);
        await page.WaitForFunctionAsync("path => window.publicDataAttempts[path] === 2", target,
            new() { Timeout = 3000 });
        await page.Clock.FastForwardAsync(10_001);
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events could not load.");
        Assert.Equal(2, await page.EvaluateAsync<int>("window.publicDataAborts"));
        Assert.Equal(0, await page.Locator("#eventsTable .events-skeleton-row").CountAsync());
        if (target == "/api/data/athletes")
        {
            await Assertions.Expect(page.Locator(".leaderboard-retry-button")).ToBeVisibleAsync();
            Assert.Equal(0, await page.Locator(".podium-skeleton-item").CountAsync());
        }
        await CaptureAsync(page, $"failure-{width}");

        await page.EvaluateAsync("window.holdPublicData = false");
        if (target == "/api/data/athletes") await page.Locator(".leaderboard-retry-button").ClickAsync();
        await page.Locator("#events-root").GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(3, await page.EvaluateAsync<int>("path => window.publicDataAttempts[path]", target));
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        await CaptureAsync(page, $"recovered-{width}");
    }

    [Theory]
    [InlineData("/api/bitcoin/total-received")]
    [InlineData("/api/bitcoin/btcusd")]
    public async Task Podium_ShowsAthletesBeforePrizeData_ThenUpdatesAmountsWithoutReplacingCards(string slowPath)
    {
        await using var context = await NewContextAsync(1440);
        var releasePrize = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prizeRequested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var totalRequests = 0;
        await context.RouteAsync("**/api/bitcoin/*", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            if (path == "/api/bitcoin/total-received") Interlocked.Increment(ref totalRequests);
            if (path == slowPath)
            {
                prizeRequested.TrySetResult();
                await releasePrize.Task;
            }
            if (path is not ("/api/bitcoin/total-received" or "/api/bitcoin/btcusd"))
            {
                await route.ContinueAsync();
                return;
            }
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = path.EndsWith("total-received", StringComparison.Ordinal)
                    ? """{"totalReceivedSatoshis":100000000}"""
                    : """{"btcToUsdRate":50000}"""
            });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await prizeRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await ExpectHomepageLoadedAsync(page);
            await page.Locator(".podium-item.first .athlete-name").FocusAsync();
            await page.EvaluateAsync("window.originalPodiumCard = document.querySelector('.podium-item.first')");
        }
        finally
        {
            releasePrize.TrySetResult();
        }

        await Assertions.Expect(page.Locator(".podium-item.first .prize-money")).ToHaveTextAsync("$27000.00");
        await Assertions.Expect(page.Locator(".podium-item.first .btc-amount")).ToHaveTextAsync("(0.54000000 BTC)");
        await Assertions.Expect(page.Locator(".podium-item.second .prize-money")).ToHaveTextAsync("$11250.00");
        await Assertions.Expect(page.Locator(".podium-item.third .prize-money")).ToHaveTextAsync("$6750.00");
        await Assertions.Expect(page.Locator(".podium-item.first .athlete-name")).ToBeFocusedAsync();
        Assert.True(await page.EvaluateAsync<bool>("window.originalPodiumCard === document.querySelector('.podium-item.first')"));
        Assert.Equal(1, totalRequests);
    }

    [Theory]
    [InlineData("/api/data/athletes")]
    [InlineData("/api/events")]
    public async Task TransientDataFailure_RecoversAutomaticallyWithoutDuplicateConsumerRequests(string target)
    {
        await using var context = await NewContextAsync(390);
        var attempts = 0;
        await context.RouteAsync($"**{target}", async route =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
                await route.FulfillAsync(new() { Status = 503, Body = "Temporarily unavailable" });
            else
                await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectHomepageLoadedAsync(page);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task UnavailableExchangeRate_KeepsPodiumAthletesAndDonationLinks()
    {
        await using var context = await NewContextAsync(1440);
        var attempts = 0;
        await context.RouteAsync("**/api/bitcoin/btcusd", async route =>
        {
            Interlocked.Increment(ref attempts);
            await route.FulfillAsync(new() { Status = 503, Body = "Temporarily unavailable" });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectHomepageLoadedAsync(page);
        await Assertions.Expect(page.Locator(".podium-item-lower[aria-busy='false'][href='#contribute']")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".podium-item.first .prize-money")).ToHaveTextAsync("—");
        Assert.Equal(2, attempts);
    }

    private async Task<IBrowserContext> NewContextAsync(int width)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new() { Width = width, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    private static async Task ExpectHomepageLoadedAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        await Assertions.Expect(page.Locator(".podium-item:not(.podium-skeleton-item)")).ToHaveCountAsync(3);
        Assert.True(await page.Locator("#eventsTable tr.main-row").CountAsync() > 0);
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("LWC_LOADING_CAPTURE_DIR");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);
        await page.Locator("#events-root-index").ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png") });
    }
}
