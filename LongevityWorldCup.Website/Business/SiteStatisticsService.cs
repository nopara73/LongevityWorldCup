using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class SiteStatisticsService : IHostedService
{
    private const string StatsSessionHeaderName = "X-LWC-Stats-Session";
    private const int MaxEventNameLength = 96;
    private const int MaxSessionIdLength = 256;
    private const int MaxTextLength = 160;
    private const int MaxCampaignTextLength = 96;
    private const int MaxMetadataJsonLength = 4096;
    private const int DefaultDashboardLimit = 2500;
    private const int MaxDashboardLimit = 5000;
    private const int MaxQueuedEvents = 2000;
    private const int FlushBatchSize = 250;
    private const string PageViewEventNamesSql = "'site_page_viewed','onboarding_entry_viewed','onboarding_page_viewed','challenge_page_viewed'";
    private const string SuccessActionEventNamesSql = "'calculator_result_generated','application_submit_succeeded','application_submit_accepted','challenge_signup_succeeded'";
    private const string EffectiveSourceSql =
        """
        CASE
            WHEN lower(coalesce(s.FirstReferrerDomain, '')) IN ('longevityworldcup.com', 'www.longevityworldcup.com') THEN 'internal'
            WHEN lower(coalesce(s.FirstReferrerDomain, '')) = 'com.google.android.gm'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE 'mail.%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%.mail.%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%gmail%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%outlook%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%hotmail%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%protonmail%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%proton.me%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%fastmail%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%icloud%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%mail.yahoo%'
              OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%yahoomail%' THEN 'email'
            WHEN s.FirstSource IS NOT NULL THEN s.FirstSource
            WHEN lower(coalesce(e.ReferrerDomain, '')) IN ('longevityworldcup.com', 'www.longevityworldcup.com') THEN 'internal'
            WHEN lower(coalesce(e.ReferrerDomain, '')) = 'com.google.android.gm'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE 'mail.%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%.mail.%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%gmail%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%outlook%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%hotmail%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%protonmail%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%proton.me%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%fastmail%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%icloud%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%mail.yahoo%'
              OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%yahoomail%' THEN 'email'
            ELSE coalesce(e.Source, 'direct')
        END
        """;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultDashboardRange = TimeSpan.FromDays(30);
    private static readonly HashSet<string> InternalReferrerDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "longevityworldcup.com",
        "www.longevityworldcup.com"
    };
    private static readonly string[] SensitiveQueryKeys =
    [
        "token", "confirm", "stop", "accessToken", "stopToken", "invoiceId", "checkoutLink",
        "email", "accountEmail", "name", "displayName"
    ];
    private static readonly HashSet<string> BioageCalculatorPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/pheno-age",
        "/bortz-age",
        "/onboarding/pheno-age.html",
        "/onboarding/bortz-age.html"
    };
    private static readonly HashSet<string> BioagePrefillQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Year", "Month", "Day", "Date",
        "AlbGL", "AlpUL", "CreatUmolL", "CrpMgL", "GluMmolL", "LymPc", "McvFL", "RdwPc",
        "Wbc1000cellsuL", "NeutrophilPc", "MonocytePc", "Rbc10e12L", "MchPg", "AltUL", "GgtUL",
        "UreaMmolL", "CystatinCMgL", "Hba1cMmolMol", "CholesterolMmolL", "ApoA1GL", "ShbgNmolL",
        "VitaminDNmolL"
    };
    private static readonly HashSet<string> SensitiveMetadataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "email", "accountEmail", "name", "displayName", "accessToken", "stopToken", "token",
        "confirm", "stop", "invoiceId", "checkoutLink", "proof", "proofUrl", "proofFile",
        "proofThumbnail", "biomarkers", "biomarkerData", "dateOfBirth", "dob", "note",
        "privateNote", "rawValue", "value", "paymentId"
    };

    private readonly DatabaseManager _database;
    private readonly ILogger<SiteStatisticsService> _logger;
    private readonly ConcurrentQueue<QueuedSiteStatisticEvent> _queuedEvents = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private readonly object _initLock = new();
    private CancellationTokenSource? _backgroundCts;
    private Task? _backgroundTask;
    private int _queuedEventCount;
    private bool _initialized;

    public SiteStatisticsService(DatabaseManager database, ILogger<SiteStatisticsService> logger)
    {
        _database = database;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _backgroundCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _backgroundTask = Task.Run(() => FlushLoopAsync(_backgroundCts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _backgroundCts?.Cancel();

        if (_backgroundTask is not null)
        {
            try
            {
                await Task.WhenAny(_backgroundTask, Task.Delay(TimeSpan.FromSeconds(2), cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        try
        {
            await FlushQueuedEventsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Site statistics queued events were dropped during shutdown.");
        }
    }

    public Task RecordClientEventAsync(SiteStatisticsEventRequest? request, HttpContext context, CancellationToken ct = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.EventName))
            return Task.CompletedTask;

        return RecordEventAsync(request, context, actorId: null, ct);
    }

    public Task RecordServerEventAsync(
        string eventName,
        HttpContext? context = null,
        string? actorId = null,
        string? flow = null,
        string? route = null,
        string? component = null,
        string? step = null,
        string? outcome = null,
        string? errorCode = null,
        long? durationMs = null,
        string? sessionId = null,
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        var request = new SiteStatisticsEventRequest
        {
            EventName = eventName,
            SessionId = RequestStatsSessionId(context) ?? SafeSessionId(sessionId),
            Flow = flow,
            Route = route,
            Component = component,
            Step = step,
            Outcome = outcome,
            ErrorCode = errorCode,
            DurationMs = durationMs,
            Metadata = metadata is null
                ? null
                : metadata.ToDictionary(kvp => kvp.Key, kvp => JsonSerializer.SerializeToElement(kvp.Value, JsonOptions), StringComparer.OrdinalIgnoreCase)
        };

        return RecordEventAsync(request, context, actorId, ct);
    }

    public async Task<SiteStatisticsDashboardResponse> GetDashboardAsync(SiteStatisticsDashboardQuery query, CancellationToken ct = default)
    {
        EnsureInitialized();
        await FlushQueuedEventsAsync(ct).ConfigureAwait(false);

        var now = DateTimeOffset.UtcNow;
        var from = ResolveFrom(query.Range, now);
        var previousFrom = from - (now - from);
        var limit = Math.Clamp(query.Limit ?? DefaultDashboardLimit, 1, MaxDashboardLimit);

        return await _database.RunAsync(sqlite =>
        {
            var events = ReadDashboardEvents(sqlite, from, now, query, limit);
            var previousEvents = ReadDashboardEvents(sqlite, previousFrom, from, query, limit);
            var trafficSummary = ReadTrafficSummary(sqlite, from, now, previousFrom, from, query);

            var filters = new SiteStatisticsDashboardFilters(
                Range: string.IsNullOrWhiteSpace(query.Range) ? "30d" : query.Range!,
                Flow: query.Flow,
                Device: query.Device,
                Source: query.Source,
                FromUtc: from.ToString("O", CultureInfo.InvariantCulture),
                ToUtc: now.ToString("O", CultureInfo.InvariantCulture),
                PreviousFromUtc: previousFrom.ToString("O", CultureInfo.InvariantCulture),
                PreviousToUtc: from.ToString("O", CultureInfo.InvariantCulture),
                Limit: limit);

            return Task.FromResult(new SiteStatisticsDashboardResponse(
                GeneratedAtUtc: now.ToString("O", CultureInfo.InvariantCulture),
                Filters: filters,
                TrafficSummary: trafficSummary,
                Events: events,
                PreviousEvents: previousEvents));
        }, ct).ConfigureAwait(false);
    }

    private static List<SiteStatisticsDashboardEvent> ReadDashboardEvents(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query,
        int limit)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            """
            SELECT e.OccurredAtUtc, e.SessionHash, e.ActorHash, e.EventName, e.Flow, e.Route, e.Component, e.Step, e.Outcome,
                   e.ErrorCode, e.DurationMs, e.DeviceClass, e.BrowserFamily, e.ReferrerDomain, e.Source, e.MetadataJson,
                   s.LandingRoute, s.FirstReferrerDomain, s.FirstSource, s.FirstCampaign, s.FirstUtmSource,
                   s.FirstUtmMedium, s.FirstUtmCampaign, s.FirstUtmTerm, s.FirstUtmContent
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from
              AND e.OccurredAtUtc < @to
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND (@source = '' OR (
                    CASE
                        WHEN lower(coalesce(s.FirstReferrerDomain, '')) IN ('longevityworldcup.com', 'www.longevityworldcup.com') THEN 'internal'
                        WHEN lower(coalesce(s.FirstReferrerDomain, '')) = 'com.google.android.gm'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE 'mail.%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%.mail.%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%gmail%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%outlook%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%hotmail%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%protonmail%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%proton.me%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%fastmail%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%icloud%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%mail.yahoo%'
                          OR lower(coalesce(s.FirstReferrerDomain, '')) LIKE '%yahoomail%' THEN 'email'
                        WHEN s.FirstSource IS NOT NULL THEN s.FirstSource
                        WHEN lower(coalesce(e.ReferrerDomain, '')) IN ('longevityworldcup.com', 'www.longevityworldcup.com') THEN 'internal'
                        WHEN lower(coalesce(e.ReferrerDomain, '')) = 'com.google.android.gm'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE 'mail.%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%.mail.%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%gmail%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%outlook%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%hotmail%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%protonmail%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%proton.me%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%fastmail%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%icloud%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%mail.yahoo%'
                          OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%yahoomail%' THEN 'email'
                        ELSE coalesce(e.Source, 'direct')
                    END
                  ) = @source)
            ORDER BY e.OccurredAtUtc DESC
            LIMIT @limit;
            """;
        Add(cmd, "@from", from.ToString("O", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("O", CultureInfo.InvariantCulture));
        Add(cmd, "@flow", NormalizeFilter(query.Flow));
        Add(cmd, "@device", NormalizeFilter(query.Device));
        Add(cmd, "@source", NormalizeFilter(query.Source));
        Add(cmd, "@limit", limit);

        var events = new List<SiteStatisticsDashboardEvent>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var referrerDomain = ReadNullableString(reader, 13);
            var firstReferrerDomain = ReadNullableString(reader, 17);
            var routeProjection = BuildRouteProjection(ReadNullableString(reader, 5));
            var landingRouteProjection = BuildRouteProjection(ReadNullableString(reader, 16));
            var metadata = ReadMetadata(ReadNullableString(reader, 15));
            if (!string.IsNullOrWhiteSpace(routeProjection.EntryMode) && !metadata.ContainsKey("entryMode"))
            {
                metadata = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)
                {
                    ["entryMode"] = routeProjection.EntryMode
                };
            }

            events.Add(new SiteStatisticsDashboardEvent(
                OccurredAtUtc: RoundToMinute(ReadString(reader, 0)),
                SessionHash: ReadString(reader, 1),
                ActorHash: ReadNullableString(reader, 2),
                EventName: ReadString(reader, 3),
                Flow: ReadNullableString(reader, 4),
                Route: routeProjection.Route,
                Component: ReadNullableString(reader, 6),
                Step: ReadNullableString(reader, 7),
                Outcome: ReadNullableString(reader, 8),
                ErrorCode: ReadNullableString(reader, 9),
                DurationMs: reader.IsDBNull(10) ? null : reader.GetInt64(10),
                DeviceClass: ReadNullableString(reader, 11),
                BrowserFamily: ReadNullableString(reader, 12),
                ReferrerDomain: referrerDomain,
                Source: NormalizeSource(ReadNullableString(reader, 14), referrerDomain),
                LandingRoute: landingRouteProjection.Route,
                FirstReferrerDomain: firstReferrerDomain,
                FirstSource: NormalizeSource(ReadNullableString(reader, 18), firstReferrerDomain),
                FirstCampaign: ReadNullableString(reader, 19),
                FirstUtmSource: ReadNullableString(reader, 20),
                FirstUtmMedium: ReadNullableString(reader, 21),
                FirstUtmCampaign: ReadNullableString(reader, 22),
                FirstUtmTerm: ReadNullableString(reader, 23),
                FirstUtmContent: ReadNullableString(reader, 24),
                Metadata: metadata));
        }

        return events;
    }

    private static SiteStatisticsTrafficSummary ReadTrafficSummary(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset previousFrom,
        DateTimeOffset previousTo,
        SiteStatisticsDashboardQuery query)
    {
        var sessionStats = ReadTrafficSessionStats(sqlite, from, to, query);

        return new SiteStatisticsTrafficSummary(
            Totals: ReadTrafficTotals(sqlite, from, to, query),
            PreviousTotals: ReadTrafficTotals(sqlite, previousFrom, previousTo, query),
            CleanTotals: BuildCleanTrafficTotals(sessionStats),
            Quality: BuildTrafficQuality(sessionStats),
            Daily: ReadDailyTraffic(sqlite, from, to, query),
            TopPages: ReadTopPages(sqlite, from, to, query),
            Sources: ReadTrafficBreakdown(sqlite, from, to, query, EffectiveSourceSql),
            Referrers: ReadTrafficBreakdown(
                sqlite,
                from,
                to,
                query,
                "coalesce(nullif(s.FirstReferrerDomain, ''), nullif(e.ReferrerDomain, ''), 'direct')"),
            Devices: ReadTrafficBreakdown(sqlite, from, to, query, "coalesce(nullif(e.DeviceClass, ''), 'unknown')"),
            Browsers: ReadTrafficBreakdown(sqlite, from, to, query, "coalesce(nullif(e.BrowserFamily, ''), 'unknown')"));
    }

    private static SiteStatisticsTrafficTotals ReadTrafficTotals(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $$"""
            SELECT COUNT(DISTINCT e.SessionHash) AS Sessions,
                   COALESCE(SUM(CASE WHEN e.EventName IN ({{PageViewEventNamesSql}}) THEN 1 ELSE 0 END), 0) AS PageViews,
                   COUNT(*) AS Events
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from
              AND e.OccurredAtUtc < @to
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND (@source = '' OR ({{EffectiveSourceSql}}) = @source);
            """;
        AddTrafficFilterParameters(cmd, from, to, query);

        using var reader = cmd.ExecuteReader();
        return reader.Read()
            ? new SiteStatisticsTrafficTotals(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2))
            : new SiteStatisticsTrafficTotals(0, 0, 0);
    }

    private static IReadOnlyList<SiteStatisticsTrafficDailyPoint> ReadDailyTraffic(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $$"""
            SELECT substr(e.OccurredAtUtc, 1, 10) AS Day,
                   COUNT(DISTINCT e.SessionHash) AS Sessions,
                   COALESCE(SUM(CASE WHEN e.EventName IN ({{PageViewEventNamesSql}}) THEN 1 ELSE 0 END), 0) AS PageViews,
                   COUNT(*) AS Events,
                   COUNT(DISTINCT CASE WHEN e.EventName IN ({{SuccessActionEventNamesSql}}) THEN e.SessionHash END) AS SuccessSessions,
                   COALESCE(SUM(CASE WHEN e.EventName IN ({{SuccessActionEventNamesSql}}) THEN 1 ELSE 0 END), 0) AS SuccessActions
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from
              AND e.OccurredAtUtc < @to
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND (@source = '' OR ({{EffectiveSourceSql}}) = @source)
            GROUP BY Day
            ORDER BY Day ASC;
            """;
        AddTrafficFilterParameters(cmd, from, to, query);

        var points = new List<SiteStatisticsTrafficDailyPoint>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new SiteStatisticsTrafficDailyPoint(
                Day: ReadString(reader, 0),
                Sessions: reader.GetInt64(1),
                PageViews: reader.GetInt64(2),
                Events: reader.GetInt64(3),
                SuccessSessions: reader.GetInt64(4),
                SuccessActions: reader.GetInt64(5)));
        }

        return points;
    }

    private static IReadOnlyList<SiteStatisticsTrafficPage> ReadTopPages(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $$"""
            SELECT e.Route,
                   e.SessionHash,
                   COUNT(*) AS PageViews
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from
              AND e.OccurredAtUtc < @to
              AND e.EventName IN ({{PageViewEventNamesSql}})
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND (@source = '' OR ({{EffectiveSourceSql}}) = @source)
            GROUP BY e.Route, e.SessionHash;
            """;
        AddTrafficFilterParameters(cmd, from, to, query);

        var pages = new Dictionary<string, MutableTrafficPage>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var route = BuildRouteProjection(ReadNullableString(reader, 0)).Route ?? "unknown";
            if (!pages.TryGetValue(route, out var page))
            {
                page = new MutableTrafficPage();
                pages[route] = page;
            }

            page.Sessions.Add(ReadString(reader, 1));
            page.PageViews += reader.GetInt64(2);
        }

        return pages
            .Select(kvp => new SiteStatisticsTrafficPage(kvp.Key, kvp.Value.Sessions.Count, kvp.Value.PageViews))
            .OrderByDescending(page => page.PageViews)
            .ThenByDescending(page => page.Sessions)
            .ThenBy(page => page.Route, StringComparer.OrdinalIgnoreCase)
            .Take(15)
            .ToList();
    }

    private static IReadOnlyList<SiteStatisticsTrafficBreakdown> ReadTrafficBreakdown(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query,
        string labelSql)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $$"""
            SELECT {{labelSql}} AS Label,
                   COUNT(DISTINCT e.SessionHash) AS Sessions,
                   COALESCE(SUM(CASE WHEN e.EventName IN ({{PageViewEventNamesSql}}) THEN 1 ELSE 0 END), 0) AS PageViews,
                   COUNT(*) AS Events
            FROM SiteStatisticEvents e
            LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
            WHERE e.OccurredAtUtc >= @from
              AND e.OccurredAtUtc < @to
              AND (@flow = '' OR e.Flow = @flow)
              AND (@device = '' OR e.DeviceClass = @device)
              AND (@source = '' OR ({{EffectiveSourceSql}}) = @source)
            GROUP BY Label
            ORDER BY Sessions DESC, PageViews DESC, Events DESC, Label ASC
            LIMIT 12;
            """;
        AddTrafficFilterParameters(cmd, from, to, query);

        var rows = new List<SiteStatisticsTrafficBreakdown>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new SiteStatisticsTrafficBreakdown(
                Label: ReadString(reader, 0),
                Sessions: reader.GetInt64(1),
                PageViews: reader.GetInt64(2),
                Events: reader.GetInt64(3)));
        }

        return rows;
    }

    private static IReadOnlyList<TrafficSessionAggregate> ReadTrafficSessionStats(
        SqliteConnection sqlite,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText =
            $$"""
            WITH filtered AS (
                SELECT e.SessionHash,
                       e.EventName,
                       coalesce(nullif(e.Route, ''), 'unknown') AS Route
                FROM SiteStatisticEvents e
                LEFT JOIN SiteStatisticSessions s ON s.SessionHash = e.SessionHash
                WHERE e.OccurredAtUtc >= @from
                  AND e.OccurredAtUtc < @to
                  AND (@flow = '' OR e.Flow = @flow)
                  AND (@device = '' OR e.DeviceClass = @device)
                  AND (@source = '' OR ({{EffectiveSourceSql}}) = @source)
            ),
            session_totals AS (
                SELECT SessionHash,
                       COUNT(*) AS Events,
                       COALESCE(SUM(CASE WHEN EventName IN ({{PageViewEventNamesSql}}) THEN 1 ELSE 0 END), 0) AS PageViews
                FROM filtered
                GROUP BY SessionHash
            ),
            route_pageviews AS (
                SELECT SessionHash,
                       Route,
                       COUNT(*) AS PageViews
                FROM filtered
                WHERE EventName IN ({{PageViewEventNamesSql}})
                GROUP BY SessionHash, Route
            ),
            session_routes AS (
                SELECT SessionHash,
                       COALESCE(MAX(PageViews), 0) AS LargestRepeatedPageViews
                FROM route_pageviews
                GROUP BY SessionHash
            )
            SELECT t.SessionHash,
                   t.Events,
                   t.PageViews,
                   COALESCE(r.LargestRepeatedPageViews, 0) AS LargestRepeatedPageViews
            FROM session_totals t
            LEFT JOIN session_routes r ON r.SessionHash = t.SessionHash;
            """;
        AddTrafficFilterParameters(cmd, from, to, query);

        var rows = new List<TrafficSessionAggregate>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new TrafficSessionAggregate(
                SessionHash: ReadString(reader, 0),
                Events: reader.GetInt64(1),
                PageViews: reader.GetInt64(2),
                LargestRepeatedPageViews: reader.GetInt64(3)));
        }

        return rows;
    }

    private static SiteStatisticsTrafficTotals BuildCleanTrafficTotals(IReadOnlyList<TrafficSessionAggregate> sessionStats)
    {
        var clean = sessionStats.Where(static row => !IsNoisyTrafficSession(row)).ToList();
        return new SiteStatisticsTrafficTotals(
            Sessions: clean.Count,
            PageViews: clean.Sum(static row => row.PageViews),
            Events: clean.Sum(static row => row.Events));
    }

    private static SiteStatisticsTrafficQuality BuildTrafficQuality(IReadOnlyList<TrafficSessionAggregate> sessionStats)
    {
        var totalEvents = sessionStats.Sum(static row => row.Events);
        var totalPageViews = sessionStats.Sum(static row => row.PageViews);
        var topSessionEvents = sessionStats.Count == 0 ? 0 : sessionStats.Max(static row => row.Events);
        var noisy = sessionStats.Where(static row => IsNoisyTrafficSession(row)).ToList();
        var repeatedPageViewSessions = sessionStats.Count(static row => row.LargestRepeatedPageViews >= 20);
        var pageViewDominantSessions = sessionStats.Count(static row =>
            row.PageViews >= 20 && row.Events > 0 && row.PageViews / (double)row.Events >= 0.6);
        var noisyPageViews = noisy.Sum(static row => row.PageViews);

        return new SiteStatisticsTrafficQuality(
            RawSessions: sessionStats.Count,
            CleanSessions: Math.Max(0, sessionStats.Count - noisy.Count),
            NoisySessions: noisy.Count,
            TopSessionEvents: topSessionEvents,
            TopSessionShare: totalEvents == 0 ? 0 : topSessionEvents / (double)totalEvents,
            RepeatedPageViewSessions: repeatedPageViewSessions,
            PageViewDominantSessions: pageViewDominantSessions,
            NoisyPageViews: noisyPageViews,
            NoisyPageViewShare: totalPageViews == 0 ? 0 : noisyPageViews / (double)totalPageViews);
    }

    private static bool IsNoisyTrafficSession(TrafficSessionAggregate row)
    {
        if (row.Events < 20)
            return false;

        return row.LargestRepeatedPageViews >= 20 ||
               (row.Events >= 40 && row.Events > 0 && row.LargestRepeatedPageViews / (double)row.Events >= 0.6) ||
               (row.PageViews >= 20 && row.Events > 0 && row.PageViews / (double)row.Events >= 0.6);
    }

    private async Task RecordEventAsync(SiteStatisticsEventRequest request, HttpContext? context, string? actorId, CancellationToken ct)
    {
        try
        {
            var queued = BuildQueuedEvent(request, context, actorId);
            if (queued is null)
                return;

            if (Interlocked.Increment(ref _queuedEventCount) > MaxQueuedEvents)
            {
                Interlocked.Decrement(ref _queuedEventCount);
                _logger.LogDebug("Site statistics event was dropped because the queue is full.");
                return;
            }

            _queuedEvents.Enqueue(queued);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Site statistics event was dropped.");
        }
    }

    private QueuedSiteStatisticEvent? BuildQueuedEvent(SiteStatisticsEventRequest request, HttpContext? context, string? actorId)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var eventName = SafeToken(request.EventName, MaxEventNameLength);
        if (string.IsNullOrWhiteSpace(eventName) || LooksSensitiveValue(eventName))
            return null;

        var rawRoute = request.Route ?? ContextRoute(context) ?? string.Empty;
        var routeProjection = BuildRouteProjection(rawRoute);
        var route = routeProjection.Route;
        var metadata = SanitizeMetadata(request.Metadata);
        if (!string.IsNullOrWhiteSpace(routeProjection.EntryMode))
        {
            metadata["entryMode"] = routeProjection.EntryMode;
        }

        var metadataJson = metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata, JsonOptions);
        if (metadataJson is { Length: > MaxMetadataJsonLength })
        {
            metadataJson = metadataJson[..MaxMetadataJsonLength];
        }

        var referrerDomain = SafeDomain(request.ReferrerDomain) ?? SafeDomain(context?.Request.Headers.Referer.FirstOrDefault());
        var source = NormalizeSource(request.Source, referrerDomain);
        var sessionIdentifier = SafeSessionId(request.SessionId) ?? ClientIdentifier.From(context ?? new DefaultHttpContext());
        var sessionHash = BuildHash("S", sessionIdentifier);
        var firstTouch = BuildFirstTouch(request, rawRoute, route, referrerDomain, source);
        var hasExplicitFirstTouch = HasExplicitFirstTouch(request);

        return new QueuedSiteStatisticEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: occurredAt.ToString("O", CultureInfo.InvariantCulture),
            SessionHash: sessionHash,
            ActorHash: string.IsNullOrWhiteSpace(actorId) ? null : BuildHash("A", actorId),
            EventName: eventName,
            Flow: SafeDisplayToken(request.Flow, MaxTextLength),
            Route: route,
            Component: SafeDisplayToken(request.Component, MaxTextLength),
            Step: SafeDisplayToken(request.Step, MaxTextLength),
            Outcome: SafeDisplayToken(request.Outcome, MaxTextLength),
            ErrorCode: SafeDisplayToken(request.ErrorCode, MaxTextLength),
            DurationMs: request.DurationMs is >= 0 and < 86_400_000 ? request.DurationMs.Value : null,
            DeviceClass: SafeDisplayToken(request.DeviceClass, MaxTextLength) ?? DetectDeviceClass(context),
            BrowserFamily: SafeDisplayToken(request.BrowserFamily, MaxTextLength) ?? DetectBrowserFamily(context),
            ReferrerDomain: referrerDomain,
            Source: source,
            LandingRoute: firstTouch.LandingRoute,
            FirstReferrerDomain: firstTouch.FirstReferrerDomain,
            FirstSource: firstTouch.FirstSource,
            FirstCampaign: firstTouch.FirstCampaign,
            FirstUtmSource: firstTouch.FirstUtmSource,
            FirstUtmMedium: firstTouch.FirstUtmMedium,
            FirstUtmCampaign: firstTouch.FirstUtmCampaign,
            FirstUtmTerm: firstTouch.FirstUtmTerm,
            FirstUtmContent: firstTouch.FirstUtmContent,
            HasExplicitFirstTouch: hasExplicitFirstTouch,
            MetadataJson: metadataJson);
    }

    private async Task FlushLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FlushInterval, ct).ConfigureAwait(false);
                await FlushQueuedEventsAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Site statistics queued events were dropped.");
            }
        }
    }

    private async Task FlushQueuedEventsAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _queuedEventCount) <= 0)
            return;

        await _flushGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _queuedEventCount) <= 0)
                return;

            EnsureInitialized();
            var batch = new List<QueuedSiteStatisticEvent>(FlushBatchSize);
            while (_queuedEvents.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _queuedEventCount);
                batch.Add(item);
                if (batch.Count >= FlushBatchSize)
                {
                    await WriteQueuedEventsAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await WriteQueuedEventsAsync(batch, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _flushGate.Release();
        }
    }

    private Task WriteQueuedEventsAsync(IReadOnlyList<QueuedSiteStatisticEvent> events, CancellationToken ct)
    {
        if (events.Count == 0)
            return Task.CompletedTask;

        return _database.RunAsync(sqlite =>
        {
            using var tx = sqlite.BeginTransaction();
            using var sessionCmd = sqlite.CreateCommand();
            sessionCmd.Transaction = tx;
            sessionCmd.CommandText =
                """
                INSERT INTO SiteStatisticSessions
                (SessionHash, FirstSeenAtUtc, LandingRoute, FirstReferrerDomain, FirstSource, FirstCampaign,
                 FirstUtmSource, FirstUtmMedium, FirstUtmCampaign, FirstUtmTerm, FirstUtmContent)
                VALUES
                (@sessionHash, @firstSeen, @landingRoute, @firstReferrer, @firstSource, @firstCampaign,
                 @firstUtmSource, @firstUtmMedium, @firstUtmCampaign, @firstUtmTerm, @firstUtmContent)
                ON CONFLICT(SessionHash) DO UPDATE SET
                    FirstSeenAtUtc = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc THEN excluded.FirstSeenAtUtc
                        ELSE SiteStatisticSessions.FirstSeenAtUtc
                    END,
                    LandingRoute = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.LandingRoute IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.LandingRoute, SiteStatisticSessions.LandingRoute)
                        ELSE SiteStatisticSessions.LandingRoute
                    END,
                    FirstReferrerDomain = CASE
                        WHEN @hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal') THEN excluded.FirstReferrerDomain
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc OR SiteStatisticSessions.FirstReferrerDomain IS NULL THEN COALESCE(excluded.FirstReferrerDomain, SiteStatisticSessions.FirstReferrerDomain)
                        ELSE SiteStatisticSessions.FirstReferrerDomain
                    END,
                    FirstSource = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstSource IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstSource, SiteStatisticSessions.FirstSource)
                        ELSE SiteStatisticSessions.FirstSource
                    END,
                    FirstCampaign = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstCampaign IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstCampaign, SiteStatisticSessions.FirstCampaign)
                        ELSE SiteStatisticSessions.FirstCampaign
                    END,
                    FirstUtmSource = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstUtmSource IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstUtmSource, SiteStatisticSessions.FirstUtmSource)
                        ELSE SiteStatisticSessions.FirstUtmSource
                    END,
                    FirstUtmMedium = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstUtmMedium IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstUtmMedium, SiteStatisticSessions.FirstUtmMedium)
                        ELSE SiteStatisticSessions.FirstUtmMedium
                    END,
                    FirstUtmCampaign = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstUtmCampaign IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstUtmCampaign, SiteStatisticSessions.FirstUtmCampaign)
                        ELSE SiteStatisticSessions.FirstUtmCampaign
                    END,
                    FirstUtmTerm = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstUtmTerm IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstUtmTerm, SiteStatisticSessions.FirstUtmTerm)
                        ELSE SiteStatisticSessions.FirstUtmTerm
                    END,
                    FirstUtmContent = CASE
                        WHEN excluded.FirstSeenAtUtc < SiteStatisticSessions.FirstSeenAtUtc
                          OR SiteStatisticSessions.FirstUtmContent IS NULL
                          OR (@hasExplicitFirstTouch = 1 AND lower(coalesce(SiteStatisticSessions.FirstSource, 'direct')) IN ('direct', 'internal')) THEN COALESCE(excluded.FirstUtmContent, SiteStatisticSessions.FirstUtmContent)
                        ELSE SiteStatisticSessions.FirstUtmContent
                    END;
                """;
            var sessionHashParam = sessionCmd.Parameters.Add("@sessionHash", SqliteType.Text);
            var firstSeenParam = sessionCmd.Parameters.Add("@firstSeen", SqliteType.Text);
            var landingRouteParam = sessionCmd.Parameters.Add("@landingRoute", SqliteType.Text);
            var firstReferrerParam = sessionCmd.Parameters.Add("@firstReferrer", SqliteType.Text);
            var firstSourceParam = sessionCmd.Parameters.Add("@firstSource", SqliteType.Text);
            var firstCampaignParam = sessionCmd.Parameters.Add("@firstCampaign", SqliteType.Text);
            var firstUtmSourceParam = sessionCmd.Parameters.Add("@firstUtmSource", SqliteType.Text);
            var firstUtmMediumParam = sessionCmd.Parameters.Add("@firstUtmMedium", SqliteType.Text);
            var firstUtmCampaignParam = sessionCmd.Parameters.Add("@firstUtmCampaign", SqliteType.Text);
            var firstUtmTermParam = sessionCmd.Parameters.Add("@firstUtmTerm", SqliteType.Text);
            var firstUtmContentParam = sessionCmd.Parameters.Add("@firstUtmContent", SqliteType.Text);
            var hasExplicitFirstTouchParam = sessionCmd.Parameters.Add("@hasExplicitFirstTouch", SqliteType.Integer);

            using var cmd = sqlite.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                """
                INSERT INTO SiteStatisticEvents
                (Id, OccurredAtUtc, SessionHash, ActorHash, EventName, Flow, Route, Component, Step, Outcome,
                 ErrorCode, DurationMs, DeviceClass, BrowserFamily, ReferrerDomain, Source, MetadataJson)
                VALUES
                (@id, @occurred, @session, @actor, @eventName, @flow, @route, @component, @step, @outcome,
                 @errorCode, @duration, @device, @browser, @referrer, @source, @metadata);
                """;

            var id = cmd.Parameters.Add("@id", SqliteType.Text);
            var occurred = cmd.Parameters.Add("@occurred", SqliteType.Text);
            var session = cmd.Parameters.Add("@session", SqliteType.Text);
            var actor = cmd.Parameters.Add("@actor", SqliteType.Text);
            var eventName = cmd.Parameters.Add("@eventName", SqliteType.Text);
            var flow = cmd.Parameters.Add("@flow", SqliteType.Text);
            var route = cmd.Parameters.Add("@route", SqliteType.Text);
            var component = cmd.Parameters.Add("@component", SqliteType.Text);
            var step = cmd.Parameters.Add("@step", SqliteType.Text);
            var outcome = cmd.Parameters.Add("@outcome", SqliteType.Text);
            var errorCode = cmd.Parameters.Add("@errorCode", SqliteType.Text);
            var duration = cmd.Parameters.Add("@duration", SqliteType.Integer);
            var device = cmd.Parameters.Add("@device", SqliteType.Text);
            var browser = cmd.Parameters.Add("@browser", SqliteType.Text);
            var referrer = cmd.Parameters.Add("@referrer", SqliteType.Text);
            var source = cmd.Parameters.Add("@source", SqliteType.Text);
            var metadata = cmd.Parameters.Add("@metadata", SqliteType.Text);

            foreach (var item in events)
            {
                sessionHashParam.Value = item.SessionHash;
                firstSeenParam.Value = item.OccurredAtUtc;
                landingRouteParam.Value = item.LandingRoute ?? (object)DBNull.Value;
                firstReferrerParam.Value = item.FirstReferrerDomain ?? (object)DBNull.Value;
                firstSourceParam.Value = item.FirstSource ?? (object)DBNull.Value;
                firstCampaignParam.Value = item.FirstCampaign ?? (object)DBNull.Value;
                firstUtmSourceParam.Value = item.FirstUtmSource ?? (object)DBNull.Value;
                firstUtmMediumParam.Value = item.FirstUtmMedium ?? (object)DBNull.Value;
                firstUtmCampaignParam.Value = item.FirstUtmCampaign ?? (object)DBNull.Value;
                firstUtmTermParam.Value = item.FirstUtmTerm ?? (object)DBNull.Value;
                firstUtmContentParam.Value = item.FirstUtmContent ?? (object)DBNull.Value;
                hasExplicitFirstTouchParam.Value = item.HasExplicitFirstTouch ? 1 : 0;
                sessionCmd.ExecuteNonQuery();

                id.Value = item.Id;
                occurred.Value = item.OccurredAtUtc;
                session.Value = item.SessionHash;
                actor.Value = item.ActorHash ?? (object)DBNull.Value;
                eventName.Value = item.EventName;
                flow.Value = item.Flow ?? (object)DBNull.Value;
                route.Value = item.Route ?? (object)DBNull.Value;
                component.Value = item.Component ?? (object)DBNull.Value;
                step.Value = item.Step ?? (object)DBNull.Value;
                outcome.Value = item.Outcome ?? (object)DBNull.Value;
                errorCode.Value = item.ErrorCode ?? (object)DBNull.Value;
                duration.Value = item.DurationMs ?? (object)DBNull.Value;
                device.Value = item.DeviceClass ?? (object)DBNull.Value;
                browser.Value = item.BrowserFamily ?? (object)DBNull.Value;
                referrer.Value = item.ReferrerDomain ?? (object)DBNull.Value;
                source.Value = item.Source ?? (object)DBNull.Value;
                metadata.Value = item.MetadataJson ?? (object)DBNull.Value;
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
            return Task.CompletedTask;
        }, ct);
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_initLock)
        {
            if (_initialized)
                return;

            _database.Run(sqlite =>
            {
                using var cmd = sqlite.CreateCommand();
                cmd.CommandText =
                    """
                    CREATE TABLE IF NOT EXISTS SiteStatisticEvents (
                        Id TEXT PRIMARY KEY,
                        OccurredAtUtc TEXT NOT NULL,
                        SessionHash TEXT NOT NULL,
                        ActorHash TEXT NULL,
                        EventName TEXT NOT NULL,
                        Flow TEXT NULL,
                        Route TEXT NULL,
                        Component TEXT NULL,
                        Step TEXT NULL,
                        Outcome TEXT NULL,
                        ErrorCode TEXT NULL,
                        DurationMs INTEGER NULL,
                        DeviceClass TEXT NULL,
                        BrowserFamily TEXT NULL,
                        ReferrerDomain TEXT NULL,
                        Source TEXT NULL,
                        MetadataJson TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticEvents_OccurredAtUtc ON SiteStatisticEvents(OccurredAtUtc);
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticEvents_Flow ON SiteStatisticEvents(Flow);
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticEvents_EventName ON SiteStatisticEvents(EventName);
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticEvents_SessionHash ON SiteStatisticEvents(SessionHash);

                    CREATE TABLE IF NOT EXISTS SiteStatisticSessions (
                        SessionHash TEXT PRIMARY KEY,
                        FirstSeenAtUtc TEXT NOT NULL,
                        LandingRoute TEXT NULL,
                        FirstReferrerDomain TEXT NULL,
                        FirstSource TEXT NULL,
                        FirstCampaign TEXT NULL,
                        FirstUtmSource TEXT NULL,
                        FirstUtmMedium TEXT NULL,
                        FirstUtmCampaign TEXT NULL,
                        FirstUtmTerm TEXT NULL,
                        FirstUtmContent TEXT NULL
                    );
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticSessions_FirstSeenAtUtc ON SiteStatisticSessions(FirstSeenAtUtc);
                    CREATE INDEX IF NOT EXISTS IX_SiteStatisticSessions_FirstSource ON SiteStatisticSessions(FirstSource);

                    INSERT OR IGNORE INTO SiteStatisticSessions
                    (SessionHash, FirstSeenAtUtc, LandingRoute, FirstReferrerDomain, FirstSource)
                    SELECT e.SessionHash,
                           e.OccurredAtUtc,
                           e.Route,
                           e.ReferrerDomain,
                           CASE
                               WHEN lower(coalesce(e.ReferrerDomain, '')) IN ('longevityworldcup.com', 'www.longevityworldcup.com') THEN 'internal'
                               WHEN lower(coalesce(e.ReferrerDomain, '')) = 'com.google.android.gm'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE 'mail.%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%.mail.%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%gmail%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%outlook%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%hotmail%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%protonmail%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%proton.me%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%fastmail%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%icloud%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%mail.yahoo%'
                                 OR lower(coalesce(e.ReferrerDomain, '')) LIKE '%yahoomail%' THEN 'email'
                               ELSE coalesce(e.Source, 'direct')
                           END
                    FROM SiteStatisticEvents e
                    INNER JOIN (
                        SELECT SessionHash, MIN(OccurredAtUtc) AS FirstSeenAtUtc
                        FROM SiteStatisticEvents
                        GROUP BY SessionHash
                    ) first_events
                      ON first_events.SessionHash = e.SessionHash
                     AND first_events.FirstSeenAtUtc = e.OccurredAtUtc;
                    """;
                cmd.ExecuteNonQuery();
            });

            _initialized = true;
        }
    }

    private static SessionFirstTouch BuildFirstTouch(
        SiteStatisticsEventRequest request,
        string rawRoute,
        string? sanitizedRoute,
        string? referrerDomain,
        string source)
    {
        var landingRouteRaw = string.IsNullOrWhiteSpace(request.LandingRoute)
            ? rawRoute
            : request.LandingRoute!;
        var landingRoute = BuildRouteProjection(landingRouteRaw).Route ?? sanitizedRoute;
        var firstReferrerDomain = SafeDomain(request.FirstReferrerDomain) ?? referrerDomain;
        var firstUtmSource = SafeCampaignValue(request.FirstUtmSource) ?? QueryValue(landingRouteRaw, "utm_source");
        var firstUtmMedium = SafeCampaignValue(request.FirstUtmMedium) ?? QueryValue(landingRouteRaw, "utm_medium");
        var firstUtmCampaign = SafeCampaignValue(request.FirstUtmCampaign) ?? QueryValue(landingRouteRaw, "utm_campaign");
        var firstUtmTerm = SafeCampaignValue(request.FirstUtmTerm) ?? QueryValue(landingRouteRaw, "utm_term");
        var firstUtmContent = SafeCampaignValue(request.FirstUtmContent) ?? QueryValue(landingRouteRaw, "utm_content");
        var firstCampaign = SafeCampaignValue(request.FirstCampaign)
            ?? QueryValue(landingRouteRaw, "campaign")
            ?? firstUtmCampaign;
        var hasCampaign = !string.IsNullOrWhiteSpace(firstCampaign)
            || !string.IsNullOrWhiteSpace(firstUtmSource)
            || !string.IsNullOrWhiteSpace(firstUtmMedium)
            || !string.IsNullOrWhiteSpace(firstUtmCampaign)
            || !string.IsNullOrWhiteSpace(firstUtmTerm)
            || !string.IsNullOrWhiteSpace(firstUtmContent);
        var firstSource = NormalizeSource(
            string.IsNullOrWhiteSpace(request.FirstSource) && hasCampaign && string.IsNullOrWhiteSpace(firstReferrerDomain)
                ? "campaign"
                : request.FirstSource ?? source,
            firstReferrerDomain);

        return new SessionFirstTouch(
            landingRoute,
            firstReferrerDomain,
            firstSource,
            firstCampaign,
            firstUtmSource,
            firstUtmMedium,
            firstUtmCampaign,
            firstUtmTerm,
            firstUtmContent);
    }

    private static bool HasExplicitFirstTouch(SiteStatisticsEventRequest request)
        => !string.IsNullOrWhiteSpace(request.LandingRoute) ||
           !string.IsNullOrWhiteSpace(request.FirstReferrerDomain) ||
           !string.IsNullOrWhiteSpace(request.FirstSource) ||
           !string.IsNullOrWhiteSpace(request.FirstCampaign) ||
           !string.IsNullOrWhiteSpace(request.FirstUtmSource) ||
           !string.IsNullOrWhiteSpace(request.FirstUtmMedium) ||
           !string.IsNullOrWhiteSpace(request.FirstUtmCampaign) ||
           !string.IsNullOrWhiteSpace(request.FirstUtmTerm) ||
           !string.IsNullOrWhiteSpace(request.FirstUtmContent);

    private static string? ContextRoute(HttpContext? context)
    {
        if (context is null)
            return null;

        return $"{context.Request.Path.Value}{context.Request.QueryString.Value}";
    }

    private static string? QueryValue(string? route, string key)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        var queryStart = route.IndexOf('?', StringComparison.Ordinal);
        if (queryStart < 0 || queryStart == route.Length - 1)
            return null;

        var query = route[(queryStart + 1)..];
        try
        {
            var parsed = QueryHelpers.ParseQuery(query);
            return parsed.TryGetValue(key, out var values)
                ? SafeCampaignValue(values.FirstOrDefault())
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static RouteProjection BuildRouteProjection(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return new RouteProjection(null, null);

        if (!Uri.TryCreate(route, UriKind.RelativeOrAbsolute, out var uri))
            return new RouteProjection(SafeToken(route.Split('?')[0], MaxTextLength), null);

        var rawPath = uri.IsAbsoluteUri ? uri.AbsolutePath : route.Split('?')[0];
        var path = SafeToken(CanonicalizeBioagePath(rawPath), MaxTextLength);
        if (string.IsNullOrWhiteSpace(path))
            return new RouteProjection(null, null);

        if (!route.Contains('?'))
            return new RouteProjection(path, null);

        var query = route[(route.IndexOf('?') + 1)..];
        if (IsBioageCalculatorPath(path))
            return new RouteProjection(path, ResolveBioageEntryMode(query));

        var pairs = new List<string>();
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var key = part.Split('=')[0];
            if (SensitiveQueryKeys.Any(s => string.Equals(s, key, StringComparison.OrdinalIgnoreCase)))
                continue;
            var safeKey = SafeToken(key, 32);
            if (!string.IsNullOrWhiteSpace(safeKey))
                pairs.Add($"{safeKey}=redacted");
        }

        return new RouteProjection(pairs.Count == 0 ? path : $"{path}?{string.Join("&", pairs)}", null);
    }

    private static string CanonicalizeBioagePath(string path)
    {
        var normalized = path.TrimEnd('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return "/";

        return normalized.ToLowerInvariant() switch
        {
            "/pheno-age" => "/pheno-age",
            "/bortz-age" => "/bortz-age",
            "/onboarding/pheno-age.html" => "/pheno-age",
            "/onboarding/bortz-age.html" => "/bortz-age",
            _ => path
        };
    }

    private static bool IsBioageCalculatorPath(string? path)
        => !string.IsNullOrWhiteSpace(path) && BioageCalculatorPaths.Contains(path);

    private static string? ResolveBioageEntryMode(string query)
    {
        try
        {
            var parsed = QueryHelpers.ParseQuery(query);
            if (IsQueryFlagSet(parsed, "fake"))
                return "fake";

            if (IsQueryFlagSet(parsed, "update"))
                return "update";

            if (HasQueryKey(parsed, "discount") || HasQueryKey(parsed, "freepass"))
                return "discount";

            return parsed.Keys.Any(key => BioagePrefillQueryKeys.Contains(key)) ? "prefilled" : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsQueryFlagSet(IReadOnlyDictionary<string, Microsoft.Extensions.Primitives.StringValues> parsed, string key)
    {
        if (!parsed.TryGetValue(key, out var values))
            return false;

        if (values.Count == 0)
            return true;

        return values.Any(value =>
            string.IsNullOrWhiteSpace(value) ||
            (!string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool HasQueryKey(IReadOnlyDictionary<string, Microsoft.Extensions.Primitives.StringValues> parsed, string key)
        => parsed.TryGetValue(key, out var values) && values.Any(value => !string.IsNullOrWhiteSpace(value));

    private static Dictionary<string, string> SanitizeMetadata(IReadOnlyDictionary<string, JsonElement>? metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is null)
            return result;

        foreach (var (rawKey, rawValue) in metadata)
        {
            var key = SafeToken(rawKey, 64);
            if (string.IsNullOrWhiteSpace(key) || IsSensitiveMetadataKey(key))
                continue;

            var value = rawValue.ValueKind switch
            {
                JsonValueKind.String => rawValue.GetString(),
                JsonValueKind.Number => rawValue.TryGetInt64(out var l)
                    ? l.ToString(CultureInfo.InvariantCulture)
                    : rawValue.TryGetDouble(out var d) ? d.ToString("0.###", CultureInfo.InvariantCulture) : null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            value = SafeMetadataValue(value);
            if (!string.IsNullOrWhiteSpace(value))
                result[key] = value;
        }

        return result;
    }

    private static string? SafeMetadataValue(string? value)
    {
        if (LooksSensitiveValue(value))
            return null;

        value = SafeToken(value, MaxTextLength);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (value.Contains('@') ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            LooksSensitiveValue(value) ||
            value.Length > 120 && value.Count(static c => c is '-' or '_' or '.') < 3)
            return null;

        return value;
    }

    private static IReadOnlyDictionary<string, string> ReadMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>();

        try
        {
            var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                ?? new Dictionary<string, string>();
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rawKey, rawValue) in raw)
            {
                var key = SafeToken(rawKey, 64);
                if (string.IsNullOrWhiteSpace(key) || IsSensitiveMetadataKey(key))
                    continue;

                var value = SafeMetadataValue(rawValue);
                if (!string.IsNullOrWhiteSpace(value))
                    result[key] = value;
            }

            return result;
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static bool IsSensitiveMetadataKey(string key)
        => SensitiveMetadataKeys.Contains(key) ||
           key.EndsWith("Email", StringComparison.OrdinalIgnoreCase) ||
           key.EndsWith("Token", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("Invoice", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("Checkout", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("Biomarker", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("Birth", StringComparison.OrdinalIgnoreCase) ||
           key.Contains("ProofUrl", StringComparison.OrdinalIgnoreCase);

    private static string? SafeDisplayToken(string? value, int maxLength)
    {
        var token = SafeToken(value, maxLength);
        return LooksSensitiveValue(token) ? null : token;
    }

    private static string? RequestStatsSessionId(HttpContext? context)
        => SafeSessionId(context?.Request.Headers[StatsSessionHeaderName].FirstOrDefault());

    private static string? SafeSessionId(string? value)
        => SafeToken(value, MaxSessionIdLength);

    private static string? SafeCampaignValue(string? value)
    {
        var token = SafeToken(value, MaxCampaignTextLength);
        return LooksSensitiveValue(token) ? null : token;
    }

    private static bool LooksSensitiveValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        var lower = trimmed.ToLowerInvariant();
        if (trimmed.Contains('@') ||
            lower.StartsWith("http://", StringComparison.Ordinal) ||
            lower.StartsWith("https://", StringComparison.Ordinal) ||
            lower.StartsWith("data:", StringComparison.Ordinal) ||
            lower.Contains("secret", StringComparison.Ordinal) ||
            lower.Contains("bearer", StringComparison.Ordinal) ||
            lower.Contains("password", StringComparison.Ordinal) ||
            lower.Contains("access-token", StringComparison.Ordinal) ||
            lower.Contains("stop-token", StringComparison.Ordinal) ||
            lower.Contains("private-token", StringComparison.Ordinal) ||
            lower.Contains("invoice-", StringComparison.Ordinal) ||
            lower.Contains("tok_", StringComparison.Ordinal) ||
            lower.Contains("sk_", StringComparison.Ordinal))
            return true;

        if (lower.Contains("token", StringComparison.Ordinal) && trimmed.Length > 16)
            return true;

        if (trimmed.Length > 80 && trimmed.Count(static c => c == '.') >= 2)
            return true;

        return trimmed.Length >= 48 &&
               !trimmed.Any(char.IsWhiteSpace) &&
               trimmed.Any(char.IsLetter) &&
               trimmed.Any(char.IsDigit);
    }

    private static string? SafeToken(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var cleaned = value.Trim();
        if (cleaned.Length > maxLength)
            cleaned = cleaned[..maxLength];

        var sb = new StringBuilder(cleaned.Length);
        foreach (var c in cleaned)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or '/' or ':' or ' ' or '$')
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    private static string BuildHash(string prefix, string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"site-stats-v1:{value}"));
        return $"{prefix}-{Convert.ToHexString(bytes, 0, 4)}";
    }

    private static string? SafeDomain(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return SafeToken(uri.Host.ToLowerInvariant(), 96);

        var cleaned = value.Trim().ToLowerInvariant();
        if (cleaned.Contains('/') || cleaned.Contains('@'))
            return null;

        return SafeToken(cleaned, 96);
    }

    private static string NormalizeSource(string? source, string? referrerDomain)
    {
        if (IsInternalReferrerDomain(referrerDomain))
            return "internal";

        if (IsEmailReferrerDomain(referrerDomain))
            return "email";

        return SafeDisplayToken(source, MaxTextLength)
            ?? ClassifyExternalReferrer(referrerDomain)
            ?? "direct";
    }

    private static string? ClassifyExternalReferrer(string? referrerDomain)
    {
        if (string.IsNullOrWhiteSpace(referrerDomain))
            return null;

        var normalized = referrerDomain.Trim().ToLowerInvariant();
        if (IsEmailReferrerDomain(normalized))
            return "email";

        if (normalized.Contains("google", StringComparison.Ordinal) ||
            normalized.Contains("bing", StringComparison.Ordinal) ||
            normalized.Contains("duckduckgo", StringComparison.Ordinal) ||
            normalized.Contains("yahoo", StringComparison.Ordinal) ||
            normalized.Contains("brave", StringComparison.Ordinal) ||
            normalized.Contains("search", StringComparison.Ordinal))
        {
            return "search";
        }

        if (normalized.Contains("x.com", StringComparison.Ordinal) ||
            normalized.Contains("twitter", StringComparison.Ordinal) ||
            normalized.Contains("facebook", StringComparison.Ordinal) ||
            normalized.Contains("instagram", StringComparison.Ordinal) ||
            normalized.Contains("threads", StringComparison.Ordinal) ||
            normalized.Contains("youtube", StringComparison.Ordinal) ||
            normalized.Contains("linkedin", StringComparison.Ordinal) ||
            normalized.Contains("reddit", StringComparison.Ordinal) ||
            normalized.Contains("slack", StringComparison.Ordinal))
        {
            return "social";
        }

        return "referral";
    }

    private static bool IsEmailReferrerDomain(string? referrerDomain)
    {
        if (string.IsNullOrWhiteSpace(referrerDomain))
            return false;

        var normalized = referrerDomain.Trim().ToLowerInvariant();
        return string.Equals(normalized, "com.google.android.gm", StringComparison.Ordinal) ||
               normalized.StartsWith("mail.", StringComparison.Ordinal) ||
               normalized.Contains(".mail.", StringComparison.Ordinal) ||
               normalized.Contains("gmail", StringComparison.Ordinal) ||
               normalized.Contains("outlook", StringComparison.Ordinal) ||
               normalized.Contains("hotmail", StringComparison.Ordinal) ||
               normalized.Contains("protonmail", StringComparison.Ordinal) ||
               normalized.Contains("proton.me", StringComparison.Ordinal) ||
               normalized.Contains("fastmail", StringComparison.Ordinal) ||
               normalized.Contains("icloud", StringComparison.Ordinal) ||
               normalized.Contains("mail.yahoo", StringComparison.Ordinal) ||
               normalized.Contains("yahoomail", StringComparison.Ordinal);
    }

    private static bool IsInternalReferrerDomain(string? referrerDomain)
    {
        if (string.IsNullOrWhiteSpace(referrerDomain))
            return false;

        var normalized = referrerDomain.Trim().ToLowerInvariant();
        if (normalized.StartsWith("www.", StringComparison.Ordinal))
            normalized = normalized[4..];

        return InternalReferrerDomains.Contains(referrerDomain) || string.Equals(normalized, "longevityworldcup.com", StringComparison.Ordinal);
    }

    private static string? DetectDeviceClass(HttpContext? context)
    {
        var ua = context?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        if (ua.Contains("Mobi", StringComparison.OrdinalIgnoreCase)) return "mobile";
        if (ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase) || ua.Contains("iPad", StringComparison.OrdinalIgnoreCase)) return "tablet";
        return string.IsNullOrWhiteSpace(ua) ? "unknown" : "desktop";
    }

    private static string? DetectBrowserFamily(HttpContext? context)
    {
        var ua = context?.Request.Headers.UserAgent.ToString() ?? string.Empty;
        if (ua.Contains("Edg/", StringComparison.OrdinalIgnoreCase)) return "Edge";
        if (ua.Contains("Chrome/", StringComparison.OrdinalIgnoreCase)) return "Chrome";
        if (ua.Contains("Firefox/", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (ua.Contains("Safari/", StringComparison.OrdinalIgnoreCase)) return "Safari";
        return string.IsNullOrWhiteSpace(ua) ? "unknown" : "other";
    }

    private static DateTimeOffset ResolveFrom(string? range, DateTimeOffset now)
        => (range ?? "30d").Trim().ToLowerInvariant() switch
        {
            "today" => new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero),
            "7d" => now.AddDays(-7),
            "90d" => now.AddDays(-90),
            "season" => new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, TimeSpan.Zero),
            "30d" or _ => now.Subtract(DefaultDashboardRange)
        };

    private static string NormalizeFilter(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : value.Trim();

    private static string RoundToMinute(string value)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto)
            ? new DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, 0, TimeSpan.Zero).ToString("O", CultureInfo.InvariantCulture)
            : value;

    private static string ReadString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static void Add(SqliteCommand cmd, string name, object? value)
        => cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static void AddTrafficFilterParameters(
        SqliteCommand cmd,
        DateTimeOffset from,
        DateTimeOffset to,
        SiteStatisticsDashboardQuery query)
    {
        Add(cmd, "@from", from.ToString("O", CultureInfo.InvariantCulture));
        Add(cmd, "@to", to.ToString("O", CultureInfo.InvariantCulture));
        Add(cmd, "@flow", NormalizeFilter(query.Flow));
        Add(cmd, "@device", NormalizeFilter(query.Device));
        Add(cmd, "@source", NormalizeFilter(query.Source));
    }

    private sealed class MutableTrafficPage
    {
        public HashSet<string> Sessions { get; } = new(StringComparer.Ordinal);
        public long PageViews { get; set; }
    }

    private sealed record TrafficSessionAggregate(
        string SessionHash,
        long Events,
        long PageViews,
        long LargestRepeatedPageViews);
}

internal sealed record SessionFirstTouch(
    string? LandingRoute,
    string? FirstReferrerDomain,
    string FirstSource,
    string? FirstCampaign,
    string? FirstUtmSource,
    string? FirstUtmMedium,
    string? FirstUtmCampaign,
    string? FirstUtmTerm,
    string? FirstUtmContent);

internal sealed record RouteProjection(string? Route, string? EntryMode);

internal sealed record QueuedSiteStatisticEvent(
    string Id,
    string OccurredAtUtc,
    string SessionHash,
    string? ActorHash,
    string EventName,
    string? Flow,
    string? Route,
    string? Component,
    string? Step,
    string? Outcome,
    string? ErrorCode,
    long? DurationMs,
    string? DeviceClass,
    string? BrowserFamily,
    string? ReferrerDomain,
    string? Source,
    string? LandingRoute,
    string? FirstReferrerDomain,
    string? FirstSource,
    string? FirstCampaign,
    string? FirstUtmSource,
    string? FirstUtmMedium,
    string? FirstUtmCampaign,
    string? FirstUtmTerm,
    string? FirstUtmContent,
    bool HasExplicitFirstTouch,
    string? MetadataJson);

public sealed class SiteStatisticsEventRequest
{
    public string? EventName { get; set; }
    public string? SessionId { get; set; }
    public string? Flow { get; set; }
    public string? Route { get; set; }
    public string? Component { get; set; }
    public string? Step { get; set; }
    public string? Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public long? DurationMs { get; set; }
    public string? DeviceClass { get; set; }
    public string? BrowserFamily { get; set; }
    public string? ReferrerDomain { get; set; }
    public string? Source { get; set; }
    public string? LandingRoute { get; set; }
    public string? FirstReferrerDomain { get; set; }
    public string? FirstSource { get; set; }
    public string? FirstCampaign { get; set; }
    public string? FirstUtmSource { get; set; }
    public string? FirstUtmMedium { get; set; }
    public string? FirstUtmCampaign { get; set; }
    public string? FirstUtmTerm { get; set; }
    public string? FirstUtmContent { get; set; }
    public Dictionary<string, JsonElement>? Metadata { get; set; }
}

public sealed class SiteStatisticsDashboardQuery
{
    public string? Range { get; set; }
    public string? Flow { get; set; }
    public string? Device { get; set; }
    public string? Source { get; set; }
    public int? Limit { get; set; }
}

public sealed record SiteStatisticsDashboardResponse(
    string GeneratedAtUtc,
    SiteStatisticsDashboardFilters Filters,
    SiteStatisticsTrafficSummary TrafficSummary,
    IReadOnlyList<SiteStatisticsDashboardEvent> Events,
    IReadOnlyList<SiteStatisticsDashboardEvent> PreviousEvents);

public sealed record SiteStatisticsDashboardFilters(
    string Range,
    string? Flow,
    string? Device,
    string? Source,
    string FromUtc,
    string ToUtc,
    string PreviousFromUtc,
    string PreviousToUtc,
    int Limit);

public sealed record SiteStatisticsDashboardEvent(
    string OccurredAtUtc,
    string SessionHash,
    string? ActorHash,
    string EventName,
    string? Flow,
    string? Route,
    string? Component,
    string? Step,
    string? Outcome,
    string? ErrorCode,
    long? DurationMs,
    string? DeviceClass,
    string? BrowserFamily,
    string? ReferrerDomain,
    string? Source,
    string? LandingRoute,
    string? FirstReferrerDomain,
    string? FirstSource,
    string? FirstCampaign,
    string? FirstUtmSource,
    string? FirstUtmMedium,
    string? FirstUtmCampaign,
    string? FirstUtmTerm,
    string? FirstUtmContent,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record SiteStatisticsTrafficSummary(
    SiteStatisticsTrafficTotals Totals,
    SiteStatisticsTrafficTotals PreviousTotals,
    SiteStatisticsTrafficTotals CleanTotals,
    SiteStatisticsTrafficQuality Quality,
    IReadOnlyList<SiteStatisticsTrafficDailyPoint> Daily,
    IReadOnlyList<SiteStatisticsTrafficPage> TopPages,
    IReadOnlyList<SiteStatisticsTrafficBreakdown> Sources,
    IReadOnlyList<SiteStatisticsTrafficBreakdown> Referrers,
    IReadOnlyList<SiteStatisticsTrafficBreakdown> Devices,
    IReadOnlyList<SiteStatisticsTrafficBreakdown> Browsers);

public sealed record SiteStatisticsTrafficTotals(
    long Sessions,
    long PageViews,
    long Events);

public sealed record SiteStatisticsTrafficQuality(
    long RawSessions,
    long CleanSessions,
    long NoisySessions,
    long TopSessionEvents,
    double TopSessionShare,
    long RepeatedPageViewSessions,
    long PageViewDominantSessions,
    long NoisyPageViews,
    double NoisyPageViewShare);

public sealed record SiteStatisticsTrafficDailyPoint(
    string Day,
    long Sessions,
    long PageViews,
    long Events,
    long SuccessSessions,
    long SuccessActions);

public sealed record SiteStatisticsTrafficPage(
    string Route,
    long Sessions,
    long PageViews);

public sealed record SiteStatisticsTrafficBreakdown(
    string Label,
    long Sessions,
    long PageViews,
    long Events);
