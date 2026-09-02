using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

internal static class PlaywrightTestNavigationExtensions
{
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
