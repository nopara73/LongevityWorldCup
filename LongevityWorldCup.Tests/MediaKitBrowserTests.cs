using System.Text;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class MediaKitBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
        // ZIP parsing belongs to JSZip. This adapter exercises the page's real
        // extraction, canvas, grouping and download code with varied image ratios.
        await context.RouteAsync("**/jszip.min.js", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript",
            Body = """
                window.JSZip = { async loadAsync() {
                    async function picture(width, height) {
                        const canvas = document.createElement('canvas');
                        canvas.width = width; canvas.height = height;
                        const ctx = canvas.getContext('2d');
                        ctx.fillStyle = '#126747'; ctx.fillRect(0, 0, width, height);
                        ctx.fillStyle = '#f0dfca'; ctx.fillRect(0, 0, width / 4, height);
                        return new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
                    }
                    const files = new Map([
                        ['MediaKit/FactSheet.pdf', new Blob(['original fact sheet bytes'])],
                        ['MediaKit/Logos/Logo.png', await picture(1800, 400)],
                        ['MediaKit/Team/Portrait_lwctext.png', await picture(1200, 3600)],
                        ['MediaKit/Athletes/Athlete.png', await picture(1600, 1200)]
                    ]);
                    return {
                        forEach(callback) { for (const path of files.keys()) callback(path, { dir: false }); },
                        file(path) { return { async async(type) { const blob = files.get(path); return type === 'arraybuffer' ? blob.arrayBuffer() : blob; } }; }
                    };
                }};
                """
        }));
    }
}
