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
type LwcWaitFinish = (outcome?: LwcWaitOutcome) => Promise<void>;

interface LwcWaitOptions {
    // Associates the control with its panel without disabling that panel.
    readonly region?: Element | null;
    readonly message?: string;
}

interface LwcWaitApi {
    readonly start: (control: Element, options?: LwcWaitOptions) => LwcWaitFinish;
    readonly finish: (control: Element, outcome?: LwcWaitOutcome) => Promise<void>;
    readonly region: (region: Element, busy: boolean) => void;
    readonly optimistic: <T>(apply: () => (() => void) | void, request: () => Promise<T>, control?: Element | null) => Promise<T>;
}

interface Window {
    LwcMotion: LwcMotionApi;
    LwcWait: LwcWaitApi;
}

(() => {
    if (window.LwcMotion) return;
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

// Explicit operations own their result. aria-busy can also provide a quiet,
// indeterminate wait; disabled alone never means network activity. Component
// entrances and content visibility remain owned by the component.
(() => {
    if (window.LwcWait) return;
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const selector = "button, [role='button'], a.button, .lmx-button";
    const properties = ["--lwc-wait-bg", "--lwc-wait-image", "--lwc-wait-fg", "--lwc-wait-border", "--lwc-wait-shadow"];
    interface ControlWait {
        readonly control: HTMLElement;
        readonly manual: boolean;
        readonly ownsBusy: boolean;
        readonly styles: readonly (readonly [string, string, string])[];
        readonly fill: HTMLElement;
        readonly ring: HTMLElement;
        readonly status: HTMLElement | null;
        readonly done: Promise<void>;
        readonly resolve: () => void;
        timer: number | undefined;
        ending: boolean;
        shown: boolean;
    }
    interface RegionWait {
        readonly host: HTMLElement;
        readonly line: HTMLElement;
        timer: number | undefined;
        ending: boolean;
    }
    const controls = new Map<HTMLElement, ControlWait>();
    const regions = new Map<HTMLElement, RegionWait>();
    const hosts = new Map<HTMLElement, { count: number; position: string; priority: string; changed: boolean }>();
    const background = new Set<symbol>();
    let syncTimer: number | undefined;
    let syncGeneration = 0;
    let syncFailed = false;
    let scrollFrame = 0;

    function token(name: string, fallback: number): number {
        const value = getComputedStyle(root).getPropertyValue(name).trim();
        const parsed = parseFloat(value) * (value.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
    }

    function retainHost(host: HTMLElement): void {
        const existing = hosts.get(host);
        if (existing) { existing.count++; return; }
        const value = { count: 1, position: host.style.getPropertyValue("position"), priority: host.style.getPropertyPriority("position"),
            changed: getComputedStyle(host).position === "static" };
        hosts.set(host, value);
        if (value.changed) host.style.position = "relative";
    }

    function releaseHost(host: HTMLElement): void {
        const value = hosts.get(host);
        if (!value || --value.count > 0) return;
        hosts.delete(host);
        if (!value.changed || host.style.position !== "relative") return;
        if (value.position) host.style.setProperty("position", value.position, value.priority);
        else host.style.removeProperty("position");
    }

    function clearControl(wait: ControlWait): void {
        window.clearTimeout(wait.timer);
        if (controls.get(wait.control) !== wait) { wait.resolve(); return; }
        controls.delete(wait.control);
        const control = wait.control;
        control.removeAttribute("data-lwc-wait");
        control.removeAttribute("data-lwc-wait-end");
        control.removeAttribute("data-lwc-wait-ring");
        if (wait.ownsBusy && control.getAttribute("aria-busy") === "true") control.removeAttribute("aria-busy");
        wait.fill.remove();
        wait.ring.remove();
        wait.status?.remove();
        for (const [name, value, priority] of wait.styles) {
            if (value) control.style.setProperty(name, value, priority);
            else control.style.removeProperty(name);
        }
        releaseHost(control);
        wait.resolve();
    }

    function begin(control: HTMLElement, options: LwcWaitOptions, manual: boolean): ControlWait {
        const previous = controls.get(control);
        if (previous) clearControl(previous);
        const computed = getComputedStyle(control);
        const values = [computed.backgroundColor, computed.backgroundImage, computed.color, computed.borderColor, computed.boxShadow];
        const styles = [...properties, "min-inline-size"].map(name =>
            [name, control.style.getPropertyValue(name), control.style.getPropertyPriority(name)] as const);
        const width = control.getBoundingClientRect().width;
        retainHost(control);
        properties.forEach((name, i) => control.style.setProperty(name, values[i] || ""));
        if (width > 0) control.style.minInlineSize = `min(${width}px, 100%)`;
        const fill = document.createElement("span");
        fill.className = "lwc-wait-fill";
        fill.setAttribute("aria-hidden", "true");
        const ring = document.createElement("span");
        ring.className = "lwc-wait-ring";
        ring.setAttribute("aria-hidden", "true");
        let resolve = () => {};
        const done = new Promise<void>(complete => { resolve = complete; });
        const status = options.message ? document.createElement("span") : null;
        if (status) {
            status.className = "lwc-wait-status";
            status.setAttribute("role", "status");
            status.setAttribute("aria-live", "polite");
            document.body.append(status);
            status.textContent = options.message || "";
        }
        const wait: ControlWait = { control, manual, ownsBusy: control.getAttribute("aria-busy") !== "true", styles,
            fill, ring, status, done, resolve, timer: undefined, ending: false, shown: false };
        controls.set(control, wait);
        control.setAttribute("data-lwc-wait", "pending");
        if (wait.ownsBusy) control.setAttribute("aria-busy", "true");
        wait.timer = window.setTimeout(() => {
            if (controls.get(control) !== wait || wait.ending) return;
            if (!control.isConnected) { clearControl(wait); return; }
            wait.shown = true;
            control.append(fill);
            control.setAttribute("data-lwc-wait", "shown");
            refreshSync();
        }, token("--lwc-wait-delay", 140));
        return wait;
    }

    function complete(wait: ControlWait, outcome: LwcWaitOutcome = "idle"): Promise<void> {
        const control = wait.control;
        if (controls.get(control) !== wait || wait.ending) return wait.done;
        window.clearTimeout(wait.timer);
        wait.ending = true;
        // Semantic readiness is immediate; the ending never gates content or input.
        control.removeAttribute("data-lwc-wait");
        if (wait.ownsBusy && control.getAttribute("aria-busy") === "true") control.removeAttribute("aria-busy");
        wait.status?.remove();
        if (outcome === "idle" || !control.isConnected) { clearControl(wait); refreshSync(); return wait.done; }
        control.setAttribute("data-lwc-wait-end", outcome);
        control.setAttribute("data-lwc-wait-ring", outcome);
        control.append(wait.ring);
        // An instant response confirms without ever inserting a pending fill.
        if (wait.shown) wait.fill.setAttribute("data-lwc-wait-end", outcome);
        wait.timer = window.setTimeout(() => { clearControl(wait); refreshSync(); },
            reduced.matches ? 0 : token("--lwc-wait-settle", 520));
        refreshSync();
        return wait.done;
    }

    function start(control: Element, options: LwcWaitOptions = {}): LwcWaitFinish {
        if (!(control instanceof HTMLElement) || !control.isConnected) return () => Promise.resolve();
        const wait = begin(control, options, true);
        // A late response can only complete the operation that created this handle.
        return outcome => complete(wait, outcome);
    }

    function finish(control: Element, outcome?: LwcWaitOutcome): Promise<void> {
        const wait = control instanceof HTMLElement ? controls.get(control) : undefined;
        return wait ? complete(wait, outcome) : Promise.resolve();
    }

    function clearRegion(region: HTMLElement, wait: RegionWait): void {
        window.clearTimeout(wait.timer);
        wait.line.remove();
        if (regions.get(region) !== wait) return;
        regions.delete(region);
        region.removeAttribute("data-lwc-wait-region");
        releaseHost(wait.host);
    }

    function region(target: Element, busy: boolean): void {
        if (!(target instanceof HTMLElement)) return;
        const previous = regions.get(target);
        if (busy) {
            if (previous && !previous.ending) return;
            if (previous) clearRegion(target, previous);
            if (!target.isConnected) return;
            let host = target;
            while (host.parentElement && /^(TABLE|TBODY|THEAD|TFOOT|TR|UL|OL|DL|SELECT)$/.test(host.tagName)) host = host.parentElement;
            retainHost(host);
            const line = document.createElement("span");
            line.className = "lwc-wait-line";
            line.setAttribute("aria-hidden", "true");
            host.prepend(line);
            const wait: RegionWait = { host, line, timer: undefined, ending: false };
            regions.set(target, wait);
            target.setAttribute("data-lwc-wait-region", "refresh");
        } else if (previous && !previous.ending) {
            previous.ending = true;
            target.setAttribute("data-lwc-wait-region", "settle");
            previous.line.setAttribute("data-lwc-wait-end", "");
            previous.timer = window.setTimeout(() => { clearRegion(target, previous); refreshSync(); },
                reduced.matches ? 0 : token("--lwc-wait-settle", 520));
        }
        refreshSync();
    }

    function onScreen(element: HTMLElement): boolean {
        const rect = element.getBoundingClientRect();
        return element.isConnected && rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < innerHeight;
    }

    function refreshSync(): void {
        const generation = ++syncGeneration;
        window.clearTimeout(syncTimer);
        syncTimer = undefined;
        const visible = [...controls.values()].some(wait => !wait.ending && wait.shown && onScreen(wait.control))
            || [...regions].some(([element, wait]) => !wait.ending && onScreen(element));
        if (background.size && !visible) {
            if (root.getAttribute("data-lwc-sync") === "on") return;
            syncTimer = window.setTimeout(() => {
                if (generation !== syncGeneration || !background.size) return;
                if (!document.getElementById("lwc-sync")) {
                    const line = document.createElement("div");
                    line.id = "lwc-sync"; line.setAttribute("aria-hidden", "true"); document.body.append(line);
                }
                root.setAttribute("data-lwc-sync", "on");
            }, token("--lwc-wait-sync-delay", 360));
        } else if (root.hasAttribute("data-lwc-sync")) {
            root.setAttribute("data-lwc-sync", syncFailed ? "error" : "done");
            syncTimer = window.setTimeout(() => {
                if (generation === syncGeneration) root.removeAttribute("data-lwc-sync");
            }, reduced.matches ? 0 : token("--lwc-wait-settle", 520));
        }
        if (!background.size) syncFailed = false;
    }

    function attributeChanged(target: HTMLElement): void {
        if (target.closest("[data-lwc-wait-skip]")) return;
        const busy = target.getAttribute("aria-busy") === "true";
        if (target.matches(selector)) {
            const current = controls.get(target);
            if (current) { if (!current.manual && !current.ending && !busy) void complete(current); return; }
            if (busy) begin(target, {}, false);
        } else if (busy) {
            // Task views, dialogs, proof processing and games already own their
            // loading presentation. Only declared list/board regions adopt here.
            if (!target.matches("[data-lwc-wait-refresh], #athleteLeaderboard tbody, #events-root")) return;
            if (!target.hasAttribute("data-lwc-wait-refresh") &&
                !target.querySelector("tr[data-athlete-name], tr.main-row")) return;
            if ([...controls.keys()].some(control => target.contains(control))) return;
            region(target, true);
        } else if (regions.has(target)) region(target, false);
    }

    const observer = new MutationObserver(records => {
        let removed = false;
        for (const record of records) {
            if (record.type === "attributes" && record.target instanceof HTMLElement) attributeChanged(record.target);
            else if (record.removedNodes.length) removed = true;
        }
        if (removed) {
            for (const wait of controls.values()) if (!wait.control.isConnected) clearControl(wait);
            for (const [element, wait] of regions) if (!element.isConnected || !wait.line.isConnected) clearRegion(element, wait);
        }
    });
    function observe(): void {
        observer.observe(document.body, { subtree: true, childList: true, attributes: true, attributeFilter: ["aria-busy"] });
        document.querySelectorAll<HTMLElement>("[aria-busy='true']").forEach(attributeChanged);
    }
    if (document.body) observe();
    else document.addEventListener("DOMContentLoaded", observe, { once: true });

    function dispose(): void {
        observer.disconnect();
        for (const wait of controls.values()) clearControl(wait);
        for (const [element, wait] of regions) clearRegion(element, wait);
        background.clear();
        window.clearTimeout(syncTimer);
        cancelAnimationFrame(scrollFrame);
        scrollFrame = 0;
        syncGeneration++;
        root.removeAttribute("data-lwc-sync");
        syncFailed = false;
    }
    window.addEventListener("pagehide", dispose);
    window.addEventListener("pageshow", event => { if (event.persisted) observe(); });
    window.addEventListener("scroll", () => {
        if (!background.size || scrollFrame) return;
        scrollFrame = requestAnimationFrame(() => { scrollFrame = 0; refreshSync(); });
    }, { passive: true });
    function preferenceChanged(): void {
        if (!reduced.matches) return;
        for (const wait of controls.values()) if (wait.ending) clearControl(wait);
        for (const [element, wait] of regions) if (wait.ending) clearRegion(element, wait);
        refreshSync();
    }
    if (typeof reduced.addEventListener === "function") reduced.addEventListener("change", preferenceChanged);
    else reduced.addListener?.(preferenceChanged);

    async function optimistic<T>(apply: () => (() => void) | void, request: () => Promise<T>, control?: Element | null): Promise<T> {
        const undo = apply();
        const end = control instanceof HTMLElement ? start(control) : null;
        const operation = Symbol();
        background.add(operation);
        refreshSync();
        try {
            const result = await request();
            void end?.("success");
            return result;
        } catch (error) {
            try { if (typeof undo === "function") undo(); }
            finally {
                void end?.("error");
                if (background.has(operation)) syncFailed = true;
            }
            throw error;
        } finally {
            if (background.delete(operation)) refreshSync();
        }
    }
    window.LwcWait = Object.freeze({ start, finish, region, optimistic });
})();
