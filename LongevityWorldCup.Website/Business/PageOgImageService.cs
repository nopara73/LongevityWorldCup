using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using LongevityWorldCup.Website.Tools;
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
    private const float TitleWidth = 820f;

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
            ["rejuvenation-olympics"] = new(
                "rejuvenation-olympics",
                "Coming from the Rejuvenation Olympics?",
                "Keep competing.",
                "Put your biological age on the leaderboard.",
                "19C3D1",
                ["Bloodwork checklist", "Pheno Age", "Bortz Age"]),
            ["longevitymaxxing"] = new(
                "longevitymaxxing",
                "Longevitymaxxing Challenge",
                "The first muscle to train is your mind",
                "Start longevitymaxxing today",
                "78DA3B",
                ["Daily check-ins", "Join anytime", "No finish line"]),
            ["pheno-age"] = new(
                "pheno-age",
                "Pheno Age",
                "Pheno Age Calculator",
                "Calculate Pheno Age from nine blood biomarkers and your age using the Longevity World Cup calculator.",
                "78DA3B",
                ["Pheno Age", "Amateur track", "Public leaderboard"]),
            ["bortz-age"] = new(
                "bortz-age",
                "Bortz Age",
                "Bortz Age Calculator",
                "Calculate Bortz Age from blood biomarkers and your age using the Longevity World Cup calculator.",
                "FFB020",
                ["Bortz Age", "Pro track", "Pheno Age + Bortz Age"]),
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
                "Pheno Improvement leaderboard",
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
    private readonly ShareCardCanvas.Fonts _fonts;
    private readonly string _outputDir;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public PageOgImageService(IWebHostEnvironment env, ILogger<PageOgImageService> log)
    {
        _env = env;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _fonts = ShareCardCanvas.GetFonts(_env.WebRootPath);
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
        File.Exists(_logoPath) && _fonts.Exist;

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

            var tempPath = ImageRenderFiles.CreateTempPath(outputPath);
            try
            {
                await RenderImageAsync(payload, tempPath, ct);
                ImageRenderFiles.Publish(tempPath, outputPath);
            }
            finally
            {
                ImageRenderFiles.DeleteTemp(tempPath);
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
        if (CustomEventArtwork.ForUrl("https://longevityworldcup.com/" + payload.Slug) is { } artwork)
        {
            using var original = await ImageInput.LoadAsync<Rgba32>(artwork.FullPath(_env.WebRootPath), ct);
            original.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(CanvasWidth, CanvasHeight), Mode = ResizeMode.Pad, PadColor = Color.ParseHex("#101c22")
            }));
            await original.SaveAsPngAsync(outputPath, ct);
            return;
        }
        var accent = Color.ParseHex("#" + payload.AccentHex);
        using var image = new Image<Rgba32>(ShareCardCanvas.Width, ShareCardCanvas.Height);
        ShareCardCanvas.DrawBackground(image, accent);
        await DrawEmblemAsync(image, ct);
        await ShareCardCanvas.DrawMastheadAsync(image, _logoPath, _fonts, caption: null, accent, _log, ct, watermark: false);
        DrawTextContent(image, payload, accent);
        ShareCardCanvas.DrawAccentBar(image, accent);

        await image.SaveAsPngAsync(outputPath, ct);
    }

    // The site's emblem, cropped tight, rises from the lower right as the card's one picture.
    private async Task DrawEmblemAsync(Image<Rgba32> image, CancellationToken ct)
    {
        try
        {
            using var logo = await LoadLogoMarkAsync(ct);
            using var emblem = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(560, 560),
                Mode = ResizeMode.Max
            }));
            image.Mutate(ctx => ctx.DrawImage(emblem, new Point(ShareCardCanvas.Width - emblem.Width + 40, (int)ShareCardCanvas.BodyTop + 20), 0.14f));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw page OG logo.");
        }
    }

    private async Task<Image<Rgba32>> LoadLogoMarkAsync(CancellationToken ct)
    {
        var logo = await ImageLogo.LoadMarkAsync(_logoPath, ct);

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

    private void DrawTextContent(Image<Rgba32> image, PageOgPayload payload, Color accent)
    {
        var (lines, titleFont) = ShareCardCanvas.FitHeadline(_fonts, payload.Title, 88f, 72f, TitleWidth, 2, 60f);
        var lineHeight = titleFont.Size * 1.1f;
        var hasDescription = string.Equals(payload.Slug, "longevitymaxxing", StringComparison.OrdinalIgnoreCase);
        var descriptionFont = _fonts.Regular.CreateFont(36f, FontStyle.Regular);
        const float ruleGap = 34f;
        const float descriptionGap = 26f;
        var blockHeight = 6f + ruleGap + (lines.Count * lineHeight) + (hasDescription ? descriptionGap + 44f : 0f);
        var top = ((ShareCardCanvas.BodyTop + ShareCardCanvas.BodyBottom) / 2f) - (blockHeight / 2f);

        image.Mutate(ctx =>
        {
            ctx.Fill(ShareCardCanvas.WithAlpha(accent, 235), new RectangularPolygon(ShareCardCanvas.Margin, top, 120f, 6f));
            var titleY = top + 6f + ruleGap;
            ShareCardCanvas.DrawLines(ctx, _fonts, lines, titleFont, ShareCardCanvas.Ink, ShareCardCanvas.Margin, titleY, lineHeight);
            if (hasDescription)
            {
                ctx.DrawText(new RichTextOptions(descriptionFont)
                {
                    Origin = new PointF(ShareCardCanvas.Margin, titleY + (lines.Count * lineHeight) + descriptionGap),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, payload.Description, accent);
            }
        });
    }

    private string ComputeSignature(PageOgDefinition definition)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;

        var raw = string.Join("|",
            "page-og-v13",
            definition.Slug,
            definition.Kicker,
            definition.Title,
            definition.Description,
            definition.AccentHex,
            string.Join(",", definition.Stats),
            logoTicks.ToString(CultureInfo.InvariantCulture),
            _fonts.Ticks);

        if (CustomEventArtwork.ForUrl("https://longevityworldcup.com/" + definition.Slug) is { } artwork)
        {
            var path = artwork.FullPath(_env.WebRootPath);
            raw += "|linked-artwork-v1|" + (File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0L).ToString(CultureInfo.InvariantCulture);
        }
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

    private static string NormalizeSlug(string slug)
    {
        return slug.Trim().ToLowerInvariant();
    }
}
