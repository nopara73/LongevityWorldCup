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
    private const float LeagueTitleY = 52f;
    private const float LeagueSubtitleY = 118f;
    private static readonly Color LeagueTitleColor = Color.White;
    private static readonly Color LeagueSubtitleColor = new(new Rgba32(255, 255, 255, 205));
    private static readonly Color AccentColor = ParseHex("78DA3B");
    private static readonly Color NameLabelColor = Color.White;
    private static readonly Color NameLabelShadowColor = new(new Rgba32(0, 0, 0, 210));
    private static readonly Color NamePlateFillColor = new(new Rgba32(8, 10, 12, 205));
    private static readonly Color NamePlateStrokeColor = new(new Rgba32(255, 255, 255, 42));
    private static readonly Color EmptySlotFillColor = new(new Rgba32(18, 21, 23, 218));
    private static readonly Color EmptySlotTextColor = new(new Rgba32(255, 255, 255, 190));

    // Slot centers and diameters matched to the league OG podium template.
    private static readonly Slot[] PodiumSlots =
    [
        // From Figma node 73:24 (og_league_example_image_pos):
        // gold: x=495 y=174 w=210 h=210
        // silver: x=248 y=250 w=171 h=171
        // bronze: x=782 y=251 w=171 h=171
        new Slot(600, 279, 230), // #1 center
        new Slot(333, 336, 192), // #2 left
        new Slot(867, 337, 192)  // #3 right
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
    private sealed record NameLayout(Font Font, IReadOnlyList<string> Lines);

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

        var fontFamily = GetFontFamily();

        for (var i = 0; i < PodiumSlots.Length; i++)
        {
            var slot = PodiumSlots[i];
            if (i >= payload.Top3Slugs.Count)
            {
                DrawEmptySlot(image, slot, fontFamily);
                continue;
            }

            if (!TryResolveProfilePath(payload.Top3Slugs[i], out var profilePath))
            {
                DrawEmptySlot(image, slot, fontFamily);
                continue;
            }

            try
            {
                await using var profileStream = File.OpenRead(profilePath);
                using var profile = await Image.LoadAsync<Rgba32>(profileStream, ct);
                profile.Mutate(ctx => ctx.AutoOrient());
                DrawCircularProfile(image, profile, slot);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render league top3 profile image for {LeagueSlug}: {Path}", payload.InternalSlug, profilePath);
                DrawEmptySlot(image, slot, fontFamily);
            }
        }

        var titleFont = fontFamily.CreateFont(58f, FontStyle.Bold);
        var subtitleFont = fontFamily.CreateFont(24f, FontStyle.Bold);
        var title = payload.DisplayName;

        while (true)
        {
            var measurement = TextMeasurer.MeasureSize(title, new RichTextOptions(titleFont));
            if (measurement.Width <= 1080f || titleFont.Size <= 38f)
                break;
            titleFont = fontFamily.CreateFont(titleFont.Size - 1f, FontStyle.Bold);
        }

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(120, 218, 59, 220), new RectangularPolygon(493f, LeagueSubtitleY + 36f, 214f, 5f));
            ctx.DrawText(new RichTextOptions(titleFont)
            {
                Origin = new PointF(CanvasWidth / 2f, LeagueTitleY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, title, LeagueTitleColor);

            ctx.DrawText(new RichTextOptions(subtitleFont)
            {
                Origin = new PointF(CanvasWidth / 2f, LeagueSubtitleY),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, payload.Top3Names.Count == 0 ? "Rankings opening soon" : "Current top longevity athletes", LeagueSubtitleColor);
        });

        if (payload.Top3Names.Count > 0)
        {
            DrawAthleteNameLabels(image, payload.Top3Names, fontFamily);
        }

        await image.SaveAsPngAsync(outputPath, ct);
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

    private static void DrawAthleteNameLabels(Image<Rgba32> image, IReadOnlyList<string> top3Names, FontFamily fontFamily)
    {
        var firstName = top3Names.Count > 0 ? top3Names[0] : "";
        var secondName = top3Names.Count > 1 ? top3Names[1] : "";
        var thirdName = top3Names.Count > 2 ? top3Names[2] : "";

        DrawPodiumNameLabel(image, firstName, fontFamily, PodiumSlots[0].CenterX, 568f, 330f, 24f, 17f);
        DrawPodiumNameLabel(image, secondName, fontFamily, PodiumSlots[1].CenterX, 568f, 258f, 22f, 16f);
        DrawPodiumNameLabel(image, thirdName, fontFamily, PodiumSlots[2].CenterX, 568f, 258f, 22f, 16f);
    }

    private static void DrawPodiumNameLabel(
        Image<Rgba32> image,
        string text,
        FontFamily fontFamily,
        float centerX,
        float topY,
        float maxWidth,
        float startSize,
        float minSize)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var layout = BuildNameLayout(text, fontFamily, maxWidth, startSize, minSize);
        var lineHeight = layout.Font.Size * 1.02f;
        var totalHeight = lineHeight * layout.Lines.Count;
        var maxLineWidth = layout.Lines.Max(line => TextMeasurer.MeasureSize(line, new RichTextOptions(layout.Font)).Width);
        var plateWidth = MathF.Min(maxWidth + 22f, MathF.Max(maxLineWidth + 34f, 112f));
        var plateHeight = MathF.Max(totalHeight + 18f, 38f);
        var plateX = centerX - (plateWidth / 2f);
        var plateY = topY - ((plateHeight - totalHeight) / 2f);
        var y = plateY + ((plateHeight - totalHeight) / 2f);

        image.Mutate(ctx =>
        {
            ctx.Fill(NamePlateFillColor, new RectangularPolygon(plateX, plateY, plateWidth, plateHeight));
            ctx.Draw(NamePlateStrokeColor, 1f, new RectangularPolygon(plateX, plateY, plateWidth, plateHeight));
            ctx.Fill(new Rgba32(255, 255, 255, 32), new RectangularPolygon(plateX, plateY, plateWidth, 1.5f));

            foreach (var line in layout.Lines)
            {
                DrawNameTextShadow(ctx, line, layout.Font, new PointF(centerX, y));
                ctx.DrawText(new RichTextOptions(layout.Font)
                {
                    Origin = new PointF(centerX, y),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                }, line, NameLabelColor);

                y += lineHeight;
            }
        });
    }

    private static void DrawEmptySlot(Image<Rgba32> target, Slot slot, FontFamily? fontFamily)
    {
        var radius = (slot.Diameter / 2f) - 7f;
        target.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.Fill(EmptySlotFillColor, new EllipsePolygon(slot.CenterX, slot.CenterY, radius));
        });

        if (fontFamily is null)
            return;

        var font = fontFamily.Value.CreateFont(22f, FontStyle.Bold);
        target.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(slot.CenterX, slot.CenterY - 13f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, "OPEN", EmptySlotTextColor);
        });
    }

    private static Font FitFontToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
            return family.CreateFont(startSize, FontStyle.Bold);

        var size = startSize;
        while (size > minSize)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            var measured = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
            // Keep a small guard margin so final rendered text + inner shadow stays inside podium.
            if (measured.Width <= (maxWidth - 8f))
                return font;
            size -= 1f;
        }

        return family.CreateFont(minSize, FontStyle.Bold);
    }

    private static NameLayout BuildNameLayout(string text, FontFamily family, float maxWidth, float startSize, float minSize)
    {
        var normalized = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var forcedLines = TrySplitCommaName(normalized);
        if (forcedLines is not null)
            return BuildMultilineNameLayout(forcedLines, family, maxWidth, startSize, minSize);

        for (var size = startSize; size >= minSize; size -= 1f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(normalized, new RichTextOptions(font)).Width <= maxWidth)
                return new NameLayout(font, [normalized]);
        }

        var lines = SplitNameIntoLines(normalized);
        return BuildMultilineNameLayout(lines, family, maxWidth, startSize, minSize);
    }

    private static NameLayout BuildMultilineNameLayout(IReadOnlyList<string> lines, FontFamily family, float maxWidth, float startSize, float minSize)
    {
        for (var size = startSize - 2f; size >= minSize; size -= 1f)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (lines.All(line => TextMeasurer.MeasureSize(line, new RichTextOptions(font)).Width <= maxWidth))
                return new NameLayout(font, lines);
        }

        var fallbackFont = family.CreateFont(minSize, FontStyle.Bold);
        return new NameLayout(fallbackFont, lines.Select(line => TruncateToWidth(line, fallbackFont, maxWidth)).ToArray());
    }

    private static IReadOnlyList<string>? TrySplitCommaName(string text)
    {
        var comma = text.IndexOf(',');
        if (comma > 0 && comma < text.Length - 1)
        {
            return
            [
                text[..comma].Trim(),
                text[(comma + 1)..].Trim()
            ];
        }

        return null;
    }

    private static IReadOnlyList<string> SplitNameIntoLines(string text)
    {
        var commaLines = TrySplitCommaName(text);
        if (commaLines is not null)
            return commaLines;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= 1)
            return [text];

        var midpoint = text.Length / 2f;
        var bestIndex = 1;
        var bestDistance = float.MaxValue;
        var lengthSoFar = 0;
        for (var i = 1; i < words.Length; i++)
        {
            lengthSoFar += words[i - 1].Length + (i == 1 ? 0 : 1);
            var distance = Math.Abs(lengthSoFar - midpoint);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestIndex = i;
        }

        return
        [
            string.Join(" ", words.Take(bestIndex)),
            string.Join(" ", words.Skip(bestIndex))
        ];
    }

    private static string TruncateToWidth(string text, Font font, float maxWidth)
    {
        if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
            return text;

        var trimmed = text;
        while (trimmed.Length > 0 && TextMeasurer.MeasureSize(trimmed + "...", new RichTextOptions(font)).Width > maxWidth)
            trimmed = trimmed[..^1].TrimEnd();

        return string.IsNullOrWhiteSpace(trimmed) ? "..." : trimmed + "...";
    }

    private static void DrawNameTextShadow(IImageProcessingContext ctx, string text, Font font, PointF origin)
    {
        var options = new RichTextOptions(font)
        {
            Origin = new PointF(origin.X, origin.Y + 3f),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top
        };
        ctx.DrawText(options, text, new Rgba32(0, 0, 0, 235));

        options.Origin = new PointF(origin.X + 1.5f, origin.Y + 1.5f);
        ctx.DrawText(options, text, new Rgba32(0, 0, 0, 170));

        options.Origin = new PointF(origin.X - 1.5f, origin.Y + 1.5f);
        ctx.DrawText(options, text, new Rgba32(0, 0, 0, 150));
    }

    private static void DrawCircularProfile(Image<Rgba32> target, Image<Rgba32> source, Slot slot)
    {
        var renderSize = Math.Max(1, slot.Diameter);
        source.Mutate(ctx => ctx.Resize(new ResizeOptions
        {
            Size = new Size(renderSize, renderSize),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }).Brightness(1.1f).Contrast(1.08f).Saturate(1.08f));

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
        target.Mutate(ctx =>
        {
            ctx.DrawImage(source, new Point(x, y), 1f);
            ctx.Draw(Color.White, 5f, new EllipsePolygon(slot.CenterX, slot.CenterY, (slot.Diameter / 2f) - 2f));
        });
    }

    private static void DrawTextShadow(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        HorizontalAlignment alignment,
        Color color,
        float offset)
    {
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = new PointF(origin.X, origin.Y + offset),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, text, color);
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
            "league-og-v30",
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
            var relativeUrl = profileUrl.Trim();
            var queryStart = relativeUrl.IndexOf('?');
            if (queryStart >= 0)
                relativeUrl = relativeUrl[..queryStart];

            var rel = relativeUrl.TrimStart('/').Replace('/', IOPath.DirectorySeparatorChar);
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

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }
}
