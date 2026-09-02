using System.Collections.Concurrent;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed partial class FlowActionDockBrowserTests(PlaywrightBrowserFixture browserFixture)
    : BrowserIntegrationTest(browserFixture)
{
    [Fact]
    public async Task HomePlayButton_NavigatesDirectlyToReadablePlayMenu()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await Task.WhenAll(
            page.WaitForURLAsync("**/play", new PageWaitForURLOptions { WaitUntil = WaitUntilState.Commit }),
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Play the game" }).ClickAsync());
        errors.Clear();
        await page.WaitForFunctionAsync("() => document.querySelector('.play-menu-wordmark')?.textContent?.trim() === 'JUST TRACK IT'");

        Assert.Null(await page.QuerySelectorAsync("#playLaunchStage"));
        Assert.False(await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('play-launching')"));

        Assert.Empty(errors);
    }

    [Fact]
    public async Task PlayStartFirstPaint_IsReadableBeforeIntroSettles()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".play-menu-wordmark");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var firstPaint = await page.EvaluateAsync<PlayStartFirstPaintState>(
            """
            () => {
                const wordmark = document.querySelector('.play-menu-wordmark');
                const actions = document.querySelector('.play-menu-actions');
                const wordmarkRect = wordmark.getBoundingClientRect();
                const actionsRect = actions.getBoundingClientRect();
                const wordmarkStyle = getComputedStyle(wordmark);
                const actionsStyle = getComputedStyle(actions);
                return {
                    WordmarkText: wordmark.textContent.trim(),
                    WordmarkOpacity: parseFloat(wordmarkStyle.opacity),
                    WordmarkTop: wordmarkRect.top,
                    WordmarkBottom: wordmarkRect.bottom,
                    ActionOpacity: parseFloat(actionsStyle.opacity),
                    ActionTop: actionsRect.top,
                    ActionBottom: actionsRect.bottom,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.Equal("JUST TRACK IT", firstPaint.WordmarkText);
        Assert.True(firstPaint.WordmarkOpacity >= 0.5, $"The play intro starts too close to blank: wordmark opacity {firstPaint.WordmarkOpacity}.");
        Assert.True(firstPaint.ActionOpacity >= 0.8, $"The play intro hides the initial actions too aggressively: action opacity {firstPaint.ActionOpacity}.");
        Assert.True(firstPaint.WordmarkTop >= 0, $"The first-paint wordmark starts above the viewport: {firstPaint.WordmarkTop}.");
        Assert.True(firstPaint.WordmarkBottom < firstPaint.ActionTop, $"The first-paint wordmark overlaps actions: {firstPaint.WordmarkBottom} >= {firstPaint.ActionTop}.");
        Assert.True(firstPaint.ActionBottom <= firstPaint.ViewportHeight + 1, $"The first-paint actions overflow the viewport: {firstPaint.ActionBottom} > {firstPaint.ViewportHeight}.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/play", ".play-menu-actions")]
    [InlineData("/pheno-age", "#lwcStepOneActions")]
    [InlineData("/bortz-age", "#lwcStepOneActions")]
    [InlineData("/apply?fake=1", ".convergence-actions")]
    public async Task MobileWorkflowActionStacks_DockInsideTheFirstViewport(
        string path,
        string actionSelector)
    {
        var app = App;
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        await ExpectActionStackDockedInViewportAsync(page, actionSelector);
        await ExpectNoHorizontalOverflowAsync(page);

        Assert.True(errors.Count == 0, $"{path}: {string.Join(Environment.NewLine, errors)}");
    }

    [Fact]
    public async Task MobileOnboardingActions_RemainDockedWhileEnteringContactEmail()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && typeof window.goToStage === 'function'");
        await page.EvaluateAsync("() => { window.goToStage(7); window.LwcFlowActionDock.refreshNow(); }");
        await ExpectActionStackDockedInViewportAsync(page, ".convergence-actions");

        await page.Locator("#accountEmail").FocusAsync();
        await page.WaitForFunctionAsync("() => document.activeElement?.id === 'accountEmail'");

        Assert.True(
            await HasDockClassAsync(page, ".convergence-actions"),
            "The onboarding action menu should not disappear when a text field receives focus.");
        await ExpectActionStackInViewportAsync(page, ".convergence-actions");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task MobileBioageContinue_FirstTapAfterEditingAdvancesTheFlow()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await page.Locator("#blood-draw-date").FillAsync(DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));

        var continueButton = page.Locator("#lwcToStep2Btn");
        await continueButton.ScrollIntoViewIfNeededAsync();
        var box = await continueButton.BoundingBoxAsync();
        Assert.NotNull(box);

        await page.Touchscreen.TapAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task PlayStartBackNavigation_KeepsActionsBottomDockedDuringPanelTransition(
        int viewportWidth,
        int viewportHeight)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

            await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && !document.querySelector('#newGameBtn')?.disabled");
            await page.Locator("#newGameBtn").ClickAsync();
            await page.WaitForDomContentLoadedUrlAsync("**/join");
            await page.WaitForFunctionAsync("() => !document.querySelector('#joinTrackPanel')?.hidden");

            await page.Locator("#joinTrackBackBtn").ClickAsync();
            await page.WaitForDomContentLoadedUrlAsync("**/play");
            await page.WaitForFunctionAsync("() => !document.querySelector('#playStartPanel')?.hidden");
            await page.WaitForFunctionAsync(
                """
                () => {
                    const panel = document.getElementById('playStartPanel');
                    const actions = document.querySelector('.play-menu-actions');
                    return panel && !panel.hidden
                        && !document.documentElement.classList.contains('play-panel-transitioning')
                        && actions?.classList.contains('flow-action-stack--docked');
                }
                """);

            var state = await page.EvaluateAsync<PlayStartBackDockState>(
                """
                () => {
                    const actions = document.querySelector('.play-menu-actions');
                    const panel = document.getElementById('playStartPanel');
                    const rect = actions.getBoundingClientRect();
                    return {
                        ActionDocked: actions.classList.contains('flow-action-stack--docked'),
                        ActionTop: rect.top,
                        ActionRight: rect.right,
                        ActionBottom: rect.bottom,
                        ActionLeft: rect.left,
                        ViewportWidth: window.innerWidth,
                        ViewportHeight: window.innerHeight,
                        PanelClass: panel?.className || '',
                        PanelTransform: panel ? getComputedStyle(panel).transform : ''
                    };
                }
                """);

            var scenario = $"{viewportWidth}x{viewportHeight}";
            Assert.True(state.ActionDocked, $"{scenario}: back to /play should keep the start actions in the bottom dock immediately.");
            Assert.InRange(Math.Abs(state.ActionBottom - state.ViewportHeight), 0, 1.1);
            Assert.True(state.ActionTop >= 0, $"{scenario}: the returned play actions start above the viewport: {state.ActionTop}.");
            Assert.True(state.ActionLeft <= 1, $"{scenario}: the returned play dock is inset from the left edge during transition: {state.ActionLeft}.");
            Assert.True(state.ActionRight >= state.ViewportWidth - 1, $"{scenario}: the returned play dock does not reach the right edge during transition: {state.ActionRight} < {state.ViewportWidth}.");
            Assert.Equal("none", state.PanelTransform);
            Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DockedActionStack_PortalsWithoutMutatingContainingBlockAncestors()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await ExpectActionStackDockedInViewportAsync(page, ".play-menu-actions");

        var state = await page.EvaluateAsync<ModernContainingBlockDockState>(
            """
            () => {
                const panel = document.getElementById('playStartPanel');
                const actions = document.querySelector('.play-menu-actions');
                panel.style.translate = '1px 1px';
                panel.style.scale = '1.001';
                panel.style.rotate = '0.01deg';
                panel.style.contain = 'paint';
                panel.style.containerType = 'inline-size';
                panel.style.contentVisibility = 'auto';
                panel.style.willChange = 'translate, scale, rotate';
                window.LwcFlowActionDock.refreshNow();

                const panelStyle = getComputedStyle(panel);
                const actionRect = actions.getBoundingClientRect();
                return {
                    ActionDocked: actions.classList.contains('flow-action-stack--docked'),
                    ActionBottom: actionRect.bottom,
                    ActionLeft: actionRect.left,
                    ActionRight: actionRect.right,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight,
                    ActionParentIsBody: actions.parentElement === document.body,
                    PanelTransform: panelStyle.transform,
                    PanelTranslate: panelStyle.translate,
                    PanelScale: panelStyle.scale,
                    PanelRotate: panelStyle.rotate,
                    PanelContain: panelStyle.contain,
                    PanelContainerType: panelStyle.containerType,
                    PanelContentVisibility: panelStyle.contentVisibility,
                    PanelWillChange: panelStyle.willChange
                };
            }
            """);

        Assert.True(state.ActionDocked, "The action stack should stay docked after ancestor containment changes.");
        Assert.True(state.ActionParentIsBody, "Docked actions should be portalled to the body instead of rewriting their ancestors.");
        Assert.InRange(Math.Abs(state.ActionBottom - state.ViewportHeight), 0, 1.1);
        Assert.True(state.ActionLeft <= 1, $"The dock is inset from the left edge: {state.ActionLeft}.");
        Assert.True(state.ActionRight >= state.ViewportWidth - 1, $"The dock does not reach the right edge: {state.ActionRight} < {state.ViewportWidth}.");
        Assert.Equal("none", state.PanelTransform);
        Assert.Equal("1px 1px", state.PanelTranslate);
        Assert.Equal("1.001", state.PanelScale);
        Assert.Equal("0.01deg", state.PanelRotate);
        Assert.Equal("paint", state.PanelContain);
        Assert.Equal("inline-size", state.PanelContainerType);
        Assert.Equal("auto", state.PanelContentVisibility);
        Assert.Contains("translate", state.PanelWillChange);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DockedActionStack_RemovalCleansItsPlaceholderWithoutResurrectingControls()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await ExpectActionStackDockedInViewportAsync(page, ".play-menu-actions");
        await page.Locator(".play-menu-actions").EvaluateAsync("element => element.remove()");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.play-menu-actions') && !document.querySelector('.play-start-panel .flow-action-dock-placeholder')");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844, 190)]
    [InlineData(1280, 720, 220)]
    [InlineData(1366, 768, 235)]
    public async Task ReviewPage_KeepsHomeActionWithReviewPanel(
        int viewportWidth,
        int viewportHeight,
        double minimumArtworkHeight)
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

        await page.GotoAsync("/review", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".application-review-copy.primary");
        await page.WaitForFunctionAsync(
            "() => { const image = document.querySelector('.application-review-visual .illustration'); return image?.complete && image.naturalWidth > 0; }");
            await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow()");
            await ExpectActionStackInViewportAsync(page, ".application-review-actions");

            var titleRect = await ReadElementRectAsync(page, ".application-review-title");
            var visualRect = await ReadElementRectAsync(page, ".application-review-visual .illustration");
            var primaryCopyRect = await ReadElementRectAsync(page, ".application-review-copy.primary");
            var actionRect = await ReadElementRectAsync(page, ".application-review-actions");
            var actionDocked = await HasDockClassAsync(page, ".application-review-actions");
            var visibleSecondaryCopies = await page.EvaluateAsync<double[]>(
                """
                () => Array.from(document.querySelectorAll('.application-review-copy:not(.primary)'))
                    .filter(element => {
                        const style = getComputedStyle(element);
                        const rect = element.getBoundingClientRect();
                        return style.display !== 'none'
                            && style.visibility !== 'hidden'
                            && rect.width > 0
                            && rect.height > 0;
                    })
                    .map(element => element.getBoundingClientRect().bottom)
                """);

            var scenario = $"{viewportWidth}x{viewportHeight}";
            Assert.True(titleRect.Top >= 0,
                $"{scenario}: review title starts above the viewport: {titleRect.Top}px.");
            Assert.Equal(1, await page.Locator(".application-review-visual .illustration").CountAsync());
            Assert.True(
                await page.Locator(".application-review-visual .illustration").EvaluateAsync<bool>(
                    "image => image.complete && image.naturalWidth > 0"),
                $"{scenario}: review joke artwork did not decode.");
            Assert.True(visualRect.Width > 0 && visualRect.Height > 0,
                $"{scenario}: review joke artwork is not visibly rendered: {visualRect.Width}x{visualRect.Height}px.");
            Assert.True(visualRect.Height + 0.5 >= minimumArtworkHeight,
                $"{scenario}: review joke artwork was over-compressed: {visualRect.Height}px < {minimumArtworkHeight}px.");
            Assert.InRange(Math.Abs(visualRect.Width / visualRect.Height - 860d / 721d), 0, 0.02);
            Assert.True(visualRect.Left >= 0 && visualRect.Right <= viewportWidth,
                $"{scenario}: review joke artwork escapes the viewport horizontally: {visualRect.Left}-{visualRect.Right}px in {viewportWidth}px.");
            Assert.True(visualRect.Top >= 0 && visualRect.Bottom <= viewportHeight,
                $"{scenario}: review joke artwork escapes the viewport vertically: {visualRect.Top}-{visualRect.Bottom}px in {viewportHeight}px.");
            Assert.True(visualRect.Bottom <= primaryCopyRect.Top,
                $"{scenario}: review joke artwork overlaps the primary message: artwork bottom {visualRect.Bottom}px, copy top {primaryCopyRect.Top}px.");
            Assert.True(primaryCopyRect.Bottom <= actionRect.Top - 8,
                $"{scenario}: review primary message is too close to the Home action: copy bottom {primaryCopyRect.Bottom}px, action top {actionRect.Top}px.");
            Assert.NotEmpty(visibleSecondaryCopies);
            foreach (var secondaryBottom in visibleSecondaryCopies)
            {
                Assert.True(secondaryBottom <= actionRect.Top - 8,
                    $"{scenario}: review secondary copy is too close to the Home action: copy bottom {secondaryBottom}px, action top {actionRect.Top}px.");
            }
            Assert.False(actionDocked,
                $"{scenario}: review Home should stay inline on standard phone and desktop viewports instead of becoming a detached bottom dock.");
            Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task MobileAthleteSearch_KeepsDockVisibleWhileTyping()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ReducedMotion = ReducedMotion.NoPreference,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await ExpectActionStackDockedInViewportAsync(page, ".play-athlete-actions");
        var beforeFocus = await ReadElementRectAsync(page, ".play-athlete-actions");

        await page.Locator("#playAthleteInput").FocusAsync();
        await ExpectActionStackDockedInViewportAsync(page, ".play-athlete-actions");
        var afterFocus = await ReadElementRectAsync(page, ".play-athlete-actions");

        Assert.True(await page.Locator("#playAthleteInput").EvaluateAsync<bool>("input => input === document.activeElement"));
        Assert.InRange(Math.Abs(afterFocus.Bottom - beforeFocus.Bottom), 0, 1);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task TabletJoinTitle_StaysAtWorkflowScale()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 768, Height = 1024 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForSelectorAsync(".play-join-title");

        var title = await page.Locator(".play-join-title").EvaluateAsync<WorkflowTitleLayout>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return {
                    Text: element.textContent.trim(),
                    FontSize: parseFloat(style.fontSize),
                    LineHeight: parseFloat(style.lineHeight),
                    Bottom: rect.bottom
                };
            }
            """);

        Assert.Equal("Choose your track", title.Text);
        Assert.InRange(title.FontSize, 30, 40);
        Assert.True(title.LineHeight <= 46, $"Tablet join title is too tall for a workflow step: {title.LineHeight}px.");
        Assert.True(title.Bottom <= 300, $"Tablet join title pushes the track choice too far down: {title.Bottom}px.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/join")]
    [InlineData("/select-athlete")]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task FlowEntryPages_DoNotStealInitialFocus(string path)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");

            var focusedElementId = await page.EvaluateAsync<string?>(
                """
                () => {
                    const active = document.activeElement;
                    if (!active || active === document.body || active === document.documentElement) return null;
                    return active.id || active.tagName;
                }
                """);

        Assert.Null(focusedElementId);
        Assert.True(errors.Count == 0, $"{path}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task StoredAthleteSelection_DoesNotFocusSearchInputOnEntry()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            """
            () => document.getElementById('playAthleteInput')?.value === 'Browser Test Athlete'
                && !document.body.classList.contains('play-route-hydrating')
            """);

        var focusedElementId = await page.EvaluateAsync<string?>(
            """
            () => {
                const active = document.activeElement;
                if (!active || active === document.body || active === document.documentElement) return null;
                return active.id || active.tagName;
            }
            """);

        Assert.Null(focusedElementId);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PlayStartWordmark_IsUnboxedOnHeaderBackground()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync(
            """
            () => {
                const wordmark = document.querySelector('.play-menu-wordmark');
                const actions = document.querySelector('.play-menu-actions');
                return wordmark?.textContent?.trim() === 'JUST TRACK IT'
                    && !document.body.classList.contains('play-start-preintro')
                    && !document.body.classList.contains('play-start-intro')
                    && actions?.classList.contains('flow-action-stack--docked');
            }
            """);

        var playStartState = await page.Locator(".play-menu-wordmark").EvaluateAsync<PlayStartWordmarkState>(
            """
            wordmark => {
                const rect = wordmark.getBoundingClientRect();
                const hero = wordmark.closest('.play-menu-hero');
                const panel = wordmark.closest('.play-start-panel');
                const header = document.querySelector('header');
                const logo = document.querySelector('.header-link > .main-logo-image');
                const watermark = document.querySelector('.play-logo-watermark');
                const stickyHeader = document.getElementById('site-sticky-header');
                const bannerText = document.querySelector('.bannertext');
                const tagline = document.querySelector('.tagline');
                const main = document.querySelector('.play-hub-main');
                const actionStack = document.querySelector('.play-menu-actions');
                const heroRect = hero.getBoundingClientRect();
                const headerRect = header.getBoundingClientRect();
                const logoRect = logo.getBoundingClientRect();
                const watermarkRect = watermark.getBoundingClientRect();
                const actionStackRect = actionStack.getBoundingClientRect();
                const mainRect = main.getBoundingClientRect();
                const wordmarkStyle = getComputedStyle(wordmark);
                const logoStyle = getComputedStyle(logo);
                const watermarkStyle = getComputedStyle(watermark);
                const stickyHeaderStyle = getComputedStyle(stickyHeader);
                const heroStyle = getComputedStyle(hero);
                const panelBeforeStyle = getComputedStyle(panel, '::before');
                const headerStyle = getComputedStyle(header);
                const bannerTextStyle = getComputedStyle(bannerText);
                const taglineStyle = getComputedStyle(tagline);
                const bodyStyle = getComputedStyle(document.body);
                const mainStyle = getComputedStyle(main);
                const actionStackStyle = getComputedStyle(actionStack);
                const headerBackground = getComputedStyle(document.documentElement)
                    .getPropertyValue('--background-color')
                    .trim();
                return {
                    HeaderBackground: headerBackground,
                    Text: wordmark.textContent.trim(),
                    Width: rect.width,
                    Height: rect.height,
                    WordmarkTop: rect.top,
                    WordmarkBottom: rect.bottom,
                    WordmarkBackgroundColor: wordmarkStyle.backgroundColor,
                    WordmarkBackgroundImage: wordmarkStyle.backgroundImage,
                    WordmarkBorderRadius: wordmarkStyle.borderRadius,
                    WordmarkBoxShadow: wordmarkStyle.boxShadow,
                    WordmarkColor: wordmarkStyle.color,
                    HeroLeft: heroRect.left,
                    HeroRight: heroRect.right,
                    HeroWidth: heroRect.width,
                    HeaderBottom: headerRect.bottom,
                    LogoWidth: logoRect.width,
                    LogoHeight: logoRect.height,
                    LogoDisplay: logoStyle.display,
                    WatermarkWidth: watermarkRect.width,
                    WatermarkHeight: watermarkRect.height,
                    WatermarkTop: watermarkRect.top,
                    WatermarkBottom: watermarkRect.bottom,
                    WatermarkDisplay: watermarkStyle.display,
                    WatermarkOpacity: watermarkStyle.opacity,
                    WatermarkFilter: watermarkStyle.filter,
                    WatermarkMixBlendMode: watermarkStyle.mixBlendMode,
                    WatermarkPosition: watermarkStyle.position,
                    WatermarkPointerEvents: watermarkStyle.pointerEvents,
                    WatermarkSource: watermark.currentSrc,
                    StickyHeaderDisplay: stickyHeaderStyle.display,
                    StickyHeaderOpacity: stickyHeaderStyle.opacity,
                    StickyHeaderPointerEvents: stickyHeaderStyle.pointerEvents,
                    BannerTextDisplay: bannerTextStyle.display,
                    TaglineDisplay: taglineStyle.display,
                    MainTop: mainRect.top,
                    HeaderBackgroundImage: headerStyle.backgroundImage,
                    HeaderBoxShadow: headerStyle.boxShadow,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight,
                    ScrollWidth: document.documentElement.scrollWidth,
                    PanelBeforeBackgroundColor: panelBeforeStyle.backgroundColor,
                    PanelBeforeBackgroundImage: panelBeforeStyle.backgroundImage,
                    PanelBeforePosition: panelBeforeStyle.position,
                    PanelBeforeZIndex: panelBeforeStyle.zIndex,
                    HeroBackgroundColor: heroStyle.backgroundColor,
                    HeroBackgroundImage: heroStyle.backgroundImage,
                    BodyBackground: bodyStyle.background,
                    BodyBackgroundColor: bodyStyle.backgroundColor,
                    BodyBackgroundImage: bodyStyle.backgroundImage,
                    MainBackgroundColor: mainStyle.backgroundColor,
                    MainBackgroundImage: mainStyle.backgroundImage,
                    ActionStackTop: actionStackRect.top,
                    ActionStackBackground: actionStackStyle.background,
                    ActionStackBorderTopWidth: actionStackStyle.borderTopWidth,
                    HasImageCrop: Boolean(document.querySelector('.img-crop')),
                    HasHeroImage: Boolean(document.querySelector('.play-menu-hero img')),
                    HasJustTrackItAsset: Boolean(document.querySelector('img[src*="JustTrackIt"], link[href*="JustTrackIt"]'))
                };
            }
            """);

        Assert.Equal("JUST TRACK IT", playStartState.Text);
        Assert.False(playStartState.HasImageCrop);
        Assert.False(playStartState.HasHeroImage);
        Assert.False(playStartState.HasJustTrackItAsset);
        Assert.True(playStartState.Width > 0, "Wordmark has no rendered width.");
        Assert.True(playStartState.Height > 0, "Wordmark has no rendered height.");
        Assert.Equal("rgba(0, 0, 0, 0)", playStartState.WordmarkBackgroundColor);
        Assert.Equal("none", playStartState.WordmarkBackgroundImage);
        Assert.Equal("0px", playStartState.WordmarkBorderRadius);
        Assert.Equal("none", playStartState.WordmarkBoxShadow);
        Assert.Equal("rgb(255, 255, 255)", playStartState.WordmarkColor);
        Assert.Equal("none", playStartState.LogoDisplay);
        Assert.Equal("block", playStartState.WatermarkDisplay);
        Assert.True(playStartState.WatermarkWidth >= playStartState.ViewportWidth * 0.75, $"Watermark should stay visually dominant: {playStartState.WatermarkWidth}px");
        Assert.True(playStartState.WatermarkWidth <= playStartState.ViewportWidth * 0.9, $"Watermark should fit the viewport width instead of cropping: {playStartState.WatermarkWidth}px");
        Assert.True(playStartState.WatermarkHeight <= playStartState.ViewportHeight * 0.42, $"Watermark should sit between the wordmark and buttons instead of filling the page: {playStartState.WatermarkHeight}px");
        AssertPlayStartLogoBetweenWordmarkAndActions(playStartState, 40);
        Assert.Equal("0.17", playStartState.WatermarkOpacity);
        Assert.Equal("none", playStartState.WatermarkFilter);
        Assert.Equal("normal", playStartState.WatermarkMixBlendMode);
        Assert.Equal("fixed", playStartState.WatermarkPosition);
        Assert.Equal("none", playStartState.WatermarkPointerEvents);
        Assert.Contains("/assets/favicon-dark-512x512.png?v=", playStartState.WatermarkSource);
        Assert.Equal("none", playStartState.StickyHeaderDisplay);
        Assert.Equal("0", playStartState.StickyHeaderOpacity);
        Assert.Equal("none", playStartState.StickyHeaderPointerEvents);
        Assert.Equal("none", playStartState.BannerTextDisplay);
        Assert.Equal("none", playStartState.TaglineDisplay);
        Assert.True(playStartState.HeroLeft >= -1, $"Hero stage overflows left: {playStartState.HeroLeft}");
        Assert.True(playStartState.HeroRight <= playStartState.ViewportWidth + 1, $"Hero stage overflows right: {playStartState.HeroRight} > {playStartState.ViewportWidth}");
        Assert.True(playStartState.ScrollWidth <= playStartState.ViewportWidth + 1, $"Play hero creates horizontal overflow: {playStartState.ScrollWidth} > {playStartState.ViewportWidth}");
        Assert.Equal("#101820", playStartState.HeaderBackground);
        Assert.Equal("none", playStartState.HeaderBackgroundImage);
        Assert.Equal("none", playStartState.BodyBackgroundImage);
        Assert.Equal("none", playStartState.MainBackgroundImage);
        Assert.Equal("none", playStartState.PanelBeforeBackgroundImage);
        Assert.Equal("absolute", playStartState.PanelBeforePosition);
        Assert.Equal("-1", playStartState.PanelBeforeZIndex);
        Assert.Equal("none", playStartState.HeaderBoxShadow);
        Assert.InRange(Math.Abs(playStartState.MainTop - playStartState.HeaderBottom), 0, 1);
        Assert.Equal("none", playStartState.HeroBackgroundImage);
        Assert.Contains("18, 18, 18", playStartState.ActionStackBackground);
        Assert.DoesNotContain("238, 238, 238", playStartState.ActionStackBackground);
        Assert.Equal("0px", playStartState.ActionStackBorderTopWidth);

        await page.SetViewportSizeAsync(1366, 768);
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });
        await page.WaitForFunctionAsync(
            """
            () => {
                const wordmark = document.querySelector('.play-menu-wordmark');
                const actions = document.querySelector('.play-menu-actions');
                return wordmark?.textContent?.trim() === 'JUST TRACK IT'
                    && !document.body.classList.contains('play-start-preintro')
                    && !document.body.classList.contains('play-start-intro')
                    && actions?.offsetParent !== null;
            }
            """);

        var desktopPlayStartState = await page.EvaluateAsync<PlayStartWordmarkState>(
            """
            () => {
                const wordmarkRect = document.querySelector('.play-menu-wordmark').getBoundingClientRect();
                const watermarkRect = document.querySelector('.play-logo-watermark').getBoundingClientRect();
                const actionStackRect = document.querySelector('.play-menu-actions').getBoundingClientRect();
                return {
                    WordmarkBottom: wordmarkRect.bottom,
                    WatermarkWidth: watermarkRect.width,
                    WatermarkHeight: watermarkRect.height,
                    WatermarkTop: watermarkRect.top,
                    WatermarkBottom: watermarkRect.bottom,
                    ActionStackTop: actionStackRect.top,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight,
                    ScrollWidth: document.documentElement.scrollWidth
                };
            }
            """);

        Assert.True(desktopPlayStartState.WatermarkWidth >= 200, $"Desktop watermark should remain visible: {desktopPlayStartState.WatermarkWidth}px");
        Assert.True(desktopPlayStartState.WatermarkHeight <= desktopPlayStartState.ViewportHeight * 0.32, $"Desktop watermark should not crowd the action buttons: {desktopPlayStartState.WatermarkHeight}px");
        Assert.True(desktopPlayStartState.ScrollWidth <= desktopPlayStartState.ViewportWidth + 1, $"Play hero creates horizontal overflow on desktop: {desktopPlayStartState.ScrollWidth} > {desktopPlayStartState.ViewportWidth}");
        AssertPlayStartLogoBetweenWordmarkAndActions(desktopPlayStartState, 40);
        Assert.Empty(errors);
    }

    internal static List<string> CapturePageErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                var location = message.Location;
                errors.Add(string.IsNullOrWhiteSpace(location)
                    ? message.Text
                    : $"{message.Text} [{location}]");
            }
        };
        page.PageError += (_, error) => errors.Add(error);
        return errors;
    }

    internal static void AssertPlayStartLogoBetweenWordmarkAndActions(
        PlayStartWordmarkState state,
        double centerTolerancePixels)
    {
        Assert.True(state.WatermarkTop >= state.WordmarkBottom + 16, $"Watermark starts too close to the wordmark: {state.WatermarkTop}px <= {state.WordmarkBottom}px");
        Assert.True(state.WatermarkBottom <= state.ActionStackTop - 16, $"Watermark overlaps the action buttons: {state.WatermarkBottom}px >= {state.ActionStackTop}px");

        var gapCenter = (state.WordmarkBottom + state.ActionStackTop) / 2;
        var watermarkCenter = (state.WatermarkTop + state.WatermarkBottom) / 2;
        Assert.InRange(Math.Abs(watermarkCenter - gapCenter), 0, centerTolerancePixels);
    }

    internal const string FlowAuditStateScript =
        """
        const athlete = {
            Name: 'Browser Test Athlete',
            DisplayName: 'Browser Test Athlete',
            Division: "Men's",
            Flag: 'United States',
            Country: 'United States',
            PersonalLink: 'https://example.test/browser-test-athlete',
            MediaContact: 'browser-test-athlete@example.test',
            Why: 'Testing the athlete navigation flow.',
            ProfilePic: '/assets/content-images/longevity-world-cup-silhouette.webp',
            ProfilePictureUrl: '/assets/content-images/longevity-world-cup-silhouette.webp',
            DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
            Biomarkers: [
                { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1, Hba1cMmolMol: 35 }
            ]
        };
        window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
        window.localStorage.setItem('selectedAthleteName', athlete.Name);
        window.localStorage.setItem('hasApplication', 'true');
        window.sessionStorage.setItem('biomarkerData', JSON.stringify({
            Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1, Hba1cMmolMol: 35 }]
        }));
        """;

    internal const string FlowActionPlacementAuditScript =
        """
        () => {
            window.LwcFlowActionDock?.refreshNow?.();

            const issues = [];
            const viewportWidth = window.innerWidth;
            const viewportHeight = window.innerHeight;
            const visible = element => {
                if (!element || !element.isConnected || element.hidden) return false;
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return rect.width > 0
                    && rect.height > 0
                    && style.display !== 'none'
                    && style.visibility !== 'hidden';
            };
            const rectOf = element => {
                const rect = element.getBoundingClientRect();
                return {
                    left: rect.left,
                    right: rect.right,
                    top: rect.top,
                    bottom: rect.bottom,
                    width: rect.width,
                    height: rect.height,
                    center: rect.left + (rect.width / 2)
                };
            };
            const dockConditionMatches = stack => {
                const requiredSelector = stack.getAttribute('data-flow-dock-when');
                if (requiredSelector && !document.querySelector(requiredSelector)) return false;

                const excludedSelector = stack.getAttribute('data-flow-dock-unless');
                if (excludedSelector && document.querySelector(excludedSelector)) return false;

                return true;
            };

            const actionStacks = Array.from(document.querySelectorAll('.flow-action-stack')).filter(visible);
            const flowActions = Array.from(document.querySelectorAll('.flow-action')).filter(visible);
            if (flowActions.length === 0) {
                issues.push('no visible flow actions');
            }

            flowActions.forEach(action => {
                const rect = rectOf(action);
                const label = action.querySelector('.flow-action__label, .dashboard-action-label');

                if (rect.left < -1) issues.push(`${action.id || action.textContent.trim()} overflows left (${rect.left.toFixed(1)})`);
                if (rect.right > viewportWidth + 1) issues.push(`${action.id || action.textContent.trim()} overflows right (${rect.right.toFixed(1)} > ${viewportWidth})`);
                if (rect.height < 43) issues.push(`${action.id || action.textContent.trim()} has too small tap target (${rect.height.toFixed(1)}px)`);
                if (label && visible(label) && label.scrollWidth > label.clientWidth + 2) {
                    issues.push(`${action.id || action.textContent.trim()} label is horizontally clipped`);
                }
                if (label && visible(label) && label.scrollHeight > label.clientHeight + 3) {
                    issues.push(`${action.id || action.textContent.trim()} label is vertically clipped`);
                }
            });

            const dockedStacks = actionStacks.filter(stack => stack.classList.contains('flow-action-stack--docked'));
            const settledDockedStacks = dockedStacks.filter(stack =>
                !stack.classList.contains('flow-action-stack--dock-entering'));
            actionStacks
                .filter(stack => stack.hasAttribute('data-flow-dock')
                    && dockConditionMatches(stack)
                    && !stack.classList.contains('flow-action-stack--docked'))
                .forEach(stack => {
                    const rect = rectOf(stack);
                    if (rect.bottom > viewportHeight + 1) {
                        const transitionActive = document.documentElement.classList.contains('play-panel-transitioning');
                        const activeElement = document.activeElement;
                        issues.push(`undocked managed action stack falls below viewport (${rect.bottom.toFixed(1)} > ${viewportHeight}; class="${stack.className}"; dock="${stack.getAttribute('data-flow-dock')}"; transition="${transitionActive}"; active="${activeElement?.tagName || ''}.${activeElement?.className || ''}")`);
                    }
                    if (rect.top < -1) {
                        issues.push(`undocked managed action stack starts above viewport (${rect.top.toFixed(1)})`);
                    }
                });

            if (dockedStacks.length !== settledDockedStacks.length) {
                const transitioningStacks = dockedStacks
                    .filter(stack => !settledDockedStacks.includes(stack))
                    .map(stack => stack.id || stack.className || stack.textContent.trim());
                issues.push(`a docked action stack is still mid-transition after settling (${transitioningStacks.join('; ')})`);
            }
            if (settledDockedStacks.length > 1) {
                issues.push(`multiple docked action stacks are visible (${settledDockedStacks.length})`);
            }

            settledDockedStacks.forEach(stack => {
                const rect = rectOf(stack);
                const maxDockHeight = Math.max(172, viewportHeight * (viewportHeight <= 480 ? 0.42 : 0.35));
                if (rect.top < -1) issues.push(`docked stack overflows top (${rect.top.toFixed(1)})`);
                if (rect.bottom > viewportHeight + 1) issues.push(`docked stack overflows bottom (${rect.bottom.toFixed(1)} > ${viewportHeight})`);
                if (rect.height > maxDockHeight) issues.push(`docked stack is too tall (${rect.height.toFixed(1)}px > ${maxDockHeight.toFixed(1)}px)`);

                const actions = Array.from(stack.querySelectorAll('.flow-action')).filter(visible);
                const back = actions.find(action => action.classList.contains('back-button') && action.classList.contains('flow-action--icon-left'));
                const primary = actions.find(action => action !== back && !action.classList.contains('flow-action--secondary'));
                if (back && primary && actions.length === 2) {
                    const backRect = rectOf(back);
                    const primaryRect = rectOf(primary);
                    if (viewportWidth >= 960) {
                        if (backRect.right > primaryRect.left - 8) {
                            issues.push('desktop docked Back action competes with the primary action instead of staying left');
                        }
                        if (Math.abs(primaryRect.center - (viewportWidth / 2)) > 24) {
                            issues.push(`desktop docked primary action is not centered (${primaryRect.center.toFixed(1)} vs ${(viewportWidth / 2).toFixed(1)})`);
                        }
                    } else {
                        if (backRect.top < primaryRect.top) {
                            issues.push('mobile docked Back action appears before the primary action');
                        }
                    }

                    if (backRect.width > Math.min(primaryRect.width * 0.82, 214)) {
                        issues.push(`Back action is too visually dominant (${backRect.width.toFixed(1)}px vs primary ${primaryRect.width.toFixed(1)}px)`);
                    }
                }
            });

            if (document.body.classList.contains('play-flow-route')) {
                if (window.scrollY > 1) {
                    issues.push(`play workflow starts scrolled down (${window.scrollY.toFixed(1)}px)`);
                }

                const footer = document.querySelector('.footer');
                if (footer && visible(footer)) {
                    issues.push('global footer is visible inside a play workflow route');
                }

                const siteMenu = document.querySelector('.site-menu');
                if (siteMenu && visible(siteMenu)) {
                    issues.push('compact header menu is visible inside a play workflow route');
                }

                const main = document.querySelector('main');
                if (main) {
                    const mainMarginBottom = parseFloat(getComputedStyle(main).marginBottom) || 0;
                    if (mainMarginBottom > 1) {
                        issues.push(`play workflow reserves ${mainMarginBottom.toFixed(1)}px of bottom space after hiding the footer`);
                    }
                }
            }

            const overflow = Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - viewportWidth;
            if (overflow > 1) {
                issues.push(`horizontal overflow (${overflow.toFixed(1)}px)`);
            }

            return issues;
        }
        """;

    internal static async Task ExpectActionStackDockedInViewportAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            """
            selector => {
                const element = document.querySelector(selector);
                if (!element?.classList.contains('flow-action-stack--docked')) return false;
                if (document.documentElement.classList.contains('play-panel-transitioning')) return false;
                const rect = element.getBoundingClientRect();
                return element?.classList.contains('flow-action-stack--docked')
                    && !element.classList.contains('flow-action-stack--dock-entering')
                    && element.getAnimations({ subtree: true })
                        .every(animation => animation.playState !== 'running')
                    && rect.top >= -1
                    && rect.bottom <= window.innerHeight + 1;
            }
            """,
            selector);

        var rect = await ReadElementRectAsync(page, selector);

        Assert.True(rect.Left >= -1, $"{selector} overflows left: {rect.Left}");
        Assert.True(rect.Right <= rect.ViewportWidth + 1, $"{selector} overflows right: {rect.Right} > {rect.ViewportWidth}");
        Assert.True(rect.Bottom <= rect.ViewportHeight + 1, $"{selector} bottom is below viewport: {rect.Bottom} > {rect.ViewportHeight}");
        Assert.True(rect.Top >= 0, $"{selector} top is above viewport: {rect.Top}");
        Assert.True(rect.Width > 0, $"{selector} has no rendered width.");
        Assert.True(rect.Height > 0, $"{selector} has no rendered height.");
    }

    internal static async Task ExpectDockPlaceholderMatchesVisibleStackAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync(
            """
            selector => {
                const element = document.querySelector(selector);
                if (!element?.classList.contains('flow-action-stack--docked')) return false;

                const placeholder = element.previousElementSibling?.classList.contains('flow-action-dock-placeholder')
                    ? element.previousElementSibling
                    : null;
                if (!placeholder || placeholder.hidden) return false;

                const actionRect = element.getBoundingClientRect();
                const placeholderRect = placeholder.getBoundingClientRect();
                return placeholderRect.height <= actionRect.height + 8;
            }
            """,
            selector);
    }

    internal static async Task WaitForManagedActionStacksSettledAsync(IPage page)
    {
        await page.WaitForFunctionAsync(
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
                const dockConditionMatches = stack => {
                    const requiredSelector = stack.getAttribute('data-flow-dock-when');
                    if (requiredSelector && !document.querySelector(requiredSelector)) return false;
                    const excludedSelector = stack.getAttribute('data-flow-dock-unless');
                    if (excludedSelector && document.querySelector(excludedSelector)) return false;
                    return true;
                };

                const managedStacks = Array.from(document.querySelectorAll('.flow-action-stack'))
                    .filter(stack => stack.hasAttribute('data-flow-dock') && dockConditionMatches(stack) && visible(stack));

                if (document.documentElement.classList.contains('play-panel-transitioning')) return false;
                if (managedStacks.length === 0) return true;

                return managedStacks.every(stack => {
                    if (stack.classList.contains('flow-action-stack--dock-entering')) {
                        return false;
                    }

                    const rect = stack.getBoundingClientRect();
                    if (stack.classList.contains('flow-action-stack--docked')) {
                        return rect.top >= -1 && rect.bottom <= window.innerHeight + 1;
                    }

                    return rect.top >= -1 && rect.bottom <= window.innerHeight + 1;
                });
            }
            """);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    internal static async Task ExpectActionStackInViewportAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        var rect = await ReadElementRectAsync(page, selector);

        Assert.True(rect.Left >= -1, $"{selector} overflows left: {rect.Left}");
        Assert.True(rect.Right <= rect.ViewportWidth + 1, $"{selector} overflows right: {rect.Right} > {rect.ViewportWidth}");
        Assert.True(rect.Bottom <= rect.ViewportHeight + 1, $"{selector} bottom is below viewport: {rect.Bottom} > {rect.ViewportHeight}");
        Assert.True(rect.Top >= 0, $"{selector} top is above viewport: {rect.Top}");
        Assert.True(rect.Width > 0, $"{selector} has no rendered width.");
        Assert.True(rect.Height > 0, $"{selector} has no rendered height.");
    }

    internal static async Task<ElementRect> ReadElementRectAsync(IPage page, string selector)
    {
        return await page.Locator(selector).EvaluateAsync<ElementRect>(
            """
            element => {
                const rect = element.getBoundingClientRect();
                return {
                    Left: rect.left,
                    Right: rect.right,
                    Top: rect.top,
                    Bottom: rect.bottom,
                    Width: rect.width,
                    Height: rect.height,
                    ViewportWidth: window.innerWidth,
                    ViewportHeight: window.innerHeight
                };
            }
            """);
    }

    internal static async Task<FlowActionChildLayout> ReadFlowActionChildLayoutAsync(IPage page, string selector)
    {
        return await page.Locator(selector).EvaluateAsync<FlowActionChildLayout>(
            """
            element => {
                const actions = Array.from(element.querySelectorAll(':scope > .flow-action'))
                    .map(action => {
                        const rect = action.getBoundingClientRect();
                        const style = getComputedStyle(action);
                        return {
                            Left: rect.left,
                            Right: rect.right,
                            Height: rect.height,
                            Width: rect.width,
                            Visible: rect.width > 0
                                && rect.height > 0
                                && style.display !== 'none'
                                && style.visibility !== 'hidden'
                        };
                    })
                    .filter(action => action.Visible)
                    .sort((a, b) => a.Left - b.Left);

                let maxGap = 0;
                for (let index = 1; index < actions.length; index += 1) {
                    maxGap = Math.max(maxGap, actions[index].Left - actions[index - 1].Right);
                }
                const heights = actions.map(action => action.Height);
                const maxHeight = heights.length ? Math.max(...heights) : 0;
                const minHeight = heights.length ? Math.min(...heights) : 0;

                return {
                    Count: actions.length,
                    MaxGap: maxGap,
                    MaxHeight: maxHeight,
                    MaxHeightDelta: maxHeight - minHeight
                };
            }
            """);
    }

    internal static async Task ExpectNoHorizontalOverflowAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<double>(
            "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - window.innerWidth");
        Assert.True(overflow <= 1, $"Page has {overflow}px horizontal overflow.");
    }

    internal static async Task<bool> HasDockClassAsync(IPage page, string selector)
    {
        return await page.Locator(selector).EvaluateAsync<bool>(
            "element => element.classList.contains('flow-action-stack--docked')");
    }

    internal static async Task FillBioageStepOneAsync(IPage page, string bloodDrawDate)
    {
        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await page.Locator("#blood-draw-date").FillAsync(bloodDrawDate);
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");
    }

    internal static async Task FillPhenoBiomarkersAsync(IPage page)
    {
        await SetFormValuesAsync(
            page,
            new Dictionary<string, string>
            {
                ["wbc"] = "6.54",
                ["wbcUnit"] = "1",
                ["lymphocyte"] = "28.6",
                ["lymphocyteUnit"] = "1",
                ["mcv"] = "92",
                ["mcvUnit"] = "1",
                ["rcdw"] = "13.4",
                ["rcdwUnit"] = "1",
                ["albumin"] = "45",
                ["albuminUnit"] = "1",
                ["ap"] = "83",
                ["apUnit"] = "1",
                ["creatinine"] = "72",
                ["creatinineUnit"] = "1",
                ["glucose"] = "5",
                ["glucoseUnit"] = "1",
                ["crp"] = "1.35",
                ["crpUnit"] = "10"
            });
    }

    internal static async Task FillBortzBiomarkersAsync(IPage page)
    {
        await SetFormValuesAsync(
            page,
            new Dictionary<string, string>
            {
                ["wbc"] = "6.54",
                ["wbcUnit"] = "1",
                ["lymphocyte_percentage"] = "28.6",
                ["lymphocyte_percentageUnit"] = "1",
                ["neutrophil_percentage"] = "64.2",
                ["neutrophil_percentageUnit"] = "1",
                ["monocyte_percentage"] = "7.2",
                ["monocyte_percentageUnit"] = "1",
                ["rbc"] = "4.5",
                ["rbcUnit"] = "1",
                ["mcv"] = "92",
                ["mcvUnit"] = "1",
                ["mch"] = "31.8",
                ["mchUnit"] = "1",
                ["rdw"] = "13.4",
                ["rdwUnit"] = "1",
                ["albumin"] = "45",
                ["albuminUnit"] = "1",
                ["alt"] = "22",
                ["altUnit"] = "1",
                ["alp"] = "83",
                ["alpUnit"] = "1",
                ["ggt"] = "29",
                ["ggtUnit"] = "1",
                ["urea"] = "5.4",
                ["ureaUnit"] = "1",
                ["creatinine"] = "72",
                ["creatinineUnit"] = "1",
                ["cystatin_c"] = "0.9",
                ["cystatin_cUnit"] = "1",
                ["glucose"] = "5",
                ["glucoseUnit"] = "1",
                ["hba1c"] = "35.5",
                ["hba1cUnit"] = "1",
                ["cholesterol"] = "5.6",
                ["cholesterolUnit"] = "1",
                ["apoa1"] = "1.52",
                ["apoa1Unit"] = "1",
                ["crp"] = "1.35",
                ["crpUnit"] = "1",
                ["shbg"] = "45.6",
                ["shbgUnit"] = "1",
                ["vitamin_d"] = "50",
                ["vitamin_dUnit"] = "1"
            });
    }

    internal static async Task SetFormValuesAsync(IPage page, Dictionary<string, string> values)
    {
        await page.EvaluateAsync(
            """
            values => {
                for (const [id, value] of Object.entries(values)) {
                    const element = document.getElementById(id);
                    if (!element) throw new Error(`Missing bioage field: ${id}`);
                    element.value = value;
                    element.dispatchEvent(new Event('input', { bubbles: true }));
                    element.dispatchEvent(new Event('change', { bubbles: true }));
                }
            }
            """,
            values);
    }

    internal sealed class ElementRect
    {
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class BioageStepOneLayout
    {
        public ElementRect Action { get; set; } = new();
        public ElementRect DateOfBirth { get; set; } = new();
        public ElementRect BloodDraw { get; set; } = new();
        public ElementRect Day { get; set; } = new();
        public ElementRect BloodDrawInput { get; set; } = new();
        public ElementRect Privacy { get; set; } = new();
        public ElementRect Title { get; set; } = new();
        public ElementRect Instructions { get; set; } = new();
        public ElementRect? LabPanel { get; set; }
        public double ScrollY { get; set; }
        public double ViewportHeight { get; set; }

        public override string ToString()
        {
            var labPanel = LabPanel is null ? "hidden" : $"{LabPanel.Top:0.0}/{LabPanel.Bottom:0.0}";
            return $"scrollY={ScrollY:0.0}, actions top/bottom={Action.Top:0.0}/{Action.Bottom:0.0}, title bottom={Title.Bottom:0.0}, instructions bottom={Instructions.Bottom:0.0}, lab panel={labPanel}, dob bottom={DateOfBirth.Bottom:0.0}, day bottom={Day.Bottom:0.0}, blood panel/input bottom={BloodDraw.Bottom:0.0}/{BloodDrawInput.Bottom:0.0}, privacy bottom={Privacy.Bottom:0.0}, viewport={ViewportHeight:0.0}";
        }
    }

    internal sealed class FlowActionChildLayout
    {
        public int Count { get; set; }
        public double MaxGap { get; set; }
        public double MaxHeight { get; set; }
        public double MaxHeightDelta { get; set; }
    }

    internal sealed class ApplyStageTransitionScrollState
    {
        public double ScrollY { get; set; }
        public ScrollIntoViewCall[] Calls { get; set; } = [];
    }

    internal sealed class ScrollIntoViewCall
    {
        public string Tag { get; set; } = "";
        public string Id { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Text { get; set; } = "";
    }

    internal sealed class WorkflowTitleLayout
    {
        public string Text { get; set; } = "";
        public double FontSize { get; set; }
        public double LineHeight { get; set; }
        public double Bottom { get; set; }
    }

    internal sealed class EditProfileInitialState
    {
        public bool SubmitDisabled { get; set; }
        public bool SubmitVisible { get; set; }
        public bool ActionsDocked { get; set; }
        public bool BodyDockActive { get; set; }
        public int CoveredVisibleFieldCount { get; set; }
        public bool BackVisible { get; set; }
        public double BackCenterDelta { get; set; }
        public double BackWidth { get; set; }
        public double PlaceholderHeight { get; set; }
        public double ActionHeight { get; set; }
        public string TempAthlete { get; set; } = "";
        public double ActionBottom { get; set; }
        public double DockTop { get; set; }
        public double PersonalLinkBottom { get; set; }
        public double MediaContactBottom { get; set; }
        public double WhyBottom { get; set; }
        public double ViewportHeight { get; set; }
        public string Division { get; set; } = "";
    }

    internal sealed class EditProfileMissingOriginalRestoreState
    {
        public string PersonalLink { get; set; } = "";
        public string MediaContact { get; set; } = "";
        public string Why { get; set; } = "";
        public bool HasUndefinedText { get; set; }
        public bool SubmitDisabled { get; set; }
        public string TempAthlete { get; set; } = "";
        public int RestoreButtonVisibleCount { get; set; }
        public bool ActionsDocked { get; set; }
        public double ActionBottom { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class EditProfileFirstViewportLayout
    {
        public double TitleFontSize { get; set; }
        public ElementRect Title { get; set; } = new();
        public ElementRect Picture { get; set; } = new();
        public ElementRect PictureButton { get; set; } = new();
        public string OptionsAos { get; set; } = "";
        public double ViewportHeight { get; set; }
    }

    internal sealed class ProofUploadFirstViewportLayout
    {
        public double TitleFontSize { get; set; }
        public ElementRect Title { get; set; } = new();
        public ElementRect Illustration { get; set; } = new();
        public ElementRect UploadButton { get; set; } = new();
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public double ScreenWidth { get; set; }
        public double ScreenHeight { get; set; }
        public bool LandscapeMediaMatches { get; set; }
        public bool CompactLandscapeMediaMatches { get; set; }

        public override string ToString()
            => $"viewport={ViewportWidth}x{ViewportHeight}, screen={ScreenWidth}x{ScreenHeight}, "
                + $"landscape={LandscapeMediaMatches}, compactLandscape={CompactLandscapeMediaMatches}";
    }

    internal sealed class ProofUploadActionLayout
    {
        public double UploadTop { get; set; }
        public double UploadBottom { get; set; }
        public double UploadLeft { get; set; }
        public double UploadRight { get; set; }
        public double ChecklistTop { get; set; }
        public double ChecklistBottom { get; set; }
        public bool ChecklistVisible { get; set; }
        public double DockTop { get; set; }
        public double DockBottom { get; set; }
        public bool ActionsDocked { get; set; }
        public bool SubmitDisabled { get; set; }
        public bool SubmitVisible { get; set; }
        public bool BackVisible { get; set; }
        public double BackCenterDelta { get; set; }
        public double BackWidth { get; set; }
        public double BackHeight { get; set; }
        public double BackTop { get; set; }
        public double BackBottom { get; set; }
        public double BackLeft { get; set; }
        public double BackRight { get; set; }
        public double PlaceholderHeight { get; set; }
        public double ActionHeight { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class ApplyFirstStageDetailsLayout
    {
        public double DetailsTop { get; set; }
        public double DetailsBottom { get; set; }
        public bool DetailsVisible { get; set; }
        public double DockTop { get; set; }
        public double DockBottom { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class DockedActionHierarchy
    {
        public double PrimaryLeft { get; set; }
        public double PrimaryRight { get; set; }
        public double PrimaryWidth { get; set; }
        public double PrimaryCenter { get; set; }
        public double BackLeft { get; set; }
        public double BackRight { get; set; }
        public double BackWidth { get; set; }
        public double ViewportCenter { get; set; }
    }

    internal sealed class PlayStartWordmarkState
    {
        public string HeaderBackground { get; set; } = "";
        public string Text { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public double WordmarkTop { get; set; }
        public double WordmarkBottom { get; set; }
        public string WordmarkBackgroundColor { get; set; } = "";
        public string WordmarkBackgroundImage { get; set; } = "";
        public string WordmarkBorderRadius { get; set; } = "";
        public string WordmarkBoxShadow { get; set; } = "";
        public string WordmarkColor { get; set; } = "";
        public double HeroLeft { get; set; }
        public double HeroRight { get; set; }
        public double HeroWidth { get; set; }
        public double HeaderBottom { get; set; }
        public double LogoWidth { get; set; }
        public double LogoHeight { get; set; }
        public string LogoDisplay { get; set; } = "";
        public double WatermarkWidth { get; set; }
        public double WatermarkHeight { get; set; }
        public double WatermarkTop { get; set; }
        public double WatermarkBottom { get; set; }
        public string WatermarkDisplay { get; set; } = "";
        public string WatermarkOpacity { get; set; } = "";
        public string WatermarkFilter { get; set; } = "";
        public string WatermarkMixBlendMode { get; set; } = "";
        public string WatermarkPosition { get; set; } = "";
        public string WatermarkPointerEvents { get; set; } = "";
        public string WatermarkSource { get; set; } = "";
        public string StickyHeaderDisplay { get; set; } = "";
        public string StickyHeaderOpacity { get; set; } = "";
        public string StickyHeaderPointerEvents { get; set; } = "";
        public string BannerTextDisplay { get; set; } = "";
        public string TaglineDisplay { get; set; } = "";
        public double MainTop { get; set; }
        public string HeaderBackgroundImage { get; set; } = "";
        public string HeaderBoxShadow { get; set; } = "";
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public double ScrollWidth { get; set; }
        public string PanelBeforeBackgroundColor { get; set; } = "";
        public string PanelBeforeBackgroundImage { get; set; } = "";
        public string PanelBeforePosition { get; set; } = "";
        public string PanelBeforeZIndex { get; set; } = "";
        public string HeroBackgroundColor { get; set; } = "";
        public string HeroBackgroundImage { get; set; } = "";
        public string BodyBackground { get; set; } = "";
        public string BodyBackgroundColor { get; set; } = "";
        public string BodyBackgroundImage { get; set; } = "";
        public string MainBackgroundColor { get; set; } = "";
        public string MainBackgroundImage { get; set; } = "";
        public double ActionStackTop { get; set; }
        public string ActionStackBackground { get; set; } = "";
        public string ActionStackBorderTopWidth { get; set; } = "";
        public bool HasImageCrop { get; set; }
        public bool HasHeroImage { get; set; }
        public bool HasJustTrackItAsset { get; set; }
    }

    internal sealed class PlayStartFirstPaintState
    {
        public string WordmarkText { get; set; } = "";
        public double WordmarkOpacity { get; set; }
        public double WordmarkTop { get; set; }
        public double WordmarkBottom { get; set; }
        public double ActionOpacity { get; set; }
        public double ActionTop { get; set; }
        public double ActionBottom { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class PlayStartBackDockState
    {
        public bool ActionDocked { get; set; }
        public double ActionTop { get; set; }
        public double ActionRight { get; set; }
        public double ActionBottom { get; set; }
        public double ActionLeft { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public string PanelClass { get; set; } = "";
        public string PanelTransform { get; set; } = "";
    }

    internal sealed class ModernContainingBlockDockState
    {
        public bool ActionDocked { get; set; }
        public double ActionBottom { get; set; }
        public double ActionLeft { get; set; }
        public double ActionRight { get; set; }
        public double ViewportWidth { get; set; }
        public double ViewportHeight { get; set; }
        public bool ActionParentIsBody { get; set; }
        public string PanelTransform { get; set; } = "";
        public string PanelTranslate { get; set; } = "";
        public string PanelScale { get; set; } = "";
        public string PanelRotate { get; set; } = "";
        public string PanelContain { get; set; } = "";
        public string PanelContainerType { get; set; } = "";
        public string PanelContentVisibility { get; set; } = "";
        public string PanelWillChange { get; set; } = "";
    }

    internal sealed class TransformedFormDockState
    {
        public string FormTransform { get; set; } = "";
        public bool ActionsParentIsBody { get; set; }
    }

    internal sealed class StickyProgressState
    {
        public string Opacity { get; set; } = "";
        public string PointerEvents { get; set; } = "";
        public string AriaHidden { get; set; } = "";
        public bool Inert { get; set; }
        public bool HasInertAttribute { get; set; }
        public string StickyAriaHidden { get; set; } = "";
        public string StickyRole { get; set; } = "";
        public string StickyAriaLive { get; set; } = "";
    }

    internal sealed class DashboardDiscountLayoutState
    {
        public bool DiscountVisible { get; set; }
        public bool DiscountInsideActionMenu { get; set; }
        public double DiscountTop { get; set; }
        public double PictureBottom { get; set; }
        public int VisibleLineCount { get; set; }
        public string[] CompactTexts { get; set; } = [];
        public double[] TextLefts { get; set; } = [];
        public double[] IconWidths { get; set; } = [];
        public double[] IconHeights { get; set; } = [];
    }

    internal sealed class InlineDashboardActionState
    {
        public bool ActionDocked { get; set; }
        public double PictureBottom { get; set; }
        public double DiscountTop { get; set; }
        public double DiscountBottom { get; set; }
        public double ActionTop { get; set; }
        public double ActionBottom { get; set; }
        public double FirstActionTop { get; set; }
        public double SecondActionTop { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class BioageResultDockLayout
    {
        public double ResultTop { get; set; }
        public double ResultBottom { get; set; }
        public double ResultHeight { get; set; }
        public bool ResultVisible { get; set; }
        public double DockTop { get; set; }
        public double DockBottom { get; set; }
        public double DockHeight { get; set; }
        public bool DockVisible { get; set; }
        public double ScrollY { get; set; }
        public double MaxScrollY { get; set; }
        public double ViewportHeight { get; set; }
        public string RootScrollPaddingTop { get; set; } = "";
        public string DockHeightVariable { get; set; } = "";
        public string BodyClasses { get; set; } = "";
        public string HtmlClasses { get; set; } = "";
        public bool MissingElement { get; set; }

        public override string ToString()
        {
            return $"result top/bottom/height={ResultTop:0.0}/{ResultBottom:0.0}/{ResultHeight:0.0}, dock top/bottom/height={DockTop:0.0}/{DockBottom:0.0}/{DockHeight:0.0}, scrollY={ScrollY:0.0}, maxScrollY={MaxScrollY:0.0}, viewport={ViewportHeight:0.0}, scrollPaddingTop={RootScrollPaddingTop}, dockVar={DockHeightVariable}, body='{BodyClasses}', html='{HtmlClasses}'";
        }
    }

    internal sealed class JoinTrackActionGrouping
    {
        public bool AmateurInCard { get; set; }
        public bool ProInCard { get; set; }
        public bool AmateurInBackStack { get; set; }
        public bool ProInBackStack { get; set; }
        public bool MobileAmateurInBackStack { get; set; }
        public bool MobileProInBackStack { get; set; }
        public bool CardAmateurVisible { get; set; }
        public bool CardProVisible { get; set; }
        public bool MobileAmateurVisible { get; set; }
        public bool MobileProVisible { get; set; }
        public int BackStackActionCount { get; set; }
        public double DockHeight { get; set; }
        public double DockBottom { get; set; }
        public double BackRight { get; set; }
        public double AmateurLeft { get; set; }
        public double AmateurRight { get; set; }
        public double ProLeft { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class PlayWorkflowChromeState
    {
        public string FooterDisplay { get; set; } = "";
        public bool HasSiteMenu { get; set; }
        public bool HasSiteMenuToggle { get; set; }
        public bool HasSiteMenuPanel { get; set; }
    }

}
