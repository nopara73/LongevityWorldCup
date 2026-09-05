using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class FlowActionDockLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task PlayWorkflowPages_DoNotExposeCompactHeaderMenu()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 480, Height = 1040 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        // The route class appears before the footer is parsed, so wait for the full
        // document before checking whether page chrome is absent or hidden.
        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.body?.classList.contains('play-flow-route')");

        var chromeState = await page.EvaluateAsync<PlayWorkflowChromeState>(
            """
            () => {
                const footer = document.querySelector('.footer');
                const menu = document.querySelector('.site-menu');
                return {
                    FooterDisplay: footer ? getComputedStyle(footer).display : '',
                    HasSiteMenu: Boolean(menu),
                    HasSiteMenuToggle: Boolean(document.querySelector('[data-site-menu-toggle]')),
                    HasSiteMenuPanel: Boolean(document.getElementById('siteMenuPanel'))
                };
            }
            """);

        Assert.Equal("none", chromeState.FooterDisplay);
        Assert.False(chromeState.HasSiteMenu);
        Assert.False(chromeState.HasSiteMenuToggle);
        Assert.False(chromeState.HasSiteMenuPanel);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task FlowActionPlacement_AuditsScopedRoutesAcrossViewportMatrix()
    {
        var routes = new[]
        {
            "/play",
            "/join",
            "/select-athlete",
            "/dashboard",
            "/edit-profile",
            "/proofs",
            "/pheno-age",
            "/bortz-age",
            "/apply?fake=1",
            "/review"
        };
        var viewports = new[]
        {
            (Width: 390, Height: 844),
            (Width: 480, Height: 1040),
            (Width: 844, Height: 390),
            (Width: 932, Height: 430),
            (Width: 768, Height: 1024),
            (Width: 1280, Height: 720),
            (Width: 1366, Height: 768)
        };
        var routeAnchorViewports = new HashSet<(int Width, int Height)>
        {
            (390, 844),
            (1280, 720)
        };
        var responsiveRepresentativeRoutes = new HashSet<string>(StringComparer.Ordinal)
        {
            "/join",
            "/apply?fake=1"
        };
        var app = App;
        var browser = Browser;
        var failures = new ConcurrentBag<string>();
        var scenarios = (
            from viewport in viewports
            from route in routes
                // Every route keeps constrained-mobile and desktop coverage. The
                // other breakpoint shapes run on two structurally different,
                // multi-action flows instead of repeating the same shared dock
                // algorithm for every route/viewport cross-product.
            where routeAnchorViewports.Contains(viewport)
                  || responsiveRepresentativeRoutes.Contains(route)
            select (Route: route, Viewport: viewport))
            .ToArray();

        await Parallel.ForEachAsync(
            scenarios,
            new ParallelOptions { MaxDegreeOfParallelism = 2 },
            async (scenario, _) =>
            {
                var route = scenario.Route;
                var viewport = scenario.Viewport;
                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    BaseURL = app.BaseAddress.ToString(),
                    Locale = "en-US",
                    ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height }
                });
                await BrowserTestApp.RouteExternalResourcesAsync(context);
                if (route != "/select-athlete")
                    await context.AddInitScriptAsync(FlowAuditStateScript);

                var page = await context.NewPageAsync();
                var errors = CapturePageErrors(page);

                try
                {
                    await page.GotoAsync(route, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                    await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
                    if (route == "/select-athlete")
                    {
                        await page.WaitForFunctionAsync(
                            """
                            () => document.documentElement.classList.contains('play-route-ready')
                                && !document.body.classList.contains('play-route-hydrating')
                                && document.getElementById('athleteSelectionPanel')?.hidden === false
                                && document.querySelector('.play-athlete-actions')?.getBoundingClientRect().height > 0
                            """);
                    }
                    else if (route == "/dashboard")
                    {
                        await page.WaitForFunctionAsync(
                            """
                            () => document.getElementById('athleteDashboardPanel')?.hidden === false
                                && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4
                            """);
                    }

                    await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
                    await WaitForManagedActionStacksSettledAsync(page);

                    var issues = await page.EvaluateAsync<string[]>(FlowActionPlacementAuditScript);
                    foreach (var issue in issues)
                        failures.Add($"{route} @ {viewport.Width}x{viewport.Height}: {issue}");
                    foreach (var error in errors)
                        failures.Add($"{route} @ {viewport.Width}x{viewport.Height}: console error: {error}");
                }
                finally
                {
                    await page.CloseAsync();
                }
            });

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(844, 390, true)]
    public async Task ConstrainedJoinTrackActions_DockTrackChoicesInsteadOfBuryingThemInCards(
        int viewportWidth,
        int viewportHeight,
        bool expectCompactLandscapeDock)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        var scenario = $"{viewportWidth}x{viewportHeight}";
        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => !document.getElementById('joinMobileStartAmateurBtn')?.disabled && !document.getElementById('joinMobileGoProButton')?.disabled");
        await ExpectActionStackDockedInViewportAsync(page, ".play-join-actions");

        var grouping = await page.EvaluateAsync<JoinTrackActionGrouping>(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const visible = element => {
                    if (!element) return false;
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return rect.width > 0
                        && rect.height > 0
                        && style.display !== 'none'
                        && style.visibility !== 'hidden';
                };
                const backStack = document.querySelector('.play-join-actions');
                const stackRect = backStack.getBoundingClientRect();
                const mobileAmateur = document.getElementById('joinMobileStartAmateurBtn');
                const mobilePro = document.getElementById('joinMobileGoProButton');
                const back = document.getElementById('joinTrackBackBtn');
                const amateurRect = mobileAmateur.getBoundingClientRect();
                const proRect = mobilePro.getBoundingClientRect();
                const backRect = back.getBoundingClientRect();
                return {
                    AmateurInCard: Boolean(document.getElementById('joinStartAmateurBtn')?.closest('.play-join-card')),
                    ProInCard: Boolean(document.getElementById('joinGoProButton')?.closest('.play-join-card--pro')),
                    AmateurInBackStack: Boolean(document.getElementById('joinStartAmateurBtn')?.closest('.play-join-actions')),
                    ProInBackStack: Boolean(document.getElementById('joinGoProButton')?.closest('.play-join-actions')),
                    MobileAmateurInBackStack: Boolean(mobileAmateur?.closest('.play-join-actions')),
                    MobileProInBackStack: Boolean(mobilePro?.closest('.play-join-actions')),
                    CardAmateurVisible: visible(document.getElementById('joinStartAmateurBtn')),
                    CardProVisible: visible(document.getElementById('joinGoProButton')),
                    MobileAmateurVisible: visible(mobileAmateur),
                    MobileProVisible: visible(mobilePro),
                    BackStackActionCount: backStack
                        ? Array.from(backStack.querySelectorAll('.flow-action')).filter(visible).length
                        : 0,
                    DockHeight: stackRect.height,
                    DockBottom: stackRect.bottom,
                    BackRight: backRect.right,
                    AmateurLeft: amateurRect.left,
                    AmateurRight: amateurRect.right,
                    ProLeft: proRect.left,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.True(grouping.AmateurInCard);
        Assert.True(grouping.ProInCard);
        Assert.False(grouping.AmateurInBackStack);
        Assert.False(grouping.ProInBackStack);
        Assert.False(grouping.CardAmateurVisible);
        Assert.False(grouping.CardProVisible);
        Assert.True(grouping.MobileAmateurInBackStack);
        Assert.True(grouping.MobileProInBackStack);
        Assert.True(grouping.MobileAmateurVisible);
        Assert.True(grouping.MobileProVisible);
        Assert.Equal(3, grouping.BackStackActionCount);
        Assert.True(grouping.DockBottom <= grouping.ViewportHeight + 1,
            $"Join track dock overflows the viewport: {grouping.DockBottom} > {grouping.ViewportHeight}.");

        if (expectCompactLandscapeDock)
        {
            Assert.True(grouping.DockHeight <= 76,
                $"Landscape join track dock should stay as a compact command bar, not a stacked menu: {grouping.DockHeight}px.");
            Assert.True(grouping.BackRight <= grouping.AmateurLeft - 8,
                $"Landscape Back should stay secondary on the left: back right {grouping.BackRight}, amateur left {grouping.AmateurLeft}.");
            Assert.True(grouping.AmateurRight <= grouping.ProLeft - 8,
                $"Landscape track choices should be separate controls: amateur right {grouping.AmateurRight}, pro left {grouping.ProLeft}.");
        }

        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DesktopJoinTrackActions_StayAttachedToTheirTrackCards()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");

        var grouping = await page.EvaluateAsync<JoinTrackActionGrouping>(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();

                const visible = element => {
                    if (!element) return false;
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return rect.width > 0
                        && rect.height > 0
                        && style.display !== 'none'
                        && style.visibility !== 'hidden';
                };
                const amateurButton = document.getElementById('joinStartAmateurBtn');
                const proButton = document.getElementById('joinGoProButton');
                const backStack = document.querySelector('.play-join-actions');
                return {
                    AmateurInCard: Boolean(amateurButton?.closest('.play-join-card')),
                    ProInCard: Boolean(proButton?.closest('.play-join-card--pro')),
                    AmateurInBackStack: Boolean(amateurButton?.closest('.play-join-actions')),
                    ProInBackStack: Boolean(proButton?.closest('.play-join-actions')),
                    MobileAmateurInBackStack: Boolean(document.getElementById('joinMobileStartAmateurBtn')?.closest('.play-join-actions')),
                    MobileProInBackStack: Boolean(document.getElementById('joinMobileGoProButton')?.closest('.play-join-actions')),
                    CardAmateurVisible: visible(amateurButton),
                    CardProVisible: visible(proButton),
                    MobileAmateurVisible: visible(document.getElementById('joinMobileStartAmateurBtn')),
                    MobileProVisible: visible(document.getElementById('joinMobileGoProButton')),
                    BackStackActionCount: backStack
                        ? Array.from(backStack.querySelectorAll('.flow-action')).filter(visible).length
                        : 0
                };
            }
            """);

        Assert.True(grouping.AmateurInCard);
        Assert.True(grouping.ProInCard);
        Assert.False(grouping.AmateurInBackStack);
        Assert.False(grouping.ProInBackStack);
        Assert.True(grouping.MobileAmateurInBackStack);
        Assert.True(grouping.MobileProInBackStack);
        Assert.True(grouping.CardAmateurVisible);
        Assert.True(grouping.CardProVisible);
        Assert.False(grouping.MobileAmateurVisible);
        Assert.False(grouping.MobileProVisible);
        Assert.Equal(1, grouping.BackStackActionCount);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopJoinTrackActions_StayVisibleInsideCardsWithVisibleBackAction()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => !document.getElementById('joinStartAmateurBtn')?.disabled && !document.getElementById('joinGoProButton')?.disabled");

        await ExpectActionStackInViewportAsync(page, ".play-join-actions");

        var amateurRect = await ReadElementRectAsync(page, "#joinStartAmateurBtn");
        var proRect = await ReadElementRectAsync(page, "#joinGoProButton");
        Assert.True(amateurRect.Bottom <= amateurRect.ViewportHeight - 8,
            $"Amateur CTA is below the first viewport: {amateurRect.Bottom}px > {amateurRect.ViewportHeight}px.");
        Assert.True(proRect.Bottom <= proRect.ViewportHeight - 8,
            $"Pro CTA is below the first viewport: {proRect.Bottom}px > {proRect.ViewportHeight}px.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", "#lwcStepOneActions", 650)]
    [InlineData("/apply?fake=1", ".convergence-actions", 768)]
    public async Task DesktopDocks_UseCompactCommandBarHeight(
        string path,
        string actionSelector,
        int viewportHeight)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1366, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await ExpectActionStackDockedInViewportAsync(page, actionSelector);

        var rect = await ReadElementRectAsync(page, actionSelector);
        Assert.True(rect.Height <= 68,
            $"{path}: {actionSelector} dock is too tall: {rect.Height}px.");
        Assert.True(errors.Count == 0, $"{path}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DesktopDock_PreservesItsInlinePlaceholderHeightWithoutOscillating()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync(
            "() => { window.LwcFlowActionDock?.refreshNow?.(); const actions = document.getElementById('lwcStepOneActions'); return actions && !actions.classList.contains('flow-action-stack--docked'); }");

        var inlineHeight = await page.Locator("#lwcStepOneActions").EvaluateAsync<double>(
            "element => element.getBoundingClientRect().height");
        Assert.True(inlineHeight > 0, "The inline action stack had no measurable height.");

        await page.SetViewportSizeAsync(1366, 650);
        await ExpectActionStackDockedInViewportAsync(page, "#lwcStepOneActions");
        var samples = await page.EvaluateAsync<double[][]>(
            """
            async () => {
                const actions = document.getElementById('lwcStepOneActions');
                const placeholder = document.querySelector('#lwc-step-1 > .flow-action-dock-placeholder');
                const samples = [];
                for (let index = 0; index < 10; index += 1) {
                    window.LwcFlowActionDock?.refreshNow?.();
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    samples.push([
                        placeholder?.getBoundingClientRect().height || 0,
                        actions?.getBoundingClientRect().height || 0,
                        actions?.classList.contains('flow-action-stack--docked') ? 1 : 0
                    ]);
                }
                return samples;
            }
            """);

        Assert.Equal(10, samples.Length);
        foreach (var sample in samples)
        {
            Assert.Equal(1, sample[2]);
            Assert.InRange(sample[0], inlineHeight - 1, inlineHeight + 1);
            Assert.True(sample[1] > 0, "The docked action stack lost its rendered height.");
        }
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopApplyFirstStage_KeepsDetailsAndActionsVisible()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => document.body?.dataset.convergenceStage === '1'");
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow()");
        await ExpectActionStackInViewportAsync(page, ".convergence-actions");

        var titleRect = await ReadElementRectAsync(page, ".convergence-main > h1");
        var detailsRect = await ReadElementRectAsync(page, "#personalDetails");
        var actionsRect = await ReadElementRectAsync(page, ".convergence-actions");
        var descriptionDisplay = await page.Locator("#descriptionForm").EvaluateAsync<string>("element => getComputedStyle(element).display");
        var scrollY = await page.EvaluateAsync<double>("() => window.scrollY");

        Assert.True(scrollY <= 1, $"Apply first stage should not auto-scroll on load: scrollY={scrollY}.");
        Assert.True(titleRect.Top >= 0, $"Apply title starts above the viewport: {titleRect.Top}px.");
        Assert.Equal("none", descriptionDisplay);
        Assert.True(detailsRect.Top >= titleRect.Bottom,
            $"Apply details should follow the title: details top {detailsRect.Top}px, title bottom {titleRect.Bottom}px.");
        Assert.True(detailsRect.Bottom <= actionsRect.Top - 12,
            $"Apply details are covered by the action dock: details bottom {detailsRect.Bottom}px, dock top {actionsRect.Top}px.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task ApplyNextStageTransition_DoesNotForceViewportScroll(
        int viewportWidth,
        int viewportHeight)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            window.__lwcScrollIntoViewCalls = [];
            const originalScrollIntoView = Element.prototype.scrollIntoView;
            Element.prototype.scrollIntoView = function (...args) {
                window.__lwcScrollIntoViewCalls.push({
                    tag: this.tagName,
                    id: this.id || '',
                    className: this.className || '',
                    text: (this.textContent || '').replace(/\s+/g, ' ').trim().slice(0, 80)
                });
                return originalScrollIntoView.apply(this, args);
            };
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        var scenario = $"{viewportWidth}x{viewportHeight}";
        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync(
            "() => typeof window.goToStage === 'function' && document.body?.dataset.convergenceStage === '1' && !document.getElementById('nextButton')?.disabled");
        await page.EvaluateAsync("() => window.scrollTo({ top: 0, left: 0, behavior: 'auto' })");
        await page.WaitForFunctionAsync(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const actions = document.querySelector('.convergence-actions');
                const nextButton = document.getElementById('nextButton');
                const rect = actions?.getBoundingClientRect();
                return window.scrollY <= 1
                    && nextButton
                    && !nextButton.disabled
                    && rect
                    && rect.top >= 0
                    && rect.bottom <= window.innerHeight;
            }
            """);
        await ExpectActionStackInViewportAsync(page, ".convergence-actions");

        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '2'");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var transition = await page.EvaluateAsync<ApplyStageTransitionScrollState>(
            """
            () => ({
                ScrollY: window.scrollY,
                Calls: window.__lwcScrollIntoViewCalls || []
            })
            """);

        Assert.True(transition.ScrollY <= 1,
            $"{scenario}: apply Next transition forced the viewport to jump: scrollY={transition.ScrollY}.");
        Assert.True(transition.Calls.Length == 0,
            $"{scenario}: apply Next called scrollIntoView for {string.Join(", ", transition.Calls.Select(call => call.Id))}.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task ApplyFirstStage_DoesNotShowDetailsPanelHalfCoveredByDock(
        int viewportWidth,
        int viewportHeight)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        var scenario = $"{viewportWidth}x{viewportHeight}";
        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => document.body?.dataset.convergenceStage === '1'");
        await ExpectActionStackDockedInViewportAsync(page, ".convergence-actions");

        var layout = await page.EvaluateAsync<ApplyFirstStageDetailsLayout>(
            """
            () => {
                const details = document.querySelector('#personalDetails');
                const actions = document.querySelector('.convergence-actions');
                const detailsRect = details.getBoundingClientRect();
                const actionRect = actions.getBoundingClientRect();
                const detailsStyle = getComputedStyle(details);
                return {
                    DetailsTop: detailsRect.top,
                    DetailsBottom: detailsRect.bottom,
                    DetailsVisible: detailsStyle.display !== 'none'
                        && detailsStyle.visibility !== 'hidden'
                        && detailsRect.width > 0
                        && detailsRect.height > 0,
                    DockTop: actionRect.top,
                    DockBottom: actionRect.bottom,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.True(layout.DetailsVisible,
            $"{scenario}: apply first stage should keep athlete details available in document flow.");
        Assert.True(layout.DetailsBottom <= layout.DockTop - 20 || layout.DetailsTop >= layout.DockBottom - 1,
            $"{scenario}: athlete details are half-covered by the action dock: details {layout.DetailsTop}-{layout.DetailsBottom}, dock {layout.DockTop}-{layout.DockBottom}, viewport {layout.ViewportHeight}.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }


}
