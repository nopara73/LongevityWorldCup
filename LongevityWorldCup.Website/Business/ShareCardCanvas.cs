using System.Globalization;
using System.Collections.Concurrent;
using System.Text;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace LongevityWorldCup.Website.Business;

/// <summary>
/// The shared frame of every link-preview card (athlete, league and page): one masthead,
/// one background, one closing accent bar and the site's Roboto/Orbitron type, so a feed
/// of Longevity World Cup links reads as a single family.
/// </summary>
internal static class ShareCardCanvas
{
    internal const int Width = 1200;
    internal const int Height = 630;
    internal const float Margin = 64f;
    internal const float MastheadHeight = 96f;
    internal const float AccentBarHeight = 10f;
    internal const float BodyTop = MastheadHeight;
    internal const float BodyBottom = Height - AccentBarHeight;
    internal const float CornerRadius = 12f;
    internal const int LogoSize = 52;
    internal const int LogoX = (int)Margin;
    internal const int LogoY = 22;

    internal static readonly Color BackgroundTop = Color.ParseHex("#05080B");
    internal static readonly Color BackgroundBottom = Color.ParseHex("#15181B");
    internal static readonly Color Ink = Color.White;
    internal static readonly Color MutedInk = new(new Rgba32(214, 222, 232, 205));
    internal static readonly Rgba32 PanelFill = new(18, 29, 28, 226);
    internal static readonly Rgba32 PanelStroke = new(255, 255, 255, 38);
    internal static readonly Rgba32 Hairline = new(255, 255, 255, 26);

    private static readonly ConcurrentDictionary<string, Fonts> FontSets = new(StringComparer.Ordinal);
    internal static Fonts GetFonts(string webRootPath) => FontSets.GetOrAdd(webRootPath, path => new Fonts(path));

    internal sealed class Fonts
    {
        private readonly FontCollection _collection = new();
        private readonly object _gate = new();
        private bool _loaded;
        private FontFamily _bold;
        private FontFamily _regular;
        private FontFamily _display;
        private IReadOnlyList<FontFamily> _fallbacks = [];

        internal Fonts(string webRootPath)
        {
            var fonts = IOPath.Combine(webRootPath, "assets", "fonts");
            BoldPath = IOPath.Combine(fonts, "Roboto-Bold.ttf");
            RegularPath = IOPath.Combine(fonts, "Roboto-Regular.ttf");
            DisplayPath = IOPath.Combine(fonts, "Orbitron-Bold.ttf");
            FallbackPaths = [IOPath.Combine(fonts, "Poppins-Bold.ttf"),
                IOPath.Combine(fonts, "share-cards", "NotoSansSC.ttf"),
                IOPath.Combine(fonts, "share-cards", "NotoEmoji.ttf")];
        }

        internal string BoldPath { get; }
        internal string RegularPath { get; }
        internal string DisplayPath { get; }
        private string[] FallbackPaths { get; }

        internal bool Exist => AllPaths.All(File.Exists);
        private IEnumerable<string> AllPaths => new[] { BoldPath, RegularPath, DisplayPath }.Concat(FallbackPaths);

        internal string Ticks => string.Join(",",
            AllPaths.Select(path =>
                (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L).ToString(CultureInfo.InvariantCulture)));

        internal FontFamily Bold { get { Load(); return _bold; } }
        internal FontFamily Regular { get { Load(); return _regular; } }
        internal FontFamily Display { get { Load(); return _display; } }
        internal IReadOnlyList<FontFamily> Fallbacks { get { Load(); return _fallbacks; } }
        internal RichTextOptions Text(Font font) => new(font) { FallbackFontFamilies = Fallbacks };

        private void Load()
        {
            lock (_gate)
            {
                if (_loaded)
                    return;
                _bold = _collection.Add(BoldPath);
                _regular = _collection.Add(RegularPath);
                _display = _collection.Add(DisplayPath);
                _fallbacks = FallbackPaths.Select(path => _collection.Add(path)).ToArray();
                _loaded = true;
            }
        }
    }

    /// <summary>
    /// Paints the dark field: the card's accent rises as a glow in the upper right, a soft
    /// shaft of the same light falls across it, and a perspective track floor runs toward
    /// that glow, so every card reads as a stadium at night rather than a flat panel.
    /// </summary>
    internal static void DrawBackground(Image<Rgba32> image, Color accentColor)
    {
        var top = BackgroundTop.ToPixel<Rgba32>();
        var bottom = BackgroundBottom.ToPixel<Rgba32>();
        var accent = accentColor.ToPixel<Rgba32>();

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                var vertical = y / (float)(accessor.Height - 1);
                for (var x = 0; x < row.Length; x++)
                {
                    var horizontal = x / (float)(row.Length - 1);
                    var glow = MathF.Max(0f, 1f - MathF.Sqrt(
                        MathF.Pow((horizontal - 0.84f) / 0.66f, 2f) +
                        MathF.Pow((vertical - 0.28f) / 0.88f, 2f)));
                    var light = MathF.Min(0.26f, glow * glow * 0.34f);

                    // A diagonal shaft of the accent light, brightest near the glow.
                    var shaft = horizontal + (vertical * 0.42f) - 1.02f;
                    var beam = MathF.Exp(-(shaft * shaft) / 0.0065f) * 0.1f * (1f - (vertical * 0.55f));

                    // The edges fall away so the middle of the card holds the eye.
                    var dx = horizontal - 0.5f;
                    var dy = vertical - 0.5f;
                    var vignette = Math.Clamp(1f - (((dx * dx * 1.25f) + (dy * dy * 1.1f)) * 0.62f), 0.72f, 1f);

                    row[x] = new Rgba32(
                        Channel(Lerp(top.R, bottom.R, vertical), accent.R, light, beam, vignette),
                        Channel(Lerp(top.G, bottom.G, vertical), accent.G, light, beam, vignette),
                        Channel(Lerp(top.B, bottom.B, vertical), accent.B, light, beam, vignette),
                        255);
                }
            }
        });

        image.Mutate(ctx =>
        {
            DrawTrackFloor(ctx, accentColor);
            ctx.Fill(new Rgba32(0, 0, 0, 92), new RectangularPolygon(0, 0, Width, MastheadHeight));
            ctx.Fill(Hairline, new RectangularPolygon(0, MastheadHeight - 1f, Width, 1f));
            // The accent runs out from under the logo along the masthead's lower edge.
            ctx.Fill(new LinearGradientBrush(
                    new PointF(0f, 0f),
                    new PointF(Width * 0.62f, 0f),
                    GradientRepetitionMode.None,
                    new ColorStop(0f, WithAlpha(accentColor, 0)),
                    new ColorStop(0.08f, WithAlpha(accentColor, 210)),
                    new ColorStop(1f, WithAlpha(accentColor, 0))),
                new RectangularPolygon(0, MastheadHeight - 2f, Width, 2f));
        });
    }

    private static byte Channel(float baseValue, byte accent, float light, float beam, float vignette) =>
        (byte)Math.Clamp(((baseValue + (accent * light) + (accent * beam) + (255f * beam * 0.18f)) * vignette), 0, 255);

    /// <summary>
    /// Lanes of a running track in perspective, converging on the accent glow and fading out
    /// at the horizon. They stay faint enough to sit behind portraits, panels and headlines.
    /// </summary>
    private static void DrawTrackFloor(IImageProcessingContext ctx, Color accent)
    {
        const float horizon = Height * 0.6f;
        var vanishing = new PointF(Width * 0.8f, horizon);
        var floor = new LinearGradientBrush(
            new PointF(0f, horizon),
            new PointF(0f, Height),
            GradientRepetitionMode.None,
            new ColorStop(0f, WithAlpha(accent, 0)),
            new ColorStop(1f, WithAlpha(accent, 54)));

        for (var i = -9; i <= 13; i++)
            ctx.DrawLine(floor, 1.4f, vanishing, new PointF((Width * 0.5f) + (i * 132f), Height));

        // Cross lines bunch toward the horizon, as painted lines on a track do.
        for (var k = 1; k <= 7; k++)
        {
            var t = k / 7f;
            var y = horizon + ((Height - horizon) * t * t);
            ctx.Fill(WithAlpha(accent, (byte)(10 + (40 * t))), new RectangularPolygon(0, y, Width, 1f + t));
        }
    }

    /// <summary>
    /// The masthead every card opens with: logo and wordmark on the left, and the card's
    /// context (league, leaderboard or page) set as an accent caption on the right.
    /// </summary>
    internal static async Task DrawMastheadAsync(
        Image<Rgba32> image,
        string logoPath,
        Fonts fonts,
        string? caption,
        Color accent,
        ILogger log,
        CancellationToken ct,
        bool watermark = true)
    {
        try
        {
            using var logo = await ImageLogo.LoadMarkAsync(logoPath, ct);
            using var mark = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(LogoSize, LogoSize),
                Mode = ResizeMode.Max
            }));
            using var faintMark = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(520, 520),
                Mode = ResizeMode.Max
            }));

            image.Mutate(ctx =>
            {
                if (watermark)
                    ctx.DrawImage(faintMark, new Point(Width - 430, BodyTop.ToInt() + 40), 0.045f);
                ctx.DrawImage(mark, new Point(LogoX, LogoY), 0.96f);
            });
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to draw share card logo.");
        }

        var wordmark = fonts.Display.CreateFont(21f, FontStyle.Bold);
        var wordmarkY = CenterTextY("LONGEVITY WORLD CUP", wordmark, MastheadHeight / 2f);
        image.Mutate(ctx =>
        {
            const float wordmarkX = LogoX + LogoSize + 18f;
            var wordmarkWidth = MeasureTracked(fonts, "LONGEVITY WORLD CUP", wordmark, 2.2f);
            DrawTrackedText(ctx, fonts, "LONGEVITY WORLD CUP", wordmark, Ink, new PointF(wordmarkX, wordmarkY), 2.2f);

            if (!string.IsNullOrWhiteSpace(caption))
            {
                var text = caption.Trim().ToUpperInvariant();
                var captionFont = fonts.Bold.CreateFont(19f, FontStyle.Bold);
                const float tracking = 2.4f;
                var maxWidth = Width - Margin - wordmarkX - wordmarkWidth - 32f;
                while (MeasureTracked(fonts, text, captionFont, tracking) > maxWidth && captionFont.Size > 14f)
                    captionFont = fonts.Bold.CreateFont(captionFont.Size - 1f, FontStyle.Bold);
                var captionElements = TextElements(text).ToList();
                while (MeasureTracked(fonts, text, captionFont, tracking) > maxWidth && captionElements.Count > 0)
                {
                    captionElements.RemoveAt(captionElements.Count - 1);
                    text = string.Concat(captionElements) + "...";
                }
                var width = MeasureTracked(fonts, text, captionFont, tracking);
                var y = CenterTextY(text, captionFont, MastheadHeight / 2f);
                DrawTrackedText(ctx, fonts, text, captionFont, accent, new PointF(Width - Margin - width, y), tracking);
            }
        });
    }

    /// <summary>
    /// The closing bar: one accent, or several equal segments for multi-metric cards. Each
    /// segment glows up into the card and carries a bright leading edge.
    /// </summary>
    internal static void DrawAccentBar(Image<Rgba32> image, params Color[] accents)
    {
        if (accents.Length == 0)
            return;

        var segment = Width / (float)accents.Length;
        const float glowHeight = 70f;
        image.Mutate(ctx =>
        {
            for (var i = 0; i < accents.Length; i++)
            {
                var x = i * segment;
                ctx.Fill(new LinearGradientBrush(
                        new PointF(0f, Height - AccentBarHeight - glowHeight),
                        new PointF(0f, Height - AccentBarHeight),
                        GradientRepetitionMode.None,
                        new ColorStop(0f, WithAlpha(accents[i], 0)),
                        new ColorStop(1f, WithAlpha(accents[i], 64))),
                    new RectangularPolygon(x, Height - AccentBarHeight - glowHeight, segment + 1f, glowHeight));
                ctx.Fill(accents[i], new RectangularPolygon(x, Height - AccentBarHeight, segment + 1f, AccentBarHeight));
                ctx.Fill(new Rgba32(255, 255, 255, 96), new RectangularPolygon(x, Height - AccentBarHeight, segment + 1f, 2f));
            }
        });
    }

    internal static IPath RoundedRect(float x, float y, float width, float height, float radius)
    {
        radius = MathF.Min(radius, MathF.Min(width, height) / 2f);
        const int steps = 10;
        var points = new List<PointF>(steps * 4 + 4);
        void Corner(float cx, float cy, float startDegrees)
        {
            for (var i = 0; i <= steps; i++)
            {
                var angle = (startDegrees + (90f * i / steps)) * MathF.PI / 180f;
                points.Add(new PointF(cx + (radius * MathF.Cos(angle)), cy + (radius * MathF.Sin(angle))));
            }
        }

        Corner(x + width - radius, y + radius, 270f);
        Corner(x + width - radius, y + height - radius, 0f);
        Corner(x + radius, y + height - radius, 90f);
        Corner(x + radius, y + radius, 180f);
        return new Polygon(new LinearLineSegment(points.ToArray()));
    }

    internal static void DrawPanel(IImageProcessingContext ctx, float x, float y, float width, float height)
    {
        ctx.Fill(new Rgba32(0, 0, 0, 96), RoundedRect(x + 4f, y + 12f, width, height, CornerRadius));
        ctx.Fill(PanelFill, RoundedRect(x, y, width, height, CornerRadius));
        // Light from above: the upper half of the glass catches a little of it.
        ctx.Fill(new LinearGradientBrush(
                new PointF(0f, y),
                new PointF(0f, y + height),
                GradientRepetitionMode.None,
                new ColorStop(0f, new Rgba32(255, 255, 255, 20)),
                new ColorStop(0.55f, new Rgba32(255, 255, 255, 0))),
            RoundedRect(x, y, width, height, CornerRadius));
        ctx.Draw(PanelStroke, 1.2f, RoundedRect(x, y, width, height, CornerRadius));
        ctx.Fill(new Rgba32(255, 255, 255, 46), new RectangularPolygon(x + CornerRadius, y + 0.6f, width - (CornerRadius * 2f), 1f));
    }

    /// <summary>Initials for a portrait slot that has no photo, so the card never shows a hole.</summary>
    internal static string Monogram(string? name)
    {
        var words = (name ?? "")
            .Split([' ', '-', '_', '\'', '"', '.'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        if (words.Length == 0)
            return "?";

        var builder = new StringBuilder();
        builder.Append(StringInfo.GetNextTextElement(words[0]).ToUpperInvariant());
        if (words.Length > 1)
            builder.Append(StringInfo.GetNextTextElement(words[^1]).ToUpperInvariant());
        return builder.ToString();
    }

    internal static void DrawMonogramDisc(IImageProcessingContext ctx, Fonts fonts, string name, float centerX, float centerY, float radius, Color accent)
    {
        var initials = Monogram(name);
        var accentPixel = accent.ToPixel<Rgba32>();
        ctx.Fill(new Rgba32(28, 36, 40, 255), new EllipsePolygon(centerX, centerY, radius));
        ctx.Fill(new Rgba32(accentPixel.R, accentPixel.G, accentPixel.B, 34), new EllipsePolygon(centerX, centerY, radius));
        var font = ImageTextLayout.FitFontToWidth(fonts.Bold, initials, radius * 0.78f, radius * 0.4f, radius * 1.3f, sizeStep: 2f, fallbackFonts: fonts.Fallbacks);
        var options = fonts.Text(font);
        var bounds = TextMeasurer.MeasureBounds(initials, options);
        options.Origin = new PointF(centerX - bounds.Left - bounds.Width / 2f, centerY - bounds.Top - bounds.Height / 2f);
        ctx.DrawText(options, initials, new Rgba32(255, 255, 255, 230));
    }

    internal static void DrawPortraitRing(IImageProcessingContext ctx, float centerX, float centerY, float radius, Color accent, float ringWidth)
    {
        var pixel = accent.ToPixel<Rgba32>();
        ctx.Fill(new Rgba32(0, 0, 0, 150), new EllipsePolygon(centerX + 6f, centerY + 12f, radius + ringWidth + 4f));
        // A halo in falling steps, so the portrait reads as lit by its own league colour.
        for (var step = 4; step >= 1; step--)
            ctx.Fill(new Rgba32(pixel.R, pixel.G, pixel.B, (byte)(14 + ((4 - step) * 9))), new EllipsePolygon(centerX, centerY, radius + ringWidth + (step * 9f)));
        ctx.Fill(new Rgba32(pixel.R, pixel.G, pixel.B, 245), new EllipsePolygon(centerX, centerY, radius + ringWidth));
        ctx.Draw(new Rgba32(255, 255, 255, 70), 1.5f, new EllipsePolygon(centerX, centerY, radius + ringWidth - 0.75f));
    }

    internal static void DrawTrackedText(IImageProcessingContext ctx, Fonts fonts, string text, Font font, Color color, PointF origin, float tracking)
    {
        var x = origin.X;
        foreach (var glyph in TextElements(text))
        {
            var options = fonts.Text(font);
            options.Origin = new PointF(x, origin.Y);
            ctx.DrawText(options, glyph, color);
            x += Advance(fonts, glyph, font) + tracking;
        }
    }

    internal static float MeasureTracked(Fonts fonts, string text, Font font, float tracking)
    {
        if (text.Length == 0)
            return 0f;
        var elements = TextElements(text).ToArray();
        return elements.Sum(glyph => Advance(fonts, glyph, font)) + (tracking * (elements.Length - 1));
    }

    private static float Advance(Fonts fonts, string glyph, Font font) => glyph == " "
        ? TextMeasurer.MeasureAdvance("a a", fonts.Text(font)).Width - TextMeasurer.MeasureAdvance("aa", fonts.Text(font)).Width
        : TextMeasurer.MeasureAdvance(glyph, fonts.Text(font)).Width;

    private static IEnumerable<string> TextElements(string text)
    {
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) yield return enumerator.GetTextElement();
    }

    internal static float CenterTextY(string text, Font font, float centerY, Fonts? fonts = null)
    {
        var bounds = TextMeasurer.MeasureBounds(text, fonts?.Text(font) ?? new RichTextOptions(font));
        return centerY - bounds.Top - (bounds.Height / 2f);
    }

    /// <summary>
    /// Fits a headline into at most <paramref name="maxLines"/> lines: shrink first, then wrap,
    /// then ellipsize only as the last resort.
    /// </summary>
    internal static (IReadOnlyList<string> Lines, Font Font) FitHeadline(
        Fonts fonts,
        string text,
        float startSize,
        float minSize,
        float maxWidth,
        int maxLines,
        float wrapMinSize)
    {
        text = (text ?? "").Trim();
        var family = fonts.Bold;
        for (var size = startSize; size >= minSize; size -= 2f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, fonts.Text(font)).Width <= maxWidth)
                return ([text], font);
        }

        for (var size = startSize; size >= wrapMinSize; size -= 2f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            var lines = Wrap(text, fonts.Text(font), maxWidth, maxLines);
            if (lines.Count <= maxLines && lines.All(line => TextMeasurer.MeasureSize(line, fonts.Text(font)).Width <= maxWidth))
                return (lines, font);
        }

        var fallback = family.CreateFont(wrapMinSize, FontStyle.Bold);
        var allLines = Wrap(text, fonts.Text(fallback), maxWidth);
        var wrapped = allLines.Take(maxLines).ToList();
        wrapped[^1] = ImageTextLayout.EllipsizeToWidth(
            string.Join(' ', allLines.Skip(maxLines - 1)), fallback, maxWidth, fonts.Fallbacks);
        return (wrapped, fallback);
    }

    private static List<string> Wrap(string text, RichTextOptions options, float maxWidth, int maxLines = int.MaxValue)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (TextMeasurer.MeasureSize(candidate, options).Width > maxWidth)
            {
                if (current.Length > 0) lines.Add(current);
                current = "";
                if (lines.Count >= maxLines)
                {
                    lines.Add(word);
                    return lines;
                }
                if (TextMeasurer.MeasureSize(word, options).Width <= maxWidth)
                {
                    current = word;
                    continue;
                }

                // Shape whole prefixes rather than measuring every growing prefix
                // at every font size. Break only at complete text elements.
                var starts = StringInfo.ParseCombiningCharacters(word);
                for (var offset = 0; offset < starts.Length;)
                {
                    var low = offset + 1;
                    var high = starts.Length;
                    var best = offset;
                    while (low <= high)
                    {
                        var middle = low + ((high - low) / 2);
                        var end = middle == starts.Length ? word.Length : starts[middle];
                        if (TextMeasurer.MeasureSize(word[starts[offset]..end], options).Width <= maxWidth)
                        {
                            best = middle;
                            low = middle + 1;
                        }
                        else high = middle - 1;
                    }
                    best = Math.Max(best, offset + 1);
                    var chunkEnd = best == starts.Length ? word.Length : starts[best];
                    current = word[starts[offset]..chunkEnd];
                    offset = best;
                    if (offset < starts.Length)
                    {
                        lines.Add(current);
                        current = "";
                        if (lines.Count > maxLines) return lines;
                    }
                }
            }
            else
            {
                current = candidate;
            }
        }

        if (current.Length > 0 || lines.Count == 0)
            lines.Add(current);
        return lines;
    }

    internal static void DrawLines(IImageProcessingContext ctx, Fonts fonts, IReadOnlyList<string> lines, Font font, Color color, float x, float y, float lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var origin = new PointF(x, y + (i * lineHeight));
            var options = fonts.Text(font);
            options.Origin = new PointF(origin.X, origin.Y + 3f);
            ctx.DrawText(options, lines[i], new Rgba32(0, 0, 0, 150));
            options.Origin = origin;
            ctx.DrawText(options, lines[i], color);
        }
    }

    internal static Rgba32 WithAlpha(Color color, byte alpha)
    {
        var pixel = color.ToPixel<Rgba32>();
        return new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
    }

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static int ToInt(this float value) => (int)MathF.Round(value);
}
