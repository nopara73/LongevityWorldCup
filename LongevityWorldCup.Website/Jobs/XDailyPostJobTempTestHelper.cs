using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private const string TempWinnerSlug = "juan_robalino";
    private const string TempPrevSlug = "michael_lustgarten";

    public static async Task<bool> TryPostTemporaryNewRankTestAsync(
        EventDataService events,
        XEventService xEvents,
        XImageService images,
        XApiClient xApiClient,
        ILogger logger)
    {
        var rawText = $"slug[{TempWinnerSlug}] rank[1] prev[{TempPrevSlug}]";

        events.CreateNewRankEvents(
            new[] { (TempWinnerSlug, DateTime.UtcNow, 1, (string?)TempPrevSlug) },
            skipIfExists: false);
        logger.LogInformation("XDailyPostJob TEMP: inserted NewRank test event for {Winner} over {Previous}.", TempWinnerSlug, TempPrevSlug);

        var msg = xEvents.TryBuildMessage(EventType.NewRank, rawText);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        var mediaIds = await XDailyPostMediaHelper.TryBuildMediaIdsAsync(EventType.NewRank, rawText, images, xApiClient);
        await xEvents.SendAsync(msg, mediaIds);

        var pendingIds = events
            .GetPendingXEvents(limit: 500)
            .Where(e => e.Type == EventType.NewRank && string.Equals(e.Text, rawText, StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToList();
        if (pendingIds.Count > 0)
            events.MarkEventsXProcessed(pendingIds);

        logger.LogInformation("XDailyPostJob TEMP: posted NewRank test event.");
        return true;
    }

}
