namespace LongevityWorldCup.Website.Business;

public sealed record LongevitymaxxingSignupRequest(
    string Email,
    string DisplayName,
    string TimeZoneId,
    string? AthleteLink,
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? CallAvailability,
    decimal? CommitmentAmountUsd = null);

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
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection>? CallAvailability,
    decimal? CommitmentAmountUsd = null);

public sealed record LongevitymaxxingCommitmentPaymentRequest(string AccessToken);

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
    IReadOnlyList<LongevitymaxxingCallAvailabilitySelection> CallAvailability,
    LongevitymaxxingCommitmentState Commitment,
    LongevitymaxxingCommitmentTrendGuidance TrendGuidance);

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
    decimal? CommitmentAmountUsd);

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
    string? CommitmentStatus);

public sealed record LongevitymaxxingCommitmentState(
    string Status,
    bool BlocksParticipant,
    bool CanEditAmount,
    bool CanPay,
    decimal? AmountUsd,
    decimal? OwedAmountUsd,
    int? TriggerChallengeDay,
    int? TriggerScore,
    decimal? ThresholdAverage,
    string? InvoiceId,
    string? CheckoutLink,
    string? InvoiceStatus,
    string? Message);

public sealed record LongevitymaxxingCommitmentTrendGuidance(
    bool Enforced,
    int PriorScoredDays,
    decimal? AveragePoints,
    int? NeededPoints,
    string Text);

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
    bool CountsForScore,
    bool IncludeCallScheduleUpdate,
    IReadOnlyList<LongevitymaxxingParticipantCall> Calls,
    bool IsCommitmentPaymentReminder = false,
    decimal? CommitmentOwedAmountUsd = null,
    int? CommitmentTriggerChallengeDay = null);

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
