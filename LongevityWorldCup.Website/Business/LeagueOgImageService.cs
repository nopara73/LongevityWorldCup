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
    private const float HeaderX = 58f;
    private const float LeagueTitleY = 74f;
    private const float LeagueSubtitleY = 145f;
    private static readonly Color LeagueTitleColor = Color.White;
    private static readonly Color LeagueSubtitleColor = new(new Rgba32(255, 255, 255, 205));
    private static readonly Color AccentColor = ParseHex("78DA3B");
    private static readonly Color BackgroundTop = ParseHex("05080B");
    private static readonly Color BackgroundBottom = ParseHex("15181B");
    private static readonly Rgba32 RowFillColor = new(34, 42, 38, 255);
    private static readonly Rgba32 RowStrokeColor = new(255, 255, 255, 26);
    private static readonly Rgba32 RowShadowColor = new(0, 0, 0, 70);
    private static readonly Color RowMutedTextColor = new(new Rgba32(255, 255, 255, 172));
    private static readonly Color GoldAccentColor = new(new Rgba32(230, 181, 55, 255));
    private static readonly Color SilverAccentColor = new(new Rgba32(196, 205, 209, 255));
    private static readonly Color BronzeAccentColor = new(new Rgba32(189, 103, 42, 255));

    private static readonly LeaderboardRow[] LeaderboardRows =
    [
        new(1, 120f, 216f, 960f, 94f, 76, GoldAccentColor),
        new(2, 120f, 326f, 870f, 86f, 68, SilverAccentColor),
        new(3, 120f, 430f, 780f, 86f, 68, BronzeAccentColor)
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
    private readonly string _boldFontPath;
    private readonly string _regularFontPath;
    private readonly string _outputDir;
    private readonly FontCollection _fonts = new();
    private readonly SemaphoreSlim _renderLock = new(1, 1);
    private FontFamily _boldFamily;
    private FontFamily _regularFamily;
    private bool _fontsLoaded;

    public LeagueOgImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<LeagueOgImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _boldFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _regularFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Regular.ttf");
        _outputDir = IOPath.Combine(_env.WebRootPath, "generated", "og", "league");
    }

    public sealed record LeagueOgPayload(
        string InternalSlug,
        string RouteSlug,
        string DisplayName,
        IReadOnlyList<string> Top3Slugs,
        IReadOnlyList<string> Top3Names,
        string Signature);

    private readonly record struct LeaderboardRow(
        int Rank,
        float X,
        float Y,
        float Width,
        float Height,
        int PortraitSize,
        Color Accent);

    public bool IsConfigured =>
        File.Exists(_logoPath) &&
        File.Exists(_boldFontPath) &&
        File.Exists(_regularFontPath);

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
        using var image = new Image<Rgba32>(CanvasWidth, CanvasHeight);
        DrawBackground(image);

        var fonts = GetFontFamilies();
        await DrawBrandAsync(image, fonts.Bold, ct);
        await DrawLeaderboardRowsAsync(image, payload, fonts.Bold, ct);

        var titleFont = FitFontToWidth(fonts.Bold, payload.DisplayName, 56f, 38f, 800f);
        var subtitleFont = fonts.Regular.CreateFont(24f, FontStyle.Regular);
        var title = payload.DisplayName;
        var subtitle = payload.Top3Names.Count == 0 ? "Rankings opening soon" : "Current top longevity athletes";

        image.Mutate(ctx =>
        {
            ctx.Fill(new Rgba32(120, 218, 59, 220), new RectangularPolygon(506f, LeagueSubtitleY + 41f, 188f, 5f));
            DrawTextShadow(ctx, title, titleFont, new PointF(CanvasWidth / 2f, LeagueTitleY), HorizontalAlignment.Center, 3f);
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
            }, subtitle, LeagueSubtitleColor);
        });

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

    private async Task DrawBrandAsync(Image<Rgba32> image, FontFamily boldFamily, CancellationToken ct)
    {
        var brandFont = boldFamily.CreateFont(21f, FontStyle.Bold);

        try
        {
            using var logo = await LoadLogoMarkAsync(ct);
            using var smallLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(54, 54),
                Mode = ResizeMode.Max
            }));
            using var backgroundLogo = logo.Clone(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(430, 430),
                Mode = ResizeMode.Max
            }));

            image.Mutate(ctx =>
            {
                ctx.DrawImage(backgroundLogo, new Point(807, 64), 0.016f);
                ctx.DrawImage(smallLogo, new Point((int)HeaderX, 36), 0.96f);
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw league OG logo.");
        }

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(brandFont)
            {
                Origin = new PointF(HeaderX + 68f, 39f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "LONGEVITY\nWORLD CUP", LeagueTitleColor);
        });
    }

    private async Task<Image<Rgba32>> LoadLogoMarkAsync(CancellationToken ct)
    {
        await using var logoStream = File.OpenRead(_logoPath);
        var logo = await Image.LoadAsync<Rgba32>(logoStream, ct);
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

    private async Task DrawLeaderboardRowsAsync(Image<Rgba32> image, LeagueOgPayload payload, FontFamily boldFamily, CancellationToken ct)
    {
        for (var i = 0; i < LeaderboardRows.Length; i++)
        {
            var row = LeaderboardRows[i];
            var hasAthlete = i < payload.Top3Slugs.Count;
            var slug = hasAthlete ? payload.Top3Slugs[i] : "";
            var name = hasAthlete && i < payload.Top3Names.Count ? payload.Top3Names[i] : "OPEN";
            await DrawLeaderboardRowAsync(image, row, slug, name, boldFamily, ct);
        }
    }

    private async Task DrawLeaderboardRowAsync(
        Image<Rgba32> image,
        LeaderboardRow row,
        string athleteSlug,
        string displayName,
        FontFamily boldFamily,
        CancellationToken ct)
    {
        DrawRowPanel(image, row);

        var profilePath = "";
        var hasPortrait = !string.IsNullOrWhiteSpace(athleteSlug) && TryResolveProfilePath(athleteSlug, out profilePath);
        if (hasPortrait)
        {
            try
            {
                await DrawProfilePortraitAsync(image, profilePath, row, ct);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Failed to render league top3 profile image for {AthleteSlug}: {Path}", athleteSlug, profilePath);
                DrawOpenPortrait(image, row);
                displayName = string.IsNullOrWhiteSpace(displayName) ? "OPEN" : displayName;
            }
        }
        else
        {
            DrawOpenPortrait(image, row);
            displayName = "OPEN";
        }

        DrawRowText(image, row, displayName, boldFamily);
    }

    private static void DrawRowPanel(Image<Rgba32> image, LeaderboardRow row)
    {
        FillRectangleLayer(image, RowShadowColor, row.X + 6f, row.Y + 8f, row.Width, row.Height);
        FillRectangleLayer(image, RowStrokeColor, row.X, row.Y, row.Width, row.Height);
        FillRectangleLayer(image, RowFillColor, row.X + 1f, row.Y + 1f, row.Width - 2f, row.Height - 2f);
        FillRectangleLayer(image, ToRgba(row.Accent, 238), row.X + 12f, row.Y + 16f, 7f, row.Height - 32f);
    }

    private async Task DrawProfilePortraitAsync(Image<Rgba32> image, string profilePath, LeaderboardRow row, CancellationToken ct)
    {
        await using var profileStream = File.OpenRead(profilePath);
        using var profile = await Image.LoadAsync<Rgba32>(profileStream, ct);
        var size = row.PortraitSize;
        profile.Mutate(ctx => ctx.AutoOrient().Resize(new ResizeOptions
        {
            Size = new Size(size, size),
            Mode = ResizeMode.Crop,
            Position = AnchorPositionMode.Center
        }).Brightness(1.08f).Contrast(1.06f).Saturate(1.05f));
        MakeCircular(profile);

        var portraitX = (int)MathF.Round(row.X + 34f);
        var portraitY = (int)MathF.Round(row.Y + ((row.Height - size) / 2f));
        var centerX = portraitX + (size / 2f);
        var centerY = portraitY + (size / 2f);

        image.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.Fill(new Rgba32(0, 0, 0, 128), new EllipsePolygon(centerX + 5f, centerY + 7f, (size / 2f) + 6f));
            ctx.Fill(new Rgba32(255, 255, 255, 16), new EllipsePolygon(centerX, centerY, (size / 2f) + 8f));
            ctx.Draw(ToRgba(row.Accent, 236), 4f, new EllipsePolygon(centerX, centerY, (size / 2f) + 2.5f));
            ctx.DrawImage(profile, new Point(portraitX, portraitY), 1f);
            ctx.Draw(new Rgba32(255, 255, 255, 70), 1.4f, new EllipsePolygon(centerX, centerY, (size / 2f) - 1f));
        });
    }

    private static void DrawOpenPortrait(Image<Rgba32> image, LeaderboardRow row)
    {
        var size = row.PortraitSize;
        var portraitX = row.X + 34f;
        var portraitY = row.Y + ((row.Height - size) / 2f);
        var centerX = portraitX + (size / 2f);
        var centerY = portraitY + (size / 2f);

        image.Mutate(ctx =>
        {
            ctx.SetGraphicsOptions(new GraphicsOptions
            {
                Antialias = true,
                AntialiasSubpixelDepth = 16
            });
            ctx.Fill(new Rgba32(0, 0, 0, 122), new EllipsePolygon(centerX + 5f, centerY + 7f, (size / 2f) + 6f));
            ctx.Fill(new Rgba32(12, 15, 18, 225), new EllipsePolygon(centerX, centerY, size / 2f));
            ctx.Draw(ToRgba(row.Accent, 220), 3.5f, new EllipsePolygon(centerX, centerY, (size / 2f) + 1.5f));
            ctx.Draw(new Rgba32(255, 255, 255, 54), 1.2f, new EllipsePolygon(centerX, centerY, (size / 2f) - 7f));
        });
    }

    private static void DrawRowText(Image<Rgba32> image, LeaderboardRow row, string displayName, FontFamily boldFamily)
    {
        var rankText = "#" + row.Rank.ToString(CultureInfo.InvariantCulture);
        var rankFont = boldFamily.CreateFont(row.Rank == 1 ? 27f : 25f, FontStyle.Bold);
        var nameFont = FitFontToWidth(boldFamily, displayName, row.Rank == 1 ? 34f : 31f, 22f, row.Width - 286f);
        var centerY = row.Y + (row.Height / 2f);
        var rankX = row.X + 132f;
        var nameX = row.X + 206f;
        var rankY = CenterTextY(rankText, rankFont, centerY);
        var nameY = CenterTextY(displayName, nameFont, centerY);
        var nameColor = string.Equals(displayName, "OPEN", StringComparison.OrdinalIgnoreCase)
            ? RowMutedTextColor
            : LeagueTitleColor;

        image.Mutate(ctx =>
        {
            DrawTextShadow(ctx, displayName, nameFont, new PointF(nameX, nameY), HorizontalAlignment.Left, 2.5f);
            ctx.DrawText(new RichTextOptions(rankFont)
            {
                Origin = new PointF(rankX, rankY),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, rankText, ToRgba(row.Accent, 245));
            ctx.DrawText(new RichTextOptions(nameFont)
            {
                Origin = new PointF(nameX, nameY),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, displayName, nameColor);
        });
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

    private static void DrawBackground(Image<Rgba32> image)
    {
        var top = BackgroundTop.ToPixel<Rgba32>();
        var bottom = BackgroundBottom.ToPixel<Rgba32>();
        var accent = AccentColor.ToPixel<Rgba32>();

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
                    var edgeLight = MathF.Max(0f, 1f - MathF.Sqrt(MathF.Pow((horizontal - 0.76f) / 0.52f, 2f) + MathF.Pow((vertical - 0.23f) / 0.68f, 2f)));
                    var laneLight = MathF.Max(0f, 1f - MathF.Abs((horizontal + vertical * 0.34f) - 0.38f) / 0.08f) * 0.16f;
                    var rowLight = MathF.Max(0f, 1f - MathF.Abs(vertical - 0.58f) / 0.35f) * 0.035f;
                    var accentLight = MathF.Min(0.22f, edgeLight * 0.17f + laneLight + rowLight);

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
            ctx.Fill(new Rgba32(0, 0, 0, 72), new RectangularPolygon(0, 0, CanvasWidth, 72f));
            ctx.Fill(new Rgba32(0, 0, 0, 76), new RectangularPolygon(0, CanvasHeight - 32f, CanvasWidth, 32f));
        });
    }

    private (FontFamily Bold, FontFamily Regular) GetFontFamilies()
    {
        if (_fontsLoaded)
            return (_boldFamily, _regularFamily);

        _boldFamily = _fonts.Add(_boldFontPath);
        _regularFamily = _fonts.Add(_regularFontPath);
        _fontsLoaded = true;
        return (_boldFamily, _regularFamily);
    }

    private static void FillRectangleLayer(Image<Rgba32> image, Rgba32 color, float x, float y, float width, float height)
    {
        if (color.A == 0)
            return;

        using var layer = new Image<Rgba32>(CanvasWidth, CanvasHeight, Color.Transparent);
        layer.Mutate(ctx => ctx.Fill(new Rgba32(color.R, color.G, color.B, 255), new RectangularPolygon(x, y, width, height)));
        image.Mutate(ctx => ctx.DrawImage(layer, new Point(0, 0), color.A / 255f));
    }

    private static void DrawTextShadow(
        IImageProcessingContext ctx,
        string text,
        Font font,
        PointF origin,
        HorizontalAlignment alignment,
        float offset)
    {
        ctx.DrawText(new RichTextOptions(font)
        {
            Origin = new PointF(origin.X + offset, origin.Y + offset),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, text, new Rgba32(0, 0, 0, 150));
    }

    private static Font FitFontToWidth(FontFamily family, string text, float startSize, float minSize, float maxWidth)
    {
        var size = startSize;
        while (size > minSize)
        {
            var font = family.CreateFont(size, FontStyle.Bold);
            if (TextMeasurer.MeasureSize(text, new RichTextOptions(font)).Width <= maxWidth)
                return font;
            size -= 1f;
        }

        return family.CreateFont(minSize, FontStyle.Bold);
    }

    private static float CenterTextY(string text, Font font, float centerY)
    {
        var measurement = TextMeasurer.MeasureSize(text, new RichTextOptions(font));
        return centerY - (measurement.Height / 2f) - 1f;
    }

    private static Rgba32 ToRgba(Color color, byte alpha)
    {
        var pixel = color.ToPixel<Rgba32>();
        return new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
    }

    private static float Lerp(float start, float end, float amount)
    {
        return start + ((end - start) * amount);
    }

    private string ComputeSignature(string leagueSlug, string leagueDisplayName, IReadOnlyList<string> top3Slugs)
    {
        var logoTicks = File.Exists(_logoPath) ? File.GetLastWriteTimeUtc(_logoPath).Ticks : 0L;
        var boldFontTicks = File.Exists(_boldFontPath) ? File.GetLastWriteTimeUtc(_boldFontPath).Ticks : 0L;
        var regularFontTicks = File.Exists(_regularFontPath) ? File.GetLastWriteTimeUtc(_regularFontPath).Ticks : 0L;
        var top3ProfileTicks = top3Slugs.Select(GetProfileTicks).ToArray();

        var raw = string.Join("|",
            "league-og-v35",
            leagueSlug,
            leagueDisplayName,
            string.Join(",", top3Slugs),
            string.Join(",", top3ProfileTicks.Select(t => t.ToString(CultureInfo.InvariantCulture))),
            logoTicks.ToString(CultureInfo.InvariantCulture),
            boldFontTicks.ToString(CultureInfo.InvariantCulture),
            regularFontTicks.ToString(CultureInfo.InvariantCulture));

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
