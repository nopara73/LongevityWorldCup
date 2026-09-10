using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

internal static class PlaywrightTestNavigationExtensions
{
    /// <summary>
    /// Waits for this wheel input to reach the document before a delayed response
    /// is released. An existing scroll position does not prove input was handled.
    /// Callers that assert scroll geometry must also wait for that geometry.
    /// </summary>
    public static async Task WheelAndWaitForInputAsync(this IPage page, float deltaX, float deltaY)
    {
        await using var input = await page.EvaluateHandleAsync("""
            () => {
                const controller = new AbortController();
                const input = { received: false, dispose: () => controller.abort() };
                document.addEventListener('wheel', () => { input.received = true; },
                    { once: true, passive: true, signal: controller.signal });
                return input;
            }
            """);
        try
        {
            await page.Mouse.WheelAsync(deltaX, deltaY);
            await page.WaitForFunctionAsync("input => input.received", input);
        }
        finally
        {
            await input.EvaluateAsync("input => input.dispose()");
        }
    }

    public static async Task SetViewportSizeAndWaitForLayoutAsync(
        this IPage page,
        int width,
        int height)
    {
        await page.SetViewportSizeAsync(width, height);
        await page.WaitForFunctionAsync(
            "size => window.innerWidth === size.width && window.innerHeight === size.height",
            new { width, height });
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => { void document.documentElement.offsetWidth; resolve(); }))");
    }

    /// <summary>
    /// Waits for the destination document needed by interaction assertions without
    /// coupling route tests to every image, font, or analytics resource's load state.
    /// </summary>
    public static async Task WaitForDomContentLoadedUrlAsync(this IPage page, string url)
    {
        // WaitForURLAsync still delegates an already-matching URL to a lifecycle
        // waiter. If Commit already fired, that can wait forever for a past event.
        // Poll the observable URL instead; WaitForFunction survives a navigation
        // and also handles the fast-navigation/already-loaded case.
        if (!url.StartsWith("**/", StringComparison.Ordinal))
            throw new ArgumentException("The navigation helper accepts an absolute-path suffix such as **/dashboard.", nameof(url));

        await page.WaitForFunctionAsync(
            "suffix => location.href.endsWith(suffix)",
            url[2..]);
        await page.WaitForFunctionAsync("() => document.readyState !== 'loading'");
    }
}
