using System.Globalization;
using LongevityWorldCup.Website.Tools;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class EventDataService
{
    private void InitializeCrowdAgeMilestones()
    {
        _db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
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
                """;
            command.ExecuteNonQuery();
        });
    }

    public void CreateCrowdAgeTop10ChangeEvents(
        IEnumerable<(string AthleteSlug, DateTime OccurredAtUtc, int Place, int? PreviousPlace, string? PreviousSlug, double CrowdAge, int CrowdCount)> items,
        double defaultRelevance = DefaultRelevanceCrowdAgeTop10Change)
    {
        ArgumentNullException.ThrowIfNull(items);

        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            foreach (var (slug, occurredAtUtc, place, previousPlace, previousSlug, crowdAge, crowdCount) in items.OrderBy(item => item.OccurredAtUtc))
            {
                if (string.IsNullOrWhiteSpace(slug) ||
                    !CrowdAgeMilestonePolicy.IsMilestone(place, previousPlace) ||
                    !double.IsFinite(crowdAge) || crowdCount < 100)
                    continue;

                var occurredAt = EnsureUtc(occurredAtUtc);
                PublishDueCrowdAgeMilestones(sqlite, transaction, occurredAt);
                var normalizedSlug = NormalizeEventToken(slug);

                using var imageCommand = sqlite.CreateCommand();
                imageCommand.Transaction = transaction;
                imageCommand.CommandText = "SELECT CrowdAgeProfileImageId FROM Athletes WHERE Key=@slug COLLATE NOCASE;";
                imageCommand.Parameters.AddWithValue("@slug", normalizedSlug);
                if (imageCommand.ExecuteScalar() is not string profileImageId || string.IsNullOrWhiteSpace(profileImageId))
                    continue;

                // Existing Events also baseline athletes when this policy is deployed.
                // Re-entering the top 10 or reaching a weaker podium spot is not a new milestone.
                using var historyCommand = sqlite.CreateCommand();
                historyCommand.Transaction = transaction;
                historyCommand.CommandText = "SELECT Text FROM Events WHERE Type=@type AND instr(lower(Text), @slug) > 0;";
                historyCommand.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);
                historyCommand.Parameters.AddWithValue("@slug", $"slug[{normalizedSlug.ToLowerInvariant()}]");
                var bestPublishedPlace = 11;
                using (var history = historyCommand.ExecuteReader())
                {
                    while (history.Read())
                    {
                        if (EventHelpers.TryExtractCrowdAgeTop10Change(history.GetString(0), out var historicalPlace, out _, out _, out _))
                            bestPublishedPlace = Math.Min(bestPublishedPlace, historicalPlace);
                    }
                }

                if (place >= bestPublishedPlace || (place > 3 && bestPublishedPlace <= 10))
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
                        if (place >= pending.GetInt32(0) || place > 3)
                            continue;

                        EventHelpers.TryExtractCrowdAgeTop10Change(pending.GetString(1), out _, out movementFrom, out _, out _);
                    }
                }

                var parts = new List<string> { $"slug[{normalizedSlug}]", $"place[{place}]" };
                if (movementFrom.HasValue)
                    parts.Add($"prevPlace[{movementFrom.Value}]");
                if (!string.IsNullOrWhiteSpace(previousSlug))
                    parts.Add($"prev[{NormalizeEventToken(previousSlug)}]");
                parts.Add($"crowdAge[{crowdAge.ToString("0.##", CultureInfo.InvariantCulture)}]");
                parts.Add($"crowdCount[{crowdCount.ToString(CultureInfo.InvariantCulture)}]");

                using var queueCommand = sqlite.CreateCommand();
                queueCommand.Transaction = transaction;
                // Keep the first deadline and event identity so successive gains cannot
                // postpone publication indefinitely or become separate announcements.
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
                queueCommand.Parameters.AddWithValue("@publishAfter", occurredAt.Add(CrowdAgeMilestonePolicy.CollectionWindow).ToString("o"));
                queueCommand.Parameters.AddWithValue("@relevance", defaultRelevance);
                queueCommand.ExecuteNonQuery();
            }
            transaction.Commit();
        });

        ReloadIntoCache();
    }

    public int PublishPendingCrowdAgeMilestones(DateTime? nowUtc = null)
    {
        var published = _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction();
            var count = PublishDueCrowdAgeMilestones(sqlite, transaction, EnsureUtc(nowUtc ?? DateTime.UtcNow));
            transaction.Commit();
            return count;
        });
        if (published > 0)
            ReloadIntoCache();
        return published;
    }

    private static int PublishDueCrowdAgeMilestones(SqliteConnection sqlite, SqliteTransaction transaction, DateTime nowUtc)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        // Unpublished milestones belong to the exact image that earned them.
        command.CommandText = """
            DELETE FROM PendingCrowdAgeMilestones
            WHERE NOT EXISTS (
                SELECT 1 FROM Athletes
                WHERE Key=PendingCrowdAgeMilestones.AthleteSlug COLLATE NOCASE
                  AND CrowdAgeProfileImageId=PendingCrowdAgeMilestones.ProfileImageId
            );
            """;
        command.ExecuteNonQuery();

        command.CommandText = """
            INSERT OR IGNORE INTO Events (Id, Type, Text, OccurredAt, Relevance)
            SELECT EventId, @type, Text, OccurredAt, Relevance
            FROM PendingCrowdAgeMilestones WHERE PublishAfterUtc <= @now;
            """;
        command.Parameters.AddWithValue("@type", (int)EventType.CrowdAgeTop10Change);
        command.Parameters.AddWithValue("@now", nowUtc.ToString("o"));
        var published = command.ExecuteNonQuery();
        command.CommandText = "DELETE FROM PendingCrowdAgeMilestones WHERE PublishAfterUtc <= @now;";
        command.ExecuteNonQuery();
        return published;
    }
}
