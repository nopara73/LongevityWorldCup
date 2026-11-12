using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class ImageOptimizationBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(4096, 1, 1024, 1)]
    [InlineData(1, 4096, 1, 1024)]
    [InlineData(2048, 1024, 1024, 512)]
    public async Task ImageOptimization_KeepsBothDimensionsPositive(
        int width, int height, int expectedWidth, int expectedHeight)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString()
        });
        await context.RouteAsync("**/image-optimization-test", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html",
            Body = "<html><body><script type='module' src='/js/misc.js'></script></body></html>"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/image-optimization-test");
        await page.WaitForFunctionAsync("() => typeof window.optimizeImageClient === 'function'");
        var actual = await page.EvaluateAsync<int[]>("""
            async ({width, height}) => {
                const canvas = document.createElement('canvas');
                canvas.width = width;
                canvas.height = height;
                const result = await window.optimizeImageClient(canvas.toDataURL(), {maxSize:1024});
                const bytes = Uint8Array.from(atob(result.dataUrl.split(',')[1]), c => c.charCodeAt(0));
                const bitmap = await createImageBitmap(new Blob([bytes], {type:result.contentType}));
                const dimensions = [bitmap.width, bitmap.height];
                bitmap.close();
                return dimensions;
            }
            """, new { width, height });

        Assert.Equal(new[] { expectedWidth, expectedHeight }, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnsupportedWebp_UsesCompactReadableJpegEvenWithoutImageBitmap(bool bitmapFails)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await context.RouteAsync("**/image-optimization-test", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<html><body><script type='module' src='/js/misc.js'></script></body></html>"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/image-optimization-test");
        await page.WaitForFunctionAsync("() => typeof window.optimizeImageClient === 'function'");
        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>("""
            async bitmapFails => {
                const source = document.createElement('canvas');
                source.width = 1400; source.height = 1800;
                const ctx = source.getContext('2d');
                const pixels = ctx.createImageData(1400, 1800);
                let seed = 73;
                for (let i = 0; i < pixels.data.length; i += 4) {
                    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
                    const shade = 160 + (seed >>> 24) % 96;
                    pixels.data.set([shade, shade, shade, 255], i);
                }
                ctx.putImageData(pixels, 0, 0);
                ctx.clearRect(0, 0, 80, 80);
                ctx.fillStyle = '#000'; ctx.font = '32px sans-serif';
                ctx.fillText('Albumin 4.5 g/dL — 5 Nov 2026', 90, 120);
                const original = source.toDataURL();
                const realToBlob = HTMLCanvasElement.prototype.toBlob;
                HTMLCanvasElement.prototype.toBlob = function(callback, type, quality) {
                    return realToBlob.call(this, callback, type === 'image/webp' ? 'image/png' : type, quality);
                };
                if (bitmapFails) window.createImageBitmap = async () => { throw new Error('Unsupported decoder'); };
                const result = await window.optimizeImageClient(original, {maxSize:2560, quality:0.88, targetMaxBytes:1.5*1024*1024});
                const image = new Image(); image.src = result.dataUrl; await image.decode();
                ctx.clearRect(0, 0, 1400, 1800); ctx.drawImage(image, 0, 0);
                const corner = [...ctx.getImageData(10, 10, 1, 1).data];
                return {type:result.contentType, bytes:atob(result.dataUrl.split(',')[1]).length,
                    ratio:result.dataUrl.length/original.length, width:image.naturalWidth, height:image.naturalHeight, corner};
            }
            """, bitmapFails);
        Assert.Equal("image/jpeg", result.GetProperty("type").GetString());
        Assert.InRange(result.GetProperty("bytes").GetInt32(), 1, 1572864);
        Assert.True(result.GetProperty("ratio").GetDouble() < 0.5);
        Assert.Equal(1400, result.GetProperty("width").GetInt32());
        Assert.Equal(1800, result.GetProperty("height").GetInt32());
        Assert.All(result.GetProperty("corner").EnumerateArray(), value => Assert.InRange(value.GetInt32(), 250, 255));
    }
}
