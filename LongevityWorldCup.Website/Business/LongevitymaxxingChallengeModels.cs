namespace LongevityWorldCup.Website.Business;

public sealed record LongevitymaxxingSignupRequest(
    string Email,
    string DisplayName,
    string TimeZoneId,
    string? AthleteLink);

public sealed record LongevitymaxxingCheckInRequest(
    string AccessToken,
    int ChallengeDay,
    int Sleep,
    int Exercise,
    int Nutrition,
    int Vices,
    string? Note,
    string? SubmissionId = null);

public sealed record LongevitymaxxingDiscussionReplyRequest(
    string AccessToken,
    string PostParticipantId,
    int ChallengeDay,
    string Body,
    string ReplyId,
    string? SystemPostId = null,
    string? ReplyToId = null,
    bool IsAdvice = false,
    string? AdviceRootId = null);

public sealed record LongevitymaxxingDiscussionReplyEditRequest(
    string AccessToken,
    string ReplyId,
    string? Body);

public sealed record LongevitymaxxingDiscussionReplyDeleteRequest(
    string AccessToken,
    string ReplyId);

public sealed record LongevitymaxxingDiscussionReplyPageRequest(
    string? AccessToken,
    string PostParticipantId,
    int ChallengeDay,
    string? BeforeCreatedAtUtc,
    string? BeforeReplyId,
    string? SystemPostId = null);

public sealed record LongevitymaxxingDiscussionReplyPage(
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies,
    int TotalCount,
    IReadOnlyList<string> LatestReplyIds,
    int RemainingEarlierReplyCount,
    bool HasEarlier,
    string? NextBeforeCreatedAtUtc,
    string? NextBeforeReplyId)
{
    public LongevitymaxxingDiscussionReadContext? ReadContext { get; init; }
}

public sealed record LongevitymaxxingDiscussionThread(
    LongevitymaxxingParticipantNote? Note,
    LongevitymaxxingDiscussionSystemPost? SystemPost)
{
    public LongevitymaxxingDiscussionReadContext? ReadContext { get; init; }
}

public sealed record LongevitymaxxingDiscussionThreadKey(string ParticipantId, int ChallengeDay, string? SystemPostId = null);
public sealed record LongevitymaxxingDiscussionNotification(string Id, string? SourceReplyId, long CreatedRevision);
public sealed record LongevitymaxxingDiscussionUnreadThread(
    string ParticipantId, int ChallengeDay, string? SystemPostId, int UnreadCount,
    string? FirstUnreadReplyId, bool HasOpeningMention, long LatestRelevantAtUnixMs,
    IReadOnlyList<LongevitymaxxingDiscussionNotification> Notifications);
public sealed record LongevitymaxxingDiscussionReadContext(long ActivityRevision,
    IReadOnlyList<LongevitymaxxingDiscussionUnreadThread> Threads);
public sealed record LongevitymaxxingDiscussionReadRequest(string AccessToken, string PostParticipantId,
    int ChallengeDay, string? SystemPostId, long ObservedRevision, IReadOnlyList<string> NotificationIds);
public sealed record LongevitymaxxingDiscussionReadResult(IReadOnlyList<string> AcknowledgedIds,
    LongevitymaxxingDiscussionReadContext ReadContext);
public sealed record LongevitymaxxingDiscussionThreadRequest(string AccessToken, string PostParticipantId,
    int ChallengeDay, string? SystemPostId = null);
public sealed record LongevitymaxxingDiscussionFeedSnapshot(
    IReadOnlyList<LongevitymaxxingDiscussionThreadKey> Keys, LongevitymaxxingDiscussionFeedCursor? NextCursor);

public sealed record LongevitymaxxingDiscussionFeedCursor(
    long RankedAtUnixMs,
    double Score,
    long ActivityAtUnixMs,
    int ChallengeDay,
    string ParticipantId,
    string? SystemPostId,
    bool NeedsAdvice = false,
    long? RankingRevision = null,
    long? ReadSnapshotRevision = null,
    bool HasPersonalUnread = false,
    long LatestRelevantAtUnixMs = 0);

public sealed record LongevitymaxxingDiscussionFeedRequest(
    string? AccessToken,
    LongevitymaxxingDiscussionFeedCursor? Before = null);

public sealed record LongevitymaxxingDiscussionFeedPage(
    IReadOnlyList<LongevitymaxxingDiscussionThread> Threads,
    LongevitymaxxingDiscussionFeedCursor? NextCursor)
{
    public LongevitymaxxingDiscussionReadContext? ReadContext { get; init; }
}

public sealed record LongevitymaxxingParticipantEditRequest(
    string AccessToken,
    string TimeZoneId,
    string? DisplayName = null,
    string? AthleteLink = null);

public sealed record LongevitymaxxingPublicState(
    string ChallengeName,
    string Phase,
    bool SignupOpen,
    string StartDate,
    string SignupClosesAtUtc,
    string EndDate,
    int DurationDays,
    int DailyMaxScore,
    IReadOnlyList<LongevitymaxxingDaySummary> Days,
    IReadOnlyList<LongevitymaxxingLeaderboardRow> Leaderboard,
    IReadOnlyList<LongevitymaxxingPodiumRow> Podium,
    IReadOnlyList<LongevitymaxxingParticipantNote> Notes,
    IReadOnlyList<LongevitymaxxingDiscussionSystemPost> SystemDiscussionPosts,
    string SlackInviteUrl,
    string? SlackRoomUrl,
    LongevitymaxxingScoringWindow ScoringWindow)
{
    public LongevitymaxxingDiscussionFeedSnapshot? DiscussionSnapshot { get; init; }
}

public sealed record LongevitymaxxingScoringWindow(int StartDay, int EndDay, string NextClosesAtUtc);

public sealed record LongevitymaxxingParticipantState(
    LongevitymaxxingPublicState Public,
    LongevitymaxxingParticipantSummary Participant,
    IReadOnlyList<LongevitymaxxingEligibleDay> EligibleDays,
    IReadOnlyList<LongevitymaxxingParticipantNote> Notes,
    LongevitymaxxingGardenState Garden)
{
    public LongevitymaxxingAdvicePrompt? PendingAdvicePrompt { get; init; }
    public LongevitymaxxingParticipantNote? UpdatedDiscussionThread { get; init; }
    public IReadOnlyList<LongevitymaxxingDiscussionSystemPost> SystemDiscussionPosts { get; init; } = [];
    public LongevitymaxxingDiscussionFeedSnapshot? DiscussionSnapshot { get; init; }
    public LongevitymaxxingDiscussionReadContext? ReadContext { get; init; }
}

public sealed record LongevitymaxxingGardenState(
    int CheckedInDays,
    LongevitymaxxingGardenHabitState Sleep,
    LongevitymaxxingGardenHabitState Exercise,
    LongevitymaxxingGardenHabitState Nutrition,
    LongevitymaxxingGardenHabitState Vices);

public sealed record LongevitymaxxingGardenHabitState(int YesCount, int NoCount, double Vitality)
{
    public const double YesGrowthRate = 0.025d;
    public const double NoRetentionRate = 0.65d;

    public static double ApplyAnswer(double vitality, int answer)
    {
        var current = Math.Clamp(vitality, 0d, 1d);
        return answer switch
        {
            2 => current + ((1d - current) * YesGrowthRate),
            0 => current * NoRetentionRate,
            _ => current
        };
    }
}

public sealed record LongevitymaxxingSignupResult(string Message);

public sealed record LongevitymaxxingAccessResult(string AccessToken, LongevitymaxxingParticipantState State);

public sealed record LongevitymaxxingParticipantSummary(
    string Id,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string? AthleteSlug,
    string? AthleteUrl,
    string? ProfileImageUrl,
    bool ChallengeEmailsStopped,
    bool ChallengeInactive,
    int DaysIn);

public sealed record LongevitymaxxingDaySummary(int ChallengeDay, string Date);

public sealed record LongevitymaxxingLeaderboardRow(
    string ParticipantId,
    string DisplayName,
    string? AthleteUrl,
    string? ProfileImageUrl,
    int CheckedInDays,
    int TotalPoints,
    int CurrentStreak,
    IReadOnlyList<LongevitymaxxingDayCell> Cells,
    IReadOnlyList<string> Badges,
    string? LatestCheckInAtUtc,
    bool ChallengeEmailsStopped,
    bool ChallengeInactive)
{
    public int Rank { get; init; }
    public bool HasFullMarks { get; init; }
    public LongevitymaxxingCharacterAllowance CharacterAllowance { get; init; } = new(0, 0);
}

public sealed record LongevitymaxxingCharacterAllowance(int ChallengePoints, int UltimateLeagueAthletesBehind)
{
    public const int BaseCharacters = 146;
    public int Limit => BaseCharacters + Math.Clamp(ChallengePoints, 0, 154) + Math.Max(0, UltimateLeagueAthletesBehind);
}

public sealed record LongevitymaxxingDayCell(
    int ChallengeDay,
    bool CheckedIn,
    int? Score,
    bool CountsForScore,
    int? Sleep,
    int? Exercise,
    int? Nutrition,
    int? Vices);

public sealed record LongevitymaxxingPodiumRow(int Placement, string DisplayName, string? AthleteUrl, string? ProfileImageUrl, int CheckedInDays, int TotalPoints);

public sealed record LongevitymaxxingEligibleDay(
    int ChallengeDay,
    string Date,
    bool CountsForScore,
    LongevitymaxxingCheckInDraft? Existing);

public sealed record LongevitymaxxingCheckInDraft(
    int Sleep,
    int Exercise,
    int Nutrition,
    int Vices,
    string? Note,
    IReadOnlyList<LongevitymaxxingCheckInImage> Images);

public sealed record LongevitymaxxingParticipantNote(
    string ParticipantId,
    string DisplayName,
    int ChallengeDay,
    string Date,
    string? Note,
    string UpdatedAtUtc,
    string LastActivityAtUtc,
    int ReplyCount,
    IReadOnlyList<LongevitymaxxingCheckInImage> Images,
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies)
{
    public LongevitymaxxingAdviceThread? Advice { get; init; }
}

public sealed record LongevitymaxxingDiscussionReply(
    string Id,
    string ParticipantId,
    string DisplayName,
    string Body,
    string CreatedAtUtc,
    string? EditedAtUtc,
    string? ReplyToId = null,
    LongevitymaxxingDiscussionReplyContext? ReplyTo = null)
{
    public IReadOnlyList<LongevitymaxxingCheckInImage> Images { get; init; } = [];
    public bool IsAdvice { get; init; }
    public string? AdviceRootId { get; init; }
    public int? AdviceRank { get; init; }
    public bool IsArchived { get; init; }
    public bool IsDeleted { get; init; }
}

public sealed record LongevitymaxxingAdvicePrompt(
    string Id, int ChallengeDay, string Date, int RawScore, double? BaselineRawScore, int CharacterLimit, int Version)
{
    public string? CheckInNote { get; init; }
}

public sealed record LongevitymaxxingAdviceStoryRequest(
    string AccessToken, string AdviceId, int Version, string? Story = null);

public sealed record LongevitymaxxingAdviceThread(
    string Id, string Story, string PublishedAtUtc, string? ActiveAdviceReplyId,
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies)
{
    public int Version { get; init; }
    public bool Withdrawn { get; init; }
    public int StoryCharacterLimit { get; init; }
    public IReadOnlyList<string> BetterAdviceParticipantIds { get; init; } = [];
}

public sealed record LongevitymaxxingDiscussionReplyContext(string DisplayName, string Body);

public sealed record LongevitymaxxingDiscussionSystemPost(
    string Id,
    string Kind,
    string ParticipantId,
    string DisplayName,
    string Date,
    string OccurredAtUtc,
    string LastActivityAtUtc,
    int ReplyCount,
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies)
{
    public LongevitymaxxingDiscussionAnnouncement? Announcement { get; init; }
}

public sealed record LongevitymaxxingDiscussionAnnouncement(string EventId, string Title, string Content);

public sealed record LongevitymaxxingCheckInImage(
    string Url,
    int Width,
    int Height);

public sealed record LongevitymaxxingReminderCandidate(
    string ParticipantId,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string AccessToken,
    string StopToken,
    int ChallengeDay,
    string TargetDate,
    bool CountsForScore,
    LongevitymaxxingDiscussionDigest DiscussionDigest);

public sealed record LongevitymaxxingDiscussionDigest(
    int MentionCount,
    int ReplyCount,
    IReadOnlyList<LongevitymaxxingDiscussionDigestItem> Items,
    IReadOnlyList<string> NotificationIds)
{
    public int TotalCount => MentionCount + ReplyCount;

    public static LongevitymaxxingDiscussionDigest Empty { get; } = new(0, 0, [], []);
}

public sealed record LongevitymaxxingDiscussionDigestItem(
    LongevitymaxxingDiscussionActivityKind Kind,
    int ChallengeDay,
    string Date,
    int Count,
    IReadOnlyList<string> ActorDisplayNames,
    string? SystemPostKind = null,
    string? PostDisplayName = null);

public enum LongevitymaxxingDiscussionActivityKind
{
    Mention,
    Reply
}

public sealed record LongevitymaxxingChallengeStartCandidate(
    string ParticipantId,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string AccessToken,
    string StopToken);

public sealed record LongevitymaxxingChallengeResultEventRow(
    string ParticipantId,
    string DisplayName,
    string? AthleteSlug,
    int Placement,
    int CheckedInDays,
    int TotalPoints,
    bool Completed,
    int DurationDays,
    DateTime OccurredAtUtc);
