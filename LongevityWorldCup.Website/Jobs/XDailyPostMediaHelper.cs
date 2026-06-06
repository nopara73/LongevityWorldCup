using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Jobs;

internal static class XDailyPostMediaHelper
{
    public static async Task<IReadOnlyList<string>?> TryBuildMediaIdsAsync(
        EventType type,
        string rawText,
        XImageService images,
        XApiClient xApiClient,
        AthleteCountMilestoneMemeService milestoneMemes)
    {
        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var winnerSlug))
                return null;
            if (!EventHelpers.TryExtractPrev(rawText, out var prevSlug))
                return null;

            await using var imageStream = await images.BuildNewRankImageAsync(winnerSlug, prevSlug);
            return await UploadSingleAsync(imageStream, "image/png", xApiClient);
        }

        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0)
                return null;

            if (milestoneMemes.TryGetMeme(count, out var meme))
            {
                await using var memeStream = File.OpenRead(meme.FullPath);
                return await UploadSingleAsync(memeStream, meme.ContentType, xApiClient);
            }

            await using var imageStream = await images.BuildAthleteCountMilestoneImageAsync(count);
            return await UploadSingleAsync(imageStream, "image/png", xApiClient);
        }

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;
        var normalized = EventHelpers.NormalizeBadgeLabel(label);
        var isNonGlobalLeagueLeader =
            EventHelpers.TryExtractPlace(rawText, out var place) &&
            place == 1 &&
            EventHelpers.TryExtractCategory(rawText, out var category) &&
            !string.Equals(category, "Global", StringComparison.OrdinalIgnoreCase);
        var isSupportedBadge =
            string.Equals(normalized, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Chronological age – oldest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Chronological age – youngest", StringComparison.OrdinalIgnoreCase) ||
            (isNonGlobalLeagueLeader && string.Equals(normalized, "Age reduction", StringComparison.OrdinalIgnoreCase));
        if (!isSupportedBadge)
            return null;
        if (!EventHelpers.TryExtractSlug(rawText, out var slug))
            return null;

        await using var singleImageStream = await images.BuildSingleAthleteImageAsync(slug);
        return await UploadSingleAsync(singleImageStream, "image/png", xApiClient);
    }

    private static async Task<IReadOnlyList<string>?> UploadSingleAsync(Stream? imageStream, string contentType, XApiClient xApiClient)
    {
        if (imageStream == null)
            return null;

        var mediaId = await xApiClient.UploadMediaAsync(imageStream, contentType);
        if (string.IsNullOrWhiteSpace(mediaId))
            return null;

        return new[] { mediaId };
    }
}
