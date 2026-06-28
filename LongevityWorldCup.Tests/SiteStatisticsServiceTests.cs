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

    private static void InsertDashboardEvent(DatabaseManager database, DateTimeOffset occurredAtUtc, string sessionHash, string eventName, string flow)
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
                 NULL, NULL, @device, @browser, NULL, @source, NULL);
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
            cmd.Parameters.AddWithValue("@occurred", occurredAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@session", sessionHash);
            cmd.Parameters.AddWithValue("@eventName", eventName);
            cmd.Parameters.AddWithValue("@flow", flow);
            cmd.Parameters.AddWithValue("@route", "/pheno-age");
            cmd.Parameters.AddWithValue("@component", "calculator");
            cmd.Parameters.AddWithValue("@step", "glucose");
            cmd.Parameters.AddWithValue("@outcome", "failed");
            cmd.Parameters.AddWithValue("@device", "desktop");
            cmd.Parameters.AddWithValue("@browser", "Chrome");
            cmd.Parameters.AddWithValue("@source", "direct");
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
