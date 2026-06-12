using LongevityWorldCup.Website.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;

namespace LongevityWorldCup.Tests;

public class ApplicationImageOptimizationTests
{
    [Fact]
    public void CorruptButSubmittedPngFallsBackToOriginalBytes()
    {
        const string corruptTinyPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9pQ9JxwAAAAASUVORK5CYII=";
        var bytes = Convert.FromBase64String(corruptTinyPng);
        var controller = new ApplicationController(new TestWebHostEnvironment(), NullLogger<HomeController>.Instance);
        var method = typeof(ApplicationController).GetMethod("OptimizeProfileImage", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var result = method!.Invoke(controller, [(bytes, "image/png", "png"), "test-submission"]);

        Assert.NotNull(result);
        Assert.True((bool)result!.GetType().GetProperty("Success")!.GetValue(result)!);
        Assert.Equal("png", result.GetType().GetProperty("Extension")!.GetValue(result));
        Assert.Equal(bytes, (byte[])result.GetType().GetProperty("Bytes")!.GetValue(result)!);
    }

    [Fact]
    public void OversizedProfileOptimizationFailureDoesNotFallBackToOriginalBytes()
    {
        var bytes = new byte[(4 * 1024 * 1024) + 1];
        var controller = new ApplicationController(new TestWebHostEnvironment(), NullLogger<HomeController>.Instance);
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
        var controller = new ApplicationController(new TestWebHostEnvironment(), NullLogger<HomeController>.Instance);
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
