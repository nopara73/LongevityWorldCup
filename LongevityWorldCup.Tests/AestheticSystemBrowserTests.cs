using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AestheticSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    internal static readonly string[] RepresentativePaths =
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
        var app = App;
        var browser = Browser;
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
        Assert.Equal("rgb(31, 122, 56)", proof.FilledActionBackground);
        Assert.Equal("rgb(255, 255, 255)", proof.FilledActionColor);
        Assert.False(proof.FilledActionDisabled);
        Assert.NotEqual(readOnly.Background, proof.FilledActionBackground);
        Assert.NotEqual(readOnly.Color, proof.FilledActionColor);

        await NavigateAndSettleAsync(page, "/review");
        await Assertions.Expect(page.Locator("#appReviewText")).ToHaveTextAsync("Result review");
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
        Assert.Equal("rgba(0, 0, 0, 0)", review.PrimaryCopyBackground);
        Assert.Equal("rgb(230, 237, 243)", review.PrimaryCopyColor);
        Assert.Equal("rgb(230, 237, 243)", review.SecondaryCopyColor);
        Assert.Equal("rgb(23, 33, 43)", review.SecondaryActionBackground);
        Assert.Equal("rgb(230, 237, 243)", review.SecondaryActionColor);

        await NavigateAndSettleAsync(page, "/");
        await Assertions.Expect(page.Locator("#viewAllAthletesBtn")).ToHaveCSSAsync("background-color", "rgb(91, 213, 221)");
        await Assertions.Expect(page.Locator("#viewAllAthletesBtn")).ToHaveCSSAsync("color", "rgb(8, 47, 53)");
    }

    [Fact]
    public async Task SelfHostedIcons_RenderEveryVisibleChallengeAndFooterIconWithoutExternalNetwork()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ColorScheme = ColorScheme.Dark,
                ViewportSize = new ViewportSize { Width = 390, Height = 844 }
            });

        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/longevitymaxxing");
        await page.Locator(".lmx-life-strip i").First.WaitForAsync();
        await page.Locator(".footer .footer-link i").First.WaitForAsync();
        await page.WaitForFunctionAsync(
            """
            () => [...document.querySelectorAll('.lmx-page i.fa, .lmx-page i.fas, .lmx-page i.fab, .lmx-page i.fa-solid, .lmx-page i.fa-brands, .footer i.fa, .footer i.fas, .footer i.fab, .footer i.fa-solid, .footer i.fa-brands, .footer .footer-link svg')]
                .filter(element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none'
                        && style.visibility !== 'hidden'
                        && Number(style.opacity) > 0
                        && rect.width > 0
                        && rect.height > 0;
                }).length >= 30
            """);

        var diagnostics = await page.EvaluateAsync<IconFontDiagnostics>(
            """
            () => {
                const selector = [
                    '.lmx-page i.fa',
                    '.lmx-page i.fas',
                    '.lmx-page i.fab',
                    '.lmx-page i.fa-solid',
                    '.lmx-page i.fa-brands',
                    '.footer i.fa',
                    '.footer i.fas',
                    '.footer i.fab',
                    '.footer i.fa-solid',
                    '.footer i.fa-brands'
                ].join(',');
                const isVisible = element => {
                    const style = getComputedStyle(element);
                    const rect = element.getBoundingClientRect();
                    return style.display !== 'none'
                        && style.visibility !== 'hidden'
                        && Number(style.opacity) > 0
                        && rect.width > 0
                        && rect.height > 0;
                };
                const icons = [...document.querySelectorAll(selector)]
                    .filter(isVisible)
                    .map(element => {
                        const pseudo = getComputedStyle(element, '::before');
                        return {
                            Label: element.closest('a, button, [role="button"]')?.textContent?.trim()
                                || element.className,
                            Content: pseudo.content,
                            FontFamily: pseudo.fontFamily,
                            Width: element.getBoundingClientRect().width,
                            Height: element.getBoundingClientRect().height,
                            IsFooterIcon: Boolean(element.closest('.footer'))
                        };
                    });
                const vectorIcons = [...document.querySelectorAll('.footer .footer-link svg')]
                    .filter(isVisible)
                    .map(element => ({
                        Label: element.closest('a')?.textContent?.trim(),
                        Width: element.getBoundingClientRect().width,
                        Height: element.getBoundingClientRect().height,
                        HasPaintedShape: [...element.querySelectorAll('path, circle, ellipse, rect, line, polyline, polygon, use')]
                            .some(shape => {
                                const style = getComputedStyle(shape);
                                const bounds = shape.getBBox();
                                const hasPaint = (color, opacity) => Number(opacity) > 0
                                    && !['none', 'transparent', 'rgba(0, 0, 0, 0)'].includes(color);
                                return Number(style.opacity) > 0
                                    && style.visibility !== 'hidden'
                                    && (bounds.width > 0 || bounds.height > 0)
                                    && (hasPaint(style.fill, style.fillOpacity) || hasPaint(style.stroke, style.strokeOpacity));
                            })
                    }));
                const resources = performance.getEntriesByType('resource')
                    .map(entry => entry.name)
                    .filter(name => name.includes('font-awesome'));
                return {
                    SolidFaceLoaded: document.fonts.check('900 16px "Font Awesome 6 Free"'),
                    BrandFaceLoaded: document.fonts.check('400 16px "Font Awesome 6 Brands"'),
                    Icons: icons,
                    VectorIcons: vectorIcons,
                    FooterIconCount: icons.filter(icon => icon.IsFooterIcon).length + vectorIcons.length,
                    FooterPlatforms: [...document.querySelectorAll('.footer-follow-links .footer-link')]
                        .map(link => link.textContent.trim()),
                    LocalResources: resources.filter(name => new URL(name).origin === location.origin),
                    ExternalResources: resources.filter(name => new URL(name).origin !== location.origin)
                };
            }
            """);

        Assert.True(diagnostics.SolidFaceLoaded, "The self-hosted solid Font Awesome face did not load.");
        Assert.True(diagnostics.BrandFaceLoaded, "The self-hosted brand Font Awesome face did not load.");
        var iconCount = diagnostics.Icons.Length + diagnostics.VectorIcons.Length;
        // Signed out, the habit tiles carry the daily questions, so their four icons are not repeated.
        Assert.True(iconCount >= 30, $"Expected at least 30 visible Challenge/footer icons, found {iconCount}.");
        Assert.Equal(15, diagnostics.FooterIconCount);
        Assert.Equal(["Instagram", "YouTube", "X", "Nostr", "Reddit", "Threads", "Mastodon"], diagnostics.FooterPlatforms);
        Assert.Contains(diagnostics.VectorIcons, icon => icon.Label == "Nostr");
        Assert.All(diagnostics.VectorIcons, icon =>
        {
            Assert.True(icon.Width > 0 && icon.Height > 0, $"{icon.Label} has no rendered geometry.");
            Assert.True(icon.HasPaintedShape, $"{icon.Label} has no visible vector artwork.");
        });
        Assert.All(diagnostics.Icons, icon =>
        {
            Assert.DoesNotContain(icon.Content, new[] { "", "none", "normal", "\"\"" });
            Assert.Contains("Font Awesome", icon.FontFamily);
            Assert.True(icon.Width > 0 && icon.Height > 0, $"{icon.Label} has no rendered geometry.");
        });
        Assert.Contains(
            diagnostics.LocalResources,
            resource => resource.EndsWith("/vendor/font-awesome/6.7.2/webfonts/fa-solid-900.woff2", StringComparison.Ordinal));
        Assert.Contains(
            diagnostics.LocalResources,
            resource => resource.EndsWith("/vendor/font-awesome/6.7.2/webfonts/fa-brands-400.woff2", StringComparison.Ordinal));
        Assert.Empty(diagnostics.ExternalResources);
    }

    [Fact]
    public async Task ReducedMotion_DisablesThePlayEntranceMotion()
    {
        var app = App;
        var browser = Browser;
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
                    FastDuration: getComputedStyle(document.documentElement).getPropertyValue('--lwc-duration-fast').trim(),
                    NormalDuration: getComputedStyle(document.documentElement).getPropertyValue('--lwc-duration-normal').trim(),
                    LogoAnimationName: logo.animationName,
                    WordmarkAnimationName: wordmark.animationName,
                    ActionAnimationName: action.animationName
                };
            }
            """);

        Assert.True(motion.ReducedMotionMatches);
        Assert.Equal("auto", motion.RootScrollBehavior);
        Assert.Equal("140ms", motion.FastDuration);
        Assert.Equal("220ms", motion.NormalDuration);
        Assert.Equal("none", motion.LogoAnimationName);
        Assert.Equal("none", motion.WordmarkAnimationName);
        Assert.Equal("none", motion.ActionAnimationName);
    }

    [Fact]
    public async Task ForcedColorsAndHigherContrast_PreserveVisibleControlBoundariesAndFocus()
    {
        var app = App;
        var browser = Browser;
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
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(
            browser,
            app,
            new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1280, Height = 800 }
            });
        var challengePage = await context.NewPageAsync();
        var applyPage = await context.NewPageAsync();
        var challengeCdp = await context.NewCDPSessionAsync(challengePage);
        var applyCdp = await context.NewCDPSessionAsync(applyPage);
        await Task.WhenAll(
            NavigateAndSettleAsync(challengePage, "/longevitymaxxing"),
            NavigateAndSettleAsync(applyPage, "/apply"));
        await Assertions.Expect(challengePage.Locator(".lmx-page")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(challengePage.Locator("#lmxSignupPanel")).ToBeVisibleAsync();
        await applyCdp.SendAsync("DOM.enable");

        foreach (var deficiency in new[] { "protanopia", "deuteranopia", "tritanopia", "achromatopsia" })
        {
            var emulation = new Dictionary<string, object> { ["type"] = deficiency };
            await Task.WhenAll(
                challengeCdp.SendAsync("Emulation.setEmulatedVisionDeficiency", emulation),
                applyCdp.SendAsync("Emulation.setEmulatedVisionDeficiency", emulation));

            var selectedAndStatus = await challengePage.EvaluateAsync<ColorIndependentStateDiagnostics>(
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

            var error = await applyPage.EvaluateAsync<ColorIndependentErrorDiagnostics>(
                """
                () => {
                    const input = document.getElementById('name');
                    const message = document.getElementById('nameError');
                    input.setAttribute('aria-invalid', 'true');
                    message.textContent = 'Name must contain at least three readable characters.';
                    const messageStyle = getComputedStyle(message);
                    const mark = getComputedStyle(message, '::before');
                    const messageRect = message.getBoundingClientRect();
                    return {
                        ErrorHasText: message.textContent.trim().length > 0,
                        ErrorIsDescribedByControl: input.getAttribute('aria-describedby')
                            ?.split(/\s+/).includes(message.id) === true,
                        ErrorIsProgrammaticallyExposed: input.getAttribute('aria-invalid') === 'true',
                        ErrorHasShapeIndicator: mark.content.includes('✕')
                            && mark.display !== 'none' && mark.visibility === 'visible'
                            && parseFloat(mark.width) >= 16 && parseFloat(mark.height) >= 16,
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
                $"Error state had no non-color error mark under {deficiency} emulation.");
            Assert.True(error.ErrorIsVisible, $"Error text was not visible under {deficiency} emulation.");

            // A computed pseudo-element size is insufficient: display:none on an
            // ancestor can suppress its real box. Check the rendered error disc.
            var document = (await applyCdp.SendAsync("DOM.getDocument"))!.Value;
            var messageNode = (await applyCdp.SendAsync("DOM.querySelector", new()
            {
                ["nodeId"] = document.GetProperty("root").GetProperty("nodeId").GetInt32(),
                ["selector"] = "#nameError"
            }))!.Value;
            var description = (await applyCdp.SendAsync("DOM.describeNode", new()
            {
                ["nodeId"] = messageNode.GetProperty("nodeId").GetInt32(), ["depth"] = 0
            }))!.Value;
            var errorMark = description.GetProperty("node").GetProperty("pseudoElements").EnumerateArray()
                .Single(element => element.GetProperty("pseudoType").GetString() == "before");
            var markBox = (await applyCdp.SendAsync("DOM.getBoxModel", new()
            {
                ["backendNodeId"] = errorMark.GetProperty("backendNodeId").GetInt32()
            }))!.Value.GetProperty("model");
            Assert.Equal(18, markBox.GetProperty("width").GetInt32());
            Assert.Equal(18, markBox.GetProperty("height").GetInt32());
            var markPoints = markBox.GetProperty("border").EnumerateArray().Select(value => value.GetDouble()).ToArray();
            var textLeft = await applyPage.Locator("#nameError").EvaluateAsync<double>(
                """
                element => {
                    const range = document.createRange();
                    range.selectNodeContents(element);
                    return range.getBoundingClientRect().left;
                }
                """);
            Assert.True(markPoints[2] <= textLeft,
                $"Error mark overlaps its text under {deficiency} emulation.");
        }

        var resetEmulation = new Dictionary<string, object> { ["type"] = "none" };
        await Task.WhenAll(
            challengeCdp.SendAsync("Emulation.setEmulatedVisionDeficiency", resetEmulation),
            applyCdp.SendAsync("Emulation.setEmulatedVisionDeficiency", resetEmulation));
        await Task.WhenAll(challengeCdp.DetachAsync(), applyCdp.DetachAsync());
    }

    internal static async Task<InteractionStateDiagnostics[]> MeasureSharedInteractionStatesAsync(IPage page)
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

    internal static void AssertInteractionStateContrast(string mode, InteractionStateDiagnostics[] diagnostics)
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
                $"expected at least {minimumTextContrast:F1}:1. " +
                $"foreground={state.ForegroundColor}, background={state.BackgroundColor}, " +
                $"outer={state.EffectiveOuterBackgroundColor}.");
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

    internal static Task<double> MeasurePlaceholderContrastAsync(ILocator locator)
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

    internal static Task AddRouteStressStateAsync(IBrowserContext context)
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

    internal static string[] GetCanonicalFirstPartyRoutes()
        => LongevityWorldCup.Website.Middleware.RouteCanonicalization.Pages
            .Select(page => page.CanonicalPath)
            .ToArray();

    internal static Task<ExtremeContentDiagnostics> InjectAndMeasureExtremeContentAsync(IPage page)
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

    internal static void AssertExtremeContentLayout(string path, ExtremeContentDiagnostics diagnostics)
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

    internal static ResponsiveMediaBranch[] GetResponsiveMediaInventory()
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
                || relativePath.StartsWith("css/athlete-dialog/", StringComparison.OrdinalIgnoreCase)
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

    internal static ResponsiveMediaBranch[] GetResponsiveScriptMediaInventory()
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

    internal static ResponsiveScriptThreshold[] GetResponsiveScriptThresholdInventory()
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

    internal static ResponsiveContainerCondition[] GetResponsiveContainerInventory()
    {
        var repositoryRoot = FindRepositoryRoot();
        var webRoot = Path.Combine(repositoryRoot, "LongevityWorldCup.Website", "wwwroot");
        var containerPattern = new Regex(
            """@container(?:\s+(?<name>[\w-]+))?\s*(?<query>\([^{};]+)\{""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var widthPattern = new Regex(
            """\(\s*(?<bound>min|max)-width\s*:\s*(?<value>\d+(?:\.\d+)?)(?<unit>rem|em|px)\s*\)""",
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
            // Scoped dialog CSS is generated from the authored styles, not a separate source.
            if (relativePath.StartsWith("internal/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("swagger", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("css/athlete-dialog/", StringComparison.OrdinalIgnoreCase)
                || relativePath.StartsWith("vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.Contains("/vendor/", StringComparison.OrdinalIgnoreCase)
                || relativePath.EndsWith(".min.css", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = Regex.Replace(File.ReadAllText(path), "/\\*.*?\\*/", "", RegexOptions.Singleline);
            foreach (Match match in containerPattern.Matches(text))
            {
                // A bounded range has two edges; inventory every width clause.
                foreach (Match width in widthPattern.Matches(match.Groups["query"].Value))
                {
                    conditions.Add(new ResponsiveContainerCondition(
                        width.Groups["bound"].Value.ToLowerInvariant(),
                        double.Parse(width.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture),
                        width.Groups["unit"].Value.ToLowerInvariant(),
                        relativePath,
                        match.Groups["name"].Success ? match.Groups["name"].Value : null));
                }
            }
        }

        return conditions.Distinct().ToArray();
    }

    internal static IEnumerable<(string Source, string Text)> EnumerateResponsiveScriptSources()
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
        foreach (var path in Directory.EnumerateFiles(frontendRoot, "*", SearchOption.AllDirectories)
                     .Where(path => path.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)))
        {
            var relativePath = "Frontend/" + Path.GetRelativePath(frontendRoot, path).Replace('\\', '/');
            yield return (relativePath, File.ReadAllText(path));
        }
    }

    internal static string NormalizeMediaCondition(string condition)
    {
        var normalized = Regex.Replace(condition, "\\s+", " ").Trim().ToLowerInvariant();
        normalized = Regex.Replace(normalized, "\\s*:\\s*", ": ");
        normalized = Regex.Replace(normalized, "\\(\\s+", "(");
        normalized = Regex.Replace(normalized, "\\s+\\)", ")");
        normalized = Regex.Replace(normalized, "\\s*,\\s*", ", ");
        return normalized;
    }

    internal static ResponsiveFeature[] GetResponsiveFeatures(string query)
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

    internal static IEnumerable<ResponsiveBoundaryCase> CreateResponsiveBoundaryCases(ResponsiveMediaBranch branch)
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

    internal static IEnumerable<ResponsiveScriptBoundaryCase> CreateResponsiveScriptBoundaryCases(
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

    internal static bool EvaluateComparison(int actual, string comparisonOperator, int threshold)
        => comparisonOperator switch
        {
            "<" => actual < threshold,
            "<=" => actual <= threshold,
            ">" => actual > threshold,
            ">=" => actual >= threshold,
            _ => throw new InvalidOperationException($"Unsupported responsive comparison '{comparisonOperator}'.")
        };

    internal static async Task AssertResponsiveContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        foreach (var source in conditions.GroupBy(condition => condition.Source))
        {
            // Each component starts from the same desktop baseline. Other
            // probes resize the page, and filesystem enumeration order varies.
            await page.SetViewportSizeAsync(1280, 800);
            switch (source.Key)
            {
                case "css/longevitymaxxing.css":
                    await AssertChallengeContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "css/bioageform.css":
                    foreach (var route in new[] { "/pheno-age", "/bortz-age" })
                    {
                        await AssertCalculatorContainerBoundariesAsync(page, route, source.ToArray());
                    }
                    break;
                case "css/leaderboard-content.css":
                    await AssertControlBarContainerBoundariesAsync(page,
                        source.Where(condition => condition.Name == "lwc-bar").ToArray());
                    await AssertLeaderboardContainerBoundariesAsync(page,
                        source.Where(condition => condition.Name != "lwc-bar").ToArray());
                    break;
                case "css/leaderboard-ordering.css":
                    await AssertOrderingContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "css/play-menu.css":
                    await AssertJoinContainerBoundariesAsync(page,
                        source.Where(condition => condition.Name != "play-player-card").ToArray());
                    await AssertPlayerCardContainerBoundariesAsync(page,
                        source.Where(condition => condition.Name == "play-player-card").ToArray());
                    break;
                case "css/homepage.css":
                    await AssertNewsletterContainerBoundariesAsync(page,
                        source.Where(condition => condition.Name == "newsletter-section").ToArray());
                    await AssertFontRelativeLeaderboardBoundariesAsync(page,
                        source.Where(condition => condition.Name != "newsletter-section").ToArray());
                    break;
                case "css/site-footer.css":
                    await AssertFooterContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "css/site-header.css":
                    await AssertNavigationContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "css/ro-guide.css":
                    await AssertRejuvenationContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "onboarding/convergence.html":
                    await AssertApplicationContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "play/edit-profile.html":
                    await AssertEditProfileContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "misc-pages/media.html":
                    await AssertMediaKitDocsContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "misc-pages/about.html":
                case "misc-pages/history.html":
                case "misc-pages/ruleset.html":
                case "privacy-policy.html":
                    // These pages share the generator's table CSS. History has
                    // the standings table that actually exercises that component.
                    await AssertDocumentationTableContainerBoundariesAsync(page, source.ToArray());
                    break;
                case "css/documentation-reading.css":
                    await AssertDocumentationReadingContainerBoundariesAsync(page, source.ToArray());
                    break;
                default:
                    Assert.Fail($"Add a rendered container-boundary probe for {source.Key}.");
                    break;
            }
        }
    }

    private static async Task AssertNavigationContainerBoundariesAsync(
        IPage page, ResponsiveContainerCondition[] conditions)
    {
        var condition = Assert.Single(conditions);
        Assert.Null(condition.Name);
        Assert.Equal("max", condition.Bound);
        Assert.Equal("em", condition.Unit);
        Assert.Equal(16, condition.Value);
        var wasReduced = await page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        try
        {
            await NavigateAndSettleAsync(page, "/ruleset");
            await page.Locator("header .site-menu-toggle").ClickAsync();
            foreach (var fontSize in new[] { 16, 32 })
            {
                await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
                var threshold = condition.Value * fontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = threshold + delta;
                    var diagnostics = await page.EvaluateAsync<double[]>("""
                        async width => {
                            const body = document.querySelector('.site-menu-body');
                            Object.assign(body.style, { boxSizing: 'content-box', width: `${width}px`, maxWidth: 'none', flexShrink: '0' });
                            Object.assign(document.querySelector('.site-menu-panel').style, { width: 'max-content', maxWidth: 'none' });
                            await document.fonts.ready;
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const rail = body.querySelector('.site-menu-pages');
                            const links = [...rail.querySelectorAll('.footer-link')];
                            return [parseFloat(getComputedStyle(body).width),
                                getComputedStyle(rail).gridTemplateColumns.split(' ').length,
                                Math.max(0, body.scrollWidth - body.clientWidth),
                                Math.max(0, ...links.map(link => link.scrollWidth - link.clientWidth))];
                        }
                        """, width);
                    Assert.Equal(width, diagnostics[0], 1);
                    Assert.Equal(width <= threshold ? 1 : 2, diagnostics[1]);
                    Assert.True(diagnostics[2] <= 1 && diagnostics[3] <= 1,
                        $"Navigation overflowed at {width}px with {fontSize}px text.");
                }
            }
            await page.Keyboard.PressAsync("Escape");
        }
        finally
        {
            await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
            await page.EmulateMediaAsync(new() { ReducedMotion = wasReduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        }
    }

    private static async Task AssertRejuvenationContainerBoundariesAsync(
        IPage page, ResponsiveContainerCondition[] conditions)
    {
        await page.SetViewportSizeAsync(1920, 1080);
        var wasReduced = await page.EvaluateAsync<bool>("() => matchMedia('(prefers-reduced-motion: reduce)').matches");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        try
        {
            await NavigateAndSettleAsync(page, "/rejuvenation-olympics");
            await page.WaitForSelectorAsync(".ro-standings.is-ready");
            foreach (var fontSize in new[] { 16, 32 })
            {
                await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
                foreach (var condition in conditions)
                {
                    Assert.Equal("max", condition.Bound);
                    Assert.Equal("rem", condition.Unit);
                    if (condition.Name == "ro-timeline") Assert.Equal(36, condition.Value);
                    else
                    {
                        Assert.Null(condition.Name);
                        Assert.Contains(condition.Value, new double[] { 12, 28 });
                    }
                    var threshold = condition.Value * fontSize;
                    foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                    {
                        var width = threshold + delta;
                        var diagnostics = await page.EvaluateAsync<double[]>("""
                            async ({ Width, Name, Boundary }) => {
                                const timeline = Name === 'ro-timeline';
                                const container = document.querySelector(timeline ? '.ro-timeline' : '.ro-standings');
                                Object.assign(container.style, { boxSizing: 'content-box', width: `${Width}px`, maxWidth: 'none', maxInlineSize: 'none' });
                                await document.fonts.ready;
                                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                                let compact, companion;
                                if (timeline) {
                                    const [first, second] = [...container.querySelectorAll('.ro-milestones li')].map(e => e.getBoundingClientRect());
                                    compact = second.top >= first.bottom - 1;
                                    companion = getComputedStyle(container.querySelector('.ro-milestones')).gridTemplateColumns.split(' ').length === 1;
                                } else if (Boundary === 28) {
                                    compact = getComputedStyle(container.querySelector('.ro-podium')).gridTemplateColumns.split(' ').length === 1;
                                    const row = container.querySelector('.ro-row-link');
                                    companion = row.querySelector('.ro-row-score').getBoundingClientRect().top >= row.querySelector('.ro-row-name').getBoundingClientRect().bottom - 1;
                                } else {
                                    compact = getComputedStyle(container.querySelector('.ro-step-link')).display === 'flex';
                                    companion = getComputedStyle(container.querySelector('.ro-row-link')).gridTemplateColumns.split(' ').length === 2;
                                }
                                const text = [...container.querySelectorAll('.ro-step-name,.ro-step-score,.ro-row-name,.ro-row-score,.ro-clock')];
                                return [parseFloat(getComputedStyle(container).width), compact ? 1 : 0, companion ? 1 : 0,
                                    Math.max(0, container.scrollWidth - container.clientWidth),
                                    Math.max(0, ...text.map(e => Math.max(e.scrollWidth - e.clientWidth, e.scrollHeight - e.clientHeight)))];
                            }
                            """, new { Width = width, condition.Name, Boundary = condition.Value });
                        Assert.Equal(width, diagnostics[0], 1);
                        Assert.Equal(width <= threshold ? 1 : 0, diagnostics[1]);
                        Assert.Equal(width <= threshold ? 1 : 0, diagnostics[2]);
                        Assert.True(diagnostics[3] <= 1, $"Rejuvenation {condition.Name ?? "standings"} overflowed at {width}px with {fontSize}px text.");
                        Assert.True(diagnostics[4] <= 1, $"Rejuvenation names, scores or countdown clipped at {width}px with {fontSize}px text.");
                    }
                }
            }
        }
        finally
        {
            await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
            await page.EmulateMediaAsync(new() { ReducedMotion = wasReduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference });
        }
    }

    private static async Task AssertMediaKitDocsContainerBoundariesAsync(
        IPage page, ResponsiveContainerCondition[] conditions)
    {
        await NavigateAndSettleAsync(page, "/media");
        // Render one document row with the page's own markup, independent of the archive.
        await page.EvaluateAsync("""
            () => {
                const list = document.createElement('ul');
                list.className = 'media-kit-file-list media-kit-tiles media-kit-docs';
                list.dataset.docsBoundaryProbe = '';
                list.innerHTML = '<li class="media-file-row media-file-tile media-file-doc"><span class="media-file-thumb media-file-doc-thumb"><i class="fas fa-file-lines media-file-icon" aria-hidden="true"></i></span><span class="media-file-name">FactSheet.txt</span><button type="button" class="media-file-button" aria-label="Download FactSheet.txt">Download</button></li>';
                document.querySelector('#zipPreview').prepend(list);
            }
            """);
        try
        {
            foreach (var fontSize in new[] { 16, 32 })
            {
                await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
                foreach (var condition in conditions)
                {
                    Assert.Equal("media-kit-docs", condition.Name);
                    Assert.Equal("max", condition.Bound);
                    Assert.Equal("rem", condition.Unit);
                    var threshold = condition.Value * fontSize;
                    foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                    {
                        var width = threshold + delta;
                        var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                            async width => {
                                const list = document.querySelector('[data-docs-boundary-probe]');
                                Object.assign(list.style, { boxSizing: 'content-box', width: `${width}px`, maxWidth: 'none', gridTemplateColumns: 'minmax(0, 1fr)' });
                                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                                const icon = list.querySelector('.media-file-doc-thumb').getBoundingClientRect();
                                const name = list.querySelector('.media-file-name').getBoundingClientRect();
                                return {
                                    ContainerWidth: parseFloat(getComputedStyle(list).width),
                                    IsCompact: name.top >= icon.bottom - 1,
                                    HorizontalOverflow: Math.max(0, list.scrollWidth - list.clientWidth)
                                };
                            }
                            """, width);
                        Assert.Equal(width, diagnostics.ContainerWidth, 1);
                        Assert.Equal(width <= threshold, diagnostics.IsCompact);
                        Assert.True(diagnostics.HorizontalOverflow <= 1, $"Media kit document row overflowed at {width}px.");
                    }
                }
            }
        }
        finally
        {
            await page.EvaluateAsync("() => document.documentElement.style.fontSize = ''");
        }
    }

    private static async Task AssertEditProfileContainerBoundariesAsync(
        IPage page, ResponsiveContainerCondition[] conditions)
    {
        var savedSelection = await page.EvaluateAsync<string?[]>("""
            () => ['selectedAthlete', 'tempAthlete'].map(key => sessionStorage.getItem(key))
            """);
        try
        {
            await page.EvaluateAsync("""
                () => {
                    sessionStorage.setItem('selectedAthlete', JSON.stringify({
                        Name: 'Container Probe Athlete', Division: "Men's", Flag: 'Hungary',
                        MediaContact: 'press@example.com', Why: 'A healthy life.', Biomarkers: []
                    }));
                    sessionStorage.removeItem('tempAthlete');
                }
                """);
            await NavigateAndSettleAsync(page, "/edit-profile");
            await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
            // Keep the real card ancestry and CSS, isolating this width probe
            // from the editor's draft/dock observers. Expose both touch sources.
            await page.EvaluateAsync("""
                () => {
                    const original = document.querySelector('.edit-profile-card');
                    const card = original.cloneNode(true);
                    original.replaceWith(card);
                    card.dataset.editCardBoundaryProbe = '';
                    card.querySelector('#takeProfileSelfieButton').style.display = 'inline-flex';
                    card.querySelector('.upload-choice-divider').style.display = 'flex';
                }
                """);
            foreach (var fontSize in new[] { 16, 32 })
            {
                await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
                foreach (var condition in conditions)
                {
                    Assert.Equal("edit-profile-card", condition.Name);
                    Assert.Equal("max", condition.Bound);
                    Assert.Equal("rem", condition.Unit);
                    var threshold = condition.Value * fontSize;
                    foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                    {
                        var width = threshold + delta;
                        var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                            async width => {
                                const card = document.querySelector('[data-edit-card-boundary-probe]');
                                Object.assign(card.style, { boxSizing: 'content-box', width: `${width}px`, maxWidth: 'none' });
                                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                                // Compact cards stack the name above the picture's actions;
                                // wide cards keep both in the portrait's overhang row.
                                const title = card.querySelector('#character-title').getBoundingClientRect();
                                const actions = card.querySelector('#changeProfilePicWrapper').getBoundingClientRect();
                                return {
                                    ContainerWidth: parseFloat(getComputedStyle(card).width),
                                    IsCompact: actions.top >= title.bottom - 1,
                                    HorizontalOverflow: Math.max(0, card.scrollWidth - card.clientWidth)
                                };
                            }
                            """, width);
                        Assert.Equal(width, diagnostics.ContainerWidth, 1);
                        Assert.Equal(width <= threshold, diagnostics.IsCompact);
                        Assert.True(diagnostics.HorizontalOverflow <= 1, $"Edit profile overflowed at {width}px.");
                    }
                }
            }
        }
        finally
        {
            await page.EvaluateAsync("""
                values => {
                    document.documentElement.style.fontSize = '';
                    ['selectedAthlete', 'tempAthlete'].forEach((key, i) => {
                        if (values[i] === null) sessionStorage.removeItem(key);
                        else sessionStorage.setItem(key, values[i]);
                    });
                }
                """, savedSelection);
        }
    }

    private static async Task AssertApplicationContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        await page.SetViewportSizeAsync(1920, 720);
        await NavigateAndSettleAsync(page, "/apply");
        var normalFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var fontSize in new[] { normalFontSize, normalFontSize * 2 })
        {
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
            foreach (var condition in conditions)
            {
                Assert.Null(condition.Name);
                Assert.Equal("min", condition.Bound);
                Assert.Equal("rem", condition.Unit);
                Assert.Equal(35, condition.Value);
                var threshold = condition.Value * fontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = threshold + delta;
                    var diagnostics = await page.EvaluateAsync<double[]>(
                        """
                        async width => {
                            const form = document.getElementById('inputForm');
                            Object.assign(form.style, {
                                boxSizing: 'content-box', width: `${width}px`, maxWidth: 'none', maxInlineSize: 'none'
                            });
                            await document.fonts.ready;
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const fields = ['name', 'division', 'flag'].map(id => document.getElementById(id).getBoundingClientRect());
                            return [parseFloat(getComputedStyle(form).width),
                                getComputedStyle(document.getElementById('personalDetails')).display === 'grid' ? 1 : 0,
                                Math.max(...fields.map(field => field.top)) - Math.min(...fields.map(field => field.top)),
                                fields[1].top - fields[0].bottom, fields[2].top - fields[1].bottom,
                                Math.max(0, form.scrollWidth - form.clientWidth)];
                        }
                        """, width);
                    Assert.Equal(width, diagnostics[0], 1);
                    Assert.Equal(width >= threshold ? 1 : 0, diagnostics[1]);
                    if (width >= threshold)
                        Assert.True(diagnostics[2] <= 1, $"Application fields must share a row at {width}px with {fontSize}px text.");
                    else
                        Assert.True(diagnostics[3] >= 0 && diagnostics[4] >= 0,
                            $"Application fields must stack at {width}px with {fontSize}px text.");
                    Assert.True(diagnostics[5] <= 1, $"Application fields overflowed at {width}px with {fontSize}px text.");
                }
            }
        }
        await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
    }

    private static async Task AssertDocumentationTableContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '20px'");
        var rootFont = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var condition in conditions)
        {
            Assert.Equal("documentation-table", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal("rem", condition.Unit);
            Assert.Equal(24, condition.Value);
            var threshold = condition.Value * rootFont;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.EvaluateAsync<double[]>("""
                    async width => {
                        const frame = document.querySelector('.documentation-table');
                        frame.style.boxSizing = 'content-box';
                        frame.style.inlineSize = width + 'px';
                        frame.style.maxInlineSize = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        return [parseFloat(getComputedStyle(frame).width),
                            getComputedStyle(frame.querySelector('thead')).position === 'absolute' ? 1 : 0,
                            Math.max(0, frame.scrollWidth - frame.clientWidth)];
                    }
                    """, width);
                Assert.Equal(width, diagnostics[0], 1);
                Assert.Equal(width <= threshold ? 1 : 0, diagnostics[1]);
                Assert.True(diagnostics[2] <= 1, $"Documentation standings overflowed at {width}px.");
            }
        }
    }

    private static async Task AssertDocumentationReadingContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        Assert.Contains(conditions, condition => condition.Bound == "min" && condition.Value == 18);
        Assert.Contains(conditions, condition => condition.Bound == "max" && condition.Value == 24);
        await NavigateAndSettleAsync(page, "/history");
        await page.EvaluateAsync("document.documentElement.style.fontSize = '20px'");
        var rootFont = await page.EvaluateAsync<double>("parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var condition in conditions)
        {
            Assert.Equal("documentation-table", condition.Name);
            Assert.Equal("rem", condition.Unit);
            Assert.True(condition.Bound == "min" && condition.Value == 18
                || condition.Bound == "max" && condition.Value == 24);
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = condition.Value * rootFont + delta;
                var diagnostics = await page.EvaluateAsync<double[]>("""
                    async width => {
                        const frame = document.querySelector('.documentation-table');
                        Object.assign(frame.style, { boxSizing: 'content-box', inlineSize: width + 'px', maxInlineSize: 'none' });
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const row = frame.querySelector('tbody tr');
                        const value = row.querySelector('td:nth-child(3)');
                        return [parseFloat(getComputedStyle(frame).width),
                            getComputedStyle(row).display === 'grid' && getComputedStyle(value).gridColumnStart === '3' ? 1 : 0,
                            Math.max(0, frame.scrollWidth - frame.clientWidth)];
                    }
                    """, width);
                Assert.Equal(width, diagnostics[0], 1);
                Assert.Equal(width >= 18 * rootFont && width <= 24 * rootFont ? 1 : 0, diagnostics[1]);
                Assert.True(diagnostics[2] <= 1, $"Documentation reading rows overflowed at {width}px.");
            }
        }
    }

    private static async Task AssertNewsletterContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        await NavigateAndSettleAsync(page, "/");
        foreach (var condition in conditions)
        {
            Assert.Equal("newsletter-section", condition.Name);
            Assert.Equal("min", condition.Bound);
            Assert.Equal("em", condition.Unit);
            Assert.Equal(34, condition.Value);
            await page.Locator(".newsletter-section").EvaluateAsync("section => section.style.fontSize = '20px'");
            var fontSize = await page.Locator(".newsletter-section").EvaluateAsync<double>("section => parseFloat(getComputedStyle(section).fontSize)");
            var threshold = condition.Value * fontSize;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.EvaluateAsync<double[]>("""
                    async width => {
                        const section = document.querySelector('.newsletter-section');
                        section.style.boxSizing = 'content-box';
                        section.style.inlineSize = `${width}px`;
                        section.style.maxInlineSize = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        return [parseFloat(getComputedStyle(section).width),
                            getComputedStyle(section.querySelector('.newsletter-panel')).alignItems === 'center' ? 1 : 0,
                            Math.max(0, section.scrollWidth - section.clientWidth)];
                    }
                    """, width);
                Assert.Equal(width, diagnostics[0], 1);
                Assert.Equal(width >= threshold ? 1 : 0, diagnostics[1]);
                Assert.True(diagnostics[2] <= 1, $"Newsletter overflowed at {width}px.");
            }
        }
    }

    private static async Task AssertOrderingContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        foreach (var condition in conditions)
        {
            Assert.Equal("max", condition.Bound);
            Assert.Equal("em", condition.Unit);
            Assert.Contains(condition.Name, new[] { "ordered-board", "leaderboard-results" });
            var results = condition.Name == "leaderboard-results";
            if (results) Assert.Equal(55, condition.Value);
            else Assert.Contains(condition.Value, new[] { 10d, 18.75d, 32d });
            await page.SetViewportSizeAsync(results ? 1440 : 390, 900);
            await NavigateAndSettleAsync(page, "/leaderboard?sort=pheno&then=age");
            await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            // Keep the real rendered markup and selectors, without live observers moving the probe.
            var fontSize = await page.EvaluateAsync<double>("""
                argument => {
                    const original = document.querySelector('#athleteLeaderboard');
                    const probe = original.cloneNode(true);
                    original.replaceWith(probe);
                    const container = argument.Results ? probe : probe.querySelector(':scope > table');
                    container.dataset.orderBoundaryProbe = '';
                    return parseFloat(getComputedStyle(container).fontSize);
                }
                """, new { Results = results });
            var threshold = condition.Value * fontSize;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                    async argument => {
                        const container = document.querySelector('[data-order-boundary-probe]');
                        Object.assign(container.style, { boxSizing:'content-box', inlineSize:`${argument.Width}px`,
                            width:`${argument.Width}px`, maxWidth:'none', maxInlineSize:'none', flex:'none' });
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const first = container.querySelector(argument.IdentityStack
                            ? 'tbody tr[data-athlete-name] .portrait-wrapper' : argument.DeepStack ? 'tbody tr[data-athlete-name] .leaderboard-order-value' : 'tbody tr[data-athlete-name] .athlete-td');
                        const second = argument.DeepStack
                            ? container.querySelector('tbody tr[data-athlete-name] .leaderboard-order-value ~ .leaderboard-order-value')
                            : argument.IdentityStack ? container.querySelector('tbody tr[data-athlete-name] .athlete-identity')
                            : container.querySelector('tbody tr[data-athlete-name] .leaderboard-order-value');
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(container).width),
                            IsCompact: argument.Results ? getComputedStyle(container.querySelector('tbody tr[data-athlete-name]')).display === 'grid'
                                : second.getBoundingClientRect().top >= first.getBoundingClientRect().bottom - 1,
                            HorizontalOverflow: Math.max(0, container.scrollWidth - container.clientWidth)
                        };
                    }
                    """, new { Width = width, Results = results,
                        DeepStack = !results && condition.Value == 18.75,
                        IdentityStack = !results && condition.Value == 10 });
                Assert.Equal(width, diagnostics.ContainerWidth, 1);
                Assert.True(diagnostics.IsCompact == (width <= threshold),
                    $"Ordering {condition.Name} did not cross {condition.Value}em at {width}px.");
                Assert.True(diagnostics.HorizontalOverflow <= 1,
                    $"Ordering {condition.Name} overflowed by {diagnostics.HorizontalOverflow}px at {width}px.");
                if (!results && condition.Value == 18.75)
                {
                    var clippedHeadings = await page.Locator("[data-order-boundary-probe]").EvaluateAsync<string[]>("""
                        container => [...container.querySelectorAll('thead .leaderboard-column-order')].flatMap(button => {
                            const box = button.getBoundingClientRect();
                            if (!box.width) return [];
                            return [...button.querySelectorAll('.order-heading-label, small')].flatMap(label => {
                                const range = document.createRange(); range.selectNodeContents(label);
                                return [...range.getClientRects()].some(text => text.width
                                    && (text.left < box.left - 1 || text.right > box.right + 1))
                                    ? [label.textContent.trim()] : [];
                            });
                        })
                        """);
                    Assert.True(clippedHeadings.Length == 0,
                        $"Ordering headings clipped at {width}px: {string.Join(", ", clippedHeadings)}.");
                }
            }
        }
    }

    private static async Task AssertPlayerCardContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        if (conditions.Length == 0) return;
        var savedSelection = await page.EvaluateAsync<string?[]>(
            "() => [sessionStorage.getItem('selectedAthlete'), localStorage.getItem('selectedAthleteName')]");
        foreach (var condition in conditions)
        {
            Assert.Equal("play-player-card", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal(20, condition.Value);
            Assert.Equal("em", condition.Unit);
            foreach (var route in new[] { "/select-athlete", "/dashboard" })
            {
                await page.SetViewportSizeAsync(1280, 900);
                await NavigateAndSettleAsync(page, "/select-athlete");
                await page.EvaluateAsync("""
                    () => sessionStorage.setItem('selectedAthlete', JSON.stringify({
                        Name: 'Container Probe Athlete', DisplayName: 'Container Probe Athlete', Biomarkers: []
                    }))
                    """);
                await NavigateAndSettleAsync(page, route);
                // Keep the real card, caption and selector ancestry. A clone
                // isolates this width probe from the live picture/dock observers.
                var fontSize = await page.EvaluateAsync<double>("""
                    () => {
                        const original = document.querySelector('.play-hub-panel:not([hidden]) .play-athlete-card, .play-hub-panel:not([hidden]) .play-dashboard-card');
                        const card = original.cloneNode(true);
                        original.replaceWith(card);
                        card.dataset.playerCardBoundaryProbe = '';
                        card.style.fontSize = '20px';
                        card.style.transition = 'none';
                        return parseFloat(getComputedStyle(card).fontSize);
                    }
                    """);
                var threshold = condition.Value * fontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = threshold + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                        async width => {
                            const card = document.querySelector('[data-player-card-boundary-probe]');
                            Object.assign(card.style, { boxSizing: 'content-box', inlineSize: `${width}px`,
                                width: `${width}px`, maxWidth: 'none', maxInlineSize: 'none' });
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const frame = card.querySelector('.athlete-picture-frame').getBoundingClientRect();
                            const caption = card.querySelector('h1').getBoundingClientRect();
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(card).width),
                                IsCompact: caption.top >= frame.bottom - 1,
                                HorizontalOverflow: Math.max(0, card.scrollWidth - card.clientWidth)
                            };
                        }
                        """, width);
                    Assert.Equal(width, diagnostics.ContainerWidth, 1);
                    Assert.Equal(width <= threshold, diagnostics.IsCompact);
                    Assert.True(diagnostics.HorizontalOverflow <= 1, $"{route} player card overflowed at {width}px.");
                }
            }
        }
        await page.EvaluateAsync("""
            values => {
                for (const [storage, key, value] of [[sessionStorage, 'selectedAthlete', values[0]], [localStorage, 'selectedAthleteName', values[1]]]) {
                    if (value === null) storage.removeItem(key);
                    else storage.setItem(key, value);
                }
            }
            """, savedSelection);
    }

    private static async Task AssertJoinContainerBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        await NavigateAndSettleAsync(page, "/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinGoProButton')?.disabled");
        var rootFontSize = await page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var condition in conditions)
        {
            if (condition.Name == "play-join-actions")
            {
                await AssertJoinActionContainerBoundaryAsync(page, condition);
                await page.SetViewportSizeAsync(1280, 800);
                await NavigateAndSettleAsync(page, "/join");
                continue;
            }
            Assert.Equal("play-track", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal(18, condition.Value);
            Assert.Equal("rem", condition.Unit);
            var threshold = condition.Value * rootFontSize;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async width => {
                        const tracks = document.querySelector('.play-join-cards');
                        const card = tracks.querySelector('.play-join-card--pro');
                        tracks.style.boxSizing = 'content-box';
                        tracks.style.inlineSize = `${width}px`;
                        tracks.style.maxInlineSize = 'none';
                        tracks.style.gridTemplateColumns = 'minmax(0, 1fr)';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(tracks).width),
                            IsCompact: getComputedStyle(card.querySelector('.play-join-ribbon')).position === 'static',
                            HorizontalOverflow: Math.max(0, card.scrollWidth - card.clientWidth)
                        };
                    }
                    """, width);
                Assert.Equal(width, diagnostics.ContainerWidth, 1);
                Assert.Equal(width <= threshold, diagnostics.IsCompact);
                Assert.True(diagnostics.HorizontalOverflow <= 1, $"Join card overflowed at {width}px.");
            }
        }
    }

    private static async Task AssertJoinActionContainerBoundaryAsync(IPage page, ResponsiveContainerCondition condition)
    {
        Assert.Equal("min", condition.Bound);
        Assert.Equal(17, condition.Value);
        Assert.Equal("rem", condition.Unit);
        await page.SetViewportSizeAsync(390, 844);
        await NavigateAndSettleAsync(page, "/join");
        await page.WaitForFunctionAsync("() => !document.getElementById('joinTrackBackBtn')?.disabled");
        // Keep the production markup and styles, while isolating the width probe
        // from dock observers that would restore its viewport-derived width.
        await page.Locator(".play-join-actions").EvaluateAsync("""
            actions => {
                const probe = actions.cloneNode(true);
                actions.replaceWith(probe);
                probe.removeAttribute('data-flow-dock');
                probe.classList.remove('flow-action-stack--dock-entering');
                probe.dataset.joinActionBoundaryProbe = '';
                probe.classList.add('flow-action-stack--docked');
            }
            """);
        foreach (var textPercent in new[] { 100, 200 })
        {
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
            var rootFont = await page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.documentElement).fontSize)");
            var threshold = condition.Value * rootFont;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.Locator("[data-join-action-boundary-probe]").EvaluateAsync<double[]>("""
                    async (actions, width) => {
                        Object.assign(actions.style, { boxSizing: 'content-box', maxInlineSize: 'none' });
                        actions.style.setProperty('width', `${width}px`, 'important');
                        actions.style.setProperty('max-width', 'none', 'important');
                        await document.fonts.ready;
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const back = actions.querySelector('#joinTrackBackBtn');
                        const amateur = actions.querySelector('#joinMobileStartAmateurBtn').getBoundingClientRect();
                        const pro = actions.querySelector('#joinMobileGoProButton').getBoundingClientRect();
                        return [parseFloat(getComputedStyle(actions).width),
                            back.querySelector('.flow-action__label').getBoundingClientRect().width,
                            Math.abs(amateur.top - pro.top),
                            Math.max(0, actions.scrollWidth - actions.clientWidth)];
                    }
                    """, width);
                Assert.Equal(width, diagnostics[0], 1);
                Assert.Equal(width >= threshold, diagnostics[1] == 0);
                if (width >= threshold) Assert.True(diagnostics[2] <= 1, "Compact track actions must share a row.");
                Assert.True(diagnostics[3] <= 1, $"Join actions overflowed at {width}px and {textPercent}% text.");
            }
        }
        await page.EvaluateAsync("() => document.documentElement.style.removeProperty('font-size')");
    }

    private static async Task AssertFooterContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        await NavigateAndSettleAsync(page, "/ruleset");
        var normalFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");
        foreach (var fontSize in new[] { normalFontSize, normalFontSize * 2 })
        {
            await page.EvaluateAsync("size => document.documentElement.style.fontSize = size + 'px'", fontSize);
            foreach (var condition in conditions)
            {
                Assert.Null(condition.Name);
                Assert.Equal("max", condition.Bound);
                Assert.Equal("rem", condition.Unit);
                Assert.Equal(20, condition.Value);
                var threshold = condition.Value * fontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = threshold + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                        """
                        async width => {
                            const footer = document.querySelector('.footer-column--follow');
                            footer.style.boxSizing = 'content-box';
                            footer.style.inlineSize = `${width}px`;
                            footer.style.maxInlineSize = 'none';
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const platforms = footer.querySelector('.footer-follow-links:not(.footer-follow-links--primary)');
                            const subscriptions = footer.querySelector('.footer-subscriptions');
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(footer).width),
                                IsCompact: subscriptions.getBoundingClientRect().top >= platforms.getBoundingClientRect().bottom - 1,
                                HorizontalOverflow: Math.max(0, footer.scrollWidth - footer.clientWidth)
                            };
                        }
                        """, width);
                    Assert.Equal(width, diagnostics.ContainerWidth, 1);
                    Assert.Equal(width <= threshold, diagnostics.IsCompact);
                    Assert.True(diagnostics.HorizontalOverflow <= 1, $"Footer overflowed at {width}px with {fontSize}px text.");
                }
            }
        }
    }

    private static async Task AssertControlBarContainerBoundariesAsync(
        IPage page, ResponsiveContainerCondition[] conditions)
    {
        foreach (var condition in conditions)
        {
            Assert.Equal("lwc-bar", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal("rem", condition.Unit);
            Assert.Contains(condition.Value, new[] { 17.5, 68.75 });
            var stackedRanking = condition.Value == 68.75;
            foreach (var fontSize in new[] { 16, 32 })
            {
                await page.SetViewportSizeAsync(stackedRanking ? 2560 : 768, 1024);
                await NavigateAndSettleAsync(page, "/leaderboard?sort=pheno&then=age");
                await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
                await page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}px`", fontSize);
                var threshold = condition.Value * fontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var width = threshold + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                        async argument => {
                            const bar = document.querySelector('.leaderboard-controls');
                            Object.assign(bar.style, { boxSizing:'content-box', inlineSize:`${argument.Width}px`, maxInlineSize:'none' });
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const search = bar.querySelector('#athleteSearch').getBoundingClientRect();
                            const tools = bar.querySelector('label[for="group-flags"]').getBoundingClientRect();
                            const ranking = bar.querySelector('.leaderboard-view-switcher').getBoundingClientRect();
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(bar).width),
                                IsCompact: argument.StackedRanking ? search.top >= ranking.bottom : tools.top >= search.bottom,
                                HorizontalOverflow: Math.max(0, bar.scrollWidth - bar.clientWidth)
                            };
                        }
                        """, new { Width = width, StackedRanking = stackedRanking });
                    Assert.Equal(width, diagnostics.ContainerWidth, 1);
                    Assert.True(diagnostics.IsCompact == (width <= threshold),
                        $"Control bar did not cross {condition.Value}rem at {width}px with {fontSize}px text.");
                    Assert.True(diagnostics.HorizontalOverflow <= 1,
                        $"Control bar overflowed by {diagnostics.HorizontalOverflow}px at {width}px with {fontSize}px text.");
                }
                await page.EvaluateAsync("document.documentElement.style.removeProperty('font-size')");
            }
        }
    }

    private static async Task AssertLeaderboardContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        await AssertFontRelativeLeaderboardBoundariesAsync(page, conditions.Where(condition => condition.Unit == "em").ToArray());
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.SetViewportSizeAsync(1440, 1000);
        await NavigateAndSettleAsync(page, "/leaderboard?filters=open,baby%20boomers");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // Probe the rendered component without hydration or scroll handlers changing its state.
        await page.EvaluateAsync("""
            () => {
                const board = document.querySelector('.leaderboard');
                const probe = board.cloneNode(true);
                probe.id = 'responsive-leaderboard-probe';
                const panel = probe.querySelector('.sidebar');
                panel.classList.add('expanded');
                panel.classList.remove('button-collapsed', 'partially-expanded');
                panel.setAttribute('aria-hidden', 'false');
                board.parentElement.append(probe);
            }
            """);
        var rootFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");

        foreach (var condition in conditions.Where(condition => condition.Name != "flag-results" && condition.Unit != "em"))
        {
            Assert.Equal("leaderboard-results", condition.Name);
            Assert.Equal("max", condition.Bound);
            Assert.Equal(45, condition.Value);
            var thresholdPixels = condition.Unit == "rem"
                ? condition.Value * rootFontSize
                : condition.Value;

            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var inlineSize = thresholdPixels + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async width => {
                        const board = document.querySelector('#responsive-leaderboard-probe');
                        board.style.boxSizing = 'content-box';
                        board.style.inlineSize = `${width}px`;
                        board.style.maxInlineSize = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        await Promise.all(board.getAnimations({ subtree: true })
                            .filter(animation => Number.isFinite(animation.effect.getComputedTiming().endTime))
                            .map(animation => animation.finished.catch(() => {})));
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const panel = board.querySelector('.sidebar');
                        const table = board.querySelector(':scope > table');
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(board).width),
                            IsCompact: getComputedStyle(panel).flexBasis === '100%'
                                && table.getBoundingClientRect().top >= panel.getBoundingClientRect().bottom - 1,
                            HorizontalOverflow: Math.max(0, board.scrollWidth - board.clientWidth)
                        };
                    }
                    """,
                    inlineSize);
                Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                Assert.True(diagnostics.IsCompact == (inlineSize <= thresholdPixels),
                    $"Leaderboard filters did not cross their {condition.Value}{condition.Unit} boundary at {inlineSize}px. " +
                    $"Compact={diagnostics.IsCompact}, width={diagnostics.ContainerWidth}, overflow={diagnostics.HorizontalOverflow}.");
                Assert.True(diagnostics.HorizontalOverflow <= 1,
                    $"Leaderboard overflowed by {diagnostics.HorizontalOverflow}px at {inlineSize}px.");
            }
        }
        await AssertFlagContainerBoundariesAsync(page, conditions.Where(condition => condition.Name == "flag-results").ToArray());
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.NoPreference });
    }

    private static async Task AssertFontRelativeLeaderboardBoundariesAsync(IPage page, ResponsiveContainerCondition[] conditions)
    {
        foreach (var condition in conditions)
        {
            Assert.Equal("em", condition.Unit);
            Assert.Contains(condition.Name, new[] { "league-filters", "podium", "athlete-stats", "athlete-proofs", "athlete-results", "leaderboard" });
            var desktop = condition.Name == "league-filters" || condition.Name == "podium" && condition.Value == 32;
            await page.SetViewportSizeAsync(desktop ? 1440 : 390, desktop ? 1000 : 844);
            await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
            var route = condition.Name == "podium" ? "/"
                : condition.Name is "athlete-stats" or "athlete-proofs" or "athlete-results" ? "/athlete/michael-lustgarten"
                : "/leaderboard?filters=open,baby%20boomers";
            await NavigateAndSettleAsync(page, route);
            if (condition.Name is "league-filters" or "leaderboard")
                await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
            if (condition.Name == "podium") await page.Locator(".podium-item.first").WaitForAsync();
            if (condition.Name == "athlete-results") await page.Locator(".biomarker-overview-table .result-summary").First.WaitForAsync();
            // Clone the rendered component in its existing scope. Hydration and
            // scrolling keep ownership of the original while this probe resizes.
            var fontSize = await page.EvaluateAsync<double>("""
                name => {
                    const source = [...document.querySelectorAll('body *')].find(e => getComputedStyle(e).containerName.split(' ').includes(name));
                    if (!source) throw new Error('Missing query container: ' + name);
                    const probe = source.cloneNode(true);
                    if (name === 'athlete-proofs') probe.querySelector('.proofs-gallery').dataset.proofCount = '2';
                    probe.dataset.responsiveEmProbe = 'true';
                    probe.style.cssText += ';box-sizing:content-box;max-inline-size:none;min-inline-size:0;font-size:20px;flex:none;overflow:visible;scrollbar-gutter:auto;';
                    source.parentElement.append(probe);
                    return parseFloat(getComputedStyle(probe).fontSize);
                }
                """, condition.Name);
            var threshold = condition.Value * fontSize;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var width = threshold + delta;
                var diagnostics = await page.EvaluateAsync<double[]>("""
                    async arg => {
                        const probe = document.querySelector('[data-responsive-em-probe]');
                        probe.style.setProperty('width', `${arg.Width}px`, 'important');
                        probe.style.setProperty('inline-size', `${arg.Width}px`, 'important');
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        await Promise.all(probe.getAnimations({ subtree: true })
                            .filter(animation => Number.isFinite(animation.effect.getComputedTiming().endTime))
                            .map(animation => animation.finished.catch(() => {})));
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        let applied;
                        if (arg.Name === 'league-filters') applied = getComputedStyle(probe.querySelector('.filter-label-copy')).display === 'block';
                        else if (arg.Name === 'athlete-stats') applied = arg.Value < 20
                            ? getComputedStyle(probe.querySelector('.athlete-info-basics tr')).gridTemplateColumns.split(' ').length === 1
                            : parseFloat(getComputedStyle(probe.querySelector('.athlete-age-board > tr')).paddingLeft) < 15.5;
                        else if (arg.Name === 'athlete-proofs') applied = getComputedStyle(probe.querySelector('.proofs-gallery')).gridTemplateColumns.split(' ').length === 1;
                        else if (arg.Name === 'athlete-results') applied = getComputedStyle(probe.querySelector('.result-summary')).display === 'grid';
                        else if (arg.Name === 'leaderboard') {
                            const row = probe.querySelector('tbody tr:not(.no-results):not(.tier-separator)');
                            applied = row.querySelector('.athlete-identity').getBoundingClientRect().top >= row.querySelector('.portrait-wrapper').getBoundingClientRect().bottom - 1;
                        } else if (arg.Value === 32) applied = Math.abs(probe.querySelector('.podium-item.first').getBoundingClientRect().width - probe.getBoundingClientRect().width) <= 2;
                        else {
                            const card = probe.querySelector('.podium-item.first');
                            applied = card.querySelector('.name-row').getBoundingClientRect().top >= card.querySelector('.podium-portrait').getBoundingClientRect().bottom - 1;
                        }
                        return [parseFloat(getComputedStyle(probe).width), applied ? 1 : 0];
                    }
                    """, new { Width = width, condition.Name, condition.Value });
                Assert.True(Math.Abs(width - diagnostics[0]) <= .1,
                    $"{condition.Name} probe width was {diagnostics[0]}px; expected {width}px.");
                var expected = condition.Bound == "min" ? width >= threshold : width <= threshold;
                Assert.True((diagnostics[1] == 1) == expected,
                    $"{condition.Name} did not cross {condition.Value}em at {width}px (applied={diagnostics[1]}).");
            }
            await page.Locator("[data-responsive-em-probe]").EvaluateAsync("probe => probe.remove()");
        }
    }

    private static async Task AssertFlagContainerBoundariesAsync(
        IPage page,
        ResponsiveContainerCondition[] conditions)
    {
        if (conditions.Length == 0) return;
        await NavigateAndSettleAsync(page, "/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        // Isolate a real rendered flag list from filtering and viewport handlers.
        await page.EvaluateAsync("""
            () => {
                const board = document.querySelector('.leaderboard');
                const probe = board.cloneNode(true);
                probe.id = 'responsive-flag-probe';
                probe.querySelector('.sidebar').remove();
                probe.querySelector('table').remove();
                probe.style.display = 'block';
                board.parentElement.append(probe);
            }
            """);
        var rootFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");

        foreach (var condition in conditions)
        {
            Assert.Equal("max", condition.Bound);
            Assert.Equal("rem", condition.Unit);
            Assert.Contains(condition.Value, new double[] { 42, 28, 22, 14 });
            var thresholdPixels = condition.Value * rootFontSize;
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var inlineSize = thresholdPixels + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async argument => {
                        const list = document.querySelector('#responsive-flag-probe .flag-leaderboard');
                        list.style.boxSizing = 'content-box';
                        list.style.inlineSize = `${argument.InlineSize}px`;
                        list.style.maxInlineSize = 'none';
                        list.querySelector('.flag-team').open = argument.Boundary === 14;
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        await Promise.all(list.getAnimations({ subtree: true })
                            .filter(animation => Number.isFinite(animation.effect.getComputedTiming().endTime))
                            .map(animation => animation.finished.catch(() => {})));
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const team = list.querySelector('.flag-team');
                        const name = team.querySelector('.flag-team-name').getBoundingClientRect();
                        const score = team.querySelector('.flag-team-score').getBoundingClientRect();
                        const athleteName = team.querySelector('.flag-athlete-name').getBoundingClientRect();
                        const athleteLink = team.querySelector('.flag-athlete-link');
                        const athleteBox = athleteLink.getBoundingClientRect();
                        const athleteStyle = getComputedStyle(athleteLink);
                        const rootFontSize = parseFloat(getComputedStyle(document.documentElement).fontSize);
                        const compact = argument.Boundary === 42
                            ? getComputedStyle(team.querySelector('.flag-team-portraits')).display === 'none'
                            : argument.Boundary === 28
                                ? Math.abs(team.querySelector('.flag-team-icon').getBoundingClientRect().width - 1.75 * rootFontSize) < .1
                                : argument.Boundary === 22 ? score.top >= name.bottom
                                    : Math.abs(athleteName.left - athleteBox.left - parseFloat(athleteStyle.paddingLeft)) < 1
                                        && Math.abs(athleteBox.right - parseFloat(athleteStyle.paddingRight) - athleteName.right) < 1;
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(list).width),
                            IsCompact: compact,
                            HorizontalOverflow: Math.max(0, list.scrollWidth - list.clientWidth)
                        };
                    }
                    """, new { InlineSize = inlineSize, Boundary = condition.Value });
                Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                Assert.True(diagnostics.IsCompact == (inlineSize <= thresholdPixels),
                    $"Flag results did not cross their {condition.Value}rem boundary at {inlineSize}px. " +
                    $"Compact={diagnostics.IsCompact}, width={diagnostics.ContainerWidth}.");
                Assert.True(diagnostics.HorizontalOverflow <= 1,
                    $"Flag results overflowed by {diagnostics.HorizontalOverflow}px at {inlineSize}px.");
            }
        }
    }

    private static async Task AssertCalculatorContainerBoundariesAsync(
        IPage page,
        string route,
        ResponsiveContainerCondition[] conditions)
    {
        // Previous probes save drafts on pagehide. Reset only calculator fixture
        // drafts as the new document starts, before it chooses its initial step.
        await page.AddInitScriptAsync("for (const storage of [localStorage, sessionStorage]) for (const key of Object.keys(storage)) if (key.startsWith('bioageDraft:')) storage.removeItem(key);");
        await NavigateAndSettleAsync(page, route);
        var rootFontSize = await page.EvaluateAsync<double>(
            "() => parseFloat(getComputedStyle(document.documentElement).fontSize)");

        foreach (var condition in conditions)
        {
            if (condition.Name == "bioage-rank")
            {
                Assert.Equal("max", condition.Bound);
                Assert.Equal("rem", condition.Unit);
                Assert.Equal(24, condition.Value);
                await NavigateAndSettleAsync(page, route);
                await FlowActionDockBrowserTests.FillBioageStepOneAsync(
                    page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
                if (route == "/bortz-age") await FlowActionDockBrowserTests.FillBortzBiomarkersAsync(page);
                else await FlowActionDockBrowserTests.FillPhenoBiomarkersAsync(page);
                await page.Locator(".bioage-calculate-button").ClickAsync();
                await page.Locator(".bioage-rank-summary").WaitForAsync();
                var rankThreshold = condition.Value * rootFontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var inlineSize = rankThreshold + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                        """
                        async argument => {
                            const rank = document.querySelector('[data-bioage-result-rank]');
                            rank.style.boxSizing = 'content-box';
                            rank.style.inlineSize = `${argument.InlineSize}px`;
                            rank.style.maxInlineSize = 'none';
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const avatar = getComputedStyle(rank.querySelector('.bioage-rank-row-avatar'));
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(rank).width),
                                IsCompact: parseFloat(avatar.width) === 28,
                                HorizontalOverflow: Math.max(0, rank.scrollWidth - rank.clientWidth)
                            };
                        }
                        """, new { InlineSize = inlineSize, Boundary = condition.Value });
                    Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                    Assert.Equal(inlineSize <= rankThreshold, diagnostics.IsCompact);
                    Assert.True(diagnostics.HorizontalOverflow <= 1,
                        $"{route} rank preview overflowed at {inlineSize}px.");
                }
                continue;
            }

            if (condition.Name == "bioage-entry")
            {
                Assert.Equal("max", condition.Bound);
                Assert.Contains(condition.Value, new[] { 18.5, 14.5 });
                Assert.Equal("rem", condition.Unit);
                await NavigateAndSettleAsync(page, route);
                await FlowActionDockBrowserTests.FillBioageStepOneAsync(
                    page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
                var entryThreshold = condition.Value * rootFontSize;
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var inlineSize = entryThreshold + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                        """
                        async width => {
                            const entry = document.querySelector('#wbc').closest('.biomarker-card-content');
                            entry.style.boxSizing = 'content-box';
                            entry.style.inlineSize = `${width}px`;
                            entry.style.maxInlineSize = 'none';
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const input = entry.querySelector('input').getBoundingClientRect();
                            const unit = entry.querySelector('select').getBoundingClientRect();
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(entry).width),
                                IsCompact: input.height >= 44 && unit.height >= 44 && unit.top >= input.bottom - 1,
                                HorizontalOverflow: Math.max(0, entry.scrollWidth - entry.clientWidth)
                            };
                        }
                        """, inlineSize);
                    Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                    // The original 18.5rem rule remains for shared forms; the
                    // calculators' unified measurement overrides it and stacks
                    // at 14.5rem. Exercise both declared boundaries in both
                    // directions, including the overridden rule.
                    Assert.True(diagnostics.IsCompact == (inlineSize <= 14.5 * rootFontSize),
                        $"{route} measurement did not use its 14.5rem boundary at {inlineSize}px (declared query: {condition.Value}rem).");
                    Assert.True(diagnostics.HorizontalOverflow <= 1,
                        $"{route} biomarker entry overflowed at {inlineSize}px.");
                }
                // The following probes exercise the date step, including after reload.
                await page.Locator("#lwcToStep1Btn").ClickAsync();
                continue;
            }

            Assert.Equal("bioage-form", condition.Name);
            Assert.Equal("max", condition.Bound);
            await NavigateAndSettleAsync(page, route);
            var selector = condition.Value switch
            {
                38 => "#lwc-step-1",
                15 => "#dobFieldset",
                14 when condition.Unit == "em" => "#dobFieldset",
                _ => throw new InvalidOperationException(
                    $"Add a rendered calculator probe for {condition.Value}{condition.Unit}.")
            };
            // Em queries use the container's font, not the root font. Give the
            // date probe a distinct font size so a rem/em mix-up cannot pass.
            var emDateProbe = condition.Unit == "em";
            if (emDateProbe)
                await page.Locator("form.bioageform").EvaluateAsync("form => form.style.fontSize = '20px'");
            var containerFontSize = await page.Locator("form.bioageform").EvaluateAsync<double>("form => parseFloat(getComputedStyle(form).fontSize)");
            var thresholdPixels = condition.Unit == "rem" ? condition.Value * rootFontSize
                : emDateProbe ? condition.Value * containerFontSize : condition.Value;

            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var inlineSize = thresholdPixels + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async argument => {
                        const form = document.querySelector('form.bioageform');
                        form.style.boxSizing = 'content-box';
                        form.style.inlineSize = `${argument.InlineSize}px`;
                        form.style.maxInlineSize = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(form).width),
                            IsCompact: argument.EmDateProbe
                                ? form.querySelector('#dob-month-label').getBoundingClientRect().top >= form.querySelector('#dob-year').getBoundingClientRect().bottom
                                : getComputedStyle(form.querySelector(argument.Selector)).display === 'block',
                            HorizontalOverflow: Math.max(0, form.scrollWidth - form.clientWidth)
                        };
                    }
                    """,
                    new { InlineSize = inlineSize, Selector = selector, EmDateProbe = emDateProbe });
                Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                Assert.True(diagnostics.IsCompact == (inlineSize <= thresholdPixels),
                    $"{route} {selector} did not cross its {condition.Value}{condition.Unit} boundary at {inlineSize}px.");
                Assert.True(diagnostics.HorizontalOverflow <= 1,
                    $"{route} overflowed by {diagnostics.HorizontalOverflow}px at {inlineSize}px.");
            }
        }
    }

    private static async Task AssertChallengeContainerBoundariesAsync(
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
            // Each container rule needs a probe of the component it actually changes.
            Assert.Equal("css/longevitymaxxing.css", condition.Source);
            Assert.Equal("max", condition.Bound);
            var thresholdPixels = condition.Unit == "rem"
                ? condition.Value * rootFontSize
                : condition.Value;
            if (condition.Name is null && (condition.Unit == "px" && condition.Value is 280 or 420
                || condition.Unit == "rem" && condition.Value is 12 or 20))
            {
                await AssertAdviceHeaderContainerBoundaryAsync(page, condition, thresholdPixels);
                if (condition.Unit == "px" && condition.Value == 420 || condition.Unit == "rem" && condition.Value == 20) continue;
            }
            if (condition.Name is null && condition.Unit == "rem" && condition.Value == 12)
            {
                await AssertUnavailableDiscussionContextBoundaryAsync(page, thresholdPixels);
                continue;
            }
            if (condition.Name == "lmx-checkin")
            {
                foreach (var delta in new[] { -1, 0, 1, 0, -1 })
                {
                    var inlineSize = thresholdPixels + delta;
                    var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                        """
                        async argument => {
                            let entry = document.getElementById('responsive-checkin-probe');
                            if (!entry) {
                                entry = document.createElement('div');
                                entry.id = 'responsive-checkin-probe';
                                entry.className = 'lmx-checkin-entry';
                                entry.innerHTML = `<div class="lmx-question">
                                    <div class="lmx-question-label">Did you set yourself up for good sleep last night?</div>
                                    <div class="lmx-growth-control"><figure class="lmx-plant"></figure>
                                        <div class="lmx-answer-options">${[0, 1, 2].map(() => '<label class="lmx-answer-option"><span class="lmx-answer-face"></span></label>').join('')}</div>
                                    </div></div>
                                    <div class="lmx-checkin-save-row">
                                        <span class="lmx-checkin-progress">0/4 answered</span>
                                        <button class="lmx-button" type="submit">Save</button>
                                    </div>`;
                                document.querySelector('main').append(entry);
                            }
                            entry.style.inlineSize = `${argument.InlineSize}px`;
                            await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                            const question = entry.querySelector('.lmx-question');
                            let isCompact;
                            if (argument.Unit === 'rem' && argument.Boundary === 28)
                                isCompact = getComputedStyle(question).gridTemplateColumns.split(' ').length === 2;
                            else if (argument.Unit === 'rem' && argument.Boundary === 22)
                                isCompact = getComputedStyle(question).gridTemplateAreas.startsWith('"label label"');
                            else if (argument.Unit === 'px' && argument.Boundary === 240)
                                isCompact = getComputedStyle(entry.querySelector('.lmx-answer-options')).gridTemplateColumns.split(' ').length === 1;
                            else if (argument.Unit === 'rem' && argument.Boundary === 14)
                                isCompact = getComputedStyle(entry.querySelector('.lmx-checkin-progress')).flexBasis === '100%';
                            else
                                throw new Error(`Add a check-in boundary probe for ${argument.Boundary}${argument.Unit}.`);
                            return {
                                ContainerWidth: parseFloat(getComputedStyle(entry).width),
                                IsCompact: isCompact,
                                HorizontalOverflow: Math.max(0, entry.scrollWidth - entry.clientWidth)
                            };
                        }
                        """, new { InlineSize = inlineSize, Boundary = condition.Value, condition.Unit });
                    Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                    Assert.True(diagnostics.IsCompact == (inlineSize <= thresholdPixels),
                        $"Check-in did not cross its {condition.Value}{condition.Unit} boundary at {inlineSize}px.");
                    Assert.True(diagnostics.HorizontalOverflow <= 1,
                        $"Check-in overflowed by {diagnostics.HorizontalOverflow}px at {inlineSize}px for {condition.Value}{condition.Unit}.");
                }
                continue;
            }
            Assert.Null(condition.Name);
            const string surface = "lmx-note";
            // Cross the boundary in both directions on the same component.
            foreach (var delta in new[] { -1, 0, 1, 0, -1 })
            {
                var inlineSize = thresholdPixels + delta;
                var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>(
                    """
                    async argument => {
                        let tile = document.getElementById('responsive-container-probe');
                        if (!tile) {
                            tile = document.createElement('article');
                            tile.id = 'responsive-container-probe';
                            tile.innerHTML = `
                                <div class="lmx-discussion-post-header">
                                    <span class="lmx-discussion-post-author">
                                        <span class="lmx-discussion-author-identity">Discussion author</span>
                                        <small>1 hour ago · Day 90 · 9 replies</small>
                                    </span>
                                    <span class="lmx-discussion-post-actions">
                                        <button class="lmx-discussion-copy-link">Link</button>
                                        <button class="lmx-discussion-reply">Reply</button>
                                    </span>
                                </div>
                                <p>A complete opening post that wraps within its card.</p>`;
                            (document.querySelector('main') || document.body).append(tile);
                        }
                        tile.className = argument.Surface;
                        tile.style.boxSizing = 'content-box';
                        tile.style.inlineSize = `${argument.InlineSize}px`;
                        tile.style.maxInlineSize = 'none';
                        tile.style.flex = 'none';
                        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                        const header = tile.querySelector('.lmx-discussion-post-header');
                        const root = document.documentElement;
                        const body = document.body;
                        return {
                            ContainerWidth: parseFloat(getComputedStyle(tile).width),
                            IsCompact: getComputedStyle(header).display === 'grid',
                            BodyIndent: parseFloat(getComputedStyle(tile.querySelector('p')).marginLeft),
                            HorizontalOverflow: Math.max(
                                0,
                                tile.scrollWidth - tile.clientWidth,
                                root.scrollWidth - root.clientWidth,
                                body.scrollWidth - root.clientWidth)
                        };
                    }
                    """,
                    new { InlineSize = inlineSize, Surface = surface });

                var conditionMatches = condition.Bound == "min"
                    ? inlineSize >= thresholdPixels
                    : inlineSize <= thresholdPixels;
                Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
                Assert.Equal(conditionMatches, diagnostics.IsCompact);
                Assert.Equal(conditionMatches ? 0 : 3 * rootFontSize, diagnostics.BodyIndent, 1);
                Assert.True(
                    diagnostics.HorizontalOverflow <= 1,
                    $"Container query from {condition.Source} caused {diagnostics.HorizontalOverflow}px " +
                    $"horizontal overflow at {inlineSize:F1}px.");
            }
        }
    }

    private static async Task AssertAdviceHeaderContainerBoundaryAsync(
        IPage page, ResponsiveContainerCondition condition, double thresholdPixels)
    {
        foreach (var delta in new[] { -1, 0, 1, 0, -1 })
        {
            var width = thresholdPixels + delta;
            var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                async argument => {
                    let post = document.getElementById('responsive-advice-probe');
                    if (!post) {
                        post = document.createElement('article');
                        post.id = 'responsive-advice-probe';
                        post.className = 'lmx-note lmx-advice-post' + (argument.Unit === 'rem' && argument.Boundary === 12 ? ' lmx-needs-advice' : '');
                        post.innerHTML = `<div class="lmx-discussion-post-header">
                            <span class="lmx-discussion-post-author">
                                <span class="lmx-discussion-author-identity"><span class="lmx-discussion-author-avatar"></span><strong>Ari Able</strong></span>
                                <small>1 hour ago</small>
                            </span>
                            <span class="lmx-discussion-post-actions"><button class="lmx-discussion-copy-link">Link</button><button class="lmx-discussion-reply">Reply</button></span>
                        </div>`;
                        document.querySelector('main').append(post);
                    }
                    post.style.cssText = `box-sizing:content-box;inline-size:${argument.Width}px;max-inline-size:none;max-width:none;`;
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    const author = post.querySelector('.lmx-discussion-post-author');
                    const actions = post.querySelector('.lmx-discussion-post-actions');
                    const metadata = author.querySelector('small');
                    return {
                        ContainerWidth: parseFloat(getComputedStyle(post).width),
                        IsCompact: argument.Unit === 'rem' ? getComputedStyle(actions).gridRowStart === '3'
                            : argument.Boundary === 420 ? getComputedStyle(author).display === 'contents'
                            : parseFloat(getComputedStyle(metadata).marginLeft) === 0,
                        HorizontalOverflow: Math.max(0, post.scrollWidth - post.clientWidth)
                    };
                }
                """, new { Width = width, condition.Unit, Boundary = condition.Value });
            Assert.Equal(width, diagnostics.ContainerWidth, 1);
            Assert.Equal(width <= thresholdPixels, diagnostics.IsCompact);
            Assert.True(diagnostics.HorizontalOverflow <= 1,
                $"Advice header overflowed at {width}px around {condition.Value}{condition.Unit}.");
        }
        await page.Locator("#responsive-advice-probe").EvaluateAsync("post => post.remove()");
    }

    private static async Task AssertUnavailableDiscussionContextBoundaryAsync(IPage page, double thresholdPixels)
    {
        foreach (var delta in new[] { -1, 0, 1, 0, -1 })
        {
            var inlineSize = thresholdPixels + delta;
            var diagnostics = await page.EvaluateAsync<ContainerQueryDiagnostics>("""
                async inlineSize => {
                    let tile = document.getElementById('responsive-unavailable-context-probe');
                    if (!tile) {
                        tile = document.createElement('article');
                        tile.id = 'responsive-unavailable-context-probe';
                        tile.className = 'lmx-note';
                        tile.innerHTML = `<div class="lmx-discussion-reply-item">
                            <div class="lmx-discussion-reply-meta">
                                <span class="lmx-discussion-author-avatar"></span>
                                <div class="lmx-discussion-reply-byline">
                                    <span class="lmx-discussion-context-link unavailable"><i class="fas fa-reply" aria-hidden="true"></i>Original reply unavailable</span>
                                </div>
                            </div></div>`;
                        document.querySelector('main').append(tile);
                    }
                    tile.style.boxSizing = 'content-box';
                    tile.style.inlineSize = `${inlineSize}px`;
                    tile.style.maxInlineSize = 'none';
                    await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                    const context = tile.querySelector('.lmx-discussion-context-link.unavailable');
                    return {
                        ContainerWidth: parseFloat(getComputedStyle(tile).width),
                        IsCompact: getComputedStyle(context).display === 'block',
                        HorizontalOverflow: Math.max(0, tile.scrollWidth - tile.clientWidth)
                    };
                }
                """, inlineSize);
            Assert.Equal(inlineSize, diagnostics.ContainerWidth, 1);
            Assert.Equal(inlineSize <= thresholdPixels, diagnostics.IsCompact);
            Assert.True(diagnostics.HorizontalOverflow <= 1,
                $"Deleted reply context overflowed by {diagnostics.HorizontalOverflow}px at {inlineSize}px.");
        }
    }

    internal static string MapResponsiveSourceToRoute(string source)
    {
        var normalized = source.Replace('\\', '/').ToLowerInvariant();
        if (normalized == "index.html" || normalized.Contains("site-statistics")) return "/";
        if (normalized.Contains("leaderboard") || normalized.Contains("guess-my-age") || normalized.Contains("badges")) return "/leaderboard";
        if (normalized.Contains("event-board")) return "/events";
        if (normalized.Contains("longevitymaxxing")) return "/longevitymaxxing";
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

    internal static ViewportSize CreateMatchingViewport(string query, int? fixedWidth, int? fixedHeight)
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

    internal static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.Website", "LongevityWorldCup.Website.csproj"))
                && File.Exists(Path.Combine(directory.FullName, "LongevityWorldCup.Tests", "LongevityWorldCup.Tests.csproj")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the LongevityWorldCup repository root.");
    }

    internal static Task<LongContentDiagnostics> InjectAndMeasureLongContentAsync(
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

    internal static void AssertLongContentLayout(string path, LongContentDiagnostics diagnostics)
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

    internal static async Task<IBrowserContext> NewContextAsync(
        IBrowser browser,
        BrowserTestApp app,
        BrowserNewContextOptions options)
    {
        options.BaseURL = app.BaseAddress.ToString();
        options.Locale = "en-US";
        var context = await browser.NewContextAsync(options);
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }

    internal static async Task NavigateAndSettleAsync(IPage page, string path)
    {
        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        // DOMContentLoaded does not wait for a stylesheet that follows the page's scripts.
        // Measure the rendered design only after those styles and their fonts have loaded.
        await page.WaitForFunctionAsync(
            "() => [...document.querySelectorAll('link[rel=stylesheet]')].every(link => link.sheet !== null)");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view')");
        await page.EvaluateAsync("() => document.fonts?.ready || Promise.resolve()");
    }

    internal static Task<LayoutDiagnostics> MeasureLayoutAsync(IPage page)
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

    internal sealed record ResponsiveMediaBranch(string Query, string Source);

    internal sealed record ResponsiveFeature(string Bound, string Axis, int Value);

    internal sealed record ResponsiveBoundaryCase(
        ResponsiveMediaBranch Branch,
        ResponsiveFeature Feature,
        int Width,
        int Height,
        bool ExpectedMatch);

    internal sealed record ResponsiveLayoutProbe(
        string Route,
        int Width,
        int Height,
        string Source,
        string Query);

    internal sealed record ResponsiveScriptThreshold(
        string Axis,
        string Operator,
        int Value,
        string Source);

    internal sealed record ResponsiveScriptBoundaryCase(
        ResponsiveScriptThreshold Threshold,
        int Width,
        int Height,
        bool ExpectedMatch);

    internal sealed record ResponsiveContainerCondition(
        string Bound,
        double Value,
        string Unit,
        string Source,
        string? Name = null);

    internal sealed class ScriptViewportDiagnostics
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    internal sealed class ContainerQueryDiagnostics
    {
        public double ContainerWidth { get; set; }
        public bool IsCompact { get; set; }
        public double BodyIndent { get; set; }
        public double HorizontalOverflow { get; set; }
    }

    internal sealed class InteractionStateDiagnostics
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

    internal sealed class ExtremeContentDiagnostics
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

    internal sealed class ExtremeContentElementDiagnostics
    {
        public string Selector { get; set; } = "";
        public bool IsVisible { get; set; }
        public bool TextIsClipped { get; set; }
        public double FontSize { get; set; }
        public double LineHeight { get; set; }
        public double Height { get; set; }
    }

    internal sealed class DarkThemeDiagnostics
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

    internal sealed class DarkControlDiagnostics
    {
        public string Background { get; set; } = "";
        public string Color { get; set; } = "";
        public string BorderColor { get; set; } = "";
        public bool IsReadOnly { get; set; }
        public bool MatchesReadOnly { get; set; }
        public bool ServedOverridePresent { get; set; }
    }

    internal sealed class DarkProofDiagnostics
    {
        public bool HasExpectedAthleteCopy { get; set; }
        public string MainCopyColor { get; set; } = "";
        public string SupportingCopyColor { get; set; } = "";
        public string FilledActionBackground { get; set; } = "";
        public string FilledActionColor { get; set; } = "";
        public bool FilledActionDisabled { get; set; }
    }

    internal sealed class DarkReviewDiagnostics
    {
        public bool ShowsResultReview { get; set; }
        public string PanelBackground { get; set; } = "";
        public string PrimaryCopyBackground { get; set; } = "";
        public string PrimaryCopyColor { get; set; } = "";
        public string SecondaryCopyColor { get; set; } = "";
        public string SecondaryActionBackground { get; set; } = "";
        public string SecondaryActionColor { get; set; } = "";
    }

    internal sealed class IconFontDiagnostics
    {
        public bool SolidFaceLoaded { get; set; }
        public bool BrandFaceLoaded { get; set; }
        public IconGlyphDiagnostics[] Icons { get; set; } = [];
        public VectorIconDiagnostics[] VectorIcons { get; set; } = [];
        public int FooterIconCount { get; set; }
        public string[] FooterPlatforms { get; set; } = [];
        public string[] LocalResources { get; set; } = [];
        public string[] ExternalResources { get; set; } = [];
    }

    internal sealed class IconGlyphDiagnostics
    {
        public string Label { get; set; } = "";
        public string Content { get; set; } = "";
        public string FontFamily { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public bool IsFooterIcon { get; set; }
    }

    internal sealed class VectorIconDiagnostics
    {
        public string Label { get; set; } = "";
        public double Width { get; set; }
        public double Height { get; set; }
        public bool HasPaintedShape { get; set; }
    }

    internal sealed class MotionDiagnostics
    {
        public bool ReducedMotionMatches { get; set; }
        public string RootScrollBehavior { get; set; } = "";
        public string FastDuration { get; set; } = "";
        public string NormalDuration { get; set; } = "";
        public string LogoAnimationName { get; set; } = "";
        public string WordmarkAnimationName { get; set; } = "";
        public string ActionAnimationName { get; set; } = "";
    }

    internal sealed class BadgeMotionDiagnostics
    {
        public string AnimationName { get; set; } = "";
        public string Transform { get; set; } = "";
        public double LongestTransitionMilliseconds { get; set; }
    }

    internal sealed class FocusStateDiagnostics
    {
        public string AnimationName { get; set; } = "";
        public double OutlineWidth { get; set; }
        public string OutlineStyle { get; set; } = "";
    }

    internal sealed class ContrastDiagnostics
    {
        public bool ForcedColorsMatches { get; set; }
        public bool MoreContrastMatches { get; set; }
        public string ForcedColorAdjust { get; set; } = "";
        public double BorderWidth { get; set; }
        public double OutlineWidth { get; set; }
        public string OutlineStyle { get; set; } = "";
    }

    internal sealed class ColorIndependentStateDiagnostics
    {
        public bool SelectedHasText { get; set; }
        public bool SelectedIsProgrammaticallyExposed { get; set; }
        public bool SelectedHasShapeIndicator { get; set; }
        public bool StatusHasText { get; set; }
        public bool StatusHasShapeIndicator { get; set; }
        public bool StatusIsVisible { get; set; }
    }

    internal sealed class ColorIndependentErrorDiagnostics
    {
        public bool ErrorHasText { get; set; }
        public bool ErrorIsDescribedByControl { get; set; }
        public bool ErrorIsProgrammaticallyExposed { get; set; }
        public bool ErrorHasShapeIndicator { get; set; }
        public bool ErrorIsVisible { get; set; }
    }

    internal sealed class LayoutDiagnostics
    {
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double HorizontalOverflow { get; set; }
        public double RootFontSize { get; set; }
        public bool HasVisibleContent { get; set; }
    }

    internal sealed class LongContentDiagnostics
    {
        public double ClientWidth { get; set; }
        public double ScrollWidth { get; set; }
        public double HorizontalOverflow { get; set; }
        public LongContentElementDiagnostics[] Elements { get; set; } = [];
    }

    internal sealed class LongContentElementDiagnostics
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
