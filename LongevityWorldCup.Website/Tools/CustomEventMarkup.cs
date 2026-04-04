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

    public static IReadOnlyList<CustomEventSegment> ParseSegments(string? text, bool keepHyperlinkLabels)
    {
        var output = new List<CustomEventSegment>();
        ParseInto(output, NormalizeNewlines(text), 0, NormalizeNewlines(text).Length, CustomEventTextStyle.Regular, keepHyperlinkLabels);
        return MergeAdjacent(output);
    }

    public static string ToPlainText(string? text, bool keepHyperlinkLabels = true)
    {
        var segments = ParseSegments(text, keepHyperlinkLabels);
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

    private static void ParseInto(List<CustomEventSegment> output, string text, int start, int length, CustomEventTextStyle inheritedStyle, bool keepHyperlinkLabels)
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
                ParseInto(output, inner, 0, inner.Length, CustomEventTextStyle.Bold, keepHyperlinkLabels);
            }
            else if (key == "strong")
            {
                ParseInto(output, inner, 0, inner.Length, CustomEventTextStyle.Strong, keepHyperlinkLabels);
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
