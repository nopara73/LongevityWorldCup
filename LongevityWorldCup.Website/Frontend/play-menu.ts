(function () {
    type PlayPanelName = 'start' | 'join' | 'selection' | 'dashboard';
    type PlayHistoryMode = 'push' | 'replace' | null;

    interface PlayRouteState {
        route: '/play' | '/join' | '/select-athlete' | '/dashboard';
        panelId: string;
        isWide: boolean;
    }

    interface PlayUrlOptions {
        preserveCurrentUrlParts?: boolean;
    }

    interface PlayPanelOptions extends PlayUrlOptions {
        animate?: boolean;
        historyMode?: PlayHistoryMode;
    }

    interface PlayIntroOptions {
        restart?: boolean;
    }

    interface ResolvedPlayPanel {
        panelName: PlayPanelName;
        dashboardAthlete: PlayAthlete | null;
        isRedirecting?: boolean;
    }

    interface PlayHistoryState {
        playPanel: PlayPanelName;
        previousPlayPanel: PlayPanelName | null;
    }

    const storageErrorMessage = 'Browser storage is unavailable. Enable storage and try again.';
    const PLAY_START_INTRO_MS = 1900;
    const PLAY_PANEL_PREPARATION_TIMEOUT_MS = 8000;
    let flow: PlayAthleteFlowApi | null = null;
    let athleteSelection: AthleteSelectionController | null = null;
    let activePlayPanelName: PlayPanelName | null = null;
    let hasRenderedPlayPanel = false;
    let activePanelTransitionTimer = 0;
    let activePanelTransitionElement: HTMLElement | null = null;
    let playPanelTransitionRunId = 0;
    let playPanelPreparationRunId = 0;
    let playStartIntroTimer = 0;
    let playStartIntroRunId = 0;
    const playRouteStates: Readonly<Record<PlayPanelName, Readonly<PlayRouteState>>> = Object.freeze({
        start: Object.freeze({ route: '/play', panelId: 'playStartPanel', isWide: false }),
        join: Object.freeze({ route: '/join', panelId: 'joinTrackPanel', isWide: true }),
        selection: Object.freeze({ route: '/select-athlete', panelId: 'athleteSelectionPanel', isWide: false }),
        dashboard: Object.freeze({ route: '/dashboard', panelId: 'athleteDashboardPanel', isWide: false })
    });

    function getFlowReady(): Promise<PlayAthleteFlowApi> {
        return Promise.resolve(window.modulesReady || undefined)
            .catch(() => {})
            .then(() => {
                flow = window.playAthleteFlow;
                if (!flow) throw new Error('Athlete flow module did not load.');
                return flow;
            });
    }

    function requirePlayFlow(): PlayAthleteFlowApi {
        if (!flow) throw new Error('Athlete flow module is not initialized.');
        return flow;
    }

    function requireAthleteSelection(): AthleteSelectionController {
        if (!athleteSelection) throw new Error('Athlete selection is not initialized.');
        return athleteSelection;
    }

    function isPlayPanelName(value: unknown): value is PlayPanelName {
        return value === 'start' || value === 'join' || value === 'selection' || value === 'dashboard';
    }

    function getPlayRouteState(panelName: PlayPanelName): Readonly<PlayRouteState> {
        return playRouteStates[panelName];
    }

    function getPlayRoute(panelName: PlayPanelName): PlayRouteState['route'] {
        return getPlayRouteState(panelName).route;
    }

    function getPlayPanelNameForPath(pathname = window.location.pathname): PlayPanelName {
        const path = pathname.toLowerCase();
        if (path === '/join') return 'join';
        if (path === '/select-athlete') return 'selection';
        if (path === '/dashboard') return 'dashboard';
        return 'start';
    }

    function getCurrentPlayPanelName(): PlayPanelName {
        const historyState: unknown = window.history?.state;
        const statePanel = historyState && typeof historyState === 'object'
            ? Reflect.get(historyState, 'playPanel')
            : null;
        return activePlayPanelName || (isPlayPanelName(statePanel) ? statePanel : getPlayPanelNameForPath());
    }

    function getAthleteSelectionPreviewReady(): Promise<unknown> {
        return athleteSelection && typeof athleteSelection.getPreviewReady === 'function'
            ? athleteSelection.getPreviewReady()
            : Promise.resolve();
    }

    function waitForPlayPanelPreparation(promise: PromiseLike<unknown> | unknown): Promise<unknown> {
        let timeoutId = 0;
        const deadline = new Promise<void>(resolve => {
            timeoutId = window.setTimeout(resolve, PLAY_PANEL_PREPARATION_TIMEOUT_MS);
        });

        return Promise.race([Promise.resolve(promise).catch(() => {}), deadline])
            .finally(() => window.clearTimeout(timeoutId));
    }

    function beginPlayPanelPreparation(): void {
        finishPlayStartIntro();
        document.documentElement.classList.remove('play-route-ready');
        document.body.classList.remove('play-start-active');
        document.body.classList.add('play-route-hydrating');
        document.body.setAttribute('aria-busy', 'true');
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function showSelectionPanelAfterPreviewReady(panelName: PlayPanelName, preparationRunId: number): void {
        waitForPlayPanelPreparation(getAthleteSelectionPreviewReady())
            .then(() => {
                if (preparationRunId === playPanelPreparationRunId
                    && getPlayPanelNameForPath() === panelName) {
                    showPanelByName(panelName);
                }
            });
    }

    function getPlayUrl(panelName: PlayPanelName, options: PlayUrlOptions = {}): string {
        const route = getPlayRoute(panelName);
        return options.preserveCurrentUrlParts
            ? `${route}${window.location.search}${window.location.hash}`
            : route;
    }

    function createPlayHistoryState(
        panelName: PlayPanelName,
        previousPlayPanel: PlayPanelName | null = null
    ): PlayHistoryState {
        return {
            playPanel: panelName,
            previousPlayPanel: previousPlayPanel && isPlayPanelName(previousPlayPanel) ? previousPlayPanel : null
        };
    }

    function setPlayHistory(
        panelName: PlayPanelName,
        historyMode: PlayHistoryMode | undefined,
        options: PlayUrlOptions = {}
    ): void {
        if (!historyMode || !window.history) return;

        const route = getPlayUrl(panelName, options);
        const currentPath = window.location.pathname.toLowerCase();
        const currentPanelName = getCurrentPlayPanelName();
        const currentState: unknown = window.history.state;
        const previousCandidate = currentState && typeof currentState === 'object'
            ? Reflect.get(currentState, 'previousPlayPanel')
            : null;
        const currentPreviousPanel = isPlayPanelName(previousCandidate) ? previousCandidate : null;
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

    function prefersReducedPlayMotion(): boolean {
        return Boolean(window.matchMedia
            && window.matchMedia('(prefers-reduced-motion: reduce)').matches);
    }

    function finishPlayStartIntro(): void {
        playStartIntroRunId += 1;
        window.clearTimeout(playStartIntroTimer);
        playStartIntroTimer = 0;
        document.body.classList.remove('play-start-preintro', 'play-start-intro');
    }

    function getPlayStartWatermarkReady(): Promise<unknown> {
        const watermark = document.querySelector<HTMLImageElement>('.play-logo-watermark');
        if (!watermark || watermark.complete) return Promise.resolve();
        if (typeof watermark.decode === 'function') {
            return watermark.decode().catch(() => {});
        }

        return new Promise<void>(resolve => {
            watermark.addEventListener('load', () => resolve(), { once: true });
            watermark.addEventListener('error', () => resolve(), { once: true });
        });
    }

    function startPlayStartIntro(options: PlayIntroOptions = {}): void {
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

    function completePlayPanelTransition(panelName: PlayPanelName, transitionRunId: number): void {
        if (transitionRunId !== playPanelTransitionRunId) return;
        activePlayPanelName = panelName;
        hasRenderedPlayPanel = true;
        activePanelTransitionTimer = 0;
        activePanelTransitionElement = null;
        document.documentElement.classList.remove('play-panel-transitioning');
        document.documentElement.classList.add('play-route-ready');
        document.body.classList.remove('play-route-hydrating');
        document.body.removeAttribute('aria-busy');
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function showPanelByName(panelName: PlayPanelName, options: PlayPanelOptions = {}): void {
        const state = getPlayRouteState(panelName);
        const main = document.querySelector<HTMLElement>('.play-hub-main');
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

        const commitPanel = (): void => {
            main.classList.toggle('play-hub-main--wide', state.isWide);
            document.body.classList.toggle('play-start-active', panelName === 'start');
            if (panelName !== 'start') finishPlayStartIntro();
            document.querySelectorAll<HTMLElement>('.play-hub-panel').forEach(panel => {
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

    function resolveRequestedPlayPanel(panelName: PlayPanelName): ResolvedPlayPanel {
        const flow = requirePlayFlow();
        const athleteSelection = requireAthleteSelection();
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

    function showPlayPanel(requestedPanelName: PlayPanelName, options: PlayPanelOptions = {}): void {
        const flow = requirePlayFlow();
        const athleteSelection = requireAthleteSelection();
        const preparationRunId = playPanelPreparationRunId + 1;
        playPanelPreparationRunId = preparationRunId;
        const requested = isPlayPanelName(requestedPanelName) ? requestedPanelName : 'start';
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
                beginPlayPanelPreparation();
                waitForPlayPanelPreparation(
                    athleteSelection.loadAthletes({ savedSelectionTransition: false }))
                    .then(() => showSelectionPanelAfterPreviewReady(panelName, preparationRunId));
                return;
            }

            athleteSelection.loadAthletes().catch(() => {});
            if (hydratedStoredAthlete) {
                beginPlayPanelPreparation();
                showSelectionPanelAfterPreviewReady(panelName, preparationRunId);
                return;
            }

            showPanelByName(panelName, { animate: false });
            return;
        }

        if (panelName === 'dashboard') {
            beginPlayPanelPreparation();
            const athlete = resolved.dashboardAthlete;
            const dashboardTitle = document.getElementById('athleteDashboardTitle');
            const dashboardPicture = document.getElementById('athleteDashboardPicture');
            const dynamicActions = document.getElementById('athleteDashboardDynamicActions');
            if (!athlete || !dashboardTitle || !dashboardPicture || !dynamicActions) return;
            const pictureReady = flow.renderAthleteDashboardHeader(athlete, {
                titleElement: dashboardTitle,
                frameElement: dashboardPicture
            });
            const actionsReady = flow.renderDashboardActions(athlete, {
                dynamicActionsElement: dynamicActions,
                discountElement: document.getElementById('athleteDashboardDiscounts')
            });
            waitForPlayPanelPreparation(Promise.allSettled([pictureReady, actionsReady])).then(() => {
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

    function showPanelForCurrentUrl(): void {
        showPlayPanel(getPlayPanelNameForPath(), { historyMode: null });
    }

    function initializePlayHistory(): void {
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

    function navigateToPreviousPlayPanel(fallbackPanelName: PlayPanelName): void {
        const historyState: unknown = window.history?.state;
        const previousCandidate = historyState && typeof historyState === 'object'
            ? Reflect.get(historyState, 'previousPlayPanel')
            : null;
        const previousPanelName = isPlayPanelName(previousCandidate) ? previousCandidate : null;
        if (previousPanelName === fallbackPanelName && window.history.length > 1) {
            window.history.back();
            return;
        }

        showPlayPanel(fallbackPanelName, { historyMode: 'replace' });
    }

    function navigateToStartPanel(): void {
        navigateToPreviousPlayPanel('start');
    }

    function navigateToSelectionPanel(): void {
        navigateToPreviousPlayPanel('selection');
    }

    function getCheckoutQuerySuffix(): string {
        const params = new URLSearchParams();
        const freePassValue = window.getFreePassValue ? window.getFreePassValue() : null;
        const discountValue = window.getDiscountValue ? window.getDiscountValue() : null;
        if (freePassValue !== null) params.set('freepass', freePassValue);
        if (discountValue !== null) params.set('discount', discountValue);
        const query = params.toString();
        return query ? `?${query}` : '';
    }

    function startAmateurApplication(retryButton: HTMLButtonElement): void {
        const flow = requirePlayFlow();
        const stored = flow.setPendingPaymentOffer({
            source: 'join-game',
            offerType: 'amateur',
            currency: 'USD',
            amountUsd: 10
        }, retryButton);
        if (!stored) return;
        window.location.href = `/pheno-age${getCheckoutQuerySuffix()}`;
    }

    function startProApplication(retryButton: HTMLButtonElement): void {
        const flow = requirePlayFlow();
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

    function renderJoinFreePassPricing(): boolean {
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

    function renderJoinPricing(): void {
        const flow = requirePlayFlow();
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

    function promotePlayStartAction(button: HTMLButtonElement): void {
        button.classList.remove('grey', 'flow-action--secondary');
        button.classList.add('green');
    }

    function demotePlayStartAction(button: HTMLButtonElement): void {
        button.classList.remove('green');
        button.classList.add('grey', 'flow-action--secondary');
    }

    function initializePlayMenu(): void {
        getFlowReady().then(() => {
            const flow = requirePlayFlow();
            if (window.captureFreePassFromUrl) {
                window.captureFreePassFromUrl();
            }
            if (window.captureDiscountFromUrl) {
                window.captureDiscountFromUrl();
            }

            const athleteInput = document.getElementById('playAthleteInput');
            const athleteError = document.getElementById('playAthleteError');
            const confirmAthleteButton = document.getElementById('playConfirmAthleteBtn');
            const selectionTitle = document.getElementById('athleteSelectionTitle');
            const selectionPicture = document.getElementById('athleteSelectionPicture');
            if (!(athleteInput instanceof HTMLInputElement)
                || !athleteError
                || !(confirmAthleteButton instanceof HTMLButtonElement)
                || !selectionTitle
                || !selectionPicture) {
                throw new Error('Athlete selection controls are missing.');
            }

            athleteSelection = flow.createAthleteSelectionController({
                input: athleteInput,
                errorElement: athleteError,
                confirmButton: confirmAthleteButton,
                titleElement: selectionTitle,
                frameElement: selectionPicture,
                autocompleteRootSelector: '.play-autocomplete-container'
            }).bind();

            initializePlayHistory();

            const hasApp = flow.hasSubmittedApplication();
            const container = document.querySelector<HTMLElement>('.play-menu-actions');
            const newBtn = document.getElementById('newGameBtn');
            const contBtn = document.getElementById('continueGameBtn');
            if (!container
                || !(newBtn instanceof HTMLButtonElement)
                || !(contBtn instanceof HTMLButtonElement)) {
                throw new Error('Play menu controls are missing.');
            }

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
            const joinTrackBackButton = document.getElementById('joinTrackBackBtn');
            const joinStartAmateurButton = document.getElementById('joinStartAmateurBtn');
            const joinMobileStartAmateurButton = document.getElementById('joinMobileStartAmateurBtn');
            const joinGoProButton = document.getElementById('joinGoProButton');
            const joinMobileGoProButton = document.getElementById('joinMobileGoProButton');
            const selectionBackButton = document.getElementById('playSelectionBackBtn');
            const dashboardBackButton = document.getElementById('playDashboardBackBtn');
            if (!(joinTrackBackButton instanceof HTMLButtonElement)
                || !(joinStartAmateurButton instanceof HTMLButtonElement)
                || !(joinMobileStartAmateurButton instanceof HTMLButtonElement)
                || !(joinGoProButton instanceof HTMLButtonElement)
                || !(joinMobileGoProButton instanceof HTMLButtonElement)
                || !(selectionBackButton instanceof HTMLButtonElement)
                || !(dashboardBackButton instanceof HTMLButtonElement)) {
                throw new Error('Play workflow buttons are missing.');
            }

            joinTrackBackButton.addEventListener('click', navigateToStartPanel);
            joinStartAmateurButton.addEventListener('click', () => startAmateurApplication(joinStartAmateurButton));
            joinMobileStartAmateurButton.addEventListener('click', () => startAmateurApplication(joinMobileStartAmateurButton));
            joinGoProButton.addEventListener('click', () => startProApplication(joinGoProButton));
            joinMobileGoProButton.addEventListener('click', () => startProApplication(joinMobileGoProButton));
            selectionBackButton.addEventListener('click', navigateToStartPanel);
            dashboardBackButton.addEventListener('click', navigateToSelectionPanel);

            confirmAthleteButton.addEventListener('click', () => {
                const flow = requirePlayFlow();
                const athleteSelection = requireAthleteSelection();
                const currentAthlete = athleteSelection.getCurrentAthlete();
                if (!flow.persistSelectedAthlete(currentAthlete)) {
                    window.customAlert(storageErrorMessage);
                    return;
                }
                showPlayPanel('dashboard', { historyMode: 'push' });
            });

            [
                newBtn,
                contBtn,
                joinTrackBackButton,
                joinStartAmateurButton,
                joinMobileStartAmateurButton,
                joinGoProButton,
                joinMobileGoProButton,
                selectionBackButton,
                dashboardBackButton
            ].forEach(button => {
                button.disabled = false;
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

export {};
