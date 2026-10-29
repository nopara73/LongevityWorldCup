namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    private const int DiscussionFeedPageSize = 10;
    private static readonly IComparer<LongevitymaxxingDiscussionFeedCursor> DiscussionFeedOrder =
        Comparer<LongevitymaxxingDiscussionFeedCursor>.Create(CompareDiscussionFeedPositions);

    public LongevitymaxxingDiscussionFeedPage GetDiscussionFeed(
        LongevitymaxxingDiscussionFeedRequest request, DateTimeOffset? nowUtc = null)
    {
        var now = EnsureUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var before = request.Before;
        if (before is not null &&
            (before.RankedAtUnixMs < 0 || before.RankedAtUnixMs > now.AddMinutes(5).ToUnixTimeMilliseconds() ||
             before.ActivityAtUnixMs < 0 || before.ActivityAtUnixMs > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() ||
             !double.IsFinite(before.Score) || before.ChallengeDay < 0 || before.RankingRevision < 0 ||
             before.ReadSnapshotRevision < 0 || before.LatestRelevantAtUnixMs < 0 ||
             !Guid.TryParse(before.ParticipantId, out _) ||
             (before.SystemPostId is not null && !Guid.TryParse(before.SystemPostId, out _))))
            throw new InvalidOperationException("That discussion feed cursor is invalid.");

        return _db.Run(sqlite =>
        {
            var viewer = string.IsNullOrWhiteSpace(request.AccessToken) ? null : RequireParticipantByAccessToken(request.AccessToken).Id;
            return BuildDiscussionFeedWindow(viewer, now, DiscussionFeedPageSize, before);
        });
    }

    private LongevitymaxxingDiscussionFeedPage BuildDiscussionFeedWindow(string? viewer, DateTimeOffset now,
        int limit, LongevitymaxxingDiscussionFeedCursor? before = null)
        => _db.Run(sqlite => {
            var versions = DiscussionVersions(sqlite);
            if (before is not null && (before.RankingRevision is null || before.ReadSnapshotRevision is null))
                throw new LongevitymaxxingDiscussionFeedChangedException();
            if (before?.RankingRevision is { } expected && expected != versions.Ranking)
                throw new LongevitymaxxingDiscussionFeedChangedException();
            if (before?.ReadSnapshotRevision > versions.Activity)
                throw new InvalidOperationException("That discussion feed cursor is invalid.");
            var snapshot = before?.ReadSnapshotRevision ?? versions.Activity;
            var rankedAt = DateTimeOffset.FromUnixTimeMilliseconds(before?.RankedAtUnixMs ?? now.ToUnixTimeMilliseconds());
            var unread = viewer is null ? null : UnreadPositions(ReadDiscussionNotifications(sqlite, viewer, snapshot));
            var notes = GetParticipantNotes(viewer is null, rankedAt, limit: limit + 1, before: before, unread: unread);
            var systemPosts = GetSystemDiscussionPosts(rankedAt, limit: limit + 1, before: before, unread: unread);
            var threads = notes.Select(note => new LongevitymaxxingDiscussionThread(note, null))
                .Concat(systemPosts.Select(post => new LongevitymaxxingDiscussionThread(null, post)))
                .Select(thread => (Thread: thread, Position: DiscussionFeedPosition(thread, rankedAt, unread) with {
                    RankingRevision = versions.Ranking, ReadSnapshotRevision = snapshot }))
                .OrderBy(item => item.Position, DiscussionFeedOrder)
                .Take(limit + 1)
                .ToList();
            var page = threads.Take(limit).ToList();
            return new LongevitymaxxingDiscussionFeedPage(page.Select(item => item.Thread).ToList(),
                threads.Count > limit ? page[^1].Position : null)
            { ReadContext = viewer is null ? null : BuildDiscussionReadContext(sqlite, viewer, page.Select(item => item.Thread).ToList()) };
        });

    private static LongevitymaxxingDiscussionFeedSnapshot DiscussionSnapshot(LongevitymaxxingDiscussionFeedPage page)
        => new(page.Threads.Select(ThreadKey).ToList(), page.NextCursor);

    private static long? PersonalUnreadAt(IReadOnlyDictionary<LongevitymaxxingDiscussionThreadKey, long>? unread,
        string participant, int day, string? systemPost = null)
        => unread is not null && unread.TryGetValue(new(participant, day, systemPost), out var at) ? at : null;

    private static LongevitymaxxingDiscussionFeedCursor DiscussionFeedPosition(
        LongevitymaxxingDiscussionThread thread, DateTimeOffset rankedAt,
        IReadOnlyDictionary<LongevitymaxxingDiscussionThreadKey, long>? unread = null)
    {
        if (thread.Note is { } note)
            return DiscussionFeedPosition(rankedAt, note.ReplyCount,
                ParseDateTimeOffset(note.LastActivityAtUtc, DateTimeOffset.UnixEpoch), note.ChallengeDay, note.ParticipantId,
                needsAdvice: note.Advice is { Withdrawn: false, ActiveAdviceReplyId: null },
                personalUnreadAt: PersonalUnreadAt(unread, note.ParticipantId, note.ChallengeDay));
        var post = thread.SystemPost!;
        return DiscussionFeedPosition(rankedAt, post.ReplyCount,
            ParseDateTimeOffset(post.LastActivityAtUtc, DateTimeOffset.UnixEpoch), 0, post.ParticipantId, post.Id,
            personalUnreadAt: PersonalUnreadAt(unread, post.ParticipantId, 0, post.Id));
    }

    private static LongevitymaxxingDiscussionFeedCursor DiscussionFeedPosition(
        DateTimeOffset rankedAt, int replyCount, DateTimeOffset activityAt, int day, string participantId,
        string? systemPostId = null, bool needsAdvice = false, long? personalUnreadAt = null)
    {
        rankedAt = DateTimeOffset.FromUnixTimeMilliseconds(rankedAt.ToUnixTimeMilliseconds());
        var activityMs = activityAt.ToUnixTimeMilliseconds();
        var score = CalculateDiscussionHotScore(replyCount, DateTimeOffset.FromUnixTimeMilliseconds(activityMs), rankedAt);
        return new(rankedAt.ToUnixTimeMilliseconds(), Math.Floor(score * 1e9 + 0.5) / 1e9, activityMs, day, participantId,
            systemPostId, needsAdvice, HasPersonalUnread: personalUnreadAt.HasValue, LatestRelevantAtUnixMs: personalUnreadAt ?? 0);
    }

    private static int CompareDiscussionFeedPositions(LongevitymaxxingDiscussionFeedCursor left, LongevitymaxxingDiscussionFeedCursor right)
    {
        var order = right.HasPersonalUnread.CompareTo(left.HasPersonalUnread);
        if (order != 0) return order;
        if (left.HasPersonalUnread && right.HasPersonalUnread) {
            order = right.LatestRelevantAtUnixMs.CompareTo(left.LatestRelevantAtUnixMs);
            if (order != 0) return order;
        }
        order = right.NeedsAdvice.CompareTo(left.NeedsAdvice);
        if (order != 0) return order;
        order = right.Score.CompareTo(left.Score);
        if (order != 0) return order;
        order = right.ActivityAtUnixMs.CompareTo(left.ActivityAtUnixMs);
        if (order != 0) return order;
        order = right.ChallengeDay.CompareTo(left.ChallengeDay);
        if (order != 0) return order;
        order = string.Compare(left.ParticipantId, right.ParticipantId, StringComparison.Ordinal);
        return order != 0 ? order : string.Compare(left.SystemPostId, right.SystemPostId, StringComparison.Ordinal);
    }
}

public sealed class LongevitymaxxingDiscussionFeedChangedException() : InvalidOperationException("The discussion feed has changed.");
