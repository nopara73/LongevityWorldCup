using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed class SiteStatisticsService : IHostedService
{
    private const int MaxEventNameLength = 96;
    private const int MaxTextLength = 160;
    private const int MaxMetadataJsonLength = 4096;
    private const int DefaultDashboardLimit = 2500;
    private const int MaxDashboardLimit = 5000;
    private const int MaxQueuedEvents = 2000;
    private const int FlushBatchSize = 250;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultDashboardRange = TimeSpan.FromDays(30);
    private static readonly string[] SensitiveQueryKeys =
    [
        "token", "confirm", "stop", "accessToken", "stopToken", "invoiceId", "checkoutLink",
        "email", "accountEmail", "name", "displayName"
    ];
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
        IReadOnlyDictionary<string, object?>? metadata = null,
        CancellationToken ct = default)
    {
        var request = new SiteStatisticsEventRequest
        {
            EventName = eventName,
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
            SELECT OccurredAtUtc, SessionHash, ActorHash, EventName, Flow, Route, Component, Step, Outcome,
                   ErrorCode, DurationMs, DeviceClass, BrowserFamily, ReferrerDomain, Source, MetadataJson
            FROM SiteStatisticEvents
            WHERE OccurredAtUtc >= @from
              AND OccurredAtUtc < @to
              AND (@flow = '' OR Flow = @flow)
              AND (@device = '' OR DeviceClass = @device)
              AND (@source = '' OR Source = @source)
            ORDER BY OccurredAtUtc DESC
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
            events.Add(new SiteStatisticsDashboardEvent(
                OccurredAtUtc: RoundToMinute(ReadString(reader, 0)),
                SessionHash: ReadString(reader, 1),
                ActorHash: ReadNullableString(reader, 2),
                EventName: ReadString(reader, 3),
                Flow: ReadNullableString(reader, 4),
                Route: ReadNullableString(reader, 5),
                Component: ReadNullableString(reader, 6),
                Step: ReadNullableString(reader, 7),
                Outcome: ReadNullableString(reader, 8),
                ErrorCode: ReadNullableString(reader, 9),
                DurationMs: reader.IsDBNull(10) ? null : reader.GetInt64(10),
                DeviceClass: ReadNullableString(reader, 11),
                BrowserFamily: ReadNullableString(reader, 12),
                ReferrerDomain: ReadNullableString(reader, 13),
                Source: ReadNullableString(reader, 14),
                Metadata: ReadMetadata(ReadNullableString(reader, 15))));
        }

        return events;
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

        var route = SanitizeRoute(request.Route ?? context?.Request.Path.Value ?? string.Empty);
        var metadata = SanitizeMetadata(request.Metadata);
        var metadataJson = metadata.Count == 0 ? null : JsonSerializer.Serialize(metadata, JsonOptions);
        if (metadataJson is { Length: > MaxMetadataJsonLength })
        {
            metadataJson = metadataJson[..MaxMetadataJsonLength];
        }

        return new QueuedSiteStatisticEvent(
            Id: Guid.NewGuid().ToString("N"),
            OccurredAtUtc: occurredAt.ToString("O", CultureInfo.InvariantCulture),
            SessionHash: BuildHash("S", request.SessionId ?? ClientIdentifier.From(context ?? new DefaultHttpContext())),
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
            ReferrerDomain: SafeDomain(request.ReferrerDomain) ?? SafeDomain(context?.Request.Headers.Referer.FirstOrDefault()),
            Source: SafeDisplayToken(request.Source, MaxTextLength) ?? "direct",
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
                    """;
                cmd.ExecuteNonQuery();
            });

            _initialized = true;
        }
    }

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

    private static string? SanitizeRoute(string? route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return null;

        if (!Uri.TryCreate(route, UriKind.RelativeOrAbsolute, out var uri))
            return SafeToken(route.Split('?')[0], MaxTextLength);

        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : route.Split('?')[0];
        path = SafeToken(path, MaxTextLength);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (!route.Contains('?'))
            return path;

        var query = route[(route.IndexOf('?') + 1)..];
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

        return pairs.Count == 0 ? path : $"{path}?{string.Join("&", pairs)}";
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
}

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
    IReadOnlyDictionary<string, string> Metadata);
