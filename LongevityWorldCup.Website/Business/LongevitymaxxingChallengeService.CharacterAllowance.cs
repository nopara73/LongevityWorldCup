namespace LongevityWorldCup.Website.Business;

public sealed partial class LongevitymaxxingChallengeService
{
    private LongevitymaxxingCharacterAllowance GetCharacterAllowance(ParticipantRecord participant, DateTimeOffset now)
    {
        var checkIns = GetCheckInsFor(new HashSet<string>(StringComparer.Ordinal) { participant.Id });
        return GetCharacterAllowance(BuildSettings(), participant, checkIns.GetValueOrDefault(participant.Id) ?? [], now);
    }

    private LongevitymaxxingCharacterAllowance GetCharacterAllowance(
        ChallengeSettings settings,
        ParticipantRecord participant,
        IReadOnlyDictionary<int, CheckInRecord> byDay,
        DateTimeOffset now)
    {
        var window = GetLeaderboardScoringWindow(settings, now);
        var points = FilterLeaderboardPerformanceDays(byDay, window.StartDay, window.EndDay)
            .Values.Sum(checkIn => GetScoredPoints(settings, participant, checkIn, byDay));
        var athlete = GetAthleteTieBreak(BuildAthleteTieBreaks(), participant.AthleteSlug);
        return new(points, athlete.AthletesBehind);
    }
}
