using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageChromeRegressionBrowserTests
{
    [Fact]
    public async Task LeaderboardPortraits_UseTheOriginalPhotoWhenGeneratedThumbnailsFail()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        await context.RouteAsync(
            "**/generated/thumbs/athletes/devarajan_narayanan_thumb_md.webp*",
            route => route.AbortAsync());
        await context.RouteAsync(
            "**/generated/thumbs/athletes/devarajan_narayanan_thumb_sm.webp*",
            route => route.AbortAsync());

        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?view=bortz", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('view-bortz')?.checked === true && document.querySelector('tr[data-athlete-name=\"Devarajan Narayanan\"] img.portrait')");

        var athleteRow = page.Locator("tr[data-athlete-name=\"Devarajan Narayanan\"]");
        var rowPortrait = athleteRow.Locator("img.portrait");
        await rowPortrait.ScrollIntoViewIfNeededAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.querySelector('tr[data-athlete-name="Devarajan Narayanan"] img.portrait');
                return image?.complete
                    && image.naturalWidth > 0
                    && image.src.includes('/athletes/devarajan_narayanan/devarajan_narayanan.webp');
            }
            """);

        Assert.DoesNotContain("portrait-fallback", await rowPortrait.GetAttributeAsync("class") ?? "");

        await athleteRow.Locator(".athlete-name").ClickAsync();
        await page.WaitForFunctionAsync(
            """
            () => {
                const image = document.getElementById('modalProfilePic');
                return document.getElementById('detailsModal')?.style.display === 'block'
                    && image?.complete
                    && image.naturalWidth > 0
                    && image.src.includes('/athletes/devarajan_narayanan/devarajan_narayanan.webp');
            }
            """);

        var modalPortrait = page.Locator("#modalProfilePic");
        Assert.DoesNotContain("portrait-fallback", await modalPortrait.GetAttributeAsync("class") ?? "");
    }

    [Fact]
    public async Task LeaderboardChangedControls_RetainTheMasterAttentionCue()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?view=bortz", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('view-bortz')?.checked === true && document.querySelector('.sidebar-toggle')?.classList.contains('has-active-state') === true");
        await page.Locator("#athleteSearch").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => getComputedStyle(document.getElementById('athleteSearch')).borderColor === 'rgb(255, 64, 129)'");

        var cueColors = await page.EvaluateAsync<string[]>(
            """
            () => {
                const toggle = document.querySelector('.sidebar-toggle');
                const trophy = document.querySelector('.sidebar-icon');
                const search = document.getElementById('athleteSearch');
                const searchIcon = document.querySelector('.search-icon');
                return [
                    getComputedStyle(toggle, '::after').backgroundColor,
                    getComputedStyle(trophy, '::after').backgroundColor,
                    getComputedStyle(search).borderColor,
                    getComputedStyle(searchIcon).color
                ];
            }
            """);

        Assert.All(cueColors, color => Assert.Equal("rgb(255, 64, 129)", color));
    }

    [Fact]
    public async Task HomepageLeaderboardsLink_IsCentered()
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

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 320, Height = 720 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);

            var description = page.Locator(".game-description");
            var link = description.Locator("a[href=\"/about\"]");
            Assert.Equal("center", await description.EvaluateAsync<string>("element => getComputedStyle(element).textAlign"));

            var horizontalOffset = await link.EvaluateAsync<double>(
                "element => { const rect = element.getBoundingClientRect(); return Math.abs((rect.left + rect.right) / 2 - document.documentElement.clientWidth / 2); }");
            Assert.InRange(horizontalOffset, 0, 1);
        }
    }

    [Fact]
    public async Task SharedHeaderBrand_HoverKeepsItsTextColorAndUsesPointerCursor()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        await AssertHoverKeepsColorAndUsesPointerAsync(
            page.Locator("header[role=\"banner\"] .header-link"));

        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("window.scrollTo(0, Math.min(700, document.documentElement.scrollHeight - innerHeight))");
        await SettleLayoutAsync(page);

        await page.Locator("#site-sticky-header")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await AssertHoverKeepsColorAndUsesPointerAsync(page.Locator(".site-sticky-header-link"));

        static async Task AssertHoverKeepsColorAndUsesPointerAsync(ILocator brand)
        {
            var colorBeforeHover = await brand.EvaluateAsync<string>("element => getComputedStyle(element).color");

            await brand.HoverAsync();

            var colorWhileHovered = await brand.EvaluateAsync<string>("element => getComputedStyle(element).color");
            var cursorWhileHovered = await brand.EvaluateAsync<string>("element => getComputedStyle(element).cursor");
            Assert.Equal(colorBeforeHover, colorWhileHovered);
            Assert.Equal("pointer", cursorWhileHovered);
        }
    }

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
    public async Task HomepagePrimaryAction_RetainsInvitationCuesAndHonorsReducedMotion()
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

        var action = page.Locator("body.home-page header[role=\"banner\"] .join-game:not(.scrolled-button)");
        var cues = await action.EvaluateAsync<InvitationCueDiagnostics>(
            """
            element => {
                const style = getComputedStyle(element);
                const halo = getComputedStyle(element, '::before');
                return {
                    BackgroundImage: style.backgroundImage,
                    Foreground: style.color,
                    BoxShadow: style.boxShadow,
                    AnimationName: style.animationName,
                    AnimationDuration: style.animationDuration,
                    AnimationIterationCount: style.animationIterationCount,
                    HaloDisplay: halo.display,
                    HaloPointerEvents: halo.pointerEvents,
                    PlayWeight: getComputedStyle(element.querySelector('strong')).fontWeight,
                    MiddleWeight: getComputedStyle(element.querySelector('.join-game-middle')).fontWeight,
                    GameWeight: getComputedStyle(element.querySelector('.join-game-end')).fontWeight,
                    Transform: style.transform
                };
            }
            """);

        Assert.Contains("linear-gradient", cues.BackgroundImage);
        Assert.Contains("rgb(76, 175, 80)", cues.BackgroundImage);
        Assert.Contains("rgb(102, 187, 106)", cues.BackgroundImage);
        Assert.Equal("rgb(255, 255, 255)", cues.Foreground);
        Assert.Equal("700", cues.PlayWeight);
        Assert.Equal("400", cues.MiddleWeight);
        Assert.Equal("700", cues.GameWeight);
        Assert.NotEqual("none", cues.BoxShadow);
        Assert.Equal("play-invitation", cues.AnimationName);
        Assert.Equal("0.88s", cues.AnimationDuration);
        Assert.Equal("3", cues.AnimationIterationCount);
        Assert.Equal("block", cues.HaloDisplay);
        Assert.Equal("none", cues.HaloPointerEvents);

        await action.HoverAsync();
        await page.WaitForTimeoutAsync(180);
        var hovered = await action.EvaluateAsync<InvitationCueDiagnostics>(
            """
            element => {
                const style = getComputedStyle(element);
                return {
                    BackgroundImage: style.backgroundImage,
                    BoxShadow: style.boxShadow,
                    AnimationName: style.animationName,
                    AnimationDuration: style.animationDuration,
                    AnimationIterationCount: style.animationIterationCount,
                    Transform: style.transform
                };
            }
            """);

        Assert.NotEqual("none", hovered.Transform);
        Assert.NotEqual(cues.BackgroundImage, hovered.BackgroundImage);

        await using var reducedContext = await NewContextAsync(browser, app, ReducedMotion.Reduce);
        var reducedPage = await reducedContext.NewPageAsync();
        await reducedPage.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(reducedPage);
        var reduced = await reducedPage
            .Locator("body.home-page header[role=\"banner\"] .join-game:not(.scrolled-button)")
            .EvaluateAsync<InvitationCueDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    return {
                        BackgroundImage: style.backgroundImage,
                        BoxShadow: style.boxShadow,
                        AnimationName: style.animationName
                    };
                }
                """);

        Assert.Contains("linear-gradient", reduced.BackgroundImage);
        Assert.NotEqual("none", reduced.BoxShadow);
        Assert.Equal("none", reduced.AnimationName);
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
    public async Task StickyPlayAction_RemainsAWorkingRouteToTheGame()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(305, 844);
        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("window.scrollTo(0, Math.min(700, document.documentElement.scrollHeight - innerHeight))");
        await SettleLayoutAsync(page);

        var stickyAction = page.Locator("header[role=\"banner\"] .join-game.scrolled-button");
        await stickyAction.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("PLAY", (await stickyAction.InnerTextAsync()).Trim());

        var fit = await page.EvaluateAsync<StickyActionFitDiagnostics>(
            """
            () => {
                const action = document.querySelector('header[role="banner"] .join-game.scrolled-button');
                const stickyHeader = document.getElementById('site-sticky-header');
                const actionRect = action.getBoundingClientRect();
                const stickyRect = stickyHeader.getBoundingClientRect();
                const halo = getComputedStyle(action, '::before');
                const haloTop = actionRect.top + parseFloat(halo.top);
                const haloBottom = actionRect.bottom - parseFloat(halo.bottom);
                return {
                    ActionWidth: actionRect.width,
                    ActionHeight: actionRect.height,
                    StickyTop: stickyRect.top,
                    StickyBottom: stickyRect.bottom,
                    HaloTop: haloTop,
                    HaloBottom: haloBottom,
                    CenterOffset: Math.abs(
                        (haloTop + haloBottom) / 2
                        - (stickyRect.top + stickyRect.bottom) / 2)
                };
            }
            """);

        Assert.True(fit.HaloTop >= fit.StickyTop - 0.5,
            $"Compact Play halo escaped above the sticky header: {fit.HaloTop:F1}px < {fit.StickyTop:F1}px.");
        Assert.True(fit.HaloBottom <= fit.StickyBottom + 0.5,
            $"Compact Play halo escaped below the sticky header: {fit.HaloBottom:F1}px > {fit.StickyBottom:F1}px.");
        Assert.InRange(fit.CenterOffset, 0, 1);
        Assert.True(fit.ActionWidth > fit.ActionHeight,
            $"Compact Play action became too square: {fit.ActionWidth:F1}x{fit.ActionHeight:F1}px.");

        await stickyAction.ClickAsync();
        await page.WaitForURLAsync("**/play");
        Assert.EndsWith("/play", new Uri(page.Url).AbsolutePath, StringComparison.Ordinal);
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

    private static async Task<IBrowserContext> NewContextAsync(
        IBrowser browser,
        BrowserTestApp app,
        ReducedMotion? reducedMotion = null)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
            ReducedMotion = reducedMotion
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
                        Foreground: style.color,
                        BackgroundImage: style.backgroundImage,
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
        if (action.IsScrolled)
        {
            Assert.Equal("PLAY", action.Text);
        }
        else if (viewport.Width > 640)
        {
            Assert.Equal("PLAY THE GAME", action.Text);
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
        Assert.Equal("rgb(255, 255, 255)", action.Foreground);
        Assert.Contains("rgb(76, 175, 80)", action.BackgroundImage);
        Assert.Contains("rgb(102, 187, 106)", action.BackgroundImage);
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
        public string Foreground { get; set; } = "";
        public string BackgroundImage { get; set; } = "";
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

    private sealed class StickyActionFitDiagnostics
    {
        public double ActionWidth { get; set; }
        public double ActionHeight { get; set; }
        public double StickyTop { get; set; }
        public double StickyBottom { get; set; }
        public double HaloTop { get; set; }
        public double HaloBottom { get; set; }
        public double CenterOffset { get; set; }
    }

    private sealed class InvitationCueDiagnostics
    {
        public string BackgroundImage { get; set; } = "";
        public string Foreground { get; set; } = "";
        public string BoxShadow { get; set; } = "";
        public string AnimationName { get; set; } = "";
        public string AnimationDuration { get; set; } = "";
        public string AnimationIterationCount { get; set; } = "";
        public string HaloDisplay { get; set; } = "";
        public string HaloPointerEvents { get; set; } = "";
        public string PlayWeight { get; set; } = "";
        public string MiddleWeight { get; set; } = "";
        public string GameWeight { get; set; } = "";
        public string Transform { get; set; } = "";
    }
}
