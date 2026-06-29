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

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("const hasApp = flow.hasSubmittedApplication();", html);
        Assert.Contains("function getBrowserStorageItem(storageName, key)", flow);
        Assert.Contains("return window[storageName].getItem(key);", flow);
        Assert.Contains("function hasSubmittedApplication()", flow);
        Assert.Contains("return getLocalItem(\"hasApplication\") === \"true\";", flow);
        Assert.Contains("} catch (_) {", flow);
        Assert.Contains("return null;", flow);
        Assert.DoesNotContain("return localStorage.getItem('hasApplication') === 'true';", html);
        Assert.DoesNotContain("const hasApp = localStorage.getItem('hasApplication') === 'true';", html);
    }

    [Fact]
    public async Task PlayMenu_AlreadyAthletePathStaysInlineAndPreservesUrls()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("id=\"continueGameBtn\" type=\"button\"", html);
        Assert.Contains("id=\"athleteSelectionPanel\"", html);
        Assert.Contains("id=\"athleteDashboardPanel\"", html);
        Assert.Contains("const playRouteStates = Object.freeze({", html);
        Assert.Contains("selection: Object.freeze({ route: '/select-athlete', panelId: 'athleteSelectionPanel', isWide: false })", html);
        Assert.Contains("dashboard: Object.freeze({ route: '/dashboard', panelId: 'athleteDashboardPanel', isWide: false })", html);
        Assert.Contains("function showPlayPanel(requestedPanelName, options = {})", html);
        Assert.Contains("function navigateToPreviousPlayPanel(fallbackPanelName)", html);
        Assert.Contains("window.history.pushState(state, '', route);", html);
        Assert.Contains("window.addEventListener('popstate', showPanelForCurrentUrl);", html);
        Assert.Contains("window.history.back();", html);
        Assert.Contains("contBtn.addEventListener('click', () => showPlayPanel('selection', { historyMode: 'push' }));", html);
        Assert.Contains("showPlayPanel('dashboard', { historyMode: 'push' });", html);
        Assert.Contains("function navigateToStartPanel()", html);
        Assert.Contains("function navigateToSelectionPanel()", html);
        Assert.Contains("navigateToPreviousPlayPanel('start');", html);
        Assert.Contains("navigateToPreviousPlayPanel('selection');", html);
        Assert.Contains("document.getElementById('playSelectionBackBtn').addEventListener('click', navigateToStartPanel);", html);
        Assert.Contains("document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);", html);
        Assert.DoesNotContain("addEventListener('click', returnToStartPanel)", html);
        Assert.DoesNotContain("addEventListener('click', returnToSelectionPanel)", html);
        Assert.DoesNotContain("onclick=\"window.location.href='/select-athlete'\"", html);
        Assert.DoesNotContain("window.location.href = '/dashboard';", html);
        Assert.DoesNotContain("function scrollPanelIntoView", html);
        Assert.DoesNotContain("requestAnimationFrame(() => scrollPanelIntoView", html);
        Assert.DoesNotContain("scrollIntoView({ behavior: 'smooth', block: 'start' });", html);
    }

    [Fact]
    public async Task PlayMenu_NewAthletePathStaysInlineAndPreservesJoinUrl()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("id=\"joinTrackPanel\"", html);
        Assert.Contains("join: Object.freeze({ route: '/join', panelId: 'joinTrackPanel', isWide: true })", html);
        Assert.Contains("if (path === '/join') return 'join';", html);
        Assert.Contains("newBtn.addEventListener('click', () => showPlayPanel('join', { historyMode: 'push' }));", html);
        Assert.Contains("document.getElementById('joinTrackBackBtn').addEventListener('click', navigateToStartPanel);", html);
        Assert.DoesNotContain("onclick=\"window.location.href='/join'\"", html);
        Assert.Contains("flow.setPendingPaymentOffer({", html);
        Assert.Contains("source: 'join-game'", html);
        Assert.Contains("window.location.href = `/pheno-age${getCheckoutQuerySuffix()}`;", html);
        Assert.Contains("window.location.href = `/bortz-age${getCheckoutQuerySuffix()}`;", html);
        Assert.Contains("createPriceHtmlFallback", flow);
    }

    [Fact]
    public async Task PlayMenu_InlineDashboardKeepsRealTasksAsNavigations()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("window.location.href='/edit-profile'", html);
        Assert.Contains("flow.persistSelectedAthlete(currentAthlete)", html);
        Assert.Contains("customAlert(storageErrorMessage);", html);
        Assert.Contains("setSessionItem(\"selectedAthlete\", JSON.stringify(athlete))", flow);
        Assert.Contains("removeSessionItem(\"biomarkerData\");", flow);
        Assert.Contains("removeLocalItem(\"contactEmail\");", flow);
        Assert.Contains("\"/pheno-age?update=1\"", flow);
        Assert.Contains("\"/bortz-age?update=1\"", flow);
    }

    [Fact]
    public async Task PlayMenu_ContextualPrimaryActionDoesNotKeepSecondaryFlowStyling()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("function promotePlayStartAction(button)", html);
        Assert.Contains("button.classList.remove('grey', 'flow-action--secondary');", html);
        Assert.Contains("button.classList.add('green');", html);
        Assert.Contains("function demotePlayStartAction(button)", html);
        Assert.Contains("button.classList.remove('green');", html);
        Assert.Contains("button.classList.add('grey', 'flow-action--secondary');", html);
        Assert.Contains("promotePlayStartAction(contBtn);", html);
        Assert.Contains("demotePlayStartAction(newBtn);", html);
        Assert.Contains("promotePlayStartAction(newBtn);", html);
        Assert.Contains("demotePlayStartAction(contBtn);", html);
    }

    [Fact]
    public async Task PlayMenu_UsesOneReducedMotionSafePanelTransition()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("const PLAY_PANEL_TRANSITION_MS = 120;", html);
        Assert.Contains(".play-hub-panel--entering", html);
        Assert.Contains(".play-hub-panel--entering.play-hub-panel--from", html);
        Assert.Contains("@media (prefers-reduced-motion: reduce)", html);
        Assert.Contains("let hasRenderedPlayPanel = false;", html);
        Assert.Contains("function prefersReducedPlayMotion()", html);
        Assert.Contains("completePlayPanelTransition(targetPanel, panelName);", html);
        Assert.Contains("&& !prefersReducedPlayMotion();", html);
        Assert.Contains("targetPanel.classList.add('play-hub-panel--entering', 'play-hub-panel--from');", html);
        Assert.DoesNotContain("function measurePlayPanelHeight(panel)", html);
        Assert.DoesNotContain(".play-hub-main--transitioning", html);
        Assert.DoesNotContain(".play-hub-panel--animating", html);
        Assert.DoesNotContain(".play-hub-panel--leaving", html);
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

        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("id=\"athleteSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"athleteDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("width: min(100%, 408px);", css);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("border: 4px solid var(--dark-text-color);", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.Contains("object-fit: cover;", css);
        Assert.Contains("transform: scale(1.035);", css);
        Assert.Contains("const ATHLETE_PICTURE_TRANSITION_MS = 180;", flow);
        Assert.Contains("function transitionAthletePicture(frame, image, src)", flow);
        Assert.Contains("image.addEventListener(\"load\", finishImageSwap, { once: true });", flow);
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
        var selectionBranch = html.IndexOf("if (panelName === 'selection')", StringComparison.Ordinal);
        var hydrate = html.IndexOf("athleteSelection.hydrateStoredAthleteSelection();", selectionBranch, StringComparison.Ordinal);
        var showPanel = html.IndexOf("showPanelByName(panelName);", hydrate, StringComparison.Ordinal);
        var load = html.IndexOf("athleteSelection.loadAthletes().catch(() => {});", showPanel, StringComparison.Ordinal);

        Assert.True(selectionBranch >= 0);
        Assert.True(hydrate > selectionBranch);
        Assert.True(showPanel > hydrate);
        Assert.True(load > showPanel);
        Assert.Contains("flow.createAthleteSelectionController({", html);
        Assert.Contains("const dashboardAthlete = athleteSelection.getCurrentAthlete()", html);
        Assert.Contains("|| flow.getStoredSelectedAthlete();", html);
    }

    [Fact]
    public async Task PlayMenu_DashboardRendersStoredAthleteBeforePanelShows()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var dashboardBranch = html.IndexOf("if (panelName === 'dashboard')", StringComparison.Ordinal);
        var renderHeader = html.IndexOf("flow.renderAthleteDashboardHeader(athlete, {", dashboardBranch, StringComparison.Ordinal);
        var renderActions = html.IndexOf("flow.renderDashboardActions(athlete, {", renderHeader, StringComparison.Ordinal);
        var showPanel = html.IndexOf("showPanelByName(panelName);", renderActions, StringComparison.Ordinal);

        Assert.True(dashboardBranch >= 0);
        Assert.True(renderHeader > dashboardBranch);
        Assert.True(renderActions > renderHeader);
        Assert.True(showPanel > renderActions);
    }

    [Fact]
    public async Task PlayMenu_LoadsInlineDashboardHelpersThroughInjectedHeadAssets()
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
    }

    [Fact]
    public async Task PlayMenu_DiscountBadgeSlotFitsMobileTapTarget()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains(".pro-discount-badge-slot {\n            width: 44px;", html);
        Assert.Contains("min-width: 44px;", html);
        Assert.Contains("height: 44px;", html);
        Assert.Contains(".pro-discount-badge-slot:empty", html);
        Assert.Contains(".pro-discount-text", html);
        Assert.Contains("overflow-wrap: anywhere;", html);
    }
}
