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
}
