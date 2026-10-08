using LongevityWorldCup.Website.Tools;
using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace LongevityWorldCup.Website.Business;

public sealed class InstagramImageService(IWebHostEnvironment environment, CustomEventImageService cards)
{
    public async Task<string> RenderAsync(string rawText, Func<string, string>? resolveName, string? memePath, CancellationToken ct = default)
    {
        using var stream = memePath is not null
            ? new MemoryStream(await File.ReadAllBytesAsync(memePath, ct))
            : await cards.RenderToStreamAsync(rawText, resolveName, ct)
                ?? throw new InvalidOperationException("Instagram announcement image rendering is unavailable.");
        using var image = await ImageInput.LoadAsync(stream, ct);
        var ratio = (double)image.Width / image.Height;
        if (image.Width is < 320 or > 1440 || ratio is < 0.8 or > 1.91)
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(1200, 1200), Mode = ResizeMode.Pad, PadColor = Color.ParseHex("151b23")
            }));
        using var jpeg = new MemoryStream();
        await image.SaveAsJpegAsync(jpeg, new JpegEncoder { Quality = 90 }, ct);
        var bytes = jpeg.ToArray();
        if (bytes.Length > 8_000_000) throw new InvalidOperationException("Instagram announcement image exceeds the supported size.");
        // A content-addressed URL keeps every prepared post's image immutable.
        var filename = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() + ".jpg";
        var directory = Path.Combine(environment.WebRootPath, "generated", "instagram");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, filename);
        if (!File.Exists(path))
        {
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(temp, bytes, ct);
                File.Move(temp, path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        return "https://longevityworldcup.com/generated/instagram/" + filename;
    }
}
