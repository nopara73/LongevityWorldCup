using LongevityWorldCup.Website.Tools;
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
    private const float TitleY = 124f;
    private const float RowX = ShareCardCanvas.Margin;
    private const float RowWidth = ShareCardCanvas.Width - (ShareCardCanvas.Margin * 2f);
    private const float RowHeight = 98f;
    private static readonly Color AccentColor = Color.ParseHex("#78DA3B");
    private static readonly Color MutedNameColor = new(new Rgba32(255, 255, 255, 150));
    private static readonly Color GoldAccentColor = new(new Rgba32(230, 181, 55, 255));
    private static readonly Color SilverAccentColor = new(new Rgba32(196, 205, 209, 255));
    private static readonly Color BronzeAccentColor = new(new Rgba32(189, 103, 42, 255));

    private static readonly LeaderboardRow[] LeaderboardRows =
    [
        new(1, RowX, 256f, RowWidth, RowHeight, 72, GoldAccentColor),
        new(2, RowX, 368f, RowWidth, RowHeight, 72, SilverAccentColor),
        new(3, RowX, 480f, RowWidth, RowHeight, 72, BronzeAccentColor)
    ];

    private static readonly IReadOnlyDictionary<string, string> LeagueDisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ultimate"] = "Ultimate League",
            ["bortz"] = "Bortz Age League",
            ["pheno"] = "Pheno Age League",
            ["improvement"] = "Pheno Improvement League",
            ["bortz-improvement"] = "Bortz Improvement League",
            ["crowd"] = "Crowd Age League",
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
    private readonly IAthleteSnapshotProvider _athletes;
    private readonly ILogger<LeagueOgImageService> _log;
    private readonly string _logoPath;
    private readonly ShareCardCanvas.Fonts _fonts;
    private readonly string _outputDir;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public LeagueOgImageService(IWebHostEnvironment env, IAthleteSnapshotProvider athletes, ILogger<LeagueOgImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _fonts = ShareCardCanvas.GetFonts(_env.WebRootPath);
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "league");
    }

    public sealed record LeagueOgPayload(
        string InternalSlug,
        string RouteSlug,
        string DisplayName,
        IReadOnlyList<string> Top3Slugs,
        IReadOnlyList<string> Top3Names,
        string Signature)
    {
        internal IReadOnlyList<string?>? ProfilePaths { get; init; }
        public string Subtitle { get; init; } = "Current standings";
        public string PreviewTitle { get; init; } = "";
        public string SharePath { get; init; } = "";
        public string? ImageQuery { get; init; }
        public bool Neutral { get; init; }
        public bool ByFlag { get; init; }
        public IReadOnlyList<string> Scores { get; init; } = [];
        public IReadOnlyList<string> ScoreLabels { get; init; } = [];
        public IReadOnlyList<string> Ranks { get; init; } = [];
        internal string? FlagPath { get; init; }
        internal bool HasFlag { get; init; }
        internal IReadOnlyList<string?> RowFlagPaths { get; init; } = [];
    }

    private readonly record struct LeaderboardRow(
        int Rank,
        float X,
        float Y,
        float Width,
        float Height,
        int PortraitSize,
        Color Accent);

    public bool IsConfigured =>
        File.Exists(_logoPath) && _fonts.Exist;

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

        return TryGetLeaderboardPayload(normalized == "ultimate" ? "/leaderboard" : "/league/" + normalized,
            QueryCollection.Empty, out payload);
    }

    public bool TryGetLeaderboardPayload(string path, IQueryCollection query, out LeagueOgPayload payload)
        => TryGetLeaderboardPayload(new PublicLeaderboardSnapshot(_athletes.GetAthletesSnapshot(), DateTime.UtcNow.Date), path, query, out payload);

    internal bool TryGetLeaderboardPayload(PublicLeaderboardSnapshot snapshot, string path, IQueryCollection query, out LeagueOgPayload payload)
    {
        payload = null!;
        var source = snapshot.Source;
        if (!LeaderboardShareView.TryCreate(snapshot, path, query, out var share)) return false;
        var selection = share.Selection;
        var rows = share.Neutral ? [] : selection.Snapshot.Rows.Take(3).ToArray();
        var teams = share.Neutral ? [] : selection.FlagTeams.Take(3).ToArray();
        var slugs = selection.ByFlag ? teams.Select(team => team.Flag.Slug).ToArray() : rows.Select(row => row.Slug).ToArray();
        var names = selection.ByFlag ? teams.Select(team => team.Flag.Name).ToArray() : rows.Select(row => row.DisplayName).ToArray();
        var profiles = selection.ByFlag ? [] : slugs.Select(slug => TryResolveProfilePath(slug, source, out var profile) ? profile : null).ToArray();
        var scores = selection.ByFlag
            ? teams.Select(team => team.Rank.HasValue ? PublicHtmlFormat.Signed(team.Score, 2) + " years" : team.Athletes.Count + "/3 to qualify").ToArray()
            : rows.Select(row => row.EffectiveAgeReductionYears.HasValue ? PublicHtmlFormat.Signed(row.EffectiveAgeReductionYears, row.MetricDecimals) + " years" : "—").ToArray();
        var ranks = selection.ByFlag ? teams.Select(team => team.Rank.HasValue ? "#" + team.Rank.Value : "—").ToArray()
            : rows.Select(row => "#" + row.Rank).ToArray();
        var scoreLabels = selection.ByFlag ? teams.Select(team => FlagLeaderboard.Label(team.View)).ToArray()
            : rows.Select(row => selection.View == "ultimate" ? row.Track : selection.MetricLabel).ToArray();
        var flagPath = selection.Flag is { } flag ? ResolveFlagPath(flag.Name) : null;
        var rowFlags = teams.Select(team => ResolveFlagPath(team.Flag.Name)).ToArray();
        var key = share.LegacySlug ?? "board-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(share.ImageQuery))).ToLowerInvariant()[..16];
        var signature = ComputeSignature(key, share.Title, slugs, names, GetProfileImageIds(slugs, source), profiles);
        var visualState = string.Join('|', signature, share.Subtitle, share.Neutral, selection.ByFlag,
            string.Join(',', scores), string.Join(',', scoreLabels), string.Join(',', ranks), ImageAssetRevision.ForFile(flagPath),
            string.Join(',', rowFlags.Select(ImageAssetRevision.ForFile)));
        signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(visualState))).ToLowerInvariant()[..12];
        payload = new LeagueOgPayload(key, share.LegacySlug ?? "leaderboard", share.Title, slugs, names, signature)
        {
            ProfilePaths = profiles, Subtitle = share.Subtitle, SharePath = share.Path,
            PreviewTitle = selection.DocumentTitle.Length > 0 ? selection.DocumentTitle : share.Title,
            ImageQuery = share.LegacySlug is null ? share.ImageQuery : null,
            Neutral = share.Neutral, ByFlag = selection.ByFlag, Scores = scores, ScoreLabels = scoreLabels, Ranks = ranks,
            FlagPath = flagPath, HasFlag = selection.Flag is not null, RowFlagPaths = rowFlags
        };
        return true;
    }

    public string BuildVersionedImageUrl(string siteBaseUrl, LeagueOgPayload payload)
    {
        return payload.ImageQuery is null
            ? $"{siteBaseUrl}/og/league/{payload.RouteSlug}.png?v={payload.Signature}"
            : $"{siteBaseUrl}/og/leaderboard.png?v={payload.Signature}{payload.ImageQuery.Replace('?', '&')}";
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

            CleanupOldRenders(payload.InternalSlug, outputPath);
            TrimBoardRenders(outputPath);
            return outputPath;
        }
        finally
        {
            _renderLock.Release();
        }
    }

    private async Task RenderImageAsync(LeagueOgPayload payload, string outputPath, CancellationToken ct)
    {
        using var image = new Image<Rgba32>(ShareCardCanvas.Width, ShareCardCanvas.Height);
        ShareCardCanvas.DrawBackground(image, AccentColor);
        await ShareCardCanvas.DrawMastheadAsync(image, _logoPath, _fonts, caption: null, AccentColor, _log, ct);

        var titleX = RowX + (payload.HasFlag ? 108f : 0f);
        var titleWidth = RowWidth - (titleX - RowX);
        var (titleLines, titleFont) = ShareCardCanvas.FitHeadline(_fonts, payload.DisplayName, 58f, 44f, titleWidth, 2, 32f);
        if (titleLines.Count > 1)
            (titleLines, titleFont) = ShareCardCanvas.FitHeadline(_fonts, payload.DisplayName, 44f, 40f, titleWidth, 2, 32f);
        var subtitle = payload.Subtitle;
        var subtitleFont = _fonts.Regular.CreateFont(25f, FontStyle.Regular);
        if (payload.FlagPath is not null) await DrawFlagAsync(image, payload.FlagPath, RowX, TitleY + 12f, 84, 63, ct);
        else if (payload.HasFlag) DrawGenericFlag(image, RowX + 16, TitleY + 12);
        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawLines(ctx, _fonts, titleLines, titleFont, ShareCardCanvas.Ink,
                titleX, titleLines.Count > 1 ? 112f : 130f, titleFont.Size * 1.05f);
            ShareCardCanvas.DrawLines(ctx, _fonts, [subtitle], subtitleFont, ShareCardCanvas.MutedInk, titleX, 218f, 0f);
        });

        if (!payload.Neutral && payload.Top3Names.Count == 0)
        {
            var emptyFont = _fonts.Regular.CreateFont(38f);
            image.Mutate(ctx =>
            {
                ShareCardCanvas.DrawPanel(ctx, RowX, 290f, RowWidth, 210f);
                ctx.DrawText(new RichTextOptions(emptyFont)
                {
                    Origin = new PointF(ShareCardCanvas.Width / 2f, 375f), HorizontalAlignment = HorizontalAlignment.Center
                }, payload.ByFlag ? "No qualifying teams" : "No qualifying athletes", ShareCardCanvas.MutedInk);
            });
        }
        var rowCount = payload.Neutral ? LeaderboardRows.Length : Math.Min(LeaderboardRows.Length, payload.Top3Slugs.Count);
        for (var i = 0; i < rowCount; i++)
        {
            var row = LeaderboardRows[i];
            if (payload.Neutral)
            {
                image.Mutate(ctx =>
                {
                    ShareCardCanvas.DrawPanel(ctx, row.X, row.Y, row.Width, row.Height);
                    ctx.Fill(new Rgba32(255, 255, 255, 26), new EllipsePolygon(row.X + 58, row.Y + 49, 24));
                    ctx.Fill(new Rgba32(255, 255, 255, 24), ShareCardCanvas.RoundedRect(row.X + 110, row.Y + 39, 380 - i * 60, 20, 10));
                });
                continue;
            }
            var hasAthlete = i < payload.Top3Slugs.Count;
            var slug = hasAthlete ? payload.Top3Slugs[i] : "";
            var name = hasAthlete && i < payload.Top3Names.Count ? payload.Top3Names[i] : "OPEN";
            var profilePath = payload.ProfilePaths is { } captured
                ? (i < captured.Count ? captured[i] : null)
                : (TryResolveProfilePath(slug, _athletes.GetAthletesSnapshot(), out var currentPath) ? currentPath : null);
            await DrawLeaderboardRowAsync(image, row, slug, name, profilePath,
                i < payload.RowFlagPaths.Count ? payload.RowFlagPaths[i] : null, payload.ByFlag,
                i < payload.Ranks.Count ? payload.Ranks[i] : "—",
                i < payload.Scores.Count ? payload.Scores[i] : "",
                i < payload.ScoreLabels.Count ? payload.ScoreLabels[i] : "", ct);
        }

        ShareCardCanvas.DrawAccentBar(image, AccentColor);
        await image.SaveAsPngAsync(outputPath, ct);
    }

    private async Task DrawLeaderboardRowAsync(
        Image<Rgba32> image,
        LeaderboardRow row,
        string athleteSlug,
        string displayName,
        string? profilePath,
        string? flagPath,
        bool byFlag,
        string rank,
        string score,
        string scoreLabel,
        CancellationToken ct)
    {
        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawPanel(ctx, row.X, row.Y, row.Width, row.Height);
            ctx.Fill(ShareCardCanvas.WithAlpha(row.Accent, 240), ShareCardCanvas.RoundedRect(row.X, row.Y, 8f, row.Height, 4f));
        });

        var isOpen = string.IsNullOrWhiteSpace(athleteSlug);
        var drewPhoto = false;
        if (!isOpen && byFlag)
        {
            if (flagPath is not null) await DrawFlagAsync(image, flagPath, row.X + 114, row.Y + 22, 76, 57, ct);
            else DrawGenericFlag(image, row.X + 124, row.Y + 23);
            drewPhoto = true;
        }
        else if (!isOpen && profilePath is not null)
        {
            try
            {
                await DrawProfilePortraitAsync(image, profilePath, row, ct);
                drewPhoto = true;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render league top3 profile image for {AthleteSlug}: {Path}", athleteSlug, profilePath);
            }
        }

        if (!drewPhoto)
            DrawOpenPortrait(image, row, isOpen ? null : displayName);

        DrawRowText(image, row, isOpen ? "OPEN" : displayName, isOpen, rank, score, scoreLabel);
    }

    private static PointF PortraitCenter(LeaderboardRow row) => new(row.X + 156f, row.Y + (row.Height / 2f));

    private string? ResolveFlagPath(string name)
    {
        var code = FlagIconCatalog.GetCode(name);
        if (code is null) return null;
        // Raster copies of the site's SVG artwork avoid a native SVG dependency.
        foreach (var relative in new[] { $"assets/flags/share/{code}.png", $"assets/flags/{code}.webp", $"assets/flags/{code}.png" })
        {
            var path = IOPath.Combine(_env.WebRootPath, relative.Replace('/', IOPath.DirectorySeparatorChar));
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static async Task DrawFlagAsync(Image<Rgba32> image, string path, float x, float y, int width, int height, CancellationToken ct)
    {
        using var flag = await ImageInput.LoadAsync<Rgba32>(path, ct);
        flag.Mutate(ctx => ctx.Resize(new ResizeOptions { Size = new Size(width, height), Mode = ResizeMode.Pad, PadColor = Color.Transparent }));
        image.Mutate(ctx => ctx.DrawImage(flag, new Point((int)x, (int)y), 1f));
    }

    private static void DrawGenericFlag(Image<Rgba32> image, float x, float y) => image.Mutate(ctx =>
    {
        ctx.Fill(ShareCardCanvas.MutedInk, new RectangularPolygon(x, y, 4, 58));
        ctx.Fill(ShareCardCanvas.MutedInk, new Polygon(new LinearLineSegment(new PointF(x + 4, y), new PointF(x + 52, y + 5),
            new PointF(x + 45, y + 34), new PointF(x + 4, y + 29))));
    });

    private void TrimBoardRenders(string keepPath)
    {
        // Named routes are small; combinations must not grow disk use without a bound.
        foreach (var file in new DirectoryInfo(_outputDir).EnumerateFiles("board-*.png")
            .Where(file => file.FullName != keepPath).OrderByDescending(file => file.LastWriteTimeUtc).Skip(255))
        {
            try { file.Delete(); }
            catch (IOException ex) { _log.LogDebug(ex, "Could not evict leaderboard preview {Path}", file.FullName); }
        }
    }

    private async Task DrawProfilePortraitAsync(Image<Rgba32> image, string profilePath, LeaderboardRow row, CancellationToken ct)
    {
        await using var profileStream = File.OpenRead(profilePath);
        using var profile = await ImageInput.LoadAsync<Rgba32>(profileStream, ct);
        var size = row.PortraitSize;
        profile.Mutate(ctx => ctx.AutoOrient().Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }).Brightness(1.08f).Contrast(1.06f).Saturate(1.05f));
        ImageMasks.MakeCircular(profile);

        var center = PortraitCenter(row);
        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawPortraitRing(ctx, center.X, center.Y, size / 2f, row.Accent, 4f);
            ctx.DrawImage(profile, new Point((int)MathF.Round(center.X - (size / 2f)), (int)MathF.Round(center.Y - (size / 2f))), 1f);
            ctx.Draw(new Rgba32(255, 255, 255, 70), 1.4f, new EllipsePolygon(center.X, center.Y, (size / 2f) - 1f));
        });
    }

    private void DrawOpenPortrait(Image<Rgba32> image, LeaderboardRow row, string? athleteName)
    {
        var center = PortraitCenter(row);
        var radius = row.PortraitSize / 2f;
        image.Mutate(ctx =>
        {
            if (athleteName is null)
            {
                ctx.Fill(new Rgba32(12, 15, 18, 235), new EllipsePolygon(center.X, center.Y, radius));
                ctx.Draw(ShareCardCanvas.WithAlpha(row.Accent, 200), 3f, new EllipsePolygon(center.X, center.Y, radius + 1.5f));
                ctx.Draw(new Rgba32(255, 255, 255, 46), 1.2f, new EllipsePolygon(center.X, center.Y, radius - 8f));
                return;
            }

            ShareCardCanvas.DrawPortraitRing(ctx, center.X, center.Y, radius, row.Accent, 4f);
            ShareCardCanvas.DrawMonogramDisc(ctx, _fonts, athleteName, center.X, center.Y, radius, row.Accent);
        });
    }

    private void DrawRowText(Image<Rgba32> image, LeaderboardRow row, string displayName, bool isOpen, string rankText, string score, string scoreLabel)
    {
        const float nameOffset = 228f;
        var rankFont = _fonts.Display.CreateFont(38f, FontStyle.Bold);
        var maxNameWidth = row.Width - nameOffset - (score.Length > 0 ? 245f : 40f);
        var nameFont = ImageTextLayout.FitFontToWidth(_fonts.Bold, displayName, 42f, 26f, maxNameWidth, sizeStep: 1f, fallbackFonts: _fonts.Fallbacks);
        var name = ImageTextLayout.EllipsizeToWidth(displayName, nameFont, maxNameWidth, _fonts.Fallbacks);
        var centerY = row.Y + (row.Height / 2f);

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(rankFont)
            {
                Origin = new PointF(row.X + 34f, ShareCardCanvas.CenterTextY(rankText, rankFont, centerY)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, rankText, ShareCardCanvas.WithAlpha(row.Accent, 245));
            ShareCardCanvas.DrawLines(ctx, _fonts, [name], nameFont, isOpen ? MutedNameColor : ShareCardCanvas.Ink, row.X + nameOffset, ShareCardCanvas.CenterTextY(name, nameFont, centerY, _fonts), 0f);
            if (score.Length > 0)
            {
                var scoreFont = _fonts.Bold.CreateFont(30f);
                var labelFont = _fonts.Regular.CreateFont(20f);
                ctx.DrawText(new RichTextOptions(scoreFont) { Origin = new PointF(row.X + row.Width - 26, row.Y + 22), HorizontalAlignment = HorizontalAlignment.Right }, score, ShareCardCanvas.Ink);
                ctx.DrawText(new RichTextOptions(labelFont) { Origin = new PointF(row.X + row.Width - 26, row.Y + 59), HorizontalAlignment = HorizontalAlignment.Right }, scoreLabel, ShareCardCanvas.MutedInk);
            }
        });
    }

    private string ComputeSignature(string leagueSlug, string leagueDisplayName, IReadOnlyList<string> top3Slugs,
        IReadOnlyList<string> top3Names, IReadOnlyList<string> profileImageIds, IReadOnlyList<string?> profilePaths)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;

        var raw = string.Join("|",
            "league-og-v43",
            leagueSlug,
            leagueDisplayName,
            string.Join(",", top3Slugs),
            string.Join("\n", top3Names),
            string.Join(",", profileImageIds),
            string.Join(",", profilePaths.Select(path => path is null ? "" : IOPath.GetRelativePath(_env.WebRootPath, path).Replace('\\', '/'))),
            string.Join(",", profilePaths.Select(ImageAssetRevision.ForFile)),
            logoTicks.ToString(CultureInfo.InvariantCulture),
            _fonts.Ticks);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
    }

    private static IReadOnlyList<string> GetProfileImageIds(IEnumerable<string> athleteSlugs, JsonArray snapshot)
    {
        var bySlug = snapshot
            .OfType<JsonObject>()
            .Select(o => new
            {
                Slug = AthleteSlug.Normalize(o["AthleteSlug"]?.GetValue<string>()),
                ProfileImageId = o["ProfileImageId"]?.GetValue<string>()
                    ?? o["ProfilePic"]?.GetValue<string>()
                    ?? ""
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug))
            .GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ProfileImageId, StringComparer.Ordinal);

        return athleteSlugs
            .Select(AthleteSlug.Normalize)
            .Select(s => bySlug.TryGetValue(s, out var profileImageId) ? profileImageId : "")
            .ToArray();
    }

    private bool TryResolveProfilePath(string athleteSlug, JsonArray snapshot, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(athleteSlug))
            return false;

        var normalizedSlug = AthleteSlug.Normalize(athleteSlug);
        var athlete = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o =>
                string.Equals(
                    AthleteSlug.Normalize(o["AthleteSlug"]?.GetValue<string>()),
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
