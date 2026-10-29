using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    private static readonly string[] DiscussionNotificationTables =
        ["LongevitymaxxingDiscussionNotifications", "LongevitymaxxingDiscussionSystemPostNotifications"];

    private static void EnsureDiscussionReadSchema(SqliteConnection sqlite)
    {
        using var transaction = sqlite.BeginTransaction();
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS LongevitymaxxingDiscussionVersions (
                Id INTEGER PRIMARY KEY CHECK (Id = 1), ActivityRevision INTEGER NOT NULL, RankingRevision INTEGER NOT NULL);
            """;
        command.ExecuteNonQuery();
        command.CommandText = "SELECT COUNT(*) FROM LongevitymaxxingDiscussionVersions;";
        var firstMigration = Convert.ToInt64(command.ExecuteScalar()) == 0;
        foreach (var table in DiscussionNotificationTables)
        {
            command.CommandText = $"PRAGMA table_info({table});";
            var columns = new HashSet<string>(StringComparer.Ordinal);
            using (var reader = command.ExecuteReader()) while (reader.Read()) columns.Add(reader.GetString(1));
            foreach (var definition in new[] { "CreatedRevision INTEGER NOT NULL DEFAULT 0", "ReadRevision INTEGER NULL", "InAppOnly INTEGER NOT NULL DEFAULT 0" })
            {
                if (columns.Contains(definition.Split(' ')[0])) continue;
                command.CommandText = $"ALTER TABLE {table} ADD COLUMN {definition};";
                command.ExecuteNonQuery();
            }
            // There was no in-app read state before this migration. Do not invent an unread backlog,
            // or confuse an email delivery timestamp with a conversation being read.
            if (firstMigration)
            {
                command.CommandText = $"UPDATE {table} SET ReadRevision = 0;";
                command.ExecuteNonQuery();
            }
            command.CommandText = $"""
                CREATE INDEX IF NOT EXISTS IX_{table}_Unread ON {table}(RecipientParticipantId, ReadRevision, CreatedRevision);
                CREATE TRIGGER IF NOT EXISTS TR_{table}_Created AFTER INSERT ON {table} BEGIN
                    UPDATE LongevitymaxxingDiscussionVersions SET ActivityRevision = ActivityRevision + 1 WHERE Id = 1;
                    UPDATE {table} SET CreatedRevision = (SELECT ActivityRevision FROM LongevitymaxxingDiscussionVersions WHERE Id = 1) WHERE Id = NEW.Id;
                END;
                CREATE TRIGGER IF NOT EXISTS TR_{table}_Deleted AFTER DELETE ON {table} BEGIN
                    UPDATE LongevitymaxxingDiscussionVersions SET ActivityRevision = ActivityRevision + 1, RankingRevision = RankingRevision + 1 WHERE Id = 1;
                END;
                CREATE TRIGGER IF NOT EXISTS TR_{table}_Identity AFTER UPDATE OF RecipientParticipantId, ActorParticipantId, SourceReplyId, {(table == DiscussionNotificationTables[0] ? "PostParticipantId, PostChallengeDay" : "PostId")} ON {table} BEGIN
                    UPDATE LongevitymaxxingDiscussionVersions SET ActivityRevision = ActivityRevision + 1, RankingRevision = RankingRevision + 1 WHERE Id = 1;
                    UPDATE {table} SET CreatedRevision = (SELECT ActivityRevision FROM LongevitymaxxingDiscussionVersions WHERE Id = 1), ReadRevision = NULL WHERE Id = NEW.Id;
                END;
                """;
            command.ExecuteNonQuery();
        }
        command.CommandText = "INSERT OR IGNORE INTO LongevitymaxxingDiscussionVersions VALUES (1, 0, 0);";
        command.ExecuteNonQuery();
        foreach (var (table, updateClause) in new[] {
            ("LongevitymaxxingDiscussionReplies", ""), ("LongevitymaxxingDiscussionSystemPostReplies", ""),
            ("LongevitymaxxingDiscussionSystemPosts", ""),
            ("LongevitymaxxingCheckInImages", ""),
            ("LongevitymaxxingCheckIns", "WHEN OLD.Note IS NOT NEW.Note OR OLD.DiscussionUpdatedAtUtc IS NOT NEW.DiscussionUpdatedAtUtc OR OLD.CheckedInAtUtc IS NOT NEW.CheckedInAtUtc"),
            ("LongevitymaxxingAdviceRequests", "WHEN OLD.Status IS NOT NEW.Status OR OLD.ActiveAdviceReplyId IS NOT NEW.ActiveAdviceReplyId OR OLD.Story IS NOT NEW.Story"),
            ("LongevitymaxxingParticipants", "WHEN OLD.ConfirmedAtUtc IS NOT NEW.ConfirmedAtUtc") })
        {
            foreach (var operation in new[] { "INSERT", "UPDATE", "DELETE" })
            {
                var predicate = operation == "UPDATE" ? updateClause : "";
                if (table == "LongevitymaxxingAdviceRequests" && operation != "UPDATE")
                    predicate = $"WHEN {(operation == "DELETE" ? "OLD" : "NEW")}.Status IN ('published', 'withdrawn')";
                command.CommandText = $"""
                    CREATE TRIGGER IF NOT EXISTS TR_{table}_Rank_{operation} AFTER {operation} ON {table} {predicate} BEGIN
                        UPDATE LongevitymaxxingDiscussionVersions SET ActivityRevision = ActivityRevision + 1, RankingRevision = RankingRevision + 1 WHERE Id = 1;
                    END;
                    """;
                command.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    private static (long Activity, long Ranking) DiscussionVersions(SqliteConnection sqlite, SqliteTransaction? transaction = null)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ActivityRevision, RankingRevision FROM LongevitymaxxingDiscussionVersions WHERE Id = 1;";
        using var reader = command.ExecuteReader();
        reader.Read();
        return (reader.GetInt64(0), reader.GetInt64(1));
    }

    private sealed record UnreadNotification(string Id, string ParticipantId, int Day, string? SystemPostId,
        string? ReplyId, long Revision, long CreatedAt);

    private static List<UnreadNotification> ReadDiscussionNotifications(SqliteConnection sqlite, string participantId,
        long? snapshot = null, SqliteTransaction? transaction = null)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT n.Id AS NotificationId, n.PostParticipantId, n.PostChallengeDay, NULL, n.SourceReplyId, n.CreatedRevision AS Revision, n.CreatedAtUtc
            FROM LongevitymaxxingDiscussionNotifications n
            JOIN LongevitymaxxingParticipants p ON p.Id = n.PostParticipantId AND p.ConfirmedAtUtc IS NOT NULL
            LEFT JOIN LongevitymaxxingDiscussionReplies r ON r.Id = n.SourceReplyId
            WHERE n.RecipientParticipantId = @participant
              AND (@snapshot IS NULL OR n.CreatedRevision <= @snapshot)
              AND (n.ReadRevision IS NULL OR (@snapshot IS NOT NULL AND n.ReadRevision > @snapshot))
              AND (n.SourceReplyId IS NULL OR (r.AdviceDeleted = 0 AND EXISTS
                  (SELECT 1 FROM LongevitymaxxingParticipants sourceAuthor WHERE sourceAuthor.Id = r.AuthorParticipantId AND sourceAuthor.ConfirmedAtUtc IS NOT NULL)))
            UNION ALL
            SELECT n.Id, p.ParticipantId, 0, n.PostId, n.SourceReplyId, n.CreatedRevision, n.CreatedAtUtc
            FROM LongevitymaxxingDiscussionSystemPostNotifications n
            JOIN LongevitymaxxingDiscussionSystemPosts p ON p.Id = n.PostId
            JOIN LongevitymaxxingParticipants author ON author.Id = p.ParticipantId AND author.ConfirmedAtUtc IS NOT NULL
            JOIN LongevitymaxxingDiscussionSystemPostReplies r ON r.Id = n.SourceReplyId
            JOIN LongevitymaxxingParticipants sourceAuthor ON sourceAuthor.Id = r.AuthorParticipantId AND sourceAuthor.ConfirmedAtUtc IS NOT NULL
            WHERE n.RecipientParticipantId = @participant
              AND (@snapshot IS NULL OR n.CreatedRevision <= @snapshot)
              AND (n.ReadRevision IS NULL OR (@snapshot IS NOT NULL AND n.ReadRevision > @snapshot))
            ORDER BY Revision, NotificationId;
            """;
        Add(command, "@participant", participantId);
        Add(command, "@snapshot", snapshot);
        var notifications = new List<UnreadNotification>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) notifications.Add(new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt64(5), ParseDateTimeOffset(reader.GetString(6), DateTimeOffset.UnixEpoch).ToUnixTimeMilliseconds()));
        return notifications;
    }

    private static LongevitymaxxingDiscussionThreadKey ThreadKey(LongevitymaxxingDiscussionThread thread)
        => thread.Note is { } note ? new(note.ParticipantId, note.ChallengeDay) : new(thread.SystemPost!.ParticipantId, 0, thread.SystemPost.Id);

    private static LongevitymaxxingDiscussionThreadKey ThreadKey(UnreadNotification notification)
        => new(notification.ParticipantId, notification.Day, notification.SystemPostId);

    private static Dictionary<LongevitymaxxingDiscussionThreadKey, long> UnreadPositions(List<UnreadNotification> notifications)
        => notifications.GroupBy(ThreadKey).ToDictionary(group => group.Key, group => group.Max(item => item.CreatedAt));

    private static LongevitymaxxingDiscussionReadContext BuildDiscussionReadContext(SqliteConnection sqlite, string participantId,
        IReadOnlyList<LongevitymaxxingDiscussionThread> threads, SqliteTransaction? transaction = null,
        IReadOnlyList<LongevitymaxxingDiscussionReply>? revealedReplies = null)
    {
        var byThread = ReadDiscussionNotifications(sqlite, participantId, transaction: transaction).GroupBy(ThreadKey)
            .ToDictionary(group => group.Key, group => group.ToList());
        var summaries = threads.Select(thread => {
            var key = ThreadKey(thread);
            var rows = byThread.GetValueOrDefault(key) ?? [];
            var firstUnreadReplyId = rows.FirstOrDefault(row => row.ReplyId is not null)?.ReplyId;
            var displayedReplyIds = (thread.Note?.Replies ?? thread.SystemPost!.Replies)
                .Concat(thread.Note?.Advice?.Replies ?? [])
                .Concat(revealedReplies ?? [])
                .Select(reply => reply.Id).ToHashSet(StringComparer.Ordinal);
            return new LongevitymaxxingDiscussionUnreadThread(key.ParticipantId, key.ChallengeDay, key.SystemPostId,
                rows.Select(row => row.ReplyId ?? "opening").Distinct(StringComparer.Ordinal).Count(),
                firstUnreadReplyId, rows.Any(row => row.ReplyId is null),
                rows.Count == 0 ? 0 : rows.Max(row => row.CreatedAt),
                // Bound hidden history while retaining the opening and every rendered or paged reply.
                // Receipts still name exact observed IDs and are written in batches of at most 100.
                rows.Take(100).Concat(rows.Where(row => row.ReplyId is null || displayedReplyIds.Contains(row.ReplyId)))
                    .DistinctBy(row => row.Id)
                    .Select(row => new LongevitymaxxingDiscussionNotification(row.Id, row.ReplyId, row.Revision)).ToList());
        }).ToList();
        return new(DiscussionVersions(sqlite, transaction).Activity, summaries);
    }

    public LongevitymaxxingDiscussionReadResult MarkDiscussionRead(LongevitymaxxingDiscussionReadRequest request)
        => _db.Run(sqlite => {
            var participant = RequireParticipantByAccessToken(request.AccessToken);
            if (request.ObservedRevision < 0 || request.NotificationIds is null || request.NotificationIds.Count > 100 ||
                request.NotificationIds.Any(id => !Guid.TryParseExact(id, "N", out _)))
                throw new InvalidOperationException("That read receipt is invalid.");
            var thread = GetDiscussionThreadCore(request.PostParticipantId, request.ChallengeDay, request.SystemPostId, false);
            using var transaction = sqlite.BeginTransaction();
            var systemPost = thread.SystemPost;
            var key = ThreadKey(thread);
            var acknowledged = new List<string>();
            var revision = DiscussionVersions(sqlite, transaction).Activity;
            foreach (var table in DiscussionNotificationTables)
            {
                if ((systemPost is null) != (table == DiscussionNotificationTables[0])) continue;
                foreach (var id in request.NotificationIds.Distinct(StringComparer.Ordinal))
                {
                    using var command = sqlite.CreateCommand();
                    command.Transaction = transaction;
                    command.CommandText = $"""
                        SELECT ReadRevision FROM {table} WHERE Id = @id AND RecipientParticipantId = @participant
                          AND CreatedRevision <= @observed AND {(systemPost is null ? "PostParticipantId = @post AND PostChallengeDay = @day" : "PostId = @post")};
                        """;
                    Add(command, "@id", id); Add(command, "@participant", participant.Id); Add(command, "@observed", request.ObservedRevision);
                    Add(command, "@post", systemPost?.Id ?? key.ParticipantId); Add(command, "@day", key.ChallengeDay);
                    var read = command.ExecuteScalar();
                    if (read is null) continue;
                    acknowledged.Add(id);
                    if (read is not DBNull) continue;
                    command.CommandText = "UPDATE LongevitymaxxingDiscussionVersions SET ActivityRevision = ActivityRevision + 1 WHERE Id = 1 RETURNING ActivityRevision;";
                    revision = Convert.ToInt64(command.ExecuteScalar());
                    command.CommandText = $"UPDATE {table} SET ReadRevision = @revision WHERE Id = @id;";
                    Add(command, "@revision", revision);
                    command.ExecuteNonQuery();
                }
            }
            var context = BuildDiscussionReadContext(sqlite, participant.Id, [thread], transaction);
            transaction.Commit();
            return new LongevitymaxxingDiscussionReadResult(acknowledged, context);
        });

    private static void NotifyDiscussionParticipants(SqliteConnection sqlite, SqliteTransaction transaction,
        string replyId, string postParticipantId, int day, string? systemPostId, string sender,
        string? replyToId, string? adviceRootId, DateTimeOffset now)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        var replyTable = systemPostId is null ? "LongevitymaxxingDiscussionReplies" : "LongevitymaxxingDiscussionSystemPostReplies";
        command.CommandText = $"""
            SELECT DISTINCT AuthorParticipantId FROM {replyTable}
            WHERE Id <> @reply AND AuthorParticipantId <> @sender
              AND {(systemPostId is null ? "PostParticipantId = @post AND PostChallengeDay = @day AND AdviceRootId IS @root" : "PostId = @post")}
            UNION SELECT AuthorParticipantId FROM {replyTable} WHERE Id = @replyTo AND AuthorParticipantId <> @sender;
            """;
        Add(command, "@reply", replyId); Add(command, "@sender", sender); Add(command, "@post", systemPostId ?? postParticipantId);
        Add(command, "@day", day); Add(command, "@root", adviceRootId); Add(command, "@replyTo", replyToId);
        var recipients = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) recipients.Add(reader.GetString(0));
        var table = systemPostId is null ? DiscussionNotificationTables[0] : DiscussionNotificationTables[1];
        foreach (var recipient in recipients)
        {
            command.Parameters.Clear();
            command.CommandText = $"""
                INSERT OR IGNORE INTO {table}
                (Id, RecipientParticipantId, ActorParticipantId, {(systemPostId is null ? "PostParticipantId, PostChallengeDay" : "PostId")}, Kind, SourceReplyId, CreatedAtUtc, InAppOnly)
                SELECT @id, @recipient, @sender, {(systemPostId is null ? "@post, @day" : "@post")}, 'reply', @reply, @now, 1
                WHERE NOT EXISTS (SELECT 1 FROM {table} WHERE SourceReplyId = @reply AND RecipientParticipantId = @recipient AND Kind = 'reply');
                """;
            Add(command, "@id", Guid.NewGuid().ToString("N")); Add(command, "@recipient", recipient); Add(command, "@sender", sender);
            Add(command, "@post", systemPostId ?? postParticipantId); Add(command, "@day", day);
            Add(command, "@reply", replyId); Add(command, "@now", now.ToString("o"));
            command.ExecuteNonQuery();
        }
    }
}
