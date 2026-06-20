using System.Security.Cryptography;
using System.Text;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AssetVersionProviderTests
{
    [Fact]
    public void AppendVersion_AddsHashForExistingRootedAsset()
    {
        using var temp = new TempDirectory();
        var assetPath = WriteAsset(temp.Path, "wwwroot/css/site.css", "body { color: black; }");
        var provider = CreateProvider(webRootPath: Path.Combine(temp.Path, "wwwroot"));

        var versionedPath = provider.AppendVersion("/css/site.css");

        Assert.Equal($"/css/site.css?v={ExpectedVersion(File.ReadAllText(assetPath))}", versionedPath);
    }

    [Fact]
    public void AppendVersion_PreservesExistingQueryString()
    {
        using var temp = new TempDirectory();
        var assetPath = WriteAsset(temp.Path, "wwwroot/js/app.js", "console.log('ok');");
        var provider = CreateProvider(webRootPath: Path.Combine(temp.Path, "wwwroot"));

        var versionedPath = provider.AppendVersion("/js/app.js?module=true");

        Assert.Equal($"/js/app.js?module=true&v={ExpectedVersion(File.ReadAllText(assetPath))}", versionedPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("css/site.css")]
    [InlineData("/css/missing.css")]
    public void AppendVersion_ReturnsOriginalPath_WhenPathCannotBeVersioned(string assetPath)
    {
        using var temp = new TempDirectory();
        var provider = CreateProvider(webRootPath: Path.Combine(temp.Path, "wwwroot"));

        Assert.Equal(assetPath, provider.AppendVersion(assetPath));
    }

    [Fact]
    public void AppendVersion_UsesContentRootWwwroot_WhenWebRootPathIsUnavailable()
    {
        using var temp = new TempDirectory();
        var assetPath = WriteAsset(temp.Path, "wwwroot/assets/logo.png", "fake image bytes");
        var provider = CreateProvider(contentRootPath: temp.Path, webRootPath: Path.Combine(temp.Path, "missing-wwwroot"));

        var versionedPath = provider.AppendVersion("/assets/logo.png");

        Assert.Equal($"/assets/logo.png?v={ExpectedVersion(File.ReadAllText(assetPath))}", versionedPath);
    }

    [Fact]
    public void AppendVersion_DoesNotHashFilesOutsideWebRoot()
    {
        using var temp = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(temp.Path, "wwwroot"));
        File.WriteAllText(Path.Combine(temp.Path, "secret.txt"), "not a public asset");
        var provider = CreateProvider(webRootPath: Path.Combine(temp.Path, "wwwroot"));

        Assert.Equal("/../secret.txt", provider.AppendVersion("/../secret.txt"));
    }

    private static AssetVersionProvider CreateProvider(string webRootPath, string? contentRootPath = null)
    {
        return new AssetVersionProvider(new TestWebHostEnvironment
        {
            ContentRootPath = contentRootPath ?? Directory.GetParent(webRootPath)?.FullName ?? webRootPath,
            WebRootPath = webRootPath
        });
    }

    private static string WriteAsset(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private static string ExpectedVersion(string content)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hash[..8]).ToLowerInvariant();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public string EnvironmentName { get; set; } = Environments.Development;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = string.Empty;
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"lwc-assets-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
