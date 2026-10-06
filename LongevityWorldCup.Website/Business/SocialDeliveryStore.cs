using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

internal sealed record PendingSocialDelivery(EventItem Event, int Priority, int Attempts, string? RecordKey, string? RecordJson, bool HiddenForMissingAthlete);

public sealed class SocialDeliveryStore(DatabaseManager db)
{
    internal const string Bluesky = "bluesky";

    internal static void InitializeBluesky(DatabaseManager db) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS SocialDeliveryChannels (Platform TEXT PRIMARY KEY, IntroducedAtUtc TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS SocialDeliveries (
                EventId TEXT NOT NULL,
                Platform TEXT NOT NULL,
                Status TEXT NOT NULL,
                RecordKey TEXT,
                RecordJson TEXT,
                ContentHash TEXT,
                RemoteUri TEXT,
                RemoteCid TEXT,
                SubjectSlug TEXT,
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                NextAttemptAtUtc TEXT,
                LastErrorCode TEXT,
                UpdatedAtUtc TEXT NOT NULL,
                PRIMARY KEY (EventId, Platform)
            );
            CREATE INDEX IF NOT EXISTS IX_SocialDeliveries_Subject ON SocialDeliveries(Platform, Status, SubjectSlug, UpdatedAtUtc);
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, UpdatedAtUtc)
            SELECT Id, 'bluesky', 'skipped', 'ChannelIntroduced', @now FROM Events
            WHERE NOT EXISTS (SELECT 1 FROM SocialDeliveryChannels WHERE Platform = 'bluesky');
            INSERT OR IGNORE INTO SocialDeliveryChannels (Platform, IntroducedAtUtc) VALUES ('bluesky', @now);
            """;
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
        tx.Commit();
    });

    internal static void SelectCustomEventTarget(SqliteConnection sqlite, SqliteTransaction tx, string id, bool selected)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, UpdatedAtUtc)
            VALUES (@id, 'bluesky', @status, @reason, @now);
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", selected ? "pending" : "skipped");
        cmd.Parameters.AddWithValue("@reason", selected ? DBNull.Value : "TargetNotSelected");
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    internal IReadOnlyList<PendingSocialDelivery> GetPending(bool customOnly, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            SELECT e.Id, e.Type, e.Text, e.OccurredAt, e.Relevance, e.VisibleOnWebsite,
                   COALESCE(d.AttemptCount, 0), d.RecordKey, d.RecordJson,
                   CASE WHEN e.VisibleOnWebsite = 0 AND
                     (e.XSkipReason = 'MissingAthlete' OR e.ThreadsSkipReason = 'MissingAthlete' OR e.FacebookSkipReason = 'MissingAthlete')
                     THEN 1 ELSE 0 END
            FROM Events e LEFT JOIN SocialDeliveries d ON d.EventId = e.Id AND d.Platform = 'bluesky'
            WHERE (d.Status IS NULL OR d.Status = 'pending')
              AND (d.NextAttemptAtUtc IS NULL OR d.NextAttemptAtUtc <= @now)
              AND (@customOnly = 0 OR e.Type = @custom)
            ORDER BY e.OccurredAt DESC;
            """;
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("@customOnly", customOnly ? 1 : 0);
        cmd.Parameters.AddWithValue("@custom", (int)EventType.CustomEvent);
        using var reader = cmd.ExecuteReader();
        var pending = new List<PendingSocialDelivery>();
        while (reader.Read())
        {
            var e = new EventItem(reader.GetString(0), (EventType)reader.GetInt32(1), reader.GetString(2),
                DateTime.Parse(reader.GetString(3), null, DateTimeStyles.RoundtripKind), reader.GetDouble(4), reader.GetInt32(5) != 0);
            pending.Add(new(e, EventDataService.GetXPriority(e.Type, e.Text), reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetInt32(9) != 0));
        }
        // Recover prepared writes before choosing another announcement.
        return pending.OrderBy(x => x.RecordJson is null).ThenBy(x => x.Priority).ThenByDescending(x => x.Event.OccurredAtUtc).ToArray();
    });

    internal (string Key, string Json) Prepare(string id, string key, string json, string? subject, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, RecordKey, RecordJson, ContentHash, SubjectSlug, UpdatedAtUtc)
            VALUES (@id, 'bluesky', 'pending', @key, @json, @hash, @subject, @now)
            ON CONFLICT(EventId, Platform) DO UPDATE SET
                RecordKey = @key, RecordJson = @json, ContentHash = @hash, SubjectSlug = @subject, UpdatedAtUtc = @now
            WHERE SocialDeliveries.Status = 'pending' AND SocialDeliveries.RecordJson IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@json", json);
        cmd.Parameters.AddWithValue("@hash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json))));
        cmd.Parameters.AddWithValue("@subject", (object?)subject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT RecordKey, RecordJson FROM SocialDeliveries WHERE EventId = @id AND Platform = 'bluesky';";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.IsDBNull(0) || reader.IsDBNull(1))
            throw new InvalidOperationException("The Bluesky delivery was not prepared.");
        return (reader.GetString(0), reader.GetString(1));
    });

    internal void Complete(string id, BlueskyPostReceipt receipt, DateTimeOffset now) => Update(id, "sent", null, receipt, null, now);
    internal void Skip(string id, string reason, DateTimeOffset now) => Update(id, "skipped", reason, null, null, now);
    internal void Fail(string id, int attempts, string reason, DateTimeOffset now) =>
        Update(id, "pending", reason, null, now.AddMinutes(Math.Min(1440, Math.Pow(2, Math.Min(attempts + 1, 11)))) , now);

    private void Update(string id, string status, string? reason, BlueskyPostReceipt? receipt, DateTimeOffset? retryAt, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, RemoteUri, RemoteCid, NextAttemptAtUtc, AttemptCount, UpdatedAtUtc)
            VALUES (@id, 'bluesky', @status, @reason, @uri, @cid, @retry, @failed, @now)
            ON CONFLICT(EventId, Platform) DO UPDATE SET
                Status = @status, LastErrorCode = @reason, RemoteUri = @uri, RemoteCid = @cid,
                NextAttemptAtUtc = @retry, AttemptCount = SocialDeliveries.AttemptCount + @failed, UpdatedAtUtc = @now;
            """;
        cmd.Parameters.AddWithValue("@id", id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@reason", (object?)reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@uri", (object?)receipt?.Uri ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cid", (object?)receipt?.Cid ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@retry", (object?)retryAt?.UtcDateTime.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@failed", retryAt.HasValue ? 1 : 0);
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        cmd.ExecuteNonQuery();
    });

    internal bool IsSubjectOnCooldown(string subject, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS (SELECT 1 FROM SocialDeliveries WHERE Platform = 'bluesky' AND Status = 'sent'
            AND SubjectSlug = @subject AND UpdatedAtUtc >= @since);
            """;
        cmd.Parameters.AddWithValue("@subject", subject);
        cmd.Parameters.AddWithValue("@since", now.AddDays(-2).UtcDateTime.ToString("o"));
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    });
}
