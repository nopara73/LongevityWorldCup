using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    public static async Task<bool> TryPostTemporaryAthleteCountMilestoneTestAsync(
        EventDataService _,
        AthleteDataService __,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var athleteCount = Random.Shared.Next(25, 25001);
        var rawText = $"athletes[{athleteCount}]";
        var msg = xEvents.TryBuildMessage(EventType.AthleteCountMilestone, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var mediaIds = await XDailyPostMediaHelper.TryBuildMediaIdsAsync(EventType.AthleteCountMilestone, rawText, images, xApiClient);
        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted AthleteCountMilestone test with count {AthleteCount}.",
            athleteCount);
        return true;
    }
}
