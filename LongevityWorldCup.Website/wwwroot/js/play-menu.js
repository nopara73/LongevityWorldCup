(function () {
    const storageErrorMessage = 'Browser storage is unavailable. Enable storage and try again.';
    const PLAY_START_INTRO_MS = 1900;
    let flow = null;
    let athleteSelection = null;
    let activePlayPanelName = null;
    let hasRenderedPlayPanel = false;
    let activePanelTransitionTimer = 0;
    let activePanelTransitionElement = null;
    let playPanelTransitionRunId = 0;
    let playPanelPreparationRunId = 0;
    let playStartIntroTimer = 0;
    let playStartIntroRunId = 0;
    const playRouteStates = Object.freeze({
        start: Object.freeze({ route: '/play', panelId: 'playStartPanel', isWide: false }),
        join: Object.freeze({ route: '/join', panelId: 'joinTrackPanel', isWide: true }),
        selection: Object.freeze({ route: '/select-athlete', panelId: 'athleteSelectionPanel', isWide: false }),
        dashboard: Object.freeze({ route: '/dashboard', panelId: 'athleteDashboardPanel', isWide: false })
    });

    function getFlowReady() {
        return Promise.resolve(window.modulesReady || undefined)
            .catch(() => {})
            .then(() => {
                flow = window.playAthleteFlow;
                if (!flow) throw new Error('Athlete flow module did not load.');
                return flow;
            });
    }

    function getPlayRouteState(panelName) {
        return playRouteStates[panelName] || playRouteStates.start;
    }

    function getPlayRoute(panelName) {
        return getPlayRouteState(panelName).route;
    }

    function getPlayPanelNameForPath(pathname = window.location.pathname) {
        const path = pathname.toLowerCase();
        if (path === '/join') return 'join';
        if (path === '/select-athlete') return 'selection';
        if (path === '/dashboard') return 'dashboard';
        return 'start';
    }

    function getCurrentPlayPanelName() {
        const statePanel = window.history && window.history.state && window.history.state.playPanel;
        return activePlayPanelName || (playRouteStates[statePanel] ? statePanel : getPlayPanelNameForPath());
    }

    function getAthleteSelectionPreviewReady() {
        return athleteSelection && typeof athleteSelection.getPreviewReady === 'function'
            ? athleteSelection.getPreviewReady()
            : Promise.resolve();
    }

    function showSelectionPanelAfterPreviewReady(panelName, preparationRunId) {
        getAthleteSelectionPreviewReady()
            .catch(() => {})
            .then(() => {
                if (preparationRunId === playPanelPreparationRunId
                    && getPlayPanelNameForPath() === panelName) {
                    showPanelByName(panelName);
                }
            });
    }

    function getPlayUrl(panelName, options = {}) {
        const route = getPlayRoute(panelName);
        return options.preserveCurrentUrlParts
            ? `${route}${window.location.search}${window.location.hash}`
            : route;
    }

    function createPlayHistoryState(panelName, previousPlayPanel = null) {
        return {
            playPanel: panelName,
            previousPlayPanel: playRouteStates[previousPlayPanel] ? previousPlayPanel : null
        };
    }

    function setPlayHistory(panelName, historyMode, options = {}) {
        if (!historyMode || !window.history) return;

        const route = getPlayUrl(panelName, options);
        const currentPath = window.location.pathname.toLowerCase();
        const currentPanelName = getCurrentPlayPanelName();
        const currentPreviousPanel = window.history.state && window.history.state.previousPlayPanel;
        const nextPreviousPanel = historyMode === 'push' && currentPanelName !== panelName
            ? currentPanelName
            : currentPreviousPanel;
        const state = createPlayHistoryState(panelName, nextPreviousPanel);
        if (historyMode === 'replace') {
            window.history.replaceState(state, '', route);
            return;
        }

        if (historyMode === 'push') {
            if (currentPath === getPlayRoute(panelName)) {
                window.history.replaceState(state, '', route);
            } else {
                window.history.pushState(state, '', route);
            }
        }
    }

    function prefersReducedPlayMotion() {
        return window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    }

    function finishPlayStartIntro() {
        playStartIntroRunId += 1;
        window.clearTimeout(playStartIntroTimer);
        playStartIntroTimer = 0;
        document.body.classList.remove('play-start-preintro', 'play-start-intro');
    }

    function getPlayStartWatermarkReady() {
        const watermark = document.querySelector('.play-logo-watermark');
        if (!watermark || watermark.complete) return Promise.resolve();
        if (typeof watermark.decode === 'function') {
            return watermark.decode().catch(() => {});
        }

        return new Promise(resolve => {
            watermark.addEventListener('load', resolve, { once: true });
            watermark.addEventListener('error', resolve, { once: true });
        });
    }

    function startPlayStartIntro(options = {}) {
        if (!document.body.classList.contains('play-start-active')) {
            finishPlayStartIntro();
            return;
        }

        window.clearTimeout(playStartIntroTimer);

        if (prefersReducedPlayMotion()) {
            finishPlayStartIntro();
            return;
        }

        const runId = playStartIntroRunId + 1;
        playStartIntroRunId = runId;
        document.body.classList.remove('play-start-intro');
        document.body.classList.add('play-start-preintro');

        if (options.restart) {
            void document.body.offsetWidth;
        }

        getPlayStartWatermarkReady().then(() => {
            if (runId !== playStartIntroRunId) return;

            window.requestAnimationFrame(() => {
                window.requestAnimationFrame(() => {
                    if (runId !== playStartIntroRunId
                        || !document.body.classList.contains('play-start-active')) {
                        return;
                    }

                    document.body.classList.remove('play-start-preintro');
                    document.body.classList.add('play-start-intro');
                    playStartIntroTimer = window.setTimeout(finishPlayStartIntro, PLAY_START_INTRO_MS);
                });
            });
        });
    }

    function completePlayPanelTransition(panelName, transitionRunId) {
        if (transitionRunId !== playPanelTransitionRunId) return;
        activePlayPanelName = panelName;
        hasRenderedPlayPanel = true;
        activePanelTransitionTimer = 0;
        activePanelTransitionElement = null;
        document.documentElement.classList.remove('play-panel-transitioning');
        document.documentElement.classList.add('play-route-ready');
        document.body.classList.remove('play-route-hydrating');
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function showPanelByName(panelName, options = {}) {
        const state = getPlayRouteState(panelName);
        const main = document.querySelector('.play-hub-main');
        const targetPanel = document.getElementById(state.panelId);
        if (!main || !targetPanel) return;

        const shouldAnimate = options.animate !== false
            && hasRenderedPlayPanel
            && activePlayPanelName !== panelName
            && !prefersReducedPlayMotion();

        const transitionRunId = playPanelTransitionRunId + 1;
        playPanelTransitionRunId = transitionRunId;
        window.clearTimeout(activePanelTransitionTimer);
        activePanelTransitionTimer = 0;
        activePanelTransitionElement?.classList.remove('play-hub-panel--entering');
        activePanelTransitionElement = null;
        document.documentElement.classList.remove('play-panel-transitioning');

        const commitPanel = () => {
            main.classList.toggle('play-hub-main--wide', state.isWide);
            document.body.classList.toggle('play-start-active', panelName === 'start');
            if (panelName !== 'start') finishPlayStartIntro();
            document.querySelectorAll('.play-hub-panel').forEach(panel => {
                panel.hidden = panel !== targetPanel;
            });
            activePlayPanelName = panelName;
            window.scrollTo(0, 0);
            window.LwcFlowActionDock?.refreshNow?.();
        };

        if (!shouldAnimate) {
            commitPanel();
            if (panelName === 'start') startPlayStartIntro({ restart: hasRenderedPlayPanel });
            completePlayPanelTransition(panelName, transitionRunId);
            return;
        }

        document.documentElement.classList.add('play-panel-transitioning');
        commitPanel();
        activePanelTransitionElement = targetPanel;
        targetPanel.classList.add('play-hub-panel--entering');
        activePanelTransitionTimer = window.setTimeout(() => {
            targetPanel.classList.remove('play-hub-panel--entering');
            if (panelName === 'start') startPlayStartIntro({ restart: true });
            completePlayPanelTransition(panelName, transitionRunId);
        }, 180);
    }

    function resolveRequestedPlayPanel(panelName) {
        if (panelName !== 'dashboard') {
            return { panelName, dashboardAthlete: null };
        }

        const dashboardAthlete = athleteSelection.getCurrentAthlete()
            || flow.getStoredSelectedAthlete();
        if (!dashboardAthlete || !dashboardAthlete.Name) {
            if (window.location.pathname.toLowerCase() === getPlayRoute('dashboard')) {
                window.location.replace(getPlayRoute('selection'));
                return { panelName: 'selection', dashboardAthlete: null, isRedirecting: true };
            }
            return { panelName: 'selection', dashboardAthlete: null };
        }

        athleteSelection.setCurrentAthlete(dashboardAthlete);
        return { panelName: 'dashboard', dashboardAthlete };
    }

    function showPlayPanel(requestedPanelName, options = {}) {
        const preparationRunId = playPanelPreparationRunId + 1;
        playPanelPreparationRunId = preparationRunId;
        const requested = playRouteStates[requestedPanelName] ? requestedPanelName : 'start';
        const resolved = resolveRequestedPlayPanel(requested);
        if (resolved.isRedirecting) return;
        const panelName = resolved.panelName;
        const historyMode = requested === panelName ? options.historyMode : (options.historyMode || 'replace');

        setPlayHistory(panelName, historyMode, options);

        if (panelName === 'join') {
            athleteSelection.closeAllLists();
            renderJoinPricing();
            showPanelByName(panelName);
            return;
        }

        if (panelName === 'selection') {
            const hydratedStoredAthlete = athleteSelection.hydrateStoredAthleteSelection();
            if (!hydratedStoredAthlete && athleteSelection.hasPendingSavedSelection()) {
                athleteSelection.loadAthletes({ savedSelectionTransition: false })
                    .catch(() => {})
                    .then(() => showSelectionPanelAfterPreviewReady(panelName, preparationRunId));
                return;
            }

            athleteSelection.loadAthletes().catch(() => {});
            if (hydratedStoredAthlete) {
                showSelectionPanelAfterPreviewReady(panelName, preparationRunId);
                return;
            }

            showPanelByName(panelName, { animate: false });
            return;
        }

        if (panelName === 'dashboard') {
            const athlete = resolved.dashboardAthlete;
            const pictureReady = flow.renderAthleteDashboardHeader(athlete, {
                titleElement: document.getElementById('athleteDashboardTitle'),
                frameElement: document.getElementById('athleteDashboardPicture')
            });
            const actionsReady = flow.renderDashboardActions(athlete, {
                dynamicActionsElement: document.getElementById('athleteDashboardDynamicActions'),
                discountElement: document.getElementById('athleteDashboardDiscounts')
            });
            Promise.allSettled([pictureReady, actionsReady]).then(() => {
                if (preparationRunId === playPanelPreparationRunId
                    && getPlayPanelNameForPath() === panelName) {
                    showPanelByName(panelName);
                }
            });
            return;
        }

        athleteSelection.closeAllLists();
        showPanelByName(panelName);
    }

    function showPanelForCurrentUrl() {
        showPlayPanel(getPlayPanelNameForPath(), { historyMode: null });
    }

    function initializePlayHistory() {
        if (!window.history) return;

        try {
            window.history.scrollRestoration = 'manual';
        } catch (_) {
        }

        const requestedPanelName = getPlayPanelNameForPath();
        const resolved = resolveRequestedPlayPanel(requestedPanelName);
        window.history.replaceState(
            createPlayHistoryState(resolved.panelName),
            '',
            getPlayUrl(resolved.panelName, { preserveCurrentUrlParts: true })
        );
    }

    function navigateToPreviousPlayPanel(fallbackPanelName) {
        const previousPanelName = window.history && window.history.state && window.history.state.previousPlayPanel;
        if (previousPanelName === fallbackPanelName && window.history.length > 1) {
            window.history.back();
            return;
        }

        showPlayPanel(fallbackPanelName, { historyMode: 'replace' });
    }

    function navigateToStartPanel() {
        navigateToPreviousPlayPanel('start');
    }

    function navigateToSelectionPanel() {
        navigateToPreviousPlayPanel('selection');
    }

    function getCheckoutQuerySuffix() {
        const params = new URLSearchParams();
        const freePassValue = window.getFreePassValue ? window.getFreePassValue() : null;
        const discountValue = window.getDiscountValue ? window.getDiscountValue() : null;
        if (freePassValue !== null) params.set('freepass', freePassValue);
        if (discountValue !== null) params.set('discount', discountValue);
        const query = params.toString();
        return query ? `?${query}` : '';
    }

    function startAmateurApplication(retryButton) {
        const stored = flow.setPendingPaymentOffer({
            source: 'join-game',
            offerType: 'amateur',
            currency: 'USD',
            amountUsd: 10
        }, retryButton);
        if (!stored) return;
        window.location.href = `/pheno-age${getCheckoutQuerySuffix()}`;
    }

    function startProApplication(retryButton) {
        const result = window.proDiscounts && typeof window.proDiscounts.buildDiscountBreakdown === 'function'
            ? window.proDiscounts.buildDiscountBreakdown(null, { isOnLeaderboard: false })
            : null;
        const amountUsd = result && Number.isFinite(result.finalPriceUsd) ? result.finalPriceUsd : 100;
        const paymentOffer = flow.preserveAppliedDiscountMetadata({
            source: 'join-game',
            offerType: 'pro',
            currency: 'USD',
            amountUsd
        }, result);
        if (!flow.setPendingPaymentOffer(paymentOffer, retryButton)) return;
        window.location.href = `/bortz-age${getCheckoutQuerySuffix()}`;
    }

    function renderJoinFreePassPricing() {
        if (!window.hasFreePass || !window.hasFreePass()) return false;

        const amateurPrice = document.querySelector('.join-amateur-entry-cell .pro-new-price');
        const note = document.getElementById('joinProDiscountNote');
        const breakdown = document.getElementById('joinProDiscountBreakdown');
        const proEntryPrice = document.getElementById('joinProEntryPrice');

        if (amateurPrice) amateurPrice.textContent = 'free';
        if (proEntryPrice) proEntryPrice.innerHTML = '<span class="pro-new-price">free</span>';
        if (breakdown) breakdown.textContent = '';
        if (breakdown) breakdown.classList.remove('pro-discount-breakdown--with-badges');
        if (note) note.style.display = 'none';
        return true;
    }

    function renderJoinPricing() {
        if (renderJoinFreePassPricing()) return;
        if (!window.proDiscounts || typeof window.proDiscounts.buildDiscountBreakdown !== 'function') return;

        const result = window.proDiscounts.buildDiscountBreakdown(null, { isOnLeaderboard: false });
        const note = document.getElementById('joinProDiscountNote');
        const breakdown = document.getElementById('joinProDiscountBreakdown');
        const proEntryPrice = document.getElementById('joinProEntryPrice');
        if (!note || !breakdown || !proEntryPrice) return;
        breakdown.classList.toggle(
            'pro-discount-breakdown--with-badges',
            Array.isArray(result.components) && result.components.some(component => component && component.isBadge)
        );

        proEntryPrice.innerHTML = typeof window.proDiscounts.createPriceHtml === 'function'
            ? window.proDiscounts.createPriceHtml(result)
            : flow.createPriceHtmlFallback(result);

        if (!Array.isArray(result.components) || result.components.length === 0) {
            breakdown.textContent = '';
            breakdown.classList.remove('pro-discount-breakdown--with-badges');
            note.style.display = 'none';
            return;
        }

        if (typeof window.proDiscounts.createBreakdownHtml === 'function') {
            breakdown.innerHTML = window.proDiscounts.createBreakdownHtml(result);
        } else {
            breakdown.textContent = window.proDiscounts.createBreakdownText(result);
        }
        note.style.display = '';
    }

    function promotePlayStartAction(button) {
        button.classList.remove('grey', 'flow-action--secondary');
        button.classList.add('green');
    }

    function demotePlayStartAction(button) {
        button.classList.remove('green');
        button.classList.add('grey', 'flow-action--secondary');
    }

    function initializePlayMenu() {
        getFlowReady().then(() => {
            if (window.captureFreePassFromUrl) {
                window.captureFreePassFromUrl();
            }
            if (window.captureDiscountFromUrl) {
                window.captureDiscountFromUrl();
            }

            athleteSelection = flow.createAthleteSelectionController({
                input: document.getElementById('playAthleteInput'),
                errorElement: document.getElementById('playAthleteError'),
                confirmButton: document.getElementById('playConfirmAthleteBtn'),
                titleElement: document.getElementById('athleteSelectionTitle'),
                frameElement: document.getElementById('athleteSelectionPicture'),
                autocompleteRootSelector: '.play-autocomplete-container'
            }).bind();

            initializePlayHistory();

            const hasApp = flow.hasSubmittedApplication();
            const container = document.querySelector('.play-menu-actions');
            const newBtn = document.getElementById('newGameBtn');
            const contBtn = document.getElementById('continueGameBtn');

            if (hasApp) {
                promotePlayStartAction(contBtn);
                demotePlayStartAction(newBtn);
                container.insertBefore(contBtn, container.firstChild);
            } else {
                promotePlayStartAction(newBtn);
                demotePlayStartAction(contBtn);
                container.insertBefore(newBtn, container.firstChild);
            }
            window.LwcFlowActionDock?.refreshNow?.();

            newBtn.addEventListener('click', () => showPlayPanel('join', { historyMode: 'push' }));
            contBtn.addEventListener('click', () => showPlayPanel('selection', { historyMode: 'push' }));
            document.getElementById('joinTrackBackBtn').addEventListener('click', navigateToStartPanel);
            document.getElementById('joinStartAmateurBtn').addEventListener('click', event => startAmateurApplication(event.currentTarget));
            document.getElementById('joinMobileStartAmateurBtn').addEventListener('click', event => startAmateurApplication(event.currentTarget));
            document.getElementById('joinGoProButton').addEventListener('click', event => startProApplication(event.currentTarget));
            document.getElementById('joinMobileGoProButton').addEventListener('click', event => startProApplication(event.currentTarget));
            document.getElementById('playSelectionBackBtn').addEventListener('click', navigateToStartPanel);
            document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);

            document.getElementById('playConfirmAthleteBtn').addEventListener('click', () => {
                const currentAthlete = athleteSelection.getCurrentAthlete();
                if (!flow.persistSelectedAthlete(currentAthlete)) {
                    customAlert(storageErrorMessage);
                    return;
                }
                showPlayPanel('dashboard', { historyMode: 'push' });
            });

            [
                newBtn,
                contBtn,
                document.getElementById('joinTrackBackBtn'),
                document.getElementById('joinStartAmateurBtn'),
                document.getElementById('joinMobileStartAmateurBtn'),
                document.getElementById('joinGoProButton'),
                document.getElementById('joinMobileGoProButton'),
                document.getElementById('playSelectionBackBtn'),
                document.getElementById('playDashboardBackBtn')
            ].forEach(button => {
                if (button) button.disabled = false;
            });
            window.LwcFlowActionDock?.refreshNow?.();

            window.addEventListener('popstate', showPanelForCurrentUrl);
            showPanelForCurrentUrl();
        }).catch(error => {
            console.error('Unable to initialize athlete flow:', error);
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializePlayMenu, { once: true });
    } else {
        initializePlayMenu();
    }
})();
