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

    private const float PortraitRadius = 150f;
    private const float PortraitRing = 7f;
    private const float PortraitCenterX = ShareCardCanvas.Margin + PortraitRing + 10f + PortraitRadius;
    private const float BodyCenterY = (ShareCardCanvas.BodyTop + ShareCardCanvas.BodyBottom) / 2f;
    private const float ColumnX = 452f;
    private const float ColumnWidth = ShareCardCanvas.Width - ShareCardCanvas.Margin - ColumnX;
    private const float TileGap = 20f;
    private const float TileHeight = 158f;
    private const float NameGap = 30f;
    private static readonly Color RankColor = Color.ParseHex("#FF4081");
    private static readonly Color ReductionColor = Color.ParseHex("#78DA3B");

    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<AthleteOgImageService> _log;
    private readonly string _logoPath;
    private readonly ShareCardCanvas.Fonts _fonts;
    private readonly string _outputDir;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public AthleteOgImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<AthleteOgImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _fonts = ShareCardCanvas.GetFonts(_env.WebRootPath);
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
        string RankLabel,
        string MetricValue,
        string MetricLabel,
        string Description,
        string? ProfilePicUrl,
        string Signature);

    public bool IsConfigured => File.Exists(_logoPath) && _fonts.Exist;

    public bool TryGetCurrentPayload(string rawSlug, out AthleteOgPayload payload)
    {
        return TryGetCurrentPayload(rawSlug, rawLeagueContext: null, out payload);
    }

    public bool TryGetCurrentPayload(string rawSlug, string? rawLeagueContext, out AthleteOgPayload payload)
    {
        payload = null!;
        if (string.IsNullOrWhiteSpace(rawSlug))
            return false;

        var normalized = AthleteSlug.Normalize(rawSlug);
        var rankings = _athletes.GetRankingsOrder();
        var leagueSlug = ResolveLeagueSlugOrDefault(rawLeagueContext);

        var snapshot = _athletes.GetAthletesSnapshot();
        var athleteJson = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                AthleteSlug.Normalize(o["AthleteSlug"]?.GetValue<string>()),
                normalized,
                StringComparison.Ordinal));
        if (athleteJson is null)
            return false;
        var athlete = athleteJson;

        var athleteName = athlete["DisplayName"]?.GetValue<string>() ?? athlete["Name"]?.GetValue<string>();
        var name = !string.IsNullOrWhiteSpace(athleteName)
            ? athleteName!.Trim()
            : AthleteSlug.ToDisplayName(rawSlug);

        if (TryBuildSpecialLeaderboardPayload(normalized, rawLeagueContext, athlete, name, out payload))
            return true;
        if (IsSpecialLeaderboardContext(rawLeagueContext))
            return false;

        if (!TryGetRankRowForLeague(normalized, leagueSlug, rankings, snapshot, out var rankingRow, out var rank))
        {
            // Robust fallback for invalid/mismatched context links.
            leagueSlug = "ultimate";
            if (!TryGetRankRowForLeague(normalized, leagueSlug, rankings, snapshot, out rankingRow, out rank))
                return false;
        }

        var nameFromRanking = rankingRow["Name"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(athleteName) && !string.IsNullOrWhiteSpace(nameFromRanking))
            name = nameFromRanking!.Trim();

        var ageReductionFromRanking = GetDouble(rankingRow, "AgeDifference") ?? 0d;
        var stats = PhenoStatsCalculator.Compute(athlete, DateTime.UtcNow.Date);
        var hasBortzRaw = stats.BortzAgeReduction.HasValue && double.IsFinite(stats.BortzAgeReduction.Value);
        var ageReduction = hasBortzRaw
            ? stats.BortzAgeReduction!.Value
            : stats.AgeReduction ?? ageReductionFromRanking;
        var leagueName = ResolveLeagueDisplayName(leagueSlug);
        var profilePicUrl = athlete["ProfilePic"]?.GetValue<string>();
        var profileImageId = athlete["ProfileImageId"]?.GetValue<string>();
        var metricValue = FormatReduction(ageReduction);
        var description = $"{leagueName} rank #{rank}. {metricValue} years.";
        var signature = ComputeSignature(normalized, leagueSlug, rank, ageReduction, name, leagueName, "Current rank", metricValue, "Age Reduction", description, profilePicUrl, profileImageId);

        payload = new AthleteOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            LeagueSlug: leagueSlug,
            Name: name,
            LeagueName: leagueName,
            Rank: rank,
            AgeReduction: ageReduction,
            RankLabel: "Current rank",
            MetricValue: metricValue,
            MetricLabel: "Age Reduction",
            Description: description,
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
        using var image = new Image<Rgba32>(ShareCardCanvas.Width, ShareCardCanvas.Height);
        ShareCardCanvas.DrawBackground(image, RankColor);
        await ShareCardCanvas.DrawMastheadAsync(image, _logoPath, _fonts, payload.LeagueName, RankColor, _log, ct);
        await DrawPortraitAsync(image, payload, ct);
        DrawScoreboard(image, payload);
        ShareCardCanvas.DrawAccentBar(image, RankColor, ReductionColor);

        await image.SaveAsPngAsync(outputPath, ct);
    }

    private async Task DrawPortraitAsync(Image<Rgba32> image, AthleteOgPayload payload, CancellationToken ct)
    {
        image.Mutate(ctx => ShareCardCanvas.DrawPortraitRing(ctx, PortraitCenterX, BodyCenterY, PortraitRadius, RankColor, PortraitRing));

        var profilePath = ResolveProfilePath(payload.ProfilePicUrl);
        if (!string.IsNullOrWhiteSpace(profilePath) && File.Exists(profilePath))
        {
            try
            {
                var size = (int)(PortraitRadius * 2f);
                await using var profileStream = File.OpenRead(profilePath);
                using var profile = await ImageInput.LoadAsync<Rgba32>(profileStream, ct);
                profile.Mutate(ctx => ctx.AutoOrient().Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
                ImageMasks.MakeCircular(profile);
                image.Mutate(ctx =>
                {
                    ctx.DrawImage(profile, new Point((int)(PortraitCenterX - PortraitRadius), (int)(BodyCenterY - PortraitRadius)), 1f);
                    ctx.Draw(new Rgba32(255, 255, 255, 60), 1.5f, new EllipsePolygon(PortraitCenterX, BodyCenterY, PortraitRadius - 1f));
                });
                return;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render athlete profile image for {Slug}", payload.InternalSlug);
            }
        }

        image.Mutate(ctx => ShareCardCanvas.DrawMonogramDisc(ctx, _fonts, payload.Name, PortraitCenterX, BodyCenterY, PortraitRadius, RankColor));
    }

    private void DrawScoreboard(Image<Rgba32> image, AthleteOgPayload payload)
    {
        var (nameLines, nameFont) = ShareCardCanvas.FitHeadline(_fonts, payload.Name, 70f, 54f, ColumnWidth, 2, 46f);
        var lineHeight = nameFont.Size * 1.12f;
        var blockHeight = (nameLines.Count * lineHeight) + NameGap + TileHeight;
        var top = BodyCenterY - (blockHeight / 2f);
        var tileY = top + (nameLines.Count * lineHeight) + NameGap;
        var tileWidth = (ColumnWidth - TileGap) / 2f;

        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawLines(ctx, _fonts, nameLines, nameFont, ShareCardCanvas.Ink, ColumnX, top, lineHeight);
            DrawMetricTile(ctx, $"#{payload.Rank.ToString(CultureInfo.InvariantCulture)}", payload.RankLabel, RankColor, ColumnX, tileY, tileWidth);
            DrawMetricTile(ctx, payload.MetricValue, payload.MetricLabel, ReductionColor, ColumnX + tileWidth + TileGap, tileY, tileWidth);
        });
    }

    private void DrawMetricTile(IImageProcessingContext ctx, string value, string label, Color accent, float x, float y, float width)
    {
        const float inset = 28f;
        ShareCardCanvas.DrawPanel(ctx, x, y, width, TileHeight);
        ctx.Fill(ShareCardCanvas.WithAlpha(accent, 235), new RectangularPolygon(x + inset, y + 24f, 40f, 5f));

        var labelText = label.ToUpperInvariant();
        var labelFont = _fonts.Bold.CreateFont(17f, FontStyle.Bold);
        while (ShareCardCanvas.MeasureTracked(_fonts, labelText, labelFont, 1.8f) > width - (inset * 2f) && labelFont.Size > 12f)
            labelFont = _fonts.Bold.CreateFont(labelFont.Size - 1f, FontStyle.Bold);
        ShareCardCanvas.DrawTrackedText(ctx, _fonts, labelText, labelFont, ShareCardCanvas.MutedInk, new PointF(x + inset, y + 42f), 1.8f);

        var valueFont = ImageTextLayout.FitFontToWidth(_fonts.Display, value, 66f, 30f, width - (inset * 2f));
        var valueY = ShareCardCanvas.CenterTextY(value, valueFont, y + 74f + ((TileHeight - 74f - 18f) / 2f));
        ctx.DrawText(new RichTextOptions(valueFont)
        {
            Origin = new PointF(x + inset, valueY + 3f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, value, new Rgba32(0, 0, 0, 150));
        ctx.DrawText(new RichTextOptions(valueFont)
        {
            Origin = new PointF(x + inset, valueY),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, value, accent);
    }

    private string ComputeSignature(
        string normalizedSlug,
        string leagueSlug,
        int rank,
        double ageReduction,
        string name,
        string leagueName,
        string rankLabel,
        string metricValue,
        string metricLabel,
        string description,
        string? profilePicUrl,
        string? profileImageId)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;

        var raw = string.Join("|",
            "athlete-og-v38",
            normalizedSlug,
            leagueSlug,
            rank.ToString(CultureInfo.InvariantCulture),
            ageReduction.ToString("0.0000", CultureInfo.InvariantCulture),
            name,
            leagueName,
            rankLabel,
            metricValue,
            metricLabel,
            description,
            profilePicUrl ?? "",
            profileImageId ?? "",
            ImageAssetRevision.ForFile(ResolveProfilePath(profilePicUrl)),
            logoTicks.ToString(CultureInfo.InvariantCulture),
            _fonts.Ticks);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private bool TryBuildSpecialLeaderboardPayload(
        string normalizedSlug,
        string? rawLeagueContext,
        JsonObject athlete,
        string name,
        out AthleteOgPayload payload)
    {
        payload = null!;
        var context = NormalizeContextSlug(rawLeagueContext);
        if (string.IsNullOrWhiteSpace(context))
            return false;

        var profilePicUrl = athlete["ProfilePic"]?.GetValue<string>();
        var profileImageId = athlete["ProfileImageId"]?.GetValue<string>();

        if (string.Equals(context, "chronological-oldest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "chronological-youngest", StringComparison.OrdinalIgnoreCase))
        {
            var oldestFirst = string.Equals(context, "chronological-oldest", StringComparison.OrdinalIgnoreCase);
            var ranked = PhenoStatsCalculator.BuildAll(_athletes.GetAthletesSnapshot(), DateTime.UtcNow.Date)
                .Values
                .Where(result =>
                    result.DobUtc.HasValue &&
                    result.ChronoAge.HasValue &&
                    double.IsFinite(result.ChronoAge.Value))
                .OrderBy(result => oldestFirst ? -result.ChronoAge!.Value : result.ChronoAge!.Value)
                .ThenBy(result => result.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var index = ranked.FindIndex(result =>
                string.Equals(AthleteSlug.Normalize(result.Slug), normalizedSlug, StringComparison.Ordinal));
            if (index < 0)
                return false;

            var entry = ranked[index];
            var age = entry.ChronoAge!.Value.ToString("0.#", CultureInfo.InvariantCulture);
            var leagueName = oldestFirst ? "Oldest athlete" : "Youngest athlete";
            var description = $"{leagueName} rank #{index + 1}. Chronological age {age} years.";
            var signature = ComputeSignature(
                normalizedSlug,
                context,
                index + 1,
                entry.ChronoAge.Value,
                name,
                leagueName,
                "Age rank",
                age,
                "Chronological age",
                description,
                profilePicUrl,
                profileImageId);

            payload = new AthleteOgPayload(
                InternalSlug: normalizedSlug,
                RouteSlug: ToRouteSlug(normalizedSlug),
                LeagueSlug: context,
                Name: name,
                LeagueName: leagueName,
                Rank: index + 1,
                AgeReduction: entry.ChronoAge.Value,
                RankLabel: "Age rank",
                MetricValue: age,
                MetricLabel: "Chronological age",
                Description: description,
                ProfilePicUrl: profilePicUrl,
                Signature: signature);
            return true;
        }

        if (TryGetDomainContext(context, out var domainKey, out var domainLabel, out var domainClockLabel))
        {
            var winnerSlug = _athletes.GetBestDomainWinnerSlug(domainKey);
            if (!string.Equals(AthleteSlug.Normalize(winnerSlug), normalizedSlug, StringComparison.Ordinal))
                return false;

            var domainLeagueSlug = $"domain-{domainKey.Replace('_', '-')}";
            var leagueName = $"{domainLabel} domain leader";
            var metricLabel = domainClockLabel is null
                ? $"{domainLabel} profile"
                : $"{domainClockLabel} {domainLabel} profile";
            var description = domainClockLabel is null
                ? $"Strongest {domainLabel} profile in the Longevity World Cup field."
                : $"Strongest {domainClockLabel} {domainLabel} profile in the Longevity World Cup field.";
            var signature = ComputeSignature(
                normalizedSlug,
                domainLeagueSlug,
                1,
                0,
                name,
                leagueName,
                "Domain rank",
                "#1",
                metricLabel,
                description,
                profilePicUrl,
                profileImageId);

            payload = new AthleteOgPayload(
                InternalSlug: normalizedSlug,
                RouteSlug: ToRouteSlug(normalizedSlug),
                LeagueSlug: domainLeagueSlug,
                Name: name,
                LeagueName: leagueName,
                Rank: 1,
                AgeReduction: 0,
                RankLabel: "Domain rank",
                MetricValue: "#1",
                MetricLabel: metricLabel,
                Description: description,
                ProfilePicUrl: profilePicUrl,
                Signature: signature);
            return true;
        }

        if (string.Equals(context, "pheno", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "bortz", StringComparison.OrdinalIgnoreCase))
        {
            var clock = string.Equals(context, "bortz", StringComparison.OrdinalIgnoreCase) ? "bortz" : "pheno";
            if (!_athletes.TryGetBiologicalAgeLeaderboardEntry(normalizedSlug, clock, out var entry))
                return false;

            var biologicalAgeLeagueSlug = entry.Clock;
            var clockLabel = string.Equals(entry.Clock, "bortz", StringComparison.OrdinalIgnoreCase)
                ? "Bortz Age"
                : "Pheno Age";
            var leagueName = $"{clockLabel} leaderboard";
            var biologicalAge = entry.BiologicalAge.ToString("0.#", CultureInfo.InvariantCulture);
            var reduction = FormatReduction(entry.AgeReduction);
            var description = $"{leagueName} rank #{entry.Rank}. {clockLabel} {biologicalAge} years; age reduction {reduction} years.";
            var signature = ComputeSignature(
                normalizedSlug,
                biologicalAgeLeagueSlug,
                entry.Rank,
                entry.AgeReduction,
                name,
                leagueName,
                $"{clockLabel} rank",
                biologicalAge,
                clockLabel,
                description,
                profilePicUrl,
                profileImageId);

            payload = new AthleteOgPayload(
                InternalSlug: normalizedSlug,
                RouteSlug: ToRouteSlug(normalizedSlug),
                LeagueSlug: biologicalAgeLeagueSlug,
                Name: name,
                LeagueName: leagueName,
                Rank: entry.Rank,
                AgeReduction: entry.AgeReduction,
                RankLabel: $"{clockLabel} rank",
                MetricValue: biologicalAge,
                MetricLabel: clockLabel,
                Description: description,
                ProfilePicUrl: profilePicUrl,
                Signature: signature);
            return true;
        }

        if (string.Equals(context, "crowd", StringComparison.OrdinalIgnoreCase))
        {
            if (!_athletes.TryGetCrowdAgeLeaderboardEntry(normalizedSlug, out var entry))
                return false;

            var metricValue = entry.CrowdAge.ToString("0.#", CultureInfo.InvariantCulture);
            var countText = entry.CrowdCount.ToString("N0", CultureInfo.InvariantCulture);
            var crowdLeagueName = "Crowd Age leaderboard";
            var crowdDescription = $"{crowdLeagueName} rank #{entry.Rank}. Crowd Age {metricValue} years from {countText} guesses.";
            var signature = ComputeSignature(
                normalizedSlug,
                "crowd",
                entry.Rank,
                entry.CrowdAgeDifference,
                name,
                crowdLeagueName,
                "Crowd Age rank",
                metricValue,
                "Crowd Age",
                crowdDescription,
                profilePicUrl,
                profileImageId);

            payload = new AthleteOgPayload(
                InternalSlug: normalizedSlug,
                RouteSlug: ToRouteSlug(normalizedSlug),
                LeagueSlug: "crowd",
                Name: name,
                LeagueName: crowdLeagueName,
                Rank: entry.Rank,
                AgeReduction: entry.CrowdAgeDifference,
                RankLabel: "Crowd Age rank",
                MetricValue: metricValue,
                MetricLabel: "Crowd Age",
                Description: crowdDescription,
                ProfilePicUrl: profilePicUrl,
                Signature: signature);
            return true;
        }

        var baselineImprovementClock = context switch
        {
            "pheno-baseline-improvement" => "pheno",
            "bortz-baseline-improvement" => "bortz",
            _ => null
        };
        if (baselineImprovementClock is not null)
        {
            if (!_athletes.TryGetBaselineImprovementLeaderboardEntry(normalizedSlug, baselineImprovementClock, out var entry))
                return false;

            var isBortz = string.Equals(baselineImprovementClock, "bortz", StringComparison.OrdinalIgnoreCase);
            var baselineLeagueSlug = isBortz ? "bortz-baseline-improvement" : "pheno-baseline-improvement";
            var leagueName = isBortz ? "Bortz Age best improvement" : "Pheno Age best improvement";
            var baselineMetric = FormatReduction(entry.Improvement);
            var description = $"{leagueName} rank #{entry.Rank}. Improvement {baselineMetric} years from first to latest eligible result.";
            var signature = ComputeSignature(
                normalizedSlug,
                baselineLeagueSlug,
                entry.Rank,
                entry.Improvement,
                name,
                leagueName,
                "Baseline rank",
                baselineMetric,
                "Baseline improvement",
                description,
                profilePicUrl,
                profileImageId);

            payload = new AthleteOgPayload(
                InternalSlug: normalizedSlug,
                RouteSlug: ToRouteSlug(normalizedSlug),
                LeagueSlug: baselineLeagueSlug,
                Name: name,
                LeagueName: leagueName,
                Rank: entry.Rank,
                AgeReduction: entry.Improvement,
                RankLabel: "Baseline rank",
                MetricValue: baselineMetric,
                MetricLabel: "Baseline improvement",
                Description: description,
                ProfilePicUrl: profilePicUrl,
                Signature: signature);
            return true;
        }

        var improvementClock = context switch
        {
            "improvement" => "pheno",
            "pheno-improvement" => "pheno",
            "bortz-improvement" => "bortz",
            _ => null
        };
        if (improvementClock is null)
            return false;

        if (!_athletes.TryGetAgeImprovementLeaderboardEntry(normalizedSlug, improvementClock, out var improvementEntry))
            return false;

        var leagueSlug = string.Equals(improvementClock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "bortz-improvement"
            : "improvement";
        var improvementLeagueName = string.Equals(improvementClock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "Bortz Improvement leaderboard"
            : "Pheno Improvement leaderboard";
        var metric = FormatReduction(improvementEntry.Improvement);
        var improvementDescription = $"{improvementLeagueName} rank #{improvementEntry.Rank}. Improvement {metric} years from worst to latest eligible result.";
        var improvementSignature = ComputeSignature(
            normalizedSlug,
            leagueSlug,
            improvementEntry.Rank,
            improvementEntry.Improvement,
            name,
            improvementLeagueName,
            "Improvement rank",
            metric,
            "Improvement",
            improvementDescription,
            profilePicUrl,
            profileImageId);

        payload = new AthleteOgPayload(
            InternalSlug: normalizedSlug,
            RouteSlug: ToRouteSlug(normalizedSlug),
            LeagueSlug: leagueSlug,
            Name: name,
            LeagueName: improvementLeagueName,
            Rank: improvementEntry.Rank,
            AgeReduction: improvementEntry.Improvement,
            RankLabel: "Improvement rank",
            MetricValue: metric,
            MetricLabel: "Improvement",
            Description: improvementDescription,
            ProfilePicUrl: profilePicUrl,
            Signature: improvementSignature);
        return true;
    }

    private static bool TryGetDomainContext(
        string context,
        out string domainKey,
        out string domainLabel,
        out string? clockLabel)
    {
        domainKey = "";
        domainLabel = "";
        clockLabel = "Bortz";
        if (!context.StartsWith("domain-", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = context["domain-".Length..].Replace('-', '_');
        (domainKey, domainLabel, clockLabel) = token switch
        {
            "liver" => ("liver", "liver", "Bortz"),
            "kidney" => ("kidney", "kidney", "Bortz"),
            "metabolic" => ("metabolic", "metabolic", "Bortz"),
            "inflammation" => ("inflammation", "inflammation", null),
            "immune" => ("immune", "immune", "Bortz"),
            "vitamin_d" => ("vitamin_d", "vitamin D", "Bortz"),
            _ => ("", "", null)
        };
        return domainKey.Length > 0;
    }

    private static bool IsSpecialLeaderboardContext(string? rawContext)
    {
        var context = NormalizeContextSlug(rawContext);
        if (string.IsNullOrWhiteSpace(context))
            return false;

        if (string.Equals(context, "pheno", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "bortz", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "crowd", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "pheno-improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "bortz-improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "pheno-baseline-improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "bortz-baseline-improvement", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "chronological-oldest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(context, "chronological-youngest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return TryGetDomainContext(context, out _, out _, out _);
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
            var slug = AthleteSlug.Normalize(o["AthleteSlug"]?.GetValue<string>());
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
            var rowSlug = AthleteSlug.Normalize(row["AthleteSlug"]?.GetValue<string>());
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

    private static string NormalizeContextSlug(string? context)
    {
        var normalized = (context ?? string.Empty).Trim().ToLowerInvariant();
        for (var i = 0; i < 2; i++)
        {
            var decoded = Uri.UnescapeDataString(normalized);
            if (string.Equals(decoded, normalized, StringComparison.Ordinal))
                break;
            normalized = decoded;
        }

        return normalized.Replace('_', '-');
    }

    private static string ToRouteSlug(string normalizedSlug)
    {
        return normalizedSlug.Replace('_', '-');
    }

    private static string FormatReduction(double value)
    {
        return value.ToString("+#0.0;-#0.0;0.0", CultureInfo.InvariantCulture);
    }
}
