using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class FlowActionDockFormBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
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
            await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

                var scenario = $"{viewportWidth}x{viewportHeight}";

                await page.WaitForFunctionAsync(
                    """
                    () => window.LwcFlowActionDock
                        && document.body
                        && document.getElementById('submitButton')
                        && document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'
                    """);

                await page.EvaluateAsync(
            """
            () => {
                document.body.classList.remove('proof-upload-has-proofs');
                document.getElementById('submitButton').disabled = true;
                window.LwcFlowActionDock?.refreshNow();
            }
            """);
                await page.EvaluateAsync("() => window.LwcFlowActionDock.refreshNow()");
                await WaitForManagedActionStacksSettledAsync(page);

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
        await page.WaitForFunctionAsync(
            """
            () => {
                const button = document.getElementById('submitButton');
                const rect = button?.getBoundingClientRect();
                const style = button ? getComputedStyle(button) : null;
                return button && !button.disabled && rect.width > 0 && rect.height > 0
                    && style.display !== 'none' && style.visibility !== 'hidden';
            }
            """);

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

        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
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
        await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

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
                        ViewportWidth: window.innerWidth,
                        ViewportHeight: window.innerHeight,
                        ScreenWidth: window.screen.width,
                        ScreenHeight: window.screen.height,
                        LandscapeMediaMatches: matchMedia('(orientation: landscape)').matches,
                        CompactLandscapeMediaMatches: matchMedia('(min-width: 640px) and (max-width: 932px) and (max-height: 480px) and (orientation: landscape)').matches
                    };
                }
                """);

            var scenario = $"{viewportWidth}x{viewportHeight}";
            Assert.True(layout.TitleFontSize <= maxTitleFontSize,
                $"{scenario}: proof upload title fell back to oversized global h1 sizing: {layout.TitleFontSize}px > {maxTitleFontSize}px. {layout}");
            Assert.InRange(layout.Illustration.Height, 80, 104);
            Assert.Equal(0, await page.Locator(".proof-upload-visual img").CountAsync());
            Assert.True(layout.UploadButton.Bottom <= layout.ViewportHeight - 8,
                $"{scenario}: upload proofs action is cut off in the first viewport: bottom {layout.UploadButton.Bottom}px, viewport {layout.ViewportHeight}px.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task DesktopPlayEntry_KeepsActionsInlineAtCommonViewport()
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

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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

    [Fact]
    public async Task PlayHub_DoesNotExposeGlobalFooterBetweenHeroAndDockedActions()
    {
        var app = App;
        var browser = Browser;
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
        var app = App;
        var browser = Browser;
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
    [InlineData(false)]
    [InlineData(true)]
    public async Task DesktopPlayEntry_InlineActionsStaySameHeightAtDefaultBrowserSize(bool hasApplication)
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
        await context.AddInitScriptAsync(
            hasApplication
                ? "window.localStorage.setItem('hasApplication', 'true');"
                : "window.localStorage.removeItem('hasApplication');");
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        await page.GotoAsync("/play", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
            await page.WaitForFunctionAsync("() => window.LwcFlowActionDock");
            await page.WaitForFunctionAsync(
                "selector => !document.querySelector(selector)?.classList.contains('flow-action-stack--docked')",
                ".play-menu-actions");
            await ExpectActionStackInViewportAsync(page, ".play-menu-actions");

            var layout = await ReadFlowActionChildLayoutAsync(page, ".play-menu-actions");
            Assert.Equal(2, layout.Count);
            Assert.True(layout.MaxHeightDelta <= 1,
                $"hasApplication={hasApplication}: inline /play actions have mismatched heights by {layout.MaxHeightDelta}px.");
            Assert.True(layout.MaxHeight is >= 56 and <= 62,
                $"hasApplication={hasApplication}: inline /play actions have unexpected height: {layout.MaxHeight}px.");
        Assert.True(errors.Count == 0, $"hasApplication={hasApplication}: {string.Join(" | ", errors)}");
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
        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

            var scenario = $"{viewportWidth}x{viewportHeight}";

        await page.WaitForFunctionAsync(
            """
            () => window.LwcFlowActionDock
                && document.querySelector('#divisionDisplaySelect')?.value === "Men's"
                && document.querySelector('.edit-profile-actions')
            """);
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
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData(768, 1024)]
    [InlineData(1366, 768)]
    public async Task EditProfile_UnchangedProfile_DoesNotLetFieldsEnterActionBand(
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
        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

        var scenario = $"{viewportWidth}x{viewportHeight}";

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

            Assert.True(coveredRows.Length == 0,
                $"{scenario}: fields entered the action band: {string.Join(", ", coveredRows)}.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1366, 768)]
    public async Task EditProfile_ChangedAthleteDocksSubmitActionsAfterEditing(
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
        await context.AddInitScriptAsync(FlowAuditStateScript);
        var page = await context.NewPageAsync();
        var errors = CapturePageErrors(page);

        var scenario = $"{viewportWidth}x{viewportHeight}";
        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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
                return personalLinkRect.bottom <= actionRect.top
                    && mediaContactRect.bottom <= actionRect.top
                    && whyRect.bottom <= actionRect.top;
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
        Assert.True(state.PersonalLinkBottom <= state.DockTop,
            $"Edited personal-link row is covered by the dock: row bottom {state.PersonalLinkBottom}, dock top {state.DockTop}.");
        Assert.True(state.MediaContactBottom <= state.DockTop,
            $"Next media-contact row is covered by the dock: row bottom {state.MediaContactBottom}, dock top {state.DockTop}.");
        Assert.True(state.WhyBottom <= state.DockTop,
            $"Next why row is covered by the dock: row bottom {state.WhyBottom}, dock top {state.DockTop}.");
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    [Fact]
    public async Task EditProfile_RestoringMissingOriginalProfileFieldsLeavesInputsEmpty()
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

        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });
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

        var scenario = $"{viewportWidth}x{viewportHeight}";
        await page.GotoAsync("/edit-profile", new PageGotoOptions { WaitUntil = WaitUntilState.Commit });

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
            $"{scenario}: edit profile title fell back to oversized global h1 sizing: {layout.TitleFontSize}px > {maxTitleFontSize}px.");
        Assert.True(layout.Picture.Height >= minPictureHeight,
            $"{scenario}: edit profile picture was over-compressed: {layout.Picture.Height}px < {minPictureHeight}px.");
        Assert.True(layout.PictureButton.Bottom <= layout.ViewportHeight - 8,
            $"Change profile picture action is cut off in the first viewport: bottom {layout.PictureButton.Bottom}px, viewport {layout.ViewportHeight}px.");
        Assert.Equal("fade", layout.OptionsAos);
        Assert.True(errors.Count == 0, $"{scenario}: {string.Join(" | ", errors)}");
    }

    private sealed class FooterVisibility
    {
        public string Display { get; set; } = "";
        public int VisibleLinkCount { get; set; }
    }

}
