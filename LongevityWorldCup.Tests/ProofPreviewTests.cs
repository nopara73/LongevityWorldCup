using System.Net;
using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "lwc-proof-preview-" + Guid.NewGuid().ToString("N"));
    private string Folder => Path.Combine(_root, "athletes", "preview_test");
    private string Source => Path.Combine(Folder, "proof_1.png");

    public ProofPreviewTests()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, "athlete.json"), """
            {"Name":"Preview Test","DateOfBirth":{"Year":1980,"Month":1,"Day":1},"Biomarkers":[],"Division":"Open","Flag":"Earth"}
            """);
    }

    [Fact]
    public async Task PreviewFitsWholePageOrientsItAndLeavesOriginalBytesUntouched()
    {
        using var image = new Image<Rgba32>(1200, 1600, Color.White);
        image[0, 0] = Color.Red;
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);
        image.Metadata.ExifProfile.SetValue(ExifTag.ImageDescription, "metadata is not a preview");
        image.SaveAsPng(Source);
        var original = await File.ReadAllBytesAsync(Source);
        using var service = CreateService();

        var preview = Assert.IsType<ProofPreview>(await service.GetAsync("preview_test", "proof_1.png", default));
        using var decoded = Image.Load(preview.Path);
        Assert.Equal(640, decoded.Width);
        Assert.Equal(480, decoded.Height);
        Assert.Null(decoded.Metadata.ExifProfile);
        Assert.Equal(original, await File.ReadAllBytesAsync(Source));
    }

    [Fact]
    public async Task ReusesCacheButSameFilenameAndTimestampWithNewBytesGetsNewPreview()
    {
        WriteImage(Color.Red);
        var time = File.GetLastWriteTimeUtc(Source);
        using var service = CreateService();
        var first = Assert.IsType<ProofPreview>(await service.GetAsync("preview_test", "proof_1.png", default));
        var written = File.GetLastWriteTimeUtc(first.Path);
        var again = Assert.IsType<ProofPreview>(await service.GetAsync("preview_test", "proof_1.png", default));
        Assert.Equal(first, again);
        Assert.Equal(written, File.GetLastWriteTimeUtc(first.Path));
        WriteImage(Color.Blue);
        File.SetLastWriteTimeUtc(Source, time);
        var changed = Assert.IsType<ProofPreview>(await service.GetAsync("preview_test", "proof_1.png", default));
        Assert.NotEqual(first.Revision, changed.Revision);
        Assert.NotEqual(first.Path, changed.Path);
        File.Delete(Source);
        Assert.Null(await service.GetAsync("preview_test", "proof_1.png", default));
    }

    [Fact]
    public async Task ConcurrentRequestsPublishOneCompletePreviewWithoutUpscalingSmallImages()
    {
        WriteImage(Color.White, 120, 160);
        using var service = CreateService();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.GetAsync("preview_test", "proof_1.png", default)));
        var paths = results.Select(p => Assert.IsType<ProofPreview>(p).Path).Distinct();
        using var image = Image.Load(Assert.Single(paths));
        Assert.Equal(120, image.Width);
        Assert.Equal(160, image.Height);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(results[0]!.Path)!, "*.tmp"));
    }

    [Theory]
    [InlineData("../preview_test", "proof_1.png")]
    [InlineData("preview_test", "../proof_1.png")]
    [InlineData("preview_test", "proof_1.png.br")]
    [InlineData("preview_test", "athlete.json")]
    [InlineData("preview_test", "proof_1.tiff")]
    [InlineData("missing", "proof_1.png")]
    public async Task OnlyPublicProofImagePathsAreEligible(string athlete, string filename)
    {
        WriteImage(Color.White);
        using var service = CreateService();
        Assert.Null(await service.GetAsync(athlete, filename, default));
    }

    [Fact]
    public async Task TiffBytesDisguisedAsPngDoNotReachTiffDecoder()
    {
        File.WriteAllBytes(Source, TiffTestFiles.Classic);
        using var service = CreateService();
        Assert.Null(await service.GetAsync("preview_test", "proof_1.png", default));
    }

    [Fact]
    public async Task CatalogPathsAndRepresentativeRealReportsStayWithinPreviewBudgets()
    {
        var catalog = Path.Combine(AestheticSystemBrowserTests.FindRepositoryRoot(),
            "LongevityWorldCup.Website", "wwwroot", "athletes");
        var files = Directory.EnumerateFiles(catalog, "proof_*.*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".br") && !p.EndsWith(".gz")).Select(p => new FileInfo(p)).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            Assert.True(ProofPreviewService.IsProofFile(file.Name), $"Unpreviewable proof: {file.FullName}");
            Assert.Matches("^[a-zA-Z0-9_-]+$", file.Directory!.Name);
            Assert.InRange(file.Length, 1, 32 * 1024 * 1024);
            using var stream = file.OpenRead();
            var info = ImageInput.Identify(stream);
            Assert.InRange((long)info.Width * info.Height, 1, 40_000_000);
        }

        // Every extension's largest file, plus the first page from the three
        // largest collections. Select from the current catalog, not one athlete.
        var sample = files.GroupBy(f => f.Extension.ToLowerInvariant()).Select(g => g.MaxBy(f => f.Length)!)
            .Concat(files.GroupBy(f => f.DirectoryName).OrderByDescending(g => g.Count()).Take(3)
                .Select(g => g.OrderBy(f => f.Name, StringComparer.Ordinal).First()))
            .DistinctBy(f => f.FullName);
        using var service = CreateService();
        foreach (var file in sample)
        {
            var athlete = file.Directory!.Name;
            var folder = Path.Combine(_root, "athletes", athlete);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "athlete.json"), "{}");
            var original = await File.ReadAllBytesAsync(file.FullName);
            await File.WriteAllBytesAsync(Path.Combine(folder, file.Name), original);
            var preview = Assert.IsType<ProofPreview>(await service.GetAsync(athlete, file.Name, default));
            var bytes = await File.ReadAllBytesAsync(preview.Path);
            Assert.True(bytes.Length <= 128 * 1024, $"{athlete}/{file.Name}: preview is {bytes.Length:N0} bytes (128 KiB budget)");
            using var decoded = Image.Load(bytes);
            Assert.InRange(decoded.Width, 1, 640);
            Assert.InRange(decoded.Height, 1, 640);
            if (original.Length > 128 * 1024)
                Assert.True(bytes.Length < original.Length / 2, $"{athlete}/{file.Name}: large original was not substantially reduced");
            Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(folder, file.Name)));
        }
    }

    [Fact]
    public async Task EndpointReturnsImageAndConditionalCacheResponseWhileDataExcludesSidecars()
    {
        WriteImage(Color.White);
        File.WriteAllText(Source + ".br", "not an image");
        File.WriteAllText(Source + ".gz", "not an image");
        using var factory = new TestWebApplicationFactory(builder => builder.UseWebRoot(_root));
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/proof-previews/preview_test/proof_1.png?v=original");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.True(response.Headers.CacheControl?.Public);
        using var image = Image.Load(await response.Content.ReadAsByteArrayAsync());
        Assert.InRange(image.Width, 1, 640);
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/proof-previews/preview_test/proof_1.png?v=original");
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(conditional)).StatusCode);
        var athletes = JsonNode.Parse(await client.GetStringAsync("/api/data/athletes"))!.AsArray();
        var proofs = Assert.Single(athletes)!["Proofs"]!.AsArray();
        Assert.Single(proofs);
        Assert.Contains("proof_1.png?v=", proofs[0]!.GetValue<string>());
        var missing = await client.GetAsync("/api/proof-previews/preview_test/proof_1.png.br");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.True(missing.Headers.CacheControl?.NoStore);
    }

    private void WriteImage(Color color, int width = 1200, int height = 1600)
    {
        using var image = new Image<Rgba32>(width, height, color);
        image.SaveAsPng(Source);
    }

    private ProofPreviewService CreateService() => new(new PreviewEnvironment { WebRootPath = _root }, NullLogger<ProofPreviewService>.Instance);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class PreviewEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "ProofPreviewTests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Testing";
    }
}
