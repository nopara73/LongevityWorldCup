using static LongevityWorldCup.Tests.FrontendSourceTestHelper;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PlayMenuPageTests
{
    [Fact]
    public async Task PlayMenu_HandlesUnavailableApplicationStorageThroughSharedFlow()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var flowSource = ReadFrontendSource("play-athlete-flow.ts");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/js/play-menu.js", html);
        Assert.Contains("const hasApp = flow.hasSubmittedApplication();", playMenu);
        Assert.Contains("function initializePlayMenu()", playMenu);
        Assert.Contains("if (document.readyState === 'loading')", playMenu);
        Assert.Contains("document.addEventListener('DOMContentLoaded', initializePlayMenu, { once: true });", playMenu);
        Assert.Contains("initializePlayMenu();", playMenu);
        Assert.Contains("function getBrowserStorageItem(storageName, key)", flow);
        Assert.Contains("return window[storageName].getItem(key);", flow);
        Assert.Contains("function hasSubmittedApplication()", flow);
        Assert.Contains("return getLocalItem(\"hasApplication\") === \"true\";", flow);
        Assert.Contains("} catch (_) {", flowSource);
        Assert.Contains("return null;", flow);
        Assert.DoesNotContain("return localStorage.getItem('hasApplication') === 'true';", playMenu);
        Assert.DoesNotContain("const hasApp = localStorage.getItem('hasApplication') === 'true';", playMenu);
    }

    [Fact]
    public async Task PlayMenu_AlreadyAthletePathStaysInPlayHubAndPreservesUrls()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        var playMenuSource = ReadFrontendSource("play-menu.ts");

        Assert.Contains("id=\"continueGameBtn\" type=\"button\"", html);
        Assert.Contains("id=\"athleteSelectionPanel\"", html);
        Assert.Contains("id=\"athleteDashboardPanel\"", html);
        Assert.Contains("const playRouteStates = Object.freeze({", playMenu);
        Assert.Contains("selection: Object.freeze({ route: '/select-athlete', panelId: 'athleteSelectionPanel', isWide: false })", playMenu);
        Assert.Contains("dashboard: Object.freeze({ route: '/dashboard', panelId: 'athleteDashboardPanel', isWide: false })", playMenu);
        Assert.Contains("function showPlayPanel(requestedPanelName, options = {})", playMenu);
        Assert.Contains("function navigateToPreviousPlayPanel(fallbackPanelName)", playMenu);
        Assert.Contains("window.history.pushState(state, '', route);", playMenu);
        Assert.Contains("window.addEventListener('popstate', showPanelForCurrentUrl);", playMenu);
        Assert.Contains("window.history.back();", playMenu);
        Assert.Contains("contBtn.addEventListener('click', () => showPlayPanel('selection', { historyMode: 'push' }));", playMenu);
        Assert.Contains("showPlayPanel('dashboard', { historyMode: 'push' });", playMenu);
        Assert.Contains("function navigateToStartPanel()", playMenu);
        Assert.Contains("function navigateToSelectionPanel()", playMenu);
        Assert.Contains("navigateToPreviousPlayPanel('start');", playMenu);
        Assert.Contains("navigateToPreviousPlayPanel('selection');", playMenu);
        Assert.Contains("const selectionBackButton = document.getElementById('playSelectionBackBtn');", playMenuSource);
        Assert.Contains("const dashboardBackButton = document.getElementById('playDashboardBackBtn');", playMenuSource);
        Assert.Contains("selectionBackButton.addEventListener('click', navigateToStartPanel);", playMenuSource);
        Assert.Contains("dashboardBackButton.addEventListener('click', navigateToSelectionPanel);", playMenuSource);
        Assert.DoesNotContain("addEventListener('click', returnToStartPanel)", playMenu);
        Assert.DoesNotContain("addEventListener('click', returnToSelectionPanel)", playMenu);
        Assert.DoesNotContain("onclick=\"window.location.href='/select-athlete'\"", html);
        Assert.DoesNotContain("window.location.href = '/dashboard';", playMenu);
        Assert.DoesNotContain("function scrollPanelIntoView", playMenu);
        Assert.DoesNotContain("requestAnimationFrame(() => scrollPanelIntoView", playMenu);
        Assert.DoesNotContain("scrollIntoView({ behavior: 'smooth', block: 'start' });", playMenu);
    }

    [Fact]
    public async Task PlayMenu_NewAthletePathStaysInPlayHubAndPreservesJoinUrl()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        var playMenuSource = ReadFrontendSource("play-menu.ts");

        Assert.Contains("id=\"joinTrackPanel\"", html);
        Assert.Contains("class=\"options-container play-join-actions flow-action-stack\" data-flow-dock=\"auto\"", html);
        Assert.Contains("id=\"joinMobileStartAmateurBtn\"", html);
        Assert.Contains("id=\"joinMobileGoProButton\"", html);
        Assert.Contains("class=\"play-join-challenge-note\"", html);
        Assert.Contains("id=\"joinStartChallengeLink\" href=\"/longevitymaxxing\"", html);
        Assert.Contains("Not ready for a blood test yet?", html);
        Assert.Contains("Try our longevitymaxxing lifestyle challenge instead", html);
        Assert.Contains("<h1 class=\"play-join-title\">Choose your track</h1>", html);
        Assert.Contains("To calculate your biological age, get a <strong>blood test</strong>.", html);
        Assert.DoesNotContain("play-join-card--challenge", html);
        Assert.DoesNotContain("play-join-secondary-actions", html);
        Assert.DoesNotContain("<span class=\"track-name\">Longevitymaxxing</span>", html);
        Assert.DoesNotContain("id=\"joinStartChallengeButton\"", html);
        Assert.DoesNotContain("Start challenge</span>", html);
        Assert.Contains("join: Object.freeze({ route: '/join', panelId: 'joinTrackPanel', isWide: true })", playMenu);
        Assert.Contains("if (path === '/join') return 'join';", playMenuSource);
        Assert.Contains("newBtn.addEventListener('click', () => showPlayPanel('join', { historyMode: 'push' }));", playMenu);
        Assert.Contains("const joinTrackBackButton = document.getElementById('joinTrackBackBtn');", playMenuSource);
        Assert.Contains("joinTrackBackButton.addEventListener('click', navigateToStartPanel);", playMenuSource);
        Assert.Contains("joinMobileStartAmateurButton.addEventListener('click', () => startAmateurApplication(joinMobileStartAmateurButton));", playMenuSource);
        Assert.Contains("joinMobileGoProButton.addEventListener('click', () => startProApplication(joinMobileGoProButton));", playMenuSource);
        Assert.DoesNotContain("onclick=\"window.location.href='/join'\"", html);
        Assert.Contains("flow.setPendingPaymentOffer({", playMenu);
        Assert.Contains("source: 'join-game'", playMenu);
        Assert.Contains("window.location.href = `/pheno-age${getCheckoutQuerySuffix()}`;", playMenu);
        Assert.Contains("window.location.href = `/bortz-age${getCheckoutQuerySuffix()}`;", playMenu);
        Assert.Contains("createPriceHtmlFallback", flow);
    }

    [Fact]
    public async Task PlayMenu_DashboardKeepsRealTasksAsNavigations()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("window.location.href='/edit-profile'", html);
        Assert.Contains("flow.persistSelectedAthlete(currentAthlete)", playMenu);
        Assert.Contains("customAlert(storageErrorMessage);", playMenu);
        Assert.Contains("setSessionItem(\"selectedAthlete\", JSON.stringify(athlete))", flow);
        Assert.Contains("removeSessionItem(\"biomarkerData\");", flow);
        Assert.Contains("removeLocalItem(\"contactEmail\");", flow);
        Assert.Contains("\"/pheno-age?update=1\"", flow);
        Assert.Contains("\"/bortz-age?update=1\"", flow);
        Assert.Contains("\"/longevitymaxxing\"", flow);
        Assert.Contains("dynamicActions.append(challengeButton, submitButton, goProButton);", flow);
        Assert.Contains("dynamicActions.append(challengeButton, phenoButton, bortzButton);", flow);
        Assert.Contains("Longevitymaxxing</span>", flow);
        Assert.DoesNotContain("Longevitymaxxing Challenge</span>", flow);
    }

    [Fact]
    public async Task PlayMenu_ContextualPrimaryActionDoesNotKeepSecondaryFlowStyling()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("body.play-flow-route .option-button.green", html);
        Assert.Contains("--play-flow-action-color: #1f7a38;", html);
        Assert.Contains("--play-flow-action-hover-color: #17612d;", html);
        Assert.Contains("function promotePlayStartAction(button)", playMenu);
        Assert.Contains("button.classList.remove('grey', 'flow-action--secondary');", playMenu);
        Assert.Contains("button.classList.add('green');", playMenu);
        Assert.Contains("function demotePlayStartAction(button)", playMenu);
        Assert.Contains("button.classList.remove('green');", playMenu);
        Assert.Contains("button.classList.add('grey', 'flow-action--secondary');", playMenu);
        Assert.Contains("promotePlayStartAction(contBtn);", playMenu);
        Assert.Contains("demotePlayStartAction(newBtn);", playMenu);
        Assert.Contains("promotePlayStartAction(newBtn);", playMenu);
        Assert.Contains("demotePlayStartAction(contBtn);", playMenu);
    }

    [Fact]
    public async Task PlayMenu_UsesOneContainedReducedMotionSafePanelTransition()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var css = await client.GetStringAsync("/css/play-menu.css");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.DoesNotContain("document.startViewTransition", playMenu);
        Assert.Contains("html.play-panel-transitioning .play-hub-panel--entering", css);
        Assert.Contains("@keyframes play-panel-enter", css);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", css);
        Assert.Contains("let hasRenderedPlayPanel = false;", playMenu);
        Assert.Contains("function prefersReducedPlayMotion()", playMenu);
        Assert.Contains("completePlayPanelTransition(panelName, transitionRunId);", playMenu);
        Assert.Contains("&& !prefersReducedPlayMotion()", playMenu);
        Assert.Contains("activePanelTransitionElement?.classList.remove('play-hub-panel--entering');", playMenu);
        Assert.DoesNotContain("function measurePlayPanelHeight(panel)", playMenu);
        Assert.DoesNotContain(".play-hub-main--transitioning", css);
        Assert.DoesNotContain("::view-transition", css);
        Assert.DoesNotContain(".play-hub-panel--leaving", css);
        Assert.DoesNotContain("play-menu-hero\" data-aos", html);
        Assert.DoesNotContain("play-menu-actions flow-action-stack\" data-aos", html);
    }

    [Fact]
    public async Task PlayMenu_AthletePicturesUseSharedFrameAndTransitionBehavior()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var css = await client.GetStringAsync("/css/play-athlete-flow.css");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var flowSource = ReadFrontendSource("play-athlete-flow.ts");

        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("id=\"athleteSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"athleteDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("width: min(100%, 408px);", css);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("border: 4px solid var(--dark-text-color);", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.DoesNotContain("object-fit: cover;", css);
        Assert.DoesNotContain("transform: scale(1.42);", css);
        Assert.Contains("const ATHLETE_PICTURE_TRANSITION_MS = 180;", flow);
        Assert.Contains("const MIN_USABLE_ATHLETE_PICTURE_SIDE = 16;", flow);
        Assert.Contains("function shouldUseDefaultForLoadedAthleteImage(image)", flow);
        Assert.Contains("function setDefaultAthleteImageSource(image)", flow);
        Assert.Contains("function watchAthleteImageLoad(image, onLoaded)", flow);
        Assert.Contains("function waitForAthletePictureFrameReady(frame)", flow);
        Assert.Contains("const pictureReadyPromises = new WeakMap();", flow);
        Assert.Contains("function transitionAthletePicture(frame, image, src)", flow);
        Assert.Contains("image.addEventListener(\"load\", handleImageLoad);", flow);
        Assert.Contains("image.addEventListener(\"error\", handleImageError);", flow);
        Assert.Contains("if (!fallbackRequested", flow);
        Assert.Contains("&& shouldUseDefaultForLoadedAthleteImage(image)", flow);
        Assert.Contains("scheduleCompletedImageInspection();", flow);
        Assert.Contains("if (!hasCompleted && image.complete)", flow);
        Assert.Contains("image.decode().catch(() => {}).then(inspectCompletedImage);", flowSource);
        Assert.Contains("const inspectLoadedImage = watchAthleteImageLoad(image, finishImageSwap);", flow);
        Assert.Contains("frame.appendChild(image);", flow);
        Assert.Contains("currentMedia.classList.add(\"is-exiting\");", flow);
        Assert.Contains("frame.replaceChildren(image);", flow);
        Assert.DoesNotContain("function transitionAthleteSelectionImage", html);
    }

    [Fact]
    public async Task PlayMenu_AthleteSelectionHydratesStoredAthleteBeforePanelShows()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var playMenuSource = ReadFrontendSource("play-menu.ts");
        var flowSource = ReadFrontendSource("play-athlete-flow.ts");
        var scriptSelectionBranch = playMenuSource.IndexOf("if (panelName === 'selection')", StringComparison.Ordinal);
        var hydrate = playMenuSource.IndexOf("const hydratedStoredAthlete = athleteSelection.hydrateStoredAthleteSelection();", scriptSelectionBranch, StringComparison.Ordinal);
        var pendingSavedSelection = playMenuSource.IndexOf("if (!hydratedStoredAthlete && athleteSelection.hasPendingSavedSelection())", hydrate, StringComparison.Ordinal);
        var preloadSavedSelection = playMenuSource.IndexOf("athleteSelection.loadAthletes({ savedSelectionTransition: false })", pendingSavedSelection, StringComparison.Ordinal);
        var delayedShowPanel = playMenuSource.IndexOf(".then(() => showSelectionPanelAfterPreviewReady(panelName, preparationRunId));", preloadSavedSelection, StringComparison.Ordinal);
        var hydratedLoad = playMenuSource.IndexOf("athleteSelection.loadAthletes().catch(() => {});", delayedShowPanel, StringComparison.Ordinal);
        var hydratedReadyGuard = playMenuSource.IndexOf("if (hydratedStoredAthlete)", hydratedLoad, StringComparison.Ordinal);
        var hydratedReadyShow = playMenuSource.IndexOf("showSelectionPanelAfterPreviewReady(panelName, preparationRunId);", hydratedReadyGuard, StringComparison.Ordinal);
        var fallbackShow = playMenuSource.IndexOf("showPanelByName(panelName, { animate: false });", hydratedReadyShow, StringComparison.Ordinal);

        Assert.True(scriptSelectionBranch >= 0);
        Assert.True(hydrate > scriptSelectionBranch);
        Assert.True(pendingSavedSelection > hydrate);
        Assert.True(preloadSavedSelection > pendingSavedSelection);
        Assert.True(delayedShowPanel > preloadSavedSelection);
        Assert.True(hydratedLoad > delayedShowPanel);
        Assert.True(hydratedReadyGuard > hydratedLoad);
        Assert.True(hydratedReadyShow > hydratedReadyGuard);
        Assert.True(fallbackShow > hydratedReadyShow);
        Assert.Contains("id=\"athleteSelectionPanel\"", html);
        Assert.Contains("flow.createAthleteSelectionController({", playMenuSource);
        Assert.Contains("function showSelectionPanelAfterPreviewReady(", playMenuSource);
        Assert.Contains("getAthleteSelectionPreviewReady()", playMenuSource);
        Assert.Contains("function beginPlayPanelPreparation()", playMenuSource);
        Assert.Contains("const PLAY_PANEL_PREPARATION_TIMEOUT_MS = 8000;", playMenuSource);
        Assert.Contains("function waitForPlayPanelPreparation(", playMenuSource);
        Assert.Contains("Promise.race([Promise.resolve(promise).catch(() => {}), deadline])", playMenuSource);
        Assert.Contains("document.body.classList.add('play-route-hydrating');", playMenuSource);
        Assert.Contains("document.body.setAttribute('aria-busy', 'true');", playMenuSource);
        Assert.Contains("document.body.removeAttribute('aria-busy');", playMenuSource);
        Assert.Contains("const dashboardAthlete = athleteSelection.getCurrentAthlete()", playMenuSource);
        Assert.Contains("|| flow.getStoredSelectedAthlete();", playMenuSource);
        Assert.Contains("hasPendingSavedSelection: () => Boolean(getSavedSelectedAthleteName()) && !currentAthlete", flowSource);
    }

    [Fact]
    public async Task PlayMenu_DashboardRendersStoredAthleteBeforePanelShows()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        var dashboardBranch = playMenu.IndexOf("if (panelName === 'dashboard')", StringComparison.Ordinal);
        var renderHeader = playMenu.IndexOf("flow.renderAthleteDashboardHeader(athlete, {", dashboardBranch, StringComparison.Ordinal);
        var renderActions = playMenu.IndexOf("flow.renderDashboardActions(athlete, {", renderHeader, StringComparison.Ordinal);
        var showPanel = playMenu.IndexOf("showPanelByName(panelName);", renderActions, StringComparison.Ordinal);

        Assert.True(dashboardBranch >= 0);
        Assert.True(renderHeader > dashboardBranch);
        Assert.True(renderActions > renderHeader);
        Assert.True(showPanel > renderActions);
    }

    [Fact]
    public async Task PlayMenu_LoadsDashboardHelpersThroughInjectedHeadAssets()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/js/misc.js", html);
        Assert.Contains("/js/pheno-age.js", html);
        Assert.Contains("/js/bortz-age.js", html);
        Assert.Contains("/js/badges.js", html);
        Assert.Contains("/js/proof-helpers.js", html);
        Assert.Contains("/js/pro-discounts.js", html);
        Assert.Contains("/js/play-menu.js", html);
    }

    [Fact]
    public async Task PlayMenu_DiscountBadgeSlotFitsMobileTapTarget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var css = await client.GetStringAsync("/css/play-menu.css");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains(".pro-discount-badge-slot {\n    width: 44px;", css);
        Assert.Contains("min-width: 44px;", css);
        Assert.Contains("height: 44px;", css);
        Assert.Contains(".pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-breakdown.pro-discount-breakdown--with-badges .pro-discount-badge-slot:empty", css);
        Assert.Contains(".pro-discount-text", css);
        Assert.Contains("overflow-wrap: anywhere;", css);
        Assert.Contains("pro-discount-breakdown--with-badges", flow);
    }

}
