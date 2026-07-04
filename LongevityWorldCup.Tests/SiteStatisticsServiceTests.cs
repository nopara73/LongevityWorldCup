using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SiteStatisticsServiceTests
{
    [Fact]
    public async Task RecordClientEventAsync_RedactsSensitiveValuesFromDashboardProjection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Path = "/longevitymaxxing";
        context.Request.QueryString = new QueryString("?token=secret-access-token");
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/125";
        context.Request.Headers.Referer = "https://example.test/source-page";

        var request = new SiteStatisticsEventRequest
        {
            EventName = "challenge_signup_succeeded",
            SessionId = "raw-session-id",
            Flow = "challenge",
            Route = "/longevitymaxxing?token=secret-access-token&confirm=secret-confirm&campaign=abc",
            Component = "signup",
            Step = "pledge",
            Outcome = "succeeded",
            ErrorCode = "private-token-error",
            DeviceClass = "desktop",
            BrowserFamily = "Chrome",
            Source = "social",
            Metadata = new Dictionary<string, JsonElement>
            {
                ["email"] = JsonSerializer.SerializeToElement("athlete@example.test"),
                ["accessToken"] = JsonSerializer.SerializeToElement("private-token"),
                ["invoiceId"] = JsonSerializer.SerializeToElement("invoice-123"),
                ["safeEcho"] = JsonSerializer.SerializeToElement("fallback@example.test"),
                ["pledgeBucket"] = JsonSerializer.SerializeToElement("$300_999"),
                ["identityMode"] = JsonSerializer.SerializeToElement("participant")
            }
        };

        await service.RecordClientEventAsync(request, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d" });
        var json = JsonSerializer.Serialize(dashboard);
        var ev = Assert.Single(dashboard.Events);

        Assert.StartsWith("S-", ev.SessionHash);
        Assert.DoesNotContain("raw-session-id", json);
        Assert.DoesNotContain("secret-access-token", json);
        Assert.DoesNotContain("secret-confirm", json);
        Assert.DoesNotContain("athlete@example.test", json);
        Assert.DoesNotContain("fallback@example.test", json);
        Assert.DoesNotContain("private-token", json);
        Assert.DoesNotContain("invoice-123", json);
        Assert.Null(ev.ErrorCode);
        Assert.False(ev.Metadata.ContainsKey("safeEcho"));
        Assert.Equal("/longevitymaxxing?campaign=redacted", ev.Route);
        Assert.Equal("$300_999", ev.Metadata["pledgeBucket"]);
        Assert.Equal("participant", ev.Metadata["identityMode"]);
    }

    [Fact]
    public async Task DashboardEndpoint_ReturnsOnlyRedactedEvents()
    {
        await using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsync(
            "/api/site-statistics/event",
            JsonContent.Create(new SiteStatisticsEventRequest
            {
                EventName = "application_review_context_missing",
                SessionId = "browser-session",
                Flow = "application",
                Route = "/review?invoiceId=secret-invoice&token=secret-token",
                Component = "application_review",
                Outcome = "missing",
                Metadata = new Dictionary<string, JsonElement>
                {
                    ["accountEmail"] = JsonSerializer.SerializeToElement("applicant@example.test"),
                    ["paymentState"] = JsonSerializer.SerializeToElement("failed")
                }
            }));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var dashboard = await client.GetFromJsonAsync<SiteStatisticsDashboardResponse>("/api/site-statistics/dashboard?range=30d");
        Assert.NotNull(dashboard);
        var json = JsonSerializer.Serialize(dashboard);

        Assert.Contains("application_review_context_missing", json);
        Assert.DoesNotContain("browser-session", json);
        Assert.DoesNotContain("secret-invoice", json);
        Assert.DoesNotContain("secret-token", json);
        Assert.DoesNotContain("applicant@example.test", json);
        Assert.Contains("paymentState", json);
    }

    [Fact]
    public async Task GetDashboardAsync_ReturnsPreviousEquivalentPeriodForTrendDetection()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);

        await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        InsertDashboardEvent(database, DateTimeOffset.UtcNow.AddDays(-8), "S-PREV", "calculator_validation_failed", "pheno");

        var context = new DefaultHttpContext();
        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "calculator_started",
            SessionId = "current-session",
            Flow = "pheno",
            Route = "/pheno-age",
            Component = "calculator",
            Outcome = "started"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });

        Assert.Single(dashboard.Events);
        var previous = Assert.Single(dashboard.PreviousEvents);
        Assert.Equal("calculator_validation_failed", previous.EventName);
        Assert.Equal("S-PREV", previous.SessionHash);
        Assert.NotEqual(dashboard.Filters.FromUtc, dashboard.Filters.PreviousFromUtc);
        Assert.Equal(dashboard.Filters.FromUtc, dashboard.Filters.PreviousToUtc);
    }

    [Fact]
    public async Task Dashboard_ClassifiesSameDomainReferrersAsInternal()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);

        await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        InsertDashboardEvent(
            database,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "S-OLD",
            "site_page_viewed",
            "site",
            source: "referral",
            referrerDomain: "longevityworldcup.com");

        var context = new DefaultHttpContext();
        context.Request.Headers.Referer = "https://www.longevityworldcup.com/join";
        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "site_page_viewed",
            SessionId = "same-domain-referrer",
            Flow = "site",
            Route = "/pheno-age",
            Source = "referral",
            ReferrerDomain = "www.longevityworldcup.com"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        Assert.Equal(2, dashboard.Events.Count);
        Assert.All(dashboard.Events, ev => Assert.Equal("internal", ev.Source));

        var internalOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d", Source = "internal" });
        Assert.Equal(2, internalOnly.Events.Count);

        var referralOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d", Source = "referral" });
        Assert.Empty(referralOnly.Events);
    }

    [Fact]
    public async Task Dashboard_UsesSessionFirstTouchForAcquisitionSource()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);
        var context = new DefaultHttpContext();

        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "onboarding_entry_viewed",
            SessionId = "acquisition-session",
            Flow = "onboarding",
            Route = "/join?utm_source=google&utm_medium=cpc&utm_campaign=summer2026&campaign=summer-launch&token=private-token",
            ReferrerDomain = "www.google.com",
            Source = "search"
        }, context);

        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "calculator_started",
            SessionId = "acquisition-session",
            Flow = "pheno",
            Route = "/pheno-age",
            ReferrerDomain = "longevityworldcup.com",
            Source = "internal"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d" });
        Assert.Equal(2, dashboard.Events.Count);
        Assert.All(dashboard.Events, ev =>
        {
            Assert.Equal("search", ev.FirstSource);
            Assert.Equal("www.google.com", ev.FirstReferrerDomain);
            Assert.Equal("google", ev.FirstUtmSource);
            Assert.Equal("cpc", ev.FirstUtmMedium);
            Assert.Equal("summer2026", ev.FirstUtmCampaign);
            Assert.Equal("summer-launch", ev.FirstCampaign);
            Assert.Equal("/join?utm_source=redacted&utm_medium=redacted&utm_campaign=redacted&campaign=redacted", ev.LandingRoute);
        });

        var internalEvent = Assert.Single(dashboard.Events, ev => ev.EventName == "calculator_started");
        Assert.Equal("internal", internalEvent.Source);
        Assert.Equal("search", internalEvent.FirstSource);

        var searchOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Source = "search" });
        Assert.Equal(2, searchOnly.Events.Count);

        var internalOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Source = "internal" });
        Assert.Empty(internalOnly.Events);

        var json = JsonSerializer.Serialize(dashboard);
        Assert.DoesNotContain("private-token", json);
    }

    [Fact]
    public async Task Dashboard_ClassifiesCampaignTaggedDirectLandingAsCampaign()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);
        var context = new DefaultHttpContext();

        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "onboarding_entry_viewed",
            SessionId = "campaign-direct-session",
            Flow = "onboarding",
            Route = "/join?utm_source=newsletter&utm_medium=email&utm_campaign=challenge_launch"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d" });
        var ev = Assert.Single(dashboard.Events);

        Assert.Equal("campaign", ev.FirstSource);
        Assert.Equal("newsletter", ev.FirstUtmSource);
        Assert.Equal("email", ev.FirstUtmMedium);
        Assert.Equal("challenge_launch", ev.FirstUtmCampaign);
        Assert.Equal("challenge_launch", ev.FirstCampaign);

        var campaignOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Source = "campaign" });
        Assert.Single(campaignOnly.Events);
    }

    [Fact]
    public async Task Dashboard_ClassifiesGmailAppReferrerAsEmail()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);

        await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        InsertDashboardEvent(
            database,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "S-GMAIL-LEGACY",
            "site_page_viewed",
            "site",
            source: "search",
            referrerDomain: "com.google.android.gm");

        var context = new DefaultHttpContext();
        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "onboarding_entry_viewed",
            SessionId = "gmail-app-session",
            Flow = "onboarding",
            Route = "/join",
            Source = "search",
            ReferrerDomain = "com.google.android.gm"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });

        Assert.Equal(2, dashboard.Events.Count);
        Assert.All(dashboard.Events, ev => Assert.Equal("email", ev.Source));
        var recordedEvent = Assert.Single(dashboard.Events, ev => ev.EventName == "onboarding_entry_viewed");
        Assert.Equal("email", recordedEvent.FirstSource);
        Assert.Equal("com.google.android.gm", recordedEvent.FirstReferrerDomain);

        var emailOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d", Source = "email" });
        Assert.Equal(2, emailOnly.Events.Count);

        var searchOnly = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d", Source = "search" });
        Assert.Empty(searchOnly.Events);
    }

    [Fact]
    public async Task RecordServerEventAsync_UsesStatsSessionHeaderToMatchClientSession()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Headers["X-LWC-Stats-Session"] = "browser-session";

        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "challenge_page_viewed",
            SessionId = "browser-session",
            Flow = "challenge",
            Route = "/longevitymaxxing",
            Component = "challenge",
            Outcome = "viewed"
        }, new DefaultHttpContext());

        await service.RecordServerEventAsync(
            "challenge_signup_succeeded",
            context,
            actorId: "participant-1",
            flow: "challenge",
            route: "/longevitymaxxing",
            component: "signup",
            step: "submit",
            outcome: "succeeded",
            sessionId: "challenge:participant-1");

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Flow = "challenge" });

        Assert.Equal(2, dashboard.Events.Count);
        Assert.Single(dashboard.Events.Select(ev => ev.SessionHash).Distinct());
    }

    [Fact]
    public async Task RecordServerEventAsync_UsesExplicitSessionIdWithoutHttpContext()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);

        await service.RecordServerEventAsync(
            "challenge_signup_succeeded",
            actorId: "participant-1",
            flow: "challenge",
            route: "/longevitymaxxing",
            component: "signup",
            step: "submit",
            outcome: "succeeded",
            sessionId: "challenge:participant-1");
        await service.RecordServerEventAsync(
            "challenge_signup_succeeded",
            actorId: "participant-2",
            flow: "challenge",
            route: "/longevitymaxxing",
            component: "signup",
            step: "submit",
            outcome: "succeeded",
            sessionId: "challenge:participant-2");

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d", Flow = "challenge" });

        Assert.Equal(2, dashboard.Events.Count);
        Assert.Equal(2, dashboard.Events.Select(ev => ev.SessionHash).Distinct().Count());
        Assert.Equal(2, dashboard.Events.Select(ev => ev.ActorHash).Distinct().Count());
    }

    [Theory]
    [InlineData(
        "/pheno-age?Year=1980&Month=5&Day=20&Date=2026-06-01&AlbGL=44&CreatUmolL=80&GluMmolL=5.2",
        "/pheno-age",
        "prefilled")]
    [InlineData(
        "/pheno-age?update=1&Year=1980&Month=5&Day=20&AlbGL=44",
        "/pheno-age",
        "update")]
    [InlineData(
        "/bortz-age?discount=MIGHTYKLAUS&Year=1980&Wbc1000cellsuL=6.5",
        "/bortz-age",
        "discount")]
    [InlineData(
        "/bortz-age?fake=1&Year=1980&Wbc1000cellsuL=6.5",
        "/bortz-age",
        "fake")]
    [InlineData(
        "/onboarding/pheno-age.html?freepass=perfect&Year=1980&AlbGL=44",
        "/pheno-age",
        "discount")]
    public async Task Dashboard_CanonicalizesBioageCalculatorRoutesAndStoresEntryMode(
        string route,
        string expectedRoute,
        string expectedEntryMode)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);
        var context = new DefaultHttpContext();

        await service.RecordClientEventAsync(new SiteStatisticsEventRequest
        {
            EventName = "calculator_started",
            SessionId = Guid.NewGuid().ToString("N"),
            Flow = expectedRoute.Contains("bortz", StringComparison.OrdinalIgnoreCase) ? "bortz" : "pheno",
            Route = route,
            Component = "calculator"
        }, context);

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "30d" });
        var ev = Assert.Single(dashboard.Events);
        var json = JsonSerializer.Serialize(dashboard);

        Assert.Equal(expectedRoute, ev.Route);
        Assert.Equal(expectedRoute, ev.LandingRoute);
        Assert.Equal(expectedEntryMode, ev.Metadata["entryMode"]);
        Assert.DoesNotContain("Year=redacted", json);
        Assert.DoesNotContain("AlbGL=redacted", json);
        Assert.DoesNotContain("Wbc1000cellsuL=redacted", json);
        Assert.DoesNotContain("MIGHTYKLAUS", json);
        Assert.DoesNotContain("perfect", json);
        Assert.DoesNotContain("1980", json);
    }

    [Fact]
    public async Task Dashboard_CanonicalizesLegacyRedactedBioageRoutesOnRead()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), "LongevityWorldCup.Tests", $"{Guid.NewGuid():N}.db");
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        await using var cleanup = new TempDatabaseCleanup(dbPath);
        using var database = new DatabaseManager(dbPath: dbPath);
        var service = new SiteStatisticsService(database, NullLogger<SiteStatisticsService>.Instance);

        await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        InsertDashboardEvent(
            database,
            DateTimeOffset.UtcNow.AddMinutes(-5),
            "S-LEGACY-BIOAGE",
            "calculator_started",
            "pheno",
            route: "/pheno-age?update=redacted&Year=redacted&Month=redacted&Day=redacted&AlbGL=redacted&CreatUmolL=redacted");

        var dashboard = await service.GetDashboardAsync(new SiteStatisticsDashboardQuery { Range = "7d" });
        var ev = Assert.Single(dashboard.Events);
        var json = JsonSerializer.Serialize(dashboard);

        Assert.Equal("/pheno-age", ev.Route);
        Assert.Equal("update", ev.Metadata["entryMode"]);
        Assert.DoesNotContain("update=redacted", json);
        Assert.DoesNotContain("Year=redacted", json);
        Assert.DoesNotContain("AlbGL=redacted", json);
    }

    private static void InsertDashboardEvent(
        DatabaseManager database,
        DateTimeOffset occurredAtUtc,
        string sessionHash,
        string eventName,
        string flow,
        string source = "direct",
        string? referrerDomain = null,
        string route = "/pheno-age")
    {
        database.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO SiteStatisticEvents
                (Id, OccurredAtUtc, SessionHash, EventName, Flow, Route, Component, Step, Outcome,
                 ErrorCode, DurationMs, DeviceClass, BrowserFamily, ReferrerDomain, Source, MetadataJson)
                VALUES
                (@id, @occurred, @session, @eventName, @flow, @route, @component, @step, @outcome,
                 NULL, NULL, @device, @browser, @referrer, @source, NULL);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@occurred", occurredAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@session", sessionHash);
            cmd.Parameters.AddWithValue("@eventName", eventName);
            cmd.Parameters.AddWithValue("@flow", flow);
            cmd.Parameters.AddWithValue("@route", route);
            cmd.Parameters.AddWithValue("@component", "calculator");
            cmd.Parameters.AddWithValue("@step", "glucose");
            cmd.Parameters.AddWithValue("@outcome", "failed");
            cmd.Parameters.AddWithValue("@device", "desktop");
            cmd.Parameters.AddWithValue("@browser", "Chrome");
            cmd.Parameters.AddWithValue("@source", source);
            cmd.Parameters.AddWithValue("@referrer", referrerDomain ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        });
    }

    private sealed class TempDatabaseCleanup(string path) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    foreach (var file in Directory.GetFiles(dir, Path.GetFileName(path) + "*"))
                    {
                        File.Delete(file);
                    }
                }
            }
            catch
            {
            }

            return ValueTask.CompletedTask;
        }
    }
}
