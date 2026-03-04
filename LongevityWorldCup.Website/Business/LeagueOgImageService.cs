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

public sealed class LeagueOgImageService
{
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 630;
    private const float LeagueTitleY = 66f;
    private static readonly Color LeagueTitleColor = Color.White;
    private static readonly Color NameLabelColor = new(new Rgba32(255, 255, 255, 92));
    // Figma nominal inner-shadow is 30%, but our rasterized pipeline needs lower effective
    // alpha to visually match the reference.
    private static readonly Rgba32 NameLabelInnerShadowColor = new(0, 0, 0, 70);

    // Slot centers and diameters matched to the league OG podium template.
    private static readonly Slot[] PodiumSlots =
    [
        // From Figma node 73:24 (og_league_example_image_pos):
        // gold: x=495 y=174 w=210 h=210
        // silver: x=248 y=250 w=171 h=171
        // bronze: x=782 y=251 w=171 h=171
        new Slot(600, 279, 210), // #1 center
        new Slot(333, 336, 170), // #2 left
        new Slot(867, 337, 170)  // #3 right
    ];

    private static readonly IReadOnlyDictionary<string, string> LeagueDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ultimate"] = "Ultimate League",
            ["amateur"] = "Amateur League",
            ["womens"] = "Women's League",
            ["mens"] = "Men's League",
            ["open"] = "Open League",
            ["silent-generation"] = "Silent Generation League",
            ["baby-boomers"] = "Baby Boomers League",
            ["gen-x"] = "Gen X League",
            ["millennials"] = "Millennials League",
            ["gen-z"] = "Gen Z League",
            ["gen-alpha"] = "Gen Alpha League",
            ["prosperan"] = "Prosperan League"
        };

    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<LeagueOgImageService> _log;
    private readonly string _templatePath;
    private readonly string _fontPath;
    private readonly string _outputDir;
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private FontFamily _fontFamily;
    private bool _fontLoaded;

    public LeagueOgImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<LeagueOgImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _templatePath = IOPath.Combine(_env.WebRootPath, "assets", "og_league_template.png");
        _fontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "league");
    }

    public sealed record LeagueOgPayload(
        string InternalSlug,
        string RouteSlug,
        string DisplayName,
        IReadOnlyList<string> Top3Slugs,
        IReadOnlyList<string> Top3Names,
        string Signature);

    private readonly record struct Slot(int CenterX, int CenterY, int Diameter);

    public bool IsConfigured => File.Exists(_templatePath) && File.Exists(_fontPath);

    public static bool TryNormalizeLeagueSlug(string? raw, out string slug)
    {
        slug = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        var candidate = NormalizeToken(raw);
        if (LeagueDisplayNames.ContainsKey(candidate))
        {
            slug = candidate;
            return true;
        }

        candidate = candidate
            .Replace(" league", "", StringComparison.Ordinal)
            .Replace('_', '-');
        if (LeagueDisplayNames.ContainsKey(candidate))
        {
            slug = candidate;
            return true;
        }

        var mapped = candidate switch
        {
            "women's" => "womens",
            "womens" => "womens",
            "men's" => "mens",
            "mens" => "mens",
            "silent generation" => "silent-generation",
            "baby boomers" => "baby-boomers",
            "gen x" => "gen-x",
            "gen z" => "gen-z",
            "gen alpha" => "gen-alpha",
            _ => ""
        };

        if (!string.IsNullOrWhiteSpace(mapped) && LeagueDisplayNames.ContainsKey(mapped))
        {
            slug = mapped;
            return true;
        }

        return false;
    }

    public static bool TryGetLeagueDisplayName(string? rawSlug, out string displayName)
    {
        displayName = "";
        if (!TryNormalizeLeagueSlug(rawSlug, out var slug))
            return false;

        displayName = LeagueDisplayNames[slug];
        return true;
    }

    public bool TryGetCurrentPayload(string rawLeagueSlug, out LeagueOgPayload payload)
    {
        payload = null!;
        if (!TryNormalizeLeagueSlug(rawLeagueSlug, out var normalized))
            return false;

        var top3Slugs = _athletes.GetTop3SlugsForLeague(normalized)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(3)
            .Select(NormalizeAthleteSlug)
            .ToArray();

        var top3Names = GetAthleteDisplayNames(top3Slugs);
        var displayName = LeagueDisplayNames[normalized];
        var signature = ComputeSignature(normalized, displayName, top3Slugs);

        payload = new LeagueOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            DisplayName: displayName,
            Top3Slugs: top3Slugs,
            Top3Names: top3Names,
            Signature: signature);
        return true;
    }

    public string BuildVersionedImageUrl(string siteBaseUrl, LeagueOgPayload payload)
    {
        return $"{siteBaseUrl}/og/league/{payload.RouteSlug}.png?v={payload.Signature}";
    }

    public async Task<string?> EnsureRenderedImageAsync(LeagueOgPayload payload, CancellationToken ct = default)
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

    private async Task RenderImageAsync(LeagueOgPayload payload, string outputPath, CancellationToken ct)
    {
        await using var templateStream = File.OpenRead(_templatePath);
        using var image = await Image.LoadAsync<Rgba32>(templateStream, ct);
        if (image.Width != CanvasWidth || image.Height != CanvasHeight)
        {
            image.Mutate(ctx => ctx.Resize(CanvasWidth, CanvasHeight));
        }

        for (var i = 0; i < payload.Top3Slugs.Count && i < PodiumSlots.Length; i++)
        {
            var slot = PodiumSlots[i];
            if (!TryResolveProfilePath(payload.Top3Slugs[i], out var profilePath))
                continue;

            try
            {
                await using var profileStream = File.OpenRead(profilePath);
                using var profile = await Image.LoadAsync<Rgba32>(profileStream, ct);
                DrawCircularProfile(image, profile, slot);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render league top3 profile image for {LeagueSlug}: {Path}", payload.InternalSlug, profilePath);
            }
        }

        var fontFamily = GetFontFamily();
        var titleFont = fontFamily.CreateFont(50f, FontStyle.Bold);
        var title = payload.DisplayName;

        while (true)
        {
            var measurement = TextMeasurer.MeasureSize(title, new RichTextOptions(titleFont));
            if (measurement.Width <= 1080f || titleFont.Size <= 34f)
                break;
            titleFont = fontFamily.CreateFont(titleFont.Size - 1f, FontStyle.Bold);
        }

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(titleFont)
            {
                Origin = new PointF(CanvasWidth / 2f, LeagueTitleY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, title, LeagueTitleColor);
        });

        DrawAthleteNameLabels(image, payload.Top3Names, fontFamily);

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private static void DrawAthleteNameLabels(Image<Rgba32> image, IReadOnlyList<string> top3Names, FontFamily fontFamily)
    {
        var firstName = top3Names.Count > 0 ? top3Names[0] : "";
        var secondName = top3Names.Count > 1 ? top3Names[1] : "";
        var thirdName = top3Names.Count > 2 ? top3Names[2] : "";

        var firstFont = fontFamily.CreateFont(25f, FontStyle.Bold);
        var sideFont = fontFamily.CreateFont(20f, FontStyle.Bold);

        // Figma y positions:
        // center label top: 444
        // side labels top: 466
        DrawCenteredLabel(image, firstName, firstFont, PodiumSlots[0].CenterX, 444f);
        DrawCenteredLabel(image, secondName, sideFont, PodiumSlots[1].CenterX, 466f);
        DrawCenteredLabel(image, thirdName, sideFont, PodiumSlots[2].CenterX, 466f);
    }

    private static void DrawCenteredLabel(Image<Rgba32> image, string text, Font font, float centerX, float topY)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        // Figma inner shadow:
        // X=0, Y=4, Blur=4, Spread=0, Color=#000000 @30%
        var textSize = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
        const int pad = 16; // enough room for blur radius
        var boxW = Math.Max(1, (int)Math.Ceiling(textSize.Width) + (pad * 2));
        var boxH = Math.Max(1, (int)Math.Ceiling(textSize.Height) + (pad * 2));
        var localOrigin = new PointF(boxW / 2f, pad);

        using var baseMask = new Image<Rgba32>(boxW, boxH, Color.Transparent);
        baseMask.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = localOrigin,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, text, Color.White);
        });

        using var innerShadowMask = new Image<Rgba32>(boxW, boxH, Color.Transparent);
        innerShadowMask.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(localOrigin.X + 0f, localOrigin.Y + 4f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, text, Color.White);
            ctx.GaussianBlur(4f);
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                AlphaCompositionMode = PixelAlphaCompositionMode.DestIn
            });
            ctx.DrawImage(baseMask, new Point(0, 0), 1f);
        });

        TintMask(innerShadowMask, NameLabelInnerShadowColor);
        var drawX = (int)Math.Round(centerX - (boxW / 2f));
        var drawY = (int)Math.Round(topY - pad);
        image.Mutate(ctx =>
        {
            // Draw fill first, then inner shadow on top (still clipped to glyph),
            // which matches the stronger engraved look in Figma.
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(centerX, topY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, text, NameLabelColor);
            ctx.DrawImage(innerShadowMask, new Point(drawX, drawY), 1f);
        });
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
                    var a = row[x].A;
                    if (a == 0)
                    {
                        row[x] = default;
                        continue;
                    }

                    var scaled = (byte)((a * color.A) / 255);
                    row[x] = new Rgba32(color.R, color.G, color.B, scaled);
                }
            }
        });
    }

    private static void DrawCircularProfile(Image<Rgba32> target, Image<Rgba32> source, Slot slot)
    {
        var renderSize = Math.Max(1, slot.Diameter);
        source.Mutate(ctx => ctx.Resize(new ResizeOptions
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

        source.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                AlphaCompositionMode = PixelAlphaCompositionMode.DestIn,
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.DrawImage(mask, new Point(0, 0), 1f);
        });

        var x = slot.CenterX - (renderSize / 2);
        var y = slot.CenterY - (renderSize / 2);
        target.Mutate(ctx => ctx.DrawImage(source, new Point(x, y), 1f));
    }

    private FontFamily GetFontFamily()
    {
        if (_fontLoaded)
            return _fontFamily;

        _fontFamily = _fonts.Add(_fontPath);
        _fontLoaded = true;
        return _fontFamily;
    }

    private string ComputeSignature(string leagueSlug, string leagueDisplayName, IReadOnlyList<string> top3Slugs)
    {
        var templateTicks = File.Exists(_templatePath) ? File.GetLastWriteTimeUtc(_templatePath).Ticks : 0L;
        var fontTicks = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0L;
        var top3ProfileTicks = top3Slugs.Select(GetProfileTicks).ToArray();

        var raw = string.Join("|",
            "league-og-v18",
            leagueSlug,
            leagueDisplayName,
            string.Join(",", top3Slugs),
            string.Join(",", top3ProfileTicks.Select(t => t.ToString(CultureInfo.InvariantCulture))),
            templateTicks.ToString(CultureInfo.InvariantCulture),
            fontTicks.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private long GetProfileTicks(string athleteSlug)
    {
        return TryResolveProfilePath(athleteSlug, out var path)
            ? File.GetLastWriteTimeUtc(path).Ticks
            : 0L;
    }

    private bool TryResolveProfilePath(string athleteSlug, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(athleteSlug))
            return false;

        var normalizedSlug = NormalizeAthleteSlug(athleteSlug);
        var snapshot = _athletes.GetAthletesSnapshot();
        var athlete = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o =>
                string.Equals(
                    NormalizeAthleteSlug(o["AthleteSlug"]?.GetValue<string>()),
                    normalizedSlug,
                    StringComparison.Ordinal));
        var profileUrl = athlete?["ProfilePic"]?.GetValue<string>();
        if (!string.IsNullOrWhiteSpace(profileUrl))
        {
            var rel = profileUrl.Trim().TrimStart('/').Replace('/', IOPath.DirectorySeparatorChar);
            var byUrlPath = IOPath.Combine(_env.WebRootPath, rel);
            if (File.Exists(byUrlPath))
            {
                fullPath = byUrlPath;
                return true;
            }
        }

        var athleteDir = IOPath.Combine(_env.WebRootPath, "athletes", normalizedSlug);
        if (!Directory.Exists(athleteDir))
            return false;

        var direct = IOPath.Combine(athleteDir, normalizedSlug + ".webp");
        if (File.Exists(direct))
        {
            fullPath = direct;
            return true;
        }

        var fallback = Directory.EnumerateFiles(athleteDir)
            .FirstOrDefault(p =>
                p.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(fallback))
            return false;

        fullPath = fallback;
        return true;
    }

    private IReadOnlyList<string> GetAthleteDisplayNames(IEnumerable<string> top3Slugs)
    {
        var snapshot = _athletes.GetAthletesSnapshot();
        var bySlug = snapshot
            .OfType<JsonObject>()
            .Select(o => new
            {
                Slug = NormalizeAthleteSlug(o["AthleteSlug"]?.GetValue<string>()),
                Name = (o["DisplayName"]?.GetValue<string>() ?? o["Name"]?.GetValue<string>() ?? "").Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug))
            .GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

        return top3Slugs
            .Select(NormalizeAthleteSlug)
            .Select(s => bySlug.TryGetValue(s, out var n) ? n : ToDisplayName(s))
            .ToArray();
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
                    _log.LogDebug(ex, "Failed to delete stale league OG render {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to cleanup stale league OG renders for {LeagueSlug}", internalSlug);
        }
    }

    private static string NormalizeAthleteSlug(string? slug)
    {
        return (slug ?? "").Trim().Replace('-', '_').ToLowerInvariant();
    }

    private static string ToRouteSlug(string normalizedSlug)
    {
        return normalizedSlug.Replace('_', '-');
    }

    private static string ToDisplayName(string slug)
    {
        var parts = NormalizeAthleteSlug(slug).Split('_', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return slug;
        return string.Join(" ", parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
    }

    private static string NormalizeToken(string raw)
    {
        var normalized = raw.Trim().ToLowerInvariant();
        for (var i = 0; i < 2; i++)
        {
            var decoded = Uri.UnescapeDataString(normalized);
            if (string.Equals(decoded, normalized, StringComparison.Ordinal))
                break;
            normalized = decoded;
        }

        normalized = normalized.Replace('+', ' ')
            .Replace("_", " ", StringComparison.Ordinal)
            .Replace("-", "-", StringComparison.Ordinal);
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return normalized;
    }
}
