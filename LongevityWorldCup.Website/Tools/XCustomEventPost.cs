using System.Globalization;
using System.Text;

namespace LongevityWorldCup.Website.Tools;

internal static class XCustomEventPost
{
    private const int MaxCharacters = 280;
    private const int ShortenedUrlCharacters = 23;

    public static CustomEventSocialPlan BuildPlan(
        string eventId, string rawText, Func<string, string>? mentionResolver = null, bool includeEventUrl = true)
    {
        var sourceUrl = CustomEventMarkup.GetSingleHyperlink(rawText);
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") || !uri.Host.Contains('.'))
        {
            sourceUrl = null;
        }

        return CustomEventSocialComposer.BuildPlan(eventId, rawText, MaxCharacters, mentionResolver,
            includeEventUrl, Count, Truncate);

        int Count(string text)
        {
            // The composer appends an explicit source link on its own line.
            if (sourceUrl is not null)
            {
                if (text.Equals(sourceUrl, StringComparison.Ordinal))
                    return ShortenedUrlCharacters;
                if (text.EndsWith("\n\n" + sourceUrl, StringComparison.Ordinal))
                    return CountText(text[..^sourceUrl.Length]) + ShortenedUrlCharacters;
            }

            return CountText(text);
        }
    }

    private static int CountText(string text)
    {
        // X's published character weights. Counting the individual code points
        // of a joined emoji sequence gives a conservative upper bound.
        int count = 0;
        foreach (var rune in text.Normalize(NormalizationForm.FormC).EnumerateRunes())
        {
            count += rune.Value is <= 0x10FF or >= 0x2000 and <= 0x200D
                or >= 0x2010 and <= 0x201F or >= 0x2032 and <= 0x2037 ? 1 : 2;
        }

        return count;
    }

    private static string Truncate(string text, int limit)
    {
        if (CountText(text) <= limit)
            return text;

        const string ellipsis = "…";
        int remaining = limit - CountText(ellipsis);
        if (remaining < 0)
            return string.Empty;

        var elements = StringInfo.GetTextElementEnumerator(text);
        int end = 0;
        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            remaining -= CountText(element);
            if (remaining < 0)
                break;
            end = elements.ElementIndex + element.Length;
        }

        return text[..end].TrimEnd() + ellipsis;
    }
}
