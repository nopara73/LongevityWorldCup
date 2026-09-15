namespace LongevityWorldCup.Website.Business;

public static class CrowdAgeMilestonePolicy
{
    public static readonly TimeSpan CollectionWindow = TimeSpan.FromHours(1);

    public static bool IsMilestone(int place, int? previousPlace) =>
        place is >= 1 and <= 10 &&
        (previousPlace is null ||
         (place <= 3 && previousPlace is >= 1 and <= 10 && place < previousPlace));
}
