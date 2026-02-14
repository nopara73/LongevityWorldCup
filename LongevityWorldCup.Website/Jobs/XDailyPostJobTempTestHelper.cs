using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private static readonly string[] DomainKeys = ["liver", "kidney", "metabolic", "inflammation", "immune"];

    public static async Task<bool> TryPostTemporaryDomainTopTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var randomizedDomains = DomainKeys.OrderBy(_ => Random.Shared.Next()).ToList();
        string? selectedDomain = null;
        string? winnerSlug = null;
        foreach (var domain in randomizedDomains)
        {
            var winner = athletes.GetBestDomainWinnerSlug(domain);
            if (string.IsNullOrWhiteSpace(winner))
                continue;
            selectedDomain = domain;
            winnerSlug = winner;
            break;
        }

        if (string.IsNullOrWhiteSpace(selectedDomain) || string.IsNullOrWhiteSpace(winnerSlug))
            return false;

        var payload = $"domain[{selectedDomain}]";
        var msg = xEvents.TryBuildFillerMessage(FillerType.DomainTop, payload);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        IReadOnlyList<string>? mediaIds = null;
        await using var imageStream = await images.BuildSingleAthleteImageAsync(winnerSlug);
        if (imageStream != null)
        {
            var mediaId = await xApiClient.UploadMediaAsync(imageStream, "image/png");
            if (!string.IsNullOrWhiteSpace(mediaId))
                mediaIds = new[] { mediaId };
        }

        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted DomainTop filler test for domain {Domain} winner {Winner}.",
            selectedDomain,
            winnerSlug);
        return true;
    }
}
