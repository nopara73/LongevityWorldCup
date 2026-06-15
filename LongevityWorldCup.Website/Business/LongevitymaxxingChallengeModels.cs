namespace LongevityWorldCup.Website.Business;

public sealed record LongevitymaxxingSignupRequest(
    string Email,
    string DisplayName,
    string TimeZoneId,
    string? AthleteLink,
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? CallAvailability);

public sealed record LongevitymaxxingCallAvailabilitySelection(string CallKey, string SlotId);

public sealed record LongevitymaxxingCheckInRequest(
    string AccessToken,
    int ChallengeDay,
    int Sleep,
    int Exercise,
    int Nutrition,
    int Vices,
    string? Note);

public sealed record LongevitymaxxingParticipantEditRequest(
    string AccessToken,
    string DisplayName,
    string TimeZoneId,
    string? AthleteLink,
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? CallAvailability);

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
    IReadOnlyList<LongevitymaxxingPublicCall> Calls,
    string SlackInviteUrl,
    string? SlackRoomUrl);

public sealed record LongevitymaxxingParticipantState(
    LongevitymaxxingPublicState Public,
    LongevitymaxxingParticipantSummary Participant,
    IReadOnlyList<LongevitymaxxingEligibleDay> EligibleDays,
    IReadOnlyList<LongevitymaxxingPrivateNote> Notes,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls,
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection> CallAvailability);

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
    bool ChallengeEmailsStopped);

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
    string? LatestCheckInAtUtc);

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

public sealed record LongevitymaxxingPrivateNote(
    string ParticipantId,
    string DisplayName,
    int ChallengeDay,
    string Date,
    string? Note,
    string UpdatedAtUtc,
    IReadOnlyList<LongevitymaxxingCheckInImage> Images);

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
    bool IncludeCallScheduleUpdate,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls);

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
