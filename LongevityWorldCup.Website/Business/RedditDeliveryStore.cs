using System.Globalization;
using System.Text.Json;
using LongevityWorldCup.Website.Tools;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

internal sealed record RedditDeliveryRecord(string EventId, string Status, string Json, string? PostId);

internal enum RedditBeginResult { Started, AlreadyStarted, DailyLimit, NotPending, IdenticalPostInReceiptWindow }

public sealed class RedditDeliveryStore(DatabaseManager db, ILogger<RedditDeliveryStore>? log = null)
{
    internal bool IsActive => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SocialDeliveryChannels WHERE Platform = 'reddit');";
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    });

    internal void Activate()
    {
        // The first authenticated poll is the activation boundary, including Custom Events
        // selected while the app was still being set up.
        SocialDeliveryStore.InitializeChannel(db, SocialDeliveryStore.Reddit, baselineSelectedTargets: true);
        db.Run(sqlite =>
        {
            using var cmd = sqlite.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS RedditDailyDeliveries (UtcDate TEXT PRIMARY KEY, EventId TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS RedditIftttAttempts (RecordKey TEXT PRIMARY KEY, Fingerprint TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS IX_RedditIftttAttempts_Fingerprint ON RedditIftttAttempts(Fingerprint);
                """;
            cmd.ExecuteNonQuery();
        });
    }

    internal bool ReserveDailyDelivery(string eventId, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR IGNORE INTO RedditDailyDeliveries (UtcDate, EventId) VALUES (@date, @id);";
        cmd.Parameters.AddWithValue("@date", now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.ExecuteNonQuery();
        cmd.CommandText = "SELECT EventId FROM RedditDailyDeliveries WHERE UtcDate = @date;";
        var reserved = (string?)cmd.ExecuteScalar() == eventId;
        tx.Commit();
        return reserved;
    });

    internal bool HasNewerLeaderEvent(DateTime occurredAtUtc) =>
        db.Run(sqlite => HasNewerLeaderEvent(sqlite, null, occurredAtUtc));

    private static bool HasNewerLeaderEvent(SqliteConnection sqlite, SqliteTransaction? tx, DateTime occurredAtUtc)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Text FROM Events WHERE Type = @type AND OccurredAt > @occurred;";
        cmd.Parameters.AddWithValue("@type", (int)EventType.NewRank);
        cmd.Parameters.AddWithValue("@occurred", occurredAtUtc.ToString("o", CultureInfo.InvariantCulture));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var text = reader.GetString(0);
            if (EventHelpers.TryExtractRank(text, out var rank) && rank == 1 &&
                EventHelpers.TryExtractSlug(text, out var slug) && !string.IsNullOrWhiteSpace(slug)) return true;
        }
        return false;
    }

    internal RedditDeliveryRecord? Find(string key) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            SELECT EventId, Status, RecordJson, RemoteCid FROM SocialDeliveries
            WHERE Platform = 'reddit' AND RecordKey = @key AND RecordJson IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? new RedditDeliveryRecord(reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3)) : null;
    });

    internal void SkipBlockedLink(string eventId, string link, DateTimeOffset now)
    {
        if (SkipUnstarted(eventId, "BlockedLink", now))
            LogBlockedLink(eventId, link);
    }

    internal bool SkipUnstarted(string eventId, string reason, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        var skipped = MarkUnstartedSkipped(sqlite, tx, eventId, reason, now);
        tx.Commit();
        return skipped;
    });

    internal RedditBeginResult Begin(string key, DateTimeOffset now, string? iftttFingerprint = null,
        string[]? confirmedBans = null) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT d.EventId, e.Type, d.FirstAttemptAtUtc, d.RecordJson, e.Text, e.OccurredAt
            FROM SocialDeliveries d JOIN Events e ON e.Id = d.EventId
            WHERE d.Platform = 'reddit' AND d.RecordKey = @key AND d.Status = 'pending' AND d.RecordJson IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        string id;
        bool custom;
        DateTime occurredAt;
        string? blockedLink;
        string? skipReason;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return RedditBeginResult.NotPending;
            if (!reader.IsDBNull(2)) return RedditBeginResult.AlreadyStarted;
            id = reader.GetString(0);
            custom = reader.GetInt32(1) == (int)EventType.CustomEvent;
            occurredAt = DateTime.Parse(reader.GetString(5), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            skipReason = RedditEventPolicy.SkipReason((EventType)reader.GetInt32(1), reader.GetString(4),
                occurredAt, now);
            var prepared = JsonSerializer.Deserialize<RedditPostRequest>(reader.GetString(3))
                ?? throw new InvalidOperationException("The saved Reddit request is invalid.");
            blockedLink = RedditLinkPolicy.FindBlockedPostLink(prepared, confirmedBans);
            if (blockedLink is null && custom)
                blockedLink = RedditLinkPolicy.FindBlockedLink(reader.GetString(4), confirmedBans);
        }
        if (skipReason is null && !custom && HasNewerLeaderEvent(sqlite, tx, occurredAt))
            skipReason = "SupersededLeader";
        if (skipReason is not null)
        {
            MarkUnstartedSkipped(sqlite, tx, id, skipReason, now);
            tx.Commit();
            return RedditBeginResult.NotPending;
        }
        if (blockedLink is not null)
        {
            MarkUnstartedSkipped(sqlite, tx, id, "BlockedLink", now);
            tx.Commit();
            LogBlockedLink(id, blockedLink);
            return RedditBeginResult.NotPending;
        }
        if (iftttFingerprint is not null)
        {
            // Keep receipt windows for identical copy from overlapping, even if the first post was already confirmed.
            cmd.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM RedditIftttAttempts a JOIN SocialDeliveries d
                        ON d.Platform = 'reddit' AND d.RecordKey = a.RecordKey
                    WHERE a.Fingerprint = @fingerprint AND d.FirstAttemptAtUtc >= @since
                );
                """;
            cmd.Parameters.AddWithValue("@fingerprint", iftttFingerprint);
            cmd.Parameters.AddWithValue("@since", (now - RedditIftttPost.ConfirmationWindow - TimeSpan.FromSeconds(1)).UtcDateTime.ToString("o"));
            if (Convert.ToInt32(cmd.ExecuteScalar()) != 0) return RedditBeginResult.IdenticalPostInReceiptWindow;
        }
        if (!custom)
        {
            cmd.CommandText = "INSERT OR IGNORE INTO RedditDailyDeliveries (UtcDate, EventId) VALUES (@date, @id);";
            cmd.Parameters.AddWithValue("@date", now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            cmd.CommandText = "SELECT EventId FROM RedditDailyDeliveries WHERE UtcDate = @date;";
            if ((string?)cmd.ExecuteScalar() != id) return RedditBeginResult.DailyLimit;
        }
        cmd.CommandText = "UPDATE SocialDeliveries SET FirstAttemptAtUtc = @now WHERE Platform = 'reddit' AND RecordKey = @key AND FirstAttemptAtUtc IS NULL AND Status = 'pending';";
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        var updated = cmd.ExecuteNonQuery();
        if (updated == 1 && iftttFingerprint is not null)
        {
            cmd.CommandText = "INSERT INTO RedditIftttAttempts (RecordKey, Fingerprint) VALUES (@key, @fingerprint);";
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return updated == 1 ? RedditBeginResult.Started : RedditBeginResult.AlreadyStarted;
    });

    private static bool MarkUnstartedSkipped(SqliteConnection sqlite, SqliteTransaction tx, string eventId,
        string reason, DateTimeOffset now)
    {
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO SocialDeliveries (EventId, Platform, Status, LastErrorCode, UpdatedAtUtc)
            VALUES (@id, 'reddit', 'skipped', @reason, @now)
            ON CONFLICT(EventId, Platform) DO UPDATE SET
                Status = 'skipped', LastErrorCode = @reason, UpdatedAtUtc = @now
            WHERE SocialDeliveries.Status = 'pending' AND SocialDeliveries.FirstAttemptAtUtc IS NULL;
            """;
        cmd.Parameters.AddWithValue("@id", eventId);
        cmd.Parameters.AddWithValue("@reason", reason);
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        if (cmd.ExecuteNonQuery() == 0) return false;
        // An abandoned reservation isn't a post. Never release one for an attempt.
        cmd.CommandText = "DELETE FROM RedditDailyDeliveries WHERE EventId = @id;";
        cmd.ExecuteNonQuery();
        return true;
    }

    private void LogBlockedLink(string eventId, string link) =>
        log?.LogInformation("Skipped Reddit delivery for Event {EventId} with blocked link {Link}.", eventId, link);

    internal bool RequireReview(string key, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            UPDATE SocialDeliveries SET Status = 'review', LastErrorCode = 'UnconfirmedPost', UpdatedAtUtc = @now
            WHERE Platform = 'reddit' AND RecordKey = @key AND RecordJson IS NOT NULL
                AND Status IN ('pending', 'review');
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        return cmd.ExecuteNonQuery() == 1;
    });

    internal void ExpireIftttAttempts(DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            UPDATE SocialDeliveries SET Status = 'review', LastErrorCode = 'UnconfirmedPost', UpdatedAtUtc = @now
            WHERE Platform = 'reddit' AND Status = 'pending' AND FirstAttemptAtUtc < @since
                AND RecordKey IN (SELECT RecordKey FROM RedditIftttAttempts);
            """;
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        cmd.Parameters.AddWithValue("@since", (now - RedditIftttPost.ConfirmationWindow).UtcDateTime.ToString("o"));
        cmd.ExecuteNonQuery();
    });

    internal bool ConfirmIfttt(string plainFingerprint, string htmlFingerprint, string postId,
        DateTimeOffset postedAt, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM sqlite_schema WHERE type = 'table' AND name = 'RedditIftttAttempts');";
        if (Convert.ToInt32(cmd.ExecuteScalar()) == 0) return false;

        // Include confirmed attempts so a provider duplicate cannot complete another Event with the same copy.
        cmd.CommandText = """
            SELECT d.RecordKey
            FROM RedditIftttAttempts a JOIN SocialDeliveries d ON d.Platform = 'reddit' AND d.RecordKey = a.RecordKey
            WHERE a.Fingerprint IN (@plain, @html) AND d.FirstAttemptAtUtc >= @since AND d.FirstAttemptAtUtc < @before
            LIMIT 2;
            """;
        cmd.Parameters.AddWithValue("@plain", plainFingerprint);
        cmd.Parameters.AddWithValue("@html", htmlFingerprint);
        cmd.Parameters.AddWithValue("@since", (postedAt - RedditIftttPost.ConfirmationWindow).UtcDateTime.ToString("o"));
        // The applet sends the post time in whole Unix seconds.
        cmd.Parameters.AddWithValue("@before", postedAt.AddSeconds(1).UtcDateTime.ToString("o"));
        string key;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return false;
            key = reader.GetString(0);
            if (reader.Read()) return false;
        }

        var matched = Complete(cmd, key, postId, now);
        tx.Commit();
        return matched;
    });

    internal bool Complete(string key, string postId, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        return Complete(cmd, key, postId, now);
    });

    private static bool Complete(SqliteCommand cmd, string key, string postId, DateTimeOffset now)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = """
            UPDATE SocialDeliveries SET Status = 'sent', RemoteCid = @postId, RemoteUri = @url,
                LastErrorCode = NULL, NextAttemptAtUtc = NULL, UpdatedAtUtc = @now
            WHERE Platform = 'reddit' AND RecordKey = @key AND RecordJson IS NOT NULL
                AND FirstAttemptAtUtc IS NOT NULL AND Status IN ('pending', 'review')
                AND NOT EXISTS (SELECT 1 FROM SocialDeliveries WHERE Platform = 'reddit' AND RemoteCid = @postId);
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@postId", postId);
        cmd.Parameters.AddWithValue("@url", $"https://www.reddit.com/r/LongevityWorldCup/comments/{postId[3..]}/");
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        if (cmd.ExecuteNonQuery() == 1) return true;
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SocialDeliveries WHERE Platform = 'reddit' AND RecordKey = @key AND Status = 'sent' AND RemoteCid = @postId);";
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }
}
