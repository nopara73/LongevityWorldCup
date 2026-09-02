using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed class SiteStatisticsTrackingBrowserTests(PlaywrightBrowserFixture browserFixture)
    : IsolatedBrowserIntegrationTest(browserFixture)
{
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
