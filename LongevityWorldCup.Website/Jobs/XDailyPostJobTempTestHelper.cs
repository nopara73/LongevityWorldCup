using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private const string TempBadgeLabel = "Chronological Age - Oldest";

    public static async Task<bool> TryPostTemporaryBadgeAwardChronologicalAgeOldestTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var oldestSlug = athletes
            .GetAthletesForX()
            .Where(a => !string.IsNullOrWhiteSpace(a.Slug) && a.ChronoAge.HasValue)
            .OrderByDescending(a => a.ChronoAge!.Value)
            .ThenBy(a => a.Slug, StringComparer.OrdinalIgnoreCase)
            .Select(a => a.Slug)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(oldestSlug))
            return false;

        var rawText = $"slug[{oldestSlug}] badge[{TempBadgeLabel}] cat[Global] val[] place[1]";
        var msg = xEvents.TryBuildMessage(EventType.BadgeAward, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var mediaIds = await XDailyPostMediaHelper.TryBuildMediaIdsAsync(EventType.BadgeAward, rawText, images, xApiClient);
        await xEvents.SendAsync(msg, mediaIds);

        logger.LogInformation(
            "XDailyPostJob TEMP: posted BadgeAward test for {BadgeLabel} on slug {Slug}.",
            TempBadgeLabel,
            oldestSlug);
        return true;
    }
}
