using LongevityWorldCup.Website.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ImageInputTests
{
    [Theory]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    [InlineData("bmp")]
    public async Task SupportedImagesStillDecode(string format)
    {
        using var original = new Image<Rgba32>(2, 3, Color.Red);
        using var stream = new MemoryStream();
        switch (format)
        {
            case "jpeg": original.SaveAsJpeg(stream); break;
            case "png": original.SaveAsPng(stream); break;
            case "webp": original.SaveAsWebp(stream); break;
            case "bmp": original.SaveAsBmp(stream); break;
        }
        stream.Position = 0;

        using var decoded = await ImageInput.LoadAsync<Rgba32>(stream);

        Assert.Equal(2, decoded.Width);
        Assert.Equal(3, decoded.Height);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TiffBytesAreRejectedBeforeIdentificationOrDecoding(bool bigTiff)
    {
        var bytes = bigTiff ? TiffTestFiles.BoundedBigTiffHeader : TiffTestFiles.Classic;
        using var stream = new MemoryStream(bytes);

        Assert.Throws<UnknownImageFormatException>(() => ImageInput.Identify(stream));
        stream.Position = 0;
        Assert.Throws<UnknownImageFormatException>(() => ImageInput.Load(stream));
        Assert.Throws<UnknownImageFormatException>(() => ImageInput.Load(bytes));
        stream.Position = 0;
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => ImageInput.LoadAsync(stream));
        stream.Position = 0;
        await Assert.ThrowsAsync<UnknownImageFormatException>(() => ImageInput.LoadAsync<Rgba32>(stream));
    }
}

internal static class TiffTestFiles
{
    // A valid, one-pixel Group 4 TIFF. No vulnerable encoder is needed to create it.
    internal static byte[] Classic => Convert.FromBase64String(
        "SUkqAAgAAAAJAAABAwABAAAAAQAAAAEBAwABAAAAAQAAAAIBAwABAAAAAQAAAAMBAwABAAAABAAAAAYBAwABAAAAAAAAABEBBAABAAAAegAAABUBAwABAAAAAQAAABYBBAABAAAAAQAAABcBBAABAAAABAAAAAAAAACACACA");

    // Keep the directory count bounded so a decoder regression cannot hang the test process.
    internal static byte[] BoundedBigTiffHeader =>
    [
        0x49, 0x49, 0x2b, 0, 8, 0, 0, 0,
        16, 0, 0, 0, 0, 0, 0, 0,
        1, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0
    ];
}
