using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IOPath = System.IO.Path;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;

namespace LongevityWorldCup.Website.Business;

public sealed class PageOgImageService
{
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 630;
    private const float ContentX = 86f;
    private const float ContentTop = 76f;
    private const float ContentWidth = 980f;

    private static readonly Color BackgroundTop = ParseHex("05080B");
    private static readonly Color BackgroundBottom = ParseHex("15181B");
    private static readonly Color TitleColor = Color.White;
    private static readonly Color BodyColor = new(new Rgba32(226, 232, 240, 232));
    private static readonly Color MutedColor = new(new Rgba32(148, 163, 184, 255));
    private static readonly Color ChipFillColor = new(new Rgba32(255, 255, 255, 18));
    private static readonly Color ChipStrokeColor = new(new Rgba32(255, 255, 255, 48));
    private static readonly Color ShadowColor = new(new Rgba32(0, 0, 0, 180));

    private static readonly IReadOnlyDictionary<string, PageOgDefinition> Definitions =
        new Dictionary<string, PageOgDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = new(
                "home",
                "Biological age competition",
                "Longevity World Cup",
                "Reverse biological age, climb public leaderboards, and compete across Pro and Amateur tracks.",
                "78DA3B",
                ["Ultimate League", "Age Reduction", "Pheno Age + Bortz Age"]),
            ["events"] = new(
                "events",
                "Highlights",
                "Competition highlights",
                "Follow Events, milestones, season updates, and athlete movement from the current field.",
                "00BCD4",
                ["Events", "Milestones", "Season updates"]),
            ["media"] = new(
                "media",
                "Media Kit",
                "Press-ready assets",
                "Official Longevity World Cup logos, visuals, and reference material for coverage.",
                "FF4081",
                ["Logo assets", "Press material", "Brand references"]),
            ["about"] = new(
                "about",
                "About",
                "Longevity sport, measured",
                "An open competition where longevity athletes rank by biomarker-based Age Reduction.",
                "78DA3B",
                ["Longevity athletes", "Biomarker results", "Public leaderboard"]),
            ["history"] = new(
                "history",
                "History",
                "Longevity as a sport",
                "From early biological age leaderboards to a public competition with seasons and prizes.",
                "00BCD4",
                ["Biological age", "Leaderboards", "Seasons"]),
            ["ruleset"] = new(
                "ruleset",
                "Ruleset",
                "Competition rules",
                "Seasons, tracks, valid submissions, rankings, proof requirements, prizes, and payouts.",
                "FFB020",
                ["Pro before Amateur", "Valid submissions", "Prize pool"]),
            ["view-bortz"] = new(
                "view-bortz",
                "Pro clock view",
                "Bortz Age leaderboard",
                "Track athletes with eligible Bortz Age results in the current Pro field.",
                "FFB020",
                ["Bortz Age", "Pro track", "Seasonal clock"]),
            ["view-pheno"] = new(
                "view-pheno",
                "Amateur clock view",
                "Pheno Age leaderboard",
                "Track verified Pheno Age submissions across the accessible Amateur path.",
                "78DA3B",
                ["Pheno Age", "Amateur track", "All-time clock"]),
            ["view-crowd"] = new(
                "view-crowd",
                "Community view",
                "Crowd Age leaderboard",
                "Rank athletes by Crowd Age Difference once enough accepted guesses are in.",
                "00BCD4",
                ["Crowd Age", "100+ guesses", "Separate leaderboard"])
        };

    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PageOgImageService> _log;
    private readonly string _logoPath;
    private readonly string _boldFontPath;
    private readonly string _regularFontPath;
    private readonly string _outputDir;
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private FontFamily _boldFamily;
    private FontFamily _regularFamily;
    private bool _fontsLoaded;

    public PageOgImageService(IWebHostEnvironment env, ILogger<PageOgImageService> log)
    {
        _env = env;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _boldFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _regularFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Regular.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "page");
    }

    public sealed record PageOgPayload(
        string Slug,
        string Kicker,
        string Title,
        string Description,
        string AccentHex,
        IReadOnlyList<string> Stats,
        string Signature);

    private sealed record PageOgDefinition(
        string Slug,
        string Kicker,
        string Title,
        string Description,
        string AccentHex,
        IReadOnlyList<string> Stats);

    public bool IsConfigured =>
        File.Exists(_logoPath) &&
        File.Exists(_boldFontPath) &&
        File.Exists(_regularFontPath);

    public bool TryGetCurrentPayload(string rawSlug, out PageOgPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(rawSlug) ||
            !Definitions.TryGetValue(NormalizeSlug(rawSlug), out var definition))
        {
            return false;
        }

        payload = new PageOgPayload(
            definition.Slug,
            definition.Kicker,
            definition.Title,
            definition.Description,
            definition.AccentHex,
            definition.Stats,
            ComputeSignature(definition));
        return true;
    }

    public string BuildVersionedImageUrl(string siteBaseUrl, PageOgPayload payload)
    {
        return $"{siteBaseUrl}/og/page/{payload.Slug}.png?v={payload.Signature}";
    }

    public async Task<string?> EnsureRenderedImageAsync(PageOgPayload payload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        Directory.CreateDirectory(_outputDir);
        var outputPath = IOPath.Combine(_outputDir, $"{payload.Slug}-{payload.Signature}.png");
        if (File.Exists(outputPath))
        {
            CleanupOldRenders(payload.Slug, outputPath);
            return outputPath;
        }

        await _renderLock.WaitAsync(ct);
        try
        {
            if (File.Exists(outputPath))
            {
                CleanupOldRenders(payload.Slug, outputPath);
                return outputPath;
            }

            var tempPath = BuildTempRenderPath(outputPath);
            try
            {
                await RenderImageAsync(payload, tempPath, ct);
                PublishTempRender(tempPath, outputPath);
            }
            finally
            {
                DeleteTempRender(tempPath);
            }

            CleanupOldRenders(payload.Slug, outputPath);
            return outputPath;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task RenderImageAsync(PageOgPayload payload, string outputPath, CancellationToken ct)
    {
        using var image = new Image<Rgba32>(CanvasWidth, CanvasHeight);
        DrawBackground(image, ParseHex(payload.AccentHex));

        var (boldFamily, regularFamily) = GetFontFamilies();
        var accent = ParseHex(payload.AccentHex);

        await DrawLogoAsync(image, ct);
        DrawHeaderText(image, boldFamily);
        DrawTextContent(image, payload, boldFamily, regularFamily, accent);
        DrawFooterChips(image, payload, boldFamily, accent);

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private async Task DrawLogoAsync(Image<Rgba32> image, CancellationToken ct)
    {
        try
        {
            await using var logoStream = File.OpenRead(_logoPath);
            using var logo = await Image.LoadAsync<Rgba32>(logoStream, ct);
            logo.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(62, 62),
                Mode = ResizeMode.Max
            }));

            image.Mutate(ctx => ctx.DrawImage(logo, new Point((int)ContentX, 42), 0.96f));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw page OG logo.");
        }
    }

    private static void DrawHeaderText(Image<Rgba32> image, FontFamily boldFamily)
    {
        var brandFont = boldFamily.CreateFont(24, FontStyle.Bold);
        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(brandFont)
            {
                Origin = new PointF(ContentX + 76f, 51f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "LONGEVITY\nWORLD CUP", Color.White);
        });
    }

    private static void DrawTextContent(Image<Rgba32> image, PageOgPayload payload, FontFamily boldFamily, FontFamily regularFamily, Color accent)
    {
        var kickerFont = boldFamily.CreateFont(26, FontStyle.Bold);
        var titleFont = FitFontToWidth(boldFamily, payload.Title, 82f, 56f, ContentWidth);
        var bodyFont = regularFamily.CreateFont(33f, FontStyle.Regular);
        var footerFont = boldFamily.CreateFont(22f, FontStyle.Bold);

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(accent.ToPixel<Rgba32>().R, accent.ToPixel<Rgba32>().G, accent.ToPixel<Rgba32>().B, 210),
                new RectangularPolygon(ContentX, ContentTop + 90f, 170f, 5f));

            ctx.DrawText(new RichTextOptions(kickerFont)
            {
                Origin = new PointF(ContentX, ContentTop + 26f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, payload.Kicker.ToUpperInvariant(), accent);

            DrawTextShadow(ctx, payload.Title, titleFont, new PointF(ContentX, ContentTop + 124f), HorizontalAlignment.Left, 3f);
            DrawWrappedText(ctx, payload.Title, titleFont, TitleColor, new PointF(ContentX, ContentTop + 124f), ContentWidth, 2, 86f);

            DrawWrappedText(ctx, payload.Description, bodyFont, BodyColor, new PointF(ContentX, ContentTop + 322f), 860f, 2, 43f);

            ctx.DrawText(new RichTextOptions(footerFont)
            {
                Origin = new PointF(ContentX, 548f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "longevityworldcup.com", MutedColor);
        });
    }

    private static void DrawFooterChips(Image<Rgba32> image, PageOgPayload payload, FontFamily boldFamily, Color accent)
    {
        var chipFont = boldFamily.CreateFont(24f, FontStyle.Bold);
        const float chipY = 474f;
        const float chipH = 58f;
        const float gap = 18f;
        var chipW = (ContentWidth - (gap * 2)) / 3f;

        image.Mutate(ctx =>
        {
            for (var i = 0; i < payload.Stats.Count; i++)
            {
                var x = ContentX + (i * (chipW + gap));
                ctx.Fill(ChipFillColor, new RectangularPolygon(x, chipY, chipW, chipH));
                ctx.Draw(ChipStrokeColor, 1f, new RectangularPolygon(x, chipY, chipW, chipH));
                ctx.Fill(new Rgba32(accent.ToPixel<Rgba32>().R, accent.ToPixel<Rgba32>().G, accent.ToPixel<Rgba32>().B, 230),
                    new RectangularPolygon(x, chipY, 5f, chipH));

                var text = payload.Stats[i];
                var font = FitFontToWidth(boldFamily, text, 24f, 18f, chipW - 34f);
                ctx.DrawText(new RichTextOptions(font)
                {
                    Origin = new PointF(x + 22f, chipY + 16f),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, text, Color.White);
            }
        });
    }

    private static void DrawBackground(Image<Rgba32> image, Color accentColor)
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
                    var baseR = Lerp(top.R, bottom.R, vertical);
                    var baseG = Lerp(top.G, bottom.G, vertical);
                    var baseB = Lerp(top.B, bottom.B, vertical);

                    var edgeLight = MathF.Max(0f, 1f - MathF.Sqrt(MathF.Pow((horizontal - 0.78f) / 0.52f, 2f) + MathF.Pow((vertical - 0.24f) / 0.64f, 2f)));
                    var laneLight = MathF.Max(0f, 1f - MathF.Abs((horizontal + vertical * 0.36f) - 0.35f) / 0.08f) * 0.16f;
                    var accentLight = MathF.Min(0.20f, edgeLight * 0.18f + laneLight);

                    row[x] = new Rgba32(
                        (byte)Math.Clamp(baseR + (accent.R * accentLight), 0, 255),
                        (byte)Math.Clamp(baseG + (accent.G * accentLight), 0, 255),
                        (byte)Math.Clamp(baseB + (accent.B * accentLight), 0, 255),
                        255);
                }
            }
        });

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(0, 0, 0, 88), new RectangularPolygon(0, 0, CanvasWidth, 58f));
            ctx.Fill(new Rgba32(255, 255, 255, 16), new RectangularPolygon(0, CanvasHeight - 58f, CanvasWidth, 1f));
        });
    }

    private (FontFamily Bold, FontFamily Regular) GetFontFamilies()
    {
        if (_fontsLoaded)
            return (_boldFamily, _regularFamily);

        _boldFamily = _fonts.Add(_boldFontPath);
        _regularFamily = _fonts.Add(_regularFontPath);
        _fontsLoaded = true;
        return (_boldFamily, _regularFamily);
    }

    private string ComputeSignature(PageOgDefinition definition)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;
        var boldFontTicks = File.Exists(_boldFontPath) ? File.GetLastWriteTimeUtc(_boldFontPath).Ticks : 0L;
        var regularFontTicks = File.Exists(_regularFontPath) ? File.GetLastWriteTimeUtc(_regularFontPath).Ticks : 0L;

        var raw = string.Join("|",
            "page-og-v2",
            definition.Slug,
            definition.Kicker,
            definition.Title,
            definition.Description,
            definition.AccentHex,
            string.Join(",", definition.Stats),
            logoTicks.ToString(CultureInfo.InvariantCulture),
            boldFontTicks.ToString(CultureInfo.InvariantCulture),
            regularFontTicks.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private void CleanupOldRenders(string slug, string keepFullPath)
    {
        try
        {
            var prefix = $"{slug}-";
            var keepName = IOPath.GetFileName(keepFullPath);
            foreach (var file in Directory.EnumerateFiles(_outputDir, $"{prefix}*.png", SearchOption.TopDirectoryOnly))
            {
                var fileName = IOPath.GetFileName(file);
                if (string.Equals(fileName, keepName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Failed to delete stale page OG render {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to cleanup stale page OG renders for {Slug}", slug);
        }
    }

    private static string BuildTempRenderPath(string outputPath)
    {
        return $"{outputPath}.{Guid.NewGuid():N}.tmp";
    }

    private static void PublishTempRender(string tempPath, string outputPath)
    {
        try
        {
            File.Move(tempPath, outputPath);
        }
        catch (IOException) when (File.Exists(outputPath))
        {
            // Another app instance finished the same render first.
        }
    }

    private static void DeleteTempRender(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
        catch
        {
            // Best-effort cleanup for abandoned temp renders.
        }
    }

    private static string NormalizeSlug(string slug)
    {
        return slug.Trim().ToLowerInvariant();
    }

    private static Font FitFontToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth)
    {
        var size = startSize;
        while (size > minSize)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
                return font;
            size -= 2f;
        }

        return family.CreateFont(minSize, FontStyle.Bold);
    }

    private static void DrawWrappedText(
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

    private static void DrawTextShadow(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        HorizontalAlignment alignment,
        float offset)
    {
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = new PointF(origin.X, origin.Y + offset),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, text, ShadowColor);
    }

    private static int Lerp(byte a, byte b, float t)
    {
        return (int)MathF.Round(a + ((b - a) * t));
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }
}
