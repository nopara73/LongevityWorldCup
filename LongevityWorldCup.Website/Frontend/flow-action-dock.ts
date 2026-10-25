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
        oversizedInlineSignature: string;
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
    const keyboardOpenClass = 'flow-input-keyboard-open';
    const singleBackActionClass = 'flow-action-stack--single-back-action';
    const backPrimaryActionClass = 'flow-action-stack--back-primary-action';
    const mobileMedia = window.matchMedia('(max-width: 760px)');
    const desktopRowMedia = window.matchMedia('(min-width: 769px)');
    const playerCardRowMedia = window.matchMedia('(min-width: 761px) and (min-height: 600px)');
    const keyboardMedia = window.matchMedia('(max-width: 760px), (pointer: coarse)');
    const reducedMotionMedia = window.matchMedia('(prefers-reduced-motion: reduce)');
    const states = new Map<HTMLElement, DockState>();
    let refreshFrame = 0;
    let resizeObserver: ResizeObserver | null = null;
    let registrationObserver: MutationObserver | null = null;
    let documentStateObserver: MutationObserver | null = null;
    let transitionsReady = false;
    let generatedFormId = 0;
    let keyboardClearFrame = 0;
    let elementClearFrame = 0;
    let focusedControlClearanceRequested = false;
    let focusedControlLayoutSignature = '';
    let largestUnobscuredViewportHeight = 0;
    let lastLayoutViewportWidth = window.innerWidth;
    let isKeyboardOpen = false;

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

    function getInputViewportBounds(): VisualViewportBounds {
        const viewport = getVisualViewportBounds();
        let top = viewport.top;
        for (const id of ['site-sticky-header', 'site-sticky-progress']) {
            const bar = document.getElementById(id);
            if (!bar?.classList.contains('visible') || !hasLayoutBox(bar)) continue;
            const style = getComputedStyle(bar);
            if (style.visibility === 'hidden') continue;
            const rect = bar.getBoundingClientRect();
            const fixedTop = parseFloat(style.top);
            top = Math.max(top, Number.isFinite(fixedTop) ? fixedTop + rect.height : rect.bottom);
        }
        top = Math.min(top, viewport.bottom);
        return { top, bottom: viewport.bottom, height: viewport.bottom - top };
    }

    function isKeyboardEditingControl(element: Element | null): element is HTMLElement {
        if (element instanceof HTMLTextAreaElement) return !element.disabled && !element.readOnly;
        if (element instanceof HTMLElement && element.isContentEditable) return true;
        if (!(element instanceof HTMLInputElement) || element.disabled || element.readOnly) return false;

        return ![
            'button',
            'checkbox',
            'color',
            'file',
            'hidden',
            'image',
            'radio',
            'range',
            'reset',
            'submit'
        ].includes(element.type);
    }

    function getObservedAppViewportHeight(viewport: VisualViewportBounds): number {
        return Math.max(viewport.height, window.innerHeight);
    }

    function syncKeyboardState(): void {
        const viewport = getVisualViewportBounds();
        const editingControl = isKeyboardEditingControl(document.activeElement);
        const viewportWidth = window.innerWidth;
        if (Math.abs(viewportWidth - lastLayoutViewportWidth) > 48) {
            lastLayoutViewportWidth = viewportWidth;
            largestUnobscuredViewportHeight = 0;
        }

        const observedAppViewportHeight = getObservedAppViewportHeight(viewport);
        if (!editingControl) {
            largestUnobscuredViewportHeight = observedAppViewportHeight;
        } else if (!largestUnobscuredViewportHeight) {
            largestUnobscuredViewportHeight = observedAppViewportHeight;
        } else {
            largestUnobscuredViewportHeight = Math.max(
                largestUnobscuredViewportHeight,
                observedAppViewportHeight
            );
        }

        const keyboardThreshold = Math.max(120, largestUnobscuredViewportHeight * 0.22);
        isKeyboardOpen = keyboardMedia.matches
            && editingControl
            && largestUnobscuredViewportHeight - viewport.height >= keyboardThreshold;
        const keyboardOcclusion = isKeyboardOpen
            ? Math.max(0, largestUnobscuredViewportHeight - viewport.height)
            : 0;
        const occlusionValue = `${Math.ceil(keyboardOcclusion)}px`;

        document.documentElement.classList.toggle(keyboardOpenClass, isKeyboardOpen);
        document.body.classList.toggle(keyboardOpenClass, isKeyboardOpen);
        document.documentElement.style.setProperty('--flow-input-keyboard-occlusion', occlusionValue);
        document.body.style.setProperty('--flow-input-keyboard-occlusion', occlusionValue);
    }

    function keepFocusedControlInVisualViewport(layoutResized: boolean): void {
        if ((!isKeyboardOpen && !layoutResized) || !isKeyboardEditingControl(document.activeElement)) return;
        if (keyboardClearFrame) window.cancelAnimationFrame(keyboardClearFrame);

        keyboardClearFrame = window.requestAnimationFrame(() => {
            keyboardClearFrame = 0;
            const control = document.activeElement;
            if ((!isKeyboardOpen && !layoutResized) || !isKeyboardEditingControl(control) || !isVisible(control)
                || control.closest('[hidden], [inert], [aria-hidden="true"]')) return;

            const viewport = getInputViewportBounds();
            const rect = control.getBoundingClientRect();
            const margin = 16;
            const topLimit = viewport.top + margin;
            const dockHeight = parseFloat(getComputedStyle(document.documentElement)
                .getPropertyValue('--flow-action-dock-height')) || 0;
            const bottomLimit = viewport.bottom - dockHeight - margin;
            if (rect.bottom > bottomLimit) {
                window.scrollBy({ top: Math.ceil(rect.bottom - bottomLimit), behavior: 'auto' });
            } else if (rect.top < topLimit) {
                window.scrollBy({ top: Math.floor(rect.top - topLimit), behavior: 'auto' });
            }
        });
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
        // A primary action keeps its slot while disabled (shown as secondary), so the dock does not jump.
        const primaryAction = visibleActions.find(action =>
            action !== backAction && (action.matches(':disabled') || !action.classList.contains('flow-action--secondary')));

        element.classList.toggle(singleBackActionClass, visibleActions.length === 1 && isBackAction(visibleActions[0]));
        element.classList.toggle(backPrimaryActionClass, visibleActions.length === 2 && !!backAction && !!primaryAction);
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

    function setPlaceholderHeight(state: DockState, height: number): void {
        const heightValue = `${Math.ceil(height)}px`;
        if (state.placeholder.style.height !== heightValue) {
            state.placeholder.style.height = heightValue;
        }
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
            placeholder,
            oversizedInlineSignature: ''
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
        if (!transitionsReady || reducedMotionMedia.matches || element.contains(document.activeElement)) return;
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

    function moveKeepingFocus(element: HTMLElement, move: () => void): void {
        const focused = document.activeElement;
        const hadFocus = focused instanceof HTMLElement && element.contains(focused);
        const scrollLeft = window.scrollX;
        const scrollTop = window.scrollY;
        let focusAssigned = false;
        const trackFocus = (event: FocusEvent): void => {
            if (event.target !== focused) focusAssigned = true;
        };
        if (hadFocus) document.addEventListener('focusin', trackFocus, true);
        try {
            move();
        } finally {
            document.removeEventListener('focusin', trackFocus, true);
        }
        // insertBefore blurs a moved subtree in older engines. Only recover
        // focus lost by the move, after its final layout and loading state agree.
        // A page handler's new focus always takes precedence.
        if (hadFocus && !focusAssigned && document.activeElement !== focused
            && (!document.activeElement || document.activeElement === document.body)
            && element.contains(focused) && focused.isConnected && focused.tabIndex >= 0
            && !focused.matches(':disabled, [aria-disabled="true"]')
            && !focused.closest('[hidden], [inert], [aria-hidden="true"]')
            && isFocusTargetVisible(focused)) {
            focused.focus({ preventScroll: true });
        }
        // A native state-preserving move can scroll a focused fixed control
        // into its new inline position even though it never lost focus.
        if (hadFocus && !focusAssigned && document.activeElement === focused
            && (window.scrollX !== scrollLeft || window.scrollY !== scrollTop)) {
            window.scrollTo({ left: scrollLeft, top: scrollTop, behavior: 'instant' });
        }
    }

    function isFocusTargetVisible(element: HTMLElement): boolean {
        if (!hasLayoutBox(element)) return false;
        // The DOM-move fallback must also work in engines without checkVisibility.
        for (let ancestor: HTMLElement | null = element; ancestor; ancestor = ancestor.parentElement) {
            const style = window.getComputedStyle(ancestor);
            if (style.display === 'none' || style.visibility === 'hidden' || style.visibility === 'collapse'
                || Number(style.opacity) === 0 || style.contentVisibility === 'hidden') return false;
        }
        return true;
    }

    function moveElement(element: HTMLElement, parent: Node, before: ChildNode | null): void {
        if ('moveBefore' in parent && typeof parent.moveBefore === 'function'
            && element.isConnected && parent.isConnected) {
            parent.moveBefore(element, before);
        } else {
            parent.insertBefore(element, before);
        }
    }

    function syncActionOrder(element: HTMLElement, docked: boolean): void {
        const actions = Array.from(element.children);
        // Multi-command docks can lead with Back while keeping it after the
        // regular actions in their inline layout. Match keyboard and visual
        // order using the same focus-preserving move as two-action flows.
        if (element.hasAttribute('data-flow-dock-back-first')) {
            const back = actions.find(action => isBackAction(action as HTMLElement)) as HTMLElement | undefined;
            if (back && (docked ? element.firstElementChild !== back : element.lastElementChild !== back)) {
                moveKeepingFocus(element, () => moveElement(back, element, docked ? element.firstChild : null));
            }
            return;
        }
        if (actions.length !== 2 || !actions.every(action => action.matches('.flow-action'))) return;
        const back = actions.find(action => isBackAction(action as HTMLElement)) as HTMLElement | undefined;
        const primary = actions.find(action => action !== back) as HTMLElement | undefined;
        if (!back || !primary) return;
        const inlineBackFirst = desktopRowMedia.matches
            || (element.classList.contains('convergence-actions') && !mobileMedia.matches)
            || (element.classList.contains('play-athlete-actions') && playerCardRowMedia.matches);
        const backFirst = docked || element.hasAttribute('data-flow-dock-inline-back-first') || (inlineBackFirst
            && !element.classList.contains('flow-action-stack--hide-disabled-primary'));
        const first = backFirst ? back : primary;
        if (element.firstElementChild !== first) {
            moveKeepingFocus(element, () => moveElement(first, element, element.firstChild));
        }
    }

    function dockElement(element: HTMLElement, state: DockState): void {
        const rect = element.getBoundingClientRect();
        state.inlineHeight = rect.height || element.offsetHeight || state.inlineHeight;
        state.placeholder.hidden = false;
        setPlaceholderHeight(state, state.inlineHeight);

        ensureSubmitButtonFormOwnership(element);
        moveKeepingFocus(element, () => {
            moveElement(element, document.body, null);
            element.classList.add(dockClass);
            state.docked = true;
            syncActionOrder(element, true);
            updateActionLayoutState(element);
        });
        beginDockEnter(element, state);
    }

    function undockElement(element: HTMLElement, state: DockState): void {
        cancelDockEnter(element, state);
        element.classList.remove(dockClass);
        updateActionLayoutState(element);
        state.docked = false;

        const placeholderParent = state.placeholder.parentNode;
        if (placeholderParent) {
            moveKeepingFocus(element, () => {
                moveElement(element, placeholderParent, state.placeholder.nextSibling);
                syncActionOrder(element, false);
            });
        }
        state.placeholder.hidden = true;
        state.placeholder.style.height = '';
    }

    function getMeasurementRect(element: HTMLElement, state: DockState): DOMRect {
        return state.docked
            ? state.placeholder.getBoundingClientRect()
            : element.getBoundingClientRect();
    }

    function getInlineSignature(element: HTMLElement): string {
        const rect = element.getBoundingClientRect();
        return [window.innerWidth, window.innerHeight, getVisualViewportBounds().height,
            rect.width, rect.height, getComputedStyle(element).fontSize,
            element.getAttribute('data-flow-dock-max-height-ratio'), element.textContent].join('|');
    }

    function shouldDock(element: HTMLElement, state: DockState): boolean {
        if (!conditionMatches(element)) return false;
        // Portaled controls still belong to their source task while it loads.
        const origin = state.docked ? state.placeholder : element;
        if (document.documentElement.hasAttribute('data-initial-view') && origin.closest('.initial-view-main')) return false;
        if (state.docked ? !hasLayoutBox(state.placeholder) : !isVisible(element)) return false;
        if (isKeyboardOpen) return false;
        // A large action set is more useful in normal flow than over most of the
        // task. Retry its dock measurement when text, content or viewport changes.
        if (!state.docked && state.oversizedInlineSignature
            && state.oversizedInlineSignature === getInlineSignature(element)) return false;

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
        // A page animating a whole screen into place holds dock decisions, so
        // transformed, mid-motion geometry never docks or releases its actions.
        // It refreshes again when the motion settles.
        if (document.documentElement.hasAttribute('data-flow-dock-hold')) return;
        // Step changes intentionally focus with preventScroll. Ordinary focus
        // only opts into clearance when the viewport or text size really changed.
        const viewport = getVisualViewportBounds();
        const inputViewport = getInputViewportBounds();
        document.documentElement.style.setProperty('--flow-input-top-occlusion', `${Math.ceil(inputViewport.top - viewport.top)}px`);
        const layoutSignature = `${window.innerWidth}|${window.innerHeight}|${viewport.height}|${getComputedStyle(document.documentElement).fontSize}`;
        const layoutResized = focusedControlLayoutSignature !== '' && focusedControlLayoutSignature !== layoutSignature;
        focusedControlLayoutSignature = layoutSignature;
        const shouldClearFocusedControl = focusedControlClearanceRequested || layoutResized;
        focusedControlClearanceRequested = false;
        ensureRegisteredElements();
        syncKeyboardState();

        let dockedHeight = 0;
        let hasDockedElement = false;

        states.forEach((state, element) => {
            // Inline controls need the same visible-action classification as
            // the dock when a primary action is hidden, disabled or restored.
            updateActionLayoutState(element);
            syncActionOrder(element, state.docked);
            const docked = shouldDock(element, state);
            if (docked && !state.docked) dockElement(element, state);
            if (!docked && state.docked) undockElement(element, state);
            if (!state.docked) return;

            updateActionLayoutState(element);
            const measuredHeight = element.getBoundingClientRect().height || element.offsetHeight;
            const maxHeightRatio = Number(element.getAttribute('data-flow-dock-max-height-ratio'));
            const fitSelector = element.getAttribute('data-flow-dock-fit');
            const fitContainer = fitSelector ? state.placeholder.closest<HTMLElement>(fitSelector) : null;
            // A full-screen lockup needs room for its content as well as its
            // actions. When text grows, keep the whole group in normal flow.
            const viewportHeight = getVisualViewportBounds().height;
            if ((maxHeightRatio > 0 && maxHeightRatio <= 1 && measuredHeight > viewportHeight * maxHeightRatio)
                || (fitContainer && fitContainer.scrollHeight > viewportHeight + 1)) {
                undockElement(element, state);
                state.oversizedInlineSignature = getInlineSignature(element);
                return;
            }
            state.oversizedInlineSignature = '';
            // Wrapped dock actions can be taller than their original inline
            // layout. Reserve that footprint so the final message stays reachable.
            setPlaceholderHeight(state, Math.max(state.inlineHeight, measuredHeight));
            dockedHeight = Math.max(dockedHeight, measuredHeight);
            hasDockedElement = true;
        });

        document.documentElement.classList.toggle(activeClass, hasDockedElement);
        document.body.classList.toggle(activeClass, hasDockedElement);
        const heightValue = hasDockedElement ? `${Math.ceil(dockedHeight)}px` : '0px';
        document.documentElement.style.setProperty('--flow-action-dock-height', heightValue);
        document.body.style.setProperty('--flow-action-dock-height', heightValue);
        if (shouldClearFocusedControl) keepFocusedControlInVisualViewport(layoutResized);
    }

    function scheduleRefresh(): void {
        if (!refreshFrame) refreshFrame = window.requestAnimationFrame(refresh);
    }

    function scheduleFocusedControlClearance(): void {
        focusedControlClearanceRequested = true;
        scheduleRefresh();
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

    function scrollElementClear(
        element: Element | null | undefined,
        options: FlowActionDockEnsureClearOptions = {}
    ): { left: number; minimumTop: number; maximumTop: number } | null {
        if (!isVisible(element)) return null;
        refreshImmediately();

        const dockHeight = parseFloat(getComputedStyle(document.documentElement)
            .getPropertyValue('--flow-action-dock-height')) || 0;
        if (!dockHeight && !isKeyboardOpen) return null;

        const requestedMargin = options.margin;
        const rect = getClearanceRect(element, options);
        const viewport = getInputViewportBounds();
        const desiredMargin = typeof requestedMargin === 'number' && Number.isFinite(requestedMargin) ? requestedMargin : 16;
        // Keep both ends visible when a field and its counter nearly fill the viewport.
        const availableMargin = Math.max(0, (viewport.height - dockHeight - (rect.bottom - rect.top)) / 2);
        const margin = Math.min(desiredMargin, availableMargin);
        const bottomLimit = viewport.bottom - dockHeight - margin;
        const topLimit = viewport.top + margin;
        const fits = rect.bottom - rect.top <= viewport.height - dockHeight;
        const scroll = rect.bottom > bottomLimit ? Math.ceil(rect.bottom - bottomLimit)
            : fits && rect.top < topLimit ? Math.floor(rect.top - topLimit) : 0;
        const start = window.scrollY;
        const target = Math.max(0, Math.min(start + scroll,
            document.documentElement.scrollHeight - window.innerHeight));
        if (scroll) window.scrollBy({
            top: scroll,
            behavior: options.behavior || (reducedMotionMedia.matches ? 'auto' : 'smooth')
        });
        // An immediate scroll has already traversed its path. Only allow the
        // remaining smooth-scroll movement during the deferred layout pass.
        const remainingStart = window.scrollY;
        return { left: window.scrollX, minimumTop: Math.min(remainingStart, target), maximumTop: Math.max(remainingStart, target) };
    }

    function cancelDeferredElementClearance(): void {
        if (elementClearFrame) window.cancelAnimationFrame(elementClearFrame);
        elementClearFrame = 0;
    }

    function ensureElementClear(element: Element | null | undefined, options: FlowActionDockEnsureClearOptions = {}): void {
        cancelDeferredElementClearance();
        const scrollPath = scrollElementClear(element, options)
            ?? { left: window.scrollX, minimumTop: window.scrollY, maximumTop: window.scrollY };
        const focused = document.activeElement;
        // A sticky bar may appear after this scroll. Reconcile that edit once,
        // allowing our smooth scroll to advance, while yielding to a later scroll.
        elementClearFrame = window.requestAnimationFrame(() => {
            elementClearFrame = window.requestAnimationFrame(() => {
                elementClearFrame = 0;
                if (document.activeElement === focused && window.scrollX === scrollPath.left
                    && window.scrollY >= scrollPath.minimumTop - 1 && window.scrollY <= scrollPath.maximumTop + 1) {
                    scrollElementClear(element, options);
                }
            });
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

        window.addEventListener('resize', scheduleFocusedControlClearance);
        window.addEventListener('orientationchange', scheduleFocusedControlClearance);
        window.addEventListener('scroll', scheduleRefresh, { passive: true });
        window.addEventListener('wheel', cancelDeferredElementClearance, { passive: true });
        window.addEventListener('touchmove', cancelDeferredElementClearance, { passive: true });
        window.addEventListener('load', scheduleRefresh);
        window.addEventListener('pageshow', scheduleRefresh);
        document.addEventListener('focusin', scheduleFocusedControlClearance, true);
        document.addEventListener('focusout', () => window.setTimeout(scheduleRefresh, 0), true);
        mobileMedia.addEventListener?.('change', scheduleRefresh);
        keyboardMedia.addEventListener?.('change', scheduleRefresh);

        if (window.visualViewport) {
            window.visualViewport.addEventListener('resize', scheduleFocusedControlClearance);
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
