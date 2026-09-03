using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class AestheticLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task RepresentativePages_DoNotOverflowAtNarrowShortAndLandscapeViewports()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var viewports = new[]
        {
            new ViewportSize { Width = 360, Height = 640 },
            new ViewportSize { Width = 844, Height = 390 },
            new ViewportSize { Width = 1280, Height = 350 }
        };

        await RunRouteWorkersAsync(context, RepresentativePaths, async (page, path) =>
        {
            await page.SetViewportSizeAsync(viewports[0].Width, viewports[0].Height);
            await NavigateAndSettleAsync(page, path);
            foreach (var viewport in viewports)
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                await page.EvaluateAsync(
                    "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                var layout = await MeasureLayoutAsync(page);

                Assert.True(layout.HasVisibleContent, $"{path} rendered no visible main content at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    layout.HorizontalOverflow <= 1,
                    $"{path} overflowed horizontally by {layout.HorizontalOverflow}px at {viewport.Width}x{viewport.Height}. " +
                    $"scrollWidth={layout.ScrollWidth}, clientWidth={layout.ClientWidth}.");
            }
        });
    }

    [Fact]
    public async Task FallbackErrorArtwork_DecodesAndRemainsVisibleAcrossResponsiveLayouts()
    {
        var app = App;
        var browser = Browser;
        var errorPaths = new[] { "/error/502.html", "/error/503.html", "/error/504.html" };
        var statusLabels = new[] { "502 Bad Gateway", "503 Service Unavailable", "504 Gateway Timeout" };
        var repositoryRoot = FindRepositoryRoot();
        var normalizedTemplates = errorPaths
            .Select((path, index) => File.ReadAllText(Path.Combine(
                    repositoryRoot,
                    "LongevityWorldCup.Website",
                    "wwwroot",
                    path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)))
                .Replace(statusLabels[index], "{{STATUS_LABEL}}", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Assert.Single(normalizedTemplates);

        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var viewports = new[]
        {
            new ViewportSize { Width = 1280, Height = 800 },
            new ViewportSize { Width = 390, Height = 844 },
            new ViewportSize { Width = 640, Height = 390 }
        };

        await RunRouteWorkersAsync(
            context,
            errorPaths,
            async (page, path) =>
        {
            // The three checked-in documents are byte-for-byte identical except
            // for their status label. Exercise every responsive shape on the
            // longest label, while still loading and decoding every route.
            var pathViewports = path.EndsWith("504.html", StringComparison.Ordinal)
                ? viewports
                : path.EndsWith("502.html", StringComparison.Ordinal)
                    ? [viewports[0]]
                    : [viewports[1]];
            await page.SetViewportSizeAsync(pathViewports[0].Width, pathViewports[0].Height);
            await NavigateAndSettleAsync(page, path);
            await page.WaitForFunctionAsync(
                "() => { const image = document.querySelector('.visual img'); return image?.complete && image.naturalWidth > 0; }");
            foreach (var viewport in pathViewports)
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                await page.EvaluateAsync(
                    "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

                var artwork = page.Locator(".visual img");
                var bounds = await artwork.BoundingBoxAsync();
                Assert.NotNull(bounds);
                Assert.True(
                    await artwork.EvaluateAsync<bool>(
                        "image => image.complete && image.naturalWidth > 0 && getComputedStyle(image).display !== 'none' && getComputedStyle(image).visibility !== 'hidden'"),
                    $"{path} artwork did not decode visibly at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    bounds.Width >= 90 && bounds.Height >= 90,
                    $"{path} artwork collapsed to {bounds.Width}x{bounds.Height}px at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    await page.EvaluateAsync<bool>(
                        "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) <= window.innerWidth + 1"),
                    $"{path} overflowed horizontally at {viewport.Width}x{viewport.Height}.");
            }
        });
    }

    private static async Task RunRouteWorkersAsync(
        IBrowserContext context,
        IReadOnlyList<string> paths,
        Func<IPage, string, Task> probeAsync)
    {
        var workerCount = Math.Min(2, paths.Count);
        await Task.WhenAll(Enumerable.Range(0, workerCount).Select(async workerIndex =>
        {
            var workerPage = await context.NewPageAsync();
            try
            {
                for (var index = workerIndex; index < paths.Count; index += workerCount)
                    await probeAsync(workerPage, paths[index]);
            }
            finally
            {
                await workerPage.CloseAsync();
            }
        }));
    }

}
