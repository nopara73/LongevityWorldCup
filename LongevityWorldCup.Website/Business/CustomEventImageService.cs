using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Tools;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace LongevityWorldCup.Website.Business;

public sealed class CustomEventImageService
{
    private const string SiteBaseUrl = "https://longevityworldcup.com";
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 675;
    private const int CardMinWidth = 900;
    private const int CardMinHeight = 330;
    private const int CardMaxWidth = 1120;
    private const int CardMaxHeight = 551;
    private const int HorizontalPadding = 20;
    private const int VerticalPadding = 40;
    private const float CardCornerRadius = 40f;
    private const float CardGlowBlur = 21.5f;
    private const float LineHeightMultiplier = 1.35f;
    private const float FontMin = 18f;
    private const float FontMax = 32f;

    private static readonly Color CardFillColor = new Rgba32(255, 255, 255, 13);
    private static readonly Rgba32 CardStrokeColor = new(255, 255, 255, 70);
    private static readonly Rgba32 CardGlowColor = new(255, 255, 255, 64);
    private static readonly Rgba32 RegularTextColor = new(255, 255, 255, 255);
    private static readonly Rgba32 StrongTextColor = new(255, 64, 129, 255);

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<CustomEventImageService> _log;
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private readonly string _templatePath;
    private readonly string _regularFontPath;
    private readonly string _boldFontPath;
    private readonly string _outputDir;
    private FontFamily? _regularFamily;
    private FontFamily? _boldFamily;

    public CustomEventImageService(IWebHostEnvironment env, ILogger<CustomEventImageService> log)
    {
        _env = env;
        _log = log;
        _templatePath = IOPath.Combine(_env.WebRootPath, "assets", "custom_event.png");
        _regularFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Roboto-Regular.ttf");
        _boldFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Roboto-Bold.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "custom-events");
    }

    public bool IsConfigured =>
        File.Exists(_templatePath) &&
        File.Exists(_regularFontPath) &&
        File.Exists(_boldFontPath);

    public async Task<(string FullPath, string PublicUrl)?> RenderAsync(string eventId, string rawText)
    {
        if (string.IsNullOrWhiteSpace(eventId) || !IsConfigured)
            return null;

        await _renderLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_outputDir);
            EnsureFontsLoaded();

            var fileName = $"{SanitizeFileName(eventId)}.png";
            var outputPath = IOPath.Combine(_outputDir, fileName);

            if (!File.Exists(outputPath))
            {
                using var image = await BuildImageAsync(rawText);
                await image.SaveAsPngAsync(outputPath);
            }

            return (outputPath, $"{SiteBaseUrl}/generated/custom-events/{Uri.EscapeDataString(fileName)}");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to render custom event image for event {EventId}", eventId);
            return null;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    public async Task<MemoryStream?> RenderToStreamAsync(string rawText)
    {
        if (!IsConfigured)
            return null;

        await _renderLock.WaitAsync();
        try
        {
            EnsureFontsLoaded();
            using var image = await BuildImageAsync(rawText);
            var stream = new MemoryStream();
            await image.SaveAsPngAsync(stream);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to render custom event image stream.");
            return null;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task<Image<Rgba32>> BuildImageAsync(string rawText)
    {
        var (_, contentRaw) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var contentSource = string.IsNullOrWhiteSpace(contentRaw) ? rawText : contentRaw;
        var segments = CustomEventMarkup.ParseSegments(contentSource, keepHyperlinkLabels: true);
        var layout = FindBestLayout(segments);

        var image = await Image.LoadAsync<Rgba32>(_templatePath);
        if (image.Width != CanvasWidth || image.Height != CanvasHeight)
            image.Mutate(ctx => ctx.Resize(CanvasWidth, CanvasHeight));

        DrawCard(image, layout);
        return image;
    }

    private LayoutResult FindBestLayout(IReadOnlyList<CustomEventSegment> segments)
    {
        LayoutResult? fallback = null;
        for (var fontSize = FontMax; fontSize >= FontMin; fontSize -= 1f)
        {
            var layout = BuildLayout(segments, fontSize);
            fallback ??= layout;
            if (layout.Fits)
                return layout;
        }

        return fallback ?? BuildLayout(segments, FontMin);
    }

    private LayoutResult BuildLayout(IReadOnlyList<CustomEventSegment> segments, float fontSize)
    {
        var regularFamily = _regularFamily ?? throw new InvalidOperationException("Regular font was not loaded.");
        var boldFamily = _boldFamily ?? throw new InvalidOperationException("Bold font was not loaded.");
        var regularFont = regularFamily.CreateFont(fontSize, FontStyle.Regular);
        var boldFont = boldFamily.CreateFont(fontSize, FontStyle.Bold);
        var maxTextWidth = CardMaxWidth - (HorizontalPadding * 2);
        var initial = WrapSegments(segments, regularFont, boldFont, maxTextWidth);
        var targetCardWidth = Clamp((int)Math.Ceiling(initial.MaxLineWidth) + (HorizontalPadding * 2), CardMinWidth, CardMaxWidth);
        var finalWrap = WrapSegments(segments, regularFont, boldFont, targetCardWidth - (HorizontalPadding * 2));
        var targetCardHeight = Clamp((int)Math.Ceiling(finalWrap.TotalHeight) + (VerticalPadding * 2), CardMinHeight, CardMaxHeight);
        var fits = finalWrap.TotalHeight <= targetCardHeight - (VerticalPadding * 2) + 0.5f;

        return new LayoutResult(
            FontSize: fontSize,
            RegularFont: regularFont,
            BoldFont: boldFont,
            CardWidth: targetCardWidth,
            CardHeight: targetCardHeight,
            TextBlockWidth: finalWrap.MaxLineWidth,
            TextBlockHeight: finalWrap.TotalHeight,
            Lines: finalWrap.Lines,
            Fits: fits);
    }

    private static float fontSizeToLineHeight(float fontSize)
    {
        return fontSize * LineHeightMultiplier;
    }

    private static void TrimTrailingWhitespace(List<WrappedRun> runs)
    {
        while (runs.Count > 0 && string.IsNullOrWhiteSpace(runs[^1].Text))
            runs.RemoveAt(runs.Count - 1);
    }

    private IReadOnlyList<MeasuredToken> Tokenize(IReadOnlyList<CustomEventSegment> segments, Font regularFont, Font boldFont)
    {
        var tokens = new List<MeasuredToken>();
        foreach (var segment in segments)
        {
            var parts = Regex.Split(segment.Text, "(\n|\\s+)");
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part))
                    continue;

                if (part == "\n")
                {
                    tokens.Add(new MeasuredToken(TokenKind.NewLine, part, segment.Style, 0f));
                    continue;
                }

                var font = segment.Style == CustomEventTextStyle.Regular ? regularFont : boldFont;
                var size = MeasureText(part, font);
                var kind = string.IsNullOrWhiteSpace(part) ? TokenKind.Whitespace : TokenKind.Word;
                tokens.Add(new MeasuredToken(kind, part, segment.Style, size.Width));
            }
        }

        return tokens;
    }

    private WrappedLayout WrapSegments(IReadOnlyList<CustomEventSegment> segments, Font regularFont, Font boldFont, int maxWidth)
    {
        var tokens = Tokenize(segments, regularFont, boldFont);
        var lines = new List<WrappedLine>();
        var currentSegments = new List<WrappedRun>();
        var currentWidth = 0f;
        var maxLineWidth = 0f;

        void FlushLine(bool preserveEmptyLine)
        {
            TrimTrailingWhitespace(currentSegments);
            if (!preserveEmptyLine && currentSegments.Count == 0)
                return;

            var width = currentSegments.Sum(x => x.Width);
            maxLineWidth = Math.Max(maxLineWidth, width);
            lines.Add(new WrappedLine(currentSegments.ToArray(), width));
            currentSegments = new List<WrappedRun>();
            currentWidth = 0f;
        }

        foreach (var token in tokens)
        {
            if (token.Kind == TokenKind.NewLine)
            {
                FlushLine(true);
                continue;
            }

            if (token.Kind == TokenKind.Whitespace)
            {
                if (currentSegments.Count == 0)
                    continue;

                currentSegments.Add(new WrappedRun(token.Text, token.Style, token.Width));
                currentWidth += token.Width;
                continue;
            }

            if (currentWidth > 0f && currentWidth + token.Width > maxWidth)
                FlushLine(false);

            if (token.Width <= maxWidth)
            {
                currentSegments.Add(new WrappedRun(token.Text, token.Style, token.Width));
                currentWidth += token.Width;
                continue;
            }

            foreach (var fragment in SplitLongWord(token, regularFont, boldFont, maxWidth))
            {
                if (currentWidth > 0f && currentWidth + fragment.Width > maxWidth)
                    FlushLine(false);

                currentSegments.Add(fragment);
                currentWidth += fragment.Width;
            }
        }

        FlushLine(false);
        if (lines.Count == 0)
            lines.Add(new WrappedLine(Array.Empty<WrappedRun>(), 0f));

        var lineHeight = fontSizeToLineHeight(regularFont.Size);
        return new WrappedLayout(lines.ToArray(), maxLineWidth, lines.Count * lineHeight, lineHeight);
    }

    private IReadOnlyList<WrappedRun> SplitLongWord(MeasuredToken token, Font regularFont, Font boldFont, int maxWidth)
    {
        var pieces = new List<WrappedRun>();
        var font = token.Style == CustomEventTextStyle.Regular ? regularFont : boldFont;
        var remaining = token.Text;
        while (remaining.Length > 0)
        {
            var low = 1;
            var high = remaining.Length;
            var best = 1;
            while (low <= high)
            {
                var mid = (low + high) / 2;
                var candidate = remaining[..mid];
                var candidateWidth = MeasureText(candidate, font).Width;
                if (candidateWidth <= maxWidth)
                {
                    best = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            var text = remaining[..best];
            var width = MeasureText(text, font).Width;
            pieces.Add(new WrappedRun(text, token.Style, width));
            remaining = remaining[best..];
        }

        return pieces;
    }

    private void DrawCard(Image<Rgba32> image, LayoutResult layout)
    {
        var cardX = (CanvasWidth - layout.CardWidth) / 2f;
        var cardY = (CanvasHeight - layout.CardHeight) / 2f;
        using var glowMask = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        FillRoundedRectMask(glowMask, cardX, cardY, layout.CardWidth, layout.CardHeight, CardCornerRadius, 255);
        glowMask.Mutate(ctx => ctx.GaussianBlur(CardGlowBlur));
        TintMask(glowMask, CardGlowColor);

        using var fillMask = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        FillRoundedRectMask(fillMask, cardX, cardY, layout.CardWidth, layout.CardHeight, CardCornerRadius, CardFillColor.ToPixel<Rgba32>().A);
        TintMask(fillMask, CardFillColor.ToPixel<Rgba32>());

        using var strokeMask = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        FillRoundedRectBorderMask(strokeMask, cardX, cardY, layout.CardWidth, layout.CardHeight, CardCornerRadius, 1.25f, CardStrokeColor.A);
        TintMask(strokeMask, CardStrokeColor);

        image.Mutate(ctx =>
        {
            ctx.DrawImage(glowMask, new Point(0, 0), 1f);
            ctx.DrawImage(fillMask, new Point(0, 0), 1f);
            ctx.DrawImage(strokeMask, new Point(0, 0), 1f);
        });

        var lineHeight = fontSizeToLineHeight(layout.FontSize);
        var textY = cardY + ((layout.CardHeight - layout.TextBlockHeight) / 2f);

        foreach (var line in layout.Lines)
        {
            var cursorX = cardX + ((layout.CardWidth - line.Width) / 2f);
            foreach (var run in line.Runs)
            {
                if (string.IsNullOrEmpty(run.Text))
                    continue;

                if (string.IsNullOrWhiteSpace(run.Text))
                {
                    cursorX += run.Width;
                    continue;
                }

                var font = run.Style == CustomEventTextStyle.Regular ? layout.RegularFont : layout.BoldFont;
                var color = run.Style == CustomEventTextStyle.Strong ? StrongTextColor : RegularTextColor;
                image.Mutate(ctx => ctx.DrawText(run.Text, font, color, new PointF(cursorX, textY)));
                cursorX += run.Width;
            }

            textY += lineHeight;
        }
    }

    private static void TintMask(Image<Rgba32> mask, Rgba32 color)
    {
        mask.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var alpha = row[x].A;
                    if (alpha == 0)
                        continue;

                    row[x] = new Rgba32(color.R, color.G, color.B, (byte)(alpha * (color.A / 255f)));
                }
            }
        });
    }

    private static SizeF MeasureText(string text, Font font)
    {
        if (string.IsNullOrEmpty(text))
            return SizeF.Empty;

        var rect = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
        return new SizeF(rect.Width, rect.Height);
    }

    private static void FillRoundedRectMask(Image<Rgba32> mask, float x, float y, float width, float height, float radius, byte alpha)
    {
        mask.ProcessPixelRows(accessor =>
        {
            for (var py = 0; py < accessor.Height; py++)
            {
                var row = accessor.GetRowSpan(py);
                var sampleY = py + 0.5f;
                for (var px = 0; px < row.Length; px++)
                {
                    var sampleX = px + 0.5f;
                    if (!IsInsideRoundedRect(sampleX, sampleY, x, y, width, height, radius))
                        continue;

                    row[px] = new Rgba32(255, 255, 255, alpha);
                }
            }
        });
    }

    private static void FillRoundedRectBorderMask(Image<Rgba32> mask, float x, float y, float width, float height, float radius, float thickness, byte alpha)
    {
        var innerX = x + thickness;
        var innerY = y + thickness;
        var innerWidth = Math.Max(0f, width - (thickness * 2f));
        var innerHeight = Math.Max(0f, height - (thickness * 2f));
        var innerRadius = Math.Max(0f, radius - thickness);

        mask.ProcessPixelRows(accessor =>
        {
            for (var py = 0; py < accessor.Height; py++)
            {
                var row = accessor.GetRowSpan(py);
                var sampleY = py + 0.5f;
                for (var px = 0; px < row.Length; px++)
                {
                    var sampleX = px + 0.5f;
                    var inOuter = IsInsideRoundedRect(sampleX, sampleY, x, y, width, height, radius);
                    if (!inOuter)
                        continue;

                    var inInner = innerWidth > 0f &&
                                  innerHeight > 0f &&
                                  IsInsideRoundedRect(sampleX, sampleY, innerX, innerY, innerWidth, innerHeight, innerRadius);
                    if (inInner)
                        continue;

                    row[px] = new Rgba32(255, 255, 255, alpha);
                }
            }
        });
    }

    private static bool IsInsideRoundedRect(float sampleX, float sampleY, float x, float y, float width, float height, float radius)
    {
        var left = x;
        var top = y;
        var right = x + width;
        var bottom = y + height;
        if (sampleX < left || sampleX > right || sampleY < top || sampleY > bottom)
            return false;

        var clampedRadius = Math.Min(radius, Math.Min(width, height) / 2f);
        if (sampleX >= left + clampedRadius && sampleX <= right - clampedRadius)
            return true;
        if (sampleY >= top + clampedRadius && sampleY <= bottom - clampedRadius)
            return true;

        var cx = sampleX < left + clampedRadius ? left + clampedRadius : right - clampedRadius;
        var cy = sampleY < top + clampedRadius ? top + clampedRadius : bottom - clampedRadius;
        var dx = sampleX - cx;
        var dy = sampleY - cy;
        return (dx * dx) + (dy * dy) <= clampedRadius * clampedRadius;
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private void EnsureFontsLoaded()
    {
        if (_regularFamily is not null && _boldFamily is not null)
            return;

        _regularFamily = _fonts.Add(_regularFontPath);
        _boldFamily = _fonts.Add(_boldFontPath);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var invalid in IOPath.GetInvalidFileNameChars())
            value = value.Replace(invalid, '_');
        return value;
    }

    private enum TokenKind
    {
        Word,
        Whitespace,
        NewLine
    }

    private sealed record MeasuredToken(TokenKind Kind, string Text, CustomEventTextStyle Style, float Width);
    private sealed record WrappedRun(string Text, CustomEventTextStyle Style, float Width);
    private sealed record WrappedLine(IReadOnlyList<WrappedRun> Runs, float Width);
    private sealed record WrappedLayout(IReadOnlyList<WrappedLine> Lines, float MaxLineWidth, float TotalHeight, float LineHeight);
    private sealed record LayoutResult(
        float FontSize,
        Font RegularFont,
        Font BoldFont,
        int CardWidth,
        int CardHeight,
        float TextBlockWidth,
        float TextBlockHeight,
        IReadOnlyList<WrappedLine> Lines,
        bool Fits);
}
