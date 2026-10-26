using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class BrowserReadinessTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task DocumentReadiness_WaitsForPendingResourcesAndAcceptsCompletedEvents()
    {
        await using var context = await Browser.NewContextAsync();
        var releaseImage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("https://readiness.test/", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<!DOCTYPE html><title>Readiness fixture</title><img src='/held.svg'>"
        }));
        await context.RouteAsync("https://readiness.test/held.svg", async route =>
        {
            await releaseImage.Task;
            await route.FulfillAsync(new()
            {
                ContentType = "image/svg+xml",
                Body = "<svg xmlns='http://www.w3.org/2000/svg' width='1' height='1'/>"
            });
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("https://readiness.test/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.WaitForDocumentContentLoadedAsync();
            Assert.False(await page.Locator("img").EvaluateAsync<bool>("image => image.complete"));
            var loaded = page.WaitForDocumentLoadedAsync();
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            Assert.False(loaded.IsCompleted, "DOM-ready must not satisfy the full-load wait while an image is pending.");
            releaseImage.TrySetResult();
            await loaded;
            Assert.True(await page.Locator("img").EvaluateAsync<bool>("image => image.complete && image.naturalWidth > 0"));
            await page.WaitForDocumentContentLoadedAsync();
            await page.WaitForDocumentLoadedAsync();
        }
        finally
        {
            releaseImage.TrySetResult();
        }
    }
}
