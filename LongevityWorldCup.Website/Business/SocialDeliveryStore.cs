using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

internal sealed record PendingSocialDelivery(EventItem Event, int Priority, int Attempts, string? RecordKey,
    string? RecordJson, DateTimeOffset? FirstAttemptAtUtc, bool HiddenForMissingAthlete);

internal sealed record SocialPostReceipt(string Id, string Url);

public sealed class SocialDeliveryStore(DatabaseManager db)
{
    internal const string Mastodon = "mastodon";
    internal const string Nostr = "nostr";
    private static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("o");

    internal static void InitializeChannel(DatabaseManager db, string platform) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS SocialDeliveryChannels (Platform TEXT PRIMARY KEY, IntroducedAtUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS SocialDeliveries (
                EventId TEXT NOT NULL, Platform TEXT NOT NULL, Status TEXT NOT NULL,
                RecordKey TEXT, RecordJson TEXT, ContentHash TEXT, RemoteUri TEXT, RemoteCid TEXT,
                SubjectSlug TEXT, AttemptCount INTEGER NOT NULL DEFAULT 0,
                FirstAttemptAtUtc TEXT, NextAttemptAtUtc TEXT, LastErrorCode TEXT, UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EventId, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_SocialDeliveries_Subject ON SocialDeliveries(Platform, Status, SubjectSlug, UpdatedAtUtc);
            """;
        cmd.ExecuteNonQuery();
        // Preserve delivery tables left by an earlier integration while adding our retry marker.
        cmd.CommandText = "PRAGMA table_info(SocialDeliveries);";
        var hasFirstAttempt = false;
        using (var reader = cmd.ExecuteReader())
            while (reader.Read()) hasFirstAttempt |= reader.GetString(1) == "FirstAttemptAtUtc";
        if (!hasFirstAttempt)
        {
            cmd.CommandText = "ALTER TABLE SocialDeliveries ADD COLUMN FirstAttemptAtUtc TEXT;";
            cmd.ExecuteNonQuery();
        }
        cmd.CommandText = """
            INSERT OR IGNORE INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, UpdatedAtUtc)
            SELECT Id, @platform, 'skipped', 'ChannelIntroduced', @now FROM Events
            WHERE NOT EXISTS (SELECT 1 FROM SocialDeliveryChannels WHERE Platform = @platform);
            INSERT OR IGNORE INTO SocialDeliveryChannels (Platform, IntroducedAtUtc) VALUES (@platform, @now);
            """;
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@now", Timestamp(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
        tx.Commit();
    });

    internal static void SelectCustomEventTarget(SqliteConnection sqlite, SqliteTransaction tx, string id, bool selected, string platform)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, UpdatedAtUtc)
            VALUES (@id, @platform, @status, @reason, @now);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@status", selected ? "pending" : "skipped");
        cmd.Parameters.AddWithValue("@reason", selected ? DBNull.Value : "TargetNotSelected");
        cmd.Parameters.AddWithValue("@now", Timestamp(DateTimeOffset.UtcNow));
        cmd.ExecuteNonQuery();
    }

    internal IReadOnlyList<PendingSocialDelivery> GetPending(string platform, bool customOnly, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            SELECT e.Id, e.Type, e.Text, e.OccurredAt, e.Relevance, e.VisibleOnWebsite,
                   COALESCE(d.AttemptCount, 0), d.RecordKey, d.RecordJson, d.FirstAttemptAtUtc,
                   CASE WHEN e.VisibleOnWebsite = 0 AND
                     (e.XSkipReason = 'MissingAthlete' OR e.ThreadsSkipReason = 'MissingAthlete' OR e.FacebookSkipReason = 'MissingAthlete')
                     THEN 1 ELSE 0 END
            FROM Events e LEFT JOIN SocialDeliveries d ON d.EventId = e.Id AND d.Platform = @platform
            WHERE (d.Status IS NULL OR d.Status = 'pending')
              AND (d.NextAttemptAtUtc IS NULL OR d.NextAttemptAtUtc <= @now)
              AND (@customOnly = 0 OR e.Type = @custom OR d.RecordJson IS NOT NULL)
            ORDER BY e.OccurredAt DESC;
            """;
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@now", Timestamp(now));
        cmd.Parameters.AddWithValue("@customOnly", customOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@custom", (int)EventType.CustomEvent);
        using var reader = cmd.ExecuteReader();
        var pending = new List<PendingSocialDelivery>();
        while (reader.Read())
        {
            var item = new EventItem(reader.GetString(0), (EventType)reader.GetInt32(1), reader.GetString(2),
                DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind), reader.GetDouble(4), reader.GetInt32(5) != 0);
            pending.Add(new(item, EventDataService.GetXPriority(item.Type, item.Text), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture), reader.GetInt32(10) != 0));
        }
        return pending.OrderBy(x => x.RecordJson is null).ThenBy(x => x.Priority).ThenByDescending(x => x.Event.OccurredAtUtc).ToArray();
    });

    internal (string Key, string Json) Prepare(string platform, string id, string json, string? subject, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, RecordKey, RecordJson, ContentHash, SubjectSlug, UpdatedAtUtc)
            VALUES (@id, @platform, 'pending', @key, @json, @hash, @subject, @now)
            ON CONFLICT(EventId, Platform) DO UPDATE SET
                RecordKey = @key, RecordJson = @json, ContentHash = @hash, SubjectSlug = @subject, UpdatedAtUtc = @now
            WHERE SocialDeliveries.Status = 'pending' AND SocialDeliveries.RecordJson IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@key", "lwc-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(platform + ":" + id))));
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@hash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        cmd.Parameters.AddWithValue("@subject", (object?)subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", Timestamp(now));
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT RecordKey, RecordJson FROM SocialDeliveries WHERE EventId = @id AND Platform = @platform;";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
            throw new InvalidOperationException("The social delivery was not prepared.");
        return (reader.GetString(0), reader.GetString(1));
    });

    internal void BeginAttempt(string platform, string id, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "UPDATE SocialDeliveries SET FirstAttemptAtUtc = COALESCE(FirstAttemptAtUtc, @now) WHERE EventId = @id AND Platform = @platform AND Status = 'pending';";
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@now", Timestamp(now));
        cmd.ExecuteNonQuery();
    });

    internal void Complete(string platform, string id, SocialPostReceipt receipt, DateTimeOffset now) => Update(platform, id, "sent", null, receipt, null, false, now);
    internal void Skip(string platform, string id, string reason, DateTimeOffset now) => Update(platform, id, "skipped", reason, null, null, false, now);
    internal void RequireReview(string platform, string id, DateTimeOffset now) => Update(platform, id, "review", "UnconfirmedPost", null, null, false, now);
    internal void Fail(string platform, string id, string code, TimeSpan retryDelay, bool clearFirstAttempt, DateTimeOffset now) =>
        Update(platform, id, "pending", code, null, now.Add(retryDelay), clearFirstAttempt, now);

    private void Update(string platform, string id, string status, string? reason, SocialPostReceipt? receipt, DateTimeOffset? retryAt,
        bool clearFirstAttempt, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, RemoteUri, RemoteCid, NextAttemptAtUtc, AttemptCount, UpdatedAtUtc)
            VALUES (@id, @platform, @status, @reason, @uri, @remoteId, @retry, @failed, @now)
            ON CONFLICT(EventId, Platform) DO UPDATE SET
                Status = @status, LastErrorCode = @reason, RemoteUri = @uri, RemoteCid = @remoteId,
                NextAttemptAtUtc = @retry, AttemptCount = SocialDeliveries.AttemptCount + @failed,
                FirstAttemptAtUtc = CASE WHEN @clear = 1 THEN NULL ELSE SocialDeliveries.FirstAttemptAtUtc END,
                UpdatedAtUtc = @now;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uri", (object?)receipt?.Url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@remoteId", (object?)receipt?.Id ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@retry", (object?)retryAt?.UtcDateTime.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@failed", retryAt.HasValue ? 1 : 0);
        cmd.Parameters.AddWithValue("@clear", clearFirstAttempt ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", Timestamp(now));
        cmd.ExecuteNonQuery();
    });

    internal bool IsSubjectOnCooldown(string platform, string subject, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SocialDeliveries WHERE Platform = @platform AND Status = 'sent' AND SubjectSlug = @subject AND UpdatedAtUtc >= @since);";
        cmd.Parameters.AddWithValue("@platform", platform);
        cmd.Parameters.AddWithValue("@subject", subject);
        cmd.Parameters.AddWithValue("@since", Timestamp(now.AddDays(-2)));
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    });
}
