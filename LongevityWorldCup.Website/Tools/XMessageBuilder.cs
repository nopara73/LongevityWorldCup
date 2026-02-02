using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class XMessageBuilder
{
    private const int MaxLength = 280;
    private const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, string?>? getPodcastLinkForSlug = null)
    {
        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0) return "";
            var countLabel = count.ToString("N0", CultureInfo.InvariantCulture);
            var text =
                $"Longevity World Cup just hit {countLabel} athletes on the leaderboard 🏁\n" +
                $"📊 View the board: {LeaderboardUrl}";
            return Truncate(text);
        }

        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank < 1 || rank > 3) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var slug)) return "";
            var current = slugToName(slug);
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;

            string text;
            if (!string.IsNullOrWhiteSpace(prev))
                text = $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} overtakes {prev} for the spot.\n📊 Leaderboard: {LeaderboardUrl}";
            else
                text = $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} takes #{rank}.\n📊 Leaderboard: {LeaderboardUrl}";
            return Truncate(text);
        }

        if (type != EventType.BadgeAward) return "";

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return "";
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);
        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase)) return "";

        if (!EventHelpers.TryExtractSlug(rawText, out var slug)) return "";

        var guest = slugToName(slug);
        var podcastUrl = getPodcastLinkForSlug?.Invoke(slug);
        if (string.IsNullOrWhiteSpace(podcastUrl)) return "";

        const string host = "@nopara73";

        var text =
            $"New Longevity World Cup podcast 🎧\n" +
            $"{host} sits down with {guest} for a full conversation on the show.\n" +
            $"📹 Full episode: {podcastUrl}";

        return Truncate(text);
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }
}
