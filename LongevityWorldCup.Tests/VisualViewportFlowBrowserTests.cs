using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class VisualViewportFlowBrowserTests
{
    [Fact]
    public async Task FlowDock_UsesVisualViewportOffsetAndHeightForDockingAndClearance()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(VisualViewportTestBootstrap(200, 400));

        var page = await context.NewPageAsync();
        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");

        var candidateDocked = await page.EvaluateAsync<bool>(
            """
            () => {
                document.querySelectorAll('[data-flow-dock]').forEach(element => {
                    element.setAttribute('data-flow-dock', 'off');
                });
                window.LwcFlowActionDock.refreshNow();

                const candidate = document.createElement('div');
                candidate.id = 'visualViewportDockCandidate';
                candidate.className = 'flow-action-stack';
                candidate.setAttribute('data-flow-dock', 'overflow');
                candidate.style.cssText = 'position:absolute;top:170px;left:20px;width:200px;height:60px;';
                document.body.appendChild(candidate);
                window.LwcFlowActionDock.refreshNow();

                return candidate.classList.contains('flow-action-stack--docked');
            }
            """);

        Assert.True(candidateDocked,
            "An action above a visual viewport whose layout-coordinate top is 200px should dock.");

        var clearance = await page.EvaluateAsync<VisualViewportClearanceResult>(
            """
            () => {
                window.__testVisualViewport.offsetTop = 0;
                window.__testVisualViewport.height = 420;

                const dock = document.createElement('div');
                dock.id = 'visualViewportClearanceDock';
                dock.className = 'flow-action-stack';
                dock.setAttribute('data-flow-dock', 'always');
                dock.style.cssText = 'height:100px!important;min-height:100px!important;box-sizing:border-box;';
                document.body.appendChild(dock);
                window.LwcFlowActionDock.refreshNow();

                const editedControl = document.createElement('div');
                editedControl.id = 'visualViewportEditedControl';
                editedControl.getBoundingClientRect = () => ({
                    x: 0,
                    y: 440,
                    top: 440,
                    right: 100,
                    bottom: 500,
                    left: 0,
                    width: 100,
                    height: 60,
                    toJSON() { return this; }
                });
                document.body.appendChild(editedControl);

                window.__testScrollByCalls.length = 0;
                window.LwcFlowActionDock.ensureClear(editedControl, { margin: 16, behavior: 'auto' });

                const dockHeight = parseFloat(getComputedStyle(document.documentElement)
                    .getPropertyValue('--flow-action-dock-height')) || 0;
                const scrollCount = window.__testScrollByCalls.length;
                const scrollTop = window.__testScrollByCalls[0]?.top ?? 0;

                dock.setAttribute('data-flow-dock', 'off');
                document.getElementById('visualViewportDockCandidate')
                    ?.setAttribute('data-flow-dock', 'off');
                window.__testVisualViewport.offsetTop = Number.NaN;
                window.__testVisualViewport.height = Number.NaN;

                const fallbackCandidate = document.createElement('div');
                fallbackCandidate.className = 'flow-action-stack';
                fallbackCandidate.setAttribute('data-flow-dock', 'overflow');
                fallbackCandidate.style.cssText = 'position:absolute;top:780px;left:20px;width:200px;height:60px;';
                document.body.appendChild(fallbackCandidate);
                window.LwcFlowActionDock.refreshNow();

                return {
                    DockHeight: dockHeight,
                    FallbackCandidateDocked: fallbackCandidate.classList.contains('flow-action-stack--docked'),
                    ScrollCount: scrollCount,
                    ScrollTop: scrollTop
                };
            }
            """);

        Assert.Equal(1, clearance.ScrollCount);
        var expectedScrollTop = 500 - (420 - clearance.DockHeight - 16);
        Assert.InRange(Math.Abs(clearance.ScrollTop - expectedScrollTop), 0, 1);
        Assert.True(clearance.FallbackCandidateDocked,
            "Non-finite visual viewport values should fall back to the layout viewport.");
    }

    [Fact]
    public async Task BioageResultReveal_UsesShiftedVisualViewportBounds()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(VisualViewportTestBootstrap(100, 400));

        var page = await context.NewPageAsync();
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && window.LwcBioageFlow");

        var reveal = await page.EvaluateAsync<VisualViewportRevealResult>(
            """
            () => {
                document.querySelectorAll('[data-flow-dock]').forEach(element => {
                    element.setAttribute('data-flow-dock', 'off');
                });

                const dock = document.createElement('div');
                dock.id = 'visualViewportBioageDock';
                dock.className = 'flow-action-stack';
                dock.setAttribute('data-flow-dock', 'always');
                dock.style.cssText = 'height:100px!important;min-height:100px!important;box-sizing:border-box;';
                document.body.appendChild(dock);
                document.documentElement.style.scrollPaddingTop = '20px';
                window.LwcFlowActionDock.refreshNow();

                const result = document.createElement('div');
                result.getBoundingClientRect = () => ({
                    x: 0,
                    y: 350,
                    top: 350,
                    right: 300,
                    bottom: 650,
                    left: 0,
                    width: 300,
                    height: 300,
                    toJSON() { return this; }
                });
                document.body.appendChild(result);

                window.__testScrollToCalls.length = 0;
                const initialScrollY = window.scrollY;
                window.LwcBioageFlow.revealBioageResult(result, { instant: true });

                return {
                    InitialScrollY: initialScrollY,
                    ScrollCount: window.__testScrollToCalls.length,
                    ScrollTop: window.__testScrollToCalls[0]?.top ?? 0
                };
            }
            """);

        Assert.Equal(1, reveal.ScrollCount);
        Assert.InRange(Math.Abs(reveal.ScrollTop - (reveal.InitialScrollY + 230)), 0, 1);
    }

    private static string VisualViewportTestBootstrap(double offsetTop, double height)
        => $$"""
        const visualViewport = new EventTarget();
        visualViewport.offsetTop = {{offsetTop}};
        visualViewport.height = {{height}};
        Object.defineProperty(window, 'visualViewport', {
            configurable: true,
            value: visualViewport
        });
        window.__testVisualViewport = visualViewport;
        window.__testScrollByCalls = [];
        window.__testScrollToCalls = [];
        window.scrollBy = (...args) => {
            const options = typeof args[0] === 'object'
                ? args[0]
                : { left: args[0] || 0, top: args[1] || 0 };
            window.__testScrollByCalls.push({
                left: Number(options.left) || 0,
                top: Number(options.top) || 0,
                behavior: options.behavior || 'auto'
            });
        };
        window.scrollTo = (...args) => {
            const options = typeof args[0] === 'object'
                ? args[0]
                : { left: args[0] || 0, top: args[1] || 0 };
            window.__testScrollToCalls.push({
                left: Number(options.left) || 0,
                top: Number(options.top) || 0,
                behavior: options.behavior || 'auto'
            });
        };
        """;

    private sealed class VisualViewportClearanceResult
    {
        public double DockHeight { get; set; }
        public bool FallbackCandidateDocked { get; set; }
        public int ScrollCount { get; set; }
        public double ScrollTop { get; set; }
    }

    private sealed class VisualViewportRevealResult
    {
        public double InitialScrollY { get; set; }
        public int ScrollCount { get; set; }
        public double ScrollTop { get; set; }
    }
}
