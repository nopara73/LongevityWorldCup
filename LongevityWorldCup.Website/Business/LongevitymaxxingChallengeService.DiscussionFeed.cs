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
             !double.IsFinite(before.Score) || before.ChallengeDay < 0 ||
             !Guid.TryParse(before.ParticipantId, out _) ||
             (before.SystemPostId is not null && !Guid.TryParse(before.SystemPostId, out _))))
            throw new InvalidOperationException("That discussion feed cursor is invalid.");

        // A fixed ranking time prevents time passing between pages from moving the cursor.
        var rankedAt = DateTimeOffset.FromUnixTimeMilliseconds(before?.RankedAtUnixMs ?? now.ToUnixTimeMilliseconds());
        return _db.Run(sqlite =>
        {
            var hasAccess = !string.IsNullOrWhiteSpace(request.AccessToken);
            if (hasAccess) _ = RequireParticipantByAccessToken(request.AccessToken!);
            var notes = GetParticipantNotes(!hasAccess, rankedAt, limit: DiscussionFeedPageSize + 1, before: before);
            var systemPosts = GetSystemDiscussionPosts(rankedAt, limit: DiscussionFeedPageSize + 1, before: before);
            var threads = notes.Select(note => new LongevitymaxxingDiscussionThread(note, null))
                .Concat(systemPosts.Select(post => new LongevitymaxxingDiscussionThread(null, post)))
                .Select(thread => (Thread: thread, Position: DiscussionFeedPosition(thread, rankedAt)))
                .OrderBy(item => item.Position, DiscussionFeedOrder)
                .Take(DiscussionFeedPageSize + 1)
                .ToList();
            var page = threads.Take(DiscussionFeedPageSize).ToList();
            return new LongevitymaxxingDiscussionFeedPage(page.Select(item => item.Thread).ToList(),
                threads.Count > DiscussionFeedPageSize ? page[^1].Position : null);
        });
    }

    private static LongevitymaxxingDiscussionFeedCursor DiscussionFeedPosition(
        LongevitymaxxingDiscussionThread thread, DateTimeOffset rankedAt)
    {
        if (thread.Note is { } note)
            return DiscussionFeedPosition(rankedAt, note.ReplyCount,
                ParseDateTimeOffset(note.LastActivityAtUtc, DateTimeOffset.UnixEpoch), note.ChallengeDay, note.ParticipantId,
                needsAdvice: note.Advice is { Withdrawn: false, ActiveAdviceReplyId: null });
        var post = thread.SystemPost!;
        return DiscussionFeedPosition(rankedAt, post.ReplyCount,
            ParseDateTimeOffset(post.LastActivityAtUtc, DateTimeOffset.UnixEpoch), 0, post.ParticipantId, post.Id);
    }

    private static LongevitymaxxingDiscussionFeedCursor DiscussionFeedPosition(
        DateTimeOffset rankedAt, int replyCount, DateTimeOffset activityAt, int day, string participantId,
        string? systemPostId = null, bool needsAdvice = false)
    {
        rankedAt = DateTimeOffset.FromUnixTimeMilliseconds(rankedAt.ToUnixTimeMilliseconds());
        var activityMs = activityAt.ToUnixTimeMilliseconds();
        var score = CalculateDiscussionHotScore(replyCount, DateTimeOffset.FromUnixTimeMilliseconds(activityMs), rankedAt);
        return new(rankedAt.ToUnixTimeMilliseconds(), Math.Floor(score * 1e9 + 0.5) / 1e9, activityMs, day, participantId, systemPostId, needsAdvice);
    }

    private static int CompareDiscussionFeedPositions(LongevitymaxxingDiscussionFeedCursor left, LongevitymaxxingDiscussionFeedCursor right)
    {
        var order = right.NeedsAdvice.CompareTo(left.NeedsAdvice);
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
