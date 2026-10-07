using System.Globalization;

namespace LongevityWorldCup.Website.Business;

internal sealed record RedditDeliveryRecord(string EventId, string Status, string Json, string? PostId);

internal enum RedditBeginResult { Started, AlreadyStarted, DailyLimit, NotPending }

public sealed class RedditDeliveryStore(DatabaseManager db)
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
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS RedditDailyDeliveries (UtcDate TEXT PRIMARY KEY, EventId TEXT NOT NULL);";
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

    internal RedditBeginResult Begin(string key, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var tx = sqlite.BeginTransaction();
        using var cmd = sqlite.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT d.EventId, e.Type, d.FirstAttemptAtUtc FROM SocialDeliveries d JOIN Events e ON e.Id = d.EventId
            WHERE d.Platform = 'reddit' AND d.RecordKey = @key AND d.Status = 'pending' AND d.RecordJson IS NOT NULL;
            """;
        cmd.Parameters.AddWithValue("@key", key);
        string id;
        bool custom;
        using (var reader = cmd.ExecuteReader())
        {
            if (!reader.Read()) return RedditBeginResult.NotPending;
            if (!reader.IsDBNull(2)) return RedditBeginResult.AlreadyStarted;
            id = reader.GetString(0);
            custom = reader.GetInt32(1) == (int)EventType.CustomEvent;
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
        tx.Commit();
        return updated == 1 ? RedditBeginResult.Started : RedditBeginResult.AlreadyStarted;
    });

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

    internal bool Complete(string key, string postId, DateTimeOffset now) => db.Run(sqlite =>
    {
        using var cmd = sqlite.CreateCommand();
        cmd.CommandText = """
            UPDATE SocialDeliveries SET Status = 'sent', RemoteCid = @postId, RemoteUri = @url,
                LastErrorCode = NULL, NextAttemptAtUtc = NULL, UpdatedAtUtc = @now
            WHERE Platform = 'reddit' AND RecordKey = @key AND RecordJson IS NOT NULL
                AND FirstAttemptAtUtc IS NOT NULL AND Status IN ('pending', 'review');
            """;
        cmd.Parameters.AddWithValue("@key", key);
        cmd.Parameters.AddWithValue("@postId", postId);
        cmd.Parameters.AddWithValue("@url", $"https://www.reddit.com/r/LongevityWorldCup/comments/{postId[3..]}/");
        cmd.Parameters.AddWithValue("@now", now.UtcDateTime.ToString("o"));
        if (cmd.ExecuteNonQuery() == 1) return true;
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM SocialDeliveries WHERE Platform = 'reddit' AND RecordKey = @key AND Status = 'sent' AND RemoteCid = @postId);";
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    });
}
