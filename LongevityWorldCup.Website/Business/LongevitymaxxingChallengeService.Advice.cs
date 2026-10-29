using System.Globalization;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    private static void EnsureAdviceSchema(SqliteConnection sqlite)
    {
        using var command = sqlite.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS LongevitymaxxingAdviceRequests (
                Id TEXT NOT NULL UNIQUE,
                ParticipantId TEXT NOT NULL,
                ChallengeDay INTEGER NOT NULL,
                Status TEXT NOT NULL,
                TriggerRawScore INTEGER NOT NULL,
                BaselineRawScore REAL NULL,
                CharacterLimit INTEGER NOT NULL,
                CreatedAtUtc TEXT NOT NULL,
                Story TEXT NULL,
                PublishedAtUtc TEXT NULL,
                UpdatedAtUtc TEXT NOT NULL,
                RankSnapshotJson TEXT NOT NULL DEFAULT '{}',
                ActiveAdviceReplyId TEXT NULL,
                Version INTEGER NOT NULL DEFAULT 1,
                PublishHash TEXT NOT NULL DEFAULT '',
                PRIMARY KEY (ParticipantId, ChallengeDay),
                FOREIGN KEY (ParticipantId, ChallengeDay)
                    REFERENCES LongevitymaxxingCheckIns(ParticipantId, ChallengeDay) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();
        TryAddLongevitymaxxingDiscussionRepliesColumn(sqlite, "IsAdvice INTEGER NOT NULL DEFAULT 0");
        TryAddLongevitymaxxingDiscussionRepliesColumn(sqlite, "AdviceRootId TEXT NULL");
        TryAddLongevitymaxxingDiscussionRepliesColumn(sqlite, "AdviceRank INTEGER NULL");
        TryAddLongevitymaxxingDiscussionRepliesColumn(sqlite, "AdviceDeleted INTEGER NOT NULL DEFAULT 0");
    }

    private void UpdateAdvicePrompt(SqliteConnection sqlite, SqliteTransaction transaction,
        ValidatedCheckIn checkIn, int characterLimit)
    {
        var history = new Dictionary<int, CheckInRecord>();
        using (var read = sqlite.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = """
                SELECT ChallengeDay, Sleep, Exercise, Nutrition, Vices
                FROM LongevitymaxxingCheckIns WHERE ParticipantId = @participant;
                """;
            Add(read, "@participant", checkIn.Participant.Id);
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                var day = reader.GetInt32(0);
                history[day] = new(checkIn.Participant.Id, day, "", reader.GetInt32(1), reader.GetInt32(2),
                    reader.GetInt32(3), reader.GetInt32(4), null, null, null, []);
            }
        }
        var current = history[checkIn.Request.ChallengeDay];
        var practice = GetParticipantPracticeDay(BuildSettings(), checkIn.Participant, history);
        // Practice still reports real habits. Include it in the baseline, but never prompt on it.
        var scores = history.Values.Where(day => day.ChallengeDay < current.ChallengeDay &&
                day.ChallengeDay >= current.ChallengeDay - 14)
            .OrderByDescending(day => day.ChallengeDay).Take(7).Select(day => day.Score).ToArray();
        double? baseline = scores.Length >= 6 ? scores.Average() : null;
        var standardDeviation = baseline is { } mean
            ? Math.Sqrt(scores.Sum(score => Math.Pow(score - mean, 2)) / (scores.Length - 1)) : 0;
        var qualifies = current.ChallengeDay >= practice + 6 && checkIn.CountsForScore &&
            GetEffectiveRawScore(current, history) != RawDailyMaxScore && baseline is { } average &&
            current.Score < average && average - current.Score >= 2 * standardDeviation;
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        // A corrected pending check-in is retired. A published or dismissed story never reopens itself.
        command.CommandText = qualifies ? """
            INSERT OR IGNORE INTO LongevitymaxxingAdviceRequests
                (Id, ParticipantId, ChallengeDay, Status, TriggerRawScore, BaselineRawScore, CharacterLimit, CreatedAtUtc, UpdatedAtUtc)
            VALUES (@id, @participant, @day, 'pending', @score, @baseline, @limit, @now, @now);
            UPDATE LongevitymaxxingAdviceRequests
            SET TriggerRawScore = @score, BaselineRawScore = @baseline
            WHERE ParticipantId = @participant AND ChallengeDay = @day AND Status = 'pending';
            """ : """
            UPDATE LongevitymaxxingAdviceRequests SET Status = 'dismissed', Version = Version + 1, UpdatedAtUtc = @now
            WHERE ParticipantId = @participant AND ChallengeDay = @day AND Status = 'pending';
            """;
        Add(command, "@id", Guid.NewGuid().ToString("N"));
        Add(command, "@participant", checkIn.Participant.Id);
        Add(command, "@day", current.ChallengeDay);
        Add(command, "@score", current.Score);
        Add(command, "@baseline", baseline);
        Add(command, "@limit", characterLimit);
        Add(command, "@now", checkIn.NowUtc.ToString("o"));
        command.ExecuteNonQuery();
    }

    private LongevitymaxxingAdvicePrompt? GetPendingAdvicePrompt(string participantId)
        => _db.Run(sqlite =>
        {
            using var command = sqlite.CreateCommand();
            command.CommandText = """
                SELECT a.Id, a.ChallengeDay, c.ChallengeDate, a.TriggerRawScore, a.BaselineRawScore, a.CharacterLimit, a.Version, c.Note
                FROM LongevitymaxxingAdviceRequests a
                JOIN LongevitymaxxingCheckIns c ON c.ParticipantId = a.ParticipantId AND c.ChallengeDay = a.ChallengeDay
                WHERE a.ParticipantId = @participant AND a.Status = 'pending'
                ORDER BY a.ChallengeDay DESC LIMIT 1;
                """;
            Add(command, "@participant", participantId);
            using var reader = command.ExecuteReader();
            return reader.Read() ? new LongevitymaxxingAdvicePrompt(reader.GetString(0), reader.GetInt32(1), reader.GetString(2),
                reader.GetInt32(3), reader.IsDBNull(4) ? null : reader.GetDouble(4), reader.GetInt32(5), reader.GetInt32(6))
                { CheckInNote = reader.IsDBNull(7) ? null : reader.GetString(7) } : null;
        });

    public LongevitymaxxingParticipantState PublishAdviceStory(LongevitymaxxingAdviceStoryRequest request, DateTimeOffset? nowUtc = null)
        => ChangeAdviceStory(request, "published", nowUtc);

    public LongevitymaxxingParticipantState DismissAdviceStory(LongevitymaxxingAdviceStoryRequest request, DateTimeOffset? nowUtc = null)
        => ChangeAdviceStory(request, "dismissed", nowUtc);

    public LongevitymaxxingParticipantState WithdrawAdviceStory(LongevitymaxxingAdviceStoryRequest request, DateTimeOffset? nowUtc = null)
        => ChangeAdviceStory(request, "withdrawn", nowUtc);

    public LongevitymaxxingParticipantState EditAdviceStory(LongevitymaxxingAdviceStoryRequest request, DateTimeOffset? nowUtc = null)
    {
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var day = 0;
        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var advice = ReadAdviceRequest(sqlite, transaction, id: request.AdviceId)
                ?? throw new InvalidOperationException("That advice request is no longer available.");
            if (advice.ParticipantId != author.Id) throw new UnauthorizedAccessException("You can only change your own advice requests.");
            if (advice.Status != "published" || advice.Version != request.Version)
                throw new LongevitymaxxingAdviceChangedException("This request changed. Refresh before editing it.");
            var story = request.Story?.Trim() ?? "";
            if (story.Length == 0 || story.Length > Math.Max(advice.CharacterLimit, advice.Story.Length))
                throw new InvalidOperationException($"Write your story in 1–{Math.Max(advice.CharacterLimit, advice.Story.Length)} characters.");
            using var update = sqlite.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE LongevitymaxxingAdviceRequests SET Story = @story, Version = Version + 1, UpdatedAtUtc = @now WHERE Id = @id;";
            Add(update, "@story", story);
            Add(update, "@now", now.ToString("o"));
            Add(update, "@id", advice.Id);
            update.ExecuteNonQuery();
            SyncAdviceStoryMentions(sqlite, transaction, advice, story, now);
            day = advice.Day;
            transaction.Commit();
        });
        return GetAdviceMutationState(request.AccessToken, author.Id, day, now);
    }

    private LongevitymaxxingParticipantState ChangeAdviceStory(LongevitymaxxingAdviceStoryRequest request, string target, DateTimeOffset? nowUtc)
    {
        var author = RequireParticipantByAccessToken(request.AccessToken);
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var story = request.Story?.Trim() ?? "";
        var affectedDay = 0;
        var ranks = target == "published" ? GetPublicState(now).Leaderboard
            .Where(row => row.Rank > 0)
            .ToDictionary(row => row.ParticipantId, row => row.Rank, StringComparer.Ordinal) : [];
        _db.Run(sqlite =>
        {
            using var transaction = sqlite.BeginTransaction(deferred: false);
            var advice = ReadAdviceRequest(sqlite, transaction, id: request.AdviceId)
                ?? throw new InvalidOperationException("That advice request is no longer available.");
            affectedDay = advice.Day;
            if (advice.ParticipantId != author.Id) throw new UnauthorizedAccessException("You can only change your own advice requests.");
            var publishHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(story)));
            if (target == "published" && (advice.Status is "published" or "withdrawn") && advice.PublishHash == publishHash) return;
            if (advice.Status == target && target != "published") return;
            if (advice.Version != request.Version) throw new LongevitymaxxingAdviceChangedException("This request changed in another tab.");
            if (target == "published")
            {
                if (advice.Status != "pending") throw new LongevitymaxxingAdviceChangedException("This advice request has already been handled.");
                if (story.Length == 0 || story.Length > advice.CharacterLimit)
                    throw new InvalidOperationException($"Write your story in 1–{advice.CharacterLimit} characters.");
            }
            else if (target == "withdrawn" ? advice.Status != "published" : advice.Status != "pending")
                throw new LongevitymaxxingAdviceChangedException("This advice request has already been handled.");
            using var update = sqlite.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE LongevitymaxxingAdviceRequests SET Status = @status, Story = @story,
                    PublishedAtUtc = COALESCE(PublishedAtUtc, @published), RankSnapshotJson = @ranks,
                    ActiveAdviceReplyId = NULL, Version = Version + 1, UpdatedAtUtc = @now, PublishHash = @hash
                WHERE Id = @id;
                """;
            Add(update, "@id", advice.Id);
            Add(update, "@status", target);
            Add(update, "@story", target == "published" ? story : null);
            Add(update, "@published", target == "published" ? now.ToString("o") : null);
            Add(update, "@ranks", target == "published" ? JsonSerializer.Serialize(ranks) : advice.Ranks);
            Add(update, "@now", now.ToString("o"));
            Add(update, "@hash", target == "published" ? publishHash : advice.PublishHash);
            update.ExecuteNonQuery();
            SyncAdviceStoryMentions(sqlite, transaction, advice, target == "published" ? story : "", now);
            if (target == "published")
            {
                using var bump = sqlite.CreateCommand();
                bump.Transaction = transaction;
                bump.CommandText = "UPDATE LongevitymaxxingCheckIns SET DiscussionUpdatedAtUtc = @now WHERE ParticipantId = @participant AND ChallengeDay = @day;";
                Add(bump, "@now", now.ToString("o"));
                Add(bump, "@participant", advice.ParticipantId);
                Add(bump, "@day", advice.Day);
                bump.ExecuteNonQuery();
            }
            transaction.Commit();
        });
        return GetAdviceMutationState(request.AccessToken, author.Id, affectedDay, now);
    }

    private LongevitymaxxingParticipantState GetAdviceMutationState(string token, string participantId, int day, DateTimeOffset now)
    {
        var state = GetParticipantState(token, now);
        var advice = _db.Run(sqlite => ReadAdviceRequest(sqlite, participantId: participantId, day: day));
        return advice?.Status is "published" or "withdrawn"
            ? state with { UpdatedDiscussionThread = GetParticipantNotes(publicOnly: false, now, participantId, day).SingleOrDefault() }
            : state;
    }

    private static void SyncAdviceStoryMentions(SqliteConnection sqlite, SqliteTransaction transaction,
        AdviceRequestRecord advice, string story, DateTimeOffset now)
    {
        using var read = sqlite.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT Note FROM LongevitymaxxingCheckIns WHERE ParticipantId = @participant AND ChallengeDay = @day;";
        Add(read, "@participant", advice.ParticipantId);
        Add(read, "@day", advice.Day);
        var note = read.ExecuteScalar() as string;
        var participants = GetConfirmedParticipants(sqlite, transaction);
        var mentioned = ResolveMentionedParticipants($"{note}\n{story}", advice.ParticipantId, participants);
        if (mentioned.Count > MaxMentionsPerCheckIn)
            throw new InvalidOperationException($"A story and its remarks can mention up to {MaxMentionsPerCheckIn} participants.");
        RemovePendingOpeningPostMentionsExcept(sqlite, transaction, advice.ParticipantId, advice.Day,
            mentioned.Select(participant => participant.Id).ToHashSet(StringComparer.Ordinal));
        foreach (var recipient in mentioned)
        {
            using var insert = sqlite.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO LongevitymaxxingDiscussionNotifications
                (Id, RecipientParticipantId, ActorParticipantId, PostParticipantId, PostChallengeDay, Kind, SourceReplyId, CreatedAtUtc, NotifiedAtUtc)
                SELECT @id, @recipient, @participant, @participant, @day, 'mention', NULL, @now, NULL
                WHERE NOT EXISTS (SELECT 1 FROM LongevitymaxxingDiscussionNotifications
                    WHERE RecipientParticipantId = @recipient AND PostParticipantId = @participant
                      AND PostChallengeDay = @day AND Kind = 'mention' AND SourceReplyId IS NULL);
                """;
            Add(insert, "@id", Guid.NewGuid().ToString("N"));
            Add(insert, "@recipient", recipient.Id);
            Add(insert, "@participant", advice.ParticipantId);
            Add(insert, "@day", advice.Day);
            Add(insert, "@now", now.ToString("o"));
            insert.ExecuteNonQuery();
        }
    }

    private static AdviceRequestRecord? ReadAdviceRequest(SqliteConnection sqlite, SqliteTransaction? transaction = null,
        string? id = null, string? participantId = null, int day = 0)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT Id, ParticipantId, ChallengeDay, Status, Story, PublishedAtUtc, RankSnapshotJson, ActiveAdviceReplyId, Version, CharacterLimit, PublishHash
            FROM LongevitymaxxingAdviceRequests
            WHERE (@id IS NOT NULL AND Id = @id) OR (@id IS NULL AND ParticipantId = @participant AND ChallengeDay = @day);
            """;
        Add(command, "@id", id);
        Add(command, "@participant", participantId);
        Add(command, "@day", day);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3),
            reader.IsDBNull(4) ? "" : reader.GetString(4), reader.IsDBNull(5) ? "" : reader.GetString(5), reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt32(8), reader.GetInt32(9), reader.GetString(10)) : null;
    }

    private static (string? RootId, int? Rank) ValidateAdviceReply(SqliteConnection sqlite, SqliteTransaction transaction,
        LongevitymaxxingDiscussionReplyRequest request, string authorId, string replyId, string? replyToId)
    {
        var advice = request.SystemPostId is null ? ReadAdviceRequest(sqlite, transaction,
            participantId: request.PostParticipantId, day: request.ChallengeDay) : null;
        if (advice is null || advice.Status is "pending" or "dismissed")
        {
            if (request.IsAdvice || request.AdviceRootId is not null) throw new InvalidOperationException("This post is not asking for advice.");
            return (null, null);
        }
        if (advice.Status != "published") throw new InvalidOperationException("This advice request has been withdrawn.");
        if (request.IsAdvice)
        {
            if (authorId == advice.ParticipantId) throw new InvalidOperationException("Join the conversation under an advice instead of advising yourself.");
            if (replyToId is not null || request.AdviceRootId is not null) throw new InvalidOperationException("Post new advice directly to the request.");
            using var existing = sqlite.CreateCommand();
            existing.Transaction = transaction;
            existing.CommandText = "SELECT 1 FROM LongevitymaxxingDiscussionReplies WHERE PostParticipantId = @participant AND PostChallengeDay = @day AND AuthorParticipantId = @author AND IsAdvice = 1 AND AdviceDeleted = 0;";
            Add(existing, "@participant", advice.ParticipantId);
            Add(existing, "@day", advice.Day);
            Add(existing, "@author", authorId);
            if (existing.ExecuteScalar() is not null) throw new InvalidOperationException("You have already given advice on this request.");
            var ranks = JsonSerializer.Deserialize<Dictionary<string, int>>(advice.Ranks) ?? [];
            int? rank = ranks.TryGetValue(authorId, out var capturedRank) ? capturedRank : null;
            if (advice.ActiveId is { } activeId &&
                (rank ?? int.MaxValue) >= (ReadAdviceRank(sqlite, activeId, transaction) ?? int.MaxValue))
                throw new LongevitymaxxingAdviceChangedException("Only someone ranked above the current adviser can give better advice. Your draft is kept.");
            return (replyId, rank);
        }
        if (request.AdviceRootId is null || advice.ActiveId != request.AdviceRootId)
            throw new LongevitymaxxingAdviceChangedException("The conversation has moved to newer, higher-ranked advice. Your draft is kept; open the active conversation.");
        if (replyToId is not null)
        {
            using var parent = sqlite.CreateCommand();
            parent.Transaction = transaction;
            parent.CommandText = "SELECT AdviceRootId FROM LongevitymaxxingDiscussionReplies WHERE Id = @id AND PostParticipantId = @participant AND PostChallengeDay = @day;";
            Add(parent, "@id", replyToId);
            Add(parent, "@participant", advice.ParticipantId);
            Add(parent, "@day", advice.Day);
            if (parent.ExecuteScalar() as string != advice.ActiveId) throw new InvalidOperationException("That earlier conversation is archived.");
        }
        return (advice.ActiveId, null);
    }

    private static int? ReadAdviceRank(SqliteConnection sqlite, string replyId, SqliteTransaction? transaction = null)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT AdviceRank FROM LongevitymaxxingDiscussionReplies WHERE Id = @id;";
        Add(command, "@id", replyId);
        var value = command.ExecuteScalar();
        return value is null or DBNull ? null : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private static void PromoteAdvice(SqliteConnection sqlite, SqliteTransaction transaction,
        string participantId, int day, string replyId, int? rank)
    {
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE LongevitymaxxingAdviceRequests SET ActiveAdviceReplyId = @reply, Version = Version + 1
            WHERE ParticipantId = @participant AND ChallengeDay = @day AND Status = 'published'
              AND (ActiveAdviceReplyId IS NULL OR COALESCE(@rank, 2147483647) < COALESCE(
                (SELECT AdviceRank FROM LongevitymaxxingDiscussionReplies WHERE Id = ActiveAdviceReplyId), 2147483647));
            """;
        Add(command, "@participant", participantId);
        Add(command, "@day", day);
        Add(command, "@reply", replyId);
        Add(command, "@rank", rank);
        command.ExecuteNonQuery();
    }

    private static void PopulateAdviceReplyMetadata(SqliteConnection sqlite, List<LongevitymaxxingDiscussionReply> replies)
    {
        if (replies.Count == 0) return;
        using var command = sqlite.CreateCommand();
        var ids = string.Join(",", replies.Select((_, index) => $"@r{index}"));
        command.CommandText = $"""
            SELECT r.Id, r.IsAdvice, r.AdviceRootId, r.AdviceRank, r.AdviceDeleted, a.Status, a.ActiveAdviceReplyId
            FROM LongevitymaxxingDiscussionReplies r
            JOIN LongevitymaxxingAdviceRequests a ON a.ParticipantId = r.PostParticipantId AND a.ChallengeDay = r.PostChallengeDay
            WHERE r.Id IN ({ids}) AND a.Status IN ('published', 'withdrawn');
            """;
        for (var i = 0; i < replies.Count; i++) Add(command, $"@r{i}", replies[i].Id);
        var metadata = new Dictionary<string, (bool Advice, string? Root, int? Rank, bool Deleted, bool Archived)>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
            while (reader.Read())
            {
                var root = reader.IsDBNull(2) ? null : reader.GetString(2);
                var active = reader.IsDBNull(6) ? null : reader.GetString(6);
                metadata[reader.GetString(0)] = (reader.GetInt32(1) == 1, root, reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetInt32(4) == 1, reader.GetString(5) != "published" || root is null || root != active);
            }
        for (var i = 0; i < replies.Count; i++)
            if (metadata.TryGetValue(replies[i].Id, out var value))
                replies[i] = replies[i] with { IsAdvice = value.Advice, AdviceRootId = value.Root, AdviceRank = value.Rank,
                    IsArchived = value.Archived, IsDeleted = value.Deleted, Body = value.Deleted ? "Advice removed by its author." : replies[i].Body };
    }

    private LongevitymaxxingAdviceThread? GetAdviceThread(SqliteConnection sqlite, string participantId, int day)
    {
        var advice = ReadAdviceRequest(sqlite, participantId: participantId, day: day);
        if (advice is null || advice.Status is not ("published" or "withdrawn")) return null;
        using var command = sqlite.CreateCommand();
        // Retain every advice anchor and a small recent conversation per branch. The ordinary
        // thread cursor can load older replies; they are always grouped by their persisted root.
        command.CommandText = """
            WITH ranked AS (
                SELECT r.*, ROW_NUMBER() OVER (PARTITION BY AdviceRootId ORDER BY CreatedAtUtc DESC, Id DESC) AS position
                FROM LongevitymaxxingDiscussionReplies r WHERE PostParticipantId = @participant AND PostChallengeDay = @day
            )
            SELECT r.Id, r.AuthorParticipantId, p.DisplayName, r.Body, r.CreatedAtUtc, r.EditedAtUtc, r.ReplyToId
            FROM ranked r JOIN LongevitymaxxingParticipants p ON p.Id = r.AuthorParticipantId
            WHERE (r.IsAdvice = 1 OR r.position <= 3) AND p.ConfirmedAtUtc IS NOT NULL
            ORDER BY r.CreatedAtUtc, r.Id;
            """;
        Add(command, "@participant", participantId);
        Add(command, "@day", day);
        var replies = new List<LongevitymaxxingDiscussionReply>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) replies.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
        PopulateDiscussionReplyContexts(sqlite, replies);
        var betterAdviceParticipants = Array.Empty<string>();
        if (advice.Status == "published" && advice.ActiveId is { } activeId)
        {
            var activeRank = ReadAdviceRank(sqlite, activeId) ?? int.MaxValue;
            var ranks = JsonSerializer.Deserialize<Dictionary<string, int>>(advice.Ranks) ?? [];
            var alreadyAdvised = replies.Where(reply => reply.IsAdvice && !reply.IsDeleted)
                .Select(reply => reply.ParticipantId).ToHashSet(StringComparer.Ordinal);
            betterAdviceParticipants = ranks.Where(entry => entry.Value < activeRank &&
                    entry.Key != advice.ParticipantId && !alreadyAdvised.Contains(entry.Key))
                .Select(entry => entry.Key).Order(StringComparer.Ordinal).ToArray();
        }
        return new(advice.Id, advice.Status == "withdrawn" ? "" : advice.Story, advice.PublishedAt,
            advice.Status == "withdrawn" ? null : advice.ActiveId, replies)
        { Version = advice.Version, Withdrawn = advice.Status == "withdrawn", StoryCharacterLimit = advice.CharacterLimit,
            BetterAdviceParticipantIds = betterAdviceParticipants };
    }

    private static void RequireAdviceReplyEditable(SqliteConnection sqlite, SqliteTransaction transaction, DiscussionReplyRecord reply)
    {
        if (reply.SystemPostId is not null) return;
        var advice = ReadAdviceRequest(sqlite, transaction, participantId: reply.PostParticipantId, day: reply.PostChallengeDay);
        if (advice is null || advice.Status is "pending" or "dismissed") return;
        using var read = sqlite.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT AdviceRootId, IsAdvice, AdviceDeleted FROM LongevitymaxxingDiscussionReplies WHERE Id = @id;";
        Add(read, "@id", reply.Id);
        using var reader = read.ExecuteReader();
        reader.Read();
        var root = reader.IsDBNull(0) ? null : reader.GetString(0);
        var isAdvice = reader.GetInt32(1) == 1 && reader.GetInt32(2) == 0;
        if (advice.Status != "published" || root is null || root != advice.ActiveId && !isAdvice)
            throw new InvalidOperationException("Archived conversations are read-only. You can still remove your own replies.");
    }

    private sealed record AdviceRequestRecord(string Id, string ParticipantId, int Day, string Status, string Story,
        string PublishedAt, string Ranks, string? ActiveId, int Version, int CharacterLimit, string PublishHash);

    private static bool DeleteAdviceAnchor(SqliteConnection sqlite, SqliteTransaction transaction, DiscussionReplyRecord reply)
    {
        if (reply.SystemPostId is not null) return false;
        using var command = sqlite.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE LongevitymaxxingDiscussionReplies SET Body = '', PhotosJson = '[]', PhotoHash = '', AdviceDeleted = 1
            WHERE Id = @id AND IsAdvice = 1;
            """;
        Add(command, "@id", reply.Id);
        if (command.ExecuteNonQuery() == 0) return false;
        command.CommandText = """
            UPDATE LongevitymaxxingAdviceRequests SET ActiveAdviceReplyId = NULL, Version = Version + 1
            WHERE ActiveAdviceReplyId = @id;
            DELETE FROM LongevitymaxxingDiscussionNotifications WHERE SourceReplyId = @id;
            """;
        command.ExecuteNonQuery();
        return true;
    }
}

public sealed class LongevitymaxxingAdviceChangedException(string message) : InvalidOperationException(message);
