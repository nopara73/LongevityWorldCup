// Shared interaction motion. motion.css owns every duration and easing; this
// classic script only decides when motion may run and animates the few changes
// CSS cannot describe (rows that move, panels whose content is redrawn).
//
// Motion starts after the visitor's first trusted pointer or key press, so
// loading, restoring and server-rendered states never animate. Reduced motion
// keeps the gate closed. Nothing here delays input or the DOM change itself.

interface LwcMotionApi {
    readonly enabled: () => boolean;
    readonly flip: (elements: readonly Element[], mutate: () => void) => void;
    readonly afterExit: (element: Element, complete: () => void) => () => void;
    readonly settle: (element: Element) => void;
}

type LwcWaitOutcome = "success" | "error" | "idle";

interface LwcWaitOptions {
    // The form or panel that must not change while this control waits.
    readonly region?: Element | null;
    // Roughly how long the wait usually takes, in ms; the fill paces itself to it.
    readonly expect?: number;
}

interface LwcWaitApi {
    readonly start: (control: Element, options?: LwcWaitOptions) => void;
    readonly finish: (control: Element, outcome?: LwcWaitOutcome) => Promise<void>;
    readonly region: (region: Element, busy: boolean) => void;
    readonly optimistic: <T>(apply: () => (() => void) | void, request: () => Promise<T>, control?: Element | null) => Promise<T>;
}

interface Window {
    LwcMotion: LwcMotionApi;
    LwcWait: LwcWaitApi;
}

(() => {
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    // Rows further than this outside the viewport snap: nobody sees them travel.
    const flipMargin = 160;
    const flipLimit = 80;
    const ownedAnimations = new Map<Element, Animation>();
    const exitWaiters = new Map<Element, () => void>();

    function readToken(name: string, fallback: number): number {
        const token = getComputedStyle(root).getPropertyValue(name).trim();
        const value = parseFloat(token) * (token.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(value) && value >= 0 ? value : fallback;
    }

    function readEase(name: string): string {
        return getComputedStyle(root).getPropertyValue(name).trim() || "ease";
    }

    function enabled(): boolean {
        return root.dataset["lwcMotion"] === "on" && !reduced.matches;
    }

    function open(event: Event): void {
        if (!event.isTrusted || reduced.matches) return;
        root.dataset["lwcMotion"] = "on";
        window.removeEventListener("pointerdown", open, true);
        window.removeEventListener("keydown", open, true);
    }

    window.addEventListener("pointerdown", open, true);
    window.addEventListener("keydown", open, true);
    function preferenceChanged(): void {
        if (!reduced.matches) return;
        delete root.dataset["lwcMotion"];
        for (const animation of ownedAnimations.values()) animation.cancel();
        ownedAnimations.clear();
        // Re-enable only on a new trusted input after the preference changes back.
        window.addEventListener("pointerdown", open, true);
        window.addEventListener("keydown", open, true);
    }
    if (typeof reduced.addEventListener === "function") reduced.addEventListener("change", preferenceChanged);
    else reduced.addListener?.(preferenceChanged);

    function cancelOwned(element: Element): void {
        ownedAnimations.get(element)?.cancel();
        ownedAnimations.delete(element);
    }

    // Own only our animation, never cancel a component's animation. Repeated
    // interactions replace their previous feedback and release finished targets.
    function animate(element: HTMLElement, frames: Keyframe[], options: KeyframeAnimationOptions): void {
        cancelOwned(element);
        if (!enabled() || typeof element.animate !== "function") return;
        let animation: Animation;
        try { animation = element.animate(frames, options); }
        catch { return; /* Decorative feedback must not interrupt the completed interaction. */ }
        ownedAnimations.set(element, animation);
        const release = () => {
            if (ownedAnimations.get(element) === animation) ownedAnimations.delete(element);
        };
        void animation.finished.then(release, release);
    }

    function nearViewport(rect: DOMRect): boolean {
        return rect.height > 0 && rect.bottom > -flipMargin && rect.top < window.innerHeight + flipMargin;
    }

    // First, Last, Invert, Play: elements keep their identity while the caller
    // reorders, shows or hides them. Moved ones glide from where they were, newly
    // shown ones fade in place. Layout is final immediately; only paint moves.
    function flip(elements: readonly Element[], mutate: () => void): void {
        if (!enabled()) {
            mutate();
            return;
        }

        const before = new Map<Element, DOMRect>();
        for (const element of elements) {
            const rect = element.getBoundingClientRect();
            if (nearViewport(rect)) before.set(element, rect);
        }

        // Measure painted positions first, then measure final layout without the
        // previous FLIP offset. Otherwise rapid reorders compound translations.
        for (const element of elements) cancelOwned(element);

        mutate();

        const duration = readToken("--lwc-motion-move", 260);
        const easing = readEase("--lwc-ease-standard");
        const arrivals = { duration: readToken("--lwc-motion-base", 200), easing: readEase("--lwc-ease-enter") };
        const pending: { element: HTMLElement; frames: Keyframe[]; options: KeyframeAnimationOptions }[] = [];
        let animated = 0;
        for (const element of elements) {
            if (animated >= flipLimit || !(element instanceof HTMLElement) || !element.isConnected) continue;
            const after = element.getBoundingClientRect();
            if (!nearViewport(after)) continue;
            const first = before.get(element);
            if (!first) {
                animated++;
                pending.push({ element, frames: settleFrames(element), options: arrivals });
                continue;
            }
            // A row arriving from far away slides in from just past the edge, not across the page.
            const travel = window.innerHeight / 3;
            const dy = Math.max(-travel, Math.min(travel, first.top - after.top));
            if (Math.abs(dy) < 1) continue;
            animated++;
            pending.push({ element,
                frames: [{ translate: `0 ${dy}px` }, { translate: "0 0" }],
                options: { duration, easing, composite: "add" } });
        }
        // Finish every geometry/style read before starting animation writes.
        // Interleaving them forces another layout for each subsequent row.
        for (const { element, frames, options } of pending) animate(element, frames, options);
    }

    // Run once the element's own exit transition or animation ends, or at once
    // when nothing is running (reduced motion, gate closed, no exit styles).
    function afterExit(element: Element, complete: () => void): () => void {
        exitWaiters.get(element)?.();
        let done = false;
        let timeout: number | undefined;
        const cancel = () => {
            if (done) return;
            done = true;
            window.clearTimeout(timeout);
            if (exitWaiters.get(element) === cancel) exitWaiters.delete(element);
        };
        const finish = () => {
            if (done) return;
            cancel();
            complete();
        };
        const running = typeof element.getAnimations === "function"
            ? element.getAnimations().filter(animation => animation.playState === "running") : [];
        if (!running.length) {
            finish();
            return cancel;
        }
        exitWaiters.set(element, cancel);
        Promise.allSettled(running.map(animation => animation.finished)).then(finish);
        // A cancelled or interrupted animation must never strand the caller.
        timeout = window.setTimeout(finish, readToken("--lwc-motion-slow", 280) + 120);
        return cancel;
    }

    function settleFrames(element: HTMLElement): Keyframe[] {
        const style = getComputedStyle(element);
        const [x = "0px", y = "0px", z = "0px"] = style.translate === "none" ? [] : style.translate.split(/\s+/);
        const opacity = Number(style.opacity);
        return [{ opacity: opacity * 0.35, translate: `${x} calc(${y} + 4px) ${z}` }, { opacity, translate: `${x} ${y} ${z}` }];
    }

    // A panel whose content was redrawn in place settles in instead of flashing.
    function settle(element: Element): void {
        if (!enabled() || !(element instanceof HTMLElement)) return;
        cancelOwned(element);
        animate(element,
            settleFrames(element),
            { duration: readToken("--lwc-motion-base", 200), easing: readEase("--lwc-ease-enter") });
    }

    // A chip, toggle or checkbox that just became selected confirms it with a small pop.
    function pop(element: Element): void {
        if (!enabled() || !(element instanceof HTMLElement) || !element.getClientRects().length) return;
        if (typeof element.getAnimations === "function" && element.getAnimations({ subtree: true }).some(animation =>
            animation !== ownedAnimations.get(element) && animation.playState === "running"
            && !(typeof CSSTransition !== "undefined" && animation instanceof CSSTransition))) return;
        animate(element,
            [{ scale: "0.94" }, { scale: "1.04", offset: 0.55 }, { scale: "1" }],
            { duration: readToken("--lwc-motion-slow", 280), easing: readEase("--lwc-ease-standard"), composite: "add" });
    }

    function isSelected(element: Element): boolean {
        return element.getAttribute("aria-pressed") === "true"
            || element.getAttribute("aria-checked") === "true"
            || element.classList.contains("active")
            || element.classList.contains("is-active");
    }

    // Tabs redraw or swap their panel. Settle whichever panel the selected tab controls.
    document.addEventListener("click", event => {
        if (!enabled() || !(event.target instanceof Element)) return;
        const tab = event.target.closest("[role='tab']");
        const toggle = tab ? null : event.target.closest("[aria-pressed], [role='radio'], [role='switch'], [role='checkbox'], .view-badge");
        if (!tab && !toggle) return;
        requestAnimationFrame(() => {
            if (toggle) {
                if (isSelected(toggle)) pop(toggle);
                return;
            }
            if (!tab || tab.getAttribute("aria-selected") !== "true") return;
            const panel = document.getElementById(tab.getAttribute("aria-controls") || "");
            if (panel && panel.getClientRects().length) settle(panel);
        });
    });

    // Native checkboxes and radios, or the visible face drawn next to a hidden one.
    document.addEventListener("change", event => {
        const input = event.target;
        if (!enabled() || !(input instanceof HTMLInputElement) || !input.checked
            || (input.type !== "checkbox" && input.type !== "radio")) return;
        const box = input.getBoundingClientRect();
        const visibleInput = box.width > 4 && box.height > 4 && getComputedStyle(input).opacity !== "0";
        const face = visibleInput ? input : input.nextElementSibling;
        if (face) pop(face);
    });

    window.LwcMotion = Object.freeze({ enabled, flip, afterExit, settle });

})();

// Waits inside a page. One choreography for every control, list and save that
// is waiting on the network, so nothing jumps and nothing looks frozen:
//
// - A control keeps its label, colours and width; a fill creeps across it,
//   paced to how long the wait usually takes.
// - A list or board that refreshes keeps its rows, dims after a beat and draws
//   one travelling line along its top; rows that arrive settle in.
// - Saves with no control on screen draw the same line along the viewport top.
// - Endings are a gesture: the fill completes and a ring confirms, or drains
//   and the control shakes once. Waits shorter than a beat never show at all.
//
// Pages adopt it without code: a pressed control that turns disabled or
// aria-busy is a wait, and so is a populated region that turns aria-busy.
// LwcWait.start/finish are for flows that know more (expected duration, the
// form to hold still, the outcome).
(() => {
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const controlSelector = "button, [role='button'], a.button, .lmx-button";
    const itemSelector = "li, tr, article, [role='listitem'], [role='row'], .lmx-discussion-post, .event-card";
    const pressMemory = 1200;
    const arrivalLimit = 16;
    const lookProperties = ["--lwc-wait-bg", "--lwc-wait-image", "--lwc-wait-fg", "--lwc-wait-border", "--lwc-wait-shadow"];

    interface ControlWait {
        readonly started: number;
        readonly fill: HTMLElement;
        readonly region: HTMLElement | null;
        readonly regionWasInert: boolean;
        readonly lockedSize: string;
        readonly positioned: boolean;
        readonly ownsBusy: boolean;
        // Disabled-after-press only counts once the page actually asks the network.
        readonly needsRequest: boolean;
        // Started through LwcWait.start: only LwcWait.finish ends it.
        readonly manual: boolean;
        requests: number;
        shown: boolean;
        showTimer: number | undefined;
        mutated: number;
        failed: number;
    }

    interface RegionWait {
        readonly started: number;
        readonly line: HTMLElement;
        readonly host: HTMLElement;
        readonly hostPositioned: boolean;
    }

    interface Look {
        readonly at: number;
        readonly control: HTMLElement;
        readonly values: readonly string[];
    }

    const controls = new Map<HTMLElement, ControlWait>();
    const regions = new Map<HTMLElement, RegionWait>();
    const settling = new Map<HTMLElement, number>();
    let lastLook: Look | null = null;
    let syncInFlight = 0;
    let syncTimer: number | undefined;
    let syncFailed = false;
    let arrivals: HTMLElement[] = [];
    let arrivalFrame = 0;

    function token(name: string, fallback: number): number {
        const value = getComputedStyle(root).getPropertyValue(name).trim();
        const parsed = parseFloat(value) * (value.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
    }

    function moving(): boolean {
        return !reduced.matches;
    }

    // The site greys out a disabled control. A waiting one keeps the look it
    // had when pressed, so the press never reads as "broken".
    function readLook(control: HTMLElement): Look {
        const style = getComputedStyle(control);
        return {
            at: performance.now(),
            control,
            values: [style.backgroundColor, style.backgroundImage, style.color, style.borderColor, style.boxShadow]
        };
    }

    function rememberPress(event: Event): void {
        if (!event.isTrusted || !(event.target instanceof Element)) return;
        const control = event.target.closest<HTMLElement>(controlSelector);
        if (!control || control.matches(":disabled, [aria-disabled='true'], [aria-busy='true']")) return;
        lastLook = readLook(control);
    }

    function rememberSubmit(event: Event): void {
        const submitter = (event as SubmitEvent).submitter;
        if (submitter instanceof HTMLElement && !submitter.matches(":disabled")) lastLook = readLook(submitter);
    }

    function pressedRecently(control: HTMLElement): Look | null {
        if (!lastLook || lastLook.control !== control || performance.now() - lastLook.at > pressMemory) return null;
        return lastLook;
    }

    function start(control: Element, options: LwcWaitOptions = {}): void {
        if (!(control instanceof HTMLElement) || controls.has(control)) return;
        beginControl(control, options, pressedRecently(control) || readLook(control), false, true);
    }

    function beginControl(control: HTMLElement, options: LwcWaitOptions, look: Look, needsRequest: boolean, manual = false): void {
        cancelSettle(control);
        const rect = control.getBoundingClientRect();
        lookProperties.forEach((name, index) => control.style.setProperty(name, look.values[index] || ""));
        if (options.expect && options.expect > 0) control.style.setProperty("--lwc-wait-expect", `${Math.round(options.expect)}ms`);
        const lockedSize = control.style.minInlineSize;
        // The fill is drawn inside the control, so it needs a containing block.
        const positioned = getComputedStyle(control).position !== "static";
        // Hold the width it had, so a swapped label cannot shrink it mid-wait.
        if (rect.width > 0) control.style.minInlineSize = `${Math.ceil(rect.width)}px`;

        const fill = document.createElement("span");
        fill.className = "lwc-wait-fill";
        fill.setAttribute("aria-hidden", "true");
        const region = options.region instanceof HTMLElement ? options.region : null;
        const regionWasInert = region ? region.inert : false;
        if (region) {
            region.setAttribute("data-lwc-wait-hold", "");
            region.inert = true;
        }
        const ownsBusy = control.getAttribute("aria-busy") !== "true";
        const wait: ControlWait = {
            started: performance.now(),
            fill,
            region,
            regionWasInert,
            lockedSize,
            positioned,
            ownsBusy,
            needsRequest,
            manual,
            requests: 0,
            shown: false,
            showTimer: undefined,
            mutated: 0,
            failed: 0
        };
        controls.set(control, wait);
        control.setAttribute("data-lwc-wait", "pending");
        if (ownsBusy) control.setAttribute("aria-busy", "true");
        // Nothing appears for a beat: fast answers never flash a progress state.
        const show = (late: boolean) => {
            wait.showTimer = undefined;
            if (controls.get(control) !== wait) return;
            if (wait.needsRequest && !wait.requests) {
                // Give a slow handler one more beat, then treat it as a plain disable.
                if (!late) wait.showTimer = window.setTimeout(() => show(true), token("--lwc-wait-delay", 140) * 2);
                else abandon(control, wait);
                return;
            }
            wait.shown = true;
            if (!positioned) control.style.position = "relative";
            if (!fill.isConnected) control.append(fill);
            control.setAttribute("data-lwc-wait", "shown");
            refreshSync();
        };
        wait.showTimer = window.setTimeout(() => show(false), token("--lwc-wait-delay", 140));
    }

    function abandon(control: HTMLElement, wait: ControlWait): void {
        controls.delete(control);
        restoreControl(control, wait);
    }

    function cancelSettle(control: HTMLElement): void {
        const timer = settling.get(control);
        if (timer !== undefined) window.clearTimeout(timer);
        settling.delete(control);
        control.removeAttribute("data-lwc-wait-end");
        control.querySelectorAll(":scope > .lwc-wait-fill").forEach(node => node.remove());
    }

    function restoreControl(control: HTMLElement, wait: ControlWait): void {
        control.removeAttribute("data-lwc-wait");
        control.removeAttribute("data-lwc-wait-end");
        if (wait.ownsBusy && control.getAttribute("aria-busy") === "true") control.removeAttribute("aria-busy");
        for (const name of lookProperties) control.style.removeProperty(name);
        control.style.removeProperty("--lwc-wait-expect");
        control.style.minInlineSize = wait.lockedSize;
        if (!wait.positioned) control.style.position = "";
        wait.fill.remove();
        refreshSync();
    }

    function finish(control: Element, outcome?: LwcWaitOutcome): Promise<void> {
        if (!(control instanceof HTMLElement)) return Promise.resolve();
        const wait = controls.get(control);
        if (!wait) return Promise.resolve();
        controls.delete(control);
        window.clearTimeout(wait.showTimer);
        if (wait.region) {
            wait.region.removeAttribute("data-lwc-wait-hold");
            wait.region.inert = wait.regionWasInert;
        }
        if (wait.ownsBusy && control.getAttribute("aria-busy") === "true") control.removeAttribute("aria-busy");
        const result: LwcWaitOutcome = outcome || (wait.failed ? "error" : wait.mutated ? "success" : "idle");

        if (!wait.shown) {
            // Answered before the beat ended: no fill, but a save still confirms.
            restoreControl(control, wait);
            if (result !== "idle") ring(control, result);
            return Promise.resolve();
        }

        // A wait that did show stays long enough to be read, then ends with a gesture.
        const visible = performance.now() - wait.started - token("--lwc-wait-delay", 140);
        const hold = Math.max(0, token("--lwc-wait-min", 420) - visible);
        return new Promise(resolve => {
            const end = () => {
                if (!control.isConnected || !moving()) {
                    restoreControl(control, wait);
                    if (control.isConnected && result !== "idle") ring(control, result);
                    resolve();
                    return;
                }
                // Carry on from wherever the creep had reached.
                const from = getComputedStyle(wait.fill).clipPath;
                const first = from && from !== "none" ? from : "inset(0 100% 0 0)";
                const last = result === "error" ? "inset(0 100% 0 0)" : "inset(0 0% 0 0)";
                control.setAttribute("data-lwc-wait-end", result);
                // A page that redrew its label took the fill with it; the ending still plays.
                if (!wait.fill.isConnected) control.append(wait.fill);
                wait.fill.style.clipPath = first;
                wait.fill.style.animationName = "none";
                try {
                    wait.fill.animate([
                        { clipPath: first, opacity: 1 },
                        { clipPath: last, opacity: 1, offset: 0.5 },
                        { clipPath: last, opacity: 0 }
                    ], { duration: token("--lwc-wait-settle", 520), easing: "cubic-bezier(0.2, 0, 0, 1)", fill: "forwards" });
                } catch { /* decorative */ }
                if (result !== "idle") ring(control, result);
                if (result === "error") shake(control);
                settling.set(control, window.setTimeout(() => {
                    settling.delete(control);
                    restoreControl(control, wait);
                    resolve();
                }, token("--lwc-wait-settle", 520)));
            };
            if (hold > 0) window.setTimeout(end, hold);
            else end();
        });
    }

    function ring(control: HTMLElement, result: LwcWaitOutcome): void {
        control.setAttribute("data-lwc-wait-ring", result);
        window.setTimeout(() => {
            if (control.getAttribute("data-lwc-wait-ring") === result) control.removeAttribute("data-lwc-wait-ring");
        }, token("--lwc-wait-settle", 520) + 260);
    }

    function shake(control: HTMLElement): void {
        if (!moving() || typeof control.animate !== "function") return;
        try {
            control.animate([
                { translate: "0 0" }, { translate: "-4px 0" }, { translate: "3px 0" },
                { translate: "-2px 0" }, { translate: "1px 0" }, { translate: "0 0" }
            ], { duration: 340, easing: "ease-out", composite: "add" });
        } catch { /* decorative */ }
    }

    // Regions: lists, boards and panels that refresh in place.
    function lineHost(region: HTMLElement): HTMLElement {
        let host: HTMLElement = region;
        while (host.parentElement && /^(TABLE|TBODY|THEAD|TFOOT|TR|UL|OL|DL|SELECT)$/.test(host.tagName)) host = host.parentElement;
        return host;
    }

    // Only a region that already shows something refreshes; a first load keeps
    // the page's own placeholder.
    function populated(region: HTMLElement): boolean {
        if (document.readyState !== "complete" || performance.now() < 1500) return false;
        const rect = region.getBoundingClientRect();
        return rect.height > 24 && (region.textContent || "").trim().length > 0;
    }

    function beginRegion(region: HTMLElement): void {
        if (regions.has(region)) return;
        // A form whose own button already shows the wait needs no second signal.
        for (const control of controls.keys()) if (region.contains(control)) return;
        const outer = region.parentElement?.closest<HTMLElement>("[data-lwc-wait-region]");
        if (outer) return;
        const host = lineHost(region);
        const line = document.createElement("span");
        line.className = "lwc-wait-line";
        line.setAttribute("aria-hidden", "true");
        const hostPositioned = getComputedStyle(host).position !== "static";
        if (!hostPositioned) host.style.position = "relative";
        host.prepend(line);
        region.setAttribute("data-lwc-wait-region", "refresh");
        regions.set(region, { started: performance.now(), line, host, hostPositioned });
        refreshSync();
    }

    function endRegion(region: HTMLElement): void {
        const wait = regions.get(region);
        if (!wait) return;
        regions.delete(region);
        const hold = Math.max(0, token("--lwc-wait-min", 420) - (performance.now() - wait.started));
        window.setTimeout(() => {
            if (regions.has(region)) return;
            region.setAttribute("data-lwc-wait-region", "settle");
            wait.line.setAttribute("data-lwc-wait-end", "");
            window.setTimeout(() => {
                if (regions.has(region)) return;
                region.removeAttribute("data-lwc-wait-region");
                wait.line.remove();
                if (!wait.hostPositioned) wait.host.style.position = "";
            }, moving() ? token("--lwc-wait-settle", 520) : 0);
            refreshSync();
        }, hold);
    }

    function region(target: Element, busy: boolean): void {
        if (!(target instanceof HTMLElement)) return;
        if (busy) beginRegion(target);
        else endRegion(target);
    }

    // Rows and cards that arrive while something waits settle in, in order.
    function queueArrival(node: Node): void {
        if (!(node instanceof HTMLElement) || !moving() || !node.matches(itemSelector)) return;
        if (arrivals.length < arrivalLimit * 2) arrivals.push(node);
        if (!arrivalFrame) arrivalFrame = requestAnimationFrame(flushArrivals);
    }

    function flushArrivals(): void {
        arrivalFrame = 0;
        const batch = arrivals;
        arrivals = [];
        const visible = batch.filter(item => {
            if (!item.isConnected || typeof item.animate !== "function") return false;
            const rect = item.getBoundingClientRect();
            // Rows the page reveals itself (hidden until it says so) keep their own entrance.
            return rect.height > 0 && rect.bottom > 0 && rect.top < window.innerHeight
                && parseFloat(getComputedStyle(item).opacity) > 0.05
                && !item.getAnimations().some(animation => animation.playState === "running");
        }).slice(0, arrivalLimit);
        const easing = getComputedStyle(root).getPropertyValue("--lwc-ease-enter").trim() || "ease-out";
        const duration = token("--lwc-motion-move", 260);
        const stagger = token("--lwc-wait-stagger", 32);
        visible.forEach((item, index) => {
            try {
                item.animate([{ opacity: 0, translate: "0 10px" }, { opacity: 1, translate: "0 0" }],
                    { duration, delay: index * stagger, easing, fill: "backwards" });
            } catch { /* decorative */ }
        });
    }

    // Saves with no visible control get one line along the viewport top.
    function onScreen(element: Element): boolean {
        const rect = element.getBoundingClientRect();
        return rect.width > 0 && rect.bottom > 0 && rect.top < window.innerHeight;
    }

    function anyVisibleWait(): boolean {
        for (const [control, wait] of controls) if (wait.shown && onScreen(control)) return true;
        for (const regionElement of regions.keys()) if (onScreen(regionElement)) return true;
        return false;
    }

    function ensureSyncLine(): void {
        if (document.getElementById("lwc-sync")) return;
        const line = document.createElement("div");
        line.id = "lwc-sync";
        line.setAttribute("aria-hidden", "true");
        document.body.append(line);
    }

    function refreshSync(): void {
        if (!document.body) return;
        const showing = root.getAttribute("data-lwc-sync") === "on";
        if (syncInFlight > 0 && !anyVisibleWait()) {
            if (!showing && syncTimer === undefined) {
                syncTimer = window.setTimeout(() => {
                    syncTimer = undefined;
                    if (syncInFlight > 0 && !anyVisibleWait()) {
                        ensureSyncLine();
                        root.setAttribute("data-lwc-sync", "on");
                    }
                }, token("--lwc-wait-sync-delay", 360));
            }
            return;
        }
        window.clearTimeout(syncTimer);
        syncTimer = undefined;
        if (!showing) return;
        const state = syncFailed ? "error" : "done";
        syncFailed = false;
        root.setAttribute("data-lwc-sync", state);
        window.setTimeout(() => {
            if (root.getAttribute("data-lwc-sync") === state) root.removeAttribute("data-lwc-sync");
        }, moving() ? token("--lwc-wait-settle", 520) + 200 : 0);
    }

    function requestUrl(input: RequestInfo | URL): string {
        return typeof input === "string" ? input : input instanceof URL ? input.href : input.url;
    }

    function isSave(input: RequestInfo | URL, init?: RequestInit): boolean {
        const method = (init?.method || (input instanceof Request ? input.method : "GET")).toUpperCase();
        if (method === "GET" || method === "HEAD" || init?.keepalive) return false;
        return !/\/api\/(site-statistics|statistics|stats|client-errors?)\b/i.test(requestUrl(input));
    }

    // Count requests so endings know whether something was saved or refused.
    // The page's own promise is returned untouched. Scripts that wrap fetch later
    // (statistics, tests) slot in underneath, so this count always sees the call.
    function watchRequest(request: Promise<Response>, input: RequestInfo | URL, init?: RequestInit): void {
        let save = false;
        try { save = isSave(input, init); } catch { save = false; }
        if (!controls.size && !save) return;
        const watching = [...controls.values()];
        for (const wait of watching) wait.requests++;
        if (save) {
            syncInFlight++;
            refreshSync();
        }
        const done = (ok: boolean) => {
            for (const wait of watching) {
                if (!ok) wait.failed++;
                else if (save) wait.mutated++;
            }
            if (!save) return;
            syncInFlight = Math.max(0, syncInFlight - 1);
            if (!ok) syncFailed = true;
            refreshSync();
        };
        request.then(response => done(response.ok), () => done(false));
    }

    if (typeof window.fetch === "function") {
        const chain: (typeof window.fetch)[] = [window.fetch];
        let depth = 0;
        const counted = function (input: RequestInfo | URL, init?: RequestInit): Promise<Response> {
            // A wrapper that captured this function calls back in: hand on to the layer beneath it.
            const layer = chain[Math.max(0, chain.length - 1 - depth)] || chain[0]!;
            depth++;
            let request: Promise<Response>;
            try { request = layer.call(window, input, init); }
            finally { depth--; }
            if (depth === 0) watchRequest(request, input, init);
            return request;
        };
        try {
            Object.defineProperty(window, "fetch", {
                configurable: true,
                enumerable: true,
                get: () => counted,
                set: (next: unknown) => {
                    if (typeof next === "function" && next !== counted) chain.push(next as typeof window.fetch);
                }
            });
        } catch {
            window.fetch = counted;
        }
    }

    // Automatic adoption: controls and regions announce their own waits.
    function waitingNow(control: HTMLElement, wait?: ControlWait): boolean {
        return (!wait?.ownsBusy && control.getAttribute("aria-busy") === "true")
            || (control instanceof HTMLButtonElement && control.disabled)
            || control.getAttribute("aria-disabled") === "true";
    }

    function attributeChanged(target: Node, name: string | null): void {
        if (!(target instanceof HTMLElement) || target.closest("[data-lwc-wait-skip]")) return;
        if (target.matches(controlSelector)) {
            const current = controls.get(target);
            if (current) {
                if (!current.manual && !waitingNow(target, current)) void finish(target);
                return;
            }
            if (!waitingNow(target) || settling.has(target)) return;
            const look = pressedRecently(target);
            // aria-busy is an explicit wait; disabled only counts right after a press.
            if (name === "aria-busy" || look) {
                beginControl(target, {}, look || readLook(target), target.getAttribute("aria-busy") !== "true");
                lastLook = null;
            }
            return;
        }
        if (name !== "aria-busy" || target.hasAttribute("data-lwc-wait-hold")) return;
        if (target.getAttribute("aria-busy") === "true") {
            if (populated(target)) beginRegion(target);
        } else {
            endRegion(target);
        }
    }

    const observer = new MutationObserver(records => {
        for (const record of records) {
            if (record.type === "attributes") attributeChanged(record.target, record.attributeName);
            else if (controls.size || regions.size) {
                const wait = record.target instanceof HTMLElement ? controls.get(record.target) : undefined;
                // A label swapped mid-wait keeps its fill.
                if (wait?.shown && !wait.fill.isConnected) (record.target as HTMLElement).append(wait.fill);
                record.addedNodes.forEach(queueArrival);
            }
        }
    });

    function observe(): void {
        observer.observe(document.body, {
            subtree: true,
            childList: true,
            attributes: true,
            attributeFilter: ["aria-busy", "disabled", "aria-disabled"]
        });
    }

    window.addEventListener("pointerdown", rememberPress, true);
    window.addEventListener("keydown", event => {
        if (event.key === "Enter" || event.key === " ") rememberPress(event);
    }, true);
    document.addEventListener("submit", rememberSubmit, true);
    window.addEventListener("scroll", () => { if (syncInFlight) refreshSync(); }, { passive: true });
    if (document.body) observe();
    else document.addEventListener("DOMContentLoaded", observe, { once: true });

    // Show the result now, send the request, and put things back if it fails.
    async function optimistic<T>(apply: () => (() => void) | void, request: () => Promise<T>, control?: Element | null): Promise<T> {
        const undo = apply();
        try {
            const result = await request();
            if (control instanceof HTMLElement) ring(control, "success");
            return result;
        } catch (error) {
            if (typeof undo === "function") undo();
            if (control instanceof HTMLElement) {
                ring(control, "error");
                shake(control);
            }
            throw error;
        }
    }

    window.LwcWait = Object.freeze({ start, finish, region, optimistic });
})();
