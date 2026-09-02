using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed class BioageKeyboardViewportBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task FocusedInput_InWindowedViewportDoesNotTreatUnusedScreenSpaceAsAKeyboard()
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = 390, Height = 500 },
            ScreenSize = new ScreenSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/pheno-age", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.Locator("#blood-draw-date").FocusAsync();
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");

        var state = await page.EvaluateAsync<WindowedViewportState>(
            """
            () => ({
                KeyboardOpen: document.documentElement.classList.contains('flow-input-keyboard-open'),
                KeyboardClearance: parseFloat(getComputedStyle(document.documentElement)
                    .getPropertyValue('--flow-input-keyboard-occlusion')) || 0,
                ScreenHeight: window.screen.height,
                ViewportHeight: window.visualViewport?.height || window.innerHeight
            })
            """);

        Assert.True(
            state.ScreenHeight - state.ViewportHeight >= 300,
            $"The test requires a substantially taller physical screen: {state.ScreenHeight} vs {state.ViewportHeight}.");
        Assert.False(state.KeyboardOpen);
        Assert.Equal(0, state.KeyboardClearance);
    }

    [Theory]
    [InlineData("/pheno-age", "#crp", "1", 390, 844, 420, 360)]
    [InlineData("/bortz-age", "#vitamin_d", "50", 390, 844, 420, 360)]
    [InlineData("/pheno-age", "#crp", "1", 844, 390, 200, 160)]
    [InlineData("/bortz-age", "#vitamin_d", "50", 844, 390, 200, 160)]
    [InlineData("/pheno-age", "#crp", "1", 956, 440, 230, 180)]
    [InlineData("/bortz-age", "#vitamin_d", "50", 956, 440, 230, 180)]
    public async Task BioageBiomarker_FocusStaysVisibleWhenTheMobileKeyboardOpens(
        string path,
        string inputSelector,
        string inputValue,
        int viewportWidth,
        int viewportHeight,
        int keyboardViewportHeight,
        int keyboardViewportOffsetTop)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            IsMobile = true,
            HasTouch = true,
            ViewportSize = new ViewportSize { Width = viewportWidth, Height = viewportHeight },
            ScreenSize = new ScreenSize { Width = viewportWidth, Height = viewportHeight }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(VisualViewportTestBootstrap());

        await AssertFocusedBiomarkerWithKeyboardAsync(
            context,
            path,
            inputSelector,
            inputValue,
            viewportHeight,
            keyboardViewportHeight,
            keyboardViewportOffsetTop);
    }

    private static async Task AssertFocusedBiomarkerWithKeyboardAsync(
        IBrowserContext context,
        string path,
        string inputSelector,
        string inputValue,
        int viewportHeight,
        int keyboardViewportHeight,
        int keyboardViewportOffsetTop)
    {

        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.EvaluateAsync(
            """
            () => {
                window.__testSetVisualViewport(window.innerHeight, 0);
                window.LwcFlowActionDock.refreshNow();
            }
            """);

        await page.Locator("#dob-year").SelectOptionAsync("1980");
        await page.Locator("#dob-month").SelectOptionAsync("5");
        await page.WaitForFunctionAsync(
            "() => Array.from(document.querySelector('#dob-day')?.options || []).some(option => option.value === '20')");
        await page.Locator("#dob-day").SelectOptionAsync("20");
        await page.Locator("#blood-draw-date")
            .FillAsync(DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await page.Locator("#lwcToStep2Btn").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwc-step-2')?.classList.contains('lwc-step--visible')");

        var biomarker = page.Locator(inputSelector);
        await biomarker.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await biomarker.ScrollIntoViewIfNeededAsync();

        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#lwcStepTwoActions')?.classList.contains('flow-action-stack--docked')");

        await biomarker.FillAsync(inputValue);
        await page.EvaluateAsync(
            "viewport => window.__testSetVisualViewport(viewport.height, viewport.offsetTop)",
            new { height = keyboardViewportHeight, offsetTop = keyboardViewportOffsetTop });
        await page.WaitForFunctionAsync(
            """
            selector => {
                const input = document.querySelector(selector);
                const viewport = window.visualViewport;
                if (!input || !viewport || document.activeElement !== input) return false;
                const rect = input.getBoundingClientRect();
                return document.documentElement.classList.contains('flow-input-keyboard-open')
                    && document.body.classList.contains('flow-input-keyboard-open')
                    && !document.querySelector('#lwcStepTwoActions')?.classList.contains('flow-action-stack--docked')
                    && rect.top >= viewport.offsetTop
                    && rect.bottom <= viewport.offsetTop + viewport.height;
            }
            """,
            inputSelector);

        var state = await page.EvaluateAsync<KeyboardViewportState>(
            """
            selector => {
                const input = document.querySelector(selector);
                const dock = document.querySelector('#lwcStepTwoActions');
                const viewport = window.visualViewport;
                const rect = input.getBoundingClientRect();
                return {
                    HtmlKeyboardOpen: document.documentElement.classList.contains('flow-input-keyboard-open'),
                    BodyKeyboardOpen: document.body.classList.contains('flow-input-keyboard-open'),
                    Docked: dock.classList.contains('flow-action-stack--docked'),
                    InputFocused: document.activeElement === input,
                    InputTop: rect.top,
                    InputBottom: rect.bottom,
                    VisualViewportTop: viewport.offsetTop,
                    VisualViewportBottom: viewport.offsetTop + viewport.height,
                    KeyboardClearance: parseFloat(getComputedStyle(document.documentElement)
                        .getPropertyValue('--flow-input-keyboard-occlusion')) || 0
                };
            }
            """,
            inputSelector);

        Assert.True(state.HtmlKeyboardOpen, "The document element should expose the keyboard-open state.");
        Assert.True(state.BodyKeyboardOpen, "The body should expose the keyboard-open state.");
        Assert.False(state.Docked, "The mobile action dock should return to document flow while the keyboard is open.");
        Assert.True(state.InputFocused, "The biomarker input should retain focus after the visual viewport shrinks.");
        Assert.InRange(
            state.KeyboardClearance,
            viewportHeight - keyboardViewportHeight - 1,
            viewportHeight - keyboardViewportHeight + 1);
        Assert.True(
            state.InputTop >= state.VisualViewportTop && state.InputBottom <= state.VisualViewportBottom,
            $"The focused biomarker must remain fully visible: input {state.InputTop}-{state.InputBottom}, "
            + $"visual viewport {state.VisualViewportTop}-{state.VisualViewportBottom}.");
        Assert.Equal("next", await biomarker.GetAttributeAsync("enterkeyhint"));
        await page.EvaluateAsync(
            """
            () => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))
            """);

        await page.EvaluateAsync("() => window.scrollTo({ top: 0, behavior: 'instant' })");
        await page.WaitForFunctionAsync("() => window.scrollY <= 1");
        Assert.InRange(await page.EvaluateAsync<double>("() => window.scrollY"), 0, 1);

        await page.EvaluateAsync(
            "() => window.scrollTo({ top: document.scrollingElement.scrollHeight, behavior: 'instant' })");
        await page.WaitForFunctionAsync(
            "() => document.scrollingElement.scrollHeight - window.innerHeight - window.scrollY <= 1");
        var downwardScroll = await page.EvaluateAsync<ManualScrollState>(
            """
            () => ({
                ScrollY: window.scrollY,
                MaxScrollY: document.scrollingElement.scrollHeight - window.innerHeight
            })
            """);
        Assert.InRange(downwardScroll.MaxScrollY - downwardScroll.ScrollY, 0, 1);

        await biomarker.PressAsync("Enter");
        await page.WaitForFunctionAsync(
            """
            () => {
                const input = document.querySelector('#wbc');
                const viewport = window.visualViewport;
                if (!input || !viewport || document.activeElement !== input) return false;
                const rect = input.getBoundingClientRect();
                return rect.top >= viewport.offsetTop
                    && rect.bottom <= viewport.offsetTop + viewport.height;
            }
            """);

        await page.EvaluateAsync("() => window.__testSetVisualViewport(window.innerHeight, 0)");
        await page.WaitForFunctionAsync(
            """
            () => !document.documentElement.classList.contains('flow-input-keyboard-open')
                && document.querySelector('#lwcStepTwoActions')?.classList.contains('flow-action-stack--docked')
            """);
        var restoredState = await page.EvaluateAsync<string>(
            """
            () => JSON.stringify({
                visualHeight: window.visualViewport?.height,
                innerHeight: window.innerHeight,
                screenWidth: window.screen?.width,
                screenHeight: window.screen?.height,
                orientation: window.screen?.orientation?.type,
                keyboardOpen: document.documentElement.classList.contains('flow-input-keyboard-open'),
                occlusion: getComputedStyle(document.documentElement)
                    .getPropertyValue('--flow-input-keyboard-occlusion'),
                docked: document.querySelector('#lwcStepTwoActions')
                    ?.classList.contains('flow-action-stack--docked')
            })
            """);
        Assert.False(await page.Locator("html").EvaluateAsync<bool>(
            "html => html.classList.contains('flow-input-keyboard-open')"), restoredState);
        Assert.True(await page.Locator("#lwcStepTwoActions").EvaluateAsync<bool>(
            "dock => dock.classList.contains('flow-action-stack--docked')"), restoredState);
        await page.CloseAsync();
    }

    private static string VisualViewportTestBootstrap()
        => """
        const visualViewport = new EventTarget();
        visualViewport.offsetTop = 0;
        visualViewport.height = window.innerHeight;
        Object.defineProperty(window, 'visualViewport', {
            configurable: true,
            value: visualViewport
        });
        window.__testVisualViewport = visualViewport;
        window.__testSetVisualViewport = (height, offsetTop = 0) => {
            visualViewport.height = height;
            visualViewport.offsetTop = offsetTop;
            visualViewport.dispatchEvent(new Event('resize'));
        };
        """;

    private sealed class KeyboardViewportState
    {
        public bool HtmlKeyboardOpen { get; set; }
        public bool BodyKeyboardOpen { get; set; }
        public bool Docked { get; set; }
        public bool InputFocused { get; set; }
        public double InputTop { get; set; }
        public double InputBottom { get; set; }
        public double VisualViewportTop { get; set; }
        public double VisualViewportBottom { get; set; }
        public double KeyboardClearance { get; set; }
    }

    private sealed class ManualScrollState
    {
        public double ScrollY { get; set; }
        public double MaxScrollY { get; set; }
    }

    private sealed class WindowedViewportState
    {
        public bool KeyboardOpen { get; set; }
        public double KeyboardClearance { get; set; }
        public double ScreenHeight { get; set; }
        public double ViewportHeight { get; set; }
    }
}
