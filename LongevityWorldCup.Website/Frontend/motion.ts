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

interface Window {
    LwcMotion: LwcMotionApi;
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
