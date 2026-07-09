using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SiteStatisticsDashboardBrowserTests
{
    [Fact]
    public async Task Dashboard_RendersRedactedOnboardingAndChallengeDrilldowns()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var client = new HttpClient { BaseAddress = app.BaseAddress };
        await SeedEventsAsync(client);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        var dashboardRequests = new List<string>();
        page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/site-statistics/dashboard", StringComparison.Ordinal))
                dashboardRequests.Add(request.Url);
        };

        await page.GotoAsync("/internal/site-statistics.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#statsTabs").GetByRole(AriaRole.Button, new() { Name = "Traffic Overview" }).WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Visitor sessions").WaitForAsync();
        await page.GetByLabel("Traffic totals").GetByText("Page views", new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Clean vs raw traffic" }).WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Noisy sessions").WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Top-session share").WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Repeated-refresh sessions").WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Daily traffic" }).WaitForAsync();
        await page.Locator("#trafficOverview .traffic-chart .traffic-bar.sessions").First.WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Top pages" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Sources" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Referrers" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Devices" }).WaitForAsync();
        Assert.Contains(dashboardRequests, url => url.Contains("limit=100", StringComparison.Ordinal));
        await page.Locator("#statsTabs").GetByRole(AriaRole.Button, new() { Name = "Onboarding Diagnostics" }).ClickAsync();
        await page.WaitForSelectorAsync("#decisionBrief .decision-card");
        Assert.Contains(dashboardRequests, url => url.Contains("limit=5000", StringComparison.Ordinal));
        await page.WaitForSelectorAsync("#outcomeStrip .metric-tile");
        await page.Locator("#decisionBrief").GetByText("Calculator completions are not reaching proof flow").WaitForAsync();
        await page.GetByText("Calculator completion sources").WaitForAsync();
        await page.GetByText("Recommended Investigations").WaitForAsync();
        await page.GetByText("Segment Comparison").WaitForAsync();
        await page.GetByText("Trend Watch").WaitForAsync();
        var onboardingDetailText = await page.Locator("#detailSections").InnerTextAsync();
        await page.Locator("#statsTabs").GetByRole(AriaRole.Button, new() { Name = "Challenge Diagnostics" }).ClickAsync();
        await page.Locator("#primaryFunnel").GetByRole(AriaRole.Button, new() { Name = "Signup accepted" }).ClickAsync();
        await page.Locator("#sessionTimeline .timeline-row").First.WaitForAsync();

        var visibleText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Site Statistics", visibleText);
        Assert.Contains("Decision Brief", visibleText);
        Assert.Contains("Noisy sessions", visibleText);
        Assert.Contains("Page views", visibleText);
        Assert.DoesNotContain("Unique visitors", visibleText);
        Assert.Contains("Challenge signups", visibleText);
        Assert.Contains("Signup accepted", visibleText);
        Assert.Contains("prefilled / initial", onboardingDetailText);
        Assert.Contains("AUTO", onboardingDetailText);
        Assert.Contains("baseline pending", visibleText);
        Assert.Contains("S-", visibleText);
        Assert.DoesNotContain("raw-browser-session", visibleText);
        Assert.DoesNotContain("private-token", visibleText);
        Assert.DoesNotContain("athlete@example.test", visibleText);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_SummarizesNoisyJoinPageBurstsAsSingleTrackSelectionBottleneck()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var client = new HttpClient { BaseAddress = app.BaseAddress };
        await SeedNoisyJoinEventsAsync(client);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/internal/site-statistics.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#statsTabs").GetByRole(AriaRole.Button, new() { Name = "Onboarding Diagnostics" }).ClickAsync();
        await page.Locator("#decisionBrief").GetByText("Join track selection bottleneck").WaitForAsync();
        await page.Locator("#eventSamples").GetByText("burst x30").WaitForAsync();
        var pageViewsValue = await page.Locator("#outcomeStrip").EvaluateAsync<string>(
            """
            host => Array.from(host.querySelectorAll('.metric-tile'))
                .find(tile => tile.querySelector('.metric-label')?.textContent?.trim() === 'Page views')
                ?.querySelector('.metric-value')?.textContent?.trim() || ''
            """);
        var investigationText = await page.Locator("#recommendedInvestigations").InnerTextAsync();

        var visibleText = await page.Locator("body").InnerTextAsync();
        Assert.Contains("Join track selection bottleneck", visibleText);
        Assert.Contains("Noisy sessions", visibleText);
        Assert.Contains("Page views", visibleText);
        Assert.Contains("burst x30", visibleText);
        Assert.Equal("4", pageViewsValue);
        Assert.Contains("baseline pending", visibleText);
        Assert.DoesNotContain("pheno age bottleneck at Amateur selected", visibleText);
        Assert.DoesNotContain("bortz age bottleneck at Pro selected", visibleText);
        Assert.Contains("4 sessions", investigationText);
        Assert.DoesNotContain("5 sessions", investigationText);
        Assert.Empty(errors);
    }

    private static async Task SeedEventsAsync(HttpClient client)
    {
        await PostEventAsync(client, "onboarding_page_viewed", "pheno", "raw-browser-session", "/pheno-age?token=private-token", "calculator", "viewed");
        await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", "raw-browser-session", "/join", "join_game", "viewed");
        await PostEventAsync(client, "onboarding_clock_selected", "pheno", "raw-browser-session", "/join", "join_game", "selected",
            new Dictionary<string, JsonElement>
            {
                ["track"] = JsonSerializer.SerializeToElement("amateur")
            });
        await PostEventAsync(client, "calculator_started", "pheno", "raw-browser-session", "/pheno-age", "calculator", "started");
        await PostEventAsync(client, "calculator_field_completed", "pheno", "raw-browser-session", "/pheno-age", "calculator", "completed",
            new Dictionary<string, JsonElement>
            {
                ["fieldKey"] = JsonSerializer.SerializeToElement("albumin"),
                ["requiredCompleted"] = JsonSerializer.SerializeToElement(4),
                ["entryMode"] = JsonSerializer.SerializeToElement("prefilled"),
                ["completionSource"] = JsonSerializer.SerializeToElement("initial")
            });
        await PostEventAsync(client, "calculator_result_generated", "pheno", "raw-browser-session", "/pheno-age", "calculator", "succeeded",
            new Dictionary<string, JsonElement>
            {
                ["clock"] = JsonSerializer.SerializeToElement("pheno"),
                ["resultBucket"] = JsonSerializer.SerializeToElement("30_39")
            });
        await PostEventAsync(client, "proof_flow_opened", "application", "raw-browser-session", "/onboarding/convergence.html", "proof", "opened");
        foreach (var session in new[] { "calc-drop-1", "calc-drop-2", "calc-drop-3" })
        {
            await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", session, "/join", "join_game", "viewed");
            await PostEventAsync(client, "onboarding_clock_selected", "pheno", session, "/join", "join_game", "selected");
            await PostEventAsync(client, "calculator_started", "pheno", session, "/pheno-age", "calculator", "started");
            await PostEventAsync(client, "calculator_result_generated", "pheno", session, "/pheno-age", "calculator", "succeeded");
        }
        await PostEventAsync(client, "challenge_page_viewed", "challenge", "challenge-session", "/longevitymaxxing", "challenge", "viewed");
        await PostEventAsync(client, "challenge_signup_succeeded", "challenge", "challenge-session", "/longevitymaxxing?token=private-token", "signup", "succeeded",
            new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement("athlete@example.test"),
                ["identityMode"] = JsonSerializer.SerializeToElement("new_participant"),
                ["pledgeBucket"] = JsonSerializer.SerializeToElement("$300_999")
            });
        await PostEventAsync(client, "challenge_practice_checkin_submitted", "challenge", "challenge-session", "/longevitymaxxing", "checkin", "succeeded",
            new Dictionary<string, JsonElement>
            {
                ["checkInKind"] = JsonSerializer.SerializeToElement("practice")
            });
    }

    private static async Task SeedNoisyJoinEventsAsync(HttpClient client)
    {
        await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", "selected-track-session", "/join", "join_game", "viewed");
        await PostEventAsync(client, "onboarding_clock_selected", "pheno", "selected-track-session", "/join", "join_game", "selected");
        await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", "join-drop-1", "/join", "join_game", "viewed");
        await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", "join-drop-2", "/join", "join_game", "viewed");
        await PostEventAsync(client, "onboarding_clock_selected", "pheno", "orphan-track-selection", "/join", "join_game", "selected");

        for (var i = 0; i < 30; i++)
        {
            await PostEventAsync(client, "onboarding_entry_viewed", "onboarding", "noisy-refresh-session", "/join", "join_game", "viewed");
        }
    }

    private static async Task PostEventAsync(
        HttpClient client,
        string eventName,
        string flow,
        string sessionId,
        string route,
        string component,
        string outcome,
        Dictionary<string, JsonElement>? metadata = null)
    {
        using var response = await client.PostAsync(
            "/api/site-statistics/event",
            JsonContent.Create(new SiteStatisticsEventRequest
            {
                EventName = eventName,
                SessionId = sessionId,
                Flow = flow,
                Route = route,
                Component = component,
                Outcome = outcome,
                DeviceClass = "desktop",
                BrowserFamily = "Chromium",
                Source = "direct",
                Metadata = metadata ?? new Dictionary<string, JsonElement>()
            }));

        response.EnsureSuccessStatusCode();
    }
}
