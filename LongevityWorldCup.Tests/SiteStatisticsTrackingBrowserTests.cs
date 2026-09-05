using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class SiteStatisticsTrackingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
