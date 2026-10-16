using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class MediaKitBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly Lazy<Dictionary<string, string>> KitFiles = new(CreateKitFiles);

    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task Kit_PreviewsWholeFilesWithBoundedThumbnailsAndDownloadsOriginalBytes(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await InstallKitFixtureAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fileList .media-file-row")).ToHaveCountAsync(4);
        await Assertions.Expect(page.Locator("#fileList .media-kit-group")).ToHaveCountAsync(4);
        foreach (var image in await page.Locator(".media-file-thumb img").AllAsync())
        {
            await image.ScrollIntoViewIfNeededAsync();
            await image.EvaluateAsync("async image => { await new Promise((resolve, reject) => { if (image.src) resolve(); else { image.onload = resolve; image.onerror = reject; } }); await image.decode(); }");
            var valid = await image.EvaluateAsync<bool>("""
                image => image.naturalWidth > 0 && image.naturalHeight > 0
                    && image.naturalWidth <= 520 && image.naturalHeight <= 520
                    && getComputedStyle(image).objectFit === 'contain'
                """);
            Assert.True(valid, "The preview must show the complete file without retaining a full-size decoded image.");
        }
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await page.EvaluateAsync("() => document.fonts.ready");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"));
        var download = await page.RunAndWaitForDownloadAsync(() =>
            page.GetByRole(AriaRole.Button, new() { Name = "Download FactSheet.pdf", Exact = true }).ClickAsync());
        Assert.Equal("FactSheet.pdf", download.SuggestedFilename);
        Assert.Equal(Encoding.UTF8.GetBytes("original fact sheet bytes"), await File.ReadAllBytesAsync((await download.PathAsync())!));
    }

    [Fact]
    public async Task FailedArchive_PreservesTheFullKitLinkAndExistingRecoveryMessage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await InstallKitFixtureAsync(context);
        await context.RouteAsync("**/MediaKit.zip", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/media", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#fallback")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#fallback")).ToHaveTextAsync("Failed to preview. Use the ZIP link above.");
        await Assertions.Expect(page.Locator("a[href$='MediaKit.zip']").First).ToBeVisibleAsync();
    }

    private static async Task InstallKitFixtureAsync(IBrowserContext context)
    {
        await context.RouteAsync("**/MediaKit.zip", route => route.FulfillAsync(new() { Body = "fixture archive" }));
        // Prepare source images outside the page's preview assertions. Encoding seven
        // megapixels in browser toBlob callbacks can exhaust the five-second row wait
        // under parallel CI load. The page still decodes and resizes the large originals.
        var archiveFiles = JsonSerializer.Serialize(KitFiles.Value);
        await context.RouteAsync("**/jszip.min.js", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript",
            Body = $$$"""
                const files = new Map(Object.entries({{{archiveFiles}}}).map(([path, base64]) => [
                    path, new Blob([Uint8Array.from(atob(base64), character => character.charCodeAt(0))])
                ]));
                window.JSZip = { async loadAsync() {
                    return {
                        forEach(callback) { for (const path of files.keys()) callback(path, { dir: false }); },
                        file(path) { return { async async(type) { const blob = files.get(path); return type === 'arraybuffer' ? blob.arrayBuffer() : blob; } }; }
                    };
                }};
                """
        }));
    }

    private static Dictionary<string, string> CreateKitFiles() => new()
    {
        ["MediaKit/FactSheet.pdf"] = Convert.ToBase64String(Encoding.UTF8.GetBytes("original fact sheet bytes")),
        ["MediaKit/Logos/Logo.png"] = CreatePicture(1800, 400),
        ["MediaKit/Team/Portrait_lwctext.png"] = CreatePicture(1200, 3600),
        ["MediaKit/Athletes/Athlete.png"] = CreatePicture(1600, 1200)
    };

    private static string CreatePicture(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(0x12, 0x67, 0x47));
        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < accessor.Height; y++)
                accessor.GetRowSpan(y)[..(width / 4)].Fill(new Rgba32(0xf0, 0xdf, 0xca));
        });
        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return Convert.ToBase64String(stream.ToArray());
    }
}
