using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private const string TempBadgeLabel = "League Leader";
    private static readonly (string LeagueSlug, string Category, string Value)[] NonGlobalLeagues =
    [
        ("mens", "Division", "Men's"),
        ("womens", "Division", "Women's"),
        ("open", "Division", "Open"),
        ("silent-generation", "Generation", "Silent Generation"),
        ("baby-boomers", "Generation", "Baby Boomers"),
        ("gen-x", "Generation", "Gen X"),
        ("millennials", "Generation", "Millennials"),
        ("gen-z", "Generation", "Gen Z"),
        ("gen-alpha", "Generation", "Gen Alpha"),
        ("prosperan", "Exclusive", "Prosperan")
    ];

    public static async Task<bool> TryPostTemporaryBadgeAwardNonGlobalLeagueLeaderTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var picks = NonGlobalLeagues
            .OrderBy(_ => Random.Shared.Next())
            .ToList();

        (string LeagueSlug, string Category, string Value)? selectedLeague = null;
        List<string>? top3 = null;
        foreach (var candidate in picks)
        {
            var currentTop3 = athletes.GetTop3SlugsForLeague(candidate.LeagueSlug)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Take(3)
                .ToList();
            if (currentTop3.Count == 0)
                continue;

            selectedLeague = candidate;
            top3 = currentTop3;
            break;
        }

        if (!selectedLeague.HasValue || top3 is null || top3.Count == 0)
            return false;

        var slug1 = top3[0];
        var slug2 = top3.Count > 1 ? top3[1] : null;
        var league = selectedLeague.Value;
        var rawText = slug2 is null
            ? $"slug[{slug1}] badge[{TempBadgeLabel}] cat[{league.Category}] val[{league.Value}] place[1]"
            : $"slug[{slug1}] badge[{TempBadgeLabel}] cat[{league.Category}] val[{league.Value}] place[1] prev[{slug2}]";

        var msg = xEvents.TryBuildMessage(EventType.BadgeAward, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var mediaIds = await XDailyPostMediaHelper.TryBuildMediaIdsAsync(EventType.BadgeAward, rawText, images, xApiClient);
        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted BadgeAward non-global #1 test for league {LeagueSlug}, #1 {Slug1}, #2 {Slug2}.",
            league.LeagueSlug,
            slug1,
            slug2 ?? "");
        return true;
    }
}
