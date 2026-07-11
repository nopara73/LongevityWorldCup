(function () {
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
    const states = new Map();
    let refreshFrame = 0;
    let resizeObserver = null;
    let registrationObserver = null;
    let documentStateObserver = null;
    let transitionsReady = false;
    let generatedFormId = 0;

    function getVisualViewportBounds() {
        const visualViewport = window.visualViewport;
        const top = Number.isFinite(visualViewport?.offsetTop) ? visualViewport.offsetTop : 0;
        const height = Number.isFinite(visualViewport?.height)
            ? visualViewport.height
            : window.innerHeight;

        return {
            top,
            bottom: top + height,
            height
        };
    }

    function hasLayoutBox(element) {
        if (!element || !element.isConnected || element.hidden) return false;
        const style = window.getComputedStyle(element);
        if (style.display === 'none') return false;
        const rect = element.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    }

    function isVisible(element) {
        if (!hasLayoutBox(element)) return false;
        const style = window.getComputedStyle(element);
        return style.visibility !== 'hidden' && Number(style.opacity) !== 0;
    }

    function isBackAction(element) {
        return element?.classList.contains('back-button')
            && element.classList.contains('flow-action--icon-left');
    }

    function updateActionLayoutState(element) {
        const visibleActions = Array.from(element.querySelectorAll(':scope > .flow-action')).filter(isVisible);
        element.querySelectorAll('.flow-action[data-flow-dock-label]').forEach(action => {
            const label = action.querySelector('.flow-action__label');
            if (label) label.dataset.flowDockLabel = action.dataset.flowDockLabel;
        });
        const backAction = visibleActions.find(isBackAction);
        const primaryAction = visibleActions.find(action =>
            action !== backAction && !action.classList.contains('flow-action--secondary'));

        element.classList.toggle(singleBackActionClass, visibleActions.length === 1 && isBackAction(visibleActions[0]));
        element.classList.toggle(backPrimaryActionClass, visibleActions.length === 2 && !!backAction && !!primaryAction);
    }

    function clearActionLayoutState(element) {
        element.classList.remove(singleBackActionClass, backPrimaryActionClass);
    }

    function conditionMatches(element) {
        const requiredSelector = element.getAttribute('data-flow-dock-when');
        if (requiredSelector && !document.querySelector(requiredSelector)) return false;

        const excludedSelector = element.getAttribute('data-flow-dock-unless');
        return !(excludedSelector && document.querySelector(excludedSelector));
    }

    function ensureSubmitButtonFormOwnership(element) {
        element.querySelectorAll('button[type="submit"], input[type="submit"]').forEach(control => {
            if (control.hasAttribute('form')) return;
            const form = control.closest('form');
            if (!form) return;

            if (!form.id) {
                generatedFormId += 1;
                form.id = `flow-action-form-${generatedFormId}`;
            }
            control.setAttribute('form', form.id);
        });
    }

    function registerElement(element) {
        if (states.has(element)) return;

        const placeholder = document.createElement('div');
        placeholder.className = placeholderClass;
        placeholder.hidden = true;
        element.parentNode.insertBefore(placeholder, element);

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

    function unregisterDisconnectedElements() {
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

    function ensureRegisteredElements() {
        document.querySelectorAll(dockSelector).forEach(registerElement);
        unregisterDisconnectedElements();
    }

    function beginDockEnter(element, state) {
        if (!transitionsReady || reducedMotionMedia.matches) return;
        element.classList.add(dockEnteringClass);
        state.enterFrame = window.requestAnimationFrame(() => {
            state.enterFrame = window.requestAnimationFrame(() => {
                element.classList.remove(dockEnteringClass);
                state.enterFrame = 0;
            });
        });
    }

    function cancelDockEnter(element, state) {
        if (state.enterFrame) {
            window.cancelAnimationFrame(state.enterFrame);
            state.enterFrame = 0;
        }
        element.classList.remove(dockEnteringClass);
    }

    function dockElement(element, state) {
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

    function undockElement(element, state) {
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

    function getMeasurementRect(element, state) {
        return state.docked
            ? state.placeholder.getBoundingClientRect()
            : element.getBoundingClientRect();
    }

    function shouldDock(element, state) {
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

    function refresh() {
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

    function scheduleRefresh() {
        if (!refreshFrame) refreshFrame = window.requestAnimationFrame(refresh);
    }

    function refreshImmediately() {
        if (refreshFrame) {
            window.cancelAnimationFrame(refreshFrame);
            refreshFrame = 0;
        }
        refresh();
    }

    function getNextVisibleSibling(element) {
        let sibling = element?.nextElementSibling;
        while (sibling) {
            if (isVisible(sibling)) return sibling;
            sibling = sibling.nextElementSibling;
        }
        return null;
    }

    function getClearanceRect(element, options) {
        const rect = element.getBoundingClientRect();
        const siblingCount = Number.isFinite(options.followingVisibleSiblingCount)
            ? Math.max(0, Math.floor(options.followingVisibleSiblingCount))
            : options.includeNextVisibleSibling ? 1 : 0;
        let top = rect.top;
        let bottom = rect.bottom;
        let sibling = element;

        for (let index = 0; index < siblingCount; index += 1) {
            sibling = getNextVisibleSibling(sibling);
            if (!sibling) break;
            const siblingRect = sibling.getBoundingClientRect();
            top = Math.min(top, siblingRect.top);
            bottom = Math.max(bottom, siblingRect.bottom);
        }
        return { top, bottom };
    }

    function ensureElementClear(element, options = {}) {
        if (!element?.isConnected) return;
        refreshImmediately();

        const dockHeight = parseFloat(getComputedStyle(document.documentElement)
            .getPropertyValue('--flow-action-dock-height')) || 0;
        if (!dockHeight) return;

        const margin = Number.isFinite(options.margin) ? options.margin : 16;
        const rect = getClearanceRect(element, options);
        const viewport = getVisualViewportBounds();
        const bottomLimit = viewport.bottom - dockHeight - margin;
        if (rect.bottom <= bottomLimit) return;

        window.scrollBy({
            top: Math.ceil(rect.bottom - bottomLimit),
            behavior: options.behavior || (reducedMotionMedia.matches ? 'auto' : 'smooth')
        });
    }

    function init() {
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

        window.LwcFlowActionDock = {
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
