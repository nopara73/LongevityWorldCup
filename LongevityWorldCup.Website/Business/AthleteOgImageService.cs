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

    // Layout values from the approved design.
    private const int ProfileSize = 381;
    private const int ProfileX = 410;
    private const int ProfileY = 92;
    private const int ProfileBleed = 2;
    private const float NameTop = ProfileY + ProfileSize + 20f;
    private const float RankX = 60f;
    private const float RankY = 245f;
    private const float LeagueX = 60f;
    private const float LeagueY = 346f;
    private const float LeagueLetterSpacingEm = 0.02f; // 2%
    private const float ReductionX = 910f;
    private const float ReductionY = 245f;

    private static readonly Color RankColor = ParseHex("FF4081");
    private static readonly Color ReductionColor = ParseHex("78DA3B");
    private static readonly Color NameColor = Color.White;
    private static readonly Color LeagueColor = ParseHex("FFFFFFCC"); // white @ 80% opacity

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

            await RenderImageAsync(payload, outputPath, ct);
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
        var leagueFont = fontFamily.CreateFont(30, FontStyle.Bold);
        var nameFont = fontFamily.CreateFont(65, FontStyle.Bold);

        var rankText = $"#{payload.Rank}";
        var leagueText = payload.LeagueName;
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

            DrawTrackedText(
                ctx,
                leagueText,
                leagueFont,
                new PointF(LeagueX, LeagueY),
                LeagueColor,
                leagueFont.Size * LeagueLetterSpacingEm);

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

    private string ComputeSignature(string normalizedSlug, string leagueSlug, int rank, double ageReduction, string name, string leagueName, string? profilePicUrl)
    {
        var templateTicks = File.Exists(_templatePath) ? File.GetLastWriteTimeUtc(_templatePath).Ticks : 0L;
        var fontTicks = File.Exists(_fontPath) ? File.GetLastWriteTimeUtc(_fontPath).Ticks : 0L;
        var profilePath = ResolveProfilePath(profilePicUrl);
        var profileTicks = !string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath)
            ? File.GetLastWriteTimeUtc(profilePath).Ticks
            : 0L;

        var raw = string.Join("|",
            "athlete-og-v12",
            normalizedSlug,
            leagueSlug,
            rank.ToString(CultureInfo.InvariantCulture),
            ageReduction.ToString("0.0000", CultureInfo.InvariantCulture),
            name,
            leagueName,
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
        var abs = Math.Abs(value);
        var sign = value < 0 ? "-" : "+";
        return $"{sign} {abs:0.0}";
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }

    private static void DrawTrackedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        Color color,
        float letterSpacingPx)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var baseOptions = new TextOptions(font);
        var additionalSpacing = 0f;
        var spaceAdvance = GetSpaceAdvance(baseOptions);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i].ToString();
            var x = origin.X + additionalSpacing;

            ctx.DrawText(
                new RichTextOptions(font)
                {
                    Origin = new PointF(x, origin.Y),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                ch,
                color);

            var advance = GetCharacterAdvance(text, i, baseOptions, spaceAdvance);
            additionalSpacing += advance;
            if (i < text.Length - 1)
                additionalSpacing += letterSpacingPx;
        }
    }

    private static float GetCharacterAdvance(string text, int index, TextOptions options, float spaceAdvance)
    {
        var ch = text[index];
        if (ch == ' ')
            return spaceAdvance;

        if (index == 0)
            return TextMeasurer.MeasureAdvance(ch.ToString(), options).Width;

        var prev = text[index - 1];
        if (prev == ' ')
        {
            // Avoid trailing-space measurement collapse by using a neutral prefix.
            var pairWithPrefix = $"A{ch}";
            return TextMeasurer.MeasureAdvance(pairWithPrefix, options).Width
                   - TextMeasurer.MeasureAdvance("A", options).Width;
        }

        var pair = string.Concat(prev, ch);
        var pairAdvance = TextMeasurer.MeasureAdvance(pair, options).Width;
        var prevAdvance = TextMeasurer.MeasureAdvance(prev.ToString(), options).Width;
        var advance = pairAdvance - prevAdvance;
        if (advance <= 0f)
            return TextMeasurer.MeasureAdvance(ch.ToString(), options).Width;
        return advance;
    }

    private static float GetSpaceAdvance(TextOptions options)
    {
        const string withSpace = "A A";
        const string withoutSpace = "AA";
        var withSpaceWidth = TextMeasurer.MeasureAdvance(withSpace, options).Width;
        var withoutSpaceWidth = TextMeasurer.MeasureAdvance(withoutSpace, options).Width;
        return Math.Max(0f, withSpaceWidth - withoutSpaceWidth);
    }

}
