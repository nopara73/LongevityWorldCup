using System.Globalization;
using LongevityWorldCup.Website.Tools;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class EventDataService
{
    private void InitializeCrowdAgeAnnouncements()
    {
        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            using var command = sqlite.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name='CrowdAgeAnnouncementState';";
            var migrating = command.ExecuteScalar() is null;
            // Retain the existing queue table name and rows across the policy correction.
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS PendingCrowdAgeMilestones (
                    AthleteSlug TEXT PRIMARY KEY COLLATE NOCASE,
                    ProfileImageId TEXT NOT NULL,
                    EventId TEXT NOT NULL,
                    Place INTEGER NOT NULL,
                    Text TEXT NOT NULL,
                    OccurredAt TEXT NOT NULL,
                    PublishAfterUtc TEXT NOT NULL,
                    Relevance REAL NOT NULL
                );
                CREATE TABLE IF NOT EXISTS CrowdAgeAnnouncementState (
                    AthleteSlug TEXT PRIMARY KEY COLLATE NOCASE,
                    LastPublishedAtUtc TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            if (migrating)
            {
                // The publisher recalculates the deadline against historical announcements.
                command.CommandText = "UPDATE PendingCrowdAgeMilestones SET PublishAfterUtc=OccurredAt;";
                command.ExecuteNonQuery();
            }

            // Restore only posts suppressed by the replaced podium-only rule.
            command.CommandText = """
                UPDATE Events SET XProcessed=0, XSkipReason=NULL WHERE XSkipReason='NonMilestoneCrowdAgeChange';
                UPDATE Events SET ThreadsProcessed=0, ThreadsSkipReason=NULL WHERE ThreadsSkipReason='NonMilestoneCrowdAgeChange';
                """;
            command.ExecuteNonQuery();
            transaction.Commit();
        });
    }

    public void CreateCrowdAgeTop10ChangeEvents(
        IEnumerable<(string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)> items,
        double defaultRelevance = DefaultRelevanceCrowdAgeTop10Change,
        DateTime? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var now = EnsureUtc(nowUtc ?? DateTime.UtcNow);

        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            DiscardChangedImageCrowdAgeAnnouncements(sqlite, transaction);
            foreach (var (slug, occurredAtUtc, place, previousPlace, previousSlug, crowdAge, crowdCount) in items.OrderBy(item => item.OccurredAtUtc))
            {
                if (string.IsNullOrWhiteSpace(slug) ||
                    !CrowdAgeAnnouncementPolicy.IsEligibleChange(place, previousPlace) ||
                    !double.IsFinite(crowdAge) || crowdCount < 100)
                    continue;

                var occurredAt = EnsureUtc(occurredAtUtc);
                var normalizedSlug = NormalizeEventToken(slug);
                using var imageCommand = sqlite.CreateCommand();
                imageCommand.Transaction = transaction;
                imageCommand.CommandText = "SELECT CrowdAgeProfileImageId FROM Athletes WHERE Key=@slug COLLATE NOCASE;";
                imageCommand.Parameters.AddWithValue("@slug", normalizedSlug);
                if (imageCommand.ExecuteScalar() is not string profileImageId || string.IsNullOrWhiteSpace(profileImageId))
                    continue;

                // Preserve the original athlete/place duplicate protection; every upward
                // move in the top 10 is eligible, including a previously unannounced 8 -> 6.
                using var historyCommand = sqlite.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = "SELECT Text FROM Events WHERE Type=@type AND instr(lower(Text), @slug) > 0;";
                historyCommand.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);
                historyCommand.Parameters.AddWithValue("@slug", $"slug[{normalizedSlug.ToLowerInvariant()}]");
                var previouslyAnnounced = false;
                using (var history = historyCommand.ExecuteReader())
                {
                    while (history.Read())
                    {
                        if (EventHelpers.TryExtractCrowdAgeTop10Change(history.GetString(0), out var historicalPlace, out _, out _, out _) &&
                            historicalPlace == place)
                            previouslyAnnounced = true;
                    }
                }
                if (previouslyAnnounced)
                    continue;

                var movementFrom = previousPlace;
                using var pendingCommand = sqlite.CreateCommand();
                pendingCommand.Transaction = transaction;
                pendingCommand.CommandText = "SELECT Place, Text FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug;";
                pendingCommand.Parameters.AddWithValue("@slug", normalizedSlug);
                using (var pending = pendingCommand.ExecuteReader())
                {
                    if (pending.Read())
                    {
                        if (place >= pending.GetInt32(0))
                            continue;
                        EventHelpers.TryExtractCrowdAgeTop10Change(pending.GetString(1), out _, out movementFrom, out _, out _);
                    }
                }

                var parts = new List<string> { $"slug[{normalizedSlug}]", $"place[{place.ToString(CultureInfo.InvariantCulture)}]" };
                if (movementFrom.HasValue)
                    parts.Add($"prevPlace[{movementFrom.Value.ToString(CultureInfo.InvariantCulture)}]");
                if (!string.IsNullOrWhiteSpace(previousSlug))
                    parts.Add($"prev[{NormalizeEventToken(previousSlug)}]");
                parts.Add($"crowdAge[{crowdAge.ToString("0.##", CultureInfo.InvariantCulture)}]");
                parts.Add($"crowdCount[{crowdCount.ToString(CultureInfo.InvariantCulture)}]");

                var lastPublishedAt = GetLastCrowdAgeAnnouncementAt(sqlite, transaction, normalizedSlug);
                var publishAfter = lastPublishedAt?.Add(CrowdAgeAnnouncementPolicy.MinimumInterval) ?? now;
                using var queueCommand = sqlite.CreateCommand();
                queueCommand.Transaction = transaction;
                // Stronger gains replace the draft without moving the cooldown deadline.
                queueCommand.CommandText = """
                    INSERT INTO PendingCrowdAgeMilestones
                        (AthleteSlug, ProfileImageId, EventId, Place, Text, OccurredAt, PublishAfterUtc, Relevance)
                    VALUES (@slug, @image, @id, @place, @text, @occurredAt, @publishAfter, @relevance)
                    ON CONFLICT(AthleteSlug) DO UPDATE SET
                        Place=excluded.Place, Text=excluded.Text,
                        OccurredAt=excluded.OccurredAt, Relevance=excluded.Relevance;
                    """;
                queueCommand.Parameters.AddWithValue("@slug", normalizedSlug);
                queueCommand.Parameters.AddWithValue("@image", profileImageId);
                queueCommand.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
                queueCommand.Parameters.AddWithValue("@place", place);
                queueCommand.Parameters.AddWithValue("@text", string.Join(" ", parts));
                queueCommand.Parameters.AddWithValue("@occurredAt", occurredAt.ToString("o"));
                queueCommand.Parameters.AddWithValue("@publishAfter", publishAfter.ToString("o"));
                queueCommand.Parameters.AddWithValue("@relevance", defaultRelevance);
                queueCommand.ExecuteNonQuery();
            }
            PublishDueCrowdAgeAnnouncements(sqlite, transaction, now);
            transaction.Commit();
        });
        ReloadIntoCache();
    }

    public int PublishPendingCrowdAgeAnnouncements(DateTime? nowUtc = null)
    {
        var published = _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            DiscardChangedImageCrowdAgeAnnouncements(sqlite, transaction);
            var count = PublishDueCrowdAgeAnnouncements(sqlite, transaction, EnsureUtc(nowUtc ?? DateTime.UtcNow));
            transaction.Commit();
            return count;
        });
        if (published > 0)
            ReloadIntoCache();
        return published;
    }

    private static DateTime? GetLastCrowdAgeAnnouncementAt(SqliteConnection sqlite, SqliteTransaction transaction, string slug)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT LastPublishedAtUtc FROM CrowdAgeAnnouncementState WHERE AthleteSlug=@slug;";
        command.Parameters.AddWithValue("@slug", slug);
        if (command.ExecuteScalar() is string publishedAt)
            return DateTime.Parse(publishedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        // Older releases did not store publication times separately from the Event.
        command.CommandText = "SELECT MAX(OccurredAt) FROM Events WHERE Type=@type AND instr(lower(Text), @token) > 0;";
        command.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);
        command.Parameters.AddWithValue("@token", $"slug[{slug.ToLowerInvariant()}]");
        return command.ExecuteScalar() is string occurredAt
            ? DateTime.Parse(occurredAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    private static void DiscardChangedImageCrowdAgeAnnouncements(SqliteConnection sqlite, SqliteTransaction transaction)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM PendingCrowdAgeMilestones
            WHERE NOT EXISTS (
                SELECT 1 FROM Athletes
                WHERE Key=PendingCrowdAgeMilestones.AthleteSlug COLLATE NOCASE
                  AND CrowdAgeProfileImageId=PendingCrowdAgeMilestones.ProfileImageId
            );
            """;
        command.ExecuteNonQuery();
    }

    private static int PublishDueCrowdAgeAnnouncements(SqliteConnection sqlite, SqliteTransaction transaction, DateTime nowUtc)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT AthleteSlug, EventId FROM PendingCrowdAgeMilestones WHERE PublishAfterUtc <= @now;";
        command.Parameters.AddWithValue("@now", nowUtc.ToString("o"));
        var due = new List<(string Slug, string EventId)>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
                due.Add((reader.GetString(0), reader.GetString(1)));
        }

        var published = 0;
        foreach (var (slug, eventId) in due)
        {
            var lastPublishedAt = GetLastCrowdAgeAnnouncementAt(sqlite, transaction, slug);
            var nextAllowedAt = lastPublishedAt?.Add(CrowdAgeAnnouncementPolicy.MinimumInterval);
            command.Parameters.Clear();
            command.Parameters.AddWithValue("@slug", slug);
            if (nextAllowedAt > nowUtc)
            {
                // Also correct drafts left by the previous one-hour collection policy.
                command.CommandText = "UPDATE PendingCrowdAgeMilestones SET PublishAfterUtc=@next WHERE AthleteSlug=@slug;";
                command.Parameters.AddWithValue("@next", nextAllowedAt.Value.ToString("o"));
                command.ExecuteNonQuery();
                continue;
            }

            command.CommandText = """
                INSERT INTO Events (Id, Type, Text, OccurredAt, Relevance)
                SELECT EventId, @type, Text, OccurredAt, Relevance
                FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug
                ON CONFLICT(Id) DO NOTHING;
                """;
            command.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);
            var inserted = command.ExecuteNonQuery();
            if (inserted > 0)
            {
                command.CommandText = """
                    INSERT INTO CrowdAgeAnnouncementState (AthleteSlug, LastPublishedAtUtc) VALUES (@slug, @now)
                    ON CONFLICT(AthleteSlug) DO UPDATE SET LastPublishedAtUtc=excluded.LastPublishedAtUtc;
                    """;
                command.Parameters.AddWithValue("@now", nowUtc.ToString("o"));
                command.ExecuteNonQuery();
                published += inserted;
            }
            command.CommandText = "DELETE FROM PendingCrowdAgeMilestones WHERE AthleteSlug=@slug AND EventId=@id;";
            command.Parameters.AddWithValue("@id", eventId);
            command.ExecuteNonQuery();
        }
        return published;
    }
}
