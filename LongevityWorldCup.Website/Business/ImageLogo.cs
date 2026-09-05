using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace LongevityWorldCup.Website.Business;

internal static class ImageLogo
{
    internal static async Task<Image<Rgba32>> LoadMarkAsync(string logoPath, CancellationToken ct = default)
    {
        await using var logoStream = File.OpenRead(logoPath);
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
}
