using Microsoft.Playwright;
using System.Text.Json;
using Xunit;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class SiteStatisticsTrackingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task AiJourneyPreservesFirstTouchAndDoesNotTransmitFormValues()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var payloads = new List<string>();
        var page = await context.NewPageAsync();
        page.Request += (_, request) =>
        {
            if (request.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) && request.PostData is { } body)
                payloads.Add(body);
        };
        // Exercise the real legacy redirect and the browser's first-touch capture.
        await page.GotoAsync("/onboarding/pheno-age.html?UTM_SOURCE=chatgpt.com&utm_campaign=ai-journey&AlbGL=49.8765");
        await page.WaitForFunctionAsync("() => !!window.LwcSiteStats");
        var firstUse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) && (r.Request.PostData?.Contains("\"eventName\":\"calculator_used\"", StringComparison.Ordinal) ?? false));
        await page.Locator("#dob-year option[value='1990']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.Locator("#dob-year").PressAsync("ArrowDown");
        await firstUse;
        await page.WaitForFunctionAsync("() => !!sessionStorage.getItem('lwcSiteStatsFirstTouch')");
        var session = await page.EvaluateAsync<string>("sessionStorage.getItem('lwcSiteStatsSessionId')");
        await page.ReloadAsync();
        var secondUse = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) && (r.Request.PostData?.Contains("\"eventName\":\"calculator_used\"", StringComparison.Ordinal) ?? false));
        await page.Locator("#dob-year option[value='1990']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.Locator("#dob-year").PressAsync("ArrowDown");
        await secondUse;
        await page.GotoAsync("/apply?utm_source=claude");
        var started = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) && (r.Request.PostData?.Contains("\"eventName\":\"application_started\"", StringComparison.Ordinal) ?? false));
        await page.Locator("#name").FillAsync("Private Fixture Name");
        await started;
        await page.Locator("#name").FillAsync("Private Fixture Name Again");
        var response = await page.EvaluateAsync<int>("async () => (await fetch('/api/application/application', {method:'POST', headers:{'Content-Type':'application/json'}, body:'{}'})).status");
        Assert.Equal(400, response);
        // Flush all pending client events deterministically with a marker and the dashboard read.
        var marker = page.WaitForResponseAsync(r => r.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) && (r.Request.PostData?.Contains("journey_finished", StringComparison.Ordinal) ?? false));
        await page.EvaluateAsync("window.LwcSiteStats.track('journey_finished')");
        await marker;
        var statistics = App.Services.GetRequiredService<SiteStatisticsService>();
        var report = await statistics.GetDashboardAsync(new() { Source = "ai:chatgpt", Range = "7d" });
        var end = Assert.Single(report.Events, e => e.EventName == "journey_finished");
        var events = report.Events.Where(e => e.SessionHash == end.SessionHash).ToArray();
        Assert.Single(events, e => e.EventName == "calculator_used");
        Assert.Single(events, e => e.EventName == "application_started");
        Assert.Contains(events, e => e.EventName == "application_submit_failed");
        Assert.DoesNotContain(events, e => e.EventName == "application_submit_succeeded");
        Assert.All(events, e => { Assert.Equal("chatgpt", e.AiProvider); Assert.Equal("/pheno-age", e.LandingRoute); });
        Assert.DoesNotContain(payloads, body => body.Contains("49.8765", StringComparison.Ordinal) || body.Contains("Private Fixture", StringComparison.Ordinal));
        Assert.All(payloads, body => Assert.Equal(session, JsonDocument.Parse(body).RootElement.GetProperty("sessionId").GetString()));
    }

    [Fact]
    public async Task Tracker_KeepsCurrentDocumentAttributionWhenStorageAndBeaconAreUnavailable()
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString()
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("""
            Object.defineProperty(window, "sessionStorage", {
                get() { throw new DOMException("Storage blocked", "SecurityError"); }
            });
            navigator.sendBeacon = () => false;
            """);
        var page = await context.NewPageAsync();
        var submissionSession = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/application/application", async route =>
        {
            submissionSession.TrySetResult(await route.Request.HeaderValueAsync("X-LWC-Stats-Session"));
            await route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{}" });
        });
        await page.GotoAsync("/join?utm_source=newsletter&utm_campaign=storage-fallback");
        var recorded = page.WaitForResponseAsync(response =>
            response.Url.EndsWith("/api/site-statistics/event", StringComparison.Ordinal) &&
            (response.Request.PostData?.Contains("\"eventName\":\"acquisition_test\"", StringComparison.Ordinal) ?? false) &&
            response.Ok);
        await page.EvaluateAsync("""
            async () => {
                await fetch("/api/application/application", { method: "POST" });
                window.LwcSiteStats.track("acquisition_test");
            }
            """);
        var response = await recorded;
        using var payload = JsonDocument.Parse(response.Request.PostData!);
        Assert.Equal(await submissionSession.Task, payload.RootElement.GetProperty("sessionId").GetString());
        Assert.Equal("storage-fallback", payload.RootElement.GetProperty("firstCampaign").GetString());
        Assert.Equal("newsletter", payload.RootElement.GetProperty("firstUtmSource").GetString());
    }

    [Fact]
    public async Task Tracker_ForwardsOnlyConfirmedBusinessConversionsToGoogleAnalyticsOnce()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/application/application**", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = route.Request.Url.Contains("failure=1", StringComparison.Ordinal) ? 500 : 200,
                ContentType = "application/json",
                Body = "{}"
            }));
        await page.RouteAsync("**/api/longevitymaxxing/signup", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{}"
            }));

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => !!window.LwcSiteStats");
        await page.EvaluateAsync(
            """
            window.__googleAnalyticsCalls = [];
            window.gtag = (...args) => window.__googleAnalyticsCalls.push(args);
            """);

        await page.EvaluateAsync(
            """
            async () => {
                const succeeded = { component: "calculator", outcome: "succeeded" };
                window.LwcSiteStats.track("calculator_result_generated", succeeded);
                window.LwcSiteStats.track("calculator_result_generated", succeeded);
                window.LwcSiteStats.track("application_submit_clicked", {
                    component: "application",
                    outcome: "clicked"
                });

                await fetch("/api/application/application", { method: "POST" });
                await fetch("/api/application/application", { method: "POST" });
                await fetch("/api/application/application?failure=1", { method: "POST" });
                await fetch("/api/longevitymaxxing/signup", { method: "POST" });
                await fetch("/api/longevitymaxxing/signup", { method: "POST" });
            }
            """);

        var calls = await page.EvaluateAsync<string[][]>(
            """
            window.__googleAnalyticsCalls
                .filter(args => args[0] === "event")
                .map(args => [
                    String(args[0]),
                    String(args[1]),
                    String(args.length)
                ])
            """);

        Assert.Equal(
            [
                "calculator_result_generated",
                "application_submit_succeeded",
                "challenge_signup_succeeded"
            ],
            calls.Select(call => call[1]));
        Assert.All(calls, call =>
        {
            Assert.Equal("event", call[0]);
            Assert.Equal("2", call[2]);
        });
    }
}
