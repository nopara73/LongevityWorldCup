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
    private readonly string _logoPath;
    private readonly ShareCardCanvas.Fonts _fonts;
    private readonly string _outputDir;
    private readonly SemaphoreSlim _renderLock = new(1, 1);

    public LeagueOgImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<LeagueOgImageService> log)
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

        var top3Slugs = _athletes.GetTop3SlugsForLeague(normalized)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(3)
            .Select(AthleteSlug.Normalize)
            .ToArray();

        var snapshot = _athletes.GetAthletesSnapshot();
        var top3Names = GetAthleteDisplayNames(top3Slugs, snapshot);
        var profileImageIds = GetProfileImageIds(top3Slugs, snapshot);
        var profilePaths = top3Slugs.Select(slug => TryResolveProfilePath(slug, snapshot, out var path) ? path : null).ToArray();
        var displayName = LeagueDisplayNames[normalized];
        var signature = ComputeSignature(normalized, displayName, top3Slugs, top3Names, profileImageIds, profilePaths);

        payload = new LeagueOgPayload(
            InternalSlug: normalized,
            RouteSlug: ToRouteSlug(normalized),
            DisplayName: displayName,
            Top3Slugs: top3Slugs,
            Top3Names: top3Names,
            Signature: signature) { ProfilePaths = profilePaths };
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

        var title = payload.DisplayName;
        var subtitle = payload.Top3Names.Count == 0 ? "Rankings opening soon" : "Current top longevity athletes";
        var subtitleFont = _fonts.Regular.CreateFont(26f, FontStyle.Regular);
        var subtitleWidth = TextMeasurer.MeasureSize(subtitle, new RichTextOptions(subtitleFont)).Width;
        var titleWidth = RowWidth - subtitleWidth - 32f;
        var titleFont = ImageTextLayout.FitFontToWidth(_fonts.Bold, title, 62f, 32f, titleWidth, sizeStep: 1f);
        title = ImageTextLayout.EllipsizeToWidth(title, titleFont, titleWidth);
        var titleCenterY = TitleY + 34f;

        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawLines(ctx, _fonts, [title], titleFont, ShareCardCanvas.Ink, RowX, ShareCardCanvas.CenterTextY(title, titleFont, titleCenterY), 0f);
            ctx.DrawText(new RichTextOptions(subtitleFont)
            {
                Origin = new PointF(RowX + RowWidth - subtitleWidth, ShareCardCanvas.CenterTextY(subtitle, subtitleFont, titleCenterY + 6f)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, subtitle, ShareCardCanvas.MutedInk);
            ctx.Fill(ShareCardCanvas.WithAlpha(AccentColor, 230), new RectangularPolygon(RowX, TitleY + 88f, 120f, 6f));
        });

        for (var i = 0; i < LeaderboardRows.Length; i++)
        {
            var row = LeaderboardRows[i];
            var hasAthlete = i < payload.Top3Slugs.Count;
            var slug = hasAthlete ? payload.Top3Slugs[i] : "";
            var name = hasAthlete && i < payload.Top3Names.Count ? payload.Top3Names[i] : "OPEN";
            var profilePath = payload.ProfilePaths is { } captured
                ? (i < captured.Count ? captured[i] : null)
                : (TryResolveProfilePath(slug, _athletes.GetAthletesSnapshot(), out var currentPath) ? currentPath : null);
            await DrawLeaderboardRowAsync(image, row, slug, name, profilePath, ct);
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
        CancellationToken ct)
    {
        image.Mutate(ctx =>
        {
            ShareCardCanvas.DrawPanel(ctx, row.X, row.Y, row.Width, row.Height);
            ctx.Fill(ShareCardCanvas.WithAlpha(row.Accent, 240), ShareCardCanvas.RoundedRect(row.X, row.Y, 8f, row.Height, 4f));
        });

        var isOpen = string.IsNullOrWhiteSpace(athleteSlug);
        var drewPhoto = false;
        if (!isOpen && profilePath is not null)
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

        DrawRowText(image, row, isOpen ? "OPEN" : displayName, isOpen);
    }

    private static PointF PortraitCenter(LeaderboardRow row) => new(row.X + 156f, row.Y + (row.Height / 2f));

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

    private void DrawRowText(Image<Rgba32> image, LeaderboardRow row, string displayName, bool isOpen)
    {
        const float nameOffset = 228f;
        var rankText = "#" + row.Rank.ToString(CultureInfo.InvariantCulture);
        var rankFont = _fonts.Display.CreateFont(38f, FontStyle.Bold);
        var maxNameWidth = row.Width - nameOffset - 40f;
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
        });
    }

    private string ComputeSignature(string leagueSlug, string leagueDisplayName, IReadOnlyList<string> top3Slugs,
        IReadOnlyList<string> top3Names, IReadOnlyList<string> profileImageIds, IReadOnlyList<string?> profilePaths)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;

        var raw = string.Join("|",
            "league-og-v41",
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

    private static IReadOnlyList<string> GetAthleteDisplayNames(IEnumerable<string> top3Slugs, JsonArray snapshot)
    {
        var bySlug = snapshot
            .OfType<JsonObject>()
            .Select(o => new
            {
                Slug = AthleteSlug.Normalize(o["AthleteSlug"]?.GetValue<string>()),
                Name = (o["DisplayName"]?.GetValue<string>() ?? o["Name"]?.GetValue<string>() ?? "").Trim()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug))
            .GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

        return top3Slugs
            .Select(AthleteSlug.Normalize)
            .Select(s => bySlug.TryGetValue(s, out var n) ? n : AthleteSlug.ToDisplayName(s))
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

    private static string ToRouteSlug(string normalizedSlug)
    {
        return normalizedSlug.Replace('_', '-');
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
