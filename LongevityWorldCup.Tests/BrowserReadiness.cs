using Microsoft.Playwright;

namespace LongevityWorldCup.Tests;

internal static class BrowserReadiness
{
    // Capture each document's events before its scripts run. Persistent browser
    // state avoids the .NET waiter's check-before-subscribe race and works with
    // Playwright's clock, which replaces navigation timing with empty entries.
    internal static Task TrackDocumentReadinessAsync(this IBrowserContext context) =>
        context.AddInitScriptAsync("""
            (() => {
                const key = Symbol.for('lwc.test.documentReadiness');
                if (window[key]) return;
                const state = { domContentLoaded: false, loaded: false };
                Object.defineProperty(window, key, { value: state });
                document.addEventListener('DOMContentLoaded', () => { state.domContentLoaded = true; }, { once: true });
                window.addEventListener('load', () => { state.loaded = true; }, { once: true });
            })();
            """);

    internal static async Task WaitForDocumentLoadedAsync(this IPage page)
    {
        await using var ready = await page.WaitForFunctionAsync(
            "() => window[Symbol.for('lwc.test.documentReadiness')]?.loaded === true");
    }

    internal static async Task WaitForDocumentContentLoadedAsync(this IPage page)
    {
        await using var ready = await page.WaitForFunctionAsync(
            "() => window[Symbol.for('lwc.test.documentReadiness')]?.domContentLoaded === true");
    }
}
