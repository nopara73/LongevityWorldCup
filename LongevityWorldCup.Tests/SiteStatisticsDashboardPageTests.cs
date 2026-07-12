using Xunit;
using System.Runtime.CompilerServices;
using static LongevityWorldCup.Tests.FrontendSourceTestHelper;

namespace LongevityWorldCup.Tests;

public sealed class SiteStatisticsDashboardPageTests
{
    [Fact]
    public async Task SiteStatisticsDashboardPage_UsesVersionedLocalAssets()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/site-statistics.html");

        Assert.Contains("/css/site-statistics.css?v=", html);
        Assert.Contains("/js/site-statistics.js?v=", html);
        Assert.Contains("trafficOverview", html);
        Assert.Contains("<span>Timeframe</span>", html);
        Assert.Contains("<option value=\"90d\">90D</option>", html);
        Assert.Contains("<option value=\"alltime\">All-time</option>", html);
        Assert.Contains("Decision Brief", html);
        Assert.Contains("Recommended Investigations", html);
        Assert.Contains("Segment Comparison", html);
        Assert.Contains("Trend Watch", html);
        Assert.Contains("dataQualityStrip", html);
        Assert.Contains("<option value=\"email\">Email</option>", html);
        Assert.Contains("<option value=\"internal\">Internal</option>", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_CSS}}", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_JS}}", html);
        Assert.DoesNotContain("{{ASSET_POPPINS_REGULAR}}", html);
    }

    [Fact]
    public void SiteStatisticsDashboard_SelectOptionsHaveReadableNativePopupColors()
    {
        var repoRoot = FindRepoRoot();
        var css = File.ReadAllText(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "css", "site-statistics.css"));

        Assert.Contains("select option", css);
        Assert.Contains("color: #11161d;", css);
        Assert.Contains("background: #ffffff;", css);
        Assert.Contains("select option:checked", css);
    }

    [Theory]
    [InlineData("/join")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    [InlineData("/apply")]
    [InlineData("/proofs")]
    [InlineData("/review")]
    [InlineData("/longevitymaxxing")]
    public async Task OnboardingAndChallengePages_UseVersionedStatisticsTracker(string path)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/site-statistics-tracking.js", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_TRACKING_JS}}", html);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/leaderboard")]
    [InlineData("/events")]
    [InlineData("/athlete/michael-lustgarten")]
    [InlineData("/league/pheno")]
    public async Task PublicDashboardEventPages_UseVersionedStatisticsTracker(string path)
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/site-statistics-tracking.js?v=", html);
        Assert.DoesNotContain("{{ASSET_SITE_STATISTICS_TRACKING_JS}}", html);
    }

    [Fact]
    public void SiteStatisticsTracker_SupportsCurrentJoinMenuControls()
    {
        var repoRoot = FindRepoRoot();
        var menu = File.ReadAllText(Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "play", "menu.html"));
        var tracker = ReadFrontendSource("site-statistics-tracking.ts");

        Assert.Contains("id=\"joinStartAmateurBtn\"", menu);
        Assert.Contains("id=\"joinGoProButton\"", menu);
        Assert.Contains("id=\"joinStartChallengeLink\"", menu);
        Assert.Contains("play-join-biomarkers", menu);
        Assert.Contains("joinStartAmateurBtn", tracker);
        Assert.Contains("joinGoProButton", tracker);
        Assert.Contains("joinStartChallengeLink", tracker);
        Assert.Contains("onboarding_challenge_selected", tracker);
        Assert.Contains("crypto.getRandomValues(new Uint8Array(16))", tracker);
        Assert.DoesNotContain("Math.random()", tracker);
        Assert.Contains(".play-join-biomarkers details", tracker);
        Assert.Contains(".play-join-card--pro", tracker);
        Assert.Contains("function setupSpaRouteTracking()", tracker);
        Assert.Contains("trackJoinPanelViewForCurrentRoute", tracker);
        Assert.Contains("window.history[method] = function ()", tracker);
        Assert.Contains("function isIgnoredClientErrorMessage(message: unknown): boolean", tracker);
        Assert.Contains("ResizeObserver loop completed with undelivered notifications", tracker);
        Assert.Contains("function hasApplicationReviewContext()", tracker);
        Assert.Contains("isReviewSource(params.get(\"from\"))", tracker);
        Assert.Contains("isReviewSource(sessionStorage.getItem(\"came-from\"))", tracker);
        Assert.Contains("function isEmailReferrer(host: string): boolean", tracker);
        const string emailSourceLine = "if (isEmailReferrer(host)) return \"email\";";
        const string searchSourceLine = "if (/google|bing|duckduckgo|yahoo|brave|search/i.test(host)) return \"search\";";
        Assert.Contains(emailSourceLine, tracker);
        Assert.Contains(searchSourceLine, tracker);
        Assert.True(tracker.IndexOf(emailSourceLine, StringComparison.Ordinal) < tracker.IndexOf(searchSourceLine, StringComparison.Ordinal));
        Assert.Contains("return \"internal\";", tracker);
    }

    [Fact]
    public void SiteStatisticsTracker_EmitsDashboardDefinedPublicAndChallengeEvents()
    {
        var tracker = ReadFrontendSource("site-statistics-tracking.ts");
        var dashboard = ReadFrontendSource("site-statistics.ts");
        var rankPreview = ReadFrontendSource("bioage-rank-preview.ts");
        var proofHelpers = ReadFrontendSource("proof-helpers.ts");

        var expectedDashboardEvents = new[]
        {
            "rank_preview_requested",
            "proof_file_rejected",
            "challenge_athlete_search_result_selected",
            "challenge_commitment_block_seen",
            "homepage_highlight_viewed",
            "homepage_highlight_clicked",
            "event_viewed",
            "event_link_clicked",
            "athlete_profile_viewed",
            "league_viewed"
        };

        foreach (var eventName in expectedDashboardEvents)
        {
            Assert.Contains(eventName, dashboard);
        }

        Assert.Contains("challenge_athlete_search_result_selected", tracker);
        Assert.Contains("challenge_commitment_block_seen", tracker);
        Assert.Contains("homepage_highlight_viewed", tracker);
        Assert.Contains("homepage_highlight_clicked", tracker);
        Assert.Contains("event_viewed", tracker);
        Assert.Contains("event_link_clicked", tracker);
        Assert.Contains("athlete_profile_viewed", tracker);
        Assert.Contains("league_viewed", tracker);
        Assert.Contains("rank_preview_requested", rankPreview);
        Assert.Contains("rank_preview_failed", rankPreview);
        Assert.Contains("proof_file_rejected", proofHelpers);
        Assert.Contains("#lmxSignupAthlete-autocomplete-list .lmx-athlete-option", tracker);
        Assert.Contains("lmxCommitmentPanel", tracker);
        Assert.Contains("querySelector(\".lmx-commitment-card[data-commitment-block='true']\")", tracker);
        Assert.DoesNotContain("querySelector(\".lmx-commitment-card\")", tracker);
        Assert.Contains("setupPublicContentTracking", tracker);
        Assert.Contains("trackPublicPageViews", tracker);
        Assert.Contains("function isIgnoredClientError(e: DashboardEvent | null | undefined): boolean", dashboard);
    }

    [Fact]
    public void SiteStatisticsTracker_RecordsPrefilledCalculatorProgress()
    {
        var tracker = ReadFrontendSource("site-statistics-tracking.ts");

        Assert.Contains("function fieldHasRequiredValue(el: RequiredCalculatorControl): boolean", tracker);
        Assert.Contains("function recordFieldCompletion(el: RequiredCalculatorControl, source: string): boolean", tracker);
        Assert.Contains("function scanRequiredFields(source: string): void", tracker);
        Assert.Contains("scanRequiredFields(\"initial\")", tracker);
        Assert.Contains("scanRequiredFields(\"autofill\")", tracker);
        Assert.Contains("scanRequiredFields(\"submit\")", tracker);
        Assert.Contains("scanRequiredFields(\"result\")", tracker);
        Assert.Contains("completionSource", tracker);
        Assert.Contains("listen(form, \"input\"", tracker);
        Assert.Contains("listen(window, \"pageshow\"", tracker);
    }

    [Fact]
    public void SiteStatisticsDashboard_SurfacesCalculatorCompletionSources()
    {
        var dashboard = ReadFrontendSource("site-statistics.ts");

        Assert.Contains("Calculator completion sources", dashboard);
        Assert.Contains("function calculatorCompletionSourceTable(events: DashboardEvent[]): string", dashboard);
        Assert.Contains("function completionSourceLabel(source: string): string", dashboard);
        Assert.Contains("completionSource", dashboard);
        Assert.Contains("entryMode", dashboard);
        Assert.Contains("source-visual-list", dashboard);
        Assert.Contains("field-visual-list", dashboard);
        Assert.Contains("completionLegend", dashboard);
        Assert.Contains("stacked-bar", dashboard);
        Assert.Contains("\"Auto\"", dashboard);
        Assert.Contains("\"Late\"", dashboard);
    }

    [Fact]
    public void SiteStatisticsTracker_DoesNotFlagValidApplicationEntryPagesAsMissingHandoff()
    {
        var tracker = ReadFrontendSource("site-statistics-tracking.ts");
        var dashboard = ReadFrontendSource("site-statistics.ts");

        var pageViewTrackingStart = tracker.IndexOf("function setupPageViews()", StringComparison.Ordinal);
        Assert.True(pageViewTrackingStart >= 0);

        var applyBlockStart = tracker.IndexOf("if (path.includes(\"convergence\") || path === \"/apply\")", pageViewTrackingStart, StringComparison.Ordinal);
        var proofsBlockStart = tracker.IndexOf("if (path.includes(\"proof-upload\") || path === \"/proofs\")", pageViewTrackingStart, StringComparison.Ordinal);
        Assert.True(applyBlockStart >= 0);
        Assert.True(proofsBlockStart > applyBlockStart);

        var applyBlock = tracker[applyBlockStart..proofsBlockStart];
        Assert.Contains("track(\"proof_flow_opened\"", applyBlock);
        Assert.DoesNotContain("proof_flow_missing_handoff", applyBlock);

        var calculatorBlockStart = tracker.IndexOf("if (flowFromPath() === \"pheno\"", proofsBlockStart, StringComparison.Ordinal);
        Assert.True(calculatorBlockStart > proofsBlockStart);

        var proofsBlock = tracker[proofsBlockStart..calculatorBlockStart];
        Assert.Contains("proof_flow_missing_handoff", proofsBlock);

        Assert.Contains("function isBenignMissingContextEvent(e: DashboardEvent | null | undefined): boolean", dashboard);
        Assert.Contains("route === \"/apply\"", dashboard);
        Assert.Contains("route.includes(\"from=proof-upload\")", dashboard);
        Assert.DoesNotContain("route.includes(\"from=redacted\")", dashboard);
        Assert.Contains("!isBenignMissingContextEvent(e)", dashboard);
    }

    [Fact]
    public void SiteStatisticsDashboard_SurfacesTrafficOverview()
    {
        var dashboard = ReadFrontendSource("site-statistics.ts");

        Assert.Contains("\"Traffic Overview\"", dashboard);
        Assert.Contains("\"Onboarding Diagnostics\"", dashboard);
        Assert.Contains("\"Challenge Diagnostics\"", dashboard);
        Assert.Contains("\"Source Quality\"", dashboard);
        Assert.Contains("\"Reliability Diagnostics\"", dashboard);
        Assert.Contains("\"Review Queue Diagnostics\"", dashboard);
        Assert.Contains("\"Public Event Diagnostics\"", dashboard);
        Assert.DoesNotContain("\"Overview\"", dashboard);
        Assert.Contains("payload.trafficSummary", dashboard);
        Assert.Contains("Visitor sessions", dashboard);
        Assert.Contains("Page views", dashboard);
        Assert.Contains("Conversions over time", dashboard);
        Assert.Contains("Conversion actions", dashboard);
        Assert.Contains("Converting sessions", dashboard);
        Assert.Contains("Session conversion rate", dashboard);
        Assert.DoesNotContain("Website success over time", dashboard);
        Assert.Contains("successTrendChart", dashboard);
        Assert.Contains("Daily traffic", dashboard);
        Assert.Contains("Top pages", dashboard);
        Assert.Contains("Sources", dashboard);
        Assert.Contains("Referrers", dashboard);
        Assert.Contains("Devices", dashboard);
        Assert.Contains("Interactions", dashboard);
        Assert.Contains("Ranked pages", dashboard);
        Assert.Contains("Clean vs raw traffic", dashboard);
        Assert.Contains("Clean sessions", dashboard);
        Assert.Contains("Noisy sessions", dashboard);
        Assert.Contains("Top-session share", dashboard);
        Assert.Contains("Noisy page-view share", dashboard);
        Assert.Contains("Repeated-refresh sessions", dashboard);
        Assert.Contains("function pageViewMixLabel(quality: TrafficQuality): string", dashboard);
        Assert.DoesNotContain("Unique visitors", dashboard);
        Assert.Contains("function dailyTrafficChart(points: TrafficDailyPoint[]): string", dashboard);
        Assert.Contains("function trafficPageTable(rows: TrafficPage[]): string", dashboard);
        Assert.Contains("function normalizeTrafficSummary(summary: RawTrafficSummary | null | undefined): TrafficSummary", dashboard);
        Assert.Contains("function normalizeTrafficQuality(quality: RawTrafficQuality | null | undefined): TrafficQuality", dashboard);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var dir = Path.GetDirectoryName(sourceFilePath)!;
        while (dir is not null && !File.Exists(Path.Combine(dir, "LongevityWorldCup.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        return dir ?? throw new InvalidOperationException("Could not find repository root.");
    }
}
