using LongevityWorldCup.Website.Business;
namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private const string TempSlugA = "juan-robalino";
    private const string TempSlugB = "deelicious";

    public static async Task<bool> TryPostTemporaryPvpDuelTestAsync(
        EventDataService _,
        XEventService xEvents,
        XImageService __,
        XApiClient ___,
        ILogger logger)
    {
        var (sent, infoToken) = await xEvents.TrySendPvpDuelThreadWithInfoTokenAsync(
            null,
            TempSlugA,
            TempSlugB);

        if (!sent)
            return false;

        logger.LogInformation(
            "XDailyPostJob TEMP: posted PvP filler duel thread for pair {SlugA} vs {SlugB}. InfoToken: {InfoToken}",
            TempSlugA,
            TempSlugB,
            infoToken);
        return true;
    }

}
