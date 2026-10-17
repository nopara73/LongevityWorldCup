using LongevityWorldCup.Website.Tools;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    private static LongevitymaxxingDiscussionAnnouncement? ReadDiscussionAnnouncement(SqliteDataReader reader)
    {
        if (reader.GetString(1) != "announcement" || reader.IsDBNull(8) || reader.IsDBNull(9))
            return null;
        var (title, content) = CustomEventMarkup.SplitTitleAndContent(reader.GetString(9));
        return new(reader.GetString(8), title, content);
    }

    private static void EnsureDiscussionAnnouncementSchema(SqliteConnection sqlite)
    {
        using (var columns = sqlite.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(LongevitymaxxingDiscussionSystemPosts);";
            using var reader = columns.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1) == "AnnouncementText")
                    return;
        }

        // Rebuild the parent table without cascading away existing welcome replies or notifications.
        using var foreignKeys = sqlite.CreateCommand();
        foreignKeys.CommandText = "PRAGMA foreign_keys = OFF;";
        foreignKeys.ExecuteNonQuery();
        try
        {
            using var transaction = sqlite.BeginTransaction();
            using var migration = sqlite.CreateCommand();
            migration.Transaction = transaction;
            migration.CommandText = """
                CREATE TABLE LongevitymaxxingDiscussionSystemPosts_Announcements (
                    Id TEXT PRIMARY KEY,
                    Kind TEXT NOT NULL CHECK (Kind IN ('participant-joined', 'announcement')),
                    ParticipantId TEXT NOT NULL,
                    OccurredDate TEXT NOT NULL,
                    OccurredAtUtc TEXT NOT NULL,
                    SourceEventId TEXT NULL,
                    AnnouncementText TEXT NULL,
                    FOREIGN KEY (ParticipantId) REFERENCES LongevitymaxxingParticipants(Id) ON DELETE CASCADE
                );
                INSERT INTO LongevitymaxxingDiscussionSystemPosts_Announcements
                    (Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc)
                SELECT Id, Kind, ParticipantId, OccurredDate, OccurredAtUtc
                FROM LongevitymaxxingDiscussionSystemPosts;
                DROP TABLE LongevitymaxxingDiscussionSystemPosts;
                ALTER TABLE LongevitymaxxingDiscussionSystemPosts_Announcements RENAME TO LongevitymaxxingDiscussionSystemPosts;
                CREATE UNIQUE INDEX UX_LongevitymaxxingDiscussionSystemPosts_ParticipantKind
                    ON LongevitymaxxingDiscussionSystemPosts(ParticipantId, Kind) WHERE Kind = 'participant-joined';
                """;
            migration.ExecuteNonQuery();
            foreach (var table in new[] { "LongevitymaxxingDiscussionSystemPosts", "LongevitymaxxingDiscussionSystemPostReplies",
                         "LongevitymaxxingDiscussionSystemPostNotifications" })
            {
                using var check = sqlite.CreateCommand();
                check.Transaction = transaction;
                check.CommandText = $"PRAGMA foreign_key_check({table});";
                using var reader = check.ExecuteReader();
                if (reader.Read())
                    throw new InvalidOperationException("Discussion announcement migration could not preserve thread references.");
            }
            transaction.Commit();
        }
        finally
        {
            foreignKeys.CommandText = "PRAGMA foreign_keys = ON;";
            foreignKeys.ExecuteNonQuery();
        }
    }
}
