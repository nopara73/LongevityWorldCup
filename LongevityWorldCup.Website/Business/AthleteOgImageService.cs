using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using IOPath = System.IO.Path;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public sealed class AthleteOgImageService
{
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 630;

    // Layout values from the approved design.
    private const int ProfileSize = 381;
    private const int ProfileX = 410;
    private const int ProfileY = 92;
    private const int ProfileBleed = 2;
    private const float NameTop = ProfileY + ProfileSize + 20f;
    private const float RankX = 60f;
    private const float RankY = 245f;
    private const float ReductionX = 910f;
    private const float ReductionY = 245f;

    private static readonly Color RankColor = ParseHex("FF4081");
    private static readonly Color ReductionColor = ParseHex("78DA3B");
    private static readonly Color NameColor = Color.White;

    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<AthleteOgImageService> _log;
    private readonly string _templatePath;
    private readonly string _fontPath;
    private readonly string _outputDir;
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private FontFamily _fontFamily;
    private bool _fontLoaded;

    public AthleteOgImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<AthleteOgImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _templatePath = IOPath.Combine(_env.WebRootPath, "assets", "og_athlete_template.png");
        _fontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "athlete");
    }

    public sealed record AthleteOgPayload(
        string InternalSlug,
        string RouteSlug,
        string Name,
        int Rank,
        double AgeReduction,
        string? ProfilePicUrl,
        string Signature);

    public bool IsConfigured => File.Exists(_templatePath) && File.Exists(_fontPath);

    public bool TryGetCurrentPayload(string rawSlug, out AthleteOgPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(rawSlug))
            return false;

        var normalized = NormalizeSlug(rawSlug);
        var rankings = _athletes.GetRankingsOrder();
        JsonObject? rankingRow = null;
        var rank = 0;
        foreach (var row in rankings.OfType<JsonObject>())
        {
            rank++;
            var rowSlug = NormalizeSlug(row["AthleteSlug"]?.GetValue<string>());
            if (string.Equals(rowSlug, normalized, StringComparison.Ordinal))
            {
                rankingRow = row;
                break;
            }
        }

        if (rankingRow is null)
            return false;

        var snapshot = _athletes.GetAthletesSnapshot();
        var athleteJson = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                NormalizeSlug(o["AthleteSlug"]?.GetValue<string>()),
                normalized,
                StringComparison.Ordinal));

        var nameFromRanking = rankingRow["Name"]?.GetValue<string>();
        var nameFromAthlete = athleteJson?["DisplayName"]?.GetValue<string>() ?? athleteJson?["Name"]?.GetValue<string>();
        var name = !string.IsNullOrWhiteSpace(nameFromAthlete)
            ? nameFromAthlete!.Trim()
            : !string.IsNullOrWhiteSpace(nameFromRanking)
                ? nameFromRanking!.Trim()
                : ToDisplayName(rawSlug);

        var ageReduction = GetDouble(rankingRow, "AgeDifference") ?? 0d;
        var profilePicUrl = athleteJson?["ProfilePic"]?.GetValue<string>();
        var signature = ComputeSignature(normalized, rank, ageReduction, name, profilePicUrl);

        payload = new AthleteOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            Name: name,
            Rank: rank,
            AgeReduction: ageReduction,
            ProfilePicUrl: profilePicUrl,
            Signature: signature);

        return true;
    }

    public string BuildVersionedImageUrl(string siteBaseUrl, AthleteOgPayload payload)
    {
        return $"{siteBaseUrl}/og/athlete/{payload.RouteSlug}.png?v={payload.Signature}";
    }

    public async Task<string?> EnsureRenderedImageAsync(AthleteOgPayload payload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        Directory.CreateDirectory(_outputDir);
        var outputPath = IOPath.Combine(_outputDir, $"{payload.InternalSlug}-{payload.Signature}.png");
        if (File.Exists(outputPath))
        {
            CleanupOldRenders(payload.InternalSlug, outputPath);
            return outputPath;
        }

        await _renderLock.WaitAsync(ct);
        try
        {
            if (File.Exists(outputPath))
            {
                CleanupOldRenders(payload.InternalSlug, outputPath);
                return outputPath;
            }

            await RenderImageAsync(payload, outputPath, ct);
            CleanupOldRenders(payload.InternalSlug, outputPath);
            return outputPath;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task RenderImageAsync(AthleteOgPayload payload, string outputPath, CancellationToken ct)
    {
        await using var templateStream = File.OpenRead(_templatePath);
        using var image = await Image.LoadAsync<Rgba32>(templateStream, ct);

        if (image.Width != CanvasWidth || image.Height != CanvasHeight)
        {
            image.Mutate(ctx => ctx.Resize(CanvasWidth, CanvasHeight));
        }

        var profilePath = ResolveProfilePath(payload.ProfilePicUrl);
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath))
        {
            try
            {
                await using var profileStream = File.OpenRead(profilePath);
                using var profile = await Image.LoadAsync<Rgba32>(profileStream, ct);
                var renderSize = ProfileSize + (ProfileBleed * 2);
                profile.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(renderSize, renderSize),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
                using var mask = new Image<Rgba32>(renderSize, renderSize, Color.Transparent);
                mask.Mutate(ctx =>
                {
                    ctx.SetGraphicsOptions(new GraphicsOptions
                    {
                        Antialias = true,
                        AntialiasSubpixelDepth = 16
                    });
                    ctx.Fill(Color.White, new EllipsePolygon(renderSize / 2f, renderSize / 2f, renderSize / 2f));
                });

                profile.Mutate(ctx =>
                {
                    ctx.SetGraphicsOptions(new GraphicsOptions
                    {
                        AlphaCompositionMode = PixelAlphaCompositionMode.DestIn,
                        Antialias = true,
                        AntialiasSubpixelDepth = 16
                    });
                    ctx.DrawImage(mask, new Point(0, 0), 1f);
                });

                image.Mutate(ctx => ctx.DrawImage(profile, new Point(ProfileX - ProfileBleed, ProfileY - ProfileBleed), 1f));
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render athlete profile image for {Slug}", payload.InternalSlug);
            }
        }

        var fontFamily = GetFontFamily();
        var metricFont = fontFamily.CreateFont(100, FontStyle.Bold);
        var nameFont = fontFamily.CreateFont(65, FontStyle.Bold);

        var rankText = $"#{payload.Rank}";
        var reductionText = FormatReduction(payload.AgeReduction);
        const float rightMetricEdgeX = 1148f;
        var reductionOptions = new RichTextOptions(metricFont);
        var reductionAdvance = TextMeasurer.MeasureSize(reductionText, reductionOptions);
        var reductionInkBounds = TextMeasurer.MeasureBounds(reductionText, reductionOptions);
        var rightBearing = reductionAdvance.Width - (reductionInkBounds.X + reductionInkBounds.Width);
        var reductionOriginX = rightMetricEdgeX + rightBearing;

        image.Mutate(ctx =>
        {
            ctx.DrawText(
                new RichTextOptions(metricFont)
                {
                    Origin = new PointF(RankX, RankY),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                rankText,
                RankColor);

            ctx.DrawText(
                new RichTextOptions(metricFont)
                {
                    Origin = new PointF(reductionOriginX, ReductionY),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top
                },
                reductionText,
                ReductionColor);
        });

        var nameToDraw = payload.Name;
        while (true)
        {
            var measurement = TextMeasurer.MeasureSize(nameToDraw, new RichTextOptions(nameFont));
            if (measurement.Width <= 1120f || nameFont.Size <= 42f)
                break;
            nameFont = fontFamily.CreateFont(nameFont.Size - 2f, FontStyle.Bold);
        }

        var nameSize = TextMeasurer.MeasureSize(nameToDraw, new RichTextOptions(nameFont));
        var nameX = (CanvasWidth - nameSize.Width) / 2f;
        image.Mutate(ctx =>
        {
            ctx.DrawText(
                new RichTextOptions(nameFont)
                {
                    Origin = new PointF(nameX, NameTop),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                nameToDraw,
                NameColor);
        });

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private FontFamily GetFontFamily()
    {
        if (_fontLoaded)
            return _fontFamily;

        _fontFamily = _fonts.Add(_fontPath);
        _fontLoaded = true;
        return _fontFamily;
    }

    private string ComputeSignature(string normalizedSlug, int rank, double ageReduction, string name, string? profilePicUrl)
    {
        var templateTicks = File.Exists(_templatePath) ? File.GetLastWriteTimeUtc(_templatePath).Ticks : 0L;
        var fontTicks = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0L;
        var profilePath = ResolveProfilePath(profilePicUrl);
        var profileTicks = !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)
            ? File.GetLastWriteTimeUtc(profilePath).Ticks
            : 0L;

        var raw = string.Join("|",
            "athlete-og-v7",
            normalizedSlug,
            rank.ToString(CultureInfo.InvariantCulture),
            ageReduction.ToString("0.0000", CultureInfo.InvariantCulture),
            name,
            profilePicUrl ?? "",
            templateTicks.ToString(CultureInfo.InvariantCulture),
            fontTicks.ToString(CultureInfo.InvariantCulture),
            profileTicks.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private string? ResolveProfilePath(string? profilePicUrl)
    {
        if (string.IsNullOrWhiteSpace(profilePicUrl))
            return null;

        var relative = profilePicUrl.Trim().TrimStart('/').Replace('/', IOPath.DirectorySeparatorChar);
        return IOPath.Combine(_env.WebRootPath, relative);
    }

    private void CleanupOldRenders(string internalSlug, string keepFullPath)
    {
        try
        {
            var prefix = $"{internalSlug}-";
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
                    _log.LogDebug(ex, "Failed to delete stale OG render {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to cleanup stale OG renders for {Slug}", internalSlug);
        }
    }

    private static double? GetDouble(JsonObject obj, string key)
    {
        if (obj[key] is JsonValue jv && jv.TryGetValue<double>(out var v))
            return v;
        return null;
    }

    private static string NormalizeSlug(string? slug)
    {
        return (slug ?? "").Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static string ToRouteSlug(string normalizedSlug)
    {
        return normalizedSlug.Replace('_', '-');
    }

    private static string ToDisplayName(string slug)
    {
        var parts = NormalizeSlug(slug).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return slug;
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string FormatReduction(double value)
    {
        var abs = Math.Abs(value);
        var sign = value < 0 ? "-" : "+";
        return $"{sign} {abs:0.0}";
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }

}
