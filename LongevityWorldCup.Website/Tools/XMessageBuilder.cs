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
        return "";
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }
}
