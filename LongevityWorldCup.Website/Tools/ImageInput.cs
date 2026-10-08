using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Pbm;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Qoi;
using SixLabors.ImageSharp.Formats.Tga;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;

namespace LongevityWorldCup.Website.Tools;

/// <summary>Reads supported images without enabling ImageSharp 3's vulnerable TIFF paths.</summary>
public static class ImageInput
{
    // Keep the existing formats except TIFF. Detect the bytes, never trust an upload's extension.
    private static readonly DecoderOptions Options = new()
    {
        Configuration = new Configuration(
            new BmpConfigurationModule(), new GifConfigurationModule(), new JpegConfigurationModule(),
            new PbmConfigurationModule(), new PngConfigurationModule(), new QoiConfigurationModule(),
            new TgaConfigurationModule(), new WebpConfigurationModule())
    };

    // v3 retains ICC profiles as raw bytes. Do not access their lazy Entries parser.
    // Preserve camera orientation and color profiles for existing image processing.
    public static Image Load(Stream input) => Image.Load(Options, input);
    public static Image Load(byte[] input) => Image.Load(Options, input);
    public static Image Load(string path) => Image.Load(Options, path);

    public static async Task<Image> LoadAsync(Stream input, CancellationToken ct = default)
        => await Image.LoadAsync(Options, input, ct).ConfigureAwait(false);

    public static async Task<Image<TPixel>> LoadAsync<TPixel>(Stream input, CancellationToken ct = default)
        where TPixel : unmanaged, IPixel<TPixel>
        => await Image.LoadAsync<TPixel>(Options, input, ct).ConfigureAwait(false);

    public static async Task<Image<TPixel>> LoadAsync<TPixel>(string path, CancellationToken ct = default)
        where TPixel : unmanaged, IPixel<TPixel>
        => await Image.LoadAsync<TPixel>(Options, path, ct).ConfigureAwait(false);

    // Retain metadata presence here so the original-byte passthrough check can reject it.
    public static ImageInfo Identify(Stream input) => Image.Identify(Options, input);
}
