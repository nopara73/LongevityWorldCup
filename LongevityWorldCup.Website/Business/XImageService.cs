using System.Globalization;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IOPath = System.IO.Path;

namespace LongevityWorldCup.Website.Business;

public class XImageService
{
    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 675;
    private const float HeaderX = 72f;

    private static readonly Color BackgroundTop = ParseHex("05080B");
    private static readonly Color BackgroundBottom = ParseHex("15181B");
    private static readonly Color TextColor = Color.White;
    private static readonly Color MutedTextColor = new(new Rgba32(214, 222, 232, 220));
    private static readonly Color FaintTextColor = new(new Rgba32(148, 163, 184, 255));
    private static readonly Color ShadowColor = new(new Rgba32(0, 0, 0, 185));
    private static readonly Color GreenAccent = ParseHex("78DA3B");
    private static readonly Color CyanAccent = ParseHex("00BCD4");
    private static readonly Color PinkAccent = ParseHex("FF4081");

    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<XImageService> _log;
    private readonly string _logoPath;
    private readonly string _boldFontPath;
    private readonly string _regularFontPath;
    private readonly FontCollection _fonts = new();
    private readonly object _fontLock = new();
    private FontFamily? _boldFamily;
    private FontFamily? _regularFamily;
    private bool _fontsLoaded;

    public XImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<XImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
        _logoPath = IOPath.Combine(_env.WebRootPath, "assets", "HdLogo.png");
        _boldFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Bold.ttf");
        _regularFontPath = IOPath.Combine(_env.WebRootPath, "assets", "fonts", "Poppins-Regular.ttf");
    }

    public async Task<Stream?> BuildNewcomersImageAsync(IReadOnlyList<string>? slugs = null)
    {
        var sourceSlugs = slugs?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? _athletes.GetRecentNewcomersForX().ToList();
        if (sourceSlugs.Count == 0)
            return null;

        var athletes = sourceSlugs
            .Select(TryGetAthleteInfo)
            .Where(a => a is { ProfilePath: not null })
            .Select(a => a!)
            .Take(4)
            .ToList();

        if (athletes.Count == 0)
            return null;

        using var image = CreateCanvas(GreenAccent);
        var fonts = GetFontFamilies();
        await DrawBrandAsync(image, fonts.Bold);
        DrawTitleBlock(
            image,
            fonts,
            "",
            "New athletes",
            "",
            GreenAccent);

        const int size = 208;
        const int gap = 34;
        var totalWidth = (athletes.Count * size) + ((athletes.Count - 1) * gap);
        var startX = (CanvasWidth - totalWidth) / 2;
        const int y = 274;

        for (var i = 0; i < athletes.Count; i++)
        {
            var athlete = athletes[i];
            var x = startX + (i * (size + gap));
            await DrawPortraitAsync(image, athlete.ProfilePath!, x, y, size, GreenAccent, 5.5f);
            DrawCenteredLabel(image, athlete.Name, fonts.Bold, x + (size / 2f), y + size + 20f, 240f, 30f);
        }

        return await SaveToStreamAsync(image);
    }

    public async Task<Stream?> BuildNewRankImageAsync(string winnerSlug, string prevSlug)
    {
        var winner = TryGetAthleteInfo(winnerSlug);
        var previous = TryGetAthleteInfo(prevSlug);
        if (winner?.ProfilePath is null || previous?.ProfilePath is null)
            return null;

        using var image = CreateCanvas(GreenAccent);
        var fonts = GetFontFamilies();
        await DrawBrandAsync(image, fonts.Bold);

        await DrawPortraitAsync(image, winner.ProfilePath, 112, 184, 404, GreenAccent, 8f);
        await DrawPortraitAsync(image, previous.ProfilePath, 824, 422, 132, FaintTextColor, 4f);

        image.Mutate(ctx =>
        {
            var title = winner.Rank > 0 ? $"New #{winner.Rank}" : "New rank";
            var titleFont = FitFontToWidth(fonts.Bold, title, 78f, 56f, 420f);
            var winnerFont = FitFontToWidth(fonts.Bold, winner.Name, 44f, 32f, 420f);
            var previousFont = FitFontToWidth(fonts.Bold, previous.Name, 30f, 22f, 180f);

            ctx.Fill(ToRgba(GreenAccent, 225), new RectangularPolygon(622f, 178f, 152f, 5f));
            DrawTextShadow(ctx, title, titleFont, new PointF(622f, 248f), HorizontalAlignment.Left, 4f);
            DrawWrappedText(ctx, title, titleFont, TextColor, new PointF(622f, 248f), 420f, 1, 80f);

            DrawTextShadow(ctx, winner.Name, winnerFont, new PointF(622f, 356f), HorizontalAlignment.Left, 3f);
            DrawWrappedText(ctx, winner.Name, winnerFont, TextColor, new PointF(622f, 356f), 420f, 1, 52f);

            DrawTextShadow(ctx, previous.Name, previousFont, new PointF(984f, 470f), HorizontalAlignment.Left, 2f);
            DrawWrappedText(ctx, previous.Name, previousFont, MutedTextColor, new PointF(984f, 470f), 180f, 1, 34f);
        });

        return await SaveToStreamAsync(image);
    }

    public async Task<Stream?> BuildSingleAthleteImageAsync(string slug)
    {
        var athlete = TryGetAthleteInfo(slug);
        if (athlete?.ProfilePath is null)
            return null;

        using var image = CreateCanvas(PinkAccent);
        var fonts = GetFontFamilies();
        await DrawBrandAsync(image, fonts.Bold);

        await DrawPortraitAsync(image, athlete.ProfilePath, 104, 202, 260, PinkAccent, 6f);

        image.Mutate(ctx =>
        {
            var titleFont = FitFontToWidth(fonts.Bold, athlete.Name, 66f, 42f, 620f);
            var valueFont = fonts.Bold.CreateFont(42f, FontStyle.Bold);
            var labelFont = fonts.Regular.CreateFont(28f, FontStyle.Regular);

            DrawTextShadow(ctx, athlete.Name, titleFont, new PointF(454f, 188f), HorizontalAlignment.Left, 3f);
            DrawWrappedText(ctx, athlete.Name, titleFont, TextColor, new PointF(454f, 188f), 620f, 1, 72f);
            DrawScoreboardMetricRow(ctx, BuildRankValue(athlete), "Current rank", valueFont, labelFont, PinkAccent, 454f, 318f, 574f, 82f);
            DrawScoreboardMetricRow(ctx, BuildReductionValue(athlete), "Age Reduction", valueFont, labelFont, GreenAccent, 454f, 422f, 574f, 82f);
        });

        return await SaveToStreamAsync(image);
    }

    public async Task<Stream?> BuildTop3LeaderboardPodiumImageAsync(IReadOnlyList<string> top3Slugs)
    {
        var athletes = (top3Slugs ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(TryGetAthleteInfo)
            .Where(a => a is { ProfilePath: not null })
            .Select(a => a!)
            .Take(3)
            .ToList();

        if (athletes.Count == 0)
            return null;

        using var image = CreateCanvas(CyanAccent);
        var fonts = GetFontFamilies();
        await DrawBrandAsync(image, fonts.Bold);

        image.Mutate(ctx =>
        {
            var titleFont = fonts.Bold.CreateFont(58f, FontStyle.Bold);
            ctx.Fill(ToRgba(CyanAccent, 225), new RectangularPolygon(HeaderX, 136f, 150f, 5f));
            DrawTextShadow(ctx, "Top 3", titleFont, new PointF(HeaderX, 164f), HorizontalAlignment.Left, 3f);
            DrawWrappedText(ctx, "Top 3", titleFont, TextColor, new PointF(HeaderX, 164f), 240f, 1, 64f);
        });

        var rows = new[]
        {
            new LeaderboardRow(1, 260, 214, 850, 88, 116),
            new LeaderboardRow(2, 260, 342, 730, 88, 94),
            new LeaderboardRow(3, 260, 452, 640, 88, 94)
        };

        for (var i = 0; i < athletes.Count && i < rows.Length; i++)
        {
            var row = rows[i];
            var athlete = athletes[i];
            var accent = i == 0 ? GreenAccent : CyanAccent;

            image.Mutate(ctx =>
            {
                ctx.Fill(new Rgba32(31, 42, 36, 220), new RectangularPolygon(row.X, row.Y, row.Width, row.Height));
                ctx.Draw(new Rgba32(255, 255, 255, 32), 1f, new RectangularPolygon(row.X, row.Y, row.Width, row.Height));
                ctx.Fill(ToRgba(accent, 235), new RectangularPolygon(row.X, row.Y, 10f, row.Height));
                ctx.Fill(new Rgba32(0, 0, 0, 74), new RectangularPolygon(row.X + 8f, row.Y + row.Height, row.Width - 22f, 4f));
            });

            await DrawPortraitAsync(
                image,
                athlete.ProfilePath!,
                row.X + 34,
                row.Y + ((row.Height - row.PortraitSize) / 2),
                row.PortraitSize,
                accent,
                i == 0 ? 5f : 4f);

            image.Mutate(ctx =>
            {
                var rankFont = fonts.Bold.CreateFont(i == 0 ? 34f : 30f, FontStyle.Bold);
                var nameFont = FitFontToWidth(fonts.Bold, athlete.Name, i == 0 ? 36f : 30f, 22f, row.Width - 350f);

                ctx.DrawText(new RichTextOptions(rankFont)
                {
                    Origin = new PointF(row.X + 168f, row.Y + 22f),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, $"#{row.Rank}", accent);

                DrawTextShadow(ctx, athlete.Name, nameFont, new PointF(row.X + 246f, row.Y + 22f), HorizontalAlignment.Left, 2f);
                DrawWrappedText(ctx, athlete.Name, nameFont, i == 0 ? TextColor : MutedTextColor, new PointF(row.X + 246f, row.Y + 22f), row.Width - 350f, 1, 36f);
            });
        }

        return await SaveToStreamAsync(image);
    }

    public async Task<Stream?> BuildAthleteCountMilestoneImageAsync(int athleteCount)
    {
        if (athleteCount <= 0)
            return null;

        using var image = new Image<Rgba32>(CanvasWidth, CanvasHeight, new Rgba32(5, 5, 15));

        var text = athleteCount.ToString(CultureInfo.InvariantCulture);
        var glyphs = BuildGlyphMap();
        var pixelSize = 22;
        const int glyphSpacing = 6;
        var maxWidth = CanvasWidth - 140;

        while (pixelSize > 8 && MeasurePixelTextWidth(text, pixelSize, glyphSpacing, glyphs) > maxWidth)
            pixelSize--;

        DrawPixelText(
            image,
            text,
            CanvasWidth / 2,
            CanvasHeight / 2,
            pixelSize,
            glyphSpacing,
            new Rgba32(245, 245, 245));

        return await SaveToStreamAsync(image);
    }

    private Image<Rgba32> CreateCanvas(Color accentColor)
    {
        var image = new Image<Rgba32>(CanvasWidth, CanvasHeight);
        DrawBackground(image, accentColor);
        return image;
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
                ctx.DrawImage(backgroundLogo, new Point(810, 54), 0.065f);
                ctx.DrawImage(smallLogo, new Point((int)HeaderX, 38), 0.96f);
            });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to draw social image logo.");
        }

        image.Mutate(ctx =>
        {
            ctx.DrawText(new RichTextOptions(brandFont)
            {
                Origin = new PointF(HeaderX + 68f, 41f),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, "LONGEVITY\nWORLD CUP", TextColor);
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

    private static void DrawTitleBlock(
        Image<Rgba32> image,
        (FontFamily Bold, FontFamily Regular) fonts,
        string kicker,
        string title,
        string subtitle,
        Color accent)
    {
        var kickerFont = fonts.Bold.CreateFont(24f, FontStyle.Bold);
        var titleFont = FitFontToWidth(fonts.Bold, title, 58f, 42f, 720f);
        var subtitleFont = fonts.Regular.CreateFont(28f, FontStyle.Regular);
        var hasKicker = !string.IsNullOrWhiteSpace(kicker);
        var hasSubtitle = !string.IsNullOrWhiteSpace(subtitle);
        var titleY = hasKicker ? 184f : 118f;
        var ruleY = hasKicker ? 165f : 96f;

        image.Mutate(ctx =>
        {
            if (hasKicker)
            {
                ctx.DrawText(new RichTextOptions(kickerFont)
                {
                    Origin = new PointF(HeaderX, 108f),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                }, kicker.ToUpperInvariant(), accent);
            }

            ctx.Fill(ToRgba(accent, 225), new RectangularPolygon(HeaderX, ruleY, 150f, 5f));
            DrawTextShadow(ctx, title, titleFont, new PointF(HeaderX, titleY), HorizontalAlignment.Left, 3f);
            DrawWrappedText(ctx, title, titleFont, TextColor, new PointF(HeaderX, titleY), 760f, 1, 64f);
            if (hasSubtitle)
                DrawWrappedText(ctx, subtitle, subtitleFont, MutedTextColor, new PointF(HeaderX, titleY + 62f), 760f, 1, 38f);
        });
    }

    private async Task DrawPortraitAsync(Image<Rgba32> image, string path, int x, int y, int size, Color accent, float strokeWidth)
    {
        try
        {
            using var profile = await Image.LoadAsync<Rgba32>(path);
            profile.Mutate(ctx => ctx.AutoOrient().Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            MakeCircular(profile);

            var centerX = x + (size / 2f);
            var centerY = y + (size / 2f);
            image.Mutate(ctx =>
            {
                ctx.Fill(new Rgba32(0, 0, 0, 150), new EllipsePolygon(centerX + 8f, centerY + 10f, (size / 2f) + 8f));
                ctx.Fill(new Rgba32(255, 255, 255, 16), new EllipsePolygon(centerX, centerY, (size / 2f) + 13f));
                ctx.Draw(ToRgba(accent, 235), strokeWidth, new EllipsePolygon(centerX, centerY, (size / 2f) + (strokeWidth / 2f)));
                ctx.DrawImage(profile, new Point(x, y), 1f);
                ctx.Draw(new Rgba32(255, 255, 255, 66), 1.5f, new EllipsePolygon(centerX, centerY, (size / 2f) - 1f));
            });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load social image profile image {Path}", path);
        }
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
        ctx.Fill(new Rgba32(18, 29, 28, 218), new RectangularPolygon(x, y, width, height));
        ctx.Draw(new Rgba32(255, 255, 255, 90), 1f, new RectangularPolygon(x, y, width, height));
        ctx.Fill(ToRgba(accent, 235), new RectangularPolygon(x, y, 10f, height));

        var valueFontToUse = value.Length > 4
            ? FitFontToWidth(valueFont.Family, value, valueFont.Size, 24f, 140f)
            : valueFont;

        DrawTextShadow(ctx, value, valueFontToUse, new PointF(x + 48f, y + 16f), HorizontalAlignment.Left, 2f);
        ctx.DrawText(new RichTextOptions(valueFontToUse)
        {
            Origin = new PointF(x + 48f, y + 16f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, value, accent);

        ctx.DrawText(new RichTextOptions(labelFont)
        {
            Origin = new PointF(x + 196f, y + 24f),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        }, label, MutedTextColor);
    }

    private static void DrawCenteredLabel(
        Image<Rgba32> image,
        string text,
        FontFamily boldFamily,
        float centerX,
        float topY,
        float maxWidth,
        float startSize,
        Color? color = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var font = FitFontToWidth(boldFamily, text, startSize, 18f, maxWidth);
        image.Mutate(ctx =>
        {
            DrawTextShadow(ctx, text, font, new PointF(centerX, topY + 4f), HorizontalAlignment.Center, 2f);
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(centerX, topY + 4f),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top
            }, text, color ?? TextColor);
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

    private (FontFamily Bold, FontFamily Regular) GetFontFamilies()
    {
        lock (_fontLock)
        {
            if (_fontsLoaded && _boldFamily is not null && _regularFamily is not null)
                return (_boldFamily.Value, _regularFamily.Value);

            var bold = _fonts.Add(_boldFontPath);
            var regular = _fonts.Add(_regularFontPath);
            _boldFamily = bold;
            _regularFamily = regular;
            _fontsLoaded = true;
            return (bold, regular);
        }
    }

    private AthleteRenderInfo? TryGetAthleteInfo(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var normalized = NormalizeSlug(slug);
        var snapshot = _athletes.GetAthletesSnapshot();
        var athlete = snapshot
            .OfType<JsonObject>()
            .FirstOrDefault(o => string.Equals(
                NormalizeSlug(o["AthleteSlug"]?.GetValue<string>()),
                normalized,
                StringComparison.Ordinal));

        var name = athlete is not null
            ? (athlete["DisplayName"]?.GetValue<string>() ?? athlete["Name"]?.GetValue<string>())
            : null;
        if (string.IsNullOrWhiteSpace(name))
            name = ToDisplayName(normalized);
        else
            name = name.Trim();

        var rank = 0;
        var position = 0;
        double? ageReduction = null;
        var rankings = _athletes.GetRankingsOrder();
        foreach (var row in rankings.OfType<JsonObject>())
        {
            var rowSlug = NormalizeSlug(row["AthleteSlug"]?.GetValue<string>());
            if (string.IsNullOrWhiteSpace(rowSlug))
                continue;

            position++;
            if (!string.Equals(rowSlug, normalized, StringComparison.Ordinal))
                continue;

            rank = position;
            ageReduction = GetDouble(row, "AgeDifference");
            break;
        }

        return TryGetProfilePath(normalized, out var profilePath)
            ? new AthleteRenderInfo(normalized, name, profilePath, rank, ageReduction)
            : new AthleteRenderInfo(normalized, name, null, rank, ageReduction);
    }

    private bool TryGetProfilePath(string slug, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(slug)) return false;

        var snapshot = _athletes.GetAthletesSnapshot();
        var bySlug = snapshot
            .OfType<JsonObject>()
            .Select(o => new
            {
                Slug = NormalizeSlug(o["AthleteSlug"]?.GetValue<string>()),
                ProfilePic = o["ProfilePic"]?.GetValue<string>()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug))
            .GroupBy(x => x.Slug, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().ProfilePic, StringComparer.Ordinal);

        var normalizedSlug = NormalizeSlug(slug);
        if (!bySlug.TryGetValue(normalizedSlug, out var url))
            url = null;

        if (!string.IsNullOrWhiteSpace(url))
        {
            var relativeUrl = url.Trim();
            var queryStart = relativeUrl.IndexOf('?');
            if (queryStart >= 0)
                relativeUrl = relativeUrl[..queryStart];

            var rel = relativeUrl.TrimStart('/').Replace('/', IOPath.DirectorySeparatorChar);
            fullPath = IOPath.Combine(_env.WebRootPath, rel);
            if (File.Exists(fullPath))
                return true;
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

    private static async Task<Stream> SaveToStreamAsync(Image<Rgba32> image)
    {
        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    private static string BuildRankValue(AthleteRenderInfo athlete)
    {
        return athlete.Rank > 0
            ? $"#{athlete.Rank}"
            : "Ranked";
    }

    private static string BuildReductionValue(AthleteRenderInfo athlete)
    {
        return athlete.AgeReduction.HasValue
            ? FormatReduction(athlete.AgeReduction.Value)
            : "-";
    }

    private static string NormalizeSlug(string? slug)
    {
        return (slug ?? "").Trim().Replace('-', '_').ToLowerInvariant();
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

    private static void DrawPixelText(
        Image<Rgba32> canvas,
        string text,
        int centerX,
        int centerY,
        int pixelSize,
        int glyphSpacing,
        Rgba32 color)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var glyphs = BuildGlyphMap();
        var widthInUnits = 0;
        foreach (var c in text)
        {
            if (!glyphs.TryGetValue(c, out var g))
                g = glyphs['?'];
            widthInUnits += g[0].Length + glyphSpacing;
        }

        if (widthInUnits > 0)
            widthInUnits -= glyphSpacing;

        var totalWidth = widthInUnits * pixelSize;
        var totalHeight = 7 * pixelSize;
        var x = centerX - totalWidth / 2;
        var y = centerY - totalHeight / 2;

        foreach (var c in text)
        {
            if (!glyphs.TryGetValue(c, out var glyph))
                glyph = glyphs['?'];

            for (var row = 0; row < glyph.Length; row++)
            {
                var line = glyph[row];
                for (var col = 0; col < line.Length; col++)
                {
                    if (line[col] != '1')
                        continue;

                    var px = x + col * pixelSize;
                    var py = y + row * pixelSize;
                    for (var dy = 0; dy < pixelSize; dy++)
                    {
                        var yy = py + dy;
                        if (yy < 0 || yy >= canvas.Height)
                            continue;
                        for (var dx = 0; dx < pixelSize; dx++)
                        {
                            var xx = px + dx;
                            if (xx < 0 || xx >= canvas.Width)
                                continue;
                            canvas[xx, yy] = color;
                        }
                    }
                }
            }

            x += (glyph[0].Length + glyphSpacing) * pixelSize;
        }
    }

    private static Dictionary<char, string[]> BuildGlyphMap()
    {
        return new Dictionary<char, string[]>
        {
            ['0'] = ["11111", "10001", "10001", "10001", "10001", "10001", "11111"],
            ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
            ['2'] = ["11111", "00001", "00001", "11111", "10000", "10000", "11111"],
            ['3'] = ["11111", "00001", "00001", "01111", "00001", "00001", "11111"],
            ['4'] = ["10001", "10001", "10001", "11111", "00001", "00001", "00001"],
            ['5'] = ["11111", "10000", "10000", "11111", "00001", "00001", "11111"],
            ['6'] = ["11111", "10000", "10000", "11111", "10001", "10001", "11111"],
            ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
            ['8'] = ["11111", "10001", "10001", "11111", "10001", "10001", "11111"],
            ['9'] = ["11111", "10001", "10001", "11111", "00001", "00001", "11111"],
            ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
            ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
            ['S'] = ["11111", "10000", "10000", "11111", "00001", "00001", "11111"],
            ['?'] = ["11111", "00001", "00010", "00100", "00100", "00000", "00100"]
        };
    }

    private static int MeasurePixelTextWidth(string text, int pixelSize, int glyphSpacing, IReadOnlyDictionary<char, string[]> glyphs)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var units = 0;
        foreach (var c in text)
        {
            if (!glyphs.TryGetValue(c, out var glyph))
                glyph = glyphs['?'];
            units += glyph[0].Length + glyphSpacing;
        }

        if (units > 0)
            units -= glyphSpacing;
        return units * pixelSize;
    }

    private static double? GetDouble(JsonObject obj, string key)
    {
        if (obj[key] is JsonValue value && value.TryGetValue<double>(out var d) && double.IsFinite(d))
            return d;

        return null;
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

    private static void DrawWrappedText(
        IImageProcessingContext ctx,
        string text,
        Font font,
        Color color,
        PointF origin,
        float maxWidth,
        int maxLines,
        float lineHeight)
    {
        var lines = WrapText(text, font, maxWidth, maxLines);
        for (var i = 0; i < lines.Count; i++)
        {
            ctx.DrawText(new RichTextOptions(font)
            {
                Origin = new PointF(origin.X, origin.Y + (lineHeight * i)),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            }, lines[i], color);
        }
    }

    private static IReadOnlyList<string> WrapText(string text, Font font, float maxWidth, int maxLines)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        var truncated = false;

        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            var candidate = string.IsNullOrWhiteSpace(current) ? word : $"{current} {word}";
            if (TextMeasurer.MeasureSize(candidate, new RichTextOptions(font)).Width <= maxWidth)
            {
                current = candidate;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(current))
                lines.Add(current);
            current = word;
            if (lines.Count >= maxLines)
            {
                truncated = index < words.Length - 1;
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(current) && lines.Count < maxLines)
            lines.Add(current);

        if (truncated && lines.Count == maxLines && words.Length > 0)
        {
            var last = lines[^1];
            while (last.Length > 0 && TextMeasurer.MeasureSize(last + "...", new RichTextOptions(font)).Width > maxWidth)
                last = last[..^1].TrimEnd();
            lines[^1] = last + "...";
        }

        return lines;
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
            Origin = new PointF(origin.X, origin.Y + offset),
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Top
        }, text, ShadowColor);
    }

    private static int Lerp(byte a, byte b, float t)
    {
        return (int)MathF.Round(a + ((b - a) * t));
    }

    private static Rgba32 ToRgba(Color color, byte alpha)
    {
        var pixel = color.ToPixel<Rgba32>();
        return new Rgba32(pixel.R, pixel.G, pixel.B, alpha);
    }

    private static Color ParseHex(string hex)
    {
        return Color.ParseHex("#" + hex);
    }

    private sealed record AthleteRenderInfo(string Slug, string Name, string? ProfilePath, int Rank, double? AgeReduction);
    private readonly record struct LeaderboardRow(int Rank, int X, int Y, int Width, int Height, int PortraitSize);
}
