using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
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

    private const int PortraitX = 112;
    private const int PortraitY = 176;
    private const int PortraitSize = 292;
    private const float TitleX = 472f;
    private const float TitleY = 178f;
    private const float TitleMaxWidth = 624f;
    private const float MetricRowX = 472f;
    private const float MetricRowWidth = 586f;
    private const float MetricRowHeight = 82f;
    private const float RankRowY = 318f;
    private const float ReductionRowY = 422f;
    private const float HeaderX = 72f;
    private static readonly Color BackgroundTop = ParseHex("05080B");
    private static readonly Color BackgroundBottom = ParseHex("15181B");
    private static readonly Color RankColor = ParseHex("FF4081");
    private static readonly Color ReductionColor = ParseHex("78DA3B");
    private static readonly Color NameColor = Color.White;
    private static readonly Color MutedTextColor = new(new Rgba32(214, 222, 232, 220));
    private static readonly Color MetricRowFillColor = new(new Rgba32(18, 29, 28, 218));
    private static readonly Color MetricRowStrokeColor = new(new Rgba32(255, 255, 255, 90));
    private static readonly Color TextShadowColor = new(new Rgba32(0, 0, 0, 185));

    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<AthleteOgImageService> _log;
    private readonly string _logoPath;
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
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _fontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "athlete");
    }

    public sealed record AthleteOgPayload(
        string InternalSlug,
        string RouteSlug,
        string LeagueSlug,
        string Name,
        string LeagueName,
        int Rank,
        double AgeReduction,
        string? ProfilePicUrl,
        string Signature);

    public bool IsConfigured => File.Exists(_logoPath) && File.Exists(_fontPath);

    public bool TryGetCurrentPayload(string rawSlug, out AthleteOgPayload payload)
    {
        return TryGetCurrentPayload(rawSlug, rawLeagueContext: null, out payload);
    }

    public bool TryGetCurrentPayload(string rawSlug, string? rawLeagueContext, out AthleteOgPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(rawSlug))
            return false;

        var normalized = NormalizeSlug(rawSlug);
        var rankings = _athletes.GetRankingsOrder();
        var leagueSlug = ResolveLeagueSlugOrDefault(rawLeagueContext);

        var snapshot = _athletes.GetAthletesSnapshot();
        var athleteJson = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                NormalizeSlug(o["AthleteSlug"]?.GetValue<string>()),
                normalized,
                StringComparison.Ordinal));
        if (athleteJson is null)
            return false;
        var athlete = athleteJson;

        if (!TryGetRankRowForLeague(normalized, leagueSlug, rankings, snapshot, out var rankingRow, out var rank))
        {
            // Robust fallback for invalid/mismatched context links.
            leagueSlug = "ultimate";
            if (!TryGetRankRowForLeague(normalized, leagueSlug, rankings, snapshot, out rankingRow, out rank))
                return false;
        }

        var nameFromRanking = rankingRow["Name"]?.GetValue<string>();
        var nameFromAthlete = athlete["DisplayName"]?.GetValue<string>() ?? athlete["Name"]?.GetValue<string>();
        var name = !string.IsNullOrWhiteSpace(nameFromAthlete)
            ? nameFromAthlete!.Trim()
            : !string.IsNullOrWhiteSpace(nameFromRanking)
                ? nameFromRanking!.Trim()
                : ToDisplayName(rawSlug);

        var ageReductionFromRanking = GetDouble(rankingRow, "AgeDifference") ?? 0d;
        var stats = PhenoStatsCalculator.Compute(athlete, DateTime.UtcNow.Date);
        var hasBortzRaw = stats.BortzAgeReduction.HasValue && double.IsFinite(stats.BortzAgeReduction.Value);
        var ageReduction = hasBortzRaw
            ? stats.BortzAgeReduction!.Value
            : stats.AgeReduction ?? ageReductionFromRanking;
        var leagueName = ResolveLeagueDisplayName(leagueSlug);
        var profilePicUrl = athlete["ProfilePic"]?.GetValue<string>();
        var signature = ComputeSignature(normalized, leagueSlug, rank, ageReduction, name, leagueName, profilePicUrl);

        payload = new AthleteOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            LeagueSlug: leagueSlug,
            Name: name,
            LeagueName: leagueName,
            Rank: rank,
            AgeReduction: ageReduction,
            ProfilePicUrl: profilePicUrl,
            Signature: signature);

        return true;
    }

    public string BuildVersionedImageUrl(string siteBaseUrl, AthleteOgPayload payload)
    {
        var ctxPart = payload.LeagueSlug == "ultimate"
            ? ""
            : $"&ctx={Uri.EscapeDataString(payload.LeagueSlug)}";
        return $"{siteBaseUrl}/og/athlete/{payload.RouteSlug}.png?v={payload.Signature}{ctxPart}";
    }

    public async Task<string?> EnsureRenderedImageAsync(AthleteOgPayload payload, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return null;

        Directory.CreateDirectory(_outputDir);
        var renderPrefix = BuildRenderPrefix(payload.InternalSlug, payload.LeagueSlug);
        var outputPath = IOPath.Combine(_outputDir, $"{renderPrefix}-{payload.Signature}.png");
        if (File.Exists(outputPath))
        {
            CleanupOldRenders(renderPrefix, outputPath);
            return outputPath;
        }

        await _renderLock.WaitAsync(ct);
        try
        {
            if (File.Exists(outputPath))
            {
                CleanupOldRenders(renderPrefix, outputPath);
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

            CleanupOldRenders(renderPrefix, outputPath);
            return outputPath;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task RenderImageAsync(AthleteOgPayload payload, string outputPath, CancellationToken ct)
    {
        using var image = new Image<Rgba32>(CanvasWidth, CanvasHeight);
        DrawBackground(image, RankColor);

        var fontFamily = GetFontFamily();
        await DrawBrandAsync(image, fontFamily);
        var profilePath = ResolveProfilePath(payload.ProfilePicUrl);
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath))
        {
            await DrawProfilePhotoAsync(image, profilePath, payload.InternalSlug, ct);
        }

        var name = FitTextToWidth(fontFamily, payload.Name, 66f, 38f, TitleMaxWidth);
        var valueFont = fontFamily.CreateFont(42f, FontStyle.Bold);
        var labelFont = fontFamily.CreateFont(28f, FontStyle.Bold);

        var rankText = $"#{payload.Rank}";
        var reductionText = FormatReduction(payload.AgeReduction);

        image.Mutate(ctx =>
        {
            DrawTextShadow(
                ctx,
                name.Text,
                name.Font,
                new PointF(TitleX, TitleY),
                HorizontalAlignment.Left);

            ctx.DrawText(
                new RichTextOptions(name.Font)
                {
                    Origin = new PointF(TitleX, TitleY),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                name.Text,
                NameColor);

            DrawScoreboardMetricRow(ctx, rankText, "Current rank", valueFont, labelFont, RankColor, MetricRowX, RankRowY, MetricRowWidth, MetricRowHeight);
            DrawScoreboardMetricRow(ctx, reductionText, "Age Reduction", valueFont, labelFont, ReductionColor, MetricRowX, ReductionRowY, MetricRowWidth, MetricRowHeight);
        });

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private async Task DrawProfilePhotoAsync(Image<Rgba32> image, string profilePath, string slug, CancellationToken ct)
    {
        try
        {
            await using var profileStream = File.OpenRead(profilePath);
            using var profile = await Image.LoadAsync<Rgba32>(profileStream, ct);
            profile.Mutate(ctx => ctx.AutoOrient().Resize(new ResizeOptions
            {
                Size = new Size(PortraitSize, PortraitSize),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            MakeCircular(profile);

            image.Mutate(ctx =>
            {
                var centerX = PortraitX + (PortraitSize / 2f);
                var centerY = PortraitY + (PortraitSize / 2f);
                ctx.Fill(new Rgba32(0, 0, 0, 150), new EllipsePolygon(centerX + 8f, centerY + 10f, (PortraitSize / 2f) + 8f));
                ctx.Fill(new Rgba32(255, 255, 255, 16), new EllipsePolygon(centerX, centerY, (PortraitSize / 2f) + 13f));
                ctx.Draw(ToRgba(RankColor, 235), 6f, new EllipsePolygon(centerX, centerY, (PortraitSize / 2f) + 3f));
                ctx.DrawImage(profile, new Point(PortraitX, PortraitY), 1f);
                ctx.Draw(new Rgba32(255, 255, 255, 66), 1.5f, new EllipsePolygon(centerX, centerY, (PortraitSize / 2f) - 1f));
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to render athlete profile image for {Slug}", slug);
        }
    }

    private async Task DrawBrandAsync(Image<Rgba32> image, FontFamily boldFamily)
    {
        var brandFont = boldFamily.CreateFont(22f, FontStyle.Bold);

        try
        {
            using var logo = await LoadLogoMarkAsync();
            using var smallLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(54, 54),
                Mode = ResizeMode.Max
            }));
            using var backgroundLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(455, 455),
                Mode = ResizeMode.Max
            }));

            image.Mutate(ctx =>
            {
                ctx.DrawImage(backgroundLogo, new Point(810, 42), 0.065f);
                ctx.DrawImage(smallLogo, new Point((int)HeaderX, 38), 0.96f);
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw athlete OG logo.");
        }

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(brandFont)
            {
                Origin = new PointF(HeaderX + 68f, 41f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "LONGEVITY\nWORLD CUP", NameColor);
        });
    }

    private async Task<Image<Rgba32>> LoadLogoMarkAsync()
    {
        await using var logoStream = File.OpenRead(_logoPath);
        var logo = await Image.LoadAsync<Rgba32>(logoStream);
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
        return logo;
    }

    private static void DrawScoreboardMetricRow(
        IImageProcessingContext ctx,
        string value,
        string label,
        Font valueFont,
        Font labelFont,
        Color accent,
        float x,
        float y,
        float width,
        float height)
    {
        ctx.Fill(MetricRowFillColor, new RectangularPolygon(x, y, width, height));
        ctx.Draw(MetricRowStrokeColor, 1f, new RectangularPolygon(x, y, width, height));
        ctx.Fill(ToRgba(accent, 235), new RectangularPolygon(x, y, 10f, height));

        var valueFontToUse = value.Length > 4
            ? FitFontToWidth(valueFont.Family, value, valueFont.Size, 24f, 142f)
            : valueFont;

        DrawTextShadow(ctx, value, valueFontToUse, new PointF(x + 48f, y + 16f), HorizontalAlignment.Left);
        ctx.DrawText(new RichTextOptions(valueFontToUse)
        {
            Origin = new PointF(x + 48f, y + 16f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, value, accent);

        ctx.DrawText(new RichTextOptions(labelFont)
        {
            Origin = new PointF(x + 198f, y + 24f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, label, MutedTextColor);
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

                    var edgeLight = MathF.Max(0f, 1f - MathF.Sqrt(MathF.Pow((horizontal - 0.76f) / 0.5f, 2f) + MathF.Pow((vertical - 0.22f) / 0.68f, 2f)));
                    var laneLight = MathF.Max(0f, 1f - MathF.Abs((horizontal + vertical * 0.32f) - 0.38f) / 0.075f) * 0.15f;
                    var accentLight = MathF.Min(0.22f, edgeLight * 0.18f + laneLight);

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
            ctx.Fill(new Rgba32(0, 0, 0, 74), new RectangularPolygon(0, 0, CanvasWidth, 72f));
        });
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

    private FontFamily GetFontFamily()
    {
        if (_fontLoaded)
            return _fontFamily;

        _fontFamily = _fonts.Add(_fontPath);
        _fontLoaded = true;
        return _fontFamily;
    }

    private string ComputeSignature(
        string normalizedSlug,
        string leagueSlug,
        int rank,
        double ageReduction,
        string name,
        string leagueName,
        string? profilePicUrl)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;
        var fontTicks = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0L;
        var profilePath = ResolveProfilePath(profilePicUrl);
        var profileTicks = !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)
            ? File.GetLastWriteTimeUtc(profilePath).Ticks
            : 0L;

        var raw = string.Join("|",
            "athlete-og-v32",
            normalizedSlug,
            leagueSlug,
            rank.ToString(CultureInfo.InvariantCulture),
            ageReduction.ToString("0.0000", CultureInfo.InvariantCulture),
            name,
            leagueName,
            profilePicUrl ?? "",
            logoTicks.ToString(CultureInfo.InvariantCulture),
            fontTicks.ToString(CultureInfo.InvariantCulture),
            profileTicks.ToString(CultureInfo.InvariantCulture));

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private string? ResolveProfilePath(string? profilePicUrl)
    {
        if (string.IsNullOrWhiteSpace(profilePicUrl))
            return null;

        var relativeUrl = profilePicUrl.Trim();
        var queryStart = relativeUrl.IndexOf('?');
        if (queryStart >= 0)
            relativeUrl = relativeUrl[..queryStart];

        var relative = relativeUrl.TrimStart('/').Replace('/', IOPath.DirectorySeparatorChar);
        return IOPath.Combine(_env.WebRootPath, relative);
    }

    private static string ResolveLeagueSlugOrDefault(string? rawLeagueContext)
    {
        return LeagueOgImageService.TryNormalizeLeagueSlug(rawLeagueContext, out var normalized)
            ? normalized
            : "ultimate";
    }

    private static string ResolveLeagueDisplayName(string leagueSlug)
    {
        return LeagueOgImageService.TryGetLeagueDisplayName(leagueSlug, out var displayName)
            ? displayName
            : "Ultimate League";
    }

    private static bool TryGetRankRowForLeague(
        string normalizedAthleteSlug,
        string leagueSlug,
        JsonArray rankings,
        JsonArray snapshot,
        out JsonObject rankingRow,
        out int rank)
    {
        rankingRow = null!;
        rank = 0;

        var divisionBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var generationBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var exclusiveBySlug = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in snapshot.OfType<JsonObject>())
        {
            var slug = NormalizeSlug(o["AthleteSlug"]?.GetValue<string>());
            if (string.IsNullOrWhiteSpace(slug))
                continue;

            var div = o["Division"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(div))
                divisionBySlug[slug] = div;

            var gen = GenerationResolver.ResolveFromAthleteJson(o);
            if (!string.IsNullOrWhiteSpace(gen))
                generationBySlug[slug] = gen;

            var ex = o["ExclusiveLeague"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(ex))
                exclusiveBySlug[slug] = ex;
        }

        foreach (var row in rankings.OfType<JsonObject>())
        {
            var rowSlug = NormalizeSlug(row["AthleteSlug"]?.GetValue<string>());
            if (string.IsNullOrWhiteSpace(rowSlug))
                continue;

            if (!IsAthleteInLeague(row, rowSlug, leagueSlug, divisionBySlug, generationBySlug, exclusiveBySlug))
                continue;

            rank++;
            if (!string.Equals(rowSlug, normalizedAthleteSlug, StringComparison.Ordinal))
                continue;

            rankingRow = row;
            return true;
        }

        return false;
    }

    private static bool IsAthleteInLeague(
        JsonObject rankingRow,
        string normalizedSlug,
        string leagueSlug,
        IReadOnlyDictionary<string, string> divisionBySlug,
        IReadOnlyDictionary<string, string> generationBySlug,
        IReadOnlyDictionary<string, string> exclusiveBySlug)
    {
        if (string.Equals(leagueSlug, "ultimate", StringComparison.OrdinalIgnoreCase))
            return true;

        string? targetDivision = leagueSlug switch
        {
            "mens" => "Men's",
            "womens" => "Women's",
            "open" => "Open",
            _ => null
        };

        string? targetGeneration = leagueSlug switch
        {
            "silent-generation" => "Silent Generation",
            "baby-boomers" => "Baby Boomers",
            "gen-x" => "Gen X",
            "millennials" => "Millennials",
            "gen-z" => "Gen Z",
            "gen-alpha" => "Gen Alpha",
            _ => null
        };

        string? targetExclusive = string.Equals(leagueSlug, "prosperan", StringComparison.OrdinalIgnoreCase)
            ? "Prosperan"
            : null;

        var isAmateur = string.Equals(leagueSlug, "amateur", StringComparison.OrdinalIgnoreCase);

        var divisionMatch = targetDivision is not null
                            && divisionBySlug.TryGetValue(normalizedSlug, out var division)
                            && string.Equals(division, targetDivision, StringComparison.OrdinalIgnoreCase);
        var generationMatch = targetGeneration is not null
                              && generationBySlug.TryGetValue(normalizedSlug, out var generation)
                              && string.Equals(generation, targetGeneration, StringComparison.OrdinalIgnoreCase);
        var exclusiveMatch = targetExclusive is not null
                             && exclusiveBySlug.TryGetValue(normalizedSlug, out var exclusive)
                             && string.Equals(exclusive, targetExclusive, StringComparison.OrdinalIgnoreCase);
        var amateurMatch = isAmateur && rankingRow["LowestBortzAge"] is not JsonValue;

        return divisionMatch || generationMatch || exclusiveMatch || amateurMatch;
    }

    private static string BuildRenderPrefix(string internalSlug, string leagueSlug)
    {
        return $"{internalSlug}-{leagueSlug}";
    }

    private void CleanupOldRenders(string renderPrefix, string keepFullPath)
    {
        try
        {
            var prefix = $"{renderPrefix}-";
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
            _log.LogDebug(ex, "Failed to cleanup stale OG renders for {RenderPrefix}", renderPrefix);
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
        return value.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture);
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }

    private static Rgba32 ToRgba(Color color, byte alpha)
    {
        var pixel = color.ToPixel<Rgba32>();
        return new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
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

    private static (string Text, Font Font) FitTextToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth)
    {
        var font = FitFontToWidth(family, text, startSize, minSize, maxWidth);
        if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
            return (text, font);

        return (EllipsizeToWidth(text, font, maxWidth), font);
    }

    private static string EllipsizeToWidth(string text, Font font, float maxWidth)
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

    private static void MakeCircular(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        var radius = Math.Min(w, h) / 2f;

        using var mask = new Image<Rgba32>(w, h, Color.Transparent);
        mask.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.Fill(Color.White, new EllipsePolygon(w / 2f, h / 2f, radius));
        });

        image.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                AlphaCompositionMode = PixelAlphaCompositionMode.DestIn,
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.DrawImage(mask, new Point(0, 0), 1f);
        });
    }

    private static void DrawTextShadow(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        HorizontalAlignment alignment)
    {
        ctx.DrawText(
            new RichTextOptions(font)
            {
                Origin = new PointF(origin.X, origin.Y + 2f),
                HorizontalAlignment = alignment,
                VerticalAlignment = VerticalAlignment.Top
            },
            text,
            TextShadowColor);
    }

}
