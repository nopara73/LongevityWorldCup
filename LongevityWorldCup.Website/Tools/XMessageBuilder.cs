using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class XMessageBuilder
{
    private const int MaxLength = 280;
    private const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    private static string AthleteUrl(string slug) =>
        $"https://longevityworldcup.com/athlete/{slug.Replace('_', '-')}";

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, string?>? getPodcastLinkForSlug = null,
        Func<string, double?>? getLowestPhenoAgeForSlug = null,
        Func<string, double?>? getChronoAgeForSlug = null)
    {
        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0) return "";
            var countLabel = count.ToString("N0", CultureInfo.InvariantCulture);
            return Truncate(
                $"Longevity World Cup just hit {countLabel} athletes on the leaderboard 🏁\n" +
                $"📊 View the board: {LeaderboardUrl}");
        }

        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank < 1 || rank > 3) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var rankSlug)) return "";
            var current = slugToName(rankSlug);
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            var newRankMsg = !string.IsNullOrWhiteSpace(prev)
                ? $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} overtakes {prev} for the spot.\n📊 Leaderboard: {LeaderboardUrl}"
                : $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} takes #{rank}.\n📊 Leaderboard: {LeaderboardUrl}";
            return Truncate(newRankMsg);
        }

        if (type != EventType.BadgeAward) return "";

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return "";
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);

        if (string.Equals(normLabel, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var phenoSlug)) return "";
            var phenoAthlete = slugToName(phenoSlug);
            var phenoAge = getLowestPhenoAgeForSlug?.Invoke(phenoSlug);
            var ageStr = phenoAge.HasValue ? $" at {phenoAge.Value.ToString("0.#", CultureInfo.InvariantCulture)} years" : "";
            var athleteUrl = AthleteUrl(phenoSlug);
            return Truncate(
                $"Lowest biological age in the Longevity World Cup field 🧬\n" +
                $"{phenoAthlete} holds it{ageStr}.\n" +
                $"📊 Profile: {athleteUrl}");
        }

        if (string.Equals(normLabel, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} just became the oldest in the Longevity World Cup field at {ageStr} 🏃‍♂️\n📊 Profile: {url}");
        }

        if (string.Equals(normLabel, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} just became the youngest in the Longevity World Cup field at {ageStr} 🏃‍♂️\n📊 Profile: {url}");
        }

        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase)) return "";

        if (!EventHelpers.TryExtractSlug(rawText, out var guestSlug)) return "";

        var guest = slugToName(guestSlug);
        var podcastUrl = getPodcastLinkForSlug?.Invoke(guestSlug);
        if (string.IsNullOrWhiteSpace(podcastUrl)) return "";

        const string host = "@nopara73";

        return Truncate(
            $"New Longevity World Cup podcast 🎧\n" +
            $"{host} sits down with {guest} for a full conversation on the show.\n" +
            $"📹 Full episode: {podcastUrl}");
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }
}
