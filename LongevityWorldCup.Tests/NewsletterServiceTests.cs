using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class NewsletterServiceTests
{
    [Fact]
    public async Task SubscribeAsync_CreatesSubscriptionFileAndRejectsDuplicateCaseInsensitive()
    {
        using var root = TempContentRoot.Create();
        var environment = new TestWebHostEnvironment(root.Path);

        var firstResult = await NewsletterService.SubscribeAsync(
            "Reader@example.com",
            NullLogger.Instance,
            environment,
            CancellationToken.None);
        var duplicateResult = await NewsletterService.SubscribeAsync(
            "reader@example.com",
            NullLogger.Instance,
            environment,
            CancellationToken.None);

        Assert.Null(firstResult);
        Assert.Equal("This email is already subscribed.", duplicateResult);
        Assert.Equal(["Reader@example.com"], File.ReadAllLines(root.SubscriptionsPath));
    }

    [Fact]
    public async Task UnsubscribeAsync_RemovesMatchingEmailCaseInsensitiveAndTrimsRemainingLines()
    {
        using var root = TempContentRoot.Create();
        Directory.CreateDirectory(root.AppDataPath);
        File.WriteAllLines(root.SubscriptionsPath, [" Keep@example.com ", "", "Remove@example.com"]);
        var environment = new TestWebHostEnvironment(root.Path);

        var result = await NewsletterService.UnsubscribeAsync(
            "remove@example.com",
            NullLogger.Instance,
            environment,
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(["Keep@example.com"], File.ReadAllLines(root.SubscriptionsPath));
    }

    [Fact]
    public async Task UnsubscribeAsync_SucceedsWhenSubscriptionFileDoesNotExist()
    {
        using var root = TempContentRoot.Create();
        var environment = new TestWebHostEnvironment(root.Path);

        var result = await NewsletterService.UnsubscribeAsync(
            "missing@example.com",
            NullLogger.Instance,
            environment,
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(File.Exists(root.SubscriptionsPath));
    }

    private sealed class TempContentRoot : IDisposable
    {
        private TempContentRoot(string path)
        {
            Path = path;
            AppDataPath = System.IO.Path.Combine(path, "AppData");
            SubscriptionsPath = System.IO.Path.Combine(AppDataPath, "subscriptions.txt");
        }

        public string Path { get; }
        public string AppDataPath { get; }
        public string SubscriptionsPath { get; }

        public static TempContentRoot Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lwc-newsletter-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempContentRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TestWebHostEnvironment(string rootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "LongevityWorldCup.Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = rootPath;
        public string EnvironmentName { get; set; } = "Test";
        public string WebRootPath { get; set; } = rootPath;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
