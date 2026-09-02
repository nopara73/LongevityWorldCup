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
    string? Note);

public sealed record LongevitymaxxingDiscussionReplyRequest(
    string AccessToken,
    string PostParticipantId,
    int ChallengeDay,
    string Body,
    string ReplyId);

public sealed record LongevitymaxxingDiscussionReplyEditRequest(
    string AccessToken,
    string ReplyId,
    string Body);

public sealed record LongevitymaxxingDiscussionReplyDeleteRequest(
    string AccessToken,
    string ReplyId);

public sealed record LongevitymaxxingDiscussionReplyPageRequest(
    string? AccessToken,
    string PostParticipantId,
    int ChallengeDay,
    string? BeforeCreatedAtUtc,
    string? BeforeReplyId);

public sealed record LongevitymaxxingDiscussionReplyPage(
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies,
    int TotalCount,
    int RemainingEarlierReplyCount,
    bool HasEarlier,
    string? NextBeforeCreatedAtUtc,
    string? NextBeforeReplyId);

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
    string CallSelectionClosesAtUtc,
    string EndDate,
    int DurationDays,
    int DailyMaxScore,
    IReadOnlyList<LongevitymaxxingDaySummary> Days,
    IReadOnlyList<LongevitymaxxingLeaderboardRow> Leaderboard,
    IReadOnlyList<LongevitymaxxingPodiumRow> Podium,
    IReadOnlyList<LongevitymaxxingParticipantNote> Notes,
    IReadOnlyList<LongevitymaxxingPublicCall> Calls,
    string SlackInviteUrl,
    string? SlackRoomUrl);

public sealed record LongevitymaxxingParticipantState(
    LongevitymaxxingPublicState Public,
    LongevitymaxxingParticipantSummary Participant,
    IReadOnlyList<LongevitymaxxingEligibleDay> EligibleDays,
    IReadOnlyList<LongevitymaxxingParticipantNote> Notes,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls,
    LongevitymaxxingGardenState Garden);

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
    bool ChallengeInactive);

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

public sealed record LongevitymaxxingPublicCall(
    string Key,
    string Label,
    IReadOnlyList<LongevitymaxxingCallSlot> CandidateSlots,
    LongevitymaxxingCallSlot? SelectedSlot);

public sealed record LongevitymaxxingParticipantCall(
    string Key,
    string Label,
    LongevitymaxxingCallSlot? SelectedSlot,
    string? VideoCallUrl);

public sealed record LongevitymaxxingCallSlot(string Id, string StartsAtUtc);

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
    IReadOnlyList<LongevitymaxxingDiscussionReply> Replies);

public sealed record LongevitymaxxingDiscussionReply(
    string Id,
    string ParticipantId,
    string DisplayName,
    string Body,
    string CreatedAtUtc,
    string? EditedAtUtc);

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
    bool IncludeCallScheduleUpdate,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls,
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
    IReadOnlyList<string> ActorDisplayNames);

public enum LongevitymaxxingDiscussionActivityKind
{
    Mention,
    Reply
}

public sealed record LongevitymaxxingCallReminderCandidate(
    string ParticipantId,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string AccessToken,
    string StopToken,
    string CallKey,
    string CallLabel,
    string StartsAtUtc,
    string ReminderKind,
    string? VideoCallUrl,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls);

public sealed record LongevitymaxxingCallAnnouncementCandidate(
    string CallKey,
    string CallLabel,
    string StartsAtUtc,
    string ReminderKind,
    string VideoCallUrl);

public sealed record LongevitymaxxingChallengeStartCandidate(
    string ParticipantId,
    string Email,
    string DisplayName,
    string TimeZoneId,
    string AccessToken,
    string StopToken,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls);

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
