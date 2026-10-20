using System.Globalization;
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

    internal sealed class Fonts
    {
        private readonly FontCollection _collection = new();
        private readonly object _gate = new();
        private bool _loaded;
        private FontFamily _bold;
        private FontFamily _regular;
        private FontFamily _display;

        internal Fonts(string webRootPath)
        {
            var fonts = IOPath.Combine(webRootPath, "assets", "fonts");
            BoldPath = IOPath.Combine(fonts, "Roboto-Bold.ttf");
            RegularPath = IOPath.Combine(fonts, "Roboto-Regular.ttf");
            DisplayPath = IOPath.Combine(fonts, "Orbitron-Bold.ttf");
        }

        internal string BoldPath { get; }
        internal string RegularPath { get; }
        internal string DisplayPath { get; }

        internal bool Exist => File.Exists(BoldPath) && File.Exists(RegularPath) && File.Exists(DisplayPath);

        internal string Ticks => string.Join(",",
            new[] { BoldPath, RegularPath, DisplayPath }.Select(path =>
                (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L).ToString(CultureInfo.InvariantCulture)));

        internal FontFamily Bold { get { Load(); return _bold; } }
        internal FontFamily Regular { get { Load(); return _regular; } }
        internal FontFamily Display { get { Load(); return _display; } }

        private void Load()
        {
            lock (_gate)
            {
                if (_loaded)
                    return;
                _bold = _collection.Add(BoldPath);
                _regular = _collection.Add(RegularPath);
                _display = _collection.Add(DisplayPath);
                _loaded = true;
            }
        }
    }

    /// <summary>Paints the dark field with a soft glow of the card's accent in the upper right.</summary>
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
                        MathF.Pow((horizontal - 0.86f) / 0.62f, 2f) +
                        MathF.Pow((vertical - 0.30f) / 0.85f, 2f)));
                    var light = MathF.Min(0.17f, glow * glow * 0.24f);

                    row[x] = new Rgba32(
                        (byte)Math.Clamp(Lerp(top.R, bottom.R, vertical) + (accent.R * light), 0, 255),
                        (byte)Math.Clamp(Lerp(top.G, bottom.G, vertical) + (accent.G * light), 0, 255),
                        (byte)Math.Clamp(Lerp(top.B, bottom.B, vertical) + (accent.B * light), 0, 255),
                        255);
                }
            }
        });

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 84), new RectangularPolygon(0, 0, Width, MastheadHeight));
            ctx.Fill(Hairline, new RectangularPolygon(0, MastheadHeight - 1f, Width, 1f));
        });
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
            DrawTrackedText(ctx, "LONGEVITY WORLD CUP", wordmark, Ink, new PointF(LogoX + LogoSize + 18f, wordmarkY), 2.2f);

            if (!string.IsNullOrWhiteSpace(caption))
            {
                var text = caption.Trim().ToUpperInvariant();
                var captionFont = fonts.Bold.CreateFont(19f, FontStyle.Bold);
                const float tracking = 2.4f;
                const float maxWidth = 520f;
                while (MeasureTracked(text, captionFont, tracking) > maxWidth && captionFont.Size > 14f)
                    captionFont = fonts.Bold.CreateFont(captionFont.Size - 1f, FontStyle.Bold);
                var width = MeasureTracked(text, captionFont, tracking);
                var y = CenterTextY(text, captionFont, MastheadHeight / 2f);
                DrawTrackedText(ctx, text, captionFont, accent, new PointF(Width - Margin - width, y), tracking);
            }
        });
    }

    /// <summary>The closing bar: one accent, or several equal segments for multi-metric cards.</summary>
    internal static void DrawAccentBar(Image<Rgba32> image, params Color[] accents)
    {
        if (accents.Length == 0)
            return;

        var segment = Width / (float)accents.Length;
        image.Mutate(ctx =>
        {
            for (var i = 0; i < accents.Length; i++)
                ctx.Fill(accents[i], new RectangularPolygon(i * segment, Height - AccentBarHeight, segment + 1f, AccentBarHeight));
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
        ctx.Fill(new Rgba32(0, 0, 0, 70), RoundedRect(x + 4f, y + 8f, width, height, CornerRadius));
        ctx.Fill(PanelFill, RoundedRect(x, y, width, height, CornerRadius));
        ctx.Draw(PanelStroke, 1.2f, RoundedRect(x, y, width, height, CornerRadius));
    }

    /// <summary>Initials for a portrait slot that has no photo, so the card never shows a hole.</summary>
    internal static string Monogram(string? name)
    {
        var words = (name ?? "")
            .Split([' ', '-', '_', '\'', '"', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => char.IsLetterOrDigit(word[0]))
            .ToArray();
        if (words.Length == 0)
            return "?";

        var builder = new StringBuilder();
        builder.Append(char.ToUpperInvariant(words[0][0]));
        if (words.Length > 1)
            builder.Append(char.ToUpperInvariant(words[^1][0]));
        return builder.ToString();
    }

    internal static void DrawMonogramDisc(IImageProcessingContext ctx, Fonts fonts, string name, float centerX, float centerY, float radius, Color accent)
    {
        var initials = Monogram(name);
        var accentPixel = accent.ToPixel<Rgba32>();
        ctx.Fill(new Rgba32(28, 36, 40, 255), new EllipsePolygon(centerX, centerY, radius));
        ctx.Fill(new Rgba32(accentPixel.R, accentPixel.G, accentPixel.B, 34), new EllipsePolygon(centerX, centerY, radius));
        var font = ImageTextLayout.FitFontToWidth(fonts.Display, initials, radius * 0.78f, radius * 0.4f, radius * 1.3f, sizeStep: 2f);
        var size = TextMeasurer.MeasureSize(initials, new RichTextOptions(font));
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = new PointF(centerX, centerY - (size.Height / 2f) - (font.Size * 0.04f)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        }, initials, new Rgba32(255, 255, 255, 230));
    }

    internal static void DrawPortraitRing(IImageProcessingContext ctx, float centerX, float centerY, float radius, Color accent, float ringWidth)
    {
        var pixel = accent.ToPixel<Rgba32>();
        ctx.Fill(new Rgba32(0, 0, 0, 140), new EllipsePolygon(centerX + 6f, centerY + 10f, radius + ringWidth + 4f));
        ctx.Fill(new Rgba32(pixel.R, pixel.G, pixel.B, 40), new EllipsePolygon(centerX, centerY, radius + ringWidth + 10f));
        ctx.Fill(new Rgba32(pixel.R, pixel.G, pixel.B, 240), new EllipsePolygon(centerX, centerY, radius + ringWidth));
    }

    internal static void DrawTrackedText(IImageProcessingContext ctx, string text, Font font, Color color, PointF origin, float tracking)
    {
        var x = origin.X;
        foreach (var ch in text)
        {
            var glyph = ch.ToString();
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(x, origin.Y),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, glyph, color);
            x += Advance(glyph, font) + tracking;
        }
    }

    internal static float MeasureTracked(string text, Font font, float tracking)
    {
        if (text.Length == 0)
            return 0f;
        return text.Sum(ch => Advance(ch.ToString(), font)) + (tracking * (text.Length - 1));
    }

    private static float Advance(string glyph, Font font)
    {
        return glyph == " "
            ? TextMeasurer.MeasureAdvance("a a", new TextOptions(font)).Width - TextMeasurer.MeasureAdvance("aa", new TextOptions(font)).Width
            : TextMeasurer.MeasureAdvance(glyph, new TextOptions(font)).Width;
    }

    internal static float CenterTextY(string text, Font font, float centerY)
    {
        var bounds = TextMeasurer.MeasureBounds(text, new RichTextOptions(font));
        return centerY - bounds.Top - (bounds.Height / 2f);
    }

    /// <summary>
    /// Fits a headline into at most <paramref name="maxLines"/> lines: shrink first, then wrap,
    /// then ellipsize only as the last resort.
    /// </summary>
    internal static (IReadOnlyList<string> Lines, Font Font) FitHeadline(
        FontFamily family,
        string text,
        float startSize,
        float minSize,
        float maxWidth,
        int maxLines,
        float wrapMinSize)
    {
        text = (text ?? "").Trim();
        for (var size = startSize; size >= minSize; size -= 2f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
                return ([text], font);
        }

        for (var size = startSize; size >= wrapMinSize; size -= 2f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            var lines = Wrap(text, font, maxWidth);
            if (lines.Count <= maxLines && lines.All(line => TextMeasurer.MeasureSize(line, new RichTextOptions(font)).Width <= maxWidth))
                return (lines, font);
        }

        var fallback = family.CreateFont(wrapMinSize, FontStyle.Bold);
        var wrapped = Wrap(text, fallback, maxWidth).Take(maxLines).ToList();
        wrapped[^1] = ImageTextLayout.EllipsizeToWidth(
            string.Join(' ', Wrap(text, fallback, maxWidth).Skip(maxLines - 1)), fallback, maxWidth);
        return (wrapped, fallback);
    }

    private static List<string> Wrap(string text, Font font, float maxWidth)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (current.Length > 0 && TextMeasurer.MeasureSize(candidate, new RichTextOptions(font)).Width > maxWidth)
            {
                lines.Add(current);
                current = word;
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

    internal static void DrawLines(IImageProcessingContext ctx, IReadOnlyList<string> lines, Font font, Color color, float x, float y, float lineHeight)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var origin = new PointF(x, y + (i * lineHeight));
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(origin.X, origin.Y + 3f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, lines[i], new Rgba32(0, 0, 0, 150));
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = origin,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, lines[i], color);
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
