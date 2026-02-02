using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class XMessageBuilder
{
    private const int MaxLength = 280;

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, string?>? getPodcastLinkForSlug = null)
    {
        if (type != EventType.BadgeAward) return "";

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return "";
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);
        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase)) return "";

        if (!EventHelpers.TryExtractSlug(rawText, out var slug)) return "";

        var name = slugToName(slug);
        var podcastUrl = getPodcastLinkForSlug?.Invoke(slug);

        string text;
        if (!string.IsNullOrWhiteSpace(podcastUrl))
            text = $"{name} just released a new Longevity World Cup podcast episode: {podcastUrl}";
        else
            text = $"{name} just released a new Longevity World Cup podcast episode.";

        return Truncate(text);
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }
}
