using System.Text;

namespace LongevityWorldCup.Website.Tools;

public enum CustomEventTextStyle
{
    Regular,
    Bold,
    Strong
}

public sealed record CustomEventSegment(string Text, CustomEventTextStyle Style);

public static class CustomEventMarkup
{
    public static string NormalizeNewlines(string? text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    public static (string Title, string Content) SplitTitleAndContent(string? rawText)
    {
        var s = NormalizeNewlines(rawText).TrimEnd();
        var idx = s.IndexOf("\n\n", StringComparison.Ordinal);
        if (idx >= 0)
            return (s[..idx].TrimEnd(), s[(idx + 2)..]);

        return (s, string.Empty);
    }

    public static bool ContainsHyperlink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        return ContainsHyperlinkCore(text, 0, text.Length);
    }

    public static string? GetSingleHyperlink(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;

        var links = new List<string>(capacity: 2);
        CollectHyperlinks(text, 0, text.Length, links);
        return links.Count == 1 ? links[0] : null;
    }

    public static IReadOnlyList<CustomEventSegment> ParseSegments(string? text, bool keepHyperlinkLabels, Func<string, string>? mentionResolver = null)
    {
        var output = new List<CustomEventSegment>();
        var normalized = NormalizeNewlines(text);
        ParseInto(output, normalized, 0, normalized.Length, CustomEventTextStyle.Regular, keepHyperlinkLabels, mentionResolver);
        return MergeAdjacent(output);
    }

    public static string ToPlainText(string? text, bool keepHyperlinkLabels = true, Func<string, string>? mentionResolver = null)
    {
        var segments = ParseSegments(text, keepHyperlinkLabels, mentionResolver);
        var sb = new StringBuilder();
        foreach (var segment in segments)
            sb.Append(segment.Text);
        return sb.ToString();
    }

    private static bool ContainsHyperlinkCore(string text, int start, int length)
    {
        var end = start + length;
        var i = start;
        while (i < end)
        {
            var open = text.IndexOf('[', i);
            if (open < 0 || open >= end)
                return false;

            var close = text.IndexOf("](", open + 1, StringComparison.Ordinal);
            if (close < 0 || close >= end)
                return false;

            var label = text[(open + 1)..close];
            var parenStart = close + 2;
            var j = parenStart;
            var depth = 1;
            while (j < end && depth > 0)
            {
                var ch = text[j];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                j++;
            }

            if (depth != 0)
                return false;

            var inner = text.Substring(parenStart, j - parenStart - 1);
            var key = label.Trim().ToLowerInvariant();
            if (key == "bold" || key == "strong")
            {
                if (ContainsHyperlinkCore(inner, 0, inner.Length))
                    return true;
            }
            else if (IsSafeHttpUrl(inner))
            {
                return true;
            }

            i = j;
        }

        return false;
    }

    private static void CollectHyperlinks(string text, int start, int length, List<string> links)
    {
        var end = start + length;
        var i = start;
        while (i < end)
        {
            var open = text.IndexOf('[', i);
            if (open < 0 || open >= end)
                return;

            var close = text.IndexOf("](", open + 1, StringComparison.Ordinal);
            if (close < 0 || close >= end)
                return;

            var label = text[(open + 1)..close];
            var parenStart = close + 2;
            var j = parenStart;
            var depth = 1;
            while (j < end && depth > 0)
            {
                var ch = text[j];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                j++;
            }

            if (depth != 0)
                return;

            var inner = text.Substring(parenStart, j - parenStart - 1);
            var key = label.Trim().ToLowerInvariant();
            if (key == "bold" || key == "strong")
            {
                CollectHyperlinks(inner, 0, inner.Length, links);
            }
            else if (IsSafeHttpUrl(inner))
            {
                links.Add(inner.Trim());
                if (links.Count > 1)
                    return;
            }

            i = j;
        }
    }

    private static void ParseInto(List<CustomEventSegment> output, string text, int start, int length, CustomEventTextStyle inheritedStyle, bool keepHyperlinkLabels, Func<string, string>? mentionResolver)
    {
        var end = start + length;
        var i = start;
        while (i < end)
        {
            var open = text.IndexOf('[', i);
            if (open < 0 || open >= end)
            {
                Append(output, text[i..end], inheritedStyle);
                return;
            }

            if (open > i)
                Append(output, text[i..open], inheritedStyle);

            var close = text.IndexOf("](", open + 1, StringComparison.Ordinal);
            if (close < 0 || close >= end)
            {
                Append(output, text[open..end], inheritedStyle);
                return;
            }

            var label = text[(open + 1)..close];
            var parenStart = close + 2;
            var j = parenStart;
            var depth = 1;
            while (j < end && depth > 0)
            {
                var ch = text[j];
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                j++;
            }

            if (depth != 0)
            {
                Append(output, text[open..end], inheritedStyle);
                return;
            }

            var inner = text.Substring(parenStart, j - parenStart - 1);
            var key = label.Trim().ToLowerInvariant();
            if (key == "bold")
            {
                ParseInto(output, inner, 0, inner.Length, CustomEventTextStyle.Bold, keepHyperlinkLabels, mentionResolver);
            }
            else if (key == "strong")
            {
                ParseInto(output, inner, 0, inner.Length, CustomEventTextStyle.Strong, keepHyperlinkLabels, mentionResolver);
            }
            else if (key == "mention")
            {
                Append(output, ResolveMentionText(inner, mentionResolver), inheritedStyle);
            }
            else if (IsSafeHttpUrl(inner))
            {
                if (keepHyperlinkLabels)
                    Append(output, label, inheritedStyle);
            }
            else
            {
                Append(output, text[open..j], inheritedStyle);
            }

            i = j;
        }
    }

    private static bool IsSafeHttpUrl(string? value)
    {
        return Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static void Append(List<CustomEventSegment> output, string text, CustomEventTextStyle style)
    {
        if (string.IsNullOrEmpty(text))
            return;

        output.Add(new CustomEventSegment(text, style));
    }

    private static string ResolveMentionText(string? slug, Func<string, string>? mentionResolver)
    {
        var normalizedSlug = (slug ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSlug))
            return string.Empty;

        var resolved = mentionResolver?.Invoke(normalizedSlug);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        return HumanizeSlug(normalizedSlug);
    }

    private static string HumanizeSlug(string slug)
    {
        var spaced = slug.Replace('_', ' ').Replace('-', ' ').Trim();
        if (string.IsNullOrWhiteSpace(spaced))
            return slug;

        return string.Join(' ', spaced
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private static IReadOnlyList<CustomEventSegment> MergeAdjacent(List<CustomEventSegment> segments)
    {
        if (segments.Count == 0)
            return Array.Empty<CustomEventSegment>();

        var merged = new List<CustomEventSegment>(segments.Count);
        foreach (var segment in segments)
        {
            if (merged.Count == 0)
            {
                merged.Add(segment);
                continue;
            }

            var prev = merged[^1];
            if (prev.Style != segment.Style)
            {
                merged.Add(segment);
                continue;
            }

            merged[^1] = prev with { Text = prev.Text + segment.Text };
        }

        return merged;
    }
}
