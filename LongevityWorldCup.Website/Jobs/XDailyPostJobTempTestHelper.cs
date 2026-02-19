using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    // Keep this list aligned with AthleteDataService.DetectAndEmitAthleteCountMilestones().
    private static readonly int[] AthleteCountMilestones =
    [
        10,
        42, 69, 100, 123,
        256, 300, 404, 500, 666, 777,
        1000, 1337, 1618,
        2000, 3141,
        5000, 6969, 9001, 10000
    ];

    public static async Task<bool> TryPostTemporaryAthleteCountMilestoneTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        ILogger logger)
    {
        if (AthleteCountMilestones.Length == 0)
            return false;

        var randomIndex = Random.Shared.Next(0, AthleteCountMilestones.Length);
        var count = AthleteCountMilestones[randomIndex];
        var rawText = $"athletes[{count}]";

        var msg = xEvents.TryBuildMessage(EventType.AthleteCountMilestone, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        await xEvents.SendAsync(msg);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted AthleteCountMilestone test for count {Count}.",
            count);
        return true;
    }
}
