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

    public async Task<Stream?> BuildNewcomersImageAsync()
    {
        var slugs = _athletes.GetRecentNewcomersForX();
        if (slugs.Count == 0)
            return null;

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

        var files = new List<string>();
        foreach (var slug in slugs)
        {
            if (!bySlug.TryGetValue(slug, out var url) || string.IsNullOrWhiteSpace(url))
                continue;

            var rel = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(_env.WebRootPath, rel);
            if (File.Exists(fullPath))
                files.Add(fullPath);
        }

        if (files.Count == 0)
            return null;

        var count = files.Count;
        const int canvasWidth = 1200;
        const int canvasHeight = 675;
        const int margin = 40;

        using var image = new Image<Rgba32>(canvasWidth, canvasHeight, new Rgba32(5, 5, 15));

        var availableWidth = canvasWidth - margin * 2;
        var size = Math.Min(canvasHeight - margin * 2, availableWidth / count);
        var totalWidth = size * count;
        var startX = margin + (availableWidth - totalWidth) / 2;
        var y = (canvasHeight - size) / 2;

        for (var i = 0; i < count; i++)
        {
            var path = files[i];
            try
            {
                using var profile = await Image.LoadAsync<Rgba32>(path);
                profile.Mutate(ctx => ctx.Resize(size, size));
                var pos = new Point(startX + i * size, y);
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
}

