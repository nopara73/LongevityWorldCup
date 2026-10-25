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
        restoreScroll?: boolean;
    }

    type PlayMoveDirection = 'forward' | 'back';

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
        playScrollY?: number;
        playEntryId: number;
    }

    const storageErrorMessage = 'Browser storage is unavailable. Enable storage and try again.';
    const PLAY_START_INTRO_MS = 1900;
    const PLAY_PANEL_PREPARATION_TIMEOUT_MS = 8000;
    let flow: PlayAthleteFlowApi | null = null;
    let athleteSelection: AthleteSelectionController | null = null;
    let activePlayPanelName: PlayPanelName | null = null;
    let hasRenderedPlayPanel = false;
    let activePanelTransitionTimer = 0;
    let activePanelMotion: Animation[] = [];
    let playPanelTransitionRunId = 0;
    let playCardSelectionRunId = 0;
    let playPanelHoldTimer = 0;
    let lastPlayInteraction: 'keyboard' | 'pointer' | null = null;
    let lastPlayInteractionAt = -Infinity;
    let lastPressedPlayAction: HTMLElement | null = null;
    const playReturnFocus: Partial<Record<PlayPanelName, string>> = {};
    const playScrollPositions = new Map<number, number>();
    let nextPlayEntryId = 0;
    let playPanelPreparationRunId = 0;
    let playStartIntroTimer = 0;
    let playStartIntroRunId = 0;
    const playRouteStates: Readonly<Record<PlayPanelName, Readonly<PlayRouteState>>> = Object.freeze({
        start: Object.freeze({ route: '/play', panelId: 'playStartPanel', isWide: false }),
        join: Object.freeze({ route: '/join', panelId: 'joinTrackPanel', isWide: true }),
        selection: Object.freeze({ route: '/select-athlete', panelId: 'athleteSelectionPanel', isWide: false }),
        dashboard: Object.freeze({ route: '/dashboard', panelId: 'athleteDashboardPanel', isWide: false })
    });

    /* Motion: the flow is a row of screens, start on the left and the dashboard
       on the right. Moving deeper slides in from the right, going back from the
       left. The complete arriving screen takes over at once; a duplicate of
       the old card must never cover its identity or controls. Values
       match the shared motion tokens (motion.css / aesthetic-system.css). */
    const playPanelDepth: Readonly<Record<PlayPanelName, number>> = Object.freeze({
        start: 0,
        join: 1,
        selection: 1,
        dashboard: 2
    });
    const PLAY_MOTION_ENTER_MS = 260;
    const PLAY_MOTION_DEAL_MS = 440;
    const PLAY_MOTION_COUNT_MS = 640;
    const PLAY_MOTION_SHIFT_PX = 32;
    const PLAY_MOTION_SAFETY_MS = 1200;
    /* An in-flow move keeps the current screen up while the next one prepares;
       only a slow preparation falls back to the loading card. */
    const PLAY_PANEL_HOLD_MS = 450;
    const PLAY_EASE_ENTER = 'cubic-bezier(0, 0, 0, 1)';
    const PLAY_EASE_STANDARD = 'cubic-bezier(0.2, 0, 0, 1)';
    const PLAY_CARD_LAYOUT_QUERY = '(min-width: 761px) and (min-height: 600px)';
    const reducedPlayMotion = window.matchMedia('(prefers-reduced-motion: reduce)');

    reducedPlayMotion.addEventListener('change', () => {
        if (!reducedPlayMotion.matches || !document.querySelector('.play-hub-main')) return;
        cancelPlayPanelMotion();
        finishPlayStartIntro();
        // Cancel only decoration. A pending identity, image or price must still
        // pass its preparation gate before the requested panel is revealed.
        window.LwcFlowActionDock?.refreshNow?.();
    });

    function trackPlayInteraction(kind: 'keyboard' | 'pointer', event: Event): void {
        lastPlayInteraction = kind;
        lastPlayInteractionAt = performance.now();
        const target = event.target instanceof Element ? event.target.closest<HTMLElement>('.flow-action') : null;
        if (target) lastPressedPlayAction = target;
    }

    function wasRecentPlayInteraction(): boolean {
        return performance.now() - lastPlayInteractionAt < 1500;
    }

    function getPlayMoveDirection(from: PlayPanelName | null, to: PlayPanelName): PlayMoveDirection {
        if (!from) return 'forward';
        return playPanelDepth[to] < playPanelDepth[from] ? 'back' : 'forward';
    }

    function playAnimate(
        element: Element | null | undefined,
        keyframes: Keyframe[],
        options: KeyframeAnimationOptions
    ): Animation | null {
        if (!element || typeof element.animate !== 'function') return null;
        try {
            const animation = element.animate(keyframes, { fill: 'backwards', ...options });
            // Older engines can silently ignore the pseudo-element option and
            // animate the card itself (including its opacity) instead.
            if (options.pseudoElement && (animation.effect as KeyframeEffect | null)?.pseudoElement !== options.pseudoElement) {
                animation.cancel();
                return null;
            }
            return animation;
        } catch (_) {
            return null;
        }
    }

    function cancelPlayPanelMotion(): void {
        playPanelTransitionRunId += 1;
        playCardSelectionRunId += 1;
        window.clearTimeout(activePanelTransitionTimer);
        activePanelTransitionTimer = 0;
        const motion = activePanelMotion;
        activePanelMotion = [];
        motion.forEach(animation => animation.cancel());
        document.documentElement.removeAttribute('data-flow-dock-hold');
        document.documentElement.classList.remove('play-panel-transitioning');
        delete document.documentElement.dataset.playMove;
    }

    function getPlayPanelLanding(panelName: PlayPanelName, direction: PlayMoveDirection): HTMLElement | null {
        const isUsable = (element: Element | null): element is HTMLElement => element instanceof HTMLElement
            && element.isConnected
            && !element.matches(':disabled')
            && !element.closest('[hidden], [inert]')
            && element.getClientRects().length > 0;

        const returnId = direction === 'back' ? playReturnFocus[panelName] : undefined;
        const returnTarget = returnId ? document.getElementById(returnId) : null;
        if (isUsable(returnTarget)) return returnTarget;

        const panel = document.getElementById(getPlayRouteState(panelName).panelId);
        if (!panel) return null;
        if (panelName === 'start') {
            return document.querySelector<HTMLElement>('.play-menu-actions .flow-action');
        }
        if (panelName === 'selection') {
            const confirm = document.getElementById('playConfirmAthleteBtn');
            if (isUsable(confirm)) return confirm;
            const input = document.getElementById('playAthleteInput');
            const coarseTouch = lastPlayInteraction === 'pointer'
                && Boolean(window.matchMedia && window.matchMedia('(pointer: coarse)').matches);
            // A touch move must not raise the on-screen keyboard over the card.
            if (!coarseTouch && isUsable(input)) return input;
            return panel.querySelector<HTMLElement>('h1');
        }
        if (panelName === 'dashboard') {
            const command = document.querySelector('#athleteDashboardActions .flow-action:not(.back-button)');
            if (isUsable(command)) return command;
        }
        return panel.querySelector<HTMLElement>('h1');
    }

    function landPlayPanelFocus(panelName: PlayPanelName, direction: PlayMoveDirection): void {
        const target = getPlayPanelLanding(panelName, direction);
        if (!target) return;
        if (target.tagName === 'H1' && !target.hasAttribute('tabindex')) {
            target.setAttribute('tabindex', '-1');
        }
        target.focus({ preventScroll: true });
        // Large text can keep a tall action group inline. The focused command
        // still needs to be on screen; a dock already owns its own landing.
        if (!target.closest('.flow-action-stack--docked')) {
            const viewport = window.visualViewport;
            const top = (viewport?.offsetTop || 0) + 8;
            const bottom = (viewport?.offsetTop || 0) + (viewport?.height || window.innerHeight) - 8;
            const box = target.getBoundingClientRect();
            const offset = box.top < top ? box.top - top : box.bottom > bottom ? box.bottom - bottom : 0;
            if (offset) window.scrollBy({ top: offset, behavior: 'instant' });
        }
    }

    function rememberPlayReturnFocus(panelName: PlayPanelName): void {
        const panel = document.getElementById(getPlayRouteState(panelName).panelId);
        const active = document.activeElement;
        const candidate = active instanceof HTMLElement && active.id && panel?.contains(active)
            ? active
            : lastPressedPlayAction;
        if (candidate?.id) playReturnFocus[panelName] = candidate.id;
    }

    function getPlayRestoredScroll(): number {
        const state: unknown = window.history?.state;
        const entryId = state && typeof state === 'object' ? Reflect.get(state, 'playEntryId') : null;
        const position = typeof entryId === 'number' ? playScrollPositions.get(entryId) : undefined;
        if (position !== undefined) return position;
        const saved = state && typeof state === 'object' ? Reflect.get(state, 'playScrollY') : null;
        return typeof saved === 'number' && Number.isFinite(saved) ? Math.max(0, saved) : 0;
    }

    function usesPlayerCardLayout(): boolean {
        return Boolean(window.matchMedia && window.matchMedia(PLAY_CARD_LAYOUT_QUERY).matches);
    }

    function dealPlayCard(
        card: Element | null,
        direction: PlayMoveDirection,
        delay = 0,
        style: 'deal' | 'flip' = 'deal'
    ): Animation[] {
        if (!card) return [];
        const side = direction === 'back' ? -1 : 1;
        // A dealt card lands on the table; a flipped card is already there and
        // turns over to its new face, so it never disappears.
        const keyframes: Keyframe[] = style === 'flip'
            ? [
                { opacity: 0.6, transform: 'perspective(900px) rotateY(-18deg) scale(0.95)' },
                { opacity: 1, offset: 0.6, transform: 'perspective(900px) rotateY(3deg) scale(1.015)' },
                { opacity: 1, transform: 'none' }
            ]
            : [
                { opacity: 1, transform: `perspective(1100px) translate(${side * 40}px, 26px) rotateX(9deg) rotateY(${side * -12}deg) rotateZ(${side * 2.5}deg) scale(0.92)` },
                { opacity: 1, offset: 0.62, transform: `perspective(1100px) translate(${side * -2}px, -4px) rotateX(-1.5deg) rotateY(${side * 1.5}deg) rotateZ(${side * -0.4}deg) scale(1.012)` },
                { opacity: 1, transform: 'none' }
            ];
        const deal = playAnimate(card, keyframes, {
            duration: style === 'flip' ? 380 : PLAY_MOTION_DEAL_MS,
            delay,
            easing: PLAY_EASE_STANDARD
        });
        const glint = playAnimate(card, [
            { opacity: 0, backgroundPosition: '130% 0' },
            { opacity: 1, offset: 0.25 },
            { opacity: 0, backgroundPosition: '-30% 0' }
        ], { duration: 560, delay: delay + 180, easing: PLAY_EASE_STANDARD, pseudoElement: '::after', fill: 'none' });
        return [deal, glint].filter((animation): animation is Animation => Boolean(animation));
    }

    function settlePlayPrices(panel: HTMLElement, delay: number): Animation[] {
        const animations: Animation[] = [];
        // Keep the actual final price visible unless the decorative counter and
        // its empty accessibility alternative are both supported.
        if (typeof CSS === 'undefined' || typeof CSS.registerProperty !== 'function'
            || !CSS.supports('content', '"$" counter(play-price) / ""')) return animations;
        panel.querySelectorAll<HTMLElement>('.pro-old-price + .pro-new-price').forEach(price => {
            const oldPrice = price.previousElementSibling;
            const from = Number.parseInt((oldPrice?.textContent || '').replace(/[^\d]/g, ''), 10);
            const text = (price.textContent || '').trim();
            const match = /^\$(\d+)$/.exec(text);
            const to = match ? Number.parseInt(match[1] || '', 10) : NaN;
            if (!Number.isFinite(from) || !Number.isFinite(to) || to >= from) return;
            price.style.setProperty('--play-price-ink', getComputedStyle(price).color);
            price.style.setProperty('--play-price', String(to));
            price.classList.add('play-price-settling');
            const count = playAnimate(price, [
                { '--play-price': String(from) },
                { '--play-price': String(to) }
            ], { duration: PLAY_MOTION_COUNT_MS, delay, easing: PLAY_EASE_ENTER });
            if (!count) {
                price.classList.remove('play-price-settling');
                return;
            }
            count.finished.catch(() => {}).finally(() => price.classList.remove('play-price-settling'));
            animations.push(count);
        });
        return animations;
    }

    function rise(elements: Iterable<Element>, delay: number, stagger: number, distance = 10): Animation[] {
        const animations: Animation[] = [];
        let index = 0;
        for (const element of elements) {
            const animation = playAnimate(element, [
                { transform: `translateY(${distance}px)` },
                { transform: 'none' }
            ], { duration: 220, delay: delay + index * stagger, easing: PLAY_EASE_ENTER });
            if (animation) animations.push(animation);
            index += 1;
        }
        return animations;
    }

    function animatePlayPanelArrival(
        panelName: PlayPanelName,
        panel: HTMLElement,
        direction: PlayMoveDirection
    ): { layout: Animation[]; extra: Animation[] } {
        const side = direction === 'back' ? -1 : 1;
        const layout: Animation[] = [];
        const extra: Animation[] = [];
        const push = (list: Animation[], animations: (Animation | null)[]): void => {
            animations.forEach(animation => { if (animation) list.push(animation); });
        };

        if (panelName === 'start') return { layout, extra };

        const cardLayout = usesPlayerCardLayout();
        if (panelName === 'dashboard' && cardLayout) {
            // The whole player card is dealt onto the table.
            push(layout, dealPlayCard(panel, direction));
        } else {
            push(layout, [playAnimate(panel, [
                { transform: `translateX(${side * PLAY_MOTION_SHIFT_PX}px)` },
                { transform: 'none' }
            ], { duration: PLAY_MOTION_ENTER_MS, easing: PLAY_EASE_ENTER })]);
        }

        if (panelName === 'dashboard') {
            if (!cardLayout) push(layout, dealPlayCard(panel.querySelector('.play-dashboard-card'), direction, 40));
            push(layout, rise(panel.querySelectorAll('.play-dashboard-discounts .pro-discount-box'), 140, 0));
            // The arriving card already supplies the movement. Its commands
            // stay readable and share that position from the first frame.
            push(extra, settlePlayPrices(document.getElementById('athleteDashboardActions') || panel, 300));
        } else if (panelName === 'selection') {
            const visual = panel.querySelector('.play-athlete-visual');
            if (visual) push(layout, rise([visual], 40, 0, 8));
        }
        return { layout, extra };
    }

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

    function clearPlayPanelHold(): void {
        window.clearTimeout(playPanelHoldTimer);
        playPanelHoldTimer = 0;
        document.body.classList.remove('play-route-pending');
    }

    function beginPlayPanelPreparation(): void {
        clearPlayPanelHold();
        const currentPanel = activePlayPanelName
            ? document.getElementById(getPlayRouteState(activePlayPanelName).panelId)
            : null;
        if (hasRenderedPlayPanel && currentPanel && !currentPanel.hidden
            && !document.body.classList.contains('play-route-hydrating')) {
            // Keep the screen the visitor is looking at, still usable, while the
            // next one prepares. Only a slow preparation shows the loading card.
            document.body.classList.add('play-route-pending');
            document.body.setAttribute('aria-busy', 'true');
            playPanelHoldTimer = window.setTimeout(() => {
                playPanelHoldTimer = 0;
                document.body.classList.remove('play-route-pending');
                hidePlayPanelsForPreparation();
            }, PLAY_PANEL_HOLD_MS);
            return;
        }

        hidePlayPanelsForPreparation();
    }

    function hidePlayPanelsForPreparation(): void {
        cancelPlayPanelMotion();
        finishPlayStartIntro();
        document.documentElement.classList.remove('play-route-ready');
        document.body.classList.remove('play-start-active');
        document.body.classList.add('play-route-hydrating');
        document.body.setAttribute('aria-busy', 'true');
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function showSelectionPanelAfterPreviewReady(
        panelName: PlayPanelName,
        preparationRunId: number,
        panelOptions: PlayPanelOptions = {}
    ): void {
        waitForPlayPanelPreparation(getAthleteSelectionPreviewReady())
            .then(() => {
                if (preparationRunId === playPanelPreparationRunId
                    && getPlayPanelNameForPath() === panelName) {
                    showPanelByName(panelName, panelOptions);
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
            playEntryId: ++nextPlayEntryId,
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
                // Remember where the visitor was, so coming back lands there.
                if (currentState && typeof currentState === 'object') {
                    window.history.replaceState({ ...currentState, playScrollY: window.scrollY }, '');
                }
                rememberPlayReturnFocus(currentPanelName);
                window.history.pushState(state, '', route);
            }
        }
    }

    function prefersReducedPlayMotion(): boolean {
        return reducedPlayMotion.matches;
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
        document.documentElement.classList.remove('play-panel-transitioning');
        delete document.documentElement.dataset.playMove;
        document.documentElement.removeAttribute('data-flow-dock-hold');
        document.documentElement.classList.add('play-route-ready');
        document.body.classList.remove('play-route-hydrating');
        document.body.removeAttribute('aria-busy');
        window.LwcInitialView.complete();
        window.LwcFlowActionDock?.refreshNow?.();
    }

    function showPanelByName(panelName: PlayPanelName, options: PlayPanelOptions = {}): void {
        const state = getPlayRouteState(panelName);
        const main = document.querySelector<HTMLElement>('.play-hub-main');
        const targetPanel = document.getElementById(state.panelId);
        if (!main || !targetPanel) return;

        const previousPanelName = activePlayPanelName;
        const isInFlowMove = hasRenderedPlayPanel && previousPanelName !== panelName;
        const direction = getPlayMoveDirection(previousPanelName, panelName);
        const shouldAnimate = options.animate !== false
            && isInFlowMove
            && !prefersReducedPlayMotion();

        clearPlayPanelHold();
        cancelPlayPanelMotion();
        const transitionRunId = playPanelTransitionRunId;

        const commitPanel = (): void => {
            main.classList.toggle('play-hub-main--wide', state.isWide);
            document.body.classList.toggle('play-start-active', panelName === 'start');
            if (panelName !== 'start') finishPlayStartIntro();
            document.querySelectorAll<HTMLElement>('.play-hub-panel').forEach(panel => {
                panel.hidden = panel !== targetPanel;
            });
            if (isInFlowMove) {
                document.body.classList.remove('play-route-hydrating');
                document.documentElement.classList.add('play-route-ready');
            }
            // Preparation is done as soon as the arriving controls are usable;
            // decorative motion must not keep the live screen busy.
            document.body.removeAttribute('aria-busy');
            activePlayPanelName = panelName;
            window.scrollTo(0, options.restoreScroll ? getPlayRestoredScroll() : 0);
            window.LwcFlowActionDock?.refreshNow?.();
            if (isInFlowMove) landPlayPanelFocus(panelName, direction);
        };

        if (!shouldAnimate) {
            commitPanel();
            if (panelName === 'start') startPlayStartIntro({ restart: hasRenderedPlayPanel });
            completePlayPanelTransition(panelName, transitionRunId);
            return;
        }

        document.documentElement.classList.add('play-panel-transitioning');
        document.documentElement.dataset.playMove = direction;
        commitPanel();
        document.documentElement.setAttribute('data-flow-dock-hold', '');
        if (panelName === 'start') startPlayStartIntro({ restart: true });

        const arrival = animatePlayPanelArrival(panelName, targetPanel, direction);
        const motion = arrival.layout;
        activePanelMotion = [...motion, ...arrival.extra];

        const settled = Promise.all(motion.map(animation => animation.finished.catch(() => {})));
        const safety = new Promise<void>(resolve => {
            activePanelTransitionTimer = window.setTimeout(resolve, PLAY_MOTION_SAFETY_MS);
        });
        Promise.race([settled, safety]).then(() => {
            if (transitionRunId !== playPanelTransitionRunId) return;
            window.clearTimeout(activePanelTransitionTimer);
            // A throttled or interrupted animation can reach the safety deadline
            // first. Restore resting geometry before releasing dock decisions.
            motion.forEach(animation => animation.cancel());
            completePlayPanelTransition(panelName, transitionRunId);
        });
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
        clearPlayPanelHold();
        cancelPlayPanelMotion();
        const preparationRunId = playPanelPreparationRunId + 1;
        playPanelPreparationRunId = preparationRunId;
        const requested = isPlayPanelName(requestedPanelName) ? requestedPanelName : 'start';
        const resolved = resolveRequestedPlayPanel(requested);
        if (resolved.isRedirecting) return;
        const panelName = resolved.panelName;
        const historyMode = requested === panelName ? options.historyMode : (options.historyMode || 'replace');
        const panelOptions: PlayPanelOptions = options.restoreScroll ? { restoreScroll: true } : {};

        setPlayHistory(panelName, historyMode, options);

        if (panelName === 'join') {
            athleteSelection.closeAllLists();
            renderJoinPricing();
            showPanelByName(panelName, panelOptions);
            return;
        }

        if (panelName === 'selection') {
            const hydratedStoredAthlete = athleteSelection.hydrateStoredAthleteSelection();
            if (!hydratedStoredAthlete && athleteSelection.hasPendingSavedSelection()) {
                beginPlayPanelPreparation();
                waitForPlayPanelPreparation(
                    athleteSelection.loadAthletes({ savedSelectionTransition: false }))
                    .then(() => showSelectionPanelAfterPreviewReady(panelName, preparationRunId, panelOptions));
                return;
            }

            athleteSelection.loadAthletes().catch(() => {});
            if (hydratedStoredAthlete) {
                beginPlayPanelPreparation();
                showSelectionPanelAfterPreviewReady(panelName, preparationRunId, panelOptions);
                return;
            }

            showPanelByName(panelName, panelOptions);
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
                    showPanelByName(panelName, panelOptions);
                }
            });
            return;
        }

        athleteSelection.closeAllLists();
        showPanelByName(panelName, panelOptions);
    }

    function showPanelForCurrentUrl(): void {
        showPlayPanel(getPlayPanelNameForPath(), { historyMode: null, restoreScroll: hasRenderedPlayPanel });
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

    function clearCompletedBioageHandoff(playFlow: PlayAthleteFlowApi): void {
        [
            'biomarkerData',
            'bioageClock',
            'chronoPhenoDifference',
            'chronoBortzDifference'
        ].forEach(key => playFlow.removeSessionItem(key));
    }

    // The chosen track holds its ring while the apply flow takes over.
    let joinChoiceTimer = 0;
    function markJoinChoice(choice: 'amateur' | 'pro'): void {
        window.clearTimeout(joinChoiceTimer);
        document.getElementById('joinTrackPanel')?.setAttribute('data-join-choice', choice);
        // Also recover on browsers without the Navigation API or a navigation
        // that never commits (for example a download or an HTTP 204 response).
        joinChoiceTimer = window.setTimeout(clearJoinChoice, 1200);
    }

    function clearJoinChoice(): void {
        window.clearTimeout(joinChoiceTimer);
        document.getElementById('joinTrackPanel')?.removeAttribute('data-join-choice');
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
        clearCompletedBioageHandoff(flow);
        markJoinChoice('amateur');
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
        clearCompletedBioageHandoff(flow);
        markJoinChoice('pro');
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
        document.addEventListener('pointerdown', event => trackPlayInteraction('pointer', event), true);
        document.addEventListener('keydown', event => {
            if (!['Shift', 'Control', 'Alt', 'Meta'].includes(event.key)) trackPlayInteraction('keyboard', event);
        }, true);
        window.addEventListener('scroll', () => {
            // Save the current entry as it moves, including entries left using
            // browser Back/Forward (where popstate already exposes the target).
            if (hasRenderedPlayPanel && activePlayPanelName === getPlayPanelNameForPath()
                && window.history.state?.playPanel === activePlayPanelName) {
                // Avoid rate-limited history writes during continuous scrolling.
                playScrollPositions.set(window.history.state.playEntryId, window.scrollY);
            }
        }, { passive: true });
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
                autocompleteRootSelector: '.play-autocomplete-container',
                onAthleteSelected: () => {
                    // A pick the visitor just made turns the card over to the new face.
                    if (!wasRecentPlayInteraction() || prefersReducedPlayMotion()) return;
                    const card = selectionTitle.closest('.play-athlete-card');
                    if (!card || card.closest('[hidden]') || !hasRenderedPlayPanel) return;
                    const selectionRunId = ++playCardSelectionRunId;
                    card.getAnimations?.({ subtree: false }).forEach(animation => animation.cancel());
                    const flip = dealPlayCard(card, 'forward', 0, 'flip');
                    activePanelMotion = activePanelMotion.filter(animation => animation.playState === 'running' || animation.playState === 'paused');
                    activePanelMotion.push(...flip);
                    // Measure the actions against the card at rest, not mid-turn.
                    const root = document.documentElement;
                    if (!root.classList.contains('play-panel-transitioning')) {
                        root.setAttribute('data-flow-dock-hold', '');
                        Promise.all(flip.slice(0, 1).map(animation => animation.finished.catch(() => {}))).then(() => {
                            if (selectionRunId !== playCardSelectionRunId || root.classList.contains('play-panel-transitioning')) return;
                            root.removeAttribute('data-flow-dock-hold');
                            window.LwcFlowActionDock?.refreshNow?.();
                        });
                    }
                }
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
            window.addEventListener('pageshow', clearJoinChoice);
            window.navigation?.addEventListener('navigate', event => {
                if (event.destination.sameDocument) clearJoinChoice();
                else event.signal.addEventListener('abort', clearJoinChoice, { once: true });
            });
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
            window.LwcInitialView.fail();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', initializePlayMenu, { once: true });
    } else {
        initializePlayMenu();
    }
})();

export {};
