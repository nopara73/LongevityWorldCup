using LongevityWorldCup.Website.Controllers;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using System.Net;
using System.Reflection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public class ApplicationImageOptimizationTests
{
    [Fact]
    public void BoundedProofPngPassesThroughWithoutReencoding()
    {
        using var image = new Image<Rgba32>(32, 24, new Rgba32(255, 255, 255));
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        var bytes = stream.ToArray();
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProofImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/png", "png"), "test-submission", 1]);

        Assert.NotNull(result);
        Assert.True((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
        Assert.Equal("png", result.GetType().GetProperty("Extension")!.GetValue(result));
        Assert.Equal(bytes, (byte[])result.GetType().GetProperty("Bytes")!.GetValue(result)!);
    }

    [Fact]
    public void ProofWithExifMetadataIsReencodedWithoutMetadata()
    {
        using var image = new Image<Rgba32>(32, 24, new Rgba32(255, 255, 255));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, "private proof metadata");
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        var bytes = stream.ToArray();
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProofImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/jpeg", "jpg"), "test-submission", 1]);

        Assert.NotNull(result);
        Assert.True((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
        Assert.Equal("webp", result.GetType().GetProperty("Extension")!.GetValue(result));
        var optimizedBytes = (byte[])result.GetType().GetProperty("Bytes")!.GetValue(result)!;
        Assert.False(bytes.SequenceEqual(optimizedBytes));
        using var optimizedImage = Image.Load(optimizedBytes);
        Assert.Null(optimizedImage.Metadata.ExifProfile);
    }

    [Fact]
    public void ExifOrientedProofIsAutoOrientedBeforeMetadataIsRemoved()
    {
        using var image = new Image<Rgba32>(40, 20, new Rgba32(255, 255, 255));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProofImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(
            controller,
            [(stream.ToArray(), "image/jpeg", "jpg"), "test-submission", 1]);

        Assert.NotNull(result);
        Assert.True((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
        var optimizedBytes = (byte[])result.GetType().GetProperty("Bytes")!.GetValue(result)!;
        using var optimizedImage = Image.Load(optimizedBytes);
        Assert.Equal(20, optimizedImage.Width);
        Assert.Equal(40, optimizedImage.Height);
        Assert.Null(optimizedImage.Metadata.ExifProfile);
    }

    [Fact]
    public void CorruptSubmittedProfileImageIsRejected()
    {
        const string corruptTinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9pQ9JxwAAAAASUVORK5CYII=";
        var bytes = Convert.FromBase64String(corruptTinyPng);
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProfileImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/png", "png"), "test-submission"]);

        Assert.NotNull(result);
        Assert.False((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
    }

    [Fact]
    public void OversizedProfileOptimizationFailureDoesNotFallBackToOriginalBytes()
    {
        var bytes = new byte[(4 * 1024 * 1024) + 1];
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProfileImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/png", "png"), "test-submission"]);

        Assert.NotNull(result);
        Assert.False((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
    }

    [Fact]
    public void OversizedProofOptimizationFailureDoesNotFallBackToOriginalBytes()
    {
        var bytes = new byte[(2 * 1024 * 1024) + 1];
        var controller = CreateController();
        var method = typeof(ApplicationController).GetMethod("OptimizeProofImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/png", "png"), "test-submission", 1]);

        Assert.NotNull(result);
        Assert.False((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" summer-pass ", "summer-pass")]
    public void NormalizeFreePassValueRequiresNonBlankToken(string? input, string? expected)
    {
        var method = typeof(ApplicationController).GetMethod("NormalizeFreePassValue", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [input]);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("mightyklaus", "mightyklaus")]
    [InlineData(" MightyKlaus ", "mightyklaus")]
    [InlineData("foo", null)]
    public void NormalizeDiscountValueAllowsOnlyReusableMightyKlausCode(string? input, string? expected)
    {
        var method = typeof(ApplicationController).GetMethod("NormalizeDiscountValue", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(null, [input]);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ApplicationBtcpayFailureMessageDoesNotExposeProviderResponseBody()
    {
        var method = typeof(ApplicationController).GetMethod("BuildBtcpayFailureMessage", BindingFlags.Static | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = (string)method!.Invoke(null, [HttpStatusCode.BadRequest])!;

        Assert.Equal("BTCPay API returned HTTP 400.", result);
        Assert.DoesNotContain("secret", result, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", result, StringComparison.Ordinal);
    }

    private static ApplicationController CreateController()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new ApplicationController(
            new TestWebHostEnvironment(),
            NullLogger<ApplicationController>.Instance,
            new ApplicationSubmissionRetryStore(cache));
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
