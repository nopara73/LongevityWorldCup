using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

public static partial class MastodonPost
{
    public const int MaxCharacters = 500;
    private const int UrlCharacters = 23;

    // Mastodon counts grapheme clusters and gives recognized URLs a fixed length.
    public static int Count(string text) => new StringInfo(Urls().Replace(text, match =>
    {
        var url = match.Value.TrimEnd('.', ',', '!', '?', ';', ':', ']', '}');
        while (url.EndsWith(')') && url.Count(c => c == ')') > url.Count(c => c == '('))
            url = url[..^1];
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !(uri.Host.Contains('.') || IPAddress.TryParse(uri.Host, out _)))
            return match.Value;
        return new string('x', UrlCharacters) + match.Value[url.Length..];
    })).LengthInTextElements;

    public static string Truncate(string text, int limit)
    {
        if (Count(text) <= limit) return text;
        if (limit <= 0) return string.Empty;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var low = 0;
        var high = starts.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            var end = mid == starts.Length ? text.Length : starts[mid];
            if (Count(text[..end].TrimEnd() + "…") <= limit) low = mid;
            else high = mid - 1;
        }
        return text[..(low == starts.Length ? text.Length : starts[low])].TrimEnd() + "…";
    }

    [GeneratedRegex("https?://[^\\s<>\"']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Urls();
}
