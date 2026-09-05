using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class HomepageChromeRegressionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task HomepageViewAllAthletesButton_ShowsLoadedAthleteCount()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'");

        var athleteCount = await page.EvaluateAsync<int>(
            "() => window.getSharedAthletes().then(athletes => athletes.length)");
        var button = page.Locator("#viewAllAthletesBtn");

        Assert.True(athleteCount > 0);
        Assert.Equal($"VIEW ALL ATHLETES ({athleteCount})", (await button.InnerTextAsync()).Trim());
        Assert.Equal("false", await button.GetAttributeAsync("data-use-default-leaderboard-url"));
    }

    [Fact]
    public async Task LeaderboardPortraits_UseTheOriginalPhotoWhenGeneratedThumbnailsFail()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        await context.RouteAsync(
            "**/generated/thumbs/athletes/devarajan_narayanan_thumb_md_*.webp*",
            route => route.AbortAsync());
        await context.RouteAsync(
            "**/generated/thumbs/athletes/devarajan_narayanan_thumb_sm_*.webp*",
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
                    && image.src.includes('/generated/profiles/athletes/devarajan_narayanan_')
                    && image.src.includes('.webp');
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
                    && image.src.includes('/generated/profiles/athletes/devarajan_narayanan_')
                    && image.src.includes('.webp');
            }
            """);

        var modalPortrait = page.Locator("#modalProfilePic");
        Assert.DoesNotContain("portrait-fallback", await modalPortrait.GetAttributeAsync("class") ?? "");
    }

    [Fact]
    public async Task LeaderboardChangedControls_ShowTheSelectionCountAlongsideSearchFocus()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app, ReducedMotion.NoPreference);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/leaderboard?view=bortz", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('view-bortz')?.checked === true && document.querySelector('.sidebar-toggle')?.classList.contains('has-active-state') === true");
        await page.Locator("#athleteSearch").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => getComputedStyle(document.getElementById('athleteSearch')).borderColor === 'rgb(8, 118, 133)'");

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

        Assert.Equal(["rgb(8, 118, 133)", "rgb(255, 64, 129)", "rgb(8, 118, 133)", "rgb(8, 118, 133)"], cueColors);
        Assert.Equal("1", await page.Locator(".sidebar-toggle").GetAttributeAsync("data-filter-count"));
        Assert.Equal("1 active filter", await page.Locator(".sidebar-toggle").GetAttributeAsync("aria-description"));
    }

    [Fact]
    public async Task HomepageLeaderboardsLink_IsCentered()
    {
        var app = App;
        var browser = Browser;
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
    public async Task HomepageSectionFooterLinks_AreCenteredWithinTheirSections()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        foreach (var selector in new[]
                 {
                     "#hall-of-fame > p",
                     "#faq .homepage-faq-more"
                 })
        {
            var footer = page.Locator(selector);
            Assert.Equal("center", await footer.EvaluateAsync<string>("element => getComputedStyle(element).textAlign"));

            var horizontalOffset = await footer.Locator("a").EvaluateAsync<double>(
                "element => { const link = element.getBoundingClientRect(); const section = element.closest('.section-container').getBoundingClientRect(); return Math.abs((link.left + link.right - section.left - section.right) / 2); }");
            Assert.InRange(horizontalOffset, 0, 1);
        }
    }

    internal const string MeasureFilledActionScript =
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

    internal static async Task<IBrowserContext> NewContextAsync(
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
        return context;
    }

    internal static async Task SettleLayoutAsync(IPage page)
    {
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    internal static async Task ScrollToStablePositionAsync(IPage page, int requestedTop)
    {
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync(
            """
            requestedTop => new Promise((resolve, reject) => {
                const positionTolerance = 0.5;
                const stableDuration = 32;
                const minimumStableSamples = 2;
                const timeout = 5000;
                const startedAt = performance.now();
                let stableSince = null;
                let stableSamples = 0;
                let previousTarget = null;

                const sample = now => {
                    const maxScroll = Math.max(0, document.documentElement.scrollHeight - innerHeight);
                    const target = Math.min(Math.max(0, requestedTop), maxScroll);
                    const targetChanged = previousTarget === null
                        || Math.abs(target - previousTarget) > positionTolerance;
                    const atTarget = Math.abs(scrollY - target) <= positionTolerance;

                    if (!atTarget) {
                        window.scrollTo({ top: target, behavior: 'instant' });
                        stableSince = null;
                        stableSamples = 0;
                    } else if (targetChanged) {
                        stableSince = now;
                        stableSamples = 1;
                    } else {
                        stableSince ??= now;
                        stableSamples += 1;
                    }

                    previousTarget = target;
                    if (stableSince !== null
                        && stableSamples >= minimumStableSamples
                        && now - stableSince >= stableDuration) {
                        resolve();
                        return;
                    }

                    if (now - startedAt >= timeout) {
                        reject(new Error(
                            `Scroll position did not stabilize: requested=${requestedTop}, `
                            + `target=${target}, scrollY=${scrollY}, maxScroll=${maxScroll}.`));
                        return;
                    }

                    requestAnimationFrame(sample);
                };

                const initialMaxScroll = Math.max(0, document.documentElement.scrollHeight - innerHeight);
                window.scrollTo({
                    top: Math.min(Math.max(0, requestedTop), initialMaxScroll),
                    behavior: 'instant'
                });
                requestAnimationFrame(sample);
            })
            """,
            requestedTop);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    internal static Task<HomepageHeaderDiagnostics> MeasureHomepageHeaderAsync(IPage page) =>
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

    internal static Task<VisibleActionDiagnostics[]> MeasurePlayActionsAsync(IPage page) =>
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
                        ScrollY: scrollY,
                        MaxScroll: Math.max(0, document.documentElement.scrollHeight - innerHeight),
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

    internal static string DescribeActions(IEnumerable<VisibleActionDiagnostics> actions) =>
        string.Join("; ", actions.Select(action =>
            $"{action.Name} ({action.Text}): display={action.Display}, visibility={action.Visibility}, " +
            $"scrolled={action.IsScrolled}, scrollY={action.ScrollY:F1}/{action.MaxScroll:F1}, " +
            $"rect={action.Left:F1},{action.Top:F1} " +
            $"{action.Width:F1}x{action.Height:F1}"));

    internal static bool IsActionFullyInsideViewport(
        ViewportSize viewport,
        VisibleActionDiagnostics action) =>
        action.Visible
        && action.Left >= -0.5
        && action.Right <= viewport.Width + 0.5
        && action.Top >= -0.5
        && action.Bottom <= viewport.Height + 0.5;

    internal static void AssertActionInsideViewport(
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
        Assert.Contains("rgb(31, 111, 53)", action.BackgroundImage);
        Assert.Contains("rgb(46, 125, 50)", action.BackgroundImage);
    }

    internal sealed class HomepageHeaderDiagnostics
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

    internal sealed class ContributePreviewDiagnostics
    {
        public string Hash { get; set; } = "";
        public double SectionTop { get; set; }
        public double QrTop { get; set; }
        public double QrBottom { get; set; }
        public double AddressTop { get; set; }
        public double AddressBottom { get; set; }
        public double ViewportHeight { get; set; }
    }

    internal sealed class VisibleActionDiagnostics
    {
        public string Name { get; set; } = "";
        public string Text { get; set; } = "";
        public string Foreground { get; set; } = "";
        public string BackgroundImage { get; set; } = "";
        public bool IsScrolled { get; set; }
        public string Display { get; set; } = "";
        public string Visibility { get; set; } = "";
        public double ScrollY { get; set; }
        public double MaxScroll { get; set; }
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

    internal sealed class FilledActionDiagnostics
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

    internal sealed class CompactHeaderDiagnostics
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

    internal sealed class StickyActionFitDiagnostics
    {
        public double ActionWidth { get; set; }
        public double ActionHeight { get; set; }
        public double StickyTop { get; set; }
        public double StickyBottom { get; set; }
        public double HaloTop { get; set; }
        public double HaloBottom { get; set; }
        public double CenterOffset { get; set; }
    }

    internal sealed class InvitationCueDiagnostics
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
