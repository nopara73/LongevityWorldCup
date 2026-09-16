using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AiReferralReportingTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lwc-ai-{Guid.NewGuid():N}.db");
    private readonly DatabaseManager _database;
    private readonly SiteStatisticsService _service;

    public AiReferralReportingTests()
    {
        _database = new DatabaseManager(dbPath: _path);
        _service = new SiteStatisticsService(_database, NullLogger<SiteStatisticsService>.Instance);
    }

    [Theory]
    [InlineData("https://CHATGPT.COM./c/123", null, "chatgpt", "referrer")]
    [InlineData("chat.openai.com", null, "chatgpt", "referrer")]
    [InlineData("www.perplexity.ai", null, "perplexity", "referrer")]
    [InlineData("claude.ai", null, "claude", "referrer")]
    [InlineData("gemini.google.com", null, "gemini", "referrer")]
    [InlineData("copilot.microsoft.com", null, "copilot", "referrer")]
    [InlineData("grok.com", null, "grok", "referrer")]
    [InlineData("chat.deepseek.com", null, "deepseek", "referrer")]
    [InlineData("chat.mistral.ai", null, "mistral", "referrer")]
    [InlineData("claude.ai", " CHATGPT.COM ", "chatgpt", "campaign")]
    [InlineData("google.com", "perplexity", "perplexity", "campaign")]
    [InlineData("claude.ai", "newsletter", "claude", "referrer")]
    [InlineData(null, "lechat", "mistral", "campaign")]
    [InlineData("evilchatgpt.com", null, null, null)]
    [InlineData("chatgpt.com.evil.test", null, null, null)]
    [InlineData("random.chatgpt.com", null, null, null)]
    [InlineData("https://chatgpt.com@evil.test", null, null, null)]
    [InlineData("https://evil.test@chatgpt.com", null, null, null)]
    [InlineData("https://evil.test/chatgpt.com", null, null, null)]
    [InlineData("chatgpt!.com", null, null, null)]
    [InlineData("chatgpt.com..", null, null, null)]
    [InlineData("chatgpt.com/path", null, null, null)]
    [InlineData("javascript://chatgpt.com", null, null, null)]
    [InlineData("google.com", "ai", null, null)]
    [InlineData("bing.com", "bing", null, null)]
    [InlineData("x.com", "grok-launch", null, null)]
    [InlineData("openai.com", "openai", null, null)]
    [InlineData(null, "https://chatgpt.com", null, null)]
    [InlineData(null, "chatgpt!.com", null, null)]
    public void ClassificationUsesExactEvidence(string? host, string? tag, string? provider, string? basis)
    {
        var result = AiReferralPolicy.Classify(host, tag);
        Assert.Equal(provider, result?.Provider);
        Assert.Equal(basis, result?.Basis);
    }

    [Theory]
    [InlineData("direct", null)]
    [InlineData("referral", "example.test")]
    [InlineData("ai", "claude.ai")]
    public async Task CapturedFirstTouchCannotBeRewrittenByLaterCampaign(string source, string? host)
    {
        await Record("visitor", "site_page_viewed", host: host, source: source);
        await Read();
        await Record("visitor", "calculator_used", host: "chatgpt.com", tag: "chatgpt", landing: "/pheno-age");
        var report = await Read();
        Assert.All(report.Events, e =>
        {
            Assert.Equal(source, e.FirstSource);
            Assert.Equal("/join", e.LandingRoute);
            Assert.Null(e.FirstUtmSource);
        });
        Assert.Equal(source == "ai" ? 1 : 0, report.AiReferrals.Totals.Visits);
    }

    [Fact]
    public async Task ReportUsesFullWindowUniqueSessionsAndMatchedDenominators()
    {
        foreach (var id in new[] { "converted", "failed", "legacy", "update", "late" })
            await Record(id, "site_page_viewed", host: "claude.ai", landing: "/pheno-age?x=redacted");
        await Record("tagged", "site_page_viewed", tag: "chatgpt.com", landing: "/join");
        await Record("direct", "site_page_viewed");
        await Record("converted", "calculator_used");
        await Record("converted", "calculator_result_generated");
        await Record("converted", "application_started");
        await Record("converted", "application_started");
        await Accepted("converted", "full-application");
        await Accepted("converted", "full-application");
        await Record("failed", "application_started");
        await _service.RecordServerEventAsync("application_submit_failed", sessionId: "failed");
        await Record("failed", "application_submit_succeeded"); // ignored: only the server can confirm
        await Accepted("legacy", null);
        await Accepted("update", "result-upload");
        await Accepted("late", "full-application"); // start outside the window / not observed

        var dashboard = await Read(limit: 1);
        var report = dashboard.AiReferrals;
        Assert.Single(dashboard.Events);
        Assert.Equal(6, report.Totals.Visits);
        Assert.Equal(5, report.Totals.ReferrerVisits);
        Assert.Equal(1, report.Totals.CampaignVisits);
        Assert.Equal(2, report.Totals.ApplicationStarts);
        Assert.Equal(2, report.Totals.Applications);
        Assert.Equal(3, report.Totals.ApplicationEvents);
        Assert.Equal(1, report.Totals.UnknownApplicationSessions);
        Assert.Equal(2d / 6, report.Totals.VisitToApplicationRate);
        Assert.Equal(0.5, report.Totals.StartToApplicationRate);
        Assert.Equal(report.Totals.Visits, report.Providers.Sum(r => r.Visits));
        Assert.Equal(report.Totals.Applications, report.LandingPages.Sum(r => r.Applications));
        Assert.Contains(report.LandingPages, r => r.Key == "/pheno-age" && r.Visits == 5);
        var filtered = await Read(source: "ai:claude");
        Assert.Equal(5, filtered.TrafficSummary.Totals.Sessions);
        Assert.All(filtered.Events, e => Assert.Equal("claude", e.AiProvider));
        Assert.Equal(5, filtered.AiReferrals.Totals.Visits);
        Assert.Single(filtered.AiReferrals.Providers);
        Assert.Equal(6, (await Read(source: "ai")).TrafficSummary.Totals.Sessions);
        var empty = await Read(source: "ai:copilot");
        Assert.Null(empty.AiReferrals.Totals.VisitToApplicationRate);
    }

    [Fact]
    public async Task ProviderPrecedenceAndFiltersAgreeAcrossSummaryEventsAndPages()
    {
        await Record("conflict", "site_page_viewed", host: "claude.ai", tag: "CHATGPT.COM", flow: "pheno");
        await Record("referrer", "site_page_viewed", host: "claude.ai", flow: "application");
        var result = await _service.GetDashboardAsync(new() { Source = "ai:chatgpt", Flow = "pheno", Device = "desktop", Limit = 1 });
        Assert.Equal(1, result.AiReferrals.Totals.Visits);
        Assert.Equal(1, result.AiReferrals.Totals.CampaignVisits);
        Assert.Equal(result.TrafficSummary.Totals.Sessions, result.AiReferrals.Totals.Visits);
        Assert.Equal("campaign", Assert.Single(result.Events).AiAttributionBasis);
        var page = await _service.GetDashboardEventsPageAsync(new()
        {
            Source = "ai:chatgpt", Flow = "pheno", Device = "desktop",
            FromUtc = DateTimeOffset.Parse(result.Filters.FromUtc, CultureInfo.InvariantCulture),
            ToUtc = DateTimeOffset.Parse(result.Filters.ToUtc, CultureInfo.InvariantCulture)
        });
        Assert.Equal("chatgpt", Assert.Single(page.Events).AiProvider);
        var otherFlow = await _service.GetDashboardAsync(new() { Source = "ai:chatgpt", Flow = "application" });
        Assert.Equal(0, otherFlow.AiReferrals.Totals.Visits);
    }

    [Theory]
    [InlineData("/join?UTM_SOURCE=chatgpt.com", "ai")]
    [InlineData("/join?utm_source=chatgpt%21.com", "direct")]
    [InlineData("/join?utm_campaign=chatgpt.com", "campaign")]
    public async Task QueryEvidenceIsParsedWithoutPromotingLookalikes(string landing, string expected)
    {
        await _service.RecordClientEventAsync(new() { EventName = "site_page_viewed", SessionId = "query", Route = landing }, new DefaultHttpContext());
        var result = await Read();
        Assert.Equal(expected, Assert.Single(result.Events).FirstSource);
    }

    [Fact]
    public async Task OldSchemaMigrationPreservesHistoryAndCoverageAcrossRestart()
    {
        await Record("old-schema", "site_page_viewed", host: "chatgpt.com");
        var first = await Read();
        _database.Run(db =>
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "ALTER TABLE SiteStatisticSessions DROP COLUMN FirstTouchCaptured; UPDATE SiteStatisticSessions SET FirstSource = 'referral';";
            cmd.ExecuteNonQuery();
        });
        var restarted = new SiteStatisticsService(_database, NullLogger<SiteStatisticsService>.Instance);
        await restarted.RecordClientEventAsync(new() { SessionId = "old-schema", EventName = "site_page_viewed", FirstSource = "campaign", FirstUtmSource = "claude", LandingRoute = "/apply" }, new DefaultHttpContext());
        var report = await restarted.GetDashboardAsync(new());
        Assert.Equal(2, report.Events.Count);
        Assert.All(report.Events, e => Assert.Equal("chatgpt", e.AiProvider));
        Assert.Equal(first.AiReferrals.InteractionTrackingSinceUtc, report.AiReferrals.InteractionTrackingSinceUtc);
    }

    [Fact]
    public async Task FirstTouchHeaderPreservesAttributionWhenNoClientBeaconArrives()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-LWC-Stats-Session"] = "header-only";
        context.Request.Headers.Referer = "https://www.longevityworldcup.com/apply";
        context.Request.Headers["X-LWC-Stats-First-Touch"] = Uri.EscapeDataString(JsonSerializer.Serialize(new
        {
            landingRoute = "/join", firstSource = "campaign", firstUtmSource = "chatgpt.com", firstReferrerDomain = ""
        }));
        await _service.RecordServerEventAsync("application_submit_succeeded", context, metadata: new Dictionary<string, object?> { ["submissionKind"] = "full-application" });
        var report = await Read();
        Assert.Equal("chatgpt", Assert.Single(report.Events).AiProvider);
        Assert.Equal(1, report.AiReferrals.Totals.Applications);
    }

    [Fact]
    public async Task HistoricalEvidenceIsReclassifiedWithoutChangingStoredRowsOrInventingNewSteps()
    {
        await Record("history", "site_page_viewed", host: "gemini.google.com");
        await Read();
        _database.Run(db =>
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE SiteStatisticSessions SET FirstSource='search'; UPDATE SiteStatisticEvents SET Source='search', OccurredAtUtc=@old;";
            cmd.Parameters.AddWithValue("@old", DateTimeOffset.UtcNow.AddDays(-40).ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        });
        Assert.Equal(0, (await Read()).AiReferrals.Totals.Visits);
        var history = await _service.GetDashboardAsync(new() { Range = "90d", Source = "ai:gemini" });
        Assert.Equal(1, history.AiReferrals.Totals.Visits);
        Assert.Equal(0, history.AiReferrals.Totals.ApplicationStarts);
        Assert.Equal(0, history.AiReferrals.Totals.CalculatorUses);
        Assert.Equal("ai", Assert.Single(history.Events).FirstSource);
        Assert.Equal("search", _database.Run(db => { using var c = db.CreateCommand(); c.CommandText = "SELECT FirstSource FROM SiteStatisticSessions"; return c.ExecuteScalar(); }));
    }

    [Fact]
    public async Task ClientCannotClaimAcceptanceUsingASanitizedEventName()
    {
        foreach (var name in new[] { "application_submit_succeeded", " application_submit_succeeded!", "APPLICATION_SUBMIT_SUCCEEDED", "application_submit_accepted" })
            await Record("client-claim", name, host: "chatgpt.com");
        Assert.Empty((await Read()).Events);
    }

    [Fact]
    public async Task NewMilestonesDeduplicateAcrossServiceRestartsButKeepClockDistinction()
    {
        await Record("repeat", "calculator_used", flow: "pheno");
        await Record("repeat", "calculator_used", flow: "pheno");
        await Read();
        var restarted = new SiteStatisticsService(_database, NullLogger<SiteStatisticsService>.Instance);
        await restarted.RecordClientEventAsync(new() { SessionId = "repeat", EventName = "calculator_used", Flow = "pheno" }, new DefaultHttpContext());
        await restarted.RecordClientEventAsync(new() { SessionId = "repeat", EventName = "calculator_used", Flow = "bortz" }, new DefaultHttpContext());
        Assert.Equal(2, (await restarted.GetDashboardAsync(new())).Events.Count);
    }

    private Task Record(string session, string name, string? host = null, string? tag = null,
        string source = "direct", string landing = "/join", string flow = "application") =>
        _service.RecordClientEventAsync(new()
        {
            EventName = name, SessionId = session, Flow = flow, DeviceClass = "desktop", Route = "/apply", LandingRoute = landing,
            FirstSource = source, FirstReferrerDomain = host, FirstUtmSource = tag
        }, new DefaultHttpContext());

    private Task Accepted(string session, string? kind) => _service.RecordServerEventAsync("application_submit_succeeded",
        sessionId: session, flow: "application", metadata: kind is null ? null : new Dictionary<string, object?> { ["submissionKind"] = kind });

    private Task<SiteStatisticsDashboardResponse> Read(int limit = 5000, string? source = null) =>
        _service.GetDashboardAsync(new() { Range = "7d", Limit = limit, Source = source });

    public void Dispose()
    {
        _database.Dispose();
        foreach (var suffix in new[] { "", "-shm", "-wal" }) File.Delete(_path + suffix);
    }
}
