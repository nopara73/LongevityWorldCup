using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed class SiteStatisticsDashboardBrowserTests(
    PlaywrightBrowserFixture browserFixture)
    : BrowserIntegrationTest(browserFixture)
{
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await ResetSiteStatisticsAsync(App);
    }

    private static async Task ResetSiteStatisticsAsync(BrowserTestApp app)
    {
        var statistics = app.Services.GetRequiredService<SiteStatisticsService>();
        await statistics.StopAsync(CancellationToken.None);
        await statistics.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });

        var database = app.Services.GetRequiredService<DatabaseManager>();
        database.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText =
                "DELETE FROM SiteStatisticEvents; DELETE FROM SiteStatisticSessions;";
            command.ExecuteNonQuery();
        });

        await statistics.StartAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Dashboard_RendersRedactedOnboardingAndChallengeDrilldowns()
    {
        var app = App;
        using var client = new HttpClient { BaseAddress = app.BaseAddress };
        await SeedEventsAsync(client);

        var browser = Browser;
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
        await page.Locator("#trafficOverview").GetByText("Visitor sessions", new() { Exact = true }).WaitForAsync();
        await page.GetByLabel("Traffic totals").GetByText("Page views", new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Clean vs raw traffic" }).WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Noisy sessions").WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Top-session share").WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Repeated-refresh sessions").WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Conversions over time" }).WaitForAsync();
        await page.Locator("#trafficOverview .success-trend-panel .traffic-legend i.success-rate").WaitForAsync();
        await page.Locator("#trafficOverview .success-trend-svg").WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Daily traffic" }).WaitForAsync();
        await page.Locator("#trafficOverview .traffic-chart .traffic-bar.sessions").First.WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Top pages" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Sources" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Referrers" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Devices" }).WaitForAsync();
        Assert.Contains(dashboardRequests, url => url.Contains("limit=100", StringComparison.Ordinal));
        var range30Response = page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/site-statistics/dashboard", StringComparison.Ordinal) &&
            response.Url.Contains("range=30d", StringComparison.Ordinal) &&
            response.Ok);
        await page.Locator("#statsRange").SelectOptionAsync("30d");
        await range30Response;
        await page.Locator("#trafficOverview .success-trend-svg").WaitForAsync();
        Assert.Contains(dashboardRequests, url => url.Contains("range=30d", StringComparison.Ordinal));
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
        await page.Locator("#flowSelectors .flow-card").Filter(new() { HasText = "application" }).ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Application stage completion" }).WaitForAsync();
        var applicationStageText = await page.Locator("#detailSections").InnerTextAsync();
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
        Assert.Contains("Identity", applicationStageText);
        Assert.Contains("Motivation", applicationStageText);
        Assert.Contains("Stopped before next", applicationStageText);
        Assert.Contains("baseline pending", visibleText);
        Assert.Contains("S-", visibleText);
        Assert.DoesNotContain("ResizeObserver loop completed", visibleText);
        Assert.DoesNotContain("raw-browser-session", visibleText);
        Assert.DoesNotContain("private-token", visibleText);
        Assert.DoesNotContain("athlete@example.test", visibleText);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_SummarizesNoisyJoinPageBurstsAsSingleTrackSelectionBottleneck()
    {
        var app = App;
        using var client = new HttpClient { BaseAddress = app.BaseAddress };
        await SeedNoisyJoinEventsAsync(client);

        var browser = Browser;
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

    [Fact]
    public async Task Dashboard_DiagnosticsLoadCompleteCurrentAndPreviousWindows()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var paginationRequests = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await RoutePagedDashboardAsync(page, paginationRequests);

        await page.GotoAsync(
            "/internal/site-statistics.html?tab=Onboarding%20Diagnostics",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#statsStatus").GetByText("2 current and 2 previous redacted events", new() { Exact = false }).WaitForAsync();

        Assert.Equal(2, paginationRequests.Count);
        Assert.Contains(paginationRequests, url => url.Contains("cursor=current-cursor", StringComparison.Ordinal));
        Assert.Contains(paginationRequests, url => url.Contains("cursor=previous-cursor", StringComparison.Ordinal));
        Assert.Contains(paginationRequests, url => url.Contains("fromUtc=2026-08-08", StringComparison.Ordinal));
        Assert.Contains(paginationRequests, url => url.Contains("fromUtc=2026-08-01", StringComparison.Ordinal));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_ExportLoadsCompleteCurrentWindow()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 },
            AcceptDownloads = true
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        var paginationRequests = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        await RoutePagedDashboardAsync(page, paginationRequests);

        await page.GotoAsync("/internal/site-statistics.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#trafficOverview").GetByText("Visitor sessions", new() { Exact = true }).WaitForAsync();
        var downloadTask = page.WaitForDownloadAsync();
        await page.Locator("#statsExport").ClickAsync();
        var download = await downloadTask;
        await page.Locator("#statsStatus").GetByText("Exported 2 redacted events from the complete 7D window.", new() { Exact = true }).WaitForAsync();

        Assert.Equal("site-statistics-redacted.csv", download.SuggestedFilename);
        var request = Assert.Single(paginationRequests);
        Assert.Contains("cursor=current-cursor", request, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor=previous-cursor", request, StringComparison.Ordinal);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ApplyPage_RecordsOnlyTheCoarseApplicationStage()
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
        var stageRequestTask = page.WaitForRequestAsync(request =>
            request.Url.Contains("/api/site-statistics/event", StringComparison.OrdinalIgnoreCase) &&
            (request.PostData ?? string.Empty).Contains("\"eventName\":\"application_stage_reached\"", StringComparison.Ordinal));

        await page.GotoAsync("/apply", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        var stageRequest = await stageRequestTask;
        using var payload = JsonDocument.Parse(stageRequest.PostData ?? "{}");
        var root = payload.RootElement;

        Assert.Equal("application_stage_reached", root.GetProperty("eventName").GetString());
        Assert.Equal("application", root.GetProperty("flow").GetString());
        Assert.Equal("identity", root.GetProperty("step").GetString());
        Assert.Equal("1", root.GetProperty("metadata").GetProperty("stageNumber").GetString());
        Assert.DoesNotContain("accountEmail", stageRequest.PostData ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("biomarkers", stageRequest.PostData ?? string.Empty, StringComparison.OrdinalIgnoreCase);
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
        await PostEventAsync(client, "application_stage_reached", "application", "raw-browser-session", "/apply", "application", "reached", step: "identity");
        await PostEventAsync(client, "application_stage_reached", "application", "raw-browser-session", "/apply", "application", "reached", step: "motivation");
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
                ["identityMode"] = JsonSerializer.SerializeToElement("new_participant")
            });
        await PostEventAsync(client, "challenge_practice_checkin_submitted", "challenge", "challenge-session", "/longevitymaxxing", "checkin", "succeeded",
            new Dictionary<string, JsonElement>
            {
                ["checkInKind"] = JsonSerializer.SerializeToElement("practice")
            });
        await PostEventAsync(
            client,
            "client_error_observed",
            "site",
            "resize-observer-noise",
            "/athlete/siim-land",
            "client",
            "failed",
            errorCode: "ResizeObserver loop completed with undelivered notifications.");
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
        Dictionary<string, JsonElement>? metadata = null,
        string? errorCode = null,
        string? step = null)
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
                Step = step,
                Outcome = outcome,
                ErrorCode = errorCode,
                DeviceClass = "desktop",
                BrowserFamily = "Chromium",
                Source = "direct",
                Metadata = metadata ?? new Dictionary<string, JsonElement>()
            }));

        response.EnsureSuccessStatusCode();
    }

    private static Task RoutePagedDashboardAsync(IPage page, List<string> paginationRequests)
        => page.RouteAsync("**/api/site-statistics/dashboard**", async route =>
        {
            var url = route.Request.Url;
            if (url.Contains("/dashboard/events", StringComparison.Ordinal))
            {
                paginationRequests.Add(url);
                var previous = url.Contains("fromUtc=2026-08-01", StringComparison.Ordinal);
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = JsonSerializer.Serialize(new
                    {
                        events = new[]
                        {
                            DashboardEventPayload(
                                "onboarding_clock_selected",
                                previous ? "previous-session" : "current-session",
                                previous ? "2026-08-07T12:00:00Z" : "2026-08-14T12:00:00Z")
                        },
                        page = new { hasMore = false, nextCursor = (string?)null }
                    })
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    generatedAtUtc = "2026-08-15T00:00:00Z",
                    filters = new
                    {
                        range = "7d",
                        flow = "all",
                        device = "all",
                        source = "all",
                        fromUtc = "2026-08-08T00:00:00Z",
                        toUtc = "2026-08-15T00:00:00Z",
                        previousFromUtc = "2026-08-01T00:00:00Z",
                        previousToUtc = "2026-08-08T00:00:00Z",
                        limit = 5000
                    },
                    trafficSummary = EmptyTrafficSummaryPayload(),
                    events = new[] { DashboardEventPayload("onboarding_entry_viewed", "current-session", "2026-08-14T11:00:00Z") },
                    previousEvents = new[] { DashboardEventPayload("onboarding_entry_viewed", "previous-session", "2026-08-07T11:00:00Z") },
                    eventsPage = new { hasMore = true, nextCursor = "current-cursor" },
                    previousEventsPage = new { hasMore = true, nextCursor = "previous-cursor" }
                })
            });
        });

    private static object DashboardEventPayload(string eventName, string sessionHash, string occurredAtUtc)
        => new
        {
            occurredAtUtc,
            sessionHash,
            actorHash = (string?)null,
            eventName,
            flow = "onboarding",
            route = "/join",
            component = "join_game",
            step = "amateur",
            outcome = "selected",
            errorCode = (string?)null,
            durationMs = (long?)null,
            deviceClass = "desktop",
            browserFamily = "Chromium",
            referrerDomain = (string?)null,
            source = "direct",
            landingRoute = "/join",
            firstReferrerDomain = (string?)null,
            firstSource = "direct",
            firstCampaign = (string?)null,
            firstUtmSource = (string?)null,
            firstUtmMedium = (string?)null,
            firstUtmCampaign = (string?)null,
            firstUtmTerm = (string?)null,
            firstUtmContent = (string?)null,
            metadata = new Dictionary<string, string>()
        };

    private static object EmptyTrafficSummaryPayload()
        => new
        {
            totals = new { sessions = 0, pageViews = 0, events = 0 },
            previousTotals = new { sessions = 0, pageViews = 0, events = 0 },
            cleanTotals = new { sessions = 0, pageViews = 0, events = 0 },
            quality = new
            {
                rawSessions = 0,
                cleanSessions = 0,
                noisySessions = 0,
                topSessionEvents = 0,
                topSessionShare = 0,
                repeatedPageViewSessions = 0,
                pageViewDominantSessions = 0,
                noisyPageViews = 0,
                noisyPageViewShare = 0
            },
            daily = Array.Empty<object>(),
            topPages = Array.Empty<object>(),
            sources = Array.Empty<object>(),
            referrers = Array.Empty<object>(),
            devices = Array.Empty<object>(),
            browsers = Array.Empty<object>()
        };
}
