using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public class XImageService
{
    private readonly IWebHostEnvironment _env;
    private readonly AthleteDataService _athletes;
    private readonly ILogger<XImageService> _log;

    public XImageService(IWebHostEnvironment env, AthleteDataService athletes, ILogger<XImageService> log)
    {
        _env = env;
        _athletes = athletes;
        _log = log;
    }

    public async Task<Stream?> BuildNewcomersImageAsync(IReadOnlyList<string>? slugs = null)
    {
        var sourceSlugs = slugs?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList() ?? _athletes.GetRecentNewcomersForX().ToList();
        if (sourceSlugs.Count == 0)
            return null;

        var files = new List<string>();
        foreach (var slug in sourceSlugs)
        {
            if (TryGetProfilePath(slug, out var fullPath))
                files.Add(fullPath);
        }

        if (files.Count == 0)
            return null;

        var count = files.Count;
        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        const int margin = 40;
        const int gap = 20;

        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));

        var availableWidth = canvasWidth - margin * 2;
        var totalGap = gap * Math.Max(0, count - 1);
        var size = Math.Min(canvasHeight - margin * 2, (availableWidth - totalGap) / Math.Max(1, count));
        var totalWidth = size * count + totalGap;
        var startX = margin + (availableWidth - totalWidth) / 2;
        var y = (canvasHeight - size) / 2;

        for (var i = 0; i < count; i++)
        {
            var path = files[i];
            try
            {
                using var profile = await Image.LoadAsync<Rgba32>(path);
                profile.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(size, size),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));
                MakeCircular(profile);
                var pos = new Point(startX + i * (size + gap), y);
                image.Mutate(ctx => ctx.DrawImage(profile, pos, 1f));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load newcomer profile image {Path}", path);
            }
        }

        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    private static void MakeCircular(Image<Rgba32> image)
    {
        var w = image.Width;
        var h = image.Height;
        var cx = (w - 1) / 2.0;
        var cy = (h - 1) / 2.0;
        var radius = Math.Min(w, h) / 2.0;
        var radiusSq = radius * radius;

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                if (dx * dx + dy * dy <= radiusSq)
                    continue;

                var p = image[x, y];
                image[x, y] = new Rgba32(p.R, p.G, p.B, 0);
            }
        }
    }

    public async Task<Stream?> BuildNewRankImageAsync(string winnerSlug, string prevSlug)
    {
        if (!TryGetProfilePath(winnerSlug, out var winnerPath)) return null;
        if (!TryGetProfilePath(prevSlug, out var prevPath)) return null;

        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        const int margin = 40;

        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));

        var availableWidth = canvasWidth - margin * 2;
        var size = Math.Min(canvasHeight - margin * 2, availableWidth / 2);
        var totalWidth = size * 2;
        var startX = margin + (availableWidth - totalWidth) / 2;
        var y = (canvasHeight - size) / 2;

        try
        {
            using var winner = await Image.LoadAsync<Rgba32>(winnerPath);
            winner.Mutate(ctx => ctx.Resize(size, size));
            image.Mutate(ctx => ctx.DrawImage(winner, new Point(startX, y), 1f));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load winner profile image {Path}", winnerPath);
            return null;
        }

        try
        {
            using var prev = await Image.LoadAsync<Rgba32>(prevPath);
            prev.Mutate(ctx => ctx.Resize(size, size));
            image.Mutate(ctx => ctx.DrawImage(prev, new Point(startX + size, y), 1f));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load previous profile image {Path}", prevPath);
            return null;
        }

        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    public async Task<Stream?> BuildSingleAthleteImageAsync(string slug)
    {
        if (!TryGetProfilePath(slug, out var profilePath))
            return null;

        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        const int margin = 60;

        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));
        var size = Math.Min(canvasWidth - margin * 2, canvasHeight - margin * 2);
        var x = (canvasWidth - size) / 2;
        var y = (canvasHeight - size) / 2;

        try
        {
            using var profile = await Image.LoadAsync<Rgba32>(profilePath);
            profile.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(size, size),
                Mode = ResizeMode.Crop,
                Position = AnchorPositionMode.Center
            }));
            image.Mutate(ctx => ctx.DrawImage(profile, new Point(x, y), 1f));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to load athlete profile image {Path}", profilePath);
            return null;
        }

        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    public async Task<Stream?> BuildTop3LeaderboardPodiumImageAsync(IReadOnlyList<string> top3Slugs)
    {
        var slugs = (top3Slugs ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Take(3)
            .ToList();
        if (slugs.Count == 0)
            return null;

        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));

        // Podium layout: #1 center and higher, #2 left lower, #3 right lower.
        var slots = new[]
        {
            new { X = canvasWidth / 2, Y = 180, Size = 320 }, // rank 1
            new { X = canvasWidth / 2 - 280, Y = 270, Size = 250 }, // rank 2
            new { X = canvasWidth / 2 + 280, Y = 270, Size = 250 }  // rank 3
        };

        for (var i = 0; i < slugs.Count && i < 3; i++)
        {
            var slot = slots[i];
            if (!TryGetProfilePath(slugs[i], out var path))
                continue;

            try
            {
                using var profile = await Image.LoadAsync<Rgba32>(path);
                profile.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(slot.Size, slot.Size),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));

                var x = slot.X - slot.Size / 2;
                var y = slot.Y - slot.Size / 2;
                image.Mutate(ctx => ctx.DrawImage(profile, new Point(x, y), 1f));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to load Top3 profile image {Path}", path);
            }
        }

        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
    }

    public async Task<Stream?> BuildAthleteCountMilestoneImageAsync(int athleteCount)
    {
        if (athleteCount <= 0)
            return null;

        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));

        var text = athleteCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var glyphs = BuildGlyphMap();
        var pixelSize = 22;
        const int glyphSpacing = 6;
        var maxWidth = canvasWidth - 140;

        while (pixelSize > 8 && MeasurePixelTextWidth(text, pixelSize, glyphSpacing, glyphs) > maxWidth)
            pixelSize--;

        DrawPixelText(
            image,
            text,
            canvasWidth / 2,
            canvasHeight / 2,
            pixelSize: pixelSize,
            glyphSpacing: glyphSpacing,
            color: new Rgba32(245, 245, 245));

        var output = new MemoryStream();
        await image.SaveAsPngAsync(output);
        output.Position = 0;
        return output;
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
                        if (yy < 0 || yy >= canvas.Height) continue;
                        for (var dx = 0; dx < pixelSize; dx++)
                        {
                            var xx = px + dx;
                            if (xx < 0 || xx >= canvas.Width) continue;
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

    private bool TryGetProfilePath(string slug, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(slug)) return false;

        var snapshot = _athletes.GetAthletesSnapshot();
        var bySlug = snapshot
            .OfType<JsonObject>()
            .Select(o => new
            {
                Slug = o["AthleteSlug"]?.GetValue<string>(),
                ProfilePic = o["ProfilePic"]?.GetValue<string>()
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Slug))
            .ToDictionary(x => x.Slug!, x => x.ProfilePic, StringComparer.OrdinalIgnoreCase);

        var normalizedSlug = slug.Replace('-', '_');
        if (!bySlug.TryGetValue(slug, out var url) && !bySlug.TryGetValue(normalizedSlug, out url))
            url = null;

        if (!string.IsNullOrWhiteSpace(url))
        {
            var rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            fullPath = Path.Combine(_env.WebRootPath, rel);
            if (File.Exists(fullPath))
                return true;
        }

        var athleteDir = Path.Combine(_env.WebRootPath, "athletes", normalizedSlug);
        if (!Directory.Exists(athleteDir))
            return false;

        var direct = Path.Combine(athleteDir, normalizedSlug + ".webp");
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
}

