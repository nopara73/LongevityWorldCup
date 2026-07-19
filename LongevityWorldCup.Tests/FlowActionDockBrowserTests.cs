using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FlowActionDockBrowserTests
{
    [Fact]
    public async Task HomePlayButton_NavigatesDirectlyToReadablePlayMenu()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Task.WhenAll(
            page.WaitForURLAsync("**/play", new PageWaitForURLOptions { WaitUntil = WaitUntilState.DOMContentLoaded }),
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync(".play-menu-wordmark");
        await page.WaitForTimeoutAsync(120);

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
    public async Task MobileWorkflowActionStacks_DockInsideTheFirstViewport(string path, string actionSelector)
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await ExpectActionStackDockedInViewportAsync(page, actionSelector);
        await ExpectNoHorizontalOverflowAsync(page);

        Assert.Empty(errors);
    }

    [Fact]
    public async Task MobileOnboardingActions_RemainDockedWhileEnteringContactEmail()
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
        await page.WaitForTimeoutAsync(100);

        Assert.True(
            await HasDockClassAsync(page, ".convergence-actions"),
            "The onboarding action menu should not disappear when a text field receives focus.");
        await ExpectActionStackInViewportAsync(page, ".convergence-actions");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task MobileBioageContinue_FirstTapAfterEditingAdvancesTheFlow()
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
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock && !document.querySelector('#newGameBtn')?.disabled");
        await page.Locator("#newGameBtn").ClickAsync();
        await page.WaitForURLAsync("**/join");
        await page.WaitForFunctionAsync("() => !document.querySelector('#joinTrackPanel')?.hidden");

        await page.Locator("#joinTrackBackBtn").ClickAsync();
        await page.WaitForURLAsync("**/play");
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

        Assert.True(state.ActionDocked, "Back to /play should keep the start actions in the bottom dock immediately.");
        Assert.InRange(Math.Abs(state.ActionBottom - state.ViewportHeight), 0, 1.1);
        Assert.True(state.ActionTop >= 0, $"The returned play actions start above the viewport: {state.ActionTop}.");
        Assert.True(state.ActionLeft <= 1, $"The returned play dock is inset from the left edge during transition: {state.ActionLeft}.");
        Assert.True(state.ActionRight >= state.ViewportWidth - 1, $"The returned play dock does not reach the right edge during transition: {state.ActionRight} < {state.ViewportWidth}.");
        Assert.Equal("none", state.PanelTransform);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DockedActionStack_PortalsWithoutMutatingContainingBlockAncestors()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, ".play-menu-actions");
        await page.Locator(".play-menu-actions").EvaluateAsync("element => element.remove()");
        await page.WaitForFunctionAsync(
            "() => !document.querySelector('.play-menu-actions') && !document.querySelector('.play-start-panel .flow-action-dock-placeholder')");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    [InlineData(1366, 768)]
    public async Task ReviewPage_KeepsHomeActionWithReviewPanel(
        int viewportWidth,
        int viewportHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/review", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForSelectorAsync(".application-review-copy.primary");
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow()");
        await page.WaitForTimeoutAsync(850);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow()");
        await ExpectActionStackInViewportAsync(page, ".application-review-actions");

        var titleRect = await ReadElementRectAsync(page, ".application-review-title");
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

        Assert.True(titleRect.Top >= 0,
            $"Review title starts above the viewport: {titleRect.Top}px.");
        Assert.Equal(0, await page.Locator(".application-review-visual, .application-review-panel img").CountAsync());
        Assert.True(primaryCopyRect.Bottom <= actionRect.Top - 8,
            $"Review primary message is too close to the Home action: copy bottom {primaryCopyRect.Bottom}px, action top {actionRect.Top}px.");
        Assert.NotEmpty(visibleSecondaryCopies);
        foreach (var secondaryBottom in visibleSecondaryCopies)
        {
            Assert.True(secondaryBottom <= actionRect.Top - 8,
                $"Review secondary copy is too close to the Home action: copy bottom {secondaryBottom}px, action top {actionRect.Top}px.");
        }
        Assert.False(actionDocked,
            "Review Home should stay inline on standard phone and desktop viewports instead of becoming a detached bottom dock.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task MobileAthleteSearch_KeepsDockVisibleWhileTyping()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
            ViewportSize = new ViewportSize { Width = 768, Height = 1024 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
        Assert.Empty(errors);
    }

    [Fact]
    public async Task StoredAthleteSelection_DoesNotFocusSearchInputOnEntry()
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

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForTimeoutAsync(650);

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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
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
        Assert.Equal("linear-gradient(120deg, #000000, #555555)", playStartState.HeaderBackground);
        Assert.Equal("none", playStartState.HeaderBackgroundImage);
        Assert.Contains("linear-gradient", playStartState.BodyBackgroundImage);
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

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(844, 390, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, true)]
    [InlineData(1366, 1024, true)]
    public async Task ProofUpload_LeavesUploadControlsInlineAndKeepsBackInlineBeforeSubmitIsReady(
        int viewportWidth,
        int viewportHeight,
        bool expectDesktopCenteredBack)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");
        await page.WaitForTimeoutAsync(900);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");

        Assert.False(await HasDockClassAsync(page, ".proof-upload-primary-action"));

        var initialLayout = await page.EvaluateAsync<ProofUploadActionLayout>(
            """
            () => {
                const upload = document.querySelector('#uploadProofButton');
                const actions = document.querySelector('.proof-upload-final-actions');
                const placeholder = actions.previousElementSibling?.classList.contains('flow-action-dock-placeholder')
                    ? actions.previousElementSibling
                    : null;
                const checklist = document.querySelector('#biomarker-checklist');
                const placeholderRect = placeholder?.getBoundingClientRect();
                const submit = document.querySelector('#submitButton');
                const back = actions.querySelector('.back-button');
                const uploadRect = upload.getBoundingClientRect();
                const actionsRect = actions.getBoundingClientRect();
                const submitRect = submit.getBoundingClientRect();
                const backRect = back.getBoundingClientRect();
                const checklistRect = checklist.getBoundingClientRect();
                const submitStyle = getComputedStyle(submit);
                const backStyle = getComputedStyle(back);
                const checklistStyle = getComputedStyle(checklist);
                return {
                    UploadTop: uploadRect.top,
                    UploadBottom: uploadRect.bottom,
                    UploadLeft: uploadRect.left,
                    UploadRight: uploadRect.right,
                    ChecklistTop: checklistRect.top,
                    ChecklistBottom: checklistRect.bottom,
                    ChecklistVisible: checklistStyle.display !== 'none'
                        && checklistStyle.visibility !== 'hidden'
                        && checklistRect.width > 0
                        && checklistRect.height > 0,
                    DockTop: actionsRect.top,
                    DockBottom: actionsRect.bottom,
                    ActionsDocked: actions.classList.contains('flow-action-stack--docked'),
                    SubmitDisabled: submit.disabled,
                    SubmitVisible: submitStyle.display !== 'none'
                        && submitStyle.visibility !== 'hidden'
                        && submitRect.width > 0
                        && submitRect.height > 0,
                    BackVisible: backStyle.display !== 'none'
                        && backStyle.visibility !== 'hidden'
                        && backRect.width > 0
                        && backRect.height > 0,
                    BackCenterDelta: Math.abs((backRect.left + (backRect.width / 2)) - (window.innerWidth / 2)),
                    BackWidth: backRect.width,
                    BackHeight: backRect.height,
                    BackTop: backRect.top,
                    BackBottom: backRect.bottom,
                    BackLeft: backRect.left,
                    BackRight: backRect.right,
                    PlaceholderHeight: placeholderRect?.height || 0,
                    ActionHeight: actionsRect.height,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        if (viewportWidth <= 760)
        {
            Assert.False(initialLayout.ActionsDocked, "Mobile proof upload should keep Back inline while the page is short and no proof exists.");
        }

        if (initialLayout.ActionsDocked)
        {
            await ExpectDockPlaceholderMatchesVisibleStackAsync(page, ".proof-upload-final-actions");
            Assert.True(initialLayout.UploadBottom <= initialLayout.DockTop - 20,
                $"Upload action should remain above the Back dock: upload bottom {initialLayout.UploadBottom}, dock top {initialLayout.DockTop}.");
            Assert.True(initialLayout.DockBottom <= initialLayout.ViewportHeight + 1,
                $"Proof upload Back dock should stay inside the viewport: {initialLayout.DockBottom} > {initialLayout.ViewportHeight}.");
        }
        else
        {
            if (viewportWidth >= 640 && viewportHeight <= 480)
            {
                Assert.True(initialLayout.BackRight <= initialLayout.UploadLeft - 8 || initialLayout.UploadRight <= initialLayout.BackLeft - 8,
                    $"Landscape proof upload Back should sit beside the upload choices, not overlap them: back {initialLayout.BackLeft}-{initialLayout.BackRight}, upload {initialLayout.UploadLeft}-{initialLayout.UploadRight}.");
            }
            else
            {
                Assert.True(initialLayout.UploadBottom <= initialLayout.BackTop - 8,
                    $"Inline proof upload Back should remain below, not over, the upload action: upload bottom {initialLayout.UploadBottom}, back top {initialLayout.BackTop}.");
            }

            Assert.True(initialLayout.BackBottom <= initialLayout.ViewportHeight - 8,
                $"Inline proof upload Back should stay in the first viewport when undocked: {initialLayout.BackBottom} > {initialLayout.ViewportHeight}.");
        }

        Assert.False(initialLayout.ChecklistVisible, "Proof tracker should stay out of the first proof-upload viewport until a proof exists.");
        Assert.True(initialLayout.ChecklistBottom <= initialLayout.DockTop - 20 || initialLayout.ChecklistTop >= initialLayout.DockBottom - 1,
            $"Proof tracker should not be half-covered by the dock: checklist {initialLayout.ChecklistTop}-{initialLayout.ChecklistBottom}, dock {initialLayout.DockTop}-{initialLayout.DockBottom}.");
        Assert.True(initialLayout.SubmitDisabled, "Proof upload Submit should start disabled until proof is attached.");
        Assert.False(initialLayout.SubmitVisible, "Disabled proof Submit should not appear as a fake primary action in the dock.");
        Assert.True(initialLayout.BackVisible, "Proof upload Back action should remain visible.");
        Assert.True(initialLayout.BackHeight <= 70,
            $"Proof upload Back action should stay button-height, not card-height: {initialLayout.BackHeight}px.");
        if (initialLayout.ActionsDocked)
        {
            Assert.True(initialLayout.PlaceholderHeight <= initialLayout.ActionHeight + 8,
                $"Proof upload dock placeholder should not reserve hidden Submit space: {initialLayout.PlaceholderHeight}px placeholder vs {initialLayout.ActionHeight}px dock.");
        }
        if (expectDesktopCenteredBack && initialLayout.ActionsDocked)
        {
            Assert.True(initialLayout.BackCenterDelta <= 3,
                $"Lone desktop proof Back action should be centered in the dock; center was off by {initialLayout.BackCenterDelta}px.");
            Assert.True(initialLayout.BackWidth <= 190,
                $"Lone desktop proof Back action should stay compact; width was {initialLayout.BackWidth}px.");
        }

        await page.EvaluateAsync(
            """
            () => {
                document.body.classList.add('proof-upload-has-proofs');
                document.querySelector('#submitButton').disabled = false;
                window.LwcFlowActionDock.refreshNow();
            }
            """);
        await page.WaitForTimeoutAsync(300);

        var submitVisible = await page.Locator("#submitButton").EvaluateAsync<bool>(
            """
            button => {
                const rect = button.getBoundingClientRect();
                const style = getComputedStyle(button);
                return style.display !== 'none'
                    && style.visibility !== 'hidden'
                    && rect.width > 0
                    && rect.height > 0;
            }
            """);
        Assert.True(submitVisible, "Enabled proof Submit should appear once a proof exists.");

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(844, 390, 32)]
    [InlineData(768, 1024, 38)]
    [InlineData(1280, 720, 36)]
    public async Task ProofUpload_FirstViewportUsesFormScaleTitleAndShowsUploadAction(
        int viewportWidth,
        int viewportHeight,
        double maxTitleFontSize)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('#character-title')?.textContent.trim() === 'Browser Test Athlete'
                && document.querySelector('.proof-upload-symbol .fa-file-medical')
                && document.querySelector('#uploadProofButton')?.getAttribute('data-listener') === 'true'
            """);

        var layout = await page.EvaluateAsync<ProofUploadFirstViewportLayout>(
            """
            () => {
                const title = document.querySelector('#character-title');
                const illustration = document.querySelector('.proof-upload-symbol');
                const uploadButton = document.querySelector('#uploadProofButton');
                const rectOf = element => {
                    const rect = element.getBoundingClientRect();
                    return {
                        Top: rect.top,
                        Bottom: rect.bottom,
                        Left: rect.left,
                        Right: rect.right,
                        Width: rect.width,
                        Height: rect.height
                    };
                };

                return {
                    TitleFontSize: parseFloat(getComputedStyle(title).fontSize),
                    Title: rectOf(title),
                    Illustration: rectOf(illustration),
                    UploadButton: rectOf(uploadButton),
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.True(layout.TitleFontSize <= maxTitleFontSize,
            $"Proof upload title fell back to oversized global h1 sizing: {layout.TitleFontSize}px > {maxTitleFontSize}px.");
        Assert.InRange(layout.Illustration.Height, 80, 104);
        Assert.Equal(0, await page.Locator(".proof-upload-visual img").CountAsync());
        Assert.True(layout.UploadButton.Bottom <= layout.ViewportHeight - 8,
            $"Upload proofs action is cut off in the first viewport: bottom {layout.UploadButton.Bottom}px, viewport {layout.ViewportHeight}px.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopPlayEntry_KeepsActionsInlineAtCommonViewport()
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
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            "selector => !document.querySelector(selector)?.classList.contains('flow-action-stack--docked')",
            ".play-menu-actions");

        await ExpectActionStackInViewportAsync(page, ".play-menu-actions");

        var inlineTail = await page.EvaluateAsync<double>(
            """
            () => {
                const main = document.querySelector('.play-hub-main');
                const actions = document.querySelector('.play-menu-actions');
                if (!main || !actions) return Number.POSITIVE_INFINITY;
                return main.getBoundingClientRect().bottom - actions.getBoundingClientRect().bottom;
            }
            """);

        Assert.True(inlineTail <= 16,
            $"Inline /play actions leave {inlineTail}px of trailing page background after the menu.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopPlayEntry_InlineActionsStaySameHeightAtDefaultBrowserSize(bool hasApplication)
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            hasApplication
                ? "window.localStorage.setItem('hasApplication', 'true');"
                : "window.localStorage.removeItem('hasApplication');");

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            "selector => !document.querySelector(selector)?.classList.contains('flow-action-stack--docked')",
            ".play-menu-actions");
        await ExpectActionStackInViewportAsync(page, ".play-menu-actions");

        var layout = await ReadFlowActionChildLayoutAsync(page, ".play-menu-actions");
        Assert.Equal(2, layout.Count);
        Assert.True(layout.MaxHeightDelta <= 1,
            $"Inline /play actions have mismatched heights by {layout.MaxHeightDelta}px.");
        Assert.True(layout.MaxHeight is >= 56 and <= 62,
            $"Inline /play actions have unexpected height: {layout.MaxHeight}px.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PlayHub_DoesNotExposeGlobalFooterBetweenHeroAndDockedActions()
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
            ViewportSize = new ViewportSize { Width = 500, Height = 1200 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, ".play-menu-actions");

        var footerDisplay = await page.Locator(".footer").EvaluateAsync<string>(
            "footer => getComputedStyle(footer).display");
        Assert.Equal("none", footerDisplay);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/join")]
    [InlineData("/select-athlete")]
    [InlineData("/dashboard")]
    [InlineData("/edit-profile")]
    [InlineData("/proofs")]
    public async Task PlayWorkflowPages_DoNotExposeGlobalFooter(string path)
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
            ViewportSize = new ViewportSize { Width = 480, Height = 1040 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Division: "Men's",
                Country: 'United States',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelector('.footer')");

        var footer = await page.Locator(".footer").EvaluateAsync<FooterVisibility>(
            """
            footer => {
                const style = getComputedStyle(footer);
                const visibleLinkCount = Array.from(footer.querySelectorAll('a')).filter(link => {
                    const rect = link.getBoundingClientRect();
                    const linkStyle = getComputedStyle(link);
                    return rect.width > 0
                        && rect.height > 0
                        && linkStyle.display !== 'none'
                        && linkStyle.visibility !== 'hidden';
                }).length;
                return {
                    Display: style.display,
                    VisibleLinkCount: visibleLinkCount
                };
            }
            """);

        Assert.Equal("none", footer.Display);
        Assert.Equal(0, footer.VisibleLinkCount);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(1280, 720, true)]
    [InlineData(1366, 768, true)]
    public async Task EditProfile_UnchangedAthleteWithStaleDraft_KeepsOnlyBackActionWithoutCoveringVisibleFields(
        int viewportWidth,
        int viewportHeight,
        bool expectDesktopCenteredBack)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
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
                Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.sessionStorage.setItem('tempAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => window.LwcFlowActionDock
                && document.querySelector('#divisionDisplaySelect')?.value === "Men's"
                && document.querySelector('.edit-profile-actions')
            """);
        await page.WaitForTimeoutAsync(900);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await WaitForManagedActionStacksSettledAsync(page);
        await ExpectActionStackInViewportAsync(page, ".edit-profile-actions");

        var state = await page.EvaluateAsync<EditProfileInitialState>(
            """
            () => {
                const actions = document.querySelector('.edit-profile-actions');
                const actionRect = actions.getBoundingClientRect();
                const placeholder = actions.previousElementSibling?.classList.contains('flow-action-dock-placeholder')
                    ? actions.previousElementSibling
                    : null;
                const placeholderRect = placeholder?.getBoundingClientRect();
                const submit = document.querySelector('#submitButton');
                const submitStyle = getComputedStyle(submit);
                const back = actions.querySelector('.back-button');
                const backRect = back.getBoundingClientRect();
                const backStyle = getComputedStyle(back);
                return {
                    SubmitDisabled: submit.disabled,
                    SubmitVisible: submitStyle.display !== 'none'
                        && submitStyle.visibility !== 'hidden'
                        && submit.getBoundingClientRect().width > 0
                        && submit.getBoundingClientRect().height > 0,
                    ActionsDocked: actions.classList.contains('flow-action-stack--docked'),
                    BodyDockActive: document.body.classList.contains('flow-action-dock-active'),
                    CoveredVisibleFieldCount: Array.from(document.querySelectorAll('#editOptionsGroup .inline-option-group'))
                        .filter(row => {
                            const rowRect = row.getBoundingClientRect();
                            const rowStyle = getComputedStyle(row);
                            const visible = rowStyle.display !== 'none'
                                && rowStyle.visibility !== 'hidden'
                                && rowRect.width > 0
                                && rowRect.height > 0;
                            return visible
                                && rowRect.top < actionRect.bottom - 1
                                && rowRect.bottom > actionRect.top + 1;
                        })
                        .length,
                    BackVisible: backStyle.display !== 'none'
                        && backStyle.visibility !== 'hidden'
                        && backRect.width > 0
                        && backRect.height > 0,
                    BackCenterDelta: Math.abs((backRect.left + (backRect.width / 2)) - (window.innerWidth / 2)),
                    BackWidth: backRect.width,
                    PlaceholderHeight: placeholderRect?.height || 0,
                    ActionHeight: actionRect.height,
                    TempAthlete: window.sessionStorage.getItem('tempAthlete') || '',
                    ActionBottom: actionRect.bottom,
                    ViewportHeight: window.innerHeight,
                    Division: document.querySelector('#divisionDisplaySelect').value
                };
            }
            """);

        Assert.Equal("Men's", state.Division);
        Assert.True(state.SubmitDisabled, "Unchanged edit profile should not present Submit as an available primary action.");
        Assert.False(state.SubmitVisible, "Disabled Submit should not appear as a fake primary action in the unchanged edit-profile dock.");
        Assert.True(state.BackVisible, "Back should remain visible while unchanged edit-profile actions are available.");
        Assert.Equal(0, state.CoveredVisibleFieldCount);
        Assert.True(state.ActionBottom <= state.ViewportHeight + 1,
            $"Unchanged edit-profile Back action should stay inside the viewport: {state.ActionBottom} > {state.ViewportHeight}.");
        if (state.ActionsDocked)
        {
            Assert.True(state.BodyDockActive, "Unchanged edit profile should reserve the bottom dock when Back docks.");
            Assert.True(state.PlaceholderHeight <= state.ActionHeight + 8,
                $"Unchanged edit-profile dock placeholder should not reserve hidden Submit space: {state.PlaceholderHeight}px placeholder vs {state.ActionHeight}px dock.");
        }

        if (expectDesktopCenteredBack && state.ActionsDocked)
        {
            Assert.True(state.BackCenterDelta <= 3,
                $"Lone desktop Back action should be centered in the dock; center was off by {state.BackCenterDelta}px.");
            Assert.True(state.BackWidth <= 190,
                $"Lone desktop Back action should stay compact; width was {state.BackWidth}px.");
        }
        Assert.Equal("", state.TempAthlete);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(768, 1024)]
    [InlineData(1366, 768)]
    public async Task EditProfile_UnchangedProfile_DoesNotLetFieldsEnterActionBand(int viewportWidth, int viewportHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Division: 'Open',
                Flag: 'Hungary',
                PersonalLink: 'https://example.test/browser-test-athlete',
                MediaContact: 'browser-test-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: '/assets/favicon-512x512.png',
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => window.LwcFlowActionDock
                && document.querySelector('#flagDisplayInput')?.value === 'Hungary'
                && document.querySelector('#submitButton')?.disabled === true
            """);
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await WaitForManagedActionStacksSettledAsync(page);
        await ExpectActionStackInViewportAsync(page, ".edit-profile-actions");

        var coveredRows = await page.EvaluateAsync<string[]>(
            """
            () => {
                const actions = document.querySelector('.edit-profile-actions');
                const actionRect = actions.getBoundingClientRect();
                const unsafeTop = actionRect.top;
                return Array.from(document.querySelectorAll('#editOptionsGroup .inline-option-group'))
                    .filter(row => {
                        const rowRect = row.getBoundingClientRect();
                        const rowStyle = getComputedStyle(row);
                        const visible = rowStyle.display !== 'none'
                            && rowStyle.visibility !== 'hidden'
                            && rowRect.width > 0
                            && rowRect.height > 0;
                        return visible
                            && rowRect.top < window.innerHeight
                            && rowRect.bottom > unsafeTop + 1;
                    })
                    .map(row => row.querySelector('input, select, textarea')?.id || '');
            }
            """);

        Assert.Empty(coveredRows);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1366, 768)]
    public async Task EditProfile_ChangedAthleteDocksSubmitActionsAfterEditing(int viewportWidth, int viewportHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(FlowAuditStateScript);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => window.LwcFlowActionDock
                && document.querySelector('#personalLinkInput')?.value === 'https://example.test/browser-test-athlete'
                && document.querySelector('#submitButton')?.disabled === true
            """);

        await page.Locator("#personalLinkInput").FillAsync("https://example.test/changed-profile");
        await page.Locator("#personalLinkInput").EvaluateAsync("input => input.blur()");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");

        await ExpectActionStackDockedInViewportAsync(page, ".edit-profile-actions");
        await page.WaitForFunctionAsync(
            """
            () => {
                const actions = document.querySelector('.edit-profile-actions');
                const personalLink = document.querySelector('#personalLinkInput')?.closest('.inline-option-group');
                const mediaContact = document.querySelector('#mediaContactInput')?.closest('.inline-option-group');
                const why = document.querySelector('#whyDisplayInput')?.closest('.inline-option-group');
                if (!actions || !personalLink || !mediaContact || !why || !actions.classList.contains('flow-action-stack--docked')) return false;

                const actionRect = actions.getBoundingClientRect();
                const personalLinkRect = personalLink.getBoundingClientRect();
                const mediaContactRect = mediaContact.getBoundingClientRect();
                const whyRect = why.getBoundingClientRect();
                return personalLinkRect.bottom <= actionRect.top - 8
                    && mediaContactRect.bottom <= actionRect.top - 8
                    && whyRect.bottom <= actionRect.top - 8;
            }
            """);

        var state = await page.EvaluateAsync<EditProfileInitialState>(
            """
            () => {
                const actions = document.querySelector('.edit-profile-actions');
                const actionRect = actions.getBoundingClientRect();
                const personalLinkRect = document.querySelector('#personalLinkInput').closest('.inline-option-group').getBoundingClientRect();
                const mediaContactRect = document.querySelector('#mediaContactInput').closest('.inline-option-group').getBoundingClientRect();
                const whyRect = document.querySelector('#whyDisplayInput').closest('.inline-option-group').getBoundingClientRect();
                return {
                    SubmitDisabled: document.querySelector('#submitButton').disabled,
                    ActionsDocked: actions.classList.contains('flow-action-stack--docked'),
                    TempAthlete: window.sessionStorage.getItem('tempAthlete') || '',
                    ActionBottom: actionRect.bottom,
                    DockTop: actionRect.top,
                    PersonalLinkBottom: personalLinkRect.bottom,
                    MediaContactBottom: mediaContactRect.bottom,
                    WhyBottom: whyRect.bottom,
                    ViewportHeight: window.innerHeight,
                    Division: document.querySelector('#divisionDisplaySelect').value
                };
            }
            """);

        Assert.False(state.SubmitDisabled, "A real edit should present Submit as an available primary action.");
        Assert.True(state.ActionsDocked, "Changed edit profile actions should dock once text entry has finished.");
        Assert.NotEmpty(state.TempAthlete);
        Assert.True(state.ActionBottom <= state.ViewportHeight + 1,
            $"Docked edit profile actions overflow the viewport: {state.ActionBottom} > {state.ViewportHeight}.");
        Assert.True(state.PersonalLinkBottom <= state.DockTop - 8,
            $"Edited personal-link row is covered by the dock: row bottom {state.PersonalLinkBottom}, dock top {state.DockTop}.");
        Assert.True(state.MediaContactBottom <= state.DockTop - 8,
            $"Next media-contact row is covered by the dock: row bottom {state.MediaContactBottom}, dock top {state.DockTop}.");
        Assert.True(state.WhyBottom <= state.DockTop - 8,
            $"Next why row is covered by the dock: row bottom {state.WhyBottom}, dock top {state.DockTop}.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task EditProfile_RestoringMissingOriginalProfileFieldsLeavesInputsEmpty()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const originalAthlete = {
                Name: 'Legacy Browser Athlete',
                DisplayName: 'Legacy Browser Athlete',
                Division: "Men's",
                Flag: 'United States',
                Country: 'United States',
                ProfilePic: '/assets/content-images/longevity-world-cup-silhouette.webp',
                ProfilePictureUrl: '/assets/content-images/longevity-world-cup-silhouette.webp',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
            };
            const draftAthlete = {
                ...originalAthlete,
                PersonalLink: 'https://example.test/legacy-draft',
                MediaContact: 'legacy-draft@example.test',
                Why: 'This temporary profile draft should be restorable.'
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(originalAthlete));
            window.sessionStorage.setItem('tempAthlete', JSON.stringify(draftAthlete));
            window.localStorage.setItem('selectedAthleteName', originalAthlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => window.LwcFlowActionDock
                && document.querySelector('#personalLinkInput')?.value === 'https://example.test/legacy-draft'
                && document.querySelector('#mediaContactInput')?.value === 'legacy-draft@example.test'
                && document.querySelector('#whyDisplayInput')?.value === 'This temporary profile draft should be restorable.'
                && document.querySelector('#submitButton')?.disabled === false
            """);

        await page.Locator("#restorePersonalLinkBtn").ClickAsync();
        await page.Locator("#restoreMediaContactBtn").ClickAsync();
        await page.Locator("#restoreWhyDisplayBtn").ClickAsync();
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");

        var state = await page.EvaluateAsync<EditProfileMissingOriginalRestoreState>(
            """
            () => {
                const actions = document.querySelector('.edit-profile-actions');
                const actionRect = actions.getBoundingClientRect();
                const personalLink = document.querySelector('#personalLinkInput');
                const mediaContact = document.querySelector('#mediaContactInput');
                const why = document.querySelector('#whyDisplayInput');
                const restoreButtons = [
                    document.querySelector('#restorePersonalLinkBtn'),
                    document.querySelector('#restoreMediaContactBtn'),
                    document.querySelector('#restoreWhyDisplayBtn')
                ];
                return {
                    PersonalLink: personalLink.value,
                    MediaContact: mediaContact.value,
                    Why: why.value,
                    HasUndefinedText: [personalLink.value, mediaContact.value, why.value]
                        .some(value => value === 'undefined'),
                    SubmitDisabled: document.querySelector('#submitButton').disabled,
                    TempAthlete: window.sessionStorage.getItem('tempAthlete') || '',
                    RestoreButtonVisibleCount: restoreButtons
                        .filter(button => {
                            const style = getComputedStyle(button);
                            const rect = button.getBoundingClientRect();
                            return style.display !== 'none'
                                && style.visibility !== 'hidden'
                                && rect.width > 0
                                && rect.height > 0;
                        })
                        .length,
                    ActionsDocked: actions.classList.contains('flow-action-stack--docked'),
                    ActionBottom: actionRect.bottom,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.Equal("", state.PersonalLink);
        Assert.Equal("", state.MediaContact);
        Assert.Equal("", state.Why);
        Assert.False(state.HasUndefinedText, "Restoring missing legacy fields should not put the literal text 'undefined' into visible inputs.");
        Assert.True(state.SubmitDisabled, "Restoring every draft-only profile field should return edit profile to the back-only state.");
        Assert.Equal("", state.TempAthlete);
        Assert.Equal(0, state.RestoreButtonVisibleCount);
        Assert.True(state.ActionsDocked, "Back should remain available in the bottom dock after all draft-only fields are restored.");
        Assert.True(state.ActionBottom <= state.ViewportHeight + 1,
            $"Back dock should stay inside the viewport after restoring fields: {state.ActionBottom} > {state.ViewportHeight}.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(768, 1024, 38, 220)]
    [InlineData(1280, 720, 36, 200)]
    public async Task EditProfile_FirstViewportUsesFormScaleTitleAndShowsPictureAction(
        int viewportWidth,
        int viewportHeight,
        double maxTitleFontSize,
        double minPictureHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Division: "Men's",
                Flag: 'Hungary',
                Country: 'Hungary',
                PersonalLink: 'https://example.test/browser-test-athlete',
                MediaContact: 'browser-test-athlete@example.test',
                Why: 'Testing the athlete navigation flow.',
                ProfilePic: '/assets/favicon-512x512.png',
                ProfilePictureUrl: '/assets/favicon-512x512.png',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [{ Date: '2026-06-19', Hba1cMmolMol: 35 }]
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.querySelector('#character-title')?.textContent.trim() === 'Browser Test Athlete'
                && document.querySelector('.edit-profile-visual img')?.complete
                && document.querySelector('#changeProfilePicButton')
            """);

        var layout = await page.EvaluateAsync<EditProfileFirstViewportLayout>(
            """
            () => {
                const title = document.querySelector('#character-title');
                const image = document.querySelector('.edit-profile-visual img');
                const pictureButton = document.querySelector('#changeProfilePicButton');
                const options = document.querySelector('#editOptionsGroup');
                const rectOf = element => {
                    const rect = element.getBoundingClientRect();
                    return {
                        Top: rect.top,
                        Bottom: rect.bottom,
                        Left: rect.left,
                        Right: rect.right,
                        Width: rect.width,
                        Height: rect.height
                    };
                };

                return {
                    TitleFontSize: parseFloat(getComputedStyle(title).fontSize),
                    Title: rectOf(title),
                    Picture: rectOf(image),
                    PictureButton: rectOf(pictureButton),
                    OptionsAos: options?.getAttribute('data-aos') || '',
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.True(layout.TitleFontSize <= maxTitleFontSize,
            $"Edit profile title fell back to oversized global h1 sizing: {layout.TitleFontSize}px > {maxTitleFontSize}px.");
        Assert.True(layout.Picture.Height >= minPictureHeight,
            $"Edit profile picture was over-compressed: {layout.Picture.Height}px < {minPictureHeight}px.");
        Assert.True(layout.PictureButton.Bottom <= layout.ViewportHeight - 8,
            $"Change profile picture action is cut off in the first viewport: bottom {layout.PictureButton.Bottom}px, viewport {layout.ViewportHeight}px.");
        Assert.Equal("fade", layout.OptionsAos);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task PlayWorkflowPages_DoNotExposeCompactHeaderMenu()
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
            ViewportSize = new ViewportSize { Width = 480, Height = 1040 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.body.classList.contains('play-flow-route')");

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
        var viewports = new (int Width, int Height)[]
        {
            (390, 844),
            (480, 1040),
            (844, 390),
            (932, 430),
            (768, 1024),
            (1280, 720),
            (1366, 768)
        };

        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var failures = new List<string>();

        foreach (var viewport in viewports)
        {
            foreach (var route in routes)
            {
                await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    BaseURL = app.BaseAddress.ToString(),
                    Locale = "en-US",
                    ViewportSize = new ViewportSize { Width = viewport.Width, Height = viewport.Height }
                });
                await BrowserTestApp.RouteExternalResourcesAsync(context);
                if (route != "/select-athlete")
                {
                    await context.AddInitScriptAsync(FlowAuditStateScript);
                }

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
                    failures.AddRange(issues.Select(issue => $"{route} @ {viewport.Width}x{viewport.Height}: {issue}"));
                    failures.AddRange(errors.Select(error => $"{route} @ {viewport.Width}x{viewport.Height}: console error: {error}"));
                }
                finally
                {
                    await page.CloseAsync();
                }
            }
        }

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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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

        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopJoinTrackActions_StayAttachedToTheirTrackCards()
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/join", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
    [InlineData("/pheno-age", "#lwcStepOneActions")]
    [InlineData("/apply?fake=1", ".convergence-actions")]
    public async Task DesktopDocks_UseCompactCommandBarHeight(string path, string actionSelector)
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
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, actionSelector);

        var rect = await ReadElementRectAsync(page, actionSelector);
        Assert.True(rect.Height <= 68, $"{actionSelector} dock is too tall: {rect.Height}px.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopApplyFirstStage_KeepsDetailsAndActionsVisible()
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1'");
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
    public async Task ApplyNextStageTransition_DoesNotForceViewportScroll(int viewportWidth, int viewportHeight)
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

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.body.dataset.convergenceStage === '1' && !document.getElementById('nextButton')?.disabled");
        await page.EvaluateAsync("() => window.scrollTo({ top: 0, left: 0, behavior: 'auto' })");

        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '2'");

        var transition = await page.EvaluateAsync<ApplyStageTransitionScrollState>(
            """
            () => ({
                ScrollY: window.scrollY,
                Calls: window.__lwcScrollIntoViewCalls || []
            })
            """);

        Assert.True(transition.ScrollY <= 1,
            $"Apply Next transition should not force the viewport to jump: scrollY={transition.ScrollY}.");
        Assert.Empty(transition.Calls);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1280, 720)]
    public async Task ApplyFirstStage_DoesNotShowDetailsPanelHalfCoveredByDock(int viewportWidth, int viewportHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/apply?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1'");
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

        Assert.True(layout.DetailsVisible, "Apply first stage should keep athlete details available in the document flow.");
        Assert.True(layout.DetailsBottom <= layout.DockTop - 20 || layout.DetailsTop >= layout.DockBottom - 1,
            $"Athlete details should not be half-covered by the action dock: details {layout.DetailsTop}-{layout.DetailsBottom}, dock {layout.DockTop}-{layout.DockBottom}, viewport {layout.ViewportHeight}.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task DesktopBioageStepActions_DockAsGroupedCommands(string path)
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
            ViewportSize = new ViewportSize { Width = 1366, Height = 768 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, "#lwcStepOneActions");

        var layout = await ReadFlowActionChildLayoutAsync(page, "#lwcStepOneActions");
        Assert.Equal(2, layout.Count);
        Assert.True(layout.MaxGap <= 24,
            $"Docked bioage commands are split apart by {layout.MaxGap}px instead of staying grouped.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", 1280, 720)]
    [InlineData("/bortz-age", 1280, 720)]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/bortz-age", 390, 844)]
    [InlineData("/pheno-age", 430, 932)]
    [InlineData("/bortz-age", 430, 932)]
    public async Task BioageStepOne_DoesNotHalfCoverDatePanelsWithActions(string path, int viewportWidth, int viewportHeight)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await page.WaitForTimeoutAsync(750);

        var layout = await page.EvaluateAsync<BioageStepOneLayout>(
            """
            () => {
                const actions = document.querySelector('#lwcStepOneActions');
                const dob = document.querySelector('#dobFieldset');
                const bloodDraw = document.querySelector('#lwc-step-1 fieldset:nth-of-type(2)');
                const day = document.querySelector('#dob-day');
                const bloodDrawInput = document.querySelector('#blood-draw-date');
                const privacy = document.querySelector('#privacyNote');
                const title = document.querySelector('#mainPageTitleH2');
                const instructions = document.querySelector('#mainInstructions');
                const labPanel = document.querySelector('.lab-access-panel:not([hidden])');
                const rectOf = element => {
                    const rect = element.getBoundingClientRect();
                    return {
                        Top: rect.top,
                        Bottom: rect.bottom,
                        Left: rect.left,
                        Right: rect.right,
                        Width: rect.width,
                        Height: rect.height
                    };
                };

                return {
                    Action: rectOf(actions),
                    DateOfBirth: rectOf(dob),
                    BloodDraw: rectOf(bloodDraw),
                    Day: rectOf(day),
                    BloodDrawInput: rectOf(bloodDrawInput),
                    Privacy: rectOf(privacy),
                    Title: rectOf(title),
                    Instructions: rectOf(instructions),
                    LabPanel: labPanel ? rectOf(labPanel) : null,
                    ScrollY: window.scrollY,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.True(layout.ScrollY <= 1,
            $"Bioage first load should not auto-scroll the header out of view. {layout}");
        Assert.True(layout.Action.Bottom <= layout.ViewportHeight + 1,
            $"Bioage step actions are below the viewport: {layout.Action.Bottom} > {layout.ViewportHeight}. {layout}");
        Assert.True(layout.Day.Bottom <= layout.Action.Top - 6,
            $"Day selector is covered by actions. {layout}");
        if (layout.BloodDraw.Bottom <= layout.Action.Top - 6)
        {
            Assert.True(layout.BloodDrawInput.Bottom <= layout.Action.Top - 6,
                $"Blood draw input is covered by actions. {layout}");
        }
        Assert.True(layout.Privacy.Bottom <= layout.Action.Top - 6,
            $"Privacy note is covered by actions. {layout}");
        Assert.True(layout.BloodDraw.Bottom <= layout.Action.Top - 6 || layout.BloodDraw.Top >= layout.Action.Bottom - 1,
            $"Blood draw panel should not be half-covered by actions. {layout}");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DesktopTwoActionDock_MakesBackSecondaryAndKeepsPrimaryCentered()
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
            // Keep the desktop width while constraining height enough to exercise the dock.
            // At 768px the compact form and both actions now fit fully inline by design.
            ViewportSize = new ViewportSize { Width = 1366, Height = 650 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, "#lwcStepOneActions");

        var layout = await page.Locator("#lwcStepOneActions").EvaluateAsync<DockedActionHierarchy>(
            """
            element => {
                const primary = element.querySelector('#lwcToStep2Btn');
                const back = element.querySelector('.back-button');
                const primaryRect = primary.getBoundingClientRect();
                const backRect = back.getBoundingClientRect();
                return {
                    PrimaryLeft: primaryRect.left,
                    PrimaryRight: primaryRect.right,
                    PrimaryWidth: primaryRect.width,
                    PrimaryCenter: primaryRect.left + (primaryRect.width / 2),
                    BackLeft: backRect.left,
                    BackRight: backRect.right,
                    BackWidth: backRect.width,
                    ViewportCenter: window.innerWidth / 2
                };
            }
            """);

        Assert.True(layout.BackRight < layout.PrimaryLeft,
            $"Back should sit to the left of the primary action. Back right {layout.BackRight}, primary left {layout.PrimaryLeft}.");
        Assert.True(layout.BackWidth <= layout.PrimaryWidth * 0.7,
            $"Back should be visibly secondary. Back width {layout.BackWidth}, primary width {layout.PrimaryWidth}.");
        Assert.True(Math.Abs(layout.PrimaryCenter - layout.ViewportCenter) <= 16,
            $"Primary action should stay centered. Primary center {layout.PrimaryCenter}, viewport center {layout.ViewportCenter}.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", "#phenoAgeForm", "#lwcStepOneActions")]
    [InlineData("/bortz-age", "#bortzAgeForm", "#lwcStepOneActions")]
    public async Task DesktopBioageStepActions_PortalWithoutRewritingTransformedForm(
        string path,
        string formSelector,
        string actionSelector)
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
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.Locator(formSelector).EvaluateAsync("form => form.style.transform = 'translateZ(0)'");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");

        await ExpectActionStackDockedInViewportAsync(page, actionSelector);

        var state = await page.EvaluateAsync<TransformedFormDockState>(
            """
            selectors => {
                const [formSelector, actionSelector] = selectors.split('|');
                const form = document.querySelector(formSelector);
                const actions = document.querySelector(actionSelector);
                return {
                    FormTransform: getComputedStyle(form).transform,
                    ActionsParentIsBody: actions.parentElement === document.body
                };
            }
            """,
            $"{formSelector}|{actionSelector}");
        Assert.NotEqual("none", state.FormTransform);
        Assert.True(state.ActionsParentIsBody);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(1366, 768, 340)]
    [InlineData(1280, 720, 288)]
    public async Task DesktopSelectAthlete_KeepsPictureAndInputAboveDockAtCommonViewport(
        int viewportWidth,
        int viewportHeight,
        double minPictureWidth)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await ExpectActionStackDockedInViewportAsync(page, ".play-athlete-actions");

        var pictureRect = await ReadElementRectAsync(page, "#athleteSelectionPicture");
        var inputRect = await ReadElementRectAsync(page, "#playAthleteInput");
        var actionsRect = await ReadElementRectAsync(page, ".play-athlete-actions");

        Assert.True(pictureRect.Width >= minPictureWidth,
            $"Athlete picture is too small for the available desktop space: {pictureRect.Width}px < {minPictureWidth}px.");
        Assert.True(pictureRect.Bottom <= actionsRect.Top - 16,
            $"Athlete picture overlaps the dock: picture bottom {pictureRect.Bottom}, dock top {actionsRect.Top}.");
        Assert.True(inputRect.Bottom <= actionsRect.Top - 16,
            $"Athlete input overlaps the dock: input bottom {inputRect.Bottom}, dock top {actionsRect.Top}.");
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(390, 844, 245, false)]
    [InlineData(390, 844, 220, true)]
    [InlineData(1366, 768, 68, false)]
    [InlineData(1366, 768, 68, true)]
    [InlineData(1280, 720, 68, false)]
    [InlineData(1280, 720, 68, true)]
    public async Task DashboardActions_DockAsCompactCommandGridWhenTheyWouldOverflow(
        int viewportWidth,
        int viewportHeight,
        double maxDockHeight,
        bool isPro)
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
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var biomarkersJson = isPro
            ? "[{ Date: '2026-06-19', Hba1cMmolMol: 35 }]"
            : "[]";
        await context.AddInitScriptAsync(
            $$"""
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                PersonalLink: 'https://example.test/browser-test-athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: {{biomarkersJson}}
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            window.localStorage.setItem('gmaHasPerfectGuess', '1');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await ExpectActionStackDockedInViewportAsync(page, ".play-dashboard-actions");

        var pictureRect = await ReadElementRectAsync(page, "#athleteDashboardPicture");
        var actionsRect = await ReadElementRectAsync(page, ".play-dashboard-actions");

        Assert.True(actionsRect.Height <= maxDockHeight,
            $".play-dashboard-actions dock is too tall: {actionsRect.Height}px.");
        var compactLabels = await page.Locator("#athleteDashboardActions .flow-action[data-flow-dock-label]").CountAsync();
        Assert.Equal(0, compactLabels);

        var actionIconSizes = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action > i'))
                .map(icon => {
                    const style = getComputedStyle(icon);
                    return `${style.width}|${style.fontSize}|${style.lineHeight}`;
                })
            """);
        Assert.Equal(5, actionIconSizes.Length);
        Assert.Single(actionIconSizes.Distinct());

        if (viewportWidth < 960)
        {
            var visibleActionIcons = await page.EvaluateAsync<int>(
                """
                () => Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action > i'))
                    .filter(icon => getComputedStyle(icon).display !== 'none').length
                """);
            Assert.Equal(5, visibleActionIcons);

            var longevityLabelUsesOneLine = await page.EvaluateAsync<bool>(
                """
                () => {
                    const label = Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action__label'))
                        .find(candidate => candidate.textContent.trim() === 'Longevitymaxxing');
                    if (!label) return false;
                    const lineHeight = parseFloat(getComputedStyle(label).lineHeight);
                    return label.getBoundingClientRect().height <= lineHeight * 1.25;
                }
                """);
            Assert.True(longevityLabelUsesOneLine,
                "The Longevitymaxxing action should not split inside its name in the compact dashboard dock.");
        }

        var actionLabels = (await page.Locator("#athleteDashboardActions .flow-action .flow-action__label")
            .AllInnerTextsAsync())
            .Select(label => label.Replace('\u00a0', ' '))
            .ToArray();
        Assert.Contains("Edit profile", actionLabels);
        Assert.Contains("Longevitymaxxing", actionLabels);
        Assert.Contains("Change athlete", actionLabels);
        if (isPro)
        {
            Assert.Contains("Update Pheno Age", actionLabels);
            Assert.Contains("Update Bortz Age", actionLabels);
        }
        else
        {
            Assert.Contains("Submit new results", actionLabels);
            Assert.Contains(actionLabels, label => label.Contains("Go pro for", StringComparison.Ordinal)
                && label.Contains("$70", StringComparison.Ordinal));
        }

        if (!isPro)
        {
            var discountState = await page.EvaluateAsync<DashboardDiscountLayoutState>(
                """
                () => {
                    const container = document.getElementById('athleteDashboardDiscounts');
                    const discount = container?.querySelector('.pro-discount-box');
                    const picture = document.getElementById('athleteDashboardPicture');
                    const lines = Array.from(discount?.querySelectorAll('.pro-discount-line') || []);
                    const icons = Array.from(discount?.querySelectorAll('.pro-discount-badge-slot .badge-class') || []);
                    const textLefts = lines.map(line => line.querySelector('.pro-discount-text')?.getBoundingClientRect().left || 0);
                    return {
                        DiscountVisible: Boolean(discount && discount.getBoundingClientRect().height > 0),
                        DiscountInsideActionMenu: Boolean(discount?.closest('#athleteDashboardActions')),
                        DiscountTop: discount?.getBoundingClientRect().top || 0,
                        PictureBottom: picture?.getBoundingClientRect().bottom || 0,
                        VisibleLineCount: lines.length,
                        CompactTexts: lines.map(line => line.querySelector('.pro-discount-text')?.dataset.compactText || ''),
                        TextLefts: textLefts,
                        IconWidths: icons.map(icon => icon.getBoundingClientRect().width),
                        IconHeights: icons.map(icon => icon.getBoundingClientRect().height)
                    };
                }
                """);
            Assert.True(discountState.DiscountVisible);
            Assert.False(discountState.DiscountInsideActionMenu,
                "Discount details belong below the athlete picture, not inside the sticky action menu.");
            Assert.True(discountState.DiscountTop >= discountState.PictureBottom,
                "Discount details should follow the athlete picture in the normal document flow.");
            Assert.Equal(3, discountState.VisibleLineCount);
            Assert.Contains("10% leaderboard", discountState.CompactTexts);
            Assert.Contains("10% personal page", discountState.CompactTexts);
            Assert.Contains("10% perfect guess", discountState.CompactTexts);
            Assert.Equal(2, discountState.IconWidths.Length);
            Assert.All(discountState.IconWidths, width => Assert.InRange(width, 43, 45));
            Assert.All(discountState.IconHeights, height => Assert.InRange(height, 43, 45));
            Assert.True(discountState.TextLefts.Max() - discountState.TextLefts.Min() <= 1,
                "Every discount percentage should start on the same vertical alignment line.");
        }

        if (viewportWidth >= 960)
        {
            var secondaryActionsAreFlat = await page.EvaluateAsync<bool>(
                """
                () => Array.from(document.querySelectorAll(
                    '.play-dashboard-actions.flow-action-stack--docked .flow-action--secondary'))
                    .every(action => {
                        const style = getComputedStyle(action);
                        return style.boxShadow === 'none'
                            && style.backgroundColor === 'rgba(0, 0, 0, 0)';
                    })
                """);
            Assert.True(secondaryActionsAreFlat,
                "Desktop dashboard secondary actions should read as one command bar, not competing pills.");
        }

        if (viewportWidth >= 960)
        {
            var minPictureWidth = viewportHeight <= 740 ? 310 : 340;
            Assert.True(pictureRect.Width >= minPictureWidth,
                $"Dashboard picture is too small for the available desktop space: {pictureRect.Width}px < {minPictureWidth}px.");
        }
        Assert.True(pictureRect.Bottom <= actionsRect.Top - 16,
            $"Dashboard picture overlaps the dock: picture bottom {pictureRect.Bottom}, dock top {actionsRect.Top}.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DashboardActions_StayInlineBelowDiscountsWhenTheViewportHasRoom()
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
            ViewportSize = new ViewportSize { Width = 1664, Height = 1130 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                PersonalLink: 'https://example.test/browser-test-athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            window.localStorage.setItem('gmaHasPerfectGuess', '1');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.getElementById('athleteDashboardPanel')?.hidden === false
                && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4
            """);
        await WaitForManagedActionStacksSettledAsync(page);

        var state = await page.EvaluateAsync<InlineDashboardActionState>(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const picture = document.getElementById('athleteDashboardPicture').getBoundingClientRect();
                const discounts = document.getElementById('athleteDashboardDiscounts').getBoundingClientRect();
                const actions = document.getElementById('athleteDashboardActions').getBoundingClientRect();
                const firstActions = Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action'))
                    .slice(0, 2)
                    .map(action => action.getBoundingClientRect());
                return {
                    ActionDocked: document.getElementById('athleteDashboardActions')
                        .classList.contains('flow-action-stack--docked'),
                    PictureBottom: picture.bottom,
                    DiscountTop: discounts.top,
                    DiscountBottom: discounts.bottom,
                    ActionTop: actions.top,
                    ActionBottom: actions.bottom,
                    FirstActionTop: firstActions[0]?.top || 0,
                    SecondActionTop: firstActions[1]?.top || 0,
                    ViewportHeight: window.innerHeight
                };
            }
            """);

        Assert.False(state.ActionDocked,
            "The dashboard menu should remain inline when the complete menu fits in the viewport.");
        Assert.True(state.PictureBottom <= state.DiscountTop);
        Assert.True(state.DiscountBottom <= state.ActionTop);
        Assert.True(state.ActionBottom <= state.ViewportHeight - 12,
            $"Inline dashboard actions exceed the viewport: {state.ActionBottom}px > {state.ViewportHeight - 12}px.");
        Assert.InRange(Math.Abs(state.FirstActionTop - state.SecondActionTop), 0, 1);

        await page.SetViewportSizeAsync(1664, 768);
        await ExpectActionStackDockedInViewportAsync(page, ".play-dashboard-actions");

        await page.SetViewportSizeAsync(1664, 1130);
        await page.WaitForFunctionAsync(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const actions = document.getElementById('athleteDashboardActions');
                const rect = actions?.getBoundingClientRect();
                return actions
                    && !actions.classList.contains('flow-action-stack--docked')
                    && rect.bottom <= window.innerHeight - 12;
            }
            """);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task DashboardAthletesIcon_RemainsCenteredAfterResizingFromMobileToDesktop()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(FlowAuditStateScript);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await ExpectActionStackDockedInViewportAsync(page, ".play-dashboard-actions");
        await page.Locator("#playDashboardBackBtn > i").EvaluateAsync(
            "icon => { icon.textContent = '\u2190'; icon.style.fontSize = '16px'; }");

        await page.SetViewportSizeAsync(1366, 768);
        await page.WaitForFunctionAsync(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const actions = document.querySelector('.play-dashboard-actions');
                return actions?.classList.contains('flow-action-stack--docked') && window.innerWidth === 1366;
            }
            """);

        var iconCenterOffset = await page.Locator("#playDashboardBackBtn").EvaluateAsync<double>(
            """
            button => {
                const buttonRect = button.getBoundingClientRect();
                const iconRect = button.querySelector('i').getBoundingClientRect();
                return Math.abs(
                    (buttonRect.top + buttonRect.height / 2)
                    - (iconRect.top + iconRect.height / 2));
            }
            """);

        Assert.InRange(iconCenterOffset, 0, 2);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", "#phenoAgeResult", ".phenoage-result-actions")]
    [InlineData("/bortz-age", "#bortzAgeResult", ".bioage-result-actions")]
    public async Task BioageResultActions_BecomeTheOnlyDockedActionsAfterCalculation(
        string path,
        string resultSelector,
        string resultActionsSelector)
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        if (path.Contains("bortz", StringComparison.Ordinal))
        {
            await FillBortzBiomarkersAsync(page);
        }
        else
        {
            await FillPhenoBiomarkersAsync(page);
        }

        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForSelectorAsync($"{resultSelector}.show");
        await page.WaitForSelectorAsync("#continueButton.show");

        await ExpectActionStackDockedInViewportAsync(page, resultActionsSelector);
        await ExpectBioageResultVisibleAboveDockAsync(page, resultSelector, resultActionsSelector);
        Assert.False(await HasDockClassAsync(page, "#lwcStepTwoActions"));

        var dockedVisibleActionCount = await page.EvaluateAsync<int>(
            """
            () => Array.from(document.querySelectorAll('.flow-action-stack--docked'))
                .filter(element => {
                    const rect = element.getBoundingClientRect();
                    const style = getComputedStyle(element);
                    return rect.width > 0
                        && rect.height > 0
                        && style.display !== 'none'
                        && style.visibility !== 'hidden';
                }).length
            """);

        Assert.Equal(1, dockedVisibleActionCount);
        Assert.Empty(errors);
    }

    private static async Task ExpectBioageResultVisibleAboveDockAsync(
        IPage page,
        string resultSelector,
        string dockSelector)
    {
        await page.WaitForTimeoutAsync(1400);

        var layout = await page.EvaluateAsync<BioageResultDockLayout>(
            """
            selectors => {
                const [resultSelector, dockSelector] = selectors.split('|');
                const result = document.querySelector(resultSelector);
                const dock = document.querySelector(dockSelector);
                if (!result || !dock) {
                    return { ResultTop: 0, ResultBottom: 0, ResultHeight: 0, DockTop: 0, DockHeight: 0, ScrollY: window.scrollY, MaxScrollY: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) - window.innerHeight, MissingElement: true };
                }

                const resultRect = result.getBoundingClientRect();
                const dockRect = dock.getBoundingClientRect();
                const resultStyle = getComputedStyle(result);
                const dockStyle = getComputedStyle(dock);

                return {
                    ResultTop: resultRect.top,
                    ResultBottom: resultRect.bottom,
                    ResultHeight: resultRect.height,
                    ResultVisible: resultRect.width > 0
                        && resultRect.height > 0
                        && resultStyle.display !== 'none'
                        && resultStyle.visibility !== 'hidden',
                    DockTop: dockRect.top,
                    DockBottom: dockRect.bottom,
                    DockHeight: dockRect.height,
                    DockVisible: dockRect.width > 0
                        && dockRect.height > 0
                        && dockStyle.display !== 'none'
                        && dockStyle.visibility !== 'hidden',
                    ScrollY: window.scrollY,
                    MaxScrollY: Math.max(document.documentElement.scrollHeight, document.body.scrollHeight) - window.innerHeight,
                    ViewportHeight: window.innerHeight,
                    RootScrollPaddingTop: getComputedStyle(document.documentElement).scrollPaddingTop,
                    DockHeightVariable: getComputedStyle(document.documentElement).getPropertyValue('--flow-action-dock-height').trim(),
                    BodyClasses: document.body.className,
                    HtmlClasses: document.documentElement.className,
                    MissingElement: false
                };
            }
            """,
            $"{resultSelector}|{dockSelector}");

        Assert.False(layout.MissingElement, $"Missing result or dock for {resultSelector} / {dockSelector}.");
        Assert.True(layout.ResultVisible, $"{resultSelector} is not visibly rendered. {layout}");
        Assert.True(layout.DockVisible, $"{dockSelector} is not visibly rendered. {layout}");
        Assert.True(layout.ResultTop >= 0, $"{resultSelector} starts above the viewport. {layout}");
        Assert.True(layout.ResultBottom <= layout.DockTop - 8, $"{resultSelector} is covered by the result action dock. {layout}");
    }

    [Fact]
    public async Task MobileBioageStickyProgress_KeepsOnlyVisibleProgressSemantics()
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
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => window.LwcStickyProgress");
        await page.WaitForTimeoutAsync(650);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.WaitForFunctionAsync("() => !document.documentElement.classList.contains('sticky-progress-visible')");
        await page.Mouse.WheelAsync(0, 360);
        await page.WaitForFunctionAsync("() => document.documentElement.classList.contains('sticky-progress-visible')");

        const string progressStateScript =
            """
            element => {
                const style = getComputedStyle(element);
                const sticky = document.getElementById('site-sticky-progress');
                return {
                    Opacity: style.opacity,
                    PointerEvents: style.pointerEvents,
                    AriaHidden: element.getAttribute('aria-hidden'),
                    Inert: element.inert,
                    HasInertAttribute: element.hasAttribute('inert'),
                    StickyAriaHidden: sticky && sticky.getAttribute('aria-hidden'),
                    StickyRole: sticky && sticky.getAttribute('role'),
                    StickyAriaLive: sticky && sticky.getAttribute('aria-live')
                };
            }
            """;

        var progressState = await page.Locator("#mainProgressBar").EvaluateAsync<StickyProgressState>(
            progressStateScript);

        Assert.Equal("0", progressState.Opacity);
        Assert.Equal("none", progressState.PointerEvents);
        Assert.Equal("true", progressState.AriaHidden);
        Assert.True(progressState.Inert);
        Assert.True(progressState.HasInertAttribute);
        Assert.Equal("false", progressState.StickyAriaHidden);
        Assert.Equal("status", progressState.StickyRole);
        Assert.Equal("polite", progressState.StickyAriaLive);

        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.WaitForFunctionAsync("() => !document.documentElement.classList.contains('sticky-progress-visible')");

        progressState = await page.Locator("#mainProgressBar").EvaluateAsync<StickyProgressState>(
            progressStateScript);

        Assert.Equal("1", progressState.Opacity);
        Assert.NotEqual("none", progressState.PointerEvents);
        Assert.Equal("false", progressState.AriaHidden);
        Assert.False(progressState.Inert);
        Assert.False(progressState.HasInertAttribute);
        Assert.Equal("true", progressState.StickyAriaHidden);
        Assert.Empty(errors);
    }

    private static List<string> CapturePageErrors(IPage page)
    {
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        return errors;
    }

    private static void AssertPlayStartLogoBetweenWordmarkAndActions(
        PlayStartWordmarkState state,
        double centerTolerancePixels)
    {
        Assert.True(state.WatermarkTop >= state.WordmarkBottom + 16, $"Watermark starts too close to the wordmark: {state.WatermarkTop}px <= {state.WordmarkBottom}px");
        Assert.True(state.WatermarkBottom <= state.ActionStackTop - 16, $"Watermark overlaps the action buttons: {state.WatermarkBottom}px >= {state.ActionStackTop}px");

        var gapCenter = (state.WordmarkBottom + state.ActionStackTop) / 2;
        var watermarkCenter = (state.WatermarkTop + state.WatermarkBottom) / 2;
        Assert.InRange(Math.Abs(watermarkCenter - gapCenter), 0, centerTolerancePixels);
    }

    private const string FlowAuditStateScript =
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

    private const string FlowActionPlacementAuditScript =
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

    private static async Task ExpectActionStackDockedInViewportAsync(IPage page, string selector)
    {
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.WaitForFunctionAsync(
            """
            selector => {
                const element = document.querySelector(selector);
                if (!element?.classList.contains('flow-action-stack--docked')) return false;
                const rect = element.getBoundingClientRect();
                return element?.classList.contains('flow-action-stack--docked')
                    && !element.classList.contains('flow-action-stack--dock-entering')
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

    private static async Task ExpectDockPlaceholderMatchesVisibleStackAsync(IPage page, string selector)
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

    private static async Task WaitForManagedActionStacksSettledAsync(IPage page)
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
    }

    private static async Task ExpectActionStackInViewportAsync(IPage page, string selector)
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

    private static async Task<ElementRect> ReadElementRectAsync(IPage page, string selector)
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

    private static async Task<FlowActionChildLayout> ReadFlowActionChildLayoutAsync(IPage page, string selector)
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

    private static async Task ExpectNoHorizontalOverflowAsync(IPage page)
    {
        var overflow = await page.EvaluateAsync<double>(
            "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) - window.innerWidth");
        Assert.True(overflow <= 1, $"Page has {overflow}px horizontal overflow.");
    }

    private static async Task<bool> HasDockClassAsync(IPage page, string selector)
    {
        return await page.Locator(selector).EvaluateAsync<bool>(
            "element => element.classList.contains('flow-action-stack--docked')");
    }

    private static async Task FillBioageStepOneAsync(IPage page, string bloodDrawDate)
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

    private static async Task FillPhenoBiomarkersAsync(IPage page)
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

    private static async Task FillBortzBiomarkersAsync(IPage page)
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

    private static async Task SetFormValuesAsync(IPage page, Dictionary<string, string> values)
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

    private sealed class ElementRect
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

    private sealed class BioageStepOneLayout
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

    private sealed class FlowActionChildLayout
    {
        public int Count { get; set; }
        public double MaxGap { get; set; }
        public double MaxHeight { get; set; }
        public double MaxHeightDelta { get; set; }
    }

    private sealed class ApplyStageTransitionScrollState
    {
        public double ScrollY { get; set; }
        public ScrollIntoViewCall[] Calls { get; set; } = [];
    }

    private sealed class ScrollIntoViewCall
    {
        public string Tag { get; set; } = "";
        public string Id { get; set; } = "";
        public string ClassName { get; set; } = "";
        public string Text { get; set; } = "";
    }

    private sealed class WorkflowTitleLayout
    {
        public string Text { get; set; } = "";
        public double FontSize { get; set; }
        public double LineHeight { get; set; }
        public double Bottom { get; set; }
    }

    private sealed class EditProfileInitialState
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

    private sealed class EditProfileMissingOriginalRestoreState
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

    private sealed class EditProfileFirstViewportLayout
    {
        public double TitleFontSize { get; set; }
        public ElementRect Title { get; set; } = new();
        public ElementRect Picture { get; set; } = new();
        public ElementRect PictureButton { get; set; } = new();
        public string OptionsAos { get; set; } = "";
        public double ViewportHeight { get; set; }
    }

    private sealed class ProofUploadFirstViewportLayout
    {
        public double TitleFontSize { get; set; }
        public ElementRect Title { get; set; } = new();
        public ElementRect Illustration { get; set; } = new();
        public ElementRect UploadButton { get; set; } = new();
        public double ViewportHeight { get; set; }
    }

    private sealed class ProofUploadActionLayout
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

    private sealed class ApplyFirstStageDetailsLayout
    {
        public double DetailsTop { get; set; }
        public double DetailsBottom { get; set; }
        public bool DetailsVisible { get; set; }
        public double DockTop { get; set; }
        public double DockBottom { get; set; }
        public double ViewportHeight { get; set; }
    }

    private sealed class DockedActionHierarchy
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

    private sealed class PlayStartWordmarkState
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

    private sealed class PlayStartFirstPaintState
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

    private sealed class PlayStartBackDockState
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

    private sealed class ModernContainingBlockDockState
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

    private sealed class TransformedFormDockState
    {
        public string FormTransform { get; set; } = "";
        public bool ActionsParentIsBody { get; set; }
    }

    private sealed class StickyProgressState
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

    private sealed class DashboardDiscountLayoutState
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

    private sealed class InlineDashboardActionState
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

    private sealed class BioageResultDockLayout
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

    private sealed class JoinTrackActionGrouping
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

    private sealed class FooterVisibility
    {
        public string Display { get; set; } = "";
        public int VisibleLinkCount { get; set; }
    }

    private sealed class PlayWorkflowChromeState
    {
        public string FooterDisplay { get; set; } = "";
        public bool HasSiteMenu { get; set; }
        public bool HasSiteMenuToggle { get; set; }
        public bool HasSiteMenuPanel { get; set; }
    }

}
