using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

internal sealed record PushAnnouncement(string Id, string Text, bool VisibleOnWebsite, string QueuedAtUtc, string? Payload);
internal sealed record PushRecipient(string Id, WebPushSubscription Subscription, int Attempts);

public sealed class WebPushStore
{
    private readonly DatabaseManager _db;
    private static string Stamp(DateTimeOffset value) => value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);
    private static string Id(WebPushSubscription subscription) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(subscription.Endpoint)));
    private static string Identity(WebPushSubscription subscription) => Convert.ToHexStringLower(SHA256.HashData(
        Encoding.UTF8.GetBytes(subscription.Endpoint + "\n" + subscription.Keys.P256dh + "\n" + subscription.Keys.Auth)));

    public WebPushStore(DatabaseManager db)
    {
        _db = db;
        db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS WebPushSubscriptions (
                    Id TEXT PRIMARY KEY, Endpoint TEXT NOT NULL, P256dh TEXT NOT NULL, Auth TEXT NOT NULL,
                    CreatedAtUtc TEXT NOT NULL, LastSeenAtUtc TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS WebPushDeliveries (
                    EventId TEXT NOT NULL, SubscriptionId TEXT NOT NULL, Status TEXT NOT NULL,
                    Attempts INTEGER NOT NULL DEFAULT 0, NextAttemptAtUtc TEXT, LastErrorCode TEXT, UpdatedAtUtc TEXT NOT NULL,
                    PRIMARY KEY (EventId, SubscriptionId),
                    FOREIGN KEY (SubscriptionId) REFERENCES WebPushSubscriptions(Id) ON DELETE CASCADE
                );
                CREATE TABLE IF NOT EXISTS WebPushRevocations (Identity TEXT PRIMARY KEY, RevokedAtUtc TEXT NOT NULL);
                """;
            cmd.ExecuteNonQuery();
        });
    }

    internal void Subscribe(WebPushSubscription subscription, DateTimeOffset now, IReadOnlyList<WebPushSubscription>? previous = null) => _db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@id", Id(subscription));
        cmd.Parameters.AddWithValue("@endpoint", subscription.Endpoint);
        cmd.Parameters.AddWithValue("@p256dh", subscription.Keys.P256dh);
        cmd.Parameters.AddWithValue("@auth", subscription.Keys.Auth);
        cmd.Parameters.AddWithValue("@now", Stamp(now));
        cmd.Parameters.AddWithValue("@identity", Identity(subscription));
        cmd.CommandText = "SELECT COUNT(*) FROM WebPushRevocations WHERE Identity = @identity;";
        if ((long)cmd.ExecuteScalar()! != 0) throw new InvalidOperationException("Subscription was revoked.");
        cmd.CommandText = "SELECT P256dh, Auth FROM WebPushSubscriptions WHERE Id = @id;";
        var existing = false;
        using (var reader = cmd.ExecuteReader())
        {
            existing = reader.Read();
            if (existing && (reader.GetString(0) != subscription.Keys.P256dh || reader.GetString(1) != subscription.Keys.Auth)
                && !(previous?.Any(old => old.Endpoint == subscription.Endpoint && old.Keys.P256dh == reader.GetString(0)
                    && old.Keys.Auth == reader.GetString(1)) ?? false))
                throw new InvalidOperationException("Subscription keys do not match.");
        }
        if (!existing)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM WebPushSubscriptions;";
            var count = Convert.ToInt64(cmd.ExecuteScalar());
            foreach (var old in (previous ?? []).DistinctBy(Id))
            {
                using var proof = sqlite.CreateCommand();
                proof.Transaction = tx;
                proof.CommandText = "SELECT COUNT(*) FROM WebPushSubscriptions WHERE Id = @old AND P256dh = @key AND Auth = @auth;";
                proof.Parameters.AddWithValue("@old", Id(old));
                proof.Parameters.AddWithValue("@key", old.Keys.P256dh);
                proof.Parameters.AddWithValue("@auth", old.Keys.Auth);
                count -= (long)proof.ExecuteScalar()!;
            }
            if (count >= 20_000) throw new InvalidOperationException("Subscription capacity reached.");
        }
        cmd.CommandText = """
            INSERT INTO WebPushSubscriptions (Id, Endpoint, P256dh, Auth, CreatedAtUtc, LastSeenAtUtc)
            VALUES (@id, @endpoint, @p256dh, @auth, @now, @now)
            ON CONFLICT(Id) DO UPDATE SET P256dh = @p256dh, Auth = @auth, LastSeenAtUtc = @now;
            """;
        cmd.ExecuteNonQuery();
        foreach (var old in previous ?? [])
        {
            if (old == subscription) continue;
            // Preserve frozen announcement recipients and their delivery receipts
            // when the provider changes endpoint. Only the supplied old proof can move them.
            using var move = sqlite.CreateCommand();
            move.Transaction = tx;
            move.Parameters.AddWithValue("@old", Id(old));
            move.Parameters.AddWithValue("@new", Id(subscription));
            move.Parameters.AddWithValue("@key", old.Keys.P256dh);
            move.Parameters.AddWithValue("@auth", old.Keys.Auth);
            move.CommandText = """
                UPDATE WebPushSubscriptions SET CreatedAtUtc = MIN(CreatedAtUtc, COALESCE(
                    (SELECT CreatedAtUtc FROM WebPushSubscriptions WHERE Id = @old AND P256dh = @key AND Auth = @auth), CreatedAtUtc))
                WHERE Id = @new;
                INSERT OR IGNORE INTO WebPushDeliveries (EventId, SubscriptionId, Status, Attempts, NextAttemptAtUtc, LastErrorCode, UpdatedAtUtc)
                SELECT d.EventId, @new, d.Status, d.Attempts, d.NextAttemptAtUtc, d.LastErrorCode, d.UpdatedAtUtc
                FROM WebPushDeliveries d JOIN WebPushSubscriptions s ON s.Id = d.SubscriptionId
                WHERE s.Id = @old AND s.P256dh = @key AND s.Auth = @auth;
                """;
            move.ExecuteNonQuery();
            Revoke(sqlite, tx, old, now);
        }
        tx.Commit();
    });

    internal void Unsubscribe(WebPushSubscription subscription) => _db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        Revoke(sqlite, tx, subscription, DateTimeOffset.UtcNow);
        tx.Commit();
    });

    private static void Revoke(SqliteConnection sqlite, SqliteTransaction tx, WebPushSubscription subscription, DateTimeOffset now)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        // A timed-out save can arrive after unsubscribe. Its exact identity must
        // remain revoked even when there was no subscription row to delete yet.
        cmd.CommandText = """
            INSERT INTO WebPushRevocations (Identity, RevokedAtUtc) VALUES (@identity, @now)
            ON CONFLICT(Identity) DO UPDATE SET RevokedAtUtc = @now;
            DELETE FROM WebPushSubscriptions WHERE Id = @id AND P256dh = @key AND Auth = @auth;
            """;
        cmd.Parameters.AddWithValue("@id", Id(subscription));
        cmd.Parameters.AddWithValue("@key", subscription.Keys.P256dh);
        cmd.Parameters.AddWithValue("@auth", subscription.Keys.Auth);
        cmd.Parameters.AddWithValue("@identity", Identity(subscription));
        cmd.Parameters.AddWithValue("@now", Stamp(now));
        cmd.ExecuteNonQuery();
    }

    internal IReadOnlyList<PushAnnouncement> Pending() => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        // An absent destination row is never consent to send a push notification.
        cmd.CommandText = """
            SELECT e.Id, e.Text, e.VisibleOnWebsite, d.UpdatedAtUtc, d.RecordJson
            FROM Events e JOIN SocialDeliveries d ON d.EventId = e.Id AND d.Platform = 'webpush'
            WHERE e.Type = 6 AND d.Status = 'pending' ORDER BY e.OccurredAt LIMIT 20;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<PushAnnouncement>();
        while (reader.Read()) result.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0,
            reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
        return result;
    });

    internal string Prepare(PushAnnouncement announcement, string payload, DateTimeOffset now) => _db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.Parameters.AddWithValue("@id", announcement.Id);
        cmd.Parameters.AddWithValue("@payload", payload);
        cmd.Parameters.AddWithValue("@now", Stamp(now));
        cmd.Parameters.AddWithValue("@queued", announcement.QueuedAtUtc);
        cmd.Parameters.AddWithValue("@since", Stamp(now.AddDays(-180)));
        cmd.CommandText = """
            INSERT INTO WebPushDeliveries (EventId, SubscriptionId, Status, UpdatedAtUtc)
            SELECT @id, s.Id, 'pending', @now FROM WebPushSubscriptions s
            WHERE s.CreatedAtUtc <= @queued AND s.LastSeenAtUtc >= @since
              AND EXISTS(SELECT 1 FROM SocialDeliveries WHERE EventId = @id AND Platform = 'webpush' AND RecordJson IS NULL AND Status = 'pending');
            UPDATE SocialDeliveries SET RecordJson = @payload, UpdatedAtUtc = @now
            WHERE EventId = @id AND Platform = 'webpush' AND RecordJson IS NULL AND Status = 'pending';
            SELECT RecordJson FROM SocialDeliveries WHERE EventId = @id AND Platform = 'webpush';
            """;
        var prepared = (string)cmd.ExecuteScalar()!;
        tx.Commit();
        return prepared;
    });

    internal IReadOnlyList<PushRecipient> Recipients(string eventId, DateTimeOffset now) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            SELECT s.Id, s.Endpoint, s.P256dh, s.Auth, d.Attempts
            FROM WebPushDeliveries d JOIN WebPushSubscriptions s ON s.Id = d.SubscriptionId
            WHERE d.EventId = @id AND d.Status = 'pending' AND (d.NextAttemptAtUtc IS NULL OR d.NextAttemptAtUtc <= @now)
            ORDER BY s.Id LIMIT 200;
            """;
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@now", Stamp(now));
        using var reader = cmd.ExecuteReader();
        var result = new List<PushRecipient>();
        while (reader.Read()) result.Add(new(reader.GetString(0), new(reader.GetString(1), new(reader.GetString(2), reader.GetString(3))), reader.GetInt32(4)));
        return result;
    });

    internal void Record(string eventId, PushRecipient recipient, string status, string? code, DateTimeOffset? retryAt, DateTimeOffset now) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            UPDATE WebPushDeliveries SET Status = @status, Attempts = Attempts + 1,
                LastErrorCode = @code, NextAttemptAtUtc = @retry, UpdatedAtUtc = @now
            WHERE EventId = @event AND SubscriptionId = @sub AND Status = 'pending'
              AND EXISTS(SELECT 1 FROM WebPushSubscriptions WHERE Id = @sub AND P256dh = @key AND Auth = @auth);
            """;
        cmd.Parameters.AddWithValue("@event", eventId);
        cmd.Parameters.AddWithValue("@sub", recipient.Id);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@code", (object?)code ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@retry", retryAt is null ? DBNull.Value : Stamp(retryAt.Value));
        cmd.Parameters.AddWithValue("@now", Stamp(now));
        cmd.Parameters.AddWithValue("@key", recipient.Subscription.Keys.P256dh);
        cmd.Parameters.AddWithValue("@auth", recipient.Subscription.Keys.Auth);
        cmd.ExecuteNonQuery();
    });

    internal (int Pending, int Accepted, int Total) Progress(string eventId) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*), COALESCE(SUM(Status = 'pending'), 0), COALESCE(SUM(Status = 'sent'), 0) FROM WebPushDeliveries WHERE EventId = @id;";
        cmd.Parameters.AddWithValue("@id", eventId);
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(0));
    });

    internal void Prune(DateTimeOffset now) => _db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            DELETE FROM WebPushSubscriptions WHERE LastSeenAtUtc < @inactive;
            DELETE FROM WebPushDeliveries WHERE Status != 'pending' AND UpdatedAtUtc < @old;
            DELETE FROM WebPushRevocations WHERE RevokedAtUtc < @inactive;
            """;
        cmd.Parameters.AddWithValue("@inactive", Stamp(now.AddDays(-180)));
        cmd.Parameters.AddWithValue("@old", Stamp(now.AddDays(-30)));
        cmd.ExecuteNonQuery();
    });
}
