// Shared touch and gesture layer. touch.css owns how every state looks; this
// classic script decides when a finger is pressing, swiping, pulling or
// dragging, and hands the result to the controls each page already has.
//
// - Taps: the pressed card, row or button gives way after a short hold-off,
//   so scrolling past a row never flashes it.
// - Sticky hover: on touch-only screens, :hover rules are rewritten to never
//   match, so a tapped card does not stay lit after the finger leaves.
// - Hit targets: small controls get an invisible margin up to 44px.
// - Scrollers: horizontal strips report which edges hide more content.
// - Gestures: sibling views swipe, flows swipe back, sheets drag down to
//   dismiss, and the board and feed pull to refresh.
//
// Every gesture only drives an existing control (a radio, a Back button, a
// close button), so keyboard, mouse and assistive paths stay the source of
// truth. Mouse input never reaches the gesture code: desktop is unchanged.
// Reduced motion keeps the gestures but drops the travel that follows the finger.

interface LwcTouchViewsOptions {
    readonly items: () => readonly HTMLElement[];
    readonly current: () => number;
    readonly activate: (item: HTMLElement) => void;
}

interface LwcTouchSheetOptions {
    readonly panel: () => HTMLElement | null;
    readonly scroller?: () => HTMLElement | null;
    readonly isOpen: () => boolean;
    readonly dismiss: () => void;
    readonly backdrop?: () => HTMLElement | null;
}

interface LwcTouchPullDetail {
    readonly waitUntil: (work: Promise<unknown>) => void;
}

interface LwcTouchApi {
    readonly claim: (owner: object) => boolean;
    readonly release: (owner: object) => void;
    readonly isTouch: () => boolean;
    readonly views: (surface: HTMLElement, options: LwcTouchViewsOptions) => void;
    readonly sheet: (overlay: HTMLElement, options: LwcTouchSheetOptions) => void;
    readonly pull: (surface: HTMLElement) => void;
    readonly scroller: (element: HTMLElement) => void;
    readonly refresh: () => void;
}

interface Window {
    LwcTouch: LwcTouchApi;
}

(() => {
    const root = document.documentElement;
    const touchOnly = window.matchMedia("(hover: none) and (pointer: coarse) and (any-hover: none)");
    const coarse = window.matchMedia("(pointer: coarse)");
    const anyCoarse = window.matchMedia("(any-pointer: coarse)");
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    const tappable = [
        "a[href]", "button", "summary", "label[for]", "label.view-badge",
        "[role='button']", "[role='tab']", "[role='link']", "[data-lwc-tap]",
        ".leaderboard table tbody tr", ".podium-item", ".option-button",
        "input[type='submit']", "input[type='button']",
    ].join(", ");
    const notTappable = ":disabled, [aria-disabled='true'], .no-results, .tier-separator, .leaderboard-skeleton-row, .events-skeleton-row";
    // Gestures never start on something that already owns a drag.
    const gestureExclusions = "a, button, input, select, textarea, canvas, iframe, video, [contenteditable]:not([contenteditable='false']), [role='button'], [role='slider'], [role='spinbutton'], [role='menu'], [role='listbox'], .athlete-share-menu, .site-menu, .sidebar-filter-list, [data-lwc-no-gesture]";
    const smallControls = [
        "button", "[role='button']", "summary", "label.view-badge", "a.modal-link",
        ".athlete-step", ".close", ".sticky-close-btn", ".clear-icon", ".image-nav",
        ".footer-link", ".event-feed-link", ".view-all a", "a.view-all",
    ].join(", ");
    const scrollerSelectors = [
        ".proof-review-filmstrip", ".lwc-merch-mobile-track", ".archive-table-wrapper",
        "#detailsModal .clock-history-scroll", "#detailsModal #biomarkerResultList",
        ".modal-badge-strip", ".lmx-dashboard-scroll", ".lmx-board-scroll",
        ".view-switcher-chips", ".leaderboard-selection-chips", "[data-lwc-scroller]",
    ].join(", ");

    const minTarget = 44;
    const pressDelay = 60;
    const pressHold = 110;
    const slop = 10;

    let owner: object | null = null;
    const cancellations = new Map<HTMLElement, Set<() => void>>();
    function registerCancellation(element: HTMLElement, cancel: () => void): void {
        const callbacks = cancellations.get(element) ?? new Set<() => void>();
        callbacks.add(cancel);
        cancellations.set(element, callbacks);
    }
    let multipleTouches = false;
    function claim(candidate: object): boolean {
        if (multipleTouches || (owner && owner !== candidate) || !window.getSelection()?.isCollapsed) return false;
        owner = candidate;
        clearPress(press, 0);
        press = null;
        return true;
    }
    function release(candidate: object): void { if (owner === candidate) owner = null; }
    function cancelGestures(): void {
        cancellations.forEach(callbacks => callbacks.forEach(cancel => cancel()));
        document.dispatchEvent(new Event("lwc:gesture-cancel"));
        owner = null;
        clearPress(press, 0);
        press = null;
    }
    document.addEventListener("touchstart", event => {
        multipleTouches = event.touches.length !== 1;
        if (multipleTouches) cancelGestures();
    }, { capture: true, passive: true });
    document.addEventListener("touchend", event => { if (!event.touches.length) multipleTouches = false; }, { capture: true, passive: true });
    document.addEventListener("touchcancel", cancelGestures, { capture: true, passive: true });
    window.addEventListener("blur", cancelGestures);
    document.addEventListener("keydown", cancelGestures, { capture: true });
    document.addEventListener("contextmenu", cancelGestures, { capture: true });
    document.addEventListener("pointercancel", cancelGestures, { capture: true, passive: true });
    document.addEventListener("visibilitychange", () => { if (document.hidden) cancelGestures(); });
    document.addEventListener("selectionchange", () => { if (!window.getSelection()?.isCollapsed) cancelGestures(); });

    function isTouch(): boolean {
        return root.dataset["lwcTouch"] === "touch";
    }

    function supportsTouch(): boolean {
        return coarse.matches || anyCoarse.matches || navigator.maxTouchPoints > 0;
    }

    function token(name: string, fallback: number): number {
        const raw = getComputedStyle(root).getPropertyValue(name).trim();
        const value = parseFloat(raw) * (raw.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(value) && value >= 0 ? value : fallback;
    }

    function ease(name: string): string {
        return getComputedStyle(root).getPropertyValue(name).trim() || "ease";
    }

    function moves(): boolean {
        return !reduced.matches;
    }

    function visible(element: Element): element is HTMLElement {
        if (!(element instanceof HTMLElement) || element.hidden) return false;
        const rect = element.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 && getComputedStyle(element).visibility !== "hidden";
    }

    function scrollsSideways(from: Element | null, stop: Element | null): boolean {
        for (let node = from; node && node !== stop && node !== document.body; node = node.parentElement) {
            if (!(node instanceof HTMLElement) || node.scrollWidth <= node.clientWidth + 1) continue;
            const overflow = getComputedStyle(node).overflowX;
            if (overflow === "auto" || overflow === "scroll") return true;
        }
        return false;
    }

    // A nested scroll region keeps its whole gesture, including at its boundary.
    // Never turn the tail of a native scroll into a page refresh or dismissal.
    function scrollsVertically(from: Element | null, stop: Element | null): boolean {
        for (let node = from; node && node !== stop && node !== document.body; node = node.parentElement) {
            if (node.scrollHeight > node.clientHeight + 1 && /auto|scroll/.test(getComputedStyle(node).overflowY)) return true;
        }
        return false;
    }

    // Recognize before the browser hands a pan to its compositor. Setting
    // touch-action on a whole main/card also disables nested native sliders;
    // a non-passive move listener can instead claim only a deliberate gesture.
    function gestureListener(element: HTMLElement, type: string, handler: (event: PointerEvent) => void, _options?: AddEventListenerOptions): void {
        const touchType: Record<string, string> = { pointerdown: 'touchstart', pointermove: 'touchmove', pointerup: 'touchend', pointercancel: 'touchcancel' };
        element.addEventListener(type, event => {
            if (!event.isTrusted) handler(event as PointerEvent);
        });
        const mapped = touchType[type];
        if (!mapped) return;
        element.addEventListener(mapped, raw => {
            const event = raw as TouchEvent;
            const touch = event.changedTouches[0];
            if (!touch) return;
            if (type === 'pointermove' && !event.cancelable) {
                // Native controls and the profile's pointer gesture can still
                // own this stream. Only abandon this surface's recognizers.
                cancellations.get(element)?.forEach(cancel => cancel());
                return;
            }
            const pointer = new PointerEvent(type, {
                pointerType: 'touch', pointerId: touch.identifier + 1, isPrimary: event.touches.length <= 1,
                clientX: touch.clientX, clientY: touch.clientY
            });
            Object.defineProperty(pointer, 'target', { value: event.target });
            handler(pointer);
            if (type === 'pointermove' && owner === element && event.touches.length === 1 && event.cancelable) event.preventDefault();
        }, { passive: type !== 'pointermove' });
    }

    // ---------------------------------------------------------------- mode
    function syncMode(): void {
        if (touchOnly.matches) root.dataset["lwcTouch"] = "touch";
        else if (coarse.matches) root.dataset["lwcTouch"] = "hybrid";
        else delete root.dataset["lwcTouch"];
    }

    syncMode();
    touchOnly.addEventListener("change", () => { cancelGestures(); syncMode(); refresh(); });
    coarse.addEventListener("change", () => { cancelGestures(); syncMode(); refresh(); });
    anyCoarse.addEventListener("change", refresh);

    // -------------------------------------------------------- sticky hover
    // :hover on a touch-only screen latches after a tap. Rewriting it to
    // :not(*) keeps every rule's other branches (focus, active) intact.
    const originalSelectors = new Map<CSSStyleRule, string>();

    function stripRules(rules: CSSRuleList): void {
        for (const rule of Array.from(rules)) {
            if (rule instanceof CSSStyleRule && rule.selectorText.includes(":hover")) {
                if (!originalSelectors.has(rule)) originalSelectors.set(rule, rule.selectorText);
                rule.selectorText = rule.selectorText.replace(/:hover\b/g, ":not(*)");
            }
            if ("cssRules" in rule && (rule as CSSGroupingRule).cssRules) {
                stripRules((rule as CSSGroupingRule).cssRules);
            }
        }
    }

    function stripHover(): void {
        if (!touchOnly.matches) {
            originalSelectors.forEach((selector, rule) => { rule.selectorText = selector; });
            originalSelectors.clear();
            return;
        }
        for (const sheet of Array.from(document.styleSheets)) {
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                continue; // Cross-origin sheets (icon fonts) carry no hover states we own.
            }
            stripRules(rules);
        }
    }

    // --------------------------------------------------------------- press
    interface Press {
        readonly id: number;
        readonly target: HTMLElement;
        readonly x: number;
        readonly y: number;
        timer: number;
        shown: boolean;
    }

    let press: Press | null = null;

    function pressKind(target: HTMLElement): string {
        if (target.dataset["lwcTap"]) return target.dataset["lwcTap"]!;
        if (target.matches("tr")) return "row";
        const rect = target.getBoundingClientRect();
        return rect.width >= 160 && rect.height >= 56 ? "card" : "control";
    }

    function showPress(state: Press): void {
        state.shown = true;
        state.target.dataset["lwcPressed"] = pressKind(state.target);
    }

    function clearPress(state: Press | null, linger: number): void {
        if (!state) return;
        window.clearTimeout(state.timer);
        const { target } = state;
        if (linger > 0) {
            window.setTimeout(() => { if (press?.target !== target) delete target.dataset["lwcPressed"]; }, linger);
        } else {
            delete target.dataset["lwcPressed"];
        }
    }

    document.addEventListener("pointerdown", event => {
        if (event.pointerType !== "touch" || !event.isPrimary || multipleTouches || !(event.target instanceof Element)) return;
        clearPress(press, 0);
        const target = event.target.closest<HTMLElement>(tappable);
        if (!target || target.matches(notTappable) || target.closest("[inert]")) { press = null; return; }
        const state: Press = { id: event.pointerId, target, x: event.clientX, y: event.clientY, timer: 0, shown: false };
        state.timer = window.setTimeout(() => showPress(state), pressDelay);
        press = state;
    }, { capture: true, passive: true });

    document.addEventListener("pointermove", event => {
        if (!press || event.pointerId !== press.id) return;
        if (Math.hypot(event.clientX - press.x, event.clientY - press.y) > slop) {
            clearPress(press, 0);
            press = null;
        }
    }, { capture: true, passive: true });

    document.addEventListener("pointerup", event => {
        if (!press || event.pointerId !== press.id) return;
        const state = press;
        press = null;
        window.clearTimeout(state.timer);
        // A quick tap still shows the press, briefly, so the tap reads as received.
        if (!state.shown) showPress(state);
        clearPress(state, pressHold);
    }, { capture: true, passive: true });

    for (const type of ["pointercancel", "lostpointercapture", "scroll", "dragstart"]) {
        document.addEventListener(type, () => { clearPress(press, 0); press = null; }, { capture: true, passive: true });
    }

    // iOS only paints :active when a touch listener exists.
    document.addEventListener("touchstart", () => undefined, { passive: true });

    // ----------------------------------------------------------- hit areas
    function growTargets(scope: ParentNode): void {
        if (!coarse.matches) return;
        document.querySelectorAll<HTMLElement>('[data-lwc-hit]').forEach(element => {
            delete element.dataset['lwcHit'];
            delete element.dataset['lwcHitAnchor'];
        });
        const targets = Array.from(document.querySelectorAll<HTMLElement>(tappable)).filter(element => visible(element) && !element.closest('[inert]'));
        const boxes = targets.map(element => ({ element, rect: element.getBoundingClientRect() }));
        for (const element of Array.from(scope.querySelectorAll<HTMLElement>(smallControls))) {
            if (!visible(element) || element.closest('[inert]')) continue;
            const rect = element.getBoundingClientRect();
            if (rect.width >= minTarget && rect.height >= minTarget) continue;
            const padX = Math.min(10, Math.max(0, (minTarget - rect.width) / 2));
            const padY = Math.min(10, Math.max(0, (minTarget - rect.height) / 2));
            if (boxes.some(({ element: other, rect: box }) => {
                if (other === element || other.contains(element) || element.contains(other)) return false;
                const x = Math.min(10, Math.max(0, (minTarget - box.width) / 2));
                const y = Math.min(10, Math.max(0, (minTarget - box.height) / 2));
                return rect.left - padX < box.right + x && rect.right + padX > box.left - x
                    && rect.top - padY < box.bottom + y && rect.bottom + padY > box.top - y;
            })) continue;
            const style = getComputedStyle(element);
            if (style.display === "inline" || style.display === "contents") continue;
            const free = (pseudo: string) => getComputedStyle(element, pseudo).content === "none";
            const slot = free("::after") ? "after" : free("::before") ? "before" : "";
            if (!slot) continue;
            element.dataset["lwcHit"] = slot;
            if (style.position === "static") element.dataset["lwcHitAnchor"] = "";
        }
    }

    // ------------------------------------------------------------ scrollers
    const cleanups = new Map<HTMLElement, Array<() => void>>();
    function cleanupFor(element: HTMLElement, cleanup: () => void): void {
        const existing = cleanups.get(element) ?? [];
        existing.push(cleanup);
        cleanups.set(element, existing);
    }
    function cleanRemoved(): void {
        cancellations.forEach((callbacks, element) => {
            if (element.isConnected) return;
            callbacks.forEach(cancel => cancel());
            cancellations.delete(element);
        });
        cleanups.forEach((callbacks, element) => {
            if (element.isConnected) return;
            callbacks.forEach(callback => callback());
            cleanups.delete(element);
        });
    }
    const scrollers = new WeakSet<HTMLElement>();
    const resize = new ResizeObserver(entries => {
        for (const entry of entries) if (entry.target instanceof HTMLElement) edges(entry.target);
    });

    function edges(element: HTMLElement): void {
        const max = element.scrollWidth - element.clientWidth;
        if (max <= 1) {
            delete element.dataset["lwcFade"];
            return;
        }
        const start = Math.abs(element.scrollLeft) > 2;
        const end = Math.abs(element.scrollLeft) < max - 2;
        element.dataset["lwcFade"] = start && end ? "both" : start ? "start" : end ? "end" : "none";
    }

    function scroller(element: HTMLElement): void {
        if (scrollers.has(element)) return;
        scrollers.add(element);
        element.dataset["lwcScroller"] ??= "";
        const update = () => edges(element);
        element.addEventListener("scroll", update, { passive: true });
        cleanupFor(element, () => { resize.unobserve(element); element.removeEventListener("scroll", update); scrollers.delete(element); });
        resize.observe(element);
        edges(element);
    }

    function findScrollers(scope: ParentNode): void {
        for (const element of Array.from(scope.querySelectorAll<HTMLElement>(scrollerSelectors))) scroller(element);
    }

    // --------------------------------------------------------- swipe views
    // Horizontal swipes step through sibling views. Row FLIP retains ownership
    // of layout movement; a restrained fade communicates the finger's intent.
    function views(surface: HTMLElement, options: LwcTouchViewsOptions): void {
        if (surface.dataset["lwcSwipeViews"] !== undefined) return;
        surface.dataset["lwcSwipeViews"] = "";
        let drag: { id: number; x: number; y: number; t: number; dx: number; active: boolean } | null = null;

        gestureListener(surface, "pointerdown", event => {
            registerCancellation(surface, cancel);
            if (event.pointerType !== "touch" || !event.isPrimary || multipleTouches || !(event.target instanceof Element) || !window.getSelection()?.isCollapsed) return;
            if (event.clientX < 20 || event.clientX > innerWidth - 20 || event.target.closest(gestureExclusions) || scrollsSideways(event.target, surface.parentElement)) return;
            drag = { id: event.pointerId, x: event.clientX, y: event.clientY, t: performance.now(), dx: 0, active: false };
        }, { passive: true });

        gestureListener(surface, "pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.active) {
                if (Math.abs(dy) > slop && Math.abs(dy) > Math.abs(dx)) { drag = null; return; }
                if (Math.abs(dx) < slop * 1.6 || Math.abs(dx) < Math.abs(dy) * 1.5) return;
                if (!claim(surface)) { drag = null; return; }
                drag.active = true;
                surface.dataset["lwcSwiping"] = "";
            }
            drag.dx = dx;
            if (!moves()) return;
            surface.style.opacity = String(1 - Math.min(0.25, Math.abs(dx) / 600));
        }, { passive: true });

        const settle = (event: PointerEvent) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { dx, t, active } = drag;
            drag = null;
            release(surface);
            if (!active) return;
            delete surface.dataset["lwcSwiping"];
            const velocity = Math.abs(dx) / Math.max(1, performance.now() - t);
            const items = options.items();
            const index = options.current();
            const step = event.type === "pointerup" && (Math.abs(dx) > 64 || (Math.abs(dx) > 28 && velocity > 0.45)) ? (dx < 0 ? 1 : -1) : 0;
            const next = items[index + step];
            const opacity = surface.style.opacity || "1";
            surface.style.opacity = "";
            if (!step || !next) {
                if (moves()) surface.animate([{ opacity }, { opacity: 1 }], { duration: token("--lwc-motion-base", 200), easing: ease("--lwc-ease-pop") });
                return;
            }
            options.activate(next);

        };
        gestureListener(surface, "pointerup", settle);
        gestureListener(surface, "pointercancel", settle);
        surface.addEventListener("lostpointercapture", event => {
            if (event.isTrusted) return; // Native implicit capture ends before touchend.
            if (drag && !surface.hasPointerCapture(event.pointerId)) settle(event);
        });
        const cancel = () => {
            if (drag) settle(new PointerEvent("pointercancel", { pointerId: drag.id }));
        };
        registerCancellation(surface, cancel);
        surface.addEventListener("touchmove", event => {
            if (drag?.active && event.touches.length === 1 && event.cancelable) event.preventDefault();
        }, { passive: false });

    }

    // ---------------------------------------------------------- swipe back
    // A rightward swipe on a flow screen presses its visible Back button. A
    // round chevron follows the finger from the left edge and pops when the
    // swipe is far enough to count.
    const backSelector = ".back-button, [data-lwc-swipe-back-target]";
    let backPeek: HTMLElement | null = null;

    function currentBack(): HTMLElement | null {
        for (const button of Array.from(document.querySelectorAll<HTMLElement>(backSelector))) {
            if (button.matches(":disabled, [aria-disabled='true']") || button.closest("[hidden], [inert]")) continue;
            if (visible(button)) return button;
        }
        return null;
    }

    function swipeBack(): void {
        const main = document.querySelector<HTMLElement>("main");
        if (!main || main.dataset["lwcSwipeBack"] !== undefined || !document.querySelector(backSelector)) return;
        main.dataset["lwcSwipeBack"] = "";
        let drag: { id: number; x: number; y: number; t: number; dx: number; active: boolean; button: HTMLElement } | null = null;
        const threshold = 88;

        gestureListener(main, "pointerdown", event => {
            registerCancellation(main, cancel);
            if (event.pointerType !== "touch" || !event.isPrimary || multipleTouches || !(event.target instanceof Element) || !window.getSelection()?.isCollapsed) return;
            // Leave the outermost strip to the browser's own back gesture.
            if (event.clientX < 20 || event.clientX > innerWidth - 20 || event.target.closest(gestureExclusions) || event.target.closest("[data-lwc-swipe-views], dialog, [role='dialog']")) return;
            if (scrollsSideways(event.target, main.parentElement)
                || Array.from(document.querySelectorAll("[aria-modal='true']:not([hidden]), dialog[open]")).some(visible)) return;
            const button = currentBack();
            if (!button) return;
            drag = { id: event.pointerId, x: event.clientX, y: event.clientY, t: performance.now(), dx: 0, active: false, button };
        }, { passive: true });

        gestureListener(main, "pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.active) {
                if ((Math.abs(dy) > slop && Math.abs(dy) > Math.abs(dx)) || dx < -slop) { drag = null; return; }
                if (dx < slop * 1.6 || dx < Math.abs(dy) * 1.5) return;
                if (!claim(main)) { drag = null; return; }
                drag.active = true;
                backPeek ??= document.body.appendChild(Object.assign(document.createElement("div"), { className: "lwc-swipe-back-peek" }));
                backPeek.setAttribute("aria-hidden", "true");
                backPeek.style.top = `${Math.round(event.clientY)}px`;
                backPeek.dataset["state"] = "pull";
            }
            drag.dx = Math.max(0, dx);
            const progress = Math.min(1, drag.dx / threshold);
            if (!backPeek) return;
            backPeek.style.setProperty("--lwc-peek", String(progress));
            backPeek.dataset["state"] = progress >= 1 ? "armed" : "pull";
        }, { passive: true });

        const settle = (event: PointerEvent) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { dx, t, active, button } = drag;
            drag = null;
            release(main);
            if (!active) return;
            const velocity = dx / Math.max(1, performance.now() - t);
            const go = event.type === "pointerup" && (dx >= threshold || (dx > 40 && velocity > 0.5));
            if (backPeek) {
                backPeek.dataset["state"] = go ? "go" : "rest";
                backPeek.style.setProperty("--lwc-peek", "0");
            }
            if (go) {
                if (navigator.vibrate) navigator.vibrate(4);
                button.click();
            }
        };
        gestureListener(main, "pointerup", settle);
        gestureListener(main, "pointercancel", settle);
        main.addEventListener("lostpointercapture", event => {
            if (event.isTrusted) return;
            if (drag && !main.hasPointerCapture(event.pointerId)) settle(event);
        });
        const cancel = () => {
            if (drag) settle(new PointerEvent("pointercancel", { pointerId: drag.id }));
        };
        registerCancellation(main, cancel);
        main.addEventListener("touchmove", event => {
            if (drag?.active && event.touches.length === 1 && event.cancelable) event.preventDefault();
        }, { passive: false });

    }

    // --------------------------------------------------------------- sheets
    // Bottom sheets follow a downward drag from their top (or from anywhere
    // once their content is scrolled to the top) and close past a third of
    // their height or on a flick. The backdrop fades with the drag.
    function sheet(overlay: HTMLElement, options: LwcTouchSheetOptions): void {
        if (overlay.dataset["lwcSheet"] !== undefined) return;
        overlay.dataset["lwcSheet"] = "";
        let drag: { id: number; y: number; x: number; t: number; dy: number; offset: number; active: boolean; panel: HTMLElement } | null = null;
        let returnMotion: Animation | null = null;
        const cancelReturn = () => {
            returnMotion?.cancel();
            returnMotion = null;
        };

        gestureListener(overlay, "pointerdown", event => {
            registerCancellation(overlay, cancel);
            if (event.pointerType !== "touch" || !event.isPrimary || multipleTouches || !options.isOpen() || !(event.target instanceof Element)) return;
            const panel = options.panel();
            if (!panel || !panel.contains(event.target) || event.target.closest(gestureExclusions)) return;
            const scroller = options.scroller?.() ?? panel;
            if (scroller.scrollTop > 0 || scrollsVertically(event.target, scroller) || scrollsSideways(event.target, panel)) return;
            const offset = returnMotion ? Math.max(0, parseFloat(getComputedStyle(panel).translate.split(/\s+/)[1] || "0") || 0) : 0;
            if (offset && !claim(overlay)) return;
            cancelReturn();
            // Re-grabbing a returning sheet takes over its painted position,
            // without snapping it home or leaving the old animation in control.
            if (offset) {
                panel.style.translate = `0 ${offset}px`;
                overlay.dataset["lwcSheetDragging"] = "";
            }
            drag = { id: event.pointerId, y: event.clientY, x: event.clientX, t: performance.now(), dy: offset, offset, active: offset > 0, panel };
        }, { passive: true });

        // A drag that starts at the top of the content would otherwise scroll
        // the page behind; claim it once it is clearly a downward pull.
        overlay.addEventListener("touchmove", event => {
            if (drag?.active && event.cancelable) event.preventDefault();
        }, { passive: false });

        gestureListener(overlay, "pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dy = event.clientY - drag.y;
            const dx = event.clientX - drag.x;
            if (!drag.active) {
                if (dy < -slop || (Math.abs(dx) > slop && Math.abs(dx) > Math.abs(dy))) { drag = null; return; }
                if (dy < slop || dy < Math.abs(dx) * 1.5) return;
                if (!claim(overlay)) { drag = null; return; }
                drag.active = true;
                overlay.dataset["lwcSheetDragging"] = "";
            }
            drag.dy = Math.max(0, drag.offset + dy - (drag.offset ? 0 : slop));
            const height = drag.panel.getBoundingClientRect().height || 1;
            const progress = Math.min(1, drag.dy / height);
            overlay.style.setProperty("--lwc-sheet-progress", String(progress));
            if (moves()) drag.panel.style.translate = `0 ${drag.dy}px`;
        }, { passive: true });

        const settle = (event: PointerEvent) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { dy, offset, t, active, panel, id } = drag;
            drag = null;
            release(overlay);
            if (panel.hasPointerCapture(id)) panel.releasePointerCapture(id);
            if (!active) return;
            delete overlay.dataset["lwcSheetDragging"];
            const height = panel.getBoundingClientRect().height || 1;
            const velocity = Math.max(0, dy - offset) / Math.max(1, performance.now() - t);
            const close = event.type === "pointerup" && (dy > height / 3 || (dy > 48 && velocity > 0.6));
            const from = panel.style.translate || "0 0";
            panel.style.translate = "";
            overlay.style.removeProperty("--lwc-sheet-progress");
            if (close) {
                options.dismiss();
                return;
            }
            if (moves() && typeof panel.animate === "function") {
                try {
                    const animation = panel.animate([{ translate: from }, { translate: "0 0" }], { duration: token("--lwc-motion-move", 260), easing: ease("--lwc-ease-pop") });
                    returnMotion = animation;
                    const releaseMotion = () => { if (returnMotion === animation) returnMotion = null; };
                    void animation.finished.then(releaseMotion, releaseMotion);
                } catch { /* The gesture is already settled; decoration is optional. */ }
            }
        };
        gestureListener(overlay, "pointerup", settle);
        gestureListener(overlay, "pointercancel", settle);
        overlay.addEventListener("lostpointercapture", event => {
            if (event.isTrusted) return;
            if (drag && !overlay.hasPointerCapture(event.pointerId)
                && !("panel" in drag && drag.panel instanceof HTMLElement && drag.panel.hasPointerCapture(event.pointerId))) settle(event);
        });
        const cancel = () => {
            if (drag) settle(new PointerEvent("pointercancel", { pointerId: drag.id }));
            cancelReturn();
        };
        registerCancellation(overlay, cancel);
        overlay.addEventListener("athlete-dialog-closing", cancel);

    }

    // ---------------------------------------------------------------- pull
    // At the top of the page, pulling the board or feed down draws a ring
    // that fills with the pull and spins once released. Pages that can
    // reload their data listen for lwc:pull-refresh and pass their promise to
    // waitUntil; elsewhere the ring spins briefly and settles.
    function pull(surface: HTMLElement): void {
        if (surface.dataset["lwcPull"] !== undefined) return;
        surface.dataset["lwcPull"] = "";
        const ring = document.createElement("div");
        ring.className = "lwc-pull-ring";
        ring.setAttribute("aria-hidden", "true");
        ring.innerHTML = '<svg viewBox="0 0 36 36" focusable="false"><circle class="lwc-pull-ring__track" cx="18" cy="18" r="13"/><circle class="lwc-pull-ring__arc" cx="18" cy="18" r="13" pathLength="100"/></svg>';
        surface.prepend(ring);
        const threshold = 76;
        let start: { y: number; x: number } | null = null;
        let distance = 0;
        let pulling = false;
        let busy = false;

        const draw = () => {
            const eased = threshold * (1 - Math.exp(-distance / threshold)) * 1.25;
            surface.style.setProperty("--lwc-pull", `${Math.round(eased)}px`);
            surface.style.setProperty("--lwc-pull-progress", String(Math.min(1, distance / threshold)));
            surface.dataset["lwcPulling"] = distance >= threshold ? "armed" : "pull";
        };

        surface.addEventListener("touchstart", event => {
            registerCancellation(surface, cancel);
            const touch = event.touches[0];
            if (busy || event.touches.length !== 1 || !touch || !(event.target instanceof Element)) { start = null; return; }
            if (window.scrollY > 0 || scrollsVertically(event.target, surface) || scrollsSideways(event.target, surface) || !window.getSelection()?.isCollapsed) return;
            if (event.target.closest(gestureExclusions) || document.querySelector("[aria-modal='true']:not([hidden]), dialog[open]")?.contains(event.target)) return;
            start = { y: touch.clientY, x: touch.clientX };
            distance = 0;
            pulling = false;
        }, { passive: true });

        surface.addEventListener("touchmove", event => {
            const touch = event.touches[0];
            if (!start || !touch || event.touches.length !== 1) return;
            const dy = touch.clientY - start.y;
            const dx = touch.clientX - start.x;
            if (!pulling) {
                // Scrolling back up to the top turns into a pull without lifting the finger.
                if (window.scrollY > 0) { start = null; return; }
                if (dy < 0 || Math.abs(dx) > Math.abs(dy)) { if (Math.abs(dy) > slop || Math.abs(dx) > slop) start = null; return; }
                if (dy < slop || dy < Math.abs(dx) * 1.5) return;
                if (!claim(surface)) { start = null; return; }
                pulling = true;
            }
            if (event.cancelable) event.preventDefault();
            distance = Math.max(0, dy - slop);
            draw();
        }, { passive: false });

        const finishPull = (cancelled = false) => {
            release(surface);
            if (!start) return;
            start = null;
            if (!pulling) return;
            pulling = false;
            if (cancelled || distance < threshold) {
                // Spring back first; dropping the state ends the translate.
                distance = 0;
                surface.dataset["lwcPulling"] = "done";
                surface.style.removeProperty("--lwc-pull");
                surface.style.removeProperty("--lwc-pull-progress");
                window.setTimeout(() => { if (surface.dataset["lwcPulling"] === "done") delete surface.dataset["lwcPulling"]; }, token("--lwc-motion-move", 260));
                return;
            }
            busy = true;
            if (navigator.vibrate) navigator.vibrate(6);
            surface.dataset["lwcPulling"] = "refreshing";
            surface.style.setProperty("--lwc-pull", "56px");
            const work: Promise<unknown>[] = [];
            const detail: LwcTouchPullDetail = { waitUntil: promise => { work.push(promise.catch(() => undefined)); } };
            surface.dispatchEvent(new CustomEvent<LwcTouchPullDetail>("lwc:pull-refresh", { bubbles: true, detail }));
            Promise.all(work).then(() => {
                surface.dataset["lwcPulling"] = "done";
                surface.style.removeProperty("--lwc-pull");
                surface.style.removeProperty("--lwc-pull-progress");
                window.setTimeout(() => { delete surface.dataset["lwcPulling"]; busy = false; distance = 0; }, token("--lwc-motion-move", 260));
            });
        };
        surface.addEventListener("touchend", event => finishPull(event.touches.length !== 0), { passive: true });
        // Cancellation belongs to native scrolling, pinch or interruption.
        // It must never count as a completed refresh.
        surface.addEventListener("touchcancel", () => finishPull(true), { passive: true });
        const cancel = () => finishPull(true);
        registerCancellation(surface, cancel);
    }

    // ------------------------------------------------------------- opt-ins
    // Surfaces that exist on master today. Pages under redesign can opt in
    // with the same calls (see the TODO list in the PR description).
    function adopt(): void {
        // Ranking views on the leaderboard: swipe the table to the neighbouring chip.
        const chips = document.querySelector<HTMLElement>(".view-switcher-chips");
        const board = document.querySelector<HTMLElement>("#leaderboardTable, .leaderboard table")?.closest<HTMLElement>(".leaderboard, .table-container, .leaderboard-table-wrap") ?? null;
        if (chips && board) {
            const labels = () => Array.from(chips.querySelectorAll<HTMLLabelElement>("label.view-badge")).filter(visible);
            views(board, {
                items: labels,
                current: () => labels().findIndex(label => label.control instanceof HTMLInputElement && label.control.checked),
                activate: label => label.click(),
            });
        }

        // The athlete profile on phones is a sheet: drag it down to close.
        const modal = document.getElementById("detailsModal");
        const close = document.getElementById("closeAthleteDetailsModal");
        if (modal && close) {
            sheet(modal, {
                panel: () => modal.querySelector<HTMLElement>(".modal-content"),
                isOpen: () => getComputedStyle(modal).display !== "none" && !modal.hidden && window.innerWidth <= 600,
                dismiss: () => close.click(),
            });
        }

        // The phone alert already rises from the bottom; it now drags away too.
        const alert = document.getElementById("custom-alert");
        const ok = document.getElementById("custom-alert-close");
        if (alert && ok) {
            sheet(alert, {
                panel: () => alert.querySelector<HTMLElement>(".custom-alert-panel"),
                isOpen: () => !alert.hidden && window.innerWidth <= 600,
                dismiss: () => ok.click(),
            });
        }

        // The board and the feed pull to refresh.
        const feed = document.querySelector<HTMLElement>("main.event-board-page");
        const fullBoard = document.querySelector<HTMLElement>("main[data-leaderboard-page='full']");
        for (const surface of [feed, fullBoard]) if (surface?.hasAttribute("data-lwc-refresh-ready")) pull(surface);

        swipeBack();
    }

    let pending = false;
    function refresh(): void {
        if (pending) return;
        pending = true;
        requestAnimationFrame(() => {
            pending = false;
            stripHover();
            if (!supportsTouch()) return;
            growTargets(document);
            findScrollers(document);
            adopt();
        });
    }

    // Scope ordinary additions; a full scan is reserved for explicit refresh,
    // viewport/input changes, and initial load.
    const additions = new Set<Element>();
    let mutationFrame = 0;
    const observer = new MutationObserver(records => {
        cleanRemoved();
        if (!supportsTouch()) return;
        for (const record of records) for (const node of record.addedNodes) {
            if (!(node instanceof Element)) continue;
            if (node.matches("link[rel=stylesheet], style") || node.querySelector("link[rel=stylesheet], style")) {
                stripHover();
                for (const link of [node, ...node.querySelectorAll("link[rel=stylesheet]")]) {
                    if (link instanceof HTMLLinkElement) link.addEventListener("load", stripHover, { once: true });
                }
            }
            additions.add(node);
        }
        if (!additions.size || mutationFrame) return;
        mutationFrame = requestAnimationFrame(() => {
            mutationFrame = 0;
            let controlsChanged = false;
            additions.forEach(node => {
                if (!node.isConnected) return;
                const scope = node.parentElement ?? node;
                if (node.matches(smallControls) || node.querySelector(smallControls)) controlsChanged = true;
                if (node.matches(scrollerSelectors) || node.querySelector(scrollerSelectors)) findScrollers(scope);
            });
            if (controlsChanged) growTargets(document);
            additions.clear();
            adopt();
        });
    });
    observer.observe(root, { childList: true, subtree: true });
    window.addEventListener("resize", refresh, { passive: true });
    reduced.addEventListener("change", cancelGestures);

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", refresh, { once: true });
    else refresh();
    window.addEventListener("load", refresh, { once: true });

    window.LwcTouch = { claim, release, isTouch, views, sheet, pull, scroller, refresh };
})();
