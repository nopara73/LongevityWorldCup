using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public sealed record LeaderboardSnapshot(IReadOnlyList<LeaderboardSnapshotRow> Rows);

public sealed record LeaderboardSnapshotRow(
    int Rank,
    string Slug,
    string RouteSlug,
    string DisplayName,
    string AthletePath,
    string AthleteUrl,
    string Track,
    string Tier,
    double? EffectiveAgeReductionYears,
    double? LowestBortzAge,
    double? LowestPhenoAge,
    double? ChronologicalAge,
    string Division,
    string Generation,
    string Flag,
    string ExclusiveLeague,
    string MediaContact,
    string? LeaderboardThumbnailUrl);

public static class LeaderboardSnapshotBuilder
{
    private const string DefaultSiteBaseUrl = "https://longevityworldcup.com";
    private static readonly Regex EmailLike = new(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.Compiled);

    public static LeaderboardSnapshot Build(JsonArray rankedRows, JsonArray athletesSnapshot, string siteBaseUrl = DefaultSiteBaseUrl)
    {
        var normalizedSiteBaseUrl = string.IsNullOrWhiteSpace(siteBaseUrl)
            ? DefaultSiteBaseUrl
            : siteBaseUrl.TrimEnd('/');
        var extraBySlug = athletesSnapshot
            .OfType<JsonObject>()
            .Select(BuildExtra)
            .Where(extra => !string.IsNullOrWhiteSpace(extra.Slug))
            .ToDictionary(extra => extra.Slug, StringComparer.OrdinalIgnoreCase);

        var rows = new List<LeaderboardSnapshotRow>();
        var rank = 0;
        foreach (var node in rankedRows.OfType<JsonObject>())
        {
            var slug = GetString(node, "AthleteSlug");
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            rank++;
            extraBySlug.TryGetValue(slug, out var extra);
            extra ??= AthleteExtra.Empty;
            var routeSlug = slug.Replace('_', '-');
            var athletePath = $"/athlete/{routeSlug}";
            var displayName = FirstNonEmpty(extra.DisplayName, GetString(node, "Name"), slug);
            var hasBortz = TryGetDouble(node, "LowestBortzAge", out var lowestBortzAge);
            var track = hasBortz ? "Pro" : "Amateur";

            TryGetDouble(node, "LowestPhenoAge", out var lowestPhenoAge);
            TryGetDouble(node, "ChronologicalAge", out var chronologicalAge);
            TryGetDouble(node, "AgeDifference", out var ageDifference);

            rows.Add(new LeaderboardSnapshotRow(
                Rank: rank,
                Slug: slug,
                RouteSlug: routeSlug,
                DisplayName: displayName,
                AthletePath: athletePath,
                AthleteUrl: $"{normalizedSiteBaseUrl}{athletePath}",
                Track: track,
                Tier: track.ToLowerInvariant(),
                EffectiveAgeReductionYears: ageDifference,
                LowestBortzAge: hasBortz ? lowestBortzAge : null,
                LowestPhenoAge: lowestPhenoAge,
                ChronologicalAge: chronologicalAge,
                Division: extra.Division,
                Generation: extra.Generation,
                Flag: extra.Flag,
                ExclusiveLeague: extra.ExclusiveLeague,
                MediaContact: FilterMediaContact(extra.MediaContact),
                LeaderboardThumbnailUrl: FirstNonEmpty(extra.ProfilePicLeaderboardThumb, extra.ProfilePicThumb, extra.ProfilePic)));
        }

        return new LeaderboardSnapshot(rows);
    }

    private static AthleteExtra BuildExtra(JsonObject athlete)
    {
        var slug = GetString(athlete, "AthleteSlug");
        return new AthleteExtra(
            Slug: slug,
            DisplayName: FirstNonEmpty(GetString(athlete, "DisplayName"), GetString(athlete, "Name"), slug),
            Division: GetString(athlete, "Division"),
            Generation: GenerationResolver.ResolveFromAthleteJson(athlete) ?? "",
            Flag: GetString(athlete, "Flag"),
            ExclusiveLeague: GetString(athlete, "ExclusiveLeague"),
            MediaContact: GetString(athlete, "MediaContact"),
            ProfilePic: GetString(athlete, "ProfilePic"),
            ProfilePicThumb: GetString(athlete, "ProfilePicThumb"),
            ProfilePicLeaderboardThumb: GetString(athlete, "ProfilePicLeaderboardThumb"));
    }

    private static string FilterMediaContact(string mediaContact)
    {
        if (string.IsNullOrWhiteSpace(mediaContact))
        {
            return "";
        }

        var trimmed = mediaContact.Trim();
        return EmailLike.IsMatch(trimmed) ? "" : trimmed;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";
    }

    private static string GetString(JsonObject obj, string propertyName)
    {
        return obj[propertyName] is JsonValue value && value.TryGetValue<string>(out var result)
            ? SanitizeText(result)
            : "";
    }

    private static string SanitizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value
                .Replace("\r", " ", StringComparison.Ordinal)
                .Replace("\n", " ", StringComparison.Ordinal)
                .Trim();
    }

    private static bool TryGetDouble(JsonObject obj, string propertyName, out double value)
    {
        value = 0;
        return obj[propertyName] is JsonValue jsonValue &&
               jsonValue.TryGetValue<double>(out value) &&
               !double.IsNaN(value) &&
               !double.IsInfinity(value);
    }

    private sealed record AthleteExtra(
        string Slug,
        string DisplayName,
        string Division,
        string Generation,
        string Flag,
        string ExclusiveLeague,
        string MediaContact,
        string ProfilePic,
        string ProfilePicThumb,
        string ProfilePicLeaderboardThumb)
    {
        public static readonly AthleteExtra Empty = new("", "", "", "", "", "", "", "", "", "");
    }
}
