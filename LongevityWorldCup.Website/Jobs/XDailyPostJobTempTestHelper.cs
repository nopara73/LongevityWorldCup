using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private static readonly string[] Top3LeagueSlugs =
    [
        "ultimate",
        "mens",
        "womens",
        "open",
        "silent-generation",
        "baby-boomers",
        "gen-x",
        "millennials",
        "gen-z",
        "gen-alpha",
        "prosperan"
    ];

    public static async Task<bool> TryPostTemporaryTop3LeaderboardTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var candidateLeagues = Top3LeagueSlugs
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        string? leagueSlug = null;
        List<string>? top3 = null;
        foreach (var l in candidateLeagues)
        {
            var picks = athletes.GetTop3SlugsForLeague(l).Take(3).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            if (picks.Count == 0) continue;
            leagueSlug = l;
            top3 = picks;
            break;
        }

        if (string.IsNullOrWhiteSpace(leagueSlug) || top3 is null || top3.Count == 0)
            return false;

        var payload = $"league[{leagueSlug}]";
        var msg = xEvents.TryBuildFillerMessage(FillerType.Top3Leaderboard, payload);
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
            "XDailyPostJob TEMP: posted Top3Leaderboard filler test for league {League} with slugs {Slugs}.",
            leagueSlug,
            string.Join(", ", top3));
        return true;
    }
}
