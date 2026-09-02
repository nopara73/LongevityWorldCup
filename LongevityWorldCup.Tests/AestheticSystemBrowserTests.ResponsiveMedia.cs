using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class AestheticSystemBrowserTests
{
    [Fact]
    public async Task ResponsiveMediaInventory_MatchesAtAndCrossesEveryDeclaredViewportBoundary()
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
        var page = await context.NewPageAsync();
        await page.SetContentAsync("<!doctype html><html><body><main>Responsive boundary probe</main></body></html>");

        var results = new Dictionary<ResponsiveBoundaryCase, bool>();
        var viewportGroups = boundaryCases
            .GroupBy(item => (item.Width, item.Height))
            .ToArray();
        var matchMediaProbeGroups = viewportGroups
            .Select(viewportGroup => new
            {
                viewportGroup.Key.Width,
                viewportGroup.Key.Height,
                Queries = viewportGroup
                    .Select(item => item.Branch.Query)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            })
            .ToArray();
        var matchMediaProbeResults = await page.EvaluateAsync<ResponsiveViewportProbeResult[]>(
            """
            groups => groups.map(group => {
                const frame = document.createElement('iframe');
                frame.style.cssText = `display:block;border:0;width:${group.Width}px;height:${group.Height}px`;
                document.body.append(frame);
                const result = {
                    Width: frame.contentWindow.innerWidth,
                    Height: frame.contentWindow.innerHeight,
                    Matches: group.Queries.map(query => frame.contentWindow.matchMedia(query).matches)
                };
                frame.remove();
                return result;
            })
            """,
            matchMediaProbeGroups);
        for (var groupIndex = 0; groupIndex < viewportGroups.Length; groupIndex++)
        {
            var viewportGroup = viewportGroups[groupIndex];
            var probeGroup = matchMediaProbeGroups[groupIndex];
            var probeResult = matchMediaProbeResults[groupIndex];
            Assert.Equal(probeGroup.Width, probeResult.Width);
            Assert.Equal(probeGroup.Height, probeResult.Height);
            var matchesByQuery = probeGroup.Queries
                .Select((query, matchIndex) => (query, probeResult.Matches[matchIndex]))
                .ToDictionary(item => item.query, item => item.Item2, StringComparer.Ordinal);

            foreach (var boundaryCase in viewportGroup)
                results[boundaryCase] = matchesByQuery[boundaryCase.Branch.Query];
        }

        foreach (var boundaryCase in boundaryCases)
        {
            Assert.True(
                results[boundaryCase] == boundaryCase.ExpectedMatch,
                $"Responsive condition '{boundaryCase.Branch.Query}' from {boundaryCase.Branch.Source} " +
                $"was expected to {(boundaryCase.ExpectedMatch ? "match" : "stop matching")} at " +
                $"{boundaryCase.Width}x{boundaryCase.Height} while checking " +
                $"{boundaryCase.Feature.Bound}-{boundaryCase.Feature.Axis}: {boundaryCase.Feature.Value}px.");
        }

        var testedBranches = boundaryCases
            .Select(item => item.Branch.Query)
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(mediaInventory, branch => Assert.Contains(branch.Query, testedBranches));

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


        var containerInventory = GetResponsiveContainerInventory();
        await AssertResponsiveContainerBoundariesAsync(page, containerInventory);
    }

    private sealed class ResponsiveViewportProbeResult
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public bool[] Matches { get; set; } = [];
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
