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
    public async Task DesktopApplication_KeepsPrimaryFieldsVisibleWithoutNestedPanels()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = 1280, Height = 720 } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        await page.GotoAsync("/apply");
        await page.WaitForFunctionAsync("() => applicationReady");
        var title = await ReadElementRectAsync(page, ".convergence-main > h1");
        var details = await ReadElementRectAsync(page, "#personalDetails");
        Assert.InRange(await page.EvaluateAsync<double>("scrollY"), 0, 1);
        Assert.True(details.Top >= title.Bottom);
        Assert.True(details.Bottom < details.ViewportHeight);
        Assert.Equal(0, await page.Locator("fieldset, #descriptionForm, .sub-progress-container").CountAsync());
        Assert.Equal("off", await page.Locator(".convergence-actions").GetAttributeAsync("data-flow-dock"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task Application_BackgroundDirectoryResponseDoesNotScrollOrStealTypingFocus(int viewportWidth, int viewportHeight)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = viewportWidth, Height = viewportHeight } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/data/divisions", async route => { await release.Task; await route.ContinueAsync(); });
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        try
        {
            await page.GotoAsync("/apply");
            await page.WaitForFunctionAsync("() => applicationReady");
            await page.Locator("#name").FillAsync("Still Typing");
            var scroll = await page.EvaluateAsync<double>("scrollY");
            var response = page.WaitForResponseAsync("**/api/data/divisions");
            release.SetResult();
            await response;
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
            await Assertions.Expect(page.Locator("#name")).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#name")).ToHaveValueAsync("Still Typing");
            Assert.InRange(await page.EvaluateAsync<double>("scrollY"), scroll - 1, scroll + 1);
            Assert.Empty(errors);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task Application_ApplyFollowsTheVisibleParticipationTerms(int viewportWidth, int viewportHeight)
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = viewportWidth, Height = viewportHeight } });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        await page.WaitForFunctionAsync("() => applicationReady");
        await page.Locator("#nextButton").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator("#privacyDetails > p")).ToBeInViewportAsync();
        await Assertions.Expect(page.Locator("#nextButton")).ToBeInViewportAsync();
        var terms = await page.Locator("#privacyDetails").BoundingBoxAsync();
        var actions = await page.Locator(".convergence-actions").BoundingBoxAsync();
        Assert.NotNull(terms); Assert.NotNull(actions);
        Assert.True(actions.Y >= terms.Y + terms.Height);
        Assert.DoesNotContain("flow-action-stack--docked", await page.Locator(".convergence-actions").GetAttributeAsync("class"));
        Assert.Equal("inputForm", await page.Locator("#nextButton").EvaluateAsync<string>("button => button.form.id"));
    }

}
