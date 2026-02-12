using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private static readonly string[] TempNewcomerPool =
    [
        "alexander",
        "andrea",
        "arielv",
        "brandon",
        "daniel_snow",
        "deelicious",
        "juan_robalino",
        "kelvin_nicholson",
        "michael_lustgarten",
        "sergey_vlasov"
    ];

    public static async Task<bool> TryPostTemporaryNewcomersTestAsync(
        EventDataService _,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var count = Random.Shared.Next(2, 9);
        var picks = TempNewcomerPool
            .OrderBy(_ => Random.Shared.Next())
            .Take(Math.Min(count, TempNewcomerPool.Length))
            .ToList();
        if (picks.Count == 0)
            return false;

        var msg = BuildTemporaryNewcomersMessage(picks);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        IReadOnlyList<string>? mediaIds = null;
        await using var imageStream = await images.BuildNewcomersImageAsync(picks);
        if (imageStream != null)
        {
            var mediaId = await xApiClient.UploadMediaAsync(imageStream, "image/png");
            if (!string.IsNullOrWhiteSpace(mediaId))
                mediaIds = new[] { mediaId };
        }

        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted Newcomers filler test with {Count} slugs: {Slugs}",
            picks.Count,
            string.Join(", ", picks));
        return true;
    }

    private static string BuildTemporaryNewcomersMessage(IReadOnlyList<string> slugs)
    {
        static string Pretty(string slug)
        {
            var raw = (slug ?? "").Replace('_', ' ').Replace('-', ' ').Trim();
            if (raw.Length == 0) return "";
            var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
        }

        var names = slugs
            .Select(Pretty)
            .Where(n => n.Length > 0)
            .Take(8)
            .ToList();
        if (names.Count == 0)
            return "";

        return
            "Fresh faces on the Longevity World Cup leaderboard\n\n" +
            string.Join(", ", names) + "\n\n" +
            "View all athletes: https://longevityworldcup.com/leaderboard";
    }
}
