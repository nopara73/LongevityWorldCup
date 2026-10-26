using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

internal static class BrowserReadiness
{
    // The .NET load-state waiter checks state before subscribing, so a load
    // delivered between those operations can be lost. Read the browser's
    // completed event timestamps instead, with the usual bounded test timeout.
    internal static async Task WaitForDocumentLoadedAsync(this IPage page)
    {
        await using var ready = await page.WaitForFunctionAsync(
            "() => performance.getEntriesByType('navigation')[0]?.loadEventEnd > 0");
    }

    internal static async Task WaitForDocumentContentLoadedAsync(this IPage page)
    {
        await using var ready = await page.WaitForFunctionAsync(
            "() => performance.getEntriesByType('navigation')[0]?.domContentLoadedEventEnd > 0");
    }
}
