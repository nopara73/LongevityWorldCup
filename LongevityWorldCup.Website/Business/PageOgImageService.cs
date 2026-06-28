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
    private const float ContentX = 78f;
    private const float AccentY = 192f;
    private const float TitleY = 258f;
    private const float TitleWidth = 760f;
    private const float ContentPanelWidth = ContentX + TitleWidth + 48f;

    private static readonly Color BackgroundTop = ParseHex("030708");
    private static readonly Color BackgroundBottom = ParseHex("111515");
    private static readonly Color TitleColor = Color.White;
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
            ["longevitymaxxing"] = new(
                "longevitymaxxing",
                "Longevitymaxxing Challenge",
                "The first muscle to train is your mind",
                "Start longevitymaxxing today",
                "78DA3B",
                ["Daily check-ins", "Join anytime", "No finish line"]),
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
            ["view-improvement"] = new(
                "view-improvement",
                "Progress view",
                "Improvement leaderboard",
                "Track Pheno Improvement from each athlete's worst eligible result to latest eligible result.",
                "78DA3B",
                ["Pheno Improvement", "Latest result", "Worst result"]),
            ["view-bortz-improvement"] = new(
                "view-bortz-improvement",
                "Pro progress view",
                "Bortz Improvement leaderboard",
                "Track Bortz Improvement from each athlete's worst eligible result to latest eligible result.",
                "FFB020",
                ["Bortz Improvement", "Pro track", "Latest result"]),
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

        await DrawLogoMarksAsync(image, ct);
        DrawHeaderText(image, boldFamily);
        DrawTextContent(image, payload, boldFamily, regularFamily, accent);

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private async Task DrawLogoMarksAsync(Image<Rgba32> image, CancellationToken ct)
    {
        try
        {
            using var logo = await LoadLogoMarkAsync(ct);
            using var smallLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(50, 50),
                Mode = ResizeMode.Max
            }));
            using var backgroundLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(820, 820),
                Mode = ResizeMode.Max
            }));

            image.Mutate(ctx =>
            {
                ctx.DrawImage(backgroundLogo, new Point(884, -24), 0.20f);
                ctx.DrawImage(smallLogo, new Point((int)ContentX, 52), 0.98f);
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw page OG logo.");
        }
    }

    private async Task<Image<Rgba32>> LoadLogoMarkAsync(CancellationToken ct)
    {
        await using var logoStream = File.OpenRead(_logoPath);
        var logo = await Image.LoadAsync<Rgba32>(logoStream, ct);
        logo.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    var pixel = row[x];
                    var brightness = (pixel.R + pixel.G + pixel.B) / 3f;
                    if (brightness < 110f)
                    {
                        row[x] = Color.Transparent;
                        continue;
                    }

                    var alpha = (byte)Math.Clamp((brightness - 110f) * 2.4f, 0f, pixel.A);
                    row[x] = new Rgba32(255, 255, 255, alpha);
                }
            }
        });

        var bounds = FindVisibleBounds(logo);
        if (bounds.Width > 0 && bounds.Height > 0)
            logo.Mutate(ctx => ctx.Crop(bounds));

        return logo;
    }

    private static Rectangle FindVisibleBounds(Image<Rgba32> image)
    {
        var minX = image.Width;
        var minY = image.Height;
        var maxX = -1;
        var maxY = -1;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A == 0)
                        continue;

                    minX = Math.Min(minX, x);
                    minY = Math.Min(minY, y);
                    maxX = Math.Max(maxX, x);
                    maxY = Math.Max(maxY, y);
                }
            }
        });

        return maxX < minX || maxY < minY
            ? new Rectangle()
            : new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static void DrawHeaderText(Image<Rgba32> image, FontFamily boldFamily)
    {
        var brandFont = boldFamily.CreateFont(20, FontStyle.Bold);
        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(brandFont)
            {
                Origin = new PointF(ContentX + 62f, 54f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "LONGEVITY\nWORLD CUP", Color.White);
        });
    }

    private static void DrawTextContent(Image<Rgba32> image, PageOgPayload payload, FontFamily boldFamily, FontFamily regularFamily, Color accent)
    {
        var titleFont = FitFontToWidth(boldFamily, payload.Title, 74f, 50f, TitleWidth);
        var subtitleFont = regularFamily.CreateFont(32, FontStyle.Regular);

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(accent.ToPixel<Rgba32>().R, accent.ToPixel<Rgba32>().G, accent.ToPixel<Rgba32>().B, 220),
                new RectangularPolygon(ContentX, AccentY, 126f, 5f));

            DrawWrappedText(ctx, payload.Title, titleFont, ShadowColor, new PointF(ContentX, TitleY + 3f), TitleWidth, 2, 78f);
            DrawWrappedText(ctx, payload.Title, titleFont, TitleColor, new PointF(ContentX, TitleY), TitleWidth, 2, 78f);
            if (string.Equals(payload.Slug, "longevitymaxxing", StringComparison.OrdinalIgnoreCase))
            {
                ctx.DrawText(new RichTextOptions(subtitleFont)
                {
                    Origin = new PointF(ContentX, TitleY + 174f),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, payload.Description, accent);
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
                    var laneLight = MathF.Max(0f, 1f - MathF.Abs((horizontal + vertical * 0.36f) - 0.26f) / 0.08f) * 0.10f;
                    var accentLight = MathF.Min(0.16f, edgeLight * 0.12f + laneLight);

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
            ctx.Fill(new Rgba32(0, 0, 0, 92), new RectangularPolygon(0, 0, ContentPanelWidth, CanvasHeight));
            ctx.Fill(new Rgba32(255, 255, 255, 12), new RectangularPolygon(ContentPanelWidth, 0, 1f, CanvasHeight));
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
            "page-og-v10",
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

    private static int Lerp(byte a, byte b, float t)
    {
        return (int)MathF.Round(a + ((b - a) * t));
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }
}
