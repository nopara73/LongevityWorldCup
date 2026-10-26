using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class StandaloneRecoveryBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IBrowserContext> RecoveryContextAsync(int status = 503)
    {
        var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync($$"""
            sessionStorage.visits = Number(sessionStorage.visits || 0) + 1;
            window.probeStatus = {{status}};
            window.probes = []; window.aborts = 0; window.probeMode = 'response';
            window.testVisibility = 'visible';
            Object.defineProperty(document, 'visibilityState', {get: () => window.testVisibility});
            const originalFetch = window.fetch.bind(window);
            window.fetch = (url, options) => {
                if (options?.method !== 'HEAD') return originalFetch(url, options);
                window.probes.push({url, method: options.method, cache: options.cache, signal: !!options.signal});
                if (window.probeMode === 'offline') return Promise.reject(new TypeError('Offline fixture'));
                if (window.probeMode === 'stall') return new Promise((resolve, reject) => {
                    options.signal?.addEventListener('abort', () => {
                        window.aborts++; reject(new DOMException('Aborted', 'AbortError'));
                    }, {once: true});
                });
                return Promise.resolve(new Response(null, {status: window.probeStatus}));
            };
            """);
        return context;
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Outage_ConfirmedRecoveryDoesNotWaitForDecorativeTimers(int pageStatus)
    {
        await using var context = await RecoveryContextAsync(200);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
        await page.Clock.InstallAsync();
        await page.Clock.PauseAtAsync(DateTime.UtcNow.AddHours(1));
        await page.GotoAsync($"/error/{pageStatus}.html");
        await page.RunAndWaitForRequestAsync(() => page.Locator("button[data-retry]").ClickAsync(),
            request => request.IsNavigationRequest && request.Method == "GET", new() { Timeout = 4000 });
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Outage_RecoversToSuccessfulAndClientErrorResponses(int pageStatus)
    {
        foreach (var recoveredStatus in new[] { 200, 204, 403, 404 })
        {
            await using var context = await RecoveryContextAsync(recoveredStatus);
            var page = await context.NewPageAsync();
            await page.GotoAsync($"/error/{pageStatus}.html");
            await page.Locator("button[data-retry]").ClickAsync();
            await page.WaitForFunctionAsync("() => Number(sessionStorage.visits) === 2", null, new() { Timeout = 4000 });
        }
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Outage_TimeoutOfflineAndRapidClicksRemainRetryable(int pageStatus)
    {
        await using var context = await RecoveryContextAsync();
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync($"/error/{pageStatus}.html");
        var button = page.Locator("button[data-retry]");
        await page.EvaluateAsync("""
            () => { probeMode = 'stall'; for (let i = 0; i < 8; i++) document.querySelector('button[data-retry]').click(); }
            """);
        Assert.Equal(1, await page.EvaluateAsync<int>("probes.length"));
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
        await page.Clock.RunForAsync(10001);
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("aria-busy", "true");
        Assert.Equal(1, await page.EvaluateAsync<int>("aborts"));
        Assert.True(await page.EvaluateAsync<bool>("probes[0].signal && probes[0].cache === 'no-store' && probes[0].url === location.href"));
        await page.EvaluateAsync("probeMode = 'offline'");
        await page.Clock.RunForAsync(20001);
        Assert.Equal(2, await page.EvaluateAsync<int>("probes.length"));
        await Assertions.Expect(button).Not.ToHaveAttributeAsync("aria-busy", "true");
        foreach (var status in new[] { 502, 503, 504 })
        {
            await page.EvaluateAsync("status => { probeMode = 'response'; probeStatus = status; }", status);
            await button.ClickAsync();
            await Assertions.Expect(page.Locator(".error-card")).ToHaveAttributeAsync("data-retry", "still");
            await Assertions.Expect(button).Not.ToHaveAttributeAsync("aria-busy", "true");
            Assert.Equal("1", await page.EvaluateAsync<string>("sessionStorage.visits"));
        }
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task Outage_HiddenAndDepartedPagesStopWork_AndReturnRearmsOneTimer(int pageStatus)
    {
        await using var context = await RecoveryContextAsync();
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.Clock.PauseAtAsync(DateTime.UtcNow.AddHours(1));
        await page.GotoAsync($"/error/{pageStatus}.html");
        await page.Clock.RunForAsync(19999);
        Assert.Equal(0, await page.EvaluateAsync<int>("probes.length"));
        await page.Clock.RunForAsync(2);
        Assert.Equal(1, await page.EvaluateAsync<int>("probes.length"));
        await page.EvaluateAsync("testVisibility = 'hidden'; document.dispatchEvent(new Event('visibilitychange'))");
        await page.Clock.RunForAsync(60000);
        Assert.Equal(1, await page.EvaluateAsync<int>("probes.length"));
        Assert.False(await page.Locator(".retry-meter").EvaluateAsync<bool>("e => e.classList.contains('is-running')"));
        await page.EvaluateAsync("testVisibility = 'visible'; document.dispatchEvent(new Event('visibilitychange'))");
        await page.Clock.RunForAsync(20001);
        Assert.Equal(2, await page.EvaluateAsync<int>("probes.length"));
        await page.EvaluateAsync("probeMode = 'stall'; document.querySelector('button[data-retry]').click(); window.dispatchEvent(new Event('pagehide'))");
        await page.Clock.RunForAsync(60000);
        Assert.Equal(3, await page.EvaluateAsync<int>("probes.length"));
        Assert.Equal(1, await page.EvaluateAsync<int>("aborts"));
        await page.EvaluateAsync("probeMode = 'response'; window.dispatchEvent(new PageTransitionEvent('pageshow', {persisted:true}))");
        await page.Clock.RunForAsync(20001);
        Assert.Equal(4, await page.EvaluateAsync<int>("probes.length"));
    }

    [Theory]
    [InlineData(200, "done")]
    [InlineData(503, "error")]
    public async Task Unsubscribe_TracksBusyOutcomeAndClearsStaleStateOnEdit(int status, string state)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/home/unsubscribe", async route => {
            await release.Task; await route.FulfillAsync(new() {Status = status, Body = "Fixture response"});
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/unsubscribe");
            var input = page.Locator("#email");
            var panel = page.Locator(".unsubscribe-panel");
            await input.FillAsync("reader@example.test");
            await page.Locator("#unsubscribe-button").ClickAsync();
            await Assertions.Expect(panel).ToHaveAttributeAsync("data-unsubscribe-state", "busy");
            release.TrySetResult();
            await Assertions.Expect(panel).ToHaveAttributeAsync("data-unsubscribe-state", state);
            await input.FillAsync("next@example.test");
            Assert.Null(await panel.GetAttributeAsync("data-unsubscribe-state"));
            await Assertions.Expect(page.Locator("#message")).ToBeEmptyAsync();
        }
        finally { release.TrySetResult(); }
    }
}
