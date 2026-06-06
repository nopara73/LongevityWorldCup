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

    private const int ProfileX = 78;
    private const int ProfileY = 126;
    private const int ProfileWidth = 420;
    private const int ProfileHeight = 420;
    private const int ProfileRadius = 36;
    private const float PanelX = 536f;
    private const float PanelY = 92f;
    private const float PanelWidth = 620f;
    private const float PanelHeight = 456f;
    private const float PanelRadius = 30f;
    private static readonly Color RankColor = ParseHex("FF4081");
    private static readonly Color ReductionColor = ParseHex("78DA3B");
    private static readonly Color NameColor = Color.White;
    private static readonly Color MetricLabelColor = ParseHex("FFFFFFD9"); // white @ 85% opacity
    private static readonly Color PanelFillColor = new(new Rgba32(5, 11, 10, 255));
    private static readonly Color PanelStrokeColor = new(new Rgba32(142, 218, 96, 82));
    private static readonly Color TextShadowColor = new(new Rgba32(0, 0, 0, 185));
    private static readonly Color RadarGridColor = new(new Rgba32(184, 211, 190, 54));
    private static readonly Color RadarAxisColor = new(new Rgba32(184, 211, 190, 45));
    private static readonly Color RadarFillColor = new(new Rgba32(120, 218, 59, 82));
    private static readonly Color RadarStrokeColor = new(new Rgba32(120, 218, 59, 232));

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
        string LeagueSlug,
        string Name,
        string LeagueName,
        int Rank,
        double AgeReduction,
        string? ProfilePicUrl,
        double[]? RadarValues,
        string Signature);

    public bool IsConfigured => File.Exists(_templatePath) && File.Exists(_fontPath);

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
        var radarValues = BuildPhenoRadarValues(normalized, snapshot);
        var signature = ComputeSignature(normalized, leagueSlug, rank, ageReduction, name, leagueName, profilePicUrl, radarValues);

        payload = new AthleteOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            LeagueSlug: leagueSlug,
            Name: name,
            LeagueName: leagueName,
            Rank: rank,
            AgeReduction: ageReduction,
            ProfilePicUrl: profilePicUrl,
            RadarValues: radarValues,
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
        await using var templateStream = File.OpenRead(_templatePath);
        using var image = await Image.LoadAsync<Rgba32>(templateStream, ct);

        if (image.Width != CanvasWidth || image.Height != CanvasHeight)
        {
            image.Mutate(ctx => ctx.Resize(CanvasWidth, CanvasHeight));
        }

        var profilePath = ResolveProfilePath(payload.ProfilePicUrl);
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath))
        {
            await DrawProfilePhotoAsync(image, profilePath, payload.InternalSlug, ct);
        }

        var fontFamily = GetFontFamily();
        var nameFont = FitFontToWidth(fontFamily, payload.Name, 58f, 38f, 500f);
        var metricFont = fontFamily.CreateFont(74f, FontStyle.Bold);
        var labelFont = fontFamily.CreateFont(25f, FontStyle.Bold);
        var smallLabelFont = fontFamily.CreateFont(20f, FontStyle.Bold);
        var radarLabelFont = fontFamily.CreateFont(22f, FontStyle.Bold);

        var rankText = $"#{payload.Rank}";
        var reductionText = FormatReduction(payload.AgeReduction);

        image.Mutate(ctx =>
        {
            FillRoundedRect(ctx, PanelX, PanelY, PanelWidth, PanelHeight, PanelRadius, PanelFillColor);
            FillRoundedRectBorder(ctx, PanelX, PanelY, PanelWidth, PanelHeight, PanelRadius, 2f, PanelStrokeColor);
            ctx.Fill(ToRgba(ReductionColor, 225), new RectangularPolygon(PanelX + 40f, PanelY + 50f, 112f, 5f));

            DrawTextShadow(
                ctx,
                payload.Name,
                nameFont,
                new PointF(PanelX + 40f, PanelY + 76f),
                HorizontalAlignment.Left);

            ctx.DrawText(
                new RichTextOptions(nameFont)
                {
                    Origin = new PointF(PanelX + 40f, PanelY + 76f),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                payload.Name,
                NameColor);

            DrawRadarChart(ctx, payload.RadarValues, fontFamily, radarLabelFont);

            DrawMetricBlock(ctx, PanelX + 40f, PanelY + 324f, 178f, RankColor, payload.LeagueName, rankText, metricFont, labelFont, smallLabelFont, HorizontalAlignment.Left);
            DrawMetricBlock(ctx, PanelX + 372f, PanelY + 324f, 174f, ReductionColor, "Age reduction", reductionText, metricFont, labelFont, smallLabelFont, HorizontalAlignment.Right);
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
                Size = new Size(ProfileWidth, ProfileHeight),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            ClipRoundedRectangle(profile, ProfileRadius);

            image.Mutate(ctx =>
            {
                FillRoundedRect(ctx, ProfileX + 10f, ProfileY + 12f, ProfileWidth, ProfileHeight, ProfileRadius, new Rgba32(0, 0, 0, 126));
                FillRoundedRect(ctx, ProfileX - 4f, ProfileY - 4f, ProfileWidth + 8f, ProfileHeight + 8f, ProfileRadius + 4f, ToRgba(ReductionColor, 210));
                ctx.DrawImage(profile, new Point(ProfileX, ProfileY), 1f);
                FillRoundedRectBorder(ctx, ProfileX, ProfileY, ProfileWidth, ProfileHeight, ProfileRadius, 2f, new Rgba32(255, 255, 255, 54));
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to render athlete profile image for {Slug}", slug);
        }
    }

    private static void DrawMetricBlock(
        IImageProcessingContext ctx,
        float x,
        float y,
        float width,
        Color accent,
        string label,
        string value,
        Font valueFont,
        Font labelFont,
        Font smallLabelFont,
        HorizontalAlignment alignment)
    {
        var ruleX = alignment == HorizontalAlignment.Right ? x + width - 120f : x;
        ctx.Fill(ToRgba(accent, 230), new RectangularPolygon(ruleX, y, 120f, 5f));
        var originX = alignment == HorizontalAlignment.Right ? x + width : x;
        var labelOrigin = new PointF(originX, y + 28f);
        var valueOrigin = new PointF(originX, y + 62f);

        ctx.DrawText(new RichTextOptions(valueFont)
        {
            Origin = valueOrigin,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, value, accent);

        var finalLabelFont = alignment == HorizontalAlignment.Left && label.Length > 15
            ? smallLabelFont
            : labelFont;

        ctx.DrawText(new RichTextOptions(finalLabelFont)
        {
            Origin = labelOrigin,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, label, MetricLabelColor);
    }

    private static void DrawRadarChart(IImageProcessingContext ctx, double[]? values, FontFamily fontFamily, Font labelFont)
    {
        var center = new PointF(830f, 302f);
        const float radius = 116f;
        var labels = new[] { "Immune", "Liver", "Kidney", "Metabolism", "Inflammation" };
        var angles = Enumerable.Range(0, labels.Length)
            .Select(i => -MathF.PI / 2f + (MathF.Tau * i / labels.Length))
            .ToArray();

        for (var ring = 1; ring <= 4; ring++)
        {
            var ringRadius = radius * ring / 4f;
            ctx.DrawPolygon(RadarGridColor, 1.15f, BuildRadarPoints(center, ringRadius, angles, null));
        }

        foreach (var angle in angles)
        {
            var end = PointOnCircle(center, radius, angle);
            DrawLine(ctx, center, end, RadarAxisColor, 1.1f);
        }

        if (values is { Length: 5 })
        {
            var shape = BuildRadarPoints(center, radius, angles, values);
            ctx.FillPolygon(RadarFillColor, shape);
            ctx.DrawPolygon(RadarStrokeColor, 3f, shape);
            foreach (var point in shape)
                ctx.Fill(RadarStrokeColor, new EllipsePolygon(point.X, point.Y, 4.5f));
        }

        DrawRadarLabel(ctx, labels[0], labelFont, new PointF(center.X, center.Y - radius - 42f), HorizontalAlignment.Center);
        DrawRadarLabel(ctx, labels[1], labelFont, new PointF(center.X + radius + 48f, center.Y - 40f), HorizontalAlignment.Left);
        DrawRadarLabel(ctx, labels[2], labelFont, new PointF(center.X + radius + 32f, center.Y + 88f), HorizontalAlignment.Left);
        DrawRadarLabel(ctx, labels[3], FitFontToWidth(fontFamily, labels[3], labelFont.Size, 16f, 126f), new PointF(center.X - radius - 42f, center.Y + 88f), HorizontalAlignment.Right);
        DrawRadarLabel(ctx, labels[4], FitFontToWidth(fontFamily, labels[4], labelFont.Size, 15f, 132f), new PointF(center.X - radius - 50f, center.Y - 40f), HorizontalAlignment.Right);
    }

    private static double[]? BuildPhenoRadarValues(string normalizedSlug, JsonArray snapshot)
    {
        var stats = snapshot
            .OfType<JsonObject>()
            .Select(o => PhenoStatsCalculator.Compute(o, DateTime.UtcNow.Date))
            .Where(s => !string.IsNullOrWhiteSpace(s.Slug) && s.BestMarkerValues is { Length: 10 })
            .ToList();

        var current = stats.FirstOrDefault(s => string.Equals(NormalizeSlug(s.Slug), normalizedSlug, StringComparison.Ordinal));
        if (current?.BestMarkerValues is not { Length: 10 } currentMarkers)
            return null;

        var allScores = stats
            .Select(s => BuildPhenoDomainScores(s.BestMarkerValues!))
            .Where(scores => scores.All(double.IsFinite))
            .ToList();
        if (allScores.Count == 0)
            return null;

        var currentScores = BuildPhenoDomainScores(currentMarkers);
        if (!currentScores.All(double.IsFinite))
            return null;

        var values = new double[currentScores.Length];
        for (var i = 0; i < currentScores.Length; i++)
        {
            var domainScores = allScores.Select(scores => scores[i]).OrderBy(score => score).ToList();
            var lowerCount = domainScores.Count(score => score < currentScores[i]);
            values[i] = 100d * (domainScores.Count - lowerCount) / domainScores.Count;
        }

        return values;
    }

    private static double[] BuildPhenoDomainScores(double[] markerValues)
    {
        return
        [
            PhenoAgeHelper.CalculateImmunePhenoAgeContributor(markerValues),
            PhenoAgeHelper.CalculateLiverPhenoAgeContributor(markerValues),
            PhenoAgeHelper.CalculateKidneyPhenoAgeContributor(markerValues),
            PhenoAgeHelper.CalculateMetabolicPhenoAgeContributor(markerValues),
            PhenoAgeHelper.CalculateInflammationPhenoAgeContributor(markerValues)
        ];
    }

    private static PointF[] BuildRadarPoints(PointF center, float radius, float[] angles, double[]? values)
    {
        var points = new PointF[angles.Length];
        for (var i = 0; i < angles.Length; i++)
        {
            var scale = values is null
                ? 1f
                : (float)Math.Clamp(values[i] / 100d, 0.08d, 1d);
            points[i] = PointOnCircle(center, radius * scale, angles[i]);
        }

        return points;
    }

    private static PointF PointOnCircle(PointF center, float radius, float angle)
    {
        return new PointF(
            center.X + (MathF.Cos(angle) * radius),
            center.Y + (MathF.Sin(angle) * radius));
    }

    private static void DrawLine(IImageProcessingContext ctx, PointF from, PointF to, Color color, float thickness)
    {
        ctx.DrawLine(color, thickness, from, to);
    }

    private static void DrawRadarLabel(IImageProcessingContext ctx, string text, Font font, PointF origin, HorizontalAlignment alignment)
    {
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = origin,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center
        }, text, MetricLabelColor);
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
        string? profilePicUrl,
        double[]? radarValues)
    {
        var templateTicks = File.Exists(_templatePath) ? File.GetLastWriteTimeUtc(_templatePath).Ticks : 0L;
        var fontTicks = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0L;
        var profilePath = ResolveProfilePath(profilePicUrl);
        var profileTicks = !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)
            ? File.GetLastWriteTimeUtc(profilePath).Ticks
            : 0L;

        var radarPart = radarValues is { Length: 5 }
            ? string.Join(",", radarValues.Select(v => v.ToString("0.00", CultureInfo.InvariantCulture)))
            : "";

        var raw = string.Join("|",
            "athlete-og-v27",
            normalizedSlug,
            leagueSlug,
            rank.ToString(CultureInfo.InvariantCulture),
            ageReduction.ToString("0.0000", CultureInfo.InvariantCulture),
            name,
            leagueName,
            profilePicUrl ?? "",
            radarPart,
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

    private static void ClipRoundedRectangle(Image<Rgba32> image, float radius)
    {
        using var mask = new Image<Rgba32>(image.Width, image.Height, Color.Transparent);
        mask.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            FillRoundedRect(ctx, 0, 0, image.Width, image.Height, radius, Color.White);
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

    private static void FillRoundedRect(IImageProcessingContext ctx, float x, float y, float width, float height, float radius, Color color)
    {
        var r = MathF.Min(radius, MathF.Min(width, height) / 2f);
        var centerWidth = MathF.Max(0f, width - (2f * r));
        var centerHeight = MathF.Max(0f, height - (2f * r));

        if (centerWidth > 0f)
            ctx.Fill(color, new RectangularPolygon(x + r, y, centerWidth, height));
        if (centerHeight > 0f)
            ctx.Fill(color, new RectangularPolygon(x, y + r, width, centerHeight));
        ctx.Fill(color, new EllipsePolygon(x + r, y + r, r));
        ctx.Fill(color, new EllipsePolygon(x + width - r, y + r, r));
        ctx.Fill(color, new EllipsePolygon(x + r, y + height - r, r));
        ctx.Fill(color, new EllipsePolygon(x + width - r, y + height - r, r));
    }

    private static void FillRoundedRectBorder(IImageProcessingContext ctx, float x, float y, float width, float height, float radius, float thickness, Color color)
    {
        FillRoundedRect(ctx, x, y, width, thickness, MathF.Min(radius, thickness), color);
        FillRoundedRect(ctx, x, y + height - thickness, width, thickness, MathF.Min(radius, thickness), color);
        FillRoundedRect(ctx, x, y, thickness, height, MathF.Min(radius, thickness), color);
        FillRoundedRect(ctx, x + width - thickness, y, thickness, height, MathF.Min(radius, thickness), color);
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
