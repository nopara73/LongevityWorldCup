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
    const touchOnly = window.matchMedia("(hover: none) and (pointer: coarse)");
    const coarse = window.matchMedia("(pointer: coarse)");
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    const tappable = [
        "a[href]", "button", "summary", "label[for]", "label.view-badge",
        "[role='button']", "[role='tab']", "[role='link']", "[data-lwc-tap]",
        ".leaderboard table tbody tr", ".podium-item", ".option-button",
        "input[type='submit']", "input[type='button']",
    ].join(", ");
    const notTappable = ":disabled, [aria-disabled='true'], .no-results, .tier-separator, .leaderboard-skeleton-row, .events-skeleton-row";
    // Gestures never start on something that already owns a drag.
    const gestureExclusions = "input:not([type='radio'], [type='checkbox']), select, textarea, canvas, iframe, video, [contenteditable]:not([contenteditable='false']), [role='slider'], [data-lwc-no-gesture]";
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

    function isTouch(): boolean {
        return root.dataset["lwcTouch"] === "touch";
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

    // ---------------------------------------------------------------- mode
    function syncMode(): void {
        if (touchOnly.matches) root.dataset["lwcTouch"] = "touch";
        else if (coarse.matches) root.dataset["lwcTouch"] = "hybrid";
        else delete root.dataset["lwcTouch"];
    }

    syncMode();
    touchOnly.addEventListener("change", () => { syncMode(); refresh(); });
    coarse.addEventListener("change", () => { syncMode(); refresh(); });

    // -------------------------------------------------------- sticky hover
    // :hover on a touch-only screen latches after a tap. Rewriting it to
    // :not(*) keeps every rule's other branches (focus, active) intact.
    const strippedSheets = new WeakSet<CSSStyleSheet>();

    function stripRules(rules: CSSRuleList): void {
        for (const rule of Array.from(rules)) {
            if (rule instanceof CSSStyleRule && rule.selectorText.includes(":hover")) {
                rule.selectorText = rule.selectorText.replace(/:hover\b/g, ":not(*)");
            }
            if ("cssRules" in rule && (rule as CSSGroupingRule).cssRules) {
                stripRules((rule as CSSGroupingRule).cssRules);
            }
        }
    }

    function stripHover(): void {
        if (!touchOnly.matches) return;
        for (const sheet of Array.from(document.styleSheets)) {
            if (strippedSheets.has(sheet)) continue;
            let rules: CSSRuleList;
            try {
                rules = sheet.cssRules;
            } catch {
                continue; // Cross-origin sheets (icon fonts) carry no hover states we own.
            }
            strippedSheets.add(sheet);
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
        if (event.pointerType !== "touch" || !event.isPrimary || !(event.target instanceof Element)) return;
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

    for (const type of ["pointercancel", "scroll", "dragstart"]) {
        document.addEventListener(type, () => { clearPress(press, 0); press = null; }, { capture: true, passive: true });
    }

    // iOS only paints :active when a touch listener exists.
    document.addEventListener("touchstart", () => undefined, { passive: true });

    // ----------------------------------------------------------- hit areas
    function growTargets(scope: ParentNode): void {
        if (!coarse.matches) return;
        for (const element of Array.from(scope.querySelectorAll<HTMLElement>(smallControls))) {
            if (element.dataset["lwcHit"] !== undefined || !visible(element)) continue;
            const rect = element.getBoundingClientRect();
            if (rect.width >= minTarget && rect.height >= minTarget) continue;
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
        element.addEventListener("scroll", () => edges(element), { passive: true });
        resize.observe(element);
        edges(element);
    }

    function findScrollers(scope: ParentNode): void {
        for (const element of Array.from(scope.querySelectorAll<HTMLElement>(scrollerSelectors))) scroller(element);
    }

    // --------------------------------------------------------- swipe views
    // Horizontal swipes on a surface step through sibling views. The surface
    // follows the finger, the next view slides in from the side it came from.
    function views(surface: HTMLElement, options: LwcTouchViewsOptions): void {
        if (surface.dataset["lwcSwipeViews"] !== undefined) return;
        surface.dataset["lwcSwipeViews"] = "";
        let drag: { id: number; x: number; y: number; t: number; dx: number; active: boolean } | null = null;

        surface.addEventListener("pointerdown", event => {
            if (event.pointerType !== "touch" || !event.isPrimary || !(event.target instanceof Element)) return;
            if (event.target.closest(gestureExclusions) || scrollsSideways(event.target, surface.parentElement)) return;
            drag = { id: event.pointerId, x: event.clientX, y: event.clientY, t: performance.now(), dx: 0, active: false };
        }, { passive: true });

        surface.addEventListener("pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.active) {
                if (Math.abs(dy) > slop && Math.abs(dy) > Math.abs(dx)) { drag = null; return; }
                if (Math.abs(dx) < slop * 1.6) return;
                drag.active = true;
                surface.dataset["lwcSwiping"] = "";
            }
            drag.dx = dx;
            if (!moves()) return;
            const items = options.items();
            const index = options.current();
            const edge = (dx > 0 && index <= 0) || (dx < 0 && index >= items.length - 1);
            // Past the last view the surface resists like a rubber band.
            const shift = edge ? dx * 0.18 : Math.sign(dx) * Math.min(Math.abs(dx) * 0.45, 72);
            surface.style.translate = `${shift}px 0`;
            surface.style.opacity = String(1 - Math.min(0.35, Math.abs(shift) / 220));
        }, { passive: true });

        const settle = (event: PointerEvent) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { dx, t, active } = drag;
            drag = null;
            if (!active) return;
            delete surface.dataset["lwcSwiping"];
            const velocity = Math.abs(dx) / Math.max(1, performance.now() - t);
            const items = options.items();
            const index = options.current();
            const step = event.type === "pointerup" && (Math.abs(dx) > 64 || (Math.abs(dx) > 28 && velocity > 0.45)) ? (dx < 0 ? 1 : -1) : 0;
            const next = items[index + step];
            const from = surface.style.translate || "0 0";
            const opacity = surface.style.opacity || "1";
            surface.style.translate = "";
            surface.style.opacity = "";
            if (!step || !next) {
                if (moves()) surface.animate([{ translate: from, opacity }, { translate: "0 0", opacity: 1 }], { duration: token("--lwc-motion-base", 200), easing: ease("--lwc-ease-pop") });
                return;
            }
            options.activate(next);
            if (!moves()) return;
            // The new view arrives from the side the finger pushed towards.
            const enter = `${step * 40}px 0`;
            surface.animate([{ translate: enter, opacity: 0.4 }, { translate: "0 0", opacity: 1 }], { duration: token("--lwc-motion-move", 260), easing: ease("--lwc-ease-enter") });
            if (navigator.vibrate) navigator.vibrate(4);
        };
        surface.addEventListener("pointerup", settle);
        surface.addEventListener("pointercancel", settle);
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

        main.addEventListener("pointerdown", event => {
            if (event.pointerType !== "touch" || !event.isPrimary || !(event.target instanceof Element)) return;
            // Leave the outermost strip to the browser's own back gesture.
            if (event.clientX < 16 || event.target.closest(gestureExclusions) || event.target.closest("[data-lwc-swipe-views], dialog, [role='dialog']")) return;
            if (scrollsSideways(event.target, main.parentElement)) return;
            const button = currentBack();
            if (!button) return;
            drag = { id: event.pointerId, x: event.clientX, y: event.clientY, t: performance.now(), dx: 0, active: false, button };
        }, { passive: true });

        main.addEventListener("pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dx = event.clientX - drag.x;
            const dy = event.clientY - drag.y;
            if (!drag.active) {
                if ((Math.abs(dy) > slop && Math.abs(dy) > Math.abs(dx)) || dx < -slop) { drag = null; return; }
                if (dx < slop * 1.6) return;
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
        main.addEventListener("pointerup", settle);
        main.addEventListener("pointercancel", settle);
    }

    // --------------------------------------------------------------- sheets
    // Bottom sheets follow a downward drag from their top (or from anywhere
    // once their content is scrolled to the top) and close past a third of
    // their height or on a flick. The backdrop fades with the drag.
    function sheet(overlay: HTMLElement, options: LwcTouchSheetOptions): void {
        if (overlay.dataset["lwcSheet"] !== undefined) return;
        overlay.dataset["lwcSheet"] = "";
        let drag: { id: number; y: number; x: number; t: number; dy: number; active: boolean; panel: HTMLElement } | null = null;

        overlay.addEventListener("pointerdown", event => {
            if (event.pointerType !== "touch" || !event.isPrimary || !options.isOpen() || !(event.target instanceof Element)) return;
            const panel = options.panel();
            if (!panel || !panel.contains(event.target) || event.target.closest(gestureExclusions)) return;
            const scroller = options.scroller?.() ?? panel;
            if (scroller.scrollTop > 0) return;
            drag = { id: event.pointerId, y: event.clientY, x: event.clientX, t: performance.now(), dy: 0, active: false, panel };
        }, { passive: true });

        // A drag that starts at the top of the content would otherwise scroll
        // the page behind; claim it once it is clearly a downward pull.
        overlay.addEventListener("touchmove", event => {
            if (drag?.active && event.cancelable) event.preventDefault();
        }, { passive: false });

        overlay.addEventListener("pointermove", event => {
            if (!drag || event.pointerId !== drag.id) return;
            const dy = event.clientY - drag.y;
            const dx = event.clientX - drag.x;
            if (!drag.active) {
                if (dy < -slop || (Math.abs(dx) > slop && Math.abs(dx) > Math.abs(dy))) { drag = null; return; }
                if (dy < slop) return;
                drag.active = true;
                overlay.dataset["lwcSheetDragging"] = "";
                try { drag.panel.setPointerCapture(event.pointerId); } catch { /* synthetic pointers */ }
            }
            drag.dy = Math.max(0, dy - slop);
            const height = drag.panel.getBoundingClientRect().height || 1;
            const progress = Math.min(1, drag.dy / height);
            overlay.style.setProperty("--lwc-sheet-progress", String(progress));
            if (moves()) drag.panel.style.translate = `0 ${drag.dy}px`;
        }, { passive: true });

        const settle = (event: PointerEvent) => {
            if (!drag || event.pointerId !== drag.id) return;
            const { dy, t, active, panel } = drag;
            drag = null;
            if (!active) return;
            delete overlay.dataset["lwcSheetDragging"];
            const height = panel.getBoundingClientRect().height || 1;
            const velocity = dy / Math.max(1, performance.now() - t);
            const close = event.type === "pointerup" && (dy > height / 3 || (dy > 48 && velocity > 0.6));
            const from = panel.style.translate || "0 0";
            panel.style.translate = "";
            overlay.style.removeProperty("--lwc-sheet-progress");
            if (close) {
                if (moves()) {
                    // Finish the throw, then let the dialog's own close run.
                    const remaining = Math.max(90, Math.min(200, (height - dy) / Math.max(velocity, 1.2)));
                    const throwOut = panel.animate([{ translate: from }, { translate: `0 ${height}px` }], { duration: remaining, easing: ease("--lwc-ease-exit"), fill: "forwards" });
                    throwOut.finished.then(() => { options.dismiss(); window.setTimeout(() => throwOut.cancel(), 320); }, () => undefined);
                } else {
                    options.dismiss();
                }
                return;
            }
            if (moves()) panel.animate([{ translate: from }, { translate: "0 0" }], { duration: token("--lwc-motion-move", 260), easing: ease("--lwc-ease-pop") });
        };
        overlay.addEventListener("pointerup", settle);
        overlay.addEventListener("pointercancel", settle);
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
            const touch = event.touches[0];
            if (busy || event.touches.length !== 1 || !touch || !(event.target instanceof Element)) return;
            if (event.target.closest(gestureExclusions) || document.querySelector("[aria-modal='true']:not([hidden]), dialog[open]")?.contains(event.target)) return;
            start = { y: touch.clientY, x: touch.clientX };
            distance = 0;
            pulling = false;
        }, { passive: true });

        surface.addEventListener("touchmove", event => {
            const touch = event.touches[0];
            if (!start || !touch) return;
            const dy = touch.clientY - start.y;
            const dx = touch.clientX - start.x;
            if (!pulling) {
                // Scrolling back up to the top turns into a pull without lifting the finger.
                if (window.scrollY > 0) { start = { y: touch.clientY, x: touch.clientX }; return; }
                if (dy < 0 || Math.abs(dx) > Math.abs(dy)) { if (Math.abs(dy) > slop || Math.abs(dx) > slop) start = null; return; }
                if (dy < slop) return;
                pulling = true;
            }
            if (event.cancelable) event.preventDefault();
            distance = Math.max(0, dy - slop);
            draw();
        }, { passive: false });

        const release = () => {
            if (!start) return;
            start = null;
            if (!pulling) return;
            pulling = false;
            if (distance < threshold) {
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
            const work: Promise<unknown>[] = [new Promise(resolve => window.setTimeout(resolve, 650))];
            const detail: LwcTouchPullDetail = { waitUntil: promise => { work.push(promise.catch(() => undefined)); } };
            surface.dispatchEvent(new CustomEvent<LwcTouchPullDetail>("lwc:pull-refresh", { bubbles: true, detail }));
            Promise.all(work).then(() => {
                surface.dataset["lwcPulling"] = "done";
                surface.style.removeProperty("--lwc-pull");
                surface.style.removeProperty("--lwc-pull-progress");
                window.setTimeout(() => { delete surface.dataset["lwcPulling"]; busy = false; distance = 0; }, token("--lwc-motion-move", 260));
            });
        };
        surface.addEventListener("touchend", release, { passive: true });
        // Chrome cancels the touch when it hands a gesture that began as a scroll
        // to the compositor; an armed pull still counts, a partial one springs back.
        surface.addEventListener("touchcancel", release, { passive: true });
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
        for (const surface of [feed, fullBoard]) if (surface) pull(surface);

        swipeBack();
    }

    let pending = false;
    function refresh(): void {
        if (pending) return;
        pending = true;
        requestAnimationFrame(() => {
            pending = false;
            stripHover();
            growTargets(document);
            findScrollers(document);
            adopt();
        });
    }

    // Late stylesheets and rendered rows join the layer as they arrive.
    new MutationObserver(records => {
        for (const record of records) {
            for (const node of Array.from(record.addedNodes)) {
                if (node instanceof HTMLLinkElement) node.addEventListener("load", refresh, { once: true });
                if (node instanceof Element) { refresh(); return; }
            }
        }
    }).observe(document.documentElement, { childList: true, subtree: true });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", refresh, { once: true });
    else refresh();
    window.addEventListener("load", refresh, { once: true });

    window.LwcTouch = { isTouch, views, sheet, pull, scroller, refresh };
})();
