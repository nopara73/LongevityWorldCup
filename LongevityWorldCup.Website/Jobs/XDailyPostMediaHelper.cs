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
        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var winnerSlug))
                return null;
            if (!EventHelpers.TryExtractPrev(rawText, out var prevSlug))
                return null;

            await using var imageStream = await images.BuildNewRankImageAsync(winnerSlug, prevSlug);
            return await UploadSinglePngAsync(imageStream, xApiClient);
        }

        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0)
                return null;

            await using var imageStream = await images.BuildAthleteCountMilestoneImageAsync(count);
            return await UploadSinglePngAsync(imageStream, xApiClient);
        }

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;
        var normalized = EventHelpers.NormalizeBadgeLabel(label);
        var isSupportedBadge =
            string.Equals(normalized, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase);
        if (!isSupportedBadge)
            return null;
        if (!EventHelpers.TryExtractSlug(rawText, out var slug))
            return null;

        await using var singleImageStream = await images.BuildSingleAthleteImageAsync(slug);
        return await UploadSinglePngAsync(singleImageStream, xApiClient);
    }

    private static async Task<IReadOnlyList<string>?> UploadSinglePngAsync(Stream? imageStream, XApiClient xApiClient)
    {
        if (imageStream == null)
            return null;

        var mediaId = await xApiClient.UploadMediaAsync(imageStream, "image/png");
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        return new[] { mediaId };
    }
}
