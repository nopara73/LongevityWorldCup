// One celebration language for every milestone the site already marks:
// a submission received, a payment confirmed, a check-in rail completed,
// a better rank or a longer streak.
//
// Three intensities share one shape. "glow" pulses a soft halo around the
// element, "rise" adds an expanding ring and a few sparks, "burst" throws a
// short podium-coloured confetti spray for the big moments.
//
// Pieces live in a fixed, aria-hidden layer with pointer-events: none, so a
// celebration never blocks input or moves layout. Any pointer, key, wheel or
// scroll ends it at once. A `once` key plays a moment a single time per
// browser. Reduced motion swaps the animation for a brief static halo.

type LwcCelebrationLevel = "glow" | "rise" | "burst";
type LwcCelebrationTone = "accent" | "success" | "gold";

interface LwcCelebrationOptions {
    readonly level?: LwcCelebrationLevel;
    readonly tone?: LwcCelebrationTone;
    // Plays at most once per browser for this key.
    readonly once?: string;
    readonly delay?: number;
}

interface LwcCelebrationApi {
    readonly play: (target: Element | null | undefined, options?: LwcCelebrationOptions) => boolean;
    readonly skip: () => void;
    readonly seen: (key: string) => boolean;
}

interface Window {
    LwcCelebrate: LwcCelebrationApi;
}

(() => {
    if (window.LwcCelebrate) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const storageKey = "lwc-milestones";
    const memoryKeys = new Set<string>();
    const active = new Set<{ readonly target: Element; readonly stop: () => void }>();
    const pending = new Set<{ readonly target: Element; readonly key: string | undefined; readonly cancel: () => void }>();
    const levels: Record<LwcCelebrationLevel, { readonly duration: number; readonly sparks: number; readonly confetti: number }> = {
        glow: { duration: 900, sparks: 0, confetti: 0 },
        rise: { duration: 1100, sparks: 8, confetti: 0 },
        burst: { duration: 1400, sparks: 10, confetti: 22 }
    };
    // Podium and accent tokens only; read at play time so dark mode follows.
    const confettiTokens = ["--lwc-gold", "--lwc-accent-bright", "--lwc-success", "--lwc-bronze", "--lwc-silver", "--lwc-gold"];
    const toneTokens: Record<LwcCelebrationTone, string> = {
        accent: "--lwc-accent-bright",
        success: "--lwc-success",
        gold: "--lwc-gold"
    };
    const ease = "cubic-bezier(0.2, 0.8, 0.2, 1)";

    function readStored(): string[] {
        try {
            const parsed: unknown = JSON.parse(localStorage.getItem(storageKey) || "[]");
            return Array.isArray(parsed) ? parsed.filter((value): value is string => typeof value === "string") : [];
        } catch {
            return [];
        }
    }

    function seen(key: string): boolean {
        return memoryKeys.has(key) || readStored().includes(key);
    }

    function remember(key: string): void {
        memoryKeys.add(key);
        if (memoryKeys.size > 200) memoryKeys.delete(memoryKeys.values().next().value!);
        try {
            const keys = readStored().filter(value => value !== key);
            keys.push(key);
            localStorage.setItem(storageKey, JSON.stringify(keys.slice(-200)));
        } catch { /* Memory still prevents a replay on this page. */ }
    }

    function color(token: string): string {
        return getComputedStyle(document.documentElement).getPropertyValue(token).trim() || "#19c3d1";
    }

    // Top-layer dialogs hide anything outside them, so pieces join the open one.
    function host(target: Element): HTMLElement {
        return target.closest<HTMLElement>("dialog[open]") || document.body;
    }

    function layerFor(target: Element, rect: DOMRect, tone: string): HTMLElement {
        const layer = document.createElement("div");
        layer.className = "lwc-celebration";
        layer.setAttribute("aria-hidden", "true");
        layer.style.setProperty("--lwc-celebration-tone", tone);
        layer.style.left = `${rect.left}px`;
        layer.style.top = `${rect.top}px`;
        layer.style.width = `${rect.width}px`;
        layer.style.height = `${rect.height}px`;
        const radius = getComputedStyle(target).borderRadius;
        layer.style.setProperty("--lwc-celebration-radius", radius && radius !== "0px" ? radius : "var(--lwc-radius-md, 8px)");
        host(target).append(layer);
        return layer;
    }

    function piece(layer: HTMLElement, className: string): HTMLElement {
        const element = document.createElement("span");
        element.className = className;
        layer.append(element);
        return element;
    }

    function visible(target: Element): boolean {
        if (!target.isConnected || document.hidden || !target.checkVisibility?.({ checkOpacity: true, checkVisibilityCSS: true })) return false;
        const rect = target.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 && rect.bottom > 0 && rect.top < innerHeight
            && rect.right > 0 && rect.left < innerWidth;
    }

    function stopAll(): void {
        pending.forEach(entry => entry.cancel());
        active.forEach(entry => entry.stop());
    }

    function cancelAnimations(animations: Animation[]): void {
        for (const animation of animations) {
            // Animation cancellation rejects finished, even when setup failed halfway.
            void animation.finished.catch(() => {});
            animation.cancel();
        }
        animations.length = 0;
    }

    function playMoment(target: Element, rect: DOMRect, level: LwcCelebrationLevel, tone: string): void {
        for (const entry of active) if (entry.target === target) entry.stop();
        const spec = levels[level];
        const layer = layerFor(target, rect, tone);
        layer.dataset["level"] = level;
        const animations: Animation[] = [];
        let finished = false;
        let frame = 0;
        let timeout = 0;
        const entry = {
            target,
            stop: () => {
                if (finished) return;
                finished = true;
                cancelAnimationFrame(frame);
                clearTimeout(timeout);
                active.delete(entry);
                cancelAnimations(animations);
                layer.remove();
            }
        };
        active.add(entry);
        const watchTarget = () => {
            if (!visible(target)) { entry.stop(); return; }
            frame = requestAnimationFrame(watchTarget);
        };
        const still = () => {
            cancelAnimations(animations);
            layer.replaceChildren();
            layer.classList.add("is-static");
            piece(layer, "lwc-celebration-halo");
            timeout = window.setTimeout(entry.stop, 2400);
        };
        try {
            frame = requestAnimationFrame(watchTarget);
            if (reduced.matches || typeof Element.prototype.animate !== "function") {
                still();
                return;
            }
            const halo = piece(layer, "lwc-celebration-halo");
            animations.push(halo.animate([
                { opacity: 0, transform: "scale(0.98)" },
                { opacity: 1, transform: "scale(1)", offset: 0.28 },
                { opacity: 0.55, transform: "scale(1.01)", offset: 0.6 },
                { opacity: 0, transform: "scale(1.02)" }
            ], { duration: spec.duration, easing: ease, fill: "both" }));

            if (level !== "glow") {
                const ring = piece(layer, "lwc-celebration-ring");
                const grow = Math.min(48, 14 + Math.max(rect.width, rect.height) * 0.12);
                animations.push(ring.animate([
                    { opacity: 0.9, outlineOffset: "0px" },
                    { opacity: 0, outlineOffset: `${grow}px` }
                ], { duration: Math.round(spec.duration * 0.62), easing: ease, fill: "both", delay: 40 }));
            }

            const cx = rect.width / 2;
            const cy = rect.height / 2;
            const reach = Math.min(170, Math.max(118, Math.max(rect.width, rect.height) * 0.7));
            for (let index = 0; index < spec.sparks; index++) {
                const spark = piece(layer, "lwc-celebration-spark");
                const angle = (index / spec.sparks) * Math.PI * 2 - Math.PI / 2;
                const near = Math.max(rect.width, rect.height) / 2 + 6;
                const far = near + 18 + (index % 2) * 10;
                const rotate = `${(angle * 180) / Math.PI + 90}deg`;
                spark.style.left = `${cx}px`;
                spark.style.top = `${cy}px`;
                animations.push(spark.animate([
                    { opacity: 0, transform: `translate(${Math.cos(angle) * near}px, ${Math.sin(angle) * near}px) rotate(${rotate}) scaleY(0.4)` },
                    { opacity: 1, transform: `translate(${Math.cos(angle) * (near + 8)}px, ${Math.sin(angle) * (near + 8)}px) rotate(${rotate}) scaleY(1)`, offset: 0.35 },
                    { opacity: 0, transform: `translate(${Math.cos(angle) * far}px, ${Math.sin(angle) * far}px) rotate(${rotate}) scaleY(0.3)` }
                ], { duration: 620, easing: ease, fill: "both", delay: 60 + (index % 3) * 24 }));
            }

            for (let index = 0; index < spec.confetti; index++) {
                const bit = piece(layer, index % 3 === 0 ? "lwc-celebration-bit is-dot" : "lwc-celebration-bit");
                bit.style.background = color(confettiTokens[index % confettiTokens.length] || "--lwc-gold");
                bit.style.left = `${cx}px`;
                bit.style.top = `${cy}px`;
                // An upward fan, so pieces arc and settle instead of spraying flat.
                const angle = -Math.PI / 2 + ((index / (spec.confetti - 1)) - 0.5) * Math.PI * 1.25 + Math.sin(index * 7.3) * 0.12;
                const distance = reach * (0.7 + ((index * 37) % 30) / 100);
                const dx = Math.cos(angle) * distance;
                const dy = Math.sin(angle) * distance;
                // Pieces leave from the element's edge, never across its face.
                const edge = Math.min(rect.width, rect.height) / 2;
                const sx = Math.cos(angle) * edge;
                const sy = Math.sin(angle) * edge;
                const spin = ((index % 2) ? 1 : -1) * (220 + (index * 53) % 260);
                animations.push(bit.animate([
                    { opacity: 0, transform: `translate(${sx}px, ${sy}px) rotate(0deg) scale(0.4)` },
                    { opacity: 1, transform: `translate(${dx * 0.72}px, ${dy * 0.82}px) rotate(${spin * 0.5}deg) scale(1)`, offset: 0.34 },
                    { opacity: 1, transform: `translate(${dx}px, ${dy * 0.7 + 22}px) rotate(${spin * 0.8}deg) scale(1)`, offset: 0.7 },
                    { opacity: 0, transform: `translate(${dx * 1.06}px, ${dy * 0.5 + 54}px) rotate(${spin}deg) scale(0.8)` }
                ], { duration: spec.duration - 120 + (index % 4) * 40, easing: "cubic-bezier(0.15, 0.7, 0.35, 1)", fill: "both", delay: (index % 5) * 18 }));
            }

            // Rise and burst give the element itself one small, additive pop.
            if (level !== "glow" && target instanceof HTMLElement) {
                try {
                    animations.push(target.animate([
                        { transform: "scale(1)" },
                        { transform: `scale(${level === "burst" ? 1.06 : 1.035})`, offset: 0.3 },
                        { transform: "scale(1)" }
                    ], { duration: 420, easing: ease, composite: "add" }));
                } catch { /* Additive transforms are optional polish. */ }
            }


            void Promise.all(animations.map(animation => animation.finished)).then(entry.stop, entry.stop);
        } catch {
            // An optional flourish must never break a successful submission.
            still();
        }
    }

    function play(target: Element | null | undefined, options: LwcCelebrationOptions = {}): boolean {
        // This optional effect watches visibility each frame; keep older engines still.
        if (!target || !target.isConnected || typeof target.checkVisibility !== 'function') return false;
        const key = options.once;
        if (key && (seen(key) || [...pending].some(entry => entry.key === key))) return false;
        for (const entry of pending) if (entry.target === target) entry.cancel();
        let timer = 0;
        let frame = 0;
        const entry = {
            target, key,
            cancel: () => {
                clearTimeout(timer);
                cancelAnimationFrame(frame);
                pending.delete(entry);
            }
        };
        const start = () => {
            pending.delete(entry);
            if (!visible(target) || (key && seen(key))) return;
            // Reserve only a moment that actually reaches the screen.
            if (key) remember(key);
            playMoment(target, target.getBoundingClientRect(), options.level || "glow", color(toneTokens[options.tone || "accent"]));
        };
        pending.add(entry);
        try {
            if (options.delay) timer = window.setTimeout(() => { frame = requestAnimationFrame(start); }, options.delay);
            else frame = requestAnimationFrame(start);
        } catch {
            entry.cancel();
            return false;
        }
        return true;
    }

    // Any deliberate input ends every running moment.
    for (const type of ["pointerdown", "keydown", "wheel", "touchstart"] as const) {
        window.addEventListener(type, stopAll, { capture: true, passive: true });
    }
    window.addEventListener("scroll", event => {
        const scroller = event.target;
        if (!(scroller instanceof Element)) { stopAll(); return; }
        // Rendering can position an unrelated board/history scroller. Only a
        // scroll that moves this target invalidates its viewport position;
        // deliberate wheel, touch and keyboard input already cancel everything.
        pending.forEach(entry => { if (scroller.contains(entry.target)) entry.cancel(); });
        active.forEach(entry => { if (scroller.contains(entry.target)) entry.stop(); });
    }, { capture: true, passive: true });
    window.addEventListener("resize", stopAll, { passive: true });

    document.addEventListener("visibilitychange", () => { if (document.hidden) stopAll(); });
    window.addEventListener("pagehide", stopAll);
    reduced.addEventListener("change", stopAll);

    window.LwcCelebrate = { play, skip: stopAll, seen };
})();
