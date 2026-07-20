using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageChromeRegressionBrowserTests
{
    [Fact]
    public async Task HomepagePrimaryAction_RemainsProminentVisibleAndSeparateFromTheBrand()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 320, Height = 720 },
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 640, Height = 390 },
                     new ViewportSize { Width = 844, Height = 390 },
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);

            var diagnostics = await MeasureHomepageHeaderAsync(page);
            Assert.True(diagnostics.ActionVisible,
                $"Homepage Play action was hidden at {viewport.Width}x{viewport.Height}.");
            Assert.Equal("PLAY THE GAME", diagnostics.ActionText);
            Assert.True(diagnostics.ActionWidth >= 44 && diagnostics.ActionHeight >= 44,
                $"Homepage Play action collapsed to {diagnostics.ActionWidth:F1}x{diagnostics.ActionHeight:F1}px at " +
                $"{viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.ActionLeft >= -0.5 && diagnostics.ActionRight <= viewport.Width + 0.5,
                $"Homepage Play action left the viewport at {viewport.Width}x{viewport.Height}.");
            Assert.False(diagnostics.ActionOverlapsBrand,
                $"Homepage Play action overlapped the brand at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.ActionBackgroundAlpha >= 0.99,
                $"Homepage Play action lost its opaque fill at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.LogoNaturalWidth > 0 && diagnostics.LogoNaturalHeight > 0,
                "Homepage logo did not decode.");
            Assert.InRange(
                Math.Abs(diagnostics.LogoRenderedAspectRatio - diagnostics.LogoNaturalAspectRatio),
                0,
                0.01);
        }
    }

    [Fact]
    public async Task PlayAction_IsNeverAbsentInCompactLandscapeOrAfterScrolling()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        // /play deliberately removes the redundant join buttons because it is
        // already their destination. The dedicated event board also keeps its
        // long-standing full-screen chrome. These routes retain the global CTA.
        foreach (var path in new[] { "/", "/leaderboard", "/ruleset", "/history" })
        {
            foreach (var viewport in new[]
                     {
                         new ViewportSize { Width = 320, Height = 720 },
                         new ViewportSize { Width = 390, Height = 844 },
                         new ViewportSize { Width = 667, Height = 375 },
                         new ViewportSize { Width = 844, Height = 390 },
                         new ViewportSize { Width = 900, Height = 450 },
                         new ViewportSize { Width = 1026, Height = 473 }
                     })
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await page.EvaluateAsync("window.scrollTo(0, 0)");
                await SettleLayoutAsync(page);

                var atTopActions = await MeasurePlayActionsAsync(page);
                var atTop = atTopActions.Where(action => action.Visible).ToArray();
                Assert.True(atTop.Length > 0,
                    $"{path} had no visible Play action at the top of {viewport.Width}x{viewport.Height}. " +
                    DescribeActions(atTopActions));
                Assert.All(atTop, action => AssertActionInsideViewport(path, viewport, action));

                await page.EvaluateAsync("window.scrollTo(0, Math.min(52, document.documentElement.scrollHeight - innerHeight))");
                await SettleLayoutAsync(page);
                var stickyHeaderVisible = await page.EvaluateAsync<bool>(
                    "() => document.getElementById('site-sticky-header')?.classList.contains('visible') === true");
                if (stickyHeaderVisible)
                {
                    var boundaryActions = await MeasurePlayActionsAsync(page);
                    var stickyAction = Assert.Single(
                        boundaryActions,
                        action => action.Visible && action.IsScrolled);
                    AssertActionInsideViewport(path, viewport, stickyAction);
                }

                await page.EvaluateAsync("window.scrollTo(0, Math.min(700, document.documentElement.scrollHeight - innerHeight))");
                await SettleLayoutAsync(page);
                var afterScrollActions = await MeasurePlayActionsAsync(page);
                var afterScroll = afterScrollActions.Where(action => action.Visible).ToArray();
                Assert.True(afterScroll.Length > 0,
                    $"{path} had no visible Play action after scrolling at {viewport.Width}x{viewport.Height}. " +
                    DescribeActions(afterScrollActions));
                Assert.All(afterScroll, action => AssertActionInsideViewport(path, viewport, action));
            }
        }
    }

    [Fact]
    public async Task NewsletterSubscribeAction_HasOpaqueHighContrastFillAcrossResponsiveLayouts()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 320, Height = 720 },
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);
            var diagnostics = await page.Locator(".enhanced-subscribe-btn").EvaluateAsync<FilledActionDiagnostics>(
                MeasureFilledActionScript);

            Assert.True(diagnostics.Visible,
                $"Subscribe action was hidden at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.Enabled,
                $"Subscribe action was unexpectedly disabled at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.BackgroundAlpha >= 0.99,
                $"Subscribe action background was {diagnostics.Background} at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.TextContrast >= 4.5,
                $"Subscribe text contrast was only {diagnostics.TextContrast:F2}:1 at " +
                $"{viewport.Width}x{viewport.Height}; foreground={diagnostics.Foreground}, " +
                $"background={diagnostics.Background}.");
            Assert.True(diagnostics.Width >= 44 && diagnostics.Height >= 44,
                $"Subscribe action collapsed to {diagnostics.Width:F1}x{diagnostics.Height:F1}px at " +
                $"{viewport.Width}x{viewport.Height}.");
        }
    }

    [Fact]
    public async Task CompactPageHeaders_PreserveTheLogoAndFullPrimaryActionWhenSpaceAllows()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/leaderboard", "/ruleset", "/history" })
        {
            foreach (var viewport in new[]
                     {
                         new ViewportSize { Width = 464, Height = 800 },
                         new ViewportSize { Width = 464, Height = 300 }
                     })
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
                await SettleLayoutAsync(page);

                var diagnostics = await page.EvaluateAsync<CompactHeaderDiagnostics>(
                    """
                    () => {
                        const header = document.querySelector('header[role="banner"]');
                        const brand = header.querySelector('.header-link');
                        const logo = brand.querySelector('.main-logo-image');
                        const action = header.querySelector('.join-game:not(.scrolled-button)');
                        const brandRect = brand.getBoundingClientRect();
                        const logoRect = logo.getBoundingClientRect();
                        const actionRect = action.getBoundingClientRect();
                        return {
                            ActionText: action.innerText.trim().replace(/\s+/g, ' '),
                            ActionLeft: actionRect.left,
                            ActionRight: actionRect.right,
                            BrandLeft: brandRect.left,
                            BrandRight: brandRect.right,
                            LogoWidth: logoRect.width,
                            LogoRenderedAspectRatio: logoRect.width / logoRect.height,
                            LogoNaturalAspectRatio: logo.naturalWidth / logo.naturalHeight,
                            HasHorizontalOverflow: document.documentElement.scrollWidth
                                > document.documentElement.clientWidth
                        };
                    }
                    """);

                Assert.Equal("PLAY THE GAME", diagnostics.ActionText);
                Assert.True(diagnostics.LogoWidth >= 44,
                    $"{path} logo collapsed to {diagnostics.LogoWidth:F1}px at " +
                    $"{viewport.Width}x{viewport.Height}.");
                Assert.InRange(
                    Math.Abs(diagnostics.LogoRenderedAspectRatio - diagnostics.LogoNaturalAspectRatio),
                    0,
                    0.01);
                Assert.True(diagnostics.ActionLeft >= diagnostics.BrandRight,
                    $"{path} brand and Play action overlapped at {viewport.Width}x{viewport.Height}.");
                Assert.True(diagnostics.BrandLeft >= -0.5 && diagnostics.ActionRight <= viewport.Width + 0.5,
                    $"{path} compact header left the viewport at {viewport.Width}x{viewport.Height}.");
                Assert.False(diagnostics.HasHorizontalOverflow,
                    $"{path} overflowed horizontally at {viewport.Width}x{viewport.Height}.");
            }
        }
    }

    private const string MeasureFilledActionScript =
        """
        element => {
            const parse = value => {
                const parts = value.match(/[\d.]+/g)?.map(Number) ?? [];
                return { r: parts[0] ?? 0, g: parts[1] ?? 0, b: parts[2] ?? 0, a: parts[3] ?? 1 };
            };
            const luminance = color => {
                const channel = value => {
                    const normalized = value / 255;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.pow((normalized + 0.055) / 1.055, 2.4);
                };
                return 0.2126 * channel(color.r) + 0.7152 * channel(color.g) + 0.0722 * channel(color.b);
            };
            const style = getComputedStyle(element);
            const rect = element.getBoundingClientRect();
            const foreground = parse(style.color);
            const background = parse(style.backgroundColor);
            const light = Math.max(luminance(foreground), luminance(background));
            const dark = Math.min(luminance(foreground), luminance(background));
            return {
                Visible: style.display !== 'none' && style.visibility !== 'hidden'
                    && rect.width > 0 && rect.height > 0,
                Enabled: !element.disabled && element.getAttribute('aria-disabled') !== 'true',
                Foreground: style.color,
                Background: style.backgroundColor,
                BackgroundAlpha: background.a,
                TextContrast: (light + 0.05) / (dark + 0.05),
                Width: rect.width,
                Height: rect.height
            };
        }
        """;

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser, BrowserTestApp app)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/bitcoin/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath;
            var body = path.EndsWith("/donation-address", StringComparison.OrdinalIgnoreCase)
                ? """{"address":""}"""
                : path.EndsWith("/btcusd", StringComparison.OrdinalIgnoreCase)
                    ? """{"btcToUsdRate":0}"""
                    : """{"totalReceivedSatoshis":0}""";
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = body
            });
        });
        return context;
    }

    private static async Task SettleLayoutAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private static Task<HomepageHeaderDiagnostics> MeasureHomepageHeaderAsync(IPage page) =>
        page.EvaluateAsync<HomepageHeaderDiagnostics>(
            """
            () => {
                const action = document.querySelector('header[role="banner"] .join-game:not(.scrolled-button)');
                const brand = document.querySelector('header[role="banner"] .header-link');
                const logo = brand.querySelector('.main-logo-image');
                const actionRect = action.getBoundingClientRect();
                const brandRect = brand.getBoundingClientRect();
                const actionStyle = getComputedStyle(action);
                const background = actionStyle.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
                const intersects = actionRect.left < brandRect.right && actionRect.right > brandRect.left
                    && actionRect.top < brandRect.bottom && actionRect.bottom > brandRect.top;
                return {
                    ActionVisible: actionStyle.display !== 'none' && actionStyle.visibility !== 'hidden'
                        && actionRect.width > 0 && actionRect.height > 0,
                    ActionText: action.innerText.trim().replace(/\s+/g, ' '),
                    ActionLeft: actionRect.left,
                    ActionRight: actionRect.right,
                    ActionWidth: actionRect.width,
                    ActionHeight: actionRect.height,
                    ActionOverlapsBrand: intersects,
                    ActionBackgroundAlpha: background[3] ?? 1,
                    LogoNaturalWidth: logo.naturalWidth,
                    LogoNaturalHeight: logo.naturalHeight,
                    LogoRenderedAspectRatio: logo.getBoundingClientRect().width / logo.getBoundingClientRect().height,
                    LogoNaturalAspectRatio: logo.naturalWidth / logo.naturalHeight
                };
            }
            """);

    private static Task<VisibleActionDiagnostics[]> MeasurePlayActionsAsync(IPage page) =>
        page.EvaluateAsync<VisibleActionDiagnostics[]>(
            """
            () => [...document.querySelectorAll('header[role="banner"] .join-game')]
                .map(action => {
                    const style = getComputedStyle(action);
                    const rect = action.getBoundingClientRect();
                    const brand = action.classList.contains('scrolled-button')
                        ? document.querySelector('.site-sticky-header-link')
                        : document.querySelector('header[role="banner"] .header-link');
                    const brandRect = brand?.getBoundingClientRect();
                    const background = style.backgroundColor.match(/[\d.]+/g)?.map(Number) ?? [];
                    return {
                        Name: action.getAttribute('aria-label'),
                        Text: action.innerText.trim().replace(/\s+/g, ' '),
                        IsScrolled: action.classList.contains('scrolled-button'),
                        Display: style.display,
                        Visibility: style.visibility,
                        Visible: style.display !== 'none' && style.visibility !== 'hidden'
                            && rect.width > 0 && rect.height > 0
                            && rect.right > 0 && rect.left < innerWidth
                            && rect.bottom > 0 && rect.top < innerHeight,
                        Left: rect.left,
                        Right: rect.right,
                        Top: rect.top,
                        Bottom: rect.bottom,
                        Width: rect.width,
                        Height: rect.height,
                        OverlapsBrand: brandRect
                            ? rect.left < brandRect.right && rect.right > brandRect.left
                                && rect.top < brandRect.bottom && rect.bottom > brandRect.top
                            : false,
                        BackgroundAlpha: background[3] ?? 1
                    };
                })
            """);

    private static string DescribeActions(IEnumerable<VisibleActionDiagnostics> actions) =>
        string.Join("; ", actions.Select(action =>
            $"{action.Name} ({action.Text}): display={action.Display}, visibility={action.Visibility}, " +
            $"rect={action.Left:F1},{action.Top:F1} {action.Width:F1}x{action.Height:F1}"));

    private static void AssertActionInsideViewport(
        string path,
        ViewportSize viewport,
        VisibleActionDiagnostics action)
    {
        Assert.Equal("Play the game", action.Name, ignoreCase: true);
        if (viewport.Width > 640)
        {
            Assert.Equal("PLAY THE GAME", action.Text);
        }
        else if (action.IsScrolled)
        {
            Assert.Equal("PLAY", action.Text);
        }
        else
        {
            Assert.Contains(action.Text, new[] { "PLAY", "PLAY THE GAME" });
        }
        Assert.True(action.Left >= -0.5 && action.Right <= viewport.Width + 0.5,
            $"{path} Play action left the viewport at {viewport.Width}x{viewport.Height}.");
        Assert.True(action.Top >= -0.5 && action.Bottom <= viewport.Height + 0.5,
            $"{path} Play action was clipped vertically at {viewport.Width}x{viewport.Height}.");
        Assert.True(action.Width >= 44 && action.Height >= 44,
            $"{path} Play action collapsed to {action.Width:F1}x{action.Height:F1}px at " +
            $"{viewport.Width}x{viewport.Height}.");
        Assert.False(action.OverlapsBrand,
            $"{path} Play action overlapped the brand at {viewport.Width}x{viewport.Height}.");
        Assert.True(action.BackgroundAlpha >= 0.99,
            $"{path} Play action lost its opaque fill at {viewport.Width}x{viewport.Height}.");
    }

    private sealed class HomepageHeaderDiagnostics
    {
        public bool ActionVisible { get; set; }
        public string ActionText { get; set; } = "";
        public double ActionLeft { get; set; }
        public double ActionRight { get; set; }
        public double ActionWidth { get; set; }
        public double ActionHeight { get; set; }
        public bool ActionOverlapsBrand { get; set; }
        public double ActionBackgroundAlpha { get; set; }
        public double LogoNaturalWidth { get; set; }
        public double LogoNaturalHeight { get; set; }
        public double LogoRenderedAspectRatio { get; set; }
        public double LogoNaturalAspectRatio { get; set; }
    }

    private sealed class VisibleActionDiagnostics
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsScrolled { get; set; }
        public string Display { get; set; } = "";
        public string Visibility { get; set; } = "";
        public bool Visible { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool OverlapsBrand { get; set; }
        public double BackgroundAlpha { get; set; }
    }

    private sealed class FilledActionDiagnostics
    {
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public string Foreground { get; set; } = "";
        public string Background { get; set; } = "";
        public double BackgroundAlpha { get; set; }
        public double TextContrast { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    private sealed class CompactHeaderDiagnostics
    {
        public string ActionText { get; set; } = "";
        public double ActionLeft { get; set; }
        public double ActionRight { get; set; }
        public double BrandLeft { get; set; }
        public double BrandRight { get; set; }
        public double LogoWidth { get; set; }
        public double LogoRenderedAspectRatio { get; set; }
        public double LogoNaturalAspectRatio { get; set; }
        public bool HasHorizontalOverflow { get; set; }
    }
}
