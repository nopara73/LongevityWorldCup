using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class AestheticSystemBrowserTests
{
    private static readonly string[] RepresentativePaths =
    [
        "/",
        "/leaderboard",
        "/events",
        "/longevitymaxxing",
        "/play",
        "/apply",
        "/pheno-age",
        "/ruleset"
    ];

    [Fact]
    public async Task DarkTheme_RendersWithTheIndependentSemanticPalette()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ColorScheme = ColorScheme.Dark,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Dark Theme Athlete',
                DisplayName: 'Dark Theme Athlete',
                AccountEmail: 'dark-theme@example.test',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            window.sessionStorage.setItem('contactEmail', 'dark-theme@example.test');
            window.sessionStorage.setItem('came-from', 'proof-upload');
            """);

        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");

        var theme = await page.EvaluateAsync<DarkThemeDiagnostics>(
            """
            () => {
                const root = getComputedStyle(document.documentElement);
                const body = getComputedStyle(document.body);
                const input = getComputedStyle(document.querySelector('input:not([type="hidden"])'));
                return {
                    DarkPreferenceMatches: matchMedia('(prefers-color-scheme: dark)').matches,
                    ColorScheme: root.colorScheme,
                    CanvasToken: root.getPropertyValue('--lwc-canvas').trim(),
                    SurfaceToken: root.getPropertyValue('--lwc-surface').trim(),
                    InkToken: root.getPropertyValue('--lwc-ink').trim(),
                    BodyBackground: body.backgroundColor,
                    BodyColor: body.color,
                    InputBackground: input.backgroundColor,
                    InputColor: input.color
                };
            }
            """);

        Assert.True(theme.DarkPreferenceMatches);
        Assert.Contains("dark", theme.ColorScheme);
        Assert.Equal("#0d141b", theme.CanvasToken);
        Assert.Equal("#17212b", theme.SurfaceToken);
        Assert.Equal("#e6edf3", theme.InkToken);
        Assert.Equal("rgb(13, 20, 27)", theme.BodyBackground);
        Assert.Equal("rgb(230, 237, 243)", theme.BodyColor);
        Assert.Equal("rgb(23, 33, 43)", theme.InputBackground);
        Assert.Equal("rgb(230, 237, 243)", theme.InputColor);

        var readOnly = await page.Locator("#name").EvaluateAsync<DarkControlDiagnostics>(
            """
            async element => {
                element.value = 'Dark Theme Athlete';
                element.readOnly = true;
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                await Promise.all(element.getAnimations().map(animation => animation.finished.catch(() => {})));
                const style = getComputedStyle(element);
                const stylesheet = [...document.querySelectorAll('link[rel="stylesheet"]')]
                    .find(link => link.href.includes('/css/aesthetic-system.css'));
                const stylesheetText = stylesheet
                    ? await fetch(stylesheet.href).then(response => response.text())
                    : '';
                return {
                    Background: style.backgroundColor,
                    Color: style.color,
                    BorderColor: style.borderTopColor,
                    IsReadOnly: element.readOnly,
                    MatchesReadOnly: element.matches(':read-only'),
                    ServedOverridePresent: stylesheetText.includes('background: var(--lwc-surface-muted) !important')
                };
            }
            """);

        Assert.True(readOnly.IsReadOnly);
        Assert.True(readOnly.MatchesReadOnly);
        Assert.True(readOnly.ServedOverridePresent);
        Assert.Equal("rgb(32, 45, 56)", readOnly.Background);
        Assert.Equal("rgb(174, 189, 202)", readOnly.Color);
        Assert.Equal("rgb(109, 125, 139)", readOnly.BorderColor);

        await NavigateAndSettleAsync(page, "/proofs");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true' " +
            "&& document.getElementById('mainProofInstructions')?.textContent.trim().length > 0");
        var proof = await page.EvaluateAsync<DarkProofDiagnostics>(
            """
            () => {
                const mainCopy = getComputedStyle(document.getElementById('mainProofInstructions'));
                const supportingCopy = getComputedStyle(document.getElementById('subProofInstructions'));
                const action = document.getElementById('uploadProofButton');
                const actionStyle = getComputedStyle(action);
                return {
                    HasExpectedAthleteCopy: document.getElementById('character-title').textContent.includes('Dark Theme Athlete'),
                    MainCopyColor: mainCopy.color,
                    SupportingCopyColor: supportingCopy.color,
                    FilledActionBackground: actionStyle.backgroundColor,
                    FilledActionColor: actionStyle.color,
                    FilledActionDisabled: action.disabled
                };
            }
            """);

        Assert.True(proof.HasExpectedAthleteCopy);
        Assert.Equal("rgb(230, 237, 243)", proof.MainCopyColor);
        Assert.Equal("rgb(230, 237, 243)", proof.SupportingCopyColor);
        Assert.Equal("rgb(8, 118, 133)", proof.FilledActionBackground);
        Assert.Equal("rgb(255, 255, 255)", proof.FilledActionColor);
        Assert.False(proof.FilledActionDisabled);
        Assert.NotEqual(readOnly.Background, proof.FilledActionBackground);
        Assert.NotEqual(readOnly.Color, proof.FilledActionColor);

        await NavigateAndSettleAsync(page, "/review");
        await page.WaitForSelectorAsync(".application-review-copy.primary");
        var review = await page.EvaluateAsync<DarkReviewDiagnostics>(
            """
            () => {
                const panel = getComputedStyle(document.querySelector('.application-review-panel'));
                const primaryCopy = getComputedStyle(document.querySelector('.application-review-copy.primary'));
                const secondaryCopy = getComputedStyle(document.querySelector('.application-review-copy.contact'));
                const secondaryAction = getComputedStyle(document.querySelector('.application-review-actions .back-button'));
                return {
                    ShowsResultReview: document.getElementById('appReviewText').textContent.trim() === 'Result review',
                    PanelBackground: panel.backgroundColor,
                    PrimaryCopyBackground: primaryCopy.backgroundColor,
                    PrimaryCopyColor: primaryCopy.color,
                    SecondaryCopyColor: secondaryCopy.color,
                    SecondaryActionBackground: secondaryAction.backgroundColor,
                    SecondaryActionColor: secondaryAction.color
                };
            }
            """);

        Assert.True(review.ShowsResultReview);
        Assert.Equal("rgb(23, 33, 43)", review.PanelBackground);
        Assert.Equal("rgb(23, 53, 37)", review.PrimaryCopyBackground);
        Assert.Equal("rgb(230, 237, 243)", review.PrimaryCopyColor);
        Assert.Equal("rgb(230, 237, 243)", review.SecondaryCopyColor);
        Assert.Equal("rgb(32, 45, 56)", review.SecondaryActionBackground);
        Assert.Equal("rgb(230, 237, 243)", review.SecondaryActionColor);
    }

    [Fact]
    public async Task ReducedMotion_DisablesThePlayEntranceMotion()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ReducedMotion = ReducedMotion.Reduce,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });

        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/play");

        var motion = await page.EvaluateAsync<MotionDiagnostics>(
            """
            () => {
                document.body.classList.add('play-start-active', 'play-start-intro');
                const logo = getComputedStyle(document.querySelector('.play-logo-watermark'));
                const wordmark = getComputedStyle(document.querySelector('.play-menu-wordmark'));
                const action = getComputedStyle(document.querySelector('.play-menu-actions .flow-action'));
                return {
                    ReducedMotionMatches: matchMedia('(prefers-reduced-motion: reduce)').matches,
                    RootScrollBehavior: getComputedStyle(document.documentElement).scrollBehavior,
                    LogoAnimationName: logo.animationName,
                    WordmarkAnimationName: wordmark.animationName,
                    ActionAnimationName: action.animationName
                };
            }
            """);

        Assert.True(motion.ReducedMotionMatches);
        Assert.Equal("auto", motion.RootScrollBehavior);
        Assert.Equal("none", motion.LogoAnimationName);
        Assert.Equal("none", motion.WordmarkAnimationName);
        Assert.Equal("none", motion.ActionAnimationName);
    }

    [Fact]
    public async Task ForcedColorsAndHigherContrast_PreserveVisibleControlBoundariesAndFocus()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                Contrast = Contrast.More,
                ForcedColors = ForcedColors.Active,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });

        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/apply");

        var input = page.Locator("input:not([type=\"hidden\"])").First;
        await input.FocusAsync();
        var contrast = await input.EvaluateAsync<ContrastDiagnostics>(
            """
            element => {
                const style = getComputedStyle(element);
                return {
                    ForcedColorsMatches: matchMedia('(forced-colors: active)').matches,
                    MoreContrastMatches: matchMedia('(prefers-contrast: more)').matches,
                    ForcedColorAdjust: style.forcedColorAdjust,
                    BorderWidth: parseFloat(style.borderTopWidth),
                    OutlineWidth: parseFloat(style.outlineWidth),
                    OutlineStyle: style.outlineStyle
                };
            }
            """);

        Assert.True(contrast.ForcedColorsMatches);
        Assert.True(contrast.MoreContrastMatches);
        Assert.Equal("auto", contrast.ForcedColorAdjust);
        Assert.True(contrast.BorderWidth >= 2, $"Expected a 2px high-contrast boundary, got {contrast.BorderWidth}px.");
        Assert.True(contrast.OutlineWidth >= 3, $"Expected a 3px forced-color focus outline, got {contrast.OutlineWidth}px.");
        Assert.NotEqual("none", contrast.OutlineStyle);
    }

    [Fact]
    public async Task ColorVisionDeficiencyEmulation_PreservesNonColorStateMeaning()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var page = await context.NewPageAsync();
        var cdp = await context.NewCDPSessionAsync(page);

        foreach (var deficiency in new[] { "protanopia", "deuteranopia", "tritanopia", "achromatopsia" })
        {
            await cdp.SendAsync(
                "Emulation.setEmulatedVisionDeficiency",
                new Dictionary<string, object> { ["type"] = deficiency });

            await NavigateAndSettleAsync(page, "/longevitymaxxing");
            var selectedAndStatus = await page.EvaluateAsync<ColorIndependentStateDiagnostics>(
                """
                () => {
                    const selected = document.querySelector('#lmxAccessTabs .lmx-tab[aria-selected="true"]');
                    const selectedStyle = getComputedStyle(selected);
                    const status = document.getElementById('lmxSignupStatus');
                    status.textContent = 'Signup ready — check your email for the next step.';
                    status.classList.add('success');
                    const statusStyle = getComputedStyle(status);
                    const statusRect = status.getBoundingClientRect();
                    return {
                        SelectedHasText: selected.textContent.trim().length > 0,
                        SelectedIsProgrammaticallyExposed: selected.getAttribute('aria-selected') === 'true',
                        SelectedHasShapeIndicator: selectedStyle.boxShadow !== 'none'
                            || parseFloat(selectedStyle.borderBottomWidth) >= 2,
                        StatusHasText: status.textContent.trim().length > 0,
                        StatusHasShapeIndicator: parseFloat(statusStyle.borderLeftWidth) >= 4
                            || parseFloat(statusStyle.borderRightWidth) >= 4,
                        StatusIsVisible: statusRect.width > 0 && statusRect.height > 0
                            && statusStyle.display !== 'none' && statusStyle.visibility !== 'hidden'
                    };
                }
                """);

            Assert.True(selectedAndStatus.SelectedHasText, $"Selected tab lost its text under {deficiency} emulation.");
            Assert.True(selectedAndStatus.SelectedIsProgrammaticallyExposed,
                $"Selected tab lost aria-selected under {deficiency} emulation.");
            Assert.True(selectedAndStatus.SelectedHasShapeIndicator,
                $"Selected tab had no underline/border indicator under {deficiency} emulation.");
            Assert.True(selectedAndStatus.StatusHasText, $"Status meaning relied on color under {deficiency} emulation.");
            Assert.True(selectedAndStatus.StatusHasShapeIndicator,
                $"Status had no non-color border indicator under {deficiency} emulation.");
            Assert.True(selectedAndStatus.StatusIsVisible, $"Status text was not visible under {deficiency} emulation.");

            await NavigateAndSettleAsync(page, "/apply");
            var error = await page.EvaluateAsync<ColorIndependentErrorDiagnostics>(
                """
                () => {
                    const input = document.getElementById('name');
                    const message = document.getElementById('nameError');
                    input.setAttribute('aria-invalid', 'true');
                    message.textContent = 'Name must contain at least three readable characters.';
                    const messageStyle = getComputedStyle(message);
                    const messageRect = message.getBoundingClientRect();
                    return {
                        ErrorHasText: message.textContent.trim().length > 0,
                        ErrorIsDescribedByControl: input.getAttribute('aria-describedby')
                            ?.split(/\s+/).includes(message.id) === true,
                        ErrorIsProgrammaticallyExposed: input.getAttribute('aria-invalid') === 'true',
                        ErrorHasShapeIndicator: parseFloat(messageStyle.borderLeftWidth) >= 4
                            || parseFloat(messageStyle.borderRightWidth) >= 4,
                        ErrorIsVisible: messageRect.width > 0 && messageRect.height > 0
                            && messageStyle.display !== 'none' && messageStyle.visibility !== 'hidden'
                    };
                }
                """);

            Assert.True(error.ErrorHasText, $"Error meaning relied on color under {deficiency} emulation.");
            Assert.True(error.ErrorIsDescribedByControl,
                $"Error text was not associated with its control under {deficiency} emulation.");
            Assert.True(error.ErrorIsProgrammaticallyExposed,
                $"Invalid state was not exposed under {deficiency} emulation.");
            Assert.True(error.ErrorHasShapeIndicator,
                $"Error state had no non-color border indicator under {deficiency} emulation.");
            Assert.True(error.ErrorIsVisible, $"Error text was not visible under {deficiency} emulation.");
        }

        await cdp.SendAsync(
            "Emulation.setEmulatedVisionDeficiency",
            new Dictionary<string, object> { ["type"] = "none" });
        await cdp.DetachAsync();
    }

    [Fact]
    public async Task RepresentativePages_DoNotOverflowAtNarrowShortAndLandscapeViewports()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var page = await context.NewPageAsync();

        var viewports = new[]
        {
            new ViewportSize { Width = 360, Height = 640 },
            new ViewportSize { Width = 844, Height = 390 },
            new ViewportSize { Width = 1280, Height = 350 }
        };

        foreach (var viewport in viewports)
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            foreach (var path in RepresentativePaths)
            {
                await NavigateAndSettleAsync(page, path);
                var layout = await MeasureLayoutAsync(page);

                Assert.True(layout.HasVisibleContent, $"{path} rendered no visible main content at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    layout.HorizontalOverflow <= 1,
                    $"{path} overflowed horizontally by {layout.HorizontalOverflow}px at {viewport.Width}x{viewport.Height}. " +
                    $"scrollWidth={layout.ScrollWidth}, clientWidth={layout.ClientWidth}.");
            }
        }
    }

    [Fact]
    public async Task FallbackErrorArtwork_DecodesAndRemainsVisibleAcrossResponsiveLayouts()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var page = await context.NewPageAsync();
        var viewports = new[]
        {
            new ViewportSize { Width = 1280, Height = 800 },
            new ViewportSize { Width = 390, Height = 844 },
            new ViewportSize { Width = 640, Height = 390 }
        };

        foreach (var viewport in viewports)
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            foreach (var path in new[] { "/error/502.html", "/error/503.html", "/error/504.html" })
            {
                await NavigateAndSettleAsync(page, path);
                await page.WaitForFunctionAsync(
                    "() => { const image = document.querySelector('.visual img'); return image?.complete && image.naturalWidth > 0; }");

                var artwork = page.Locator(".visual img");
                var bounds = await artwork.BoundingBoxAsync();
                Assert.NotNull(bounds);
                Assert.True(
                    await artwork.EvaluateAsync<bool>(
                        "image => image.complete && image.naturalWidth > 0 && getComputedStyle(image).display !== 'none' && getComputedStyle(image).visibility !== 'hidden'"),
                    $"{path} artwork did not decode visibly at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    bounds.Width >= 90 && bounds.Height >= 90,
                    $"{path} artwork collapsed to {bounds.Width}x{bounds.Height}px at {viewport.Width}x{viewport.Height}.");
                Assert.True(
                    await page.EvaluateAsync<bool>(
                        "() => Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) <= window.innerWidth + 1"),
                    $"{path} overflowed horizontally at {viewport.Width}x{viewport.Height}.");
            }
        }
    }

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

        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
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
        foreach (var viewportGroup in boundaryCases.GroupBy(item => (item.Width, item.Height)))
        {
            await page.SetViewportSizeAsync(viewportGroup.Key.Width, viewportGroup.Key.Height);
            var queries = viewportGroup
                .Select(item => item.Branch.Query)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var matches = await page.EvaluateAsync<bool[]>(
                "queries => queries.map(query => matchMedia(query).matches)",
                queries);
            var matchesByQuery = queries
                .Select((query, index) => (query, matches[index]))
                .ToDictionary(item => item.query, item => item.Item2, StringComparer.Ordinal);

            foreach (var boundaryCase in viewportGroup)
            {
                results[boundaryCase] = matchesByQuery[boundaryCase.Branch.Query];
            }
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

        foreach (var viewportGroup in scriptBoundaryCases.GroupBy(item => (item.Width, item.Height)))
        {
            await page.SetViewportSizeAsync(viewportGroup.Key.Width, viewportGroup.Key.Height);
            var actualViewport = await page.EvaluateAsync<ScriptViewportDiagnostics>(
                "() => ({ Width: window.innerWidth, Height: window.innerHeight })");
            Assert.Equal(viewportGroup.Key.Width, actualViewport.Width);
            Assert.Equal(viewportGroup.Key.Height, actualViewport.Height);
            foreach (var scriptCase in viewportGroup)
            {
                var actualValue = scriptCase.Threshold.Axis == "width"
                    ? actualViewport.Width
                    : actualViewport.Height;
                Assert.Equal(
                    scriptCase.ExpectedMatch,
                    EvaluateComparison(actualValue, scriptCase.Threshold.Operator, scriptCase.Threshold.Value));
            }
        }

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

        foreach (var routeGroup in layoutProbes)
        {
            var probes = routeGroup
                .OrderBy(item => item.Width)
                .ThenBy(item => item.Height)
                .ToArray();
            await page.SetViewportSizeAsync(probes[0].Width, probes[0].Height);
            await NavigateAndSettleAsync(page, routeGroup.Key);

            foreach (var probe in probes)
            {
                await page.SetViewportSizeAsync(probe.Width, probe.Height);
                await page.EvaluateAsync(
                    "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
                var layout = await MeasureLayoutAsync(page);
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
        Assert.NotEmpty(containerInventory);
        await AssertResponsiveContainerBoundariesAsync(page, containerInventory);
    }

    [Fact]
    public async Task RepresentativePages_ReflowAtFourHundredPercentZoomApproximation()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                // A 320 CSS-pixel viewport approximates viewing a 1280px desktop
                // page at 400% browser zoom, where the layout viewport reflows.
                ViewportSize = new ViewportSize { Width = 320, Height = 720 }
            });
        var page = await context.NewPageAsync();

        foreach (var path in RepresentativePaths)
        {
            await NavigateAndSettleAsync(page, path);
            var layout = await MeasureLayoutAsync(page);

            Assert.True(layout.RootFontSize >= 16, $"{path} reduced the root font below 16px during reflow.");
            Assert.True(
                layout.HorizontalOverflow <= 1,
                $"{path} did not reflow at the 400% zoom approximation: " +
                $"overflow={layout.HorizontalOverflow}px, scrollWidth={layout.ScrollWidth}, clientWidth={layout.ClientWidth}.");
        }
    }

    [Fact]
    public async Task LongLocalizedContent_ReflowsWithoutClippingAtThreeHundredTwentyPixels()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 320, Height = 720 }
            });
        await AddRouteStressStateAsync(context);
        var page = await context.NewPageAsync();

        await NavigateAndSettleAsync(page, "/leaderboard");
        await page.WaitForSelectorAsync(
            ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible });
        var leaderboard = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                [".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name"] =
                    "Alexandria-Cassandra von Hohenlohe-Longevity-Research-Collective",
                [".leaderboard-view-switcher .view-badge"] =
                    "Classement général de toutes les catégories biologiques",
                [".ranking-explanation"] =
                    "Classement international calculé à partir de la réduction de l’âge biologique, " +
                    "avec départage transparent selon les mesures admissibles les plus récentes."
            },
            [
                ".leaderboard tbody:not(.loading-skeleton) tr[data-athlete-name] .athlete-name",
                ".leaderboard-view-switcher .view-badge",
                ".ranking-explanation"
            ]);
        AssertLongContentLayout("/leaderboard", leaderboard);

        await NavigateAndSettleAsync(page, "/apply");
        var form = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                ["label[for=\"name\"]"] =
                    "Nom complet ou pseudonyme public international de l’athlète (obligatoire)",
                ["#nextButton .flow-action__label"] =
                    "Continuer vers l’étape suivante de la candidature internationale"
            },
            ["label[for=\"name\"]", "#nextButton"]);
        AssertLongContentLayout("/apply", form);

        await NavigateAndSettleAsync(page, "/play");
        var actions = await InjectAndMeasureLongContentAsync(
            page,
            new Dictionary<string, string>
            {
                ["#newGameBtn .flow-action__label"] =
                    "Je souhaite participer pour la toute première fois à cette compétition",
                ["#continueGameBtn .flow-action__label"] =
                    "Je participe déjà en tant qu’athlète international enregistré"
            },
            ["#newGameBtn", "#continueGameBtn"]);
        AssertLongContentLayout("/play", actions);

        var canonicalRoutes = GetCanonicalFirstPartyRoutes();
        Assert.NotEmpty(canonicalRoutes);
        foreach (var path in canonicalRoutes)
        {
            await NavigateAndSettleAsync(page, path);
            var stress = await InjectAndMeasureExtremeContentAsync(page);
            AssertExtremeContentLayout(path, stress);
        }
    }

    [Fact]
    public async Task MobileDocumentationNavigation_UsesProgressiveDisclosureAndLargeTargets()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });
        var page = await context.NewPageAsync();

        await NavigateAndSettleAsync(page, "/history");
        var toggle = page.Locator(".documentation-nav-toggle");
        var links = page.Locator(".documentation-nav-links");
        var heading = page.Locator(".documentation-document h1");

        Assert.True(await toggle.IsVisibleAsync());
        Assert.False(await links.IsVisibleAsync());
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));
        var toggleBox = await toggle.BoundingBoxAsync();
        var headingBox = await heading.BoundingBoxAsync();
        Assert.NotNull(toggleBox);
        Assert.NotNull(headingBox);
        Assert.True(toggleBox.Height >= 44, $"Documentation disclosure measured {toggleBox.Height}px high.");
        Assert.True(headingBox.Y < 260, $"Collapsed navigation delayed the History heading to y={headingBox.Y}px.");

        await toggle.ClickAsync();
        Assert.True(await links.IsVisibleAsync());
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));

        var targets = page.Locator(".documentation-nav-links a");
        var targetCount = await targets.CountAsync();
        Assert.True(targetCount > 8, "History should retain its detailed document navigation inside the disclosure.");
        for (var index = 0; index < targetCount; index++)
        {
            var box = await targets.Nth(index).BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Height >= 44, $"Documentation navigation target {index + 1} measured {box.Height}px high.");
        }

        var nestedMargin = await page.Locator(".documentation-nav-level-3").First.EvaluateAsync<double>(
            "element => parseFloat(getComputedStyle(element).marginLeft)");
        Assert.True(nestedMargin >= 8, $"Nested documentation hierarchy lost its indent ({nestedMargin}px).");
    }

    [Fact]
    public async Task SharedInteractionStates_MeetComputedContrastAcrossLightDarkAndHigherContrastModes()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);

        foreach (var mode in new[] { "light", "dark", "higher-contrast" })
        {
            var options = new BrowserNewContextOptions
            {
                ColorScheme = mode == "dark" ? ColorScheme.Dark : ColorScheme.Light,
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            };
            if (mode == "higher-contrast")
            {
                options.Contrast = Contrast.More;
            }

            await using var context = await NewContextAsync(browser, app, options);
            var page = await context.NewPageAsync();
            await NavigateAndSettleAsync(page, "/apply");

            var stateDiagnostics = await MeasureSharedInteractionStatesAsync(page);
            AssertInteractionStateContrast(mode, stateDiagnostics);

            if (mode != "light")
            {
                continue;
            }

            var placeholderContrast = await MeasurePlaceholderContrastAsync(page.Locator("#name"));
            Assert.True(placeholderContrast >= 4.5, $"Placeholder contrast was only {placeholderContrast:F2}:1.");

            await NavigateAndSettleAsync(page, "/leaderboard");
            var badge = page.Locator("a.badge-class, .badge-class.badge-clickable, .badge-class[data-clickable=\"true\"]").First;
            await badge.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            await badge.HoverAsync();
            var hoverMotion = await badge.EvaluateAsync<BadgeMotionDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    const durations = style.transitionDuration.split(',').map(value => {
                        const trimmed = value.trim();
                        return trimmed.endsWith('ms') ? parseFloat(trimmed) : parseFloat(trimmed) * 1000;
                    });
                    return {
                        AnimationName: style.animationName,
                        Transform: style.transform,
                        LongestTransitionMilliseconds: Math.max(0, ...durations)
                    };
                }
                """);

            Assert.Equal("none", hoverMotion.AnimationName);
            Assert.Equal("none", hoverMotion.Transform);
            Assert.True(
                hoverMotion.LongestTransitionMilliseconds <= 220,
                $"Badge hover transition lasted {hoverMotion.LongestTransitionMilliseconds}ms.");

            await badge.FocusAsync();
            var focus = await badge.EvaluateAsync<FocusStateDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    return {
                        AnimationName: style.animationName,
                        OutlineWidth: parseFloat(style.outlineWidth),
                        OutlineStyle: style.outlineStyle
                    };
                }
                """);
            Assert.Equal("none", focus.AnimationName);
            Assert.True(focus.OutlineWidth >= 3);
            Assert.NotEqual("none", focus.OutlineStyle);
        }
    }

    [Fact]
    public async Task SlowAndOfflineEventRequests_ShowLoadingAndRecoveryStates()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);

        await using (var slowContext = await NewContextAsync(
                         browser,
                         app,
                         new BrowserNewContextOptions
                         {
                             ViewportSize = new ViewportSize { Width = 390, Height = 844 }
                         }))
        {
            await slowContext.RouteAsync("**/api/events", async route =>
            {
                await Task.Delay(900);
                await route.ContinueAsync();
            });
            var slowPage = await slowContext.NewPageAsync();
            await slowPage.GotoAsync("/events", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            var loadingRoot = slowPage.Locator("#events-root[aria-busy=\"true\"]");
            await loadingRoot.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
            Assert.Equal("Loading events...", await slowPage.Locator("#eventsStatus").InnerTextAsync());
            await slowPage.Locator("#events-root[aria-busy=\"false\"]").WaitForAsync(
                new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
        }

        await using (var offlineContext = await NewContextAsync(
                         browser,
                         app,
                         new BrowserNewContextOptions
                         {
                             ViewportSize = new ViewportSize { Width = 390, Height = 844 }
                         }))
        {
            await offlineContext.RouteAsync("**/api/events", route => route.AbortAsync("internetdisconnected"));
            var offlinePage = await offlineContext.NewPageAsync();
            await NavigateAndSettleAsync(offlinePage, "/events");
            var retry = offlinePage.Locator(".events-retry-button");
            await retry.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15_000 });
            var retryBox = await retry.BoundingBoxAsync();
            Assert.NotNull(retryBox);
            Assert.True(retryBox.Height >= 44);
            Assert.Equal("alert", await offlinePage.Locator("#eventsStatus").GetAttributeAsync("role"));
            Assert.Contains("could not load", await offlinePage.Locator("#eventsStatus").InnerTextAsync(), StringComparison.OrdinalIgnoreCase);
        }
    }

    private static async Task<InteractionStateDiagnostics[]> MeasureSharedInteractionStatesAsync(IPage page)
    {
        await page.EvaluateAsync(
            """
            () => {
                document.getElementById('aesthetic-state-probe')?.remove();
                const probe = document.createElement('section');
                probe.id = 'aesthetic-state-probe';
                probe.className = 'section-container';
                probe.setAttribute('aria-label', 'Shared interaction state probe');
                probe.innerHTML = `
                    <button id="state-action" class="option-button" type="button">Continue application</button>
                    <button id="state-disabled" class="option-button" type="button" disabled>Application unavailable</button>
                    <label for="state-focus">Focused field</label>
                    <input id="state-focus" value="Focused value">
                    <label for="state-invalid">Invalid field</label>
                    <input id="state-invalid" value="Invalid value" aria-invalid="true" aria-describedby="state-error">
                    <label for="state-readonly">Read-only field</label>
                    <input id="state-readonly" value="Verified value" readonly>
                    <div id="state-loading" class="lmx-status" role="status" aria-busy="true">Loading current standings…</div>
                    <div id="state-success" class="lmx-status success" role="status">Application saved successfully.</div>
                    <div id="state-error" class="lmx-status error" role="alert">Application could not be saved. Review the highlighted field.</div>`;
                (document.querySelector('main') || document.body).prepend(probe);
            }
            """);
        await page.EvaluateAsync(
            """
            async () => {
                await (document.fonts?.ready || Promise.resolve());
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
            }
            """);

        const string measurementScript =
            """
            (element, stateName) => {
                element.getAnimations().forEach(animation => {
                    try {
                        animation.finish();
                    } catch (_) {
                        // Infinite or already-idle animations cannot be finished and do not
                        // affect the finite interaction-state transitions measured here.
                    }
                });

                const parseColor = value => {
                    const parts = (value || '').match(/[\d.]+/g)?.map(Number) || [];
                    return {
                        r: parts[0] || 0,
                        g: parts[1] || 0,
                        b: parts[2] || 0,
                        a: parts.length > 3 ? parts[3] : 1
                    };
                };
                const composite = (foreground, background) => ({
                    r: foreground.r * foreground.a + background.r * (1 - foreground.a),
                    g: foreground.g * foreground.a + background.g * (1 - foreground.a),
                    b: foreground.b * foreground.a + background.b * (1 - foreground.a),
                    a: 1
                });
                const effectiveBackground = start => {
                    const layers = [];
                    for (let node = start; node instanceof Element; node = node.parentElement) {
                        layers.push(parseColor(getComputedStyle(node).backgroundColor));
                    }
                    return layers.reverse().reduce(
                        (background, foreground) => composite(foreground, background),
                        { r: 255, g: 255, b: 255, a: 1 });
                };
                const linearize = channel => {
                    const normalized = channel / 255;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.pow((normalized + 0.055) / 1.055, 2.4);
                };
                const luminance = color =>
                    0.2126 * linearize(color.r)
                    + 0.7152 * linearize(color.g)
                    + 0.0722 * linearize(color.b);
                const contrast = (first, second) => {
                    const firstLuminance = luminance(first);
                    const secondLuminance = luminance(second);
                    return (Math.max(firstLuminance, secondLuminance) + 0.05)
                        / (Math.min(firstLuminance, secondLuminance) + 0.05);
                };

                const style = getComputedStyle(element);
                const background = effectiveBackground(element);
                const outerBackground = effectiveBackground(element.parentElement || document.body);
                const foreground = composite(parseColor(style.color), background);
                const indicatorContrasts = [contrast(background, outerBackground)];
                let widestBoundary = 0;
                for (const side of ['Top', 'Right', 'Bottom', 'Left']) {
                    const width = parseFloat(style[`border${side}Width`]) || 0;
                    const border = parseColor(style[`border${side}Color`]);
                    if (width > 0 && border.a > 0) {
                        widestBoundary = Math.max(widestBoundary, width);
                        const compositedBorder = composite(border, outerBackground);
                        indicatorContrasts.push(contrast(compositedBorder, outerBackground));
                        indicatorContrasts.push(contrast(compositedBorder, background));
                    }
                }
                const outlineWidth = parseFloat(style.outlineWidth) || 0;
                if (outlineWidth > 0 && style.outlineStyle !== 'none') {
                    widestBoundary = Math.max(widestBoundary, outlineWidth);
                    const outline = composite(parseColor(style.outlineColor), outerBackground);
                    indicatorContrasts.push(contrast(outline, outerBackground));
                }

                const hasText = element.textContent.trim().length > 0 || element.value?.trim().length > 0;
                const stateEvidence = {
                    default: hasText,
                    hover: element.matches(':hover'),
                    focus: document.activeElement === element,
                    pressed: element.matches(':active'),
                    disabled: element.disabled === true,
                    invalid: element.getAttribute('aria-invalid') === 'true',
                    'read-only': element.readOnly === true && element.matches(':read-only'),
                    loading: element.getAttribute('aria-busy') === 'true' && hasText,
                    success: element.getAttribute('role') === 'status' && element.classList.contains('success') && hasText,
                    error: element.getAttribute('role') === 'alert' && element.classList.contains('error') && hasText
                };

                return {
                    Name: stateName,
                    TextContrast: contrast(foreground, background),
                    BoundaryContrast: Math.max(...indicatorContrasts),
                    BoundaryWidth: widestBoundary,
                    ForegroundColor: style.color,
                    BackgroundColor: style.backgroundColor,
                    EffectiveOuterBackgroundColor: `rgb(${Math.round(outerBackground.r)}, ${Math.round(outerBackground.g)}, ${Math.round(outerBackground.b)})`,
                    BorderColor: style.borderTopColor,
                    HasStateEvidence: stateEvidence[stateName] === true
                };
            }
            """;

        var diagnostics = new List<InteractionStateDiagnostics>();
        async Task CaptureAsync(string name, ILocator locator)
        {
            diagnostics.Add(await locator.EvaluateAsync<InteractionStateDiagnostics>(measurementScript, name));
        }

        var action = page.Locator("#state-action");
        await CaptureAsync("default", action);
        var actionBox = await action.BoundingBoxAsync();
        Assert.NotNull(actionBox);
        await page.Mouse.MoveAsync(actionBox.X + actionBox.Width / 2, actionBox.Y + actionBox.Height / 2);
        await page.WaitForFunctionAsync(
            "() => document.getElementById('state-action')?.matches(':hover') === true");
        await CaptureAsync("hover", action);

        await page.Mouse.MoveAsync(actionBox.X + actionBox.Width / 2, actionBox.Y + actionBox.Height / 2);
        await page.Mouse.DownAsync();
        try
        {
            await CaptureAsync("pressed", action);
        }
        finally
        {
            await page.Mouse.UpAsync();
        }

        var focused = page.Locator("#state-focus");
        await focused.FocusAsync();
        await CaptureAsync("focus", focused);
        await CaptureAsync("disabled", page.Locator("#state-disabled"));
        await CaptureAsync("invalid", page.Locator("#state-invalid"));
        await CaptureAsync("read-only", page.Locator("#state-readonly"));
        await CaptureAsync("loading", page.Locator("#state-loading"));
        await CaptureAsync("success", page.Locator("#state-success"));
        await CaptureAsync("error", page.Locator("#state-error"));
        return diagnostics.ToArray();
    }

    private static void AssertInteractionStateContrast(string mode, InteractionStateDiagnostics[] diagnostics)
    {
        var expectedStates = new[]
        {
            "default", "hover", "pressed", "focus", "disabled",
            "invalid", "read-only", "loading", "success", "error"
        };
        Assert.Equal(expectedStates, diagnostics.Select(item => item.Name));

        foreach (var state in diagnostics)
        {
            var minimumTextContrast = state.Name == "disabled" ? 3.0 : 4.5;
            Assert.True(
                state.TextContrast >= minimumTextContrast,
                $"{mode} {state.Name} text contrast was {state.TextContrast:F2}:1; " +
                $"expected at least {minimumTextContrast:F1}:1.");
            Assert.True(
                state.HasStateEvidence,
                $"{mode} {state.Name} state was not active or semantically exposed when measured.");

            // Disabled controls are exempt from WCAG contrast requirements, but their
            // text is still measured above so the intentionally muted state remains legible.
            if (state.Name != "disabled")
            {
                Assert.True(
                    state.BoundaryContrast >= 3,
                    $"{mode} {state.Name} boundary contrast was {state.BoundaryContrast:F2}:1. " +
                    $"foreground={state.ForegroundColor}, background={state.BackgroundColor}, " +
                    $"outer={state.EffectiveOuterBackgroundColor}, border={state.BorderColor}.");
            }
        }

        var byName = diagnostics.ToDictionary(item => item.Name, StringComparer.Ordinal);
        Assert.NotEqual(byName["default"].BackgroundColor, byName["hover"].BackgroundColor);
        Assert.True(byName["focus"].BoundaryWidth >= 1);
        Assert.True(byName["invalid"].BoundaryWidth >= 1);
        Assert.True(byName["loading"].BoundaryWidth >= 4);
        Assert.True(byName["success"].BoundaryWidth >= 4);
        Assert.True(byName["error"].BoundaryWidth >= 4);
    }

    private static Task<double> MeasurePlaceholderContrastAsync(ILocator locator)
        => locator.EvaluateAsync<double>(
            """
            element => {
                const parse = value => value.match(/[\d.]+/g).slice(0, 3).map(Number);
                const linearize = channel => {
                    const normalized = channel / 255;
                    return normalized <= 0.04045
                        ? normalized / 12.92
                        : Math.pow((normalized + 0.055) / 1.055, 2.4);
                };
                const luminance = value => {
                    const [red, green, blue] = parse(value).map(linearize);
                    return 0.2126 * red + 0.7152 * green + 0.0722 * blue;
                };
                const foreground = luminance(getComputedStyle(element, '::placeholder').color);
                const background = luminance(getComputedStyle(element).backgroundColor);
                return (Math.max(foreground, background) + 0.05)
                    / (Math.min(foreground, background) + 0.05);
            }
            """);

    private static Task AddRouteStressStateAsync(IBrowserContext context)
        => context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Id: 9001,
                Name: 'Localization Stress Athlete',
                DisplayName: 'Localization Stress Athlete',
                AccountEmail: 'localization-stress@example.test',
                DateOfBirth: '1990-01-01',
                Country: 'HU',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-07-18', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            window.sessionStorage.setItem('contactEmail', 'localization-stress@example.test');
            window.sessionStorage.setItem('came-from', 'proof-upload');
            """);

    private static string[] GetCanonicalFirstPartyRoutes()
    {
        var repositoryRoot = FindRepositoryRoot();
        var middlewarePath = Path.Combine(
            repositoryRoot,
            "LongevityWorldCup.Website",
            "Middleware",
            "CleanPathMiddleware.cs");
        var source = File.ReadAllText(middlewarePath);
        var rewrittenRoutes = Regex.Matches(
                source,
                "case\\s+\"(?<path>/[^\"]+)\"\\s*:",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups["path"].Value);

        return new[] { "/" }
            .Concat(rewrittenRoutes)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static Task<ExtremeContentDiagnostics> InjectAndMeasureExtremeContentAsync(IPage page)
        => page.EvaluateAsync<ExtremeContentDiagnostics>(
            """
            async () => {
                document.getElementById('aesthetic-content-stress-probe')?.remove();
                const probe = document.createElement('section');
                probe.id = 'aesthetic-content-stress-probe';
                probe.className = 'section-container';
                probe.setAttribute('aria-label', 'Content stress probe');

                const heading = document.createElement('h2');
                heading.id = 'stress-localized-heading';
                heading.textContent = 'Présentation internationale des résultats biologiques vérifiés et comparables';

                const localized = document.createElement('p');
                localized.id = 'stress-localized-copy';
                localized.textContent = 'Les participantes et participants peuvent consulter les mesures admissibles, '
                    + 'comprendre les critères de départage et poursuivre leur parcours sans perdre le contexte.';

                const extreme = document.createElement('p');
                extreme.id = 'stress-extreme-token';
                extreme.textContent = 'IdentifiantScientifiqueExtrêmementLongSansSéparateurVisuel'.repeat(5);

                const empty = document.createElement('p');
                empty.id = 'stress-empty-content';
                empty.setAttribute('aria-label', 'Optional supporting content is empty');

                const action = document.createElement('button');
                action.id = 'stress-localized-action';
                action.type = 'button';
                action.className = 'option-button';
                action.textContent = 'Continuer vers la prochaine étape de vérification internationale';

                const status = document.createElement('div');
                status.id = 'stress-localized-status';
                status.className = 'lmx-status success';
                status.setAttribute('role', 'status');
                status.textContent = 'Toutes les informations disponibles ont été vérifiées avec succès.';

                probe.append(heading, localized, extreme, empty, action, status);
                (document.querySelector('main') || document.body).append(probe);
                await (document.fonts?.ready || Promise.resolve());
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

                const root = document.documentElement;
                const body = document.body;
                const containerBounds = probe.getBoundingClientRect();
                const measureText = selector => {
                    const element = document.querySelector(selector);
                    const style = getComputedStyle(element);
                    const bounds = element.getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const tolerance = 1.5;
                    const textIsContained = [...range.getClientRects()]
                        .filter(rect => rect.width > 0 && rect.height > 0)
                        .every(rect => rect.left >= bounds.left - tolerance
                            && rect.right <= bounds.right + tolerance
                            && rect.top >= bounds.top - tolerance
                            && rect.bottom <= bounds.bottom + tolerance);
                    const fontSize = parseFloat(style.fontSize) || 0;
                    const parsedLineHeight = parseFloat(style.lineHeight);
                    return {
                        Selector: selector,
                        IsVisible: bounds.width > 0 && bounds.height > 0
                            && style.display !== 'none' && style.visibility !== 'hidden',
                        TextIsClipped: element.scrollWidth > element.clientWidth + 1
                            || element.scrollHeight > element.clientHeight + 1
                            || !textIsContained,
                        FontSize: fontSize,
                        LineHeight: Number.isFinite(parsedLineHeight) ? parsedLineHeight : fontSize * 1.2,
                        Height: bounds.height
                    };
                };
                const emptyBounds = empty.getBoundingClientRect();
                return {
                    ClientWidth: root.clientWidth,
                    ScrollWidth: Math.max(root.scrollWidth, body.scrollWidth),
                    HorizontalOverflow: Math.max(
                        0,
                        root.scrollWidth - root.clientWidth,
                        body.scrollWidth - root.clientWidth),
                    ContainerLeft: containerBounds.left,
                    ContainerRight: containerBounds.right,
                    EmptyTextLength: empty.textContent.length,
                    EmptyContentOverflows: empty.scrollWidth > empty.clientWidth + 1
                        || empty.scrollHeight > empty.clientHeight + 1
                        || emptyBounds.right > root.clientWidth + 1,
                    Elements: [
                        '#stress-localized-heading',
                        '#stress-localized-copy',
                        '#stress-extreme-token',
                        '#stress-localized-action',
                        '#stress-localized-status'
                    ].map(measureText)
                };
            }
            """);

    private static void AssertExtremeContentLayout(string path, ExtremeContentDiagnostics diagnostics)
    {
        Assert.True(
            diagnostics.HorizontalOverflow <= 1,
            $"{path} overflowed horizontally by {diagnostics.HorizontalOverflow}px under empty/localized/extreme content " +
            $"(scrollWidth={diagnostics.ScrollWidth}, clientWidth={diagnostics.ClientWidth}).");
        Assert.True(diagnostics.ContainerLeft >= -1, $"{path} positioned the stress content off the left edge.");
        Assert.True(
            diagnostics.ContainerRight <= diagnostics.ClientWidth + 1,
            $"{path} positioned the stress content beyond the right edge " +
            $"({diagnostics.ContainerRight}px > {diagnostics.ClientWidth}px).");
        Assert.Equal(0, diagnostics.EmptyTextLength);
        Assert.False(diagnostics.EmptyContentOverflows, $"{path} allowed its empty content slot to overflow.");
        Assert.NotEmpty(diagnostics.Elements);

        foreach (var element in diagnostics.Elements)
        {
            Assert.True(element.IsVisible, $"{path} stress target {element.Selector} was not visible.");
            Assert.False(element.TextIsClipped, $"{path} stress target {element.Selector} clipped its content.");
            Assert.True(element.FontSize >= 12, $"{path} stress target {element.Selector} shrank below 12px.");
            Assert.True(
                element.LineHeight >= element.FontSize * 1.15,
                $"{path} stress target {element.Selector} used cramped line height.");
        }
    }

    private static ResponsiveMediaBranch[] GetResponsiveMediaInventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repositoryRoot, "LongevityWorldCup.Website", "wwwroot");
        var mediaRulePattern = new Regex(
            "@media\\s+(?<condition>[^\\{]+)\\{",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        var branches = new List<ResponsiveMediaBranch>();

        foreach (var path in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(webRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("internal/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("swagger", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("lib/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/lib/", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var source = File.ReadAllText(path);
            source = Regex.Replace(source, "/\\*.*?\\*/", "", RegexOptions.Singleline);
            foreach (Match match in mediaRulePattern.Matches(source))
            {
                var condition = NormalizeMediaCondition(match.Groups["condition"].Value);
                foreach (var queryBranch in condition.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (GetResponsiveFeatures(queryBranch).Length == 0)
                    {
                        continue;
                    }

                    branches.Add(new ResponsiveMediaBranch(queryBranch, relativePath));
                }
            }
        }

        return branches
            .GroupBy(branch => (branch.Query, branch.Source))
            .Select(group => group.First())
            .OrderBy(branch => branch.Query, StringComparer.Ordinal)
            .ThenBy(branch => branch.Source, StringComparer.Ordinal)
            .ToArray();
    }

    private static ResponsiveMediaBranch[] GetResponsiveScriptMediaInventory()
    {
        var matchMediaPattern = new Regex(
            """matchMedia\s*\(\s*['"`](?<condition>[^'"`]+)['"`]\s*\)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var branches = new List<ResponsiveMediaBranch>();
        foreach (var (source, text) in EnumerateResponsiveScriptSources())
        {
            foreach (Match match in matchMediaPattern.Matches(text))
            {
                var condition = NormalizeMediaCondition(match.Groups["condition"].Value);
                foreach (var queryBranch in condition.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (GetResponsiveFeatures(queryBranch).Length > 0)
                    {
                        branches.Add(new ResponsiveMediaBranch(queryBranch, source));
                    }
                }
            }
        }

        return branches
            .GroupBy(item => (item.Query, item.Source))
            .Select(group => group.First())
            .OrderBy(item => item.Query, StringComparer.Ordinal)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ToArray();
    }

    private static ResponsiveScriptThreshold[] GetResponsiveScriptThresholdInventory()
    {
        var comparisonPattern = new Regex(
            """(?:window\s*\.\s*)?inner(?<axis>width|height)\s*(?<operator><=|>=|<|>)\s*(?<value>\d+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var thresholds = new List<ResponsiveScriptThreshold>();
        foreach (var (source, text) in EnumerateResponsiveScriptSources())
        {
            foreach (Match match in comparisonPattern.Matches(text))
            {
                thresholds.Add(new ResponsiveScriptThreshold(
                    match.Groups["axis"].Value.ToLowerInvariant(),
                    match.Groups["operator"].Value,
                    int.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    source));
            }
        }

        return thresholds
            .Distinct()
            .OrderBy(item => item.Axis, StringComparer.Ordinal)
            .ThenBy(item => item.Value)
            .ThenBy(item => item.Source, StringComparer.Ordinal)
            .ToArray();
    }

    private static ResponsiveContainerCondition[] GetResponsiveContainerInventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repositoryRoot, "LongevityWorldCup.Website", "wwwroot");
        var containerPattern = new Regex(
            """@container(?:\s+[\w-]+)?\s*\(\s*(?<bound>min|max)-width\s*:\s*(?<value>\d+(?:\.\d+)?)(?<unit>rem|px)\s*\)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var conditions = new List<ResponsiveContainerCondition>();

        foreach (var path in Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".css", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(webRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("internal/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("swagger", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = Regex.Replace(File.ReadAllText(path), "/\\*.*?\\*/", "", RegexOptions.Singleline);
            foreach (Match match in containerPattern.Matches(text))
            {
                conditions.Add(new ResponsiveContainerCondition(
                    match.Groups["bound"].Value.ToLowerInvariant(),
                    double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    match.Groups["unit"].Value.ToLowerInvariant(),
                    relativePath));
            }
        }

        return conditions.Distinct().ToArray();
    }

    private static IEnumerable<(string Source, string Text)> EnumerateResponsiveScriptSources()
    {
        var repositoryRoot = FindRepositoryRoot();
        var websiteRoot = Path.Combine(repositoryRoot, "LongevityWorldCup.Website");
        var webRoot = Path.Combine(websiteRoot, "wwwroot");
        foreach (var path in Directory.EnumerateFiles(webRoot, "*.html", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(webRoot, path).Replace('\\', '/');
            if (relativePath.StartsWith("internal/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("swagger", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/vendor/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return (relativePath, File.ReadAllText(path));
        }

        var frontendRoot = Path.Combine(websiteRoot, "Frontend");
        foreach (var path in Directory.EnumerateFiles(frontendRoot, "*.ts", SearchOption.AllDirectories))
        {
            var relativePath = "Frontend/" + Path.GetRelativePath(frontendRoot, path).Replace('\\', '/');
            yield return (relativePath, File.ReadAllText(path));
        }
    }

    private static string NormalizeMediaCondition(string condition)
    {
        var normalized = Regex.Replace(condition, "\\s+", " ").Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\s*:\\s*", ": ");
        normalized = Regex.Replace(normalized, "\\(\\s+", "(");
        normalized = Regex.Replace(normalized, "\\s+\\)", ")");
        normalized = Regex.Replace(normalized, "\\s*,\\s*", ", ");
        return normalized;
    }

    private static ResponsiveFeature[] GetResponsiveFeatures(string query)
        => Regex.Matches(
                query,
                "\\((?<bound>min|max)-(?<axis>width|height):\\s*(?<value>\\d+(?:\\.\\d+)?)px\\)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => new ResponsiveFeature(
                match.Groups["bound"].Value.ToLowerInvariant(),
                match.Groups["axis"].Value.ToLowerInvariant(),
                (int)Math.Round(
                    double.Parse(match.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture),
                    MidpointRounding.AwayFromZero)))
            .Distinct()
            .ToArray();

    private static IEnumerable<ResponsiveBoundaryCase> CreateResponsiveBoundaryCases(ResponsiveMediaBranch branch)
    {
        var features = GetResponsiveFeatures(branch.Query);
        foreach (var feature in features)
        {
            foreach (var delta in new[] { -1, 0, 1 })
            {
                var value = feature.Value + delta;
                var viewport = CreateMatchingViewport(
                    branch.Query,
                    feature.Axis == "width" ? value : null,
                    feature.Axis == "height" ? value : null);
                var expectedMatch = delta == 0
                    || (delta < 0 && feature.Bound == "max")
                    || (delta > 0 && feature.Bound == "min");
                yield return new ResponsiveBoundaryCase(
                    branch,
                    feature,
                    viewport.Width,
                    viewport.Height,
                    expectedMatch);
            }
        }
    }

    private static IEnumerable<ResponsiveScriptBoundaryCase> CreateResponsiveScriptBoundaryCases(
        ResponsiveScriptThreshold threshold)
    {
        foreach (var delta in new[] { -1, 0, 1 })
        {
            var value = threshold.Value + delta;
            var width = threshold.Axis == "width" ? value : 1024;
            var height = threshold.Axis == "height" ? value : 720;
            yield return new ResponsiveScriptBoundaryCase(
                threshold,
                width,
                height,
                EvaluateComparison(value, threshold.Operator, threshold.Value));
        }
    }

    private static bool EvaluateComparison(int actual, string comparisonOperator, int threshold)
        => comparisonOperator switch
        {
            "<" => actual < threshold,
            "<=" => actual <= threshold,
            ">" => actual > threshold,
            ">=" => actual >= threshold,
            _ => throw new InvalidOperationException($"Unsupported responsive comparison '{comparisonOperator}'.")
        };

    private static async Task AssertResponsiveContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        Assert.All(
            conditions,
            condition => Assert.Contains("longevitymaxxing", condition.Source, StringComparison.OrdinalIgnoreCase));
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        var rootFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");

        foreach (var condition in conditions)
        {
            var thresholdPixels = condition.Unit == "rem"
                ? condition.Value * rootFontSize
                : condition.Value;
            foreach (var delta in new[] { -1, 0, 1 })
            {
                var inlineSize = thresholdPixels + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async argument => {
                        let tile = document.getElementById('responsive-container-probe');
                        if (!tile) {
                            tile = document.createElement('div');
                            tile.id = 'responsive-container-probe';
                            tile.className = 'lmx-ops-tile community-calls';
                            tile.innerHTML = `
                                <span class="lmx-ops-label-short">Calls</span>
                                <span class="lmx-ops-label-long">Community calls</span>
                                <strong>12</strong>`;
                            (document.querySelector('main') || document.body).append(tile);
                        }
                        tile.style.boxSizing = 'content-box';
                        tile.style.inlineSize = `${argument.InlineSize}px`;
                        tile.style.maxInlineSize = 'none';
                        tile.style.flex = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const shortLabel = tile.querySelector('.lmx-ops-label-short');
                        const longLabel = tile.querySelector('.lmx-ops-label-long');
                        const root = document.documentElement;
                        const body = document.body;
                        return {
                            ContainerWidth: tile.getBoundingClientRect().width,
                            ShortVisible: getComputedStyle(shortLabel).display !== 'none',
                            LongVisible: getComputedStyle(longLabel).display !== 'none',
                            HorizontalOverflow: Math.max(
                                0,
                                root.scrollWidth - root.clientWidth,
                                body.scrollWidth - root.clientWidth)
                        };
                    }
                    """,
                    new { InlineSize = inlineSize });

                var conditionMatches = condition.Bound == "min"
                    ? inlineSize >= thresholdPixels
                    : inlineSize <= thresholdPixels;
                Assert.Equal(!conditionMatches, diagnostics.ShortVisible);
                Assert.Equal(conditionMatches, diagnostics.LongVisible);
                Assert.True(
                    diagnostics.HorizontalOverflow <= 1,
                    $"Container query from {condition.Source} caused {diagnostics.HorizontalOverflow}px " +
                    $"horizontal overflow at {inlineSize:F1}px.");
            }
        }
    }

    private static string MapResponsiveSourceToRoute(string source)
    {
        var normalized = source.Replace('\\', '/').ToLowerInvariant();
        if (normalized == "index.html" || normalized.Contains("site-statistics")) return "/";
        if (normalized.Contains("leaderboard") || normalized.Contains("guess-my-age") || normalized.Contains("badges")) return "/leaderboard";
        if (normalized.Contains("event-board")) return "/events";
        if (normalized.Contains("longevitymaxxing")) return "/longevitymaxxing";
        if (normalized.Contains("helstab-kihivas")) return "/helstab-kihivas";
        if (normalized.Contains("play-menu")) return "/play";
        if (normalized.Contains("convergence")
            || normalized.Contains("play-athlete-flow")
            || normalized.Contains("flow-controls")
            || normalized.Contains("flow-action-dock")) return "/apply";
        if (normalized.Contains("application-review")) return "/review";
        if (normalized.Contains("proof-upload")) return "/proofs";
        if (normalized.Contains("edit-profile")) return "/edit-profile";
        if (normalized.Contains("pheno-age")) return "/pheno-age";
        if (normalized.Contains("bortz-age")) return "/bortz-age";
        if (normalized.Contains("bioageform")) return "/pheno-age";
        if (normalized.Contains("age-visualization")) return "/pheno-age";
        if (normalized.Contains("ruleset")) return "/ruleset";
        if (normalized.Contains("history")) return "/history";
        if (normalized.Contains("about")) return "/about";
        if (normalized.Contains("media.html")) return "/media";
        if (normalized.Contains("privacy")) return "/privacy";
        if (normalized.Contains("unsubscribe")) return "/unsubscribe";
        if (normalized.Contains("error-system")) return "/error/503.html";
        if (normalized.Contains("error/")) return "/" + normalized;
        if (normalized.Contains("custom-event-designer")) return "/internal/custom-event-designer.html";
        if (normalized.Contains("swagger")) return "/swagger/index.html";

        // Shared chrome and system styles intentionally use the home page as a
        // bounded rendered witness; feature-specific sources map above.
        return "/";
    }

    private static ViewportSize CreateMatchingViewport(string query, int? fixedWidth, int? fixedHeight)
    {
        var features = GetResponsiveFeatures(query);
        var minimumWidth = features
            .Where(feature => feature.Axis == "width" && feature.Bound == "min")
            .Select(feature => feature.Value)
            .DefaultIfEmpty(240)
            .Max();
        var maximumWidth = features
            .Where(feature => feature.Axis == "width" && feature.Bound == "max")
            .Select(feature => feature.Value)
            .DefaultIfEmpty(1600)
            .Min();
        var minimumHeight = features
            .Where(feature => feature.Axis == "height" && feature.Bound == "min")
            .Select(feature => feature.Value)
            .DefaultIfEmpty(240)
            .Max();
        var maximumHeight = features
            .Where(feature => feature.Axis == "height" && feature.Bound == "max")
            .Select(feature => feature.Value)
            .DefaultIfEmpty(1080)
            .Min();

        var width = fixedWidth ?? Math.Clamp(1024, minimumWidth, maximumWidth);
        var height = fixedHeight ?? Math.Clamp(720, minimumHeight, maximumHeight);
        if (query.Contains("orientation: landscape", StringComparison.Ordinal))
        {
            if (width <= height && fixedHeight is null)
            {
                height = Math.Clamp(width - 1, minimumHeight, maximumHeight);
            }
            if (width <= height && fixedWidth is null)
            {
                width = Math.Clamp(height + 1, minimumWidth, maximumWidth);
            }
        }
        else if (query.Contains("orientation: portrait", StringComparison.Ordinal))
        {
            if (height <= width && fixedWidth is null)
            {
                width = Math.Clamp(height - 1, minimumWidth, maximumWidth);
            }
            if (height <= width && fixedHeight is null)
            {
                height = Math.Clamp(width + 1, minimumHeight, maximumHeight);
            }
        }

        Assert.True(width > 0 && height > 0, $"Invalid viewport generated for '{query}'.");
        return new ViewportSize { Width = width, Height = height };
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.Website"))
                && Directory.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.Tests")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LongevityWorldCup repository root.");
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
        => await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

    private static Task<LongContentDiagnostics> InjectAndMeasureLongContentAsync(
        IPage page,
        Dictionary<string, string> replacements,
        string[] measuredSelectors)
        => page.EvaluateAsync<LongContentDiagnostics>(
            """
            async argument => {
                for (const [selector, text] of Object.entries(argument.Replacements)) {
                    const element = document.querySelector(selector);
                    if (!element) throw new Error(`Missing long-content target: ${selector}`);
                    element.textContent = text;
                }

                await (document.fonts?.ready || Promise.resolve());
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));

                const root = document.documentElement;
                const body = document.body;
                const elements = argument.MeasuredSelectors.map(selector => {
                    const element = document.querySelector(selector);
                    if (!element) throw new Error(`Missing measured target: ${selector}`);

                    const style = getComputedStyle(element);
                    const bounds = element.getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    const textRects = [...range.getClientRects()]
                        .filter(rect => rect.width > 0 && rect.height > 0);
                    const tolerance = 1.5;
                    const textIsContained = textRects.every(rect =>
                        rect.left >= bounds.left - tolerance
                        && rect.right <= bounds.right + tolerance
                        && rect.top >= bounds.top - tolerance
                        && rect.bottom <= bounds.bottom + tolerance);
                    const fontSize = parseFloat(style.fontSize) || 0;
                    const parsedLineHeight = parseFloat(style.lineHeight);
                    const lineHeight = Number.isFinite(parsedLineHeight)
                        ? parsedLineHeight
                        : fontSize * 1.2;

                    return {
                        Selector: selector,
                        Text: element.textContent.trim(),
                        IsVisible: bounds.width > 0 && bounds.height > 0
                            && style.display !== 'none' && style.visibility !== 'hidden',
                        TextIsClipped: element.scrollWidth > element.clientWidth + 1
                            || element.scrollHeight > element.clientHeight + 1
                            || !textIsContained,
                        IsControl: element.matches('button, .view-badge, .flow-action'),
                        Width: bounds.width,
                        Height: bounds.height,
                        ClientWidth: element.clientWidth,
                        ScrollWidth: element.scrollWidth,
                        ClientHeight: element.clientHeight,
                        ScrollHeight: element.scrollHeight,
                        TextIsContained: textIsContained,
                        WhiteSpace: style.whiteSpace,
                        OverflowWrap: style.overflowWrap,
                        FontSize: fontSize,
                        LineHeight: lineHeight
                    };
                });

                return {
                    ClientWidth: root.clientWidth,
                    ScrollWidth: Math.max(root.scrollWidth, body.scrollWidth),
                    HorizontalOverflow: Math.max(
                        0,
                        root.scrollWidth - root.clientWidth,
                        body.scrollWidth - root.clientWidth),
                    Elements: elements
                };
            }
            """,
            new { Replacements = replacements, MeasuredSelectors = measuredSelectors });

    private static void AssertLongContentLayout(string path, LongContentDiagnostics diagnostics)
    {
        Assert.True(
            diagnostics.HorizontalOverflow <= 1,
            $"{path} overflowed horizontally by {diagnostics.HorizontalOverflow}px after long-content injection " +
            $"(scrollWidth={diagnostics.ScrollWidth}, clientWidth={diagnostics.ClientWidth}).");
        Assert.NotEmpty(diagnostics.Elements);

        foreach (var element in diagnostics.Elements)
        {
            Assert.True(element.IsVisible, $"{path} target {element.Selector} was not visible.");
            Assert.False(
                element.TextIsClipped,
                $"{path} target {element.Selector} clipped: {element.Text}. " +
                $"client={element.ClientWidth}x{element.ClientHeight}, scroll={element.ScrollWidth}x{element.ScrollHeight}, " +
                $"textContained={element.TextIsContained}, whiteSpace={element.WhiteSpace}, overflowWrap={element.OverflowWrap}.");
            Assert.True(element.FontSize >= 12,
                $"{path} target {element.Selector} fell below a readable size ({element.FontSize}px).");
            Assert.True(element.LineHeight >= element.FontSize * 1.15,
                $"{path} target {element.Selector} has cramped line height " +
                $"({element.LineHeight}px for {element.FontSize}px text).");
            if (element.IsControl)
            {
                Assert.True(element.Height >= 44,
                    $"{path} control {element.Selector} shrank below 44px ({element.Height}px). ");
            }
        }
    }

    private static async Task<IBrowserContext> NewContextAsync(
        IBrowser browser,
        BrowserTestApp app,
        BrowserNewContextOptions options)
    {
        options.BaseURL = app.BaseAddress.ToString();
        options.Locale = "en-US";
        var context = await browser.NewContextAsync(options);
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

    private static async Task NavigateAndSettleAsync(IPage page, string path)
    {
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.readyState === 'complete'");
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
    }

    private static Task<LayoutDiagnostics> MeasureLayoutAsync(IPage page)
        => page.EvaluateAsync<LayoutDiagnostics>(
            """
            () => {
                const root = document.documentElement;
                const body = document.body;
                const rootOverflow = root.scrollWidth - root.clientWidth;
                const bodyOverflow = body.scrollWidth - root.clientWidth;
                const visibleContent = [...document.querySelectorAll('main, h1, [role="main"]')]
                    .some(element => {
                        const rect = element.getBoundingClientRect();
                        const style = getComputedStyle(element);
                        return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
                    });

                return {
                    ClientWidth: root.clientWidth,
                    ScrollWidth: Math.max(root.scrollWidth, body.scrollWidth),
                    HorizontalOverflow: Math.max(0, rootOverflow, bodyOverflow),
                    RootFontSize: parseFloat(getComputedStyle(root).fontSize),
                    HasVisibleContent: visibleContent
                };
            }
            """);

    private sealed record ResponsiveMediaBranch(string Query, string Source);

    private sealed record ResponsiveFeature(string Bound, string Axis, int Value);

    private sealed record ResponsiveBoundaryCase(
        ResponsiveMediaBranch Branch,
        ResponsiveFeature Feature,
        int Width,
        int Height,
        bool ExpectedMatch);

    private sealed record ResponsiveLayoutProbe(
        string Route,
        int Width,
        int Height,
        string Source,
        string Query);

    private sealed record ResponsiveScriptThreshold(
        string Axis,
        string Operator,
        int Value,
        string Source);

    private sealed record ResponsiveScriptBoundaryCase(
        ResponsiveScriptThreshold Threshold,
        int Width,
        int Height,
        bool ExpectedMatch);

    private sealed record ResponsiveContainerCondition(
        string Bound,
        double Value,
        string Unit,
        string Source);

    private sealed class ScriptViewportDiagnostics
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    private sealed class ContainerQueryDiagnostics
    {
        public double ContainerWidth { get; set; }
        public bool ShortVisible { get; set; }
        public bool LongVisible { get; set; }
        public double HorizontalOverflow { get; set; }
    }

    private sealed class InteractionStateDiagnostics
    {
        public string Name { get; set; } = "";
        public double TextContrast { get; set; }
        public double BoundaryContrast { get; set; }
        public double BoundaryWidth { get; set; }
        public string ForegroundColor { get; set; } = "";
        public string BackgroundColor { get; set; } = "";
        public string EffectiveOuterBackgroundColor { get; set; } = "";
        public string BorderColor { get; set; } = "";
        public bool HasStateEvidence { get; set; }
    }

    private sealed class ExtremeContentDiagnostics
    {
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double HorizontalOverflow { get; set; }
        public double ContainerLeft { get; set; }
        public double ContainerRight { get; set; }
        public int EmptyTextLength { get; set; }
        public bool EmptyContentOverflows { get; set; }
        public ExtremeContentElementDiagnostics[] Elements { get; set; } = [];
    }

    private sealed class ExtremeContentElementDiagnostics
    {
        public string Selector { get; set; } = "";
        public bool IsVisible { get; set; }
        public bool TextIsClipped { get; set; }
        public double FontSize { get; set; }
        public double LineHeight { get; set; }
        public double Height { get; set; }
    }

    private sealed class DarkThemeDiagnostics
    {
        public bool DarkPreferenceMatches { get; set; }
        public string ColorScheme { get; set; } = "";
        public string CanvasToken { get; set; } = "";
        public string SurfaceToken { get; set; } = "";
        public string InkToken { get; set; } = "";
        public string BodyBackground { get; set; } = "";
        public string BodyColor { get; set; } = "";
        public string InputBackground { get; set; } = "";
        public string InputColor { get; set; } = "";
    }

    private sealed class DarkControlDiagnostics
    {
        public string Background { get; set; } = "";
        public string Color { get; set; } = "";
        public string BorderColor { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public bool MatchesReadOnly { get; set; }
        public bool ServedOverridePresent { get; set; }
    }

    private sealed class DarkProofDiagnostics
    {
        public bool HasExpectedAthleteCopy { get; set; }
        public string MainCopyColor { get; set; } = "";
        public string SupportingCopyColor { get; set; } = "";
        public string FilledActionBackground { get; set; } = "";
        public string FilledActionColor { get; set; } = "";
        public bool FilledActionDisabled { get; set; }
    }

    private sealed class DarkReviewDiagnostics
    {
        public bool ShowsResultReview { get; set; }
        public string PanelBackground { get; set; } = "";
        public string PrimaryCopyBackground { get; set; } = "";
        public string PrimaryCopyColor { get; set; } = "";
        public string SecondaryCopyColor { get; set; } = "";
        public string SecondaryActionBackground { get; set; } = "";
        public string SecondaryActionColor { get; set; } = "";
    }

    private sealed class MotionDiagnostics
    {
        public bool ReducedMotionMatches { get; set; }
        public string RootScrollBehavior { get; set; } = "";
        public string LogoAnimationName { get; set; } = "";
        public string WordmarkAnimationName { get; set; } = "";
        public string ActionAnimationName { get; set; } = "";
    }

    private sealed class BadgeMotionDiagnostics
    {
        public string AnimationName { get; set; } = "";
        public string Transform { get; set; } = "";
        public double LongestTransitionMilliseconds { get; set; }
    }

    private sealed class FocusStateDiagnostics
    {
        public string AnimationName { get; set; } = "";
        public double OutlineWidth { get; set; }
        public string OutlineStyle { get; set; } = "";
    }

    private sealed class ContrastDiagnostics
    {
        public bool ForcedColorsMatches { get; set; }
        public bool MoreContrastMatches { get; set; }
        public string ForcedColorAdjust { get; set; } = "";
        public double BorderWidth { get; set; }
        public double OutlineWidth { get; set; }
        public string OutlineStyle { get; set; } = "";
    }

    private sealed class ColorIndependentStateDiagnostics
    {
        public bool SelectedHasText { get; set; }
        public bool SelectedIsProgrammaticallyExposed { get; set; }
        public bool SelectedHasShapeIndicator { get; set; }
        public bool StatusHasText { get; set; }
        public bool StatusHasShapeIndicator { get; set; }
        public bool StatusIsVisible { get; set; }
    }

    private sealed class ColorIndependentErrorDiagnostics
    {
        public bool ErrorHasText { get; set; }
        public bool ErrorIsDescribedByControl { get; set; }
        public bool ErrorIsProgrammaticallyExposed { get; set; }
        public bool ErrorHasShapeIndicator { get; set; }
        public bool ErrorIsVisible { get; set; }
    }

    private sealed class LayoutDiagnostics
    {
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double HorizontalOverflow { get; set; }
        public double RootFontSize { get; set; }
        public bool HasVisibleContent { get; set; }
    }

    private sealed class LongContentDiagnostics
    {
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double HorizontalOverflow { get; set; }
        public LongContentElementDiagnostics[] Elements { get; set; } = [];
    }

    private sealed class LongContentElementDiagnostics
    {
        public string Selector { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsVisible { get; set; }
        public bool TextIsClipped { get; set; }
        public bool IsControl { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double ClientHeight { get; set; }
        public double ScrollHeight { get; set; }
        public bool TextIsContained { get; set; }
        public string WhiteSpace { get; set; } = "";
        public string OverflowWrap { get; set; } = "";
        public double FontSize { get; set; }
        public double LineHeight { get; set; }
    }
}
