using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostMediaHelper
{
    public static async Task<IReadOnlyList<string>?> TryBuildMediaIdsAsync(
        EventType type,
        string rawText,
        XImageService images,
        XApiClient xApiClient)
    {
        if (type != EventType.NewRank)
            return null;

        if (!EventHelpers.TryExtractSlug(rawText, out var winnerSlug))
            return null;
        if (!EventHelpers.TryExtractPrev(rawText, out var prevSlug))
            return null;

        await using var imageStream = await images.BuildNewRankImageAsync(winnerSlug, prevSlug);
        if (imageStream == null)
            return null;

        var mediaId = await xApiClient.UploadMediaAsync(imageStream, "image/png");
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        return new[] { mediaId };
    }
}
