using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostJobTempTestHelper
{
    private static readonly string[] DomainTopKeys =
    [
        "liver",
        "kidney",
        "metabolic",
        "inflammation",
        "immune"
    ];

    public static async Task<bool> TryPostTemporaryFillerDomainTopTestAsync(
        EventDataService _,
        AthleteDataService athletes,
        XEventService xEvents,
        ILogger logger)
    {
        if (DomainTopKeys.Length == 0)
        {
            logger.LogWarning("XDailyPostJob TEMP: no domain configured for DomainTop test.");
            return true;
        }

        var start = Random.Shared.Next(DomainTopKeys.Length);

        for (var i = 0; i < DomainTopKeys.Length; i++)
        {
            var key = DomainTopKeys[(start + i) % DomainTopKeys.Length];
            var payload = $"domain[{key}]";
            var msg = xEvents.TryBuildFillerMessage(FillerType.DomainTop, payload);
            if (string.IsNullOrWhiteSpace(msg))
                continue;

            await xEvents.SendAsync(msg);
            logger.LogInformation(
                "XDailyPostJob TEMP: posted filler test (DomainTop) with payload {Payload}.",
                payload);
            return true;
        }

        logger.LogWarning("XDailyPostJob TEMP: failed to build filler test message (DomainTop) for all configured domains.");
        return true;
    }
}
