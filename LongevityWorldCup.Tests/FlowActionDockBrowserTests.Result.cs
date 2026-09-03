using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlowActionDockResultBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task DesktopBioageStepActions_DockAsGroupedCommands(string path)
    {
        var app = App;
        var browser = Browser;
        // The compact step-one form fits inline at 768px; constrain the height to exercise the dock.
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1366, Height = 650 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await ExpectActionStackDockedInViewportAsync(page, "#lwcStepOneActions");

        var layout = await ReadFlowActionChildLayoutAsync(page, "#lwcStepOneActions");
        Assert.Equal(2, layout.Count);
        Assert.True(layout.MaxGap <= 24,
            $"{path}: docked bioage commands are split apart by {layout.MaxGap}px instead of staying grouped.");
        Assert.True(errors.Count == 0, $"{path}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData("/pheno-age", 1280, 720)]
    [InlineData("/bortz-age", 1280, 720)]
    [InlineData("/pheno-age", 390, 844)]
    [InlineData("/bortz-age", 390, 844)]
    [InlineData("/pheno-age", 430, 932)]
    [InlineData("/bortz-age", 430, 932)]
    public async Task BioageStepOne_DoesNotHalfCoverDatePanelsWithActions(
        string path,
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

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
        await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
        await WaitForManagedActionStacksSettledAsync(page);

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

        var scenario = $"{path} @ {viewportWidth}x{viewportHeight}";
        Assert.True(layout.ScrollY <= 1,
            $"{scenario}: bioage first load should not auto-scroll the header out of view. {layout}");
        Assert.True(layout.Action.Bottom <= layout.ViewportHeight + 1,
            $"{scenario}: bioage step actions are below the viewport: {layout.Action.Bottom} > {layout.ViewportHeight}. {layout}");
        Assert.True(layout.Day.Bottom <= layout.Action.Top - 6,
            $"{scenario}: day selector is covered by actions. {layout}");
        if (layout.BloodDraw.Bottom <= layout.Action.Top - 6)
        {
            Assert.True(layout.BloodDrawInput.Bottom <= layout.Action.Top - 6,
                $"{scenario}: blood draw input is covered by actions. {layout}");
        }
        Assert.True(layout.Privacy.Bottom <= layout.Action.Top - 6,
            $"{scenario}: privacy note is covered by actions. {layout}");
        Assert.True(layout.BloodDraw.Bottom <= layout.Action.Top - 6 || layout.BloodDraw.Top >= layout.Action.Bottom - 1,
            $"{scenario}: blood draw panel should not be half-covered by actions. {layout}");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DesktopTwoActionDock_MakesBackSecondaryAndKeepsPrimaryCentered()
    {
        var app = App;
        var browser = Browser;
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

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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
        Assert.True(state.ActionsParentIsBody, path);
        Assert.True(errors.Count == 0, $"{path}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData(1366, 768, 340)]
    [InlineData(1280, 720, 288)]
    public async Task DesktopSelectAthlete_KeepsPictureInputAndInlineActionsVisibleAtCommonViewport(
        int viewportWidth,
        int viewportHeight,
        double minPictureWidth)
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
        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        await WaitForManagedActionStacksSettledAsync(page);
        await ExpectActionStackInViewportAsync(page, ".play-athlete-actions");

        var pictureRect = await ReadElementRectAsync(page, "#athleteSelectionPicture");
        var inputRect = await ReadElementRectAsync(page, "#playAthleteInput");
        var actionsRect = await ReadElementRectAsync(page, ".play-athlete-actions");
        var scenario = $"{viewportWidth}x{viewportHeight}";

        Assert.True(pictureRect.Width >= minPictureWidth,
            $"{scenario}: athlete picture is too small for the available desktop space: {pictureRect.Width}px < {minPictureWidth}px.");
        Assert.True(pictureRect.Bottom <= inputRect.Top - 16,
            $"{scenario}: athlete picture overlaps the search field: picture bottom {pictureRect.Bottom}, input top {inputRect.Top}.");
        Assert.True(inputRect.Bottom <= actionsRect.Top - 16,
            $"{scenario}: athlete input overlaps the actions: input bottom {inputRect.Bottom}, actions top {actionsRect.Top}.");
        Assert.False(await HasDockClassAsync(page, ".play-athlete-actions"),
            $"{scenario}: athlete actions should stay inline when the full selection flow fits in the common desktop viewport.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
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
            const isPro = new URL(window.location.href).searchParams.get('testPro') === '1';
            const athlete = {
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                PersonalLink: 'https://example.test/browser-test-athlete',
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: isPro ? [{ Date: '2026-06-19', Hba1cMmolMol: 35 }] : []
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify(athlete));
            window.localStorage.setItem('selectedAthleteName', athlete.Name);
            window.localStorage.setItem('hasApplication', 'true');
            window.localStorage.setItem('gmaHasPerfectGuess', '1');
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync($"/dashboard?testPro={(isPro ? 1 : 0)}", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        var scenario = $"{(isPro ? "pro" : "amateur")} {viewportWidth}x{viewportHeight}";

        await page.WaitForFunctionAsync(
            """
                    () => document.getElementById('athleteDashboardPanel')?.hidden === false
                        && document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4
                        && document.getElementById('athleteDashboardActions')?.getBoundingClientRect().width > 0
                    """);
        await page.EvaluateAsync("() => window.LwcFlowActionDock?.refreshNow?.()");
        await WaitForManagedActionStacksSettledAsync(page);
        await ExpectActionStackDockedInViewportAsync(page, ".play-dashboard-actions");

        var pictureRect = await ReadElementRectAsync(page, "#athleteDashboardPicture");
        var actionsRect = await ReadElementRectAsync(page, ".play-dashboard-actions");

        Assert.True(actionsRect.Height <= maxDockHeight,
            $"{scenario}: .play-dashboard-actions dock is too tall: {actionsRect.Height}px.");
        var compactLabels = await page.Locator("#athleteDashboardActions .flow-action[data-flow-dock-label]").CountAsync();
        Assert.True(compactLabels == 0, $"{scenario}: found {compactLabels} unexpected compact labels.");

        var actionIconSizes = await page.EvaluateAsync<string[]>(
            """
                    () => Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action > i'))
                        .map(icon => {
                            const style = getComputedStyle(icon);
                            return `${style.width}|${style.fontSize}|${style.lineHeight}`;
                        })
                    """);
        Assert.True(actionIconSizes.Length == 5, $"{scenario}: expected five action icons, found {actionIconSizes.Length}.");
        Assert.True(actionIconSizes.Distinct().Count() == 1,
            $"{scenario}: action icon sizing differed: {string.Join("; ", actionIconSizes)}.");

        if (viewportWidth < 960)
        {
            var visibleActionIcons = await page.EvaluateAsync<int>(
                """
                        () => Array.from(document.querySelectorAll('#athleteDashboardActions .flow-action > i'))
                            .filter(icon => getComputedStyle(icon).display !== 'none').length
                        """);
            Assert.True(visibleActionIcons == 5,
                $"{scenario}: expected five visible action icons, found {visibleActionIcons}.");

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
                $"{scenario}: the Longevitymaxxing action should not split inside its name.");
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
            Assert.True(discountState.DiscountVisible, $"{scenario}: discount details are not visible.");
            Assert.False(discountState.DiscountInsideActionMenu,
                $"{scenario}: discount details belong below the athlete picture, not inside the action menu.");
            Assert.True(discountState.DiscountTop >= discountState.PictureBottom,
                $"{scenario}: discount details should follow the athlete picture in normal flow.");
            Assert.True(discountState.VisibleLineCount == 3,
                $"{scenario}: expected three discount lines, found {discountState.VisibleLineCount}.");
            Assert.Contains("10% leaderboard", discountState.CompactTexts);
            Assert.Contains("10% personal page", discountState.CompactTexts);
            Assert.Contains("10% perfect guess", discountState.CompactTexts);
            Assert.True(discountState.IconWidths.Length == 2,
                $"{scenario}: expected two discount icons, found {discountState.IconWidths.Length}.");
            Assert.All(discountState.IconWidths, width => Assert.InRange(width, 43, 45));
            Assert.All(discountState.IconHeights, height => Assert.InRange(height, 43, 45));
            Assert.True(discountState.TextLefts.Max() - discountState.TextLefts.Min() <= 1,
                $"{scenario}: discount percentages do not share one alignment line.");
        }

        if (viewportWidth >= 960)
        {
            var raisedSecondaryActions = await page.EvaluateAsync<string[]>(
                """
                        () => Array.from(document.querySelectorAll(
                            '.play-dashboard-actions.flow-action-stack--docked .flow-action--secondary'))
                            .map(action => {
                                const style = getComputedStyle(action);
                                return {
                                    action,
                                    boxShadow: style.boxShadow,
                                    backgroundColor: style.backgroundColor
                                };
                            })
                            .filter(item => item.boxShadow !== 'none'
                                || item.backgroundColor !== 'rgba(0, 0, 0, 0)')
                            .map(item => `${item.action.id || item.action.textContent.trim()}: `
                                + `box-shadow=${item.boxShadow}, background=${item.backgroundColor}, `
                                + `class=${item.action.className}`)
                        """);
            Assert.True(raisedSecondaryActions.Length == 0,
                $"{scenario}: desktop secondary actions should read as one command bar: "
                + string.Join("; ", raisedSecondaryActions));

            var minPictureWidth = viewportHeight <= 740 ? 310 : 340;
            Assert.True(pictureRect.Width >= minPictureWidth,
                $"{scenario}: dashboard picture is too small: {pictureRect.Width}px < {minPictureWidth}px.");
        }

        Assert.True(pictureRect.Bottom <= actionsRect.Top - 16,
            $"{scenario}: dashboard picture overlaps the dock: {pictureRect.Bottom} > {actionsRect.Top - 16}.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DashboardActions_StayInlineBelowDiscountsWhenTheViewportHasRoom()
    {
        var app = App;
        var browser = Browser;
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

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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
        var app = App;
        var browser = Browser;
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

        await page.GotoAsync("/dashboard", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#athleteDashboardActions .flow-action').length >= 4");
        await ExpectActionStackDockedInViewportAsync(page, ".play-dashboard-actions");
        await page.Locator("#playDashboardBackBtn > i").EvaluateAsync(
            "icon => { icon.textContent = '\u2190'; icon.style.fontSize = '16px'; }");

        await page.SetViewportSizeAndWaitForLayoutAsync(1366, 768);
        await page.WaitForFunctionAsync(
            """
            () => {
                window.LwcFlowActionDock?.refreshNow?.();
                const actions = document.querySelector('.play-dashboard-actions');
                const button = document.getElementById('playDashboardBackBtn');
                const icon = button?.querySelector('i');
                if (!actions?.classList.contains('flow-action-stack--docked') || !button || !icon) return false;
                const buttonRect = button.getBoundingClientRect();
                const iconRect = icon.getBoundingClientRect();
                return window.innerWidth === 1366
                    && Math.abs(
                        (buttonRect.top + buttonRect.height / 2)
                        - (iconRect.top + iconRect.height / 2)) <= 2;
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
        var app = App;
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            "window.localStorage.clear(); window.sessionStorage.clear();");

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        try
        {
            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
            if (path == "/bortz-age")
                await FillBortzBiomarkersAsync(page);
            else
                await FillPhenoBiomarkersAsync(page);

            await page.Locator(".bioage-calculate-button").ClickAsync();
            await page.WaitForSelectorAsync($"{resultSelector}.show");
            await page.WaitForSelectorAsync("#continueButton.show");

            await ExpectActionStackDockedInViewportAsync(page, resultActionsSelector);
            await ExpectBioageResultReadableWithDockAsync(
                page,
                resultSelector,
                resultActionsSelector);
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
            Assert.True(errors.Count == 0, $"{path}: {string.Join(Environment.NewLine, errors)}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task BioageResult_DoesNotMoveTheReadingPositionWhenRankPreviewExpands()
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
            window.__rankPreviewGate = new Promise(resolve => {
                window.__releaseRankPreview = resolve;
            });
            window.getSharedAthletes = () => window.__rankPreviewGate;
            """);

        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await FillPhenoBiomarkersAsync(page);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForSelectorAsync("#phenoAgeResult.show");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('phenoAgeRankPreview')?.getAttribute('aria-busy') === 'true'");
        await page.WaitForFunctionAsync(
            """
            () => document.getElementById('phenoAgeResult')?.dataset.bioageResultStage === 'rank'
                && document.querySelector('#phenoAgeResult .bio-age-number-container')?.dataset.bioageRevealState === 'complete'
            """);
        await ExpectActionStackDockedInViewportAsync(page, ".phenoage-result-actions");
        await ExpectBioageResultReadableWithDockAsync(page, "#phenoAgeResult", ".phenoage-result-actions");

        await page.EvaluateAsync(
            """
            () => {
                const result = document.getElementById('phenoAgeResult').getBoundingClientRect();
                const dock = document.querySelector('.phenoage-result-actions').getBoundingClientRect();
                window.scrollBy({ top: result.bottom - (dock.top - 12), behavior: 'auto' });
            }
            """);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        var initialLayout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const result = document.getElementById('phenoAgeResult').getBoundingClientRect();
                const dock = document.querySelector('.phenoage-result-actions').getBoundingClientRect();
                return [result.height, result.bottom, dock.top, window.scrollY];
            }
            """);
        Assert.InRange(initialLayout[2] - initialLayout[1], 8, 16);

        await page.EvaluateAsync("() => window.__releaseRankPreview([])");
        await page.WaitForFunctionAsync(
            """
            initialResultHeight => {
                const preview = document.getElementById('phenoAgeRankPreview');
                const result = document.getElementById('phenoAgeResult');
                if (preview?.getAttribute('aria-busy') !== 'false'
                    || !preview.querySelector('.bioage-rank-neighbors')
                    || !result) return false;
                return result.getBoundingClientRect().height >= Number(initialResultHeight) + 50;
            }
            """,
            initialLayout[0]);
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var finalLayout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const result = document.getElementById('phenoAgeResult').getBoundingClientRect();
                const dock = document.querySelector('.phenoage-result-actions').getBoundingClientRect();
                return [result.height, result.bottom, dock.top, window.scrollY];
            }
            """);
        Assert.True(
            finalLayout[0] >= initialLayout[0] + 50,
            $"Rank preview did not materially expand the result: {initialLayout[0]}px to {finalLayout[0]}px.");
        Assert.InRange(
            Math.Abs(finalLayout[3] - initialLayout[3]),
            0,
            2);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task BioageResult_DoesNotChaseContinuousResultResizing()
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
        await page.GotoAsync("/bortz-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await FillBioageStepOneAsync(page, DateTime.UtcNow.Date.AddDays(-9).ToString("yyyy-MM-dd"));
        await FillBortzBiomarkersAsync(page);
        await page.Locator(".bioage-calculate-button").ClickAsync();
        await page.WaitForSelectorAsync("#bortzAgeResult.show");
        await page.WaitForSelectorAsync("#continueButton.show");
        await ExpectActionStackDockedInViewportAsync(page, ".bioage-result-actions");
        await ExpectBioageResultReadableWithDockAsync(page, "#bortzAgeResult", ".bioage-result-actions");

        await page.EvaluateAsync(
            """
            () => {
                const result = document.getElementById('bortzAgeResult');
                const dock = document.querySelector('.bioage-result-actions');
                const resultRect = result.getBoundingClientRect();
                const dockRect = dock.getBoundingClientRect();
                window.scrollBy({ top: resultRect.bottom - (dockRect.top - 12), behavior: 'auto' });
                window.__bioageResizeStartScrollY = window.scrollY;

                const growingContent = document.createElement('div');
                growingContent.setAttribute('aria-hidden', 'true');
                result.append(growingContent);
                window.__bioageResizeStormFrame = 0;
                window.__bioageResizeStormActive = true;
                const growResult = () => {
                    if (!window.__bioageResizeStormActive) return;
                    window.__bioageResizeStormFrame += 1;
                    growingContent.style.height = `${window.__bioageResizeStormFrame}px`;
                    if (window.__bioageResizeStormFrame < 600) {
                        requestAnimationFrame(growResult);
                    }
                };
                requestAnimationFrame(growResult);
            }
            """);
        await page.WaitForFunctionAsync("() => window.__bioageResizeStormFrame >= 24");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                window.__bioageResizeStormActive = false;
                const result = document.getElementById('bortzAgeResult').getBoundingClientRect();
                const dock = document.querySelector('.bioage-result-actions').getBoundingClientRect();
                return [result.bottom, dock.top, window.__bioageResizeStormFrame, window.scrollY, window.__bioageResizeStartScrollY];
            }
            """);

        Assert.InRange(layout[2], 24, 599);
        Assert.InRange(
            Math.Abs(layout[3] - layout[4]),
            0,
            2);
        Assert.Empty(errors);
    }

    private static async Task ExpectBioageResultReadableWithDockAsync(
        IPage page,
        string resultSelector,
        string dockSelector)
    {
        await WaitForManagedActionStacksSettledAsync(page);
        await page.WaitForFunctionAsync(
            """
            selectors => {
                const [resultSelector, dockSelector] = selectors.split('|');
                const result = document.querySelector(resultSelector);
                const dock = document.querySelector(dockSelector);
                if (!result || !dock) return false;

                const resultRect = result.getBoundingClientRect();
                const dockRect = dock.getBoundingClientRect();
                return resultRect.top >= 0 && resultRect.top <= dockRect.top - 44;
            }
            """,
            $"{resultSelector}|{dockSelector}");
        await page.EvaluateAsync(
            "() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

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
        Assert.True(
            layout.ResultTop <= layout.DockTop - 44,
            $"{resultSelector} does not begin in the readable area above the result action dock. {layout}");

        var remainingScroll = Math.Max(0, layout.MaxScrollY - layout.ScrollY);
        Assert.True(
            layout.ResultBottom - remainingScroll <= layout.DockTop - 8,
            $"{resultSelector} cannot be scrolled fully clear of the result action dock. {layout}");
    }

    [Fact]
    public async Task MobileBioageStickyProgress_KeepsOnlyVisibleProgressSemantics()
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

        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
        await page.WaitForFunctionAsync(
            """
            () => {
                const main = document.getElementById('mainProgressBar');
                const sticky = document.getElementById('site-sticky-progress');
                const form = document.querySelector('.bioageform');
                const maxScroll = Math.max(0, document.documentElement.scrollHeight - window.innerHeight);
                return window.LwcStickyProgress
                    && form?.classList.contains('bioage-biomarker-entry-ready')
                    && main?.offsetHeight > 0
                    && sticky
                    && maxScroll > 0;
            }
            """);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.WaitForFunctionAsync(
            """
            () => !document.documentElement.classList.contains('sticky-progress-visible')
                && parseFloat(getComputedStyle(document.getElementById('mainProgressBar')).opacity) >= 0.99
            """);
        await page.Mouse.MoveAsync(195, 422);
        await page.Mouse.WheelAsync(0, 10_000);
        await page.WaitForFunctionAsync("() => window.scrollY > 0");
        await using var stickyStateHandle = await page.WaitForFunctionAsync(
            """
            () => {
                const main = document.getElementById('mainProgressBar');
                const sticky = document.getElementById('site-sticky-progress');
                const style = main && getComputedStyle(main);
                const ready = document.documentElement.classList.contains('sticky-progress-visible')
                    && style?.opacity === '0'
                    && style.pointerEvents === 'none'
                    && main.getAttribute('aria-hidden') === 'true'
                    && main.inert
                    && sticky?.getAttribute('aria-hidden') === 'false';
                if (!ready) return null;
                return {
                    Opacity: style.opacity,
                    PointerEvents: style.pointerEvents,
                    AriaHidden: main.getAttribute('aria-hidden'),
                    Inert: main.inert,
                    HasInertAttribute: main.hasAttribute('inert'),
                    StickyAriaHidden: sticky.getAttribute('aria-hidden'),
                    StickyRole: sticky.getAttribute('role'),
                    StickyAriaLive: sticky.getAttribute('aria-live')
                };
            }
            """);
        var progressState = await stickyStateHandle.JsonValueAsync<StickyProgressState>();

        Assert.Equal("0", progressState.Opacity);
        Assert.Equal("none", progressState.PointerEvents);
        Assert.Equal("true", progressState.AriaHidden);
        Assert.True(progressState.Inert);
        Assert.True(progressState.HasInertAttribute);
        Assert.Equal("false", progressState.StickyAriaHidden);
        Assert.Equal("status", progressState.StickyRole);
        Assert.Equal("polite", progressState.StickyAriaLive);

        await page.Mouse.WheelAsync(0, -10_000);
        await page.WaitForFunctionAsync("() => window.scrollY === 0");
        await using var restoredStateHandle = await page.WaitForFunctionAsync(
            """
            () => {
                const main = document.getElementById('mainProgressBar');
                const sticky = document.getElementById('site-sticky-progress');
                const style = main && getComputedStyle(main);
                const ready = !document.documentElement.classList.contains('sticky-progress-visible')
                    && parseFloat(style?.opacity || '0') >= 0.99
                    && style.pointerEvents !== 'none'
                    && main.getAttribute('aria-hidden') === 'false'
                    && !main.inert
                    && sticky?.getAttribute('aria-hidden') === 'true';
                if (!ready) return null;
                return {
                    Opacity: style.opacity,
                    PointerEvents: style.pointerEvents,
                    AriaHidden: main.getAttribute('aria-hidden'),
                    Inert: main.inert,
                    HasInertAttribute: main.hasAttribute('inert'),
                    StickyAriaHidden: sticky.getAttribute('aria-hidden'),
                    StickyRole: sticky.getAttribute('role'),
                    StickyAriaLive: sticky.getAttribute('aria-live')
                };
            }
            """);
        progressState = await restoredStateHandle.JsonValueAsync<StickyProgressState>();

        Assert.Equal("1", progressState.Opacity);
        Assert.NotEqual("none", progressState.PointerEvents);
        Assert.Equal("false", progressState.AriaHidden);
        Assert.False(progressState.Inert);
        Assert.False(progressState.HasInertAttribute);
        Assert.Equal("true", progressState.StickyAriaHidden);
        Assert.Empty(errors);
    }
}
