using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

internal static class ImageTextLayout
{
    internal static Font FitFontToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth, float sizeStep = 2f)
    {
        var size = startSize;
        while (size > minSize)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
                return font;
            size -= sizeStep;
        }

        return family.CreateFont(minSize, FontStyle.Bold);
    }

    internal static void DrawWrappedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        PointF origin,
        float maxWidth,
        int maxLines,
        float lineHeight)
    {
        var lines = WrapText(text, font, maxWidth, maxLines);
        for (var i = 0; i < lines.Count; i++)
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(origin.X, origin.Y + (lineHeight * i)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, lines[i], color);
        }
    }

    private static IReadOnlyList<string> WrapText(string text, Font font, float maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        var truncated = false;
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (TextMeasurer.MeasureSize(candidate, new RichTextOptions(font)).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
                lines.Add(current);
            current = word;
            if (lines.Count >= maxLines)
            {
                truncated = index < words.Length - 1;
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(current) && lines.Count < maxLines)
            lines.Add(current);

        if (truncated && lines.Count == maxLines && words.Length > 0)
        {
            var last = lines[^1];
            while (last.Length > 0 && TextMeasurer.MeasureSize(last + "...", new RichTextOptions(font)).Width > maxWidth)
            {
                last = last[..^1].TrimEnd();
            }
            lines[^1] = last + "...";
        }

        return lines;
    }

    internal static string EllipsizeToWidth(string text, Font font, float maxWidth)
    {
        const string ellipsis = "...";
        if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
            return text;

        var trimmed = text.TrimEnd();
        while (trimmed.Length > 0 &&
               TextMeasurer.MeasureSize(trimmed + ellipsis, new RichTextOptions(font)).Width > maxWidth)
        {
            trimmed = trimmed[..^1].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(trimmed) ? ellipsis : trimmed + ellipsis;
    }
}
