interface FlowActionDockEnsureClearOptions {
    followingVisibleSiblingCount?: number;
    includeNextVisibleSibling?: boolean;
    margin?: number;
    behavior?: ScrollBehavior;
}

interface FlowActionDockApi {
    refresh: () => void;
    refreshNow: () => void;
    ensureClear: (element: Element | null | undefined, options?: FlowActionDockEnsureClearOptions) => void;
}

(function () {
    interface DockState {
        docked: boolean;
        enterFrame: number;
        inlineHeight: number;
        mutationObserver: MutationObserver;
        placeholder: HTMLDivElement;
    }

    interface VisualViewportBounds {
        top: number;
        bottom: number;
        height: number;
    }

    const dockSelector = '[data-flow-dock]';
    const dockClass = 'flow-action-stack--docked';
    const dockEnteringClass = 'flow-action-stack--dock-entering';
    const placeholderClass = 'flow-action-dock-placeholder';
    const activeClass = 'flow-action-dock-active';
    const readyClass = 'flow-action-dock-ready';
    const singleBackActionClass = 'flow-action-stack--single-back-action';
    const backPrimaryActionClass = 'flow-action-stack--back-primary-action';
    const mobileMedia = window.matchMedia('(max-width: 760px)');
    const reducedMotionMedia = window.matchMedia('(prefers-reduced-motion: reduce)');
    const states = new Map<HTMLElement, DockState>();
    let refreshFrame = 0;
    let resizeObserver: ResizeObserver | null = null;
    let registrationObserver: MutationObserver | null = null;
    let documentStateObserver: MutationObserver | null = null;
    let transitionsReady = false;
    let generatedFormId = 0;

    function getVisualViewportBounds(): VisualViewportBounds {
        const visualViewport = window.visualViewport;
        const top = visualViewport && Number.isFinite(visualViewport.offsetTop) ? visualViewport.offsetTop : 0;
        const height = visualViewport && Number.isFinite(visualViewport.height)
            ? visualViewport.height
            : window.innerHeight;

        return {
            top,
            bottom: top + height,
            height
        };
    }

    function hasLayoutBox(element: Element | null | undefined): element is HTMLElement {
        if (!(element instanceof HTMLElement) || !element.isConnected || element.hidden) return false;
        const style = window.getComputedStyle(element);
        if (style.display === 'none') return false;
        const rect = element.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    }

    function isVisible(element: Element | null | undefined): element is HTMLElement {
        if (!hasLayoutBox(element)) return false;
        const style = window.getComputedStyle(element);
        return style.visibility !== 'hidden' && Number(style.opacity) !== 0;
    }

    function isBackAction(element: HTMLElement | undefined): boolean {
        return Boolean(element?.classList.contains('back-button')
            && element.classList.contains('flow-action--icon-left'));
    }

    function updateActionLayoutState(element: HTMLElement): void {
        const visibleActions = Array.from(element.querySelectorAll<HTMLElement>(':scope > .flow-action')).filter(isVisible);
        element.querySelectorAll<HTMLElement>('.flow-action[data-flow-dock-label]').forEach(action => {
            const label = action.querySelector<HTMLElement>('.flow-action__label');
            if (label) label.dataset.flowDockLabel = action.dataset.flowDockLabel;
        });
        const backAction = visibleActions.find(isBackAction);
        const primaryAction = visibleActions.find(action =>
            action !== backAction && !action.classList.contains('flow-action--secondary'));

        element.classList.toggle(singleBackActionClass, visibleActions.length === 1 && isBackAction(visibleActions[0]));
        element.classList.toggle(backPrimaryActionClass, visibleActions.length === 2 && !!backAction && !!primaryAction);
    }

    function clearActionLayoutState(element: HTMLElement): void {
        element.classList.remove(singleBackActionClass, backPrimaryActionClass);
    }

    function conditionMatches(element: HTMLElement): boolean {
        const requiredSelector = element.getAttribute('data-flow-dock-when');
        if (requiredSelector && !document.querySelector(requiredSelector)) return false;

        const excludedSelector = element.getAttribute('data-flow-dock-unless');
        return !(excludedSelector && document.querySelector(excludedSelector));
    }

    function ensureSubmitButtonFormOwnership(element: HTMLElement): void {
        element.querySelectorAll<HTMLButtonElement | HTMLInputElement>('button[type="submit"], input[type="submit"]').forEach(control => {
            if (control.hasAttribute('form')) return;
            const form = control.closest<HTMLFormElement>('form');
            if (!form) return;

            if (!form.id) {
                generatedFormId += 1;
                form.id = `flow-action-form-${generatedFormId}`;
            }
            control.setAttribute('form', form.id);
        });
    }

    function registerElement(element: Element): void {
        if (!(element instanceof HTMLElement)) return;
        if (states.has(element)) return;

        const placeholder = document.createElement('div');
        placeholder.className = placeholderClass;
        placeholder.hidden = true;
        element.parentNode?.insertBefore(placeholder, element);

        const mutationObserver = new MutationObserver(scheduleRefresh);
        mutationObserver.observe(element, {
            attributes: true,
            attributeFilter: ['class', 'hidden', 'style', 'disabled', 'aria-hidden'],
            childList: true,
            subtree: true
        });

        const state = {
            docked: false,
            enterFrame: 0,
            inlineHeight: 0,
            mutationObserver,
            placeholder
        };
        states.set(element, state);
        resizeObserver?.observe(element);
        resizeObserver?.observe(placeholder);
    }

    function unregisterDisconnectedElements(): void {
        Array.from(states.entries()).forEach(([element, state]) => {
            if (element.isConnected && state.placeholder.isConnected) return;
            if (state.enterFrame) window.cancelAnimationFrame(state.enterFrame);
            state.mutationObserver.disconnect();
            resizeObserver?.unobserve(element);
            resizeObserver?.unobserve(state.placeholder);
            if (state.docked && !state.placeholder.isConnected) element.remove();
            state.placeholder.remove();
            states.delete(element);
        });
    }

    function ensureRegisteredElements(): void {
        document.querySelectorAll(dockSelector).forEach(registerElement);
        unregisterDisconnectedElements();
    }

    function beginDockEnter(element: HTMLElement, state: DockState): void {
        if (!transitionsReady || reducedMotionMedia.matches) return;
        element.classList.add(dockEnteringClass);
        state.enterFrame = window.requestAnimationFrame(() => {
            state.enterFrame = window.requestAnimationFrame(() => {
                element.classList.remove(dockEnteringClass);
                state.enterFrame = 0;
            });
        });
    }

    function cancelDockEnter(element: HTMLElement, state: DockState): void {
        if (state.enterFrame) {
            window.cancelAnimationFrame(state.enterFrame);
            state.enterFrame = 0;
        }
        element.classList.remove(dockEnteringClass);
    }

    function dockElement(element: HTMLElement, state: DockState): void {
        const rect = element.getBoundingClientRect();
        state.inlineHeight = rect.height || element.offsetHeight || state.inlineHeight;
        state.placeholder.hidden = false;
        state.placeholder.style.height = `${Math.ceil(state.inlineHeight)}px`;

        ensureSubmitButtonFormOwnership(element);
        document.body.appendChild(element);
        element.classList.add(dockClass);
        state.docked = true;
        updateActionLayoutState(element);
        beginDockEnter(element, state);
    }

    function undockElement(element: HTMLElement, state: DockState): void {
        cancelDockEnter(element, state);
        element.classList.remove(dockClass);
        clearActionLayoutState(element);
        state.docked = false;

        if (state.placeholder.parentNode) {
            state.placeholder.parentNode.insertBefore(element, state.placeholder.nextSibling);
        }
        state.placeholder.hidden = true;
        state.placeholder.style.height = '';
    }

    function getMeasurementRect(element: HTMLElement, state: DockState): DOMRect {
        return state.docked
            ? state.placeholder.getBoundingClientRect()
            : element.getBoundingClientRect();
    }

    function shouldDock(element: HTMLElement, state: DockState): boolean {
        if (!conditionMatches(element)) return false;
        if (state.docked ? !hasLayoutBox(state.placeholder) : !isVisible(element)) return false;

        const mode = (element.getAttribute('data-flow-dock') || 'auto').toLowerCase();
        if (mode === 'off') return false;
        if (mode === 'mobile' && !mobileMedia.matches) return false;
        if (mode === 'always') return true;
        if (mode !== 'overflow' && mobileMedia.matches) return true;

        const rect = getMeasurementRect(element, state);
        const viewport = getVisualViewportBounds();
        const gap = Number(element.getAttribute('data-flow-dock-gap') || 12);
        const releaseGap = Number(element.getAttribute('data-flow-dock-release-gap') || 48);

        if (state.docked) {
            const releaseHeight = Math.max(rect.height, state.inlineHeight || 0);
            const releaseBottom = rect.top + releaseHeight;
            return releaseBottom > viewport.bottom - gap - releaseGap
                || rect.top < viewport.top + gap + releaseGap;
        }

        state.inlineHeight = rect.height || state.inlineHeight || 0;
        return rect.bottom > viewport.bottom - gap || rect.top < viewport.top + gap;
    }

    function refresh(): void {
        refreshFrame = 0;
        ensureRegisteredElements();

        let dockedHeight = 0;
        let hasDockedElement = false;

        states.forEach((state, element) => {
            const docked = shouldDock(element, state);
            if (docked && !state.docked) dockElement(element, state);
            if (!docked && state.docked) undockElement(element, state);
            if (!state.docked) return;

            updateActionLayoutState(element);
            const measuredHeight = element.getBoundingClientRect().height || element.offsetHeight;
            state.placeholder.style.height = `${Math.ceil(measuredHeight)}px`;
            dockedHeight = Math.max(dockedHeight, measuredHeight);
            hasDockedElement = true;
        });

        document.documentElement.classList.toggle(activeClass, hasDockedElement);
        document.body.classList.toggle(activeClass, hasDockedElement);
        const heightValue = hasDockedElement ? `${Math.ceil(dockedHeight)}px` : '0px';
        document.documentElement.style.setProperty('--flow-action-dock-height', heightValue);
        document.body.style.setProperty('--flow-action-dock-height', heightValue);
    }

    function scheduleRefresh(): void {
        if (!refreshFrame) refreshFrame = window.requestAnimationFrame(refresh);
    }

    function refreshImmediately(): void {
        if (refreshFrame) {
            window.cancelAnimationFrame(refreshFrame);
            refreshFrame = 0;
        }
        refresh();
    }

    function getNextVisibleSibling(element: Element | null | undefined): HTMLElement | null {
        let sibling = element?.nextElementSibling;
        while (sibling) {
            if (isVisible(sibling)) return sibling;
            sibling = sibling.nextElementSibling;
        }
        return null;
    }

    function getClearanceRect(element: Element, options: FlowActionDockEnsureClearOptions): { top: number; bottom: number } {
        const rect = element.getBoundingClientRect();
        const requestedSiblingCount = options.followingVisibleSiblingCount;
        const siblingCount = typeof requestedSiblingCount === 'number' && Number.isFinite(requestedSiblingCount)
            ? Math.max(0, Math.floor(requestedSiblingCount))
            : options.includeNextVisibleSibling ? 1 : 0;
        let top = rect.top;
        let bottom = rect.bottom;
        let sibling: Element | null = element;

        for (let index = 0; index < siblingCount; index += 1) {
            sibling = getNextVisibleSibling(sibling);
            if (!sibling) break;
            const siblingRect = sibling.getBoundingClientRect();
            top = Math.min(top, siblingRect.top);
            bottom = Math.max(bottom, siblingRect.bottom);
        }
        return { top, bottom };
    }

    function ensureElementClear(
        element: Element | null | undefined,
        options: FlowActionDockEnsureClearOptions = {}
    ): void {
        if (!element?.isConnected) return;
        refreshImmediately();

        const dockHeight = parseFloat(getComputedStyle(document.documentElement)
            .getPropertyValue('--flow-action-dock-height')) || 0;
        if (!dockHeight) return;

        const requestedMargin = options.margin;
        const margin = typeof requestedMargin === 'number' && Number.isFinite(requestedMargin) ? requestedMargin : 16;
        const rect = getClearanceRect(element, options);
        const viewport = getVisualViewportBounds();
        const bottomLimit = viewport.bottom - dockHeight - margin;
        if (rect.bottom <= bottomLimit) return;

        window.scrollBy({
            top: Math.ceil(rect.bottom - bottomLimit),
            behavior: options.behavior || (reducedMotionMedia.matches ? 'auto' : 'smooth')
        });
    }

    function init(): void {
        if ('ResizeObserver' in window) resizeObserver = new ResizeObserver(scheduleRefresh);
        ensureRegisteredElements();

        registrationObserver = new MutationObserver(scheduleRefresh);
        registrationObserver.observe(document.body, {
            attributes: true,
            attributeFilter: ['class', 'hidden', 'style', 'aria-hidden'],
            childList: true,
            subtree: true
        });

        documentStateObserver = new MutationObserver(scheduleRefresh);
        documentStateObserver.observe(document.documentElement, {
            attributes: true,
            attributeFilter: ['class']
        });
        documentStateObserver.observe(document.body, {
            attributes: true,
            attributeFilter: ['class']
        });

        window.addEventListener('resize', scheduleRefresh);
        window.addEventListener('orientationchange', scheduleRefresh);
        window.addEventListener('scroll', scheduleRefresh, { passive: true });
        window.addEventListener('load', scheduleRefresh);
        window.addEventListener('pageshow', scheduleRefresh);
        document.addEventListener('focusin', scheduleRefresh, true);
        document.addEventListener('focusout', () => window.setTimeout(scheduleRefresh, 0), true);
        mobileMedia.addEventListener?.('change', scheduleRefresh);

        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', scheduleRefresh);
            window.visualViewport.addEventListener('scroll', scheduleRefresh);
        }
        document.fonts?.ready.then(scheduleRefresh).catch(() => {});

        const runtimeWindow = window as Window & { LwcFlowActionDock?: FlowActionDockApi };
        runtimeWindow.LwcFlowActionDock = {
            refresh: scheduleRefresh,
            refreshNow: refreshImmediately,
            ensureClear: ensureElementClear
        };

        refreshImmediately();
        window.requestAnimationFrame(() => {
            transitionsReady = true;
            document.documentElement.classList.add(readyClass);
            document.body.classList.add(readyClass);
            refreshImmediately();
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
