using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class SiteStatisticsDashboardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
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
    public async Task Dashboard_DefaultUrlStaysCleanAndOnlyChangedFiltersRoundTrip()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await RoutePagedDashboardAsync(page, []);
        var request = await page.RunAndWaitForRequestAsync(
            () => page.GotoAsync("/internal/site-statistics.html"),
            candidate => candidate.Url.Contains("/api/site-statistics/dashboard?"));
        await page.Locator("#trafficDailyChart").WaitForAsync();
        await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("90d");
        Assert.Contains("range=90d", request.Url);
        Assert.Equal("/internal/site-statistics.html", new Uri(page.Url).PathAndQuery);

        await page.Locator("#statsRange").SelectOptionAsync("30d");
        await page.Locator("#statsDevice").SelectOptionAsync("mobile");
        await page.Locator("#statsSource").SelectOptionAsync("search");
        await Assertions.Expect(page.Locator("#statsExport")).ToBeEnabledAsync();
        Assert.Equal("?range=30d&device=mobile&source=search", new Uri(page.Url).Query);

        await page.ReloadAsync();
        await page.Locator("#trafficDailyChart").WaitForAsync();
        await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("30d");
        await Assertions.Expect(page.Locator("#statsDevice")).ToHaveValueAsync("mobile");
        await Assertions.Expect(page.Locator("#statsSource")).ToHaveValueAsync("search");
        Assert.Equal("?range=30d&device=mobile&source=search", new Uri(page.Url).Query);

        await page.Locator("#statsReset").ClickAsync();
        await Assertions.Expect(page.Locator("#statsExport")).ToBeEnabledAsync();
        await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("90d");
        Assert.Equal("/internal/site-statistics.html", new Uri(page.Url).PathAndQuery);

        await page.Locator("#statsRange").SelectOptionAsync("7d");
        await Assertions.Expect(page.Locator("#statsExport")).ToBeEnabledAsync();
        Assert.Equal("?range=7d", new Uri(page.Url).Query);
        await page.ReloadAsync();
        await page.Locator("#trafficDailyChart").WaitForAsync();
        await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("7d");
        Assert.Equal("?range=7d", new Uri(page.Url).Query);
        await page.Locator("#statsRange").SelectOptionAsync("90d");
        await Assertions.Expect(page.Locator("#statsExport")).ToBeEnabledAsync();
        Assert.Equal("/internal/site-statistics.html", new Uri(page.Url).PathAndQuery);
    }

    [Theory]
    [InlineData("?tab=Traffic+Overview&range=90d&flow=all&device=all&source=all&selectedFlow=pheno", "")]
    [InlineData("?tab=Traffic+Overview&range=90d&flow=all&device=all&source=all&selectedFlow=pheno&note=keep#measurement", "?note=keep#measurement")]
    [InlineData("?range=invalid&flow=invalid&device=invalid&source=invalid&selectedFlow=pheno,", "")]
    public async Task Dashboard_DefaultLinksNormalizeWithoutLosingOtherUrlState(string query, string expected)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await RoutePagedDashboardAsync(page, []);
        await page.GotoAsync("/internal/site-statistics.html" + query);
        await page.Locator("#trafficDailyChart").WaitForAsync();
        var url = new Uri(page.Url);
        Assert.Equal(expected, url.Query + url.Fragment);
        await Assertions.Expect(page.Locator("#statsRange")).ToHaveValueAsync("90d");
    }

    [Fact]
    public async Task Dashboard_OverviewUsesCompleteOutcomesAndKeepsZeroAndPartialDatesReadable()
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US",
            ViewportSize = new() { Width = 1440, Height = 1000 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/site-statistics/dashboard?**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                generatedAtUtc = "2026-10-17T12:00:00Z",
                filters = new { fromUtc = "2026-10-10T12:00:00Z", toUtc = "2026-10-17T12:00:00Z" },
                trafficSummary = new SiteStatisticsTrafficSummary(
                    Totals: new(6, 11, 100), PreviousTotals: new(12, 22, 200), CleanTotals: new(6, 11, 100),
                    Quality: new(6, 6, 0, 10, .1, 0, 0, 0, 0),
                    Daily: [new("2026-10-12", 2, 3, 40, 2, 3), new("2026-10-14", 5, 8, 60, 1, 3)],
                    TopPages: [new("/", 6, 11)], Sources: [new("direct", 6, 11, 100)],
                    Referrers: [], Devices: [], Browsers: [],
                    Outcomes: new(6, 3, 3, 2, 1, 0, 0, 0)),
                events = Array.Empty<object>(), previousEvents = Array.Empty<object>()
            }, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/internal/site-statistics.html");
        await Assertions.Expect(page.Locator(".traffic-outcome").Filter(new() { HasText = "New applications" }).Locator("strong")).ToHaveTextAsync("2");
        await Assertions.Expect(page.Locator("#trafficDay option")).ToHaveCountAsync(8);
        await Assertions.Expect(page.Locator("#trafficDailyChart .partial")).ToHaveCountAsync(2);
        await Assertions.Expect(page.Locator("#statsDiagnostics")).Not.ToHaveAttributeAsync("open", "");
        await page.GetByLabel("Selected day (UTC)").SelectOptionAsync("2026-10-13");
        await Assertions.Expect(page.Locator("#trafficDayDetail")).ToHaveTextAsync("Oct 13 · 0 sessions · 0 page views");
        await page.GetByLabel("Selected day (UTC)").FocusAsync();
        await page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(page.Locator("#trafficDayDetail")).ToHaveTextAsync("Oct 14 · 5 sessions · 8 page views");
        await page.Locator(".traffic-quality-panel > summary").ClickAsync();
        await Assertions.Expect(page.Locator(".traffic-measurement")).ToContainTextAsync("3 distinct sessions");
        await page.Locator(".traffic-quality-panel > summary").ClickAsync();

        var screenshots = Environment.GetEnvironmentVariable("LWC_STATS_SCREENSHOTS");
        if (!string.IsNullOrEmpty(screenshots)) Directory.CreateDirectory(screenshots);
        foreach (var width in new[] { 1440, 768, 390, 320 })
        {
            await page.SetViewportSizeAsync(width, 1000);
            await Assertions.Expect(page.Locator("#trafficDailyChart")).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
            if (!string.IsNullOrEmpty(screenshots))
                await page.ScreenshotAsync(new() { Path = Path.Combine(screenshots, $"statistics-{width}.png"), FullPage = true });
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_AiReportSharesProviderFiltersAndFitsDesktopAndMobile()
    {
        var statistics = App.Services.GetRequiredService<SiteStatisticsService>();
        for (var i = 0; i < 4; i++)
        {
            var session = $"ai-report-{i}";
            await statistics.RecordClientEventAsync(new()
            {
                EventName = "site_page_viewed", SessionId = session, Route = "/join", LandingRoute = i == 0 ? "/" : "/pheno-age",
                FirstSource = "referral", FirstReferrerDomain = i == 0 ? "perplexity.ai" : "chatgpt.com"
            }, new DefaultHttpContext());
            if (i == 3) continue;
            await statistics.RecordClientEventAsync(new() { EventName = "application_started", SessionId = session, Flow = "application" }, new DefaultHttpContext());
            await statistics.RecordServerEventAsync("application_submit_succeeded", sessionId: session,
                metadata: new Dictionary<string, object?> { ["submissionKind"] = "full-application" });
        }
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ViewportSize = new() { Width = 1440, Height = 980 } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/internal/site-statistics.html?tab=Source%20Quality&source=ai:chatgpt");
        var report = page.Locator("#aiReferralReport");
        await Assertions.Expect(report.Locator(".ai-stage strong")).ToHaveTextAsync(["3", "0", "0", "2", "2"]);
        await Assertions.Expect(page.Locator("#statsSource")).ToHaveValueAsync("ai:chatgpt");
        await Assertions.Expect(report.Locator(".ai-rates")).ToContainTextAsync("66.7%");
        await page.Locator("#statsSource").SelectOptionAsync("ai");
        await Assertions.Expect(report.Locator(".ai-stage strong").First).ToHaveTextAsync("4");
        var output = Environment.GetEnvironmentVariable("LWC_AI_SCREENSHOTS");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            await report.ScreenshotAsync(new() { Path = Path.Combine(output, "ai-report-desktop.png") });
        }
        await report.GetByRole(AriaRole.Button, new() { Name = "Perplexity", Exact = true }).ClickAsync();
        await Assertions.Expect(report.Locator(".ai-stage strong").First).ToHaveTextAsync("1");
        await Assertions.Expect(page.Locator("#statsSource")).ToHaveValueAsync("ai:perplexity");
        await page.SetViewportSizeAsync(390, 844);
        await page.Locator("#statsSource").SelectOptionAsync("ai");
        await Assertions.Expect(report.Locator(".ai-stage strong").First).ToHaveTextAsync("4");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth"));
        if (!string.IsNullOrEmpty(output)) await report.ScreenshotAsync(new() { Path = Path.Combine(output, "ai-report-mobile.png") });
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_AttributesServerConfirmedApplicationsOncePerSession()
    {
        var statistics = App.Services.GetRequiredService<SiteStatisticsService>();
        foreach (var session in new[] { "server-only", "both-events", "direct-application", "failed-application" })
        {
            var direct = session == "direct-application";
            await statistics.RecordClientEventAsync(new SiteStatisticsEventRequest
            {
                EventName = "proof_flow_opened",
                SessionId = session,
                Flow = "application",
                Route = "/apply",
                LandingRoute = "/join",
                FirstSource = direct ? "direct" : "social",
                FirstReferrerDomain = direct ? null : "www.reddit.com",
                FirstCampaign = direct ? null : "outreach"
            }, new DefaultHttpContext());

            var submissionContext = new DefaultHttpContext();
            submissionContext.Request.Headers["X-LWC-Stats-Session"] = session;
            submissionContext.Request.Headers.Referer = "https://www.longevityworldcup.com/apply";
            await statistics.RecordServerEventAsync(
                session == "failed-application" ? "application_submit_failed" : "application_submit_succeeded",
                submissionContext, flow: "application", component: "application", step: "submit");
            if (session == "both-events")
            {
                await statistics.RecordClientEventAsync(new SiteStatisticsEventRequest
                {
                    EventName = "application_submit_succeeded", SessionId = session, Flow = "application"
                }, new DefaultHttpContext());
            }
        }

        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new ViewportSize { Width = 1440, Height = 980 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/internal/site-statistics.html?tab=Source%20Quality");

        var applications = page.Locator("#outcomeStrip .metric-tile")
            .Filter(new() { HasText = "Submissions accepted" }).Locator(".metric-value");
        await Assertions.Expect(applications).ToHaveTextAsync("3");
        var sourcePanel = page.Locator("#detailSections .detail-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Acquisition quality", Exact = true }) });
        var social = sourcePanel.Locator("tbody tr").Filter(new() { HasText = "social" });
        await Assertions.Expect(social.Locator("td")).ToHaveTextAsync(["social", "3", "0", "2", "0", "10"]);
        var directRow = sourcePanel.Locator("tbody tr").Filter(new() { HasText = "direct" });
        await Assertions.Expect(directRow.Locator("td")).ToHaveTextAsync(["direct", "1", "0", "1", "0", "5"]);

        var referrers = page.Locator("#detailSections .detail-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "First referrers", Exact = true }) });
        await Assertions.Expect(referrers.Locator("tbody tr").Filter(new() { HasText = "www.reddit.com" }).Locator("td"))
            .ToHaveTextAsync(["www.reddit.com", "3", "2"]);
        var campaigns = page.Locator("#detailSections .detail-panel")
            .Filter(new() { Has = page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns", Exact = true }) });
        await Assertions.Expect(campaigns.Locator("tbody tr").Filter(new() { HasText = "outreach" }).Locator("td"))
            .ToHaveTextAsync(["outreach", "3", "social", "0", "2"]);
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
        await page.Locator("#statsTabs").GetByRole(AriaRole.Button, new() { Name = "Overview", Exact = true }).WaitForAsync();
        await page.Locator("#trafficOverview").GetByText("Recorded sessions", new() { Exact = true }).WaitForAsync();
        await page.GetByLabel("Traffic totals").GetByText("Page views", new() { Exact = true }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Results & submissions" }).WaitForAsync();
        await page.Locator("#trafficOverview .traffic-quality-panel > summary").ClickAsync();
        await page.Locator("#trafficOverview").GetByText("Repeated-refresh sessions", new() { Exact = true }).WaitForAsync();
        await page.Locator("#trafficOverview .traffic-quality-panel > summary").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Daily sessions" }).WaitForAsync();
        await page.Locator("#trafficDailyChart").WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Most viewed pages" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Where sessions came from" }).WaitForAsync();
        await page.Locator("[data-traffic-disclosure=breakdowns] > summary").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Referrers" }).WaitForAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Devices" }).WaitForAsync();
        Assert.Contains(dashboardRequests, url => url.Contains("limit=100", StringComparison.Ordinal));
        var range30Response = page.WaitForResponseAsync(response =>
            response.Url.Contains("/api/site-statistics/dashboard", StringComparison.Ordinal) &&
            response.Url.Contains("range=30d", StringComparison.Ordinal) &&
            response.Ok);
        await page.Locator("#statsRange").SelectOptionAsync("30d");
        await range30Response;
        await Assertions.Expect(page.Locator("#trafficDay option")).ToHaveCountAsync(31);
        Assert.Contains(dashboardRequests, url => url.Contains("range=30d", StringComparison.Ordinal));
        await page.Locator("#statsDiagnostics > summary").ClickAsync();
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
        Assert.Contains("Decision Brief", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Noisy sessions", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page views", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unique visitors", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Challenge signups", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Signup accepted", visibleText);
        Assert.Contains("prefilled / initial", onboardingDetailText);
        Assert.Contains("AUTO", onboardingDetailText);
        Assert.Contains("Identity", applicationStageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Motivation", applicationStageText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Stopped before next", applicationStageText, StringComparison.OrdinalIgnoreCase);
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
        await page.Locator("#statsDiagnostics > summary").ClickAsync();
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
        Assert.Contains("Noisy sessions", visibleText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Page views", visibleText, StringComparison.OrdinalIgnoreCase);
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
        await page.Locator("#trafficOverview").GetByText("Recorded sessions", new() { Exact = true }).WaitForAsync();
        var downloadTask = page.WaitForDownloadAsync();
        await page.Locator("#statsExport").ClickAsync();
        var download = await downloadTask;
        await page.Locator("#statsStatus").GetByText("Exported 2 redacted events for last 90 days.", new() { Exact = true }).WaitForAsync();

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
