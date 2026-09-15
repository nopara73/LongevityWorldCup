namespace LongevityWorldCup.Website.Business;

public static class CrowdAgeAnnouncementPolicy
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(24);

    public static bool IsEligibleChange(int place, int? previousPlace) =>
        place is >= 1 and <= 10 &&
        (previousPlace is null ||
         (previousPlace is >= 1 and <= 10 && place < previousPlace));
}
