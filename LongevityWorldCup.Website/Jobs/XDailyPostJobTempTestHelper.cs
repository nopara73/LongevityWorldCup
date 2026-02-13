using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    public static async Task<bool> TryPostTemporaryCrowdGuessesTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var top3 = athletes.GetCrowdLowestAgeTop3()
            .Select(x => x.Slug)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(3)
            .ToList();
        if (top3.Count == 0)
            return false;

        var msg = xEvents.TryBuildFillerMessage(FillerType.CrowdGuesses, "");
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        IReadOnlyList<string>? mediaIds = null;
        await using var imageStream = await images.BuildTop3LeaderboardPodiumImageAsync(top3);
        if (imageStream != null)
        {
            var mediaId = await xApiClient.UploadMediaAsync(imageStream, "image/png");
            if (!string.IsNullOrWhiteSpace(mediaId))
                mediaIds = new[] { mediaId };
        }

        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted CrowdGuesses filler test with slugs {Slugs}.",
            string.Join(", ", top3));
        return true;
    }
}
