using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class AestheticResponsiveMediaBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task ResponsivePages_StayVisibleAndFitAtDeclaredViewportBoundaries()
    {
        var mediaInventory = GetResponsiveMediaInventory()
            .Concat(GetResponsiveScriptMediaInventory())
            .GroupBy(item => (item.Query, item.Source))
            .Select(group => group.First())
            .ToArray();
        Assert.NotEmpty(mediaInventory);
        var scriptThresholds = GetResponsiveScriptThresholdInventory();
        Assert.NotEmpty(scriptThresholds);

        var boundaryCases = mediaInventory
            .SelectMany(CreateResponsiveBoundaryCases)
            .ToArray();
        var scriptBoundaryCases = scriptThresholds
            .SelectMany(CreateResponsiveScriptBoundaryCases)
            .ToArray();
        Assert.NotEmpty(boundaryCases);

        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        await AddRouteStressStateAsync(context);
        var layoutProbes = boundaryCases
            .Select(item => new ResponsiveLayoutProbe(
                MapResponsiveSourceToRoute(item.Branch.Source),
                item.Width,
                item.Height,
                item.Branch.Source,
                item.Branch.Query))
            .Concat(scriptBoundaryCases.Select(item => new ResponsiveLayoutProbe(
                MapResponsiveSourceToRoute(item.Threshold.Source),
                item.Width,
                item.Height,
                item.Threshold.Source,
                $"inner{item.Threshold.Axis} {item.Threshold.Operator} {item.Threshold.Value}")))
            .GroupBy(item => (item.Route, item.Width, item.Height))
            .Select(group => group.First())
            .GroupBy(item => item.Route, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();

        var layoutWorkerCount = Math.Min(2, layoutProbes.Length);
        var layoutWorkerInputs = Enumerable.Range(0, layoutWorkerCount)
            .Select(workerIndex => layoutProbes
                .Where((_, routeIndex) => routeIndex % layoutWorkerCount == workerIndex)
                .Select(routeGroup => new
                {
                    Route = routeGroup.Key,
                    Probes = routeGroup
                        .OrderBy(item => item.Width)
                        .ThenBy(item => item.Height)
                        .Select(item => new { item.Width, item.Height })
                        .ToArray()
                })
                .ToArray())
            .ToArray();
        var layoutResultsByWorker = await Task.WhenAll(layoutWorkerInputs.Select(async workerInput =>
        {
            var routePage = await context.NewPageAsync();
            try
            {
                await routePage.GotoAsync(
                    new Uri(app.BaseAddress, "/__browser-test/external.js").ToString(),
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                return await routePage.EvaluateAsync<ResponsiveLayoutProbeResult[]>(
                    """
                    async ({ BaseUrl, RouteGroups }) => {
                        const frame = document.createElement('iframe');
                        frame.style.cssText = 'display:block;border:0;position:fixed;inset:0 auto auto 0';
                        document.body.append(frame);
                        const results = [];
                        for (const group of RouteGroups) {
                            const firstProbe = group.Probes[0];
                            frame.style.width = `${firstProbe.Width}px`;
                            frame.style.height = `${firstProbe.Height}px`;
                            await new Promise((resolve, reject) => {
                                frame.onload = resolve;
                                frame.onerror = () => reject(new Error(`Failed to load ${group.Route}`));
                                frame.src = new URL(group.Route, BaseUrl).href;
                            });

                            const frameWindow = frame.contentWindow;
                            const frameDocument = frame.contentDocument;
                            await (frameDocument.fonts?.ready || Promise.resolve());
                            for (const probe of group.Probes) {
                                frame.style.width = `${probe.Width}px`;
                                frame.style.height = `${probe.Height}px`;
                                frameWindow.dispatchEvent(new Event('resize'));

                                const root = frameDocument.documentElement;
                                const body = frameDocument.body;
                                const rootOverflow = root.scrollWidth - root.clientWidth;
                                const bodyOverflow = body.scrollWidth - root.clientWidth;
                                const hasVisibleContent = [...frameDocument.querySelectorAll('main, h1, [role="main"]')]
                                    .some(element => {
                                        const rect = element.getBoundingClientRect();
                                        const style = frameWindow.getComputedStyle(element);
                                        return rect.width > 0
                                            && rect.height > 0
                                            && style.display !== 'none'
                                            && style.visibility !== 'hidden';
                                    });
                                results.push({
                                    Route: group.Route,
                                    Width: probe.Width,
                                    Height: probe.Height,
                                    ClientWidth: root.clientWidth,
                                    ScrollWidth: Math.max(root.scrollWidth, body.scrollWidth),
                                    HorizontalOverflow: Math.max(0, rootOverflow, bodyOverflow),
                                    HasVisibleContent: hasVisibleContent
                                });
                            }
                        }

                        frame.remove();
                        return results;
                    }
                    """,
                    new { BaseUrl = app.BaseAddress.ToString(), RouteGroups = workerInput });
            }
            finally
            {
                await routePage.CloseAsync();
            }
        }));

        var layoutsByProbe = layoutResultsByWorker
            .SelectMany(results => results)
            .ToDictionary(result => (result.Route, result.Width, result.Height));
        foreach (var routeGroup in layoutProbes)
        {
            foreach (var probe in routeGroup)
            {
                var layout = layoutsByProbe[(probe.Route, probe.Width, probe.Height)];
                Assert.True(
                    layout.HasVisibleContent,
                    $"{probe.Route} rendered no visible content at {probe.Width}x{probe.Height} " +
                    $"for '{probe.Query}' from {probe.Source}.");
                Assert.True(
                    layout.HorizontalOverflow <= 1,
                    $"{probe.Route} overflowed horizontally by {layout.HorizontalOverflow}px at " +
                    $"{probe.Width}x{probe.Height} for '{probe.Query}' from {probe.Source}. " +
                    $"scrollWidth={layout.ScrollWidth}, clientWidth={layout.ClientWidth}.");
            }
        }
    }

    [Fact]
    public async Task ResponsiveContainerInventory_MatchesAtAndCrossesEveryDeclaredContainerBoundary()
    {
        var containerInventory = GetResponsiveContainerInventory();
        Assert.NotEmpty(containerInventory);
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1280, Height = 800 }
        });
        var page = await context.NewPageAsync();
        // Ordering probes finish at a phone viewport. Run them first so the
        // next component must restore its own baseline on every platform.
        var orderingFirst = containerInventory.OrderBy(condition =>
            condition.Source == "css/leaderboard-ordering.css" ? 0 : 1).ToArray();
        await AssertResponsiveContainerBoundariesAsync(page, orderingFirst);
    }

    private sealed class ResponsiveLayoutProbeResult
    {
        public string Route { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int ClientWidth { get; set; }
        public int ScrollWidth { get; set; }
        public int HorizontalOverflow { get; set; }
        public bool HasVisibleContent { get; set; }
    }

}
