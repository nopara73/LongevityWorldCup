// Browsing the competition as one continuous motion.
//
// - Opening and closing move the whole readable card, keeping identity attached.
// - Stepping between athletes (buttons, arrow keys or a horizontal swipe)
//   gives the new profile a small directional arrival without hiding its content.
// - The profile's ages count in once they are on screen.
// - The shared motion controller owns board movement when rankings change.
// - Closing returns scroll and focus to the row of the athlete you ended on.
//
// This classic script observes content and receives explicit dialog lifecycle
// events for deterministic scroll and focus return. It never delays
// or replaces their own behaviour and never blocks input. The real elements
// stay visible and interactive throughout. Motion waits for the visitor's first press, matching
// the shared motion gate, and prefers-reduced-motion keeps all of it off apart
// from the scroll and focus return.

(() => {
    const modal = document.getElementById("detailsModal");
    if (!modal) return;

    type Source = { readonly row: HTMLElement; readonly slug: string; readonly at: number };
    type CloseReturn = { scrollTop: number; returnFocusTo: HTMLElement | null };
    type Figure = { readonly prefix: string; readonly sign: string; readonly value: number; readonly decimals: number; readonly suffix: string };
    type Swipe = { readonly id: number; readonly x: number; readonly y: number; readonly t: number; dx: number; active: boolean; readonly profile: HTMLElement };

    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const rowSelector = "tr[data-athlete-name], .podium-item[data-athlete-name]";
    const figureSelector = ".age-board-figure, #ageRadarCenterValue, #ageRadarCenterBio";
    const swipeExclusions = "a, button, input, select, textarea, canvas, iframe, [contenteditable]:not([contenteditable='false']), [role='button'], [role='slider'], [role='tablist'], [role='listbox'], [role='menu'], .age-clock-switch, .athlete-share-menu";
    let gateOpen = false;

    function enabled(): boolean {
        return !reduced.matches && (gateOpen || root.dataset["lwcMotion"] === "on");
    }

    function openGate(event: Event): void {
        if (event.isTrusted && !reduced.matches) gateOpen = true;
    }
    window.addEventListener("pointerdown", openGate, true);
    window.addEventListener("keydown", openGate, true);

    // Durations and easings follow the shared motion tokens when present.
    function token(name: string, fallback: number): number {
        const value = parseFloat(getComputedStyle(root).getPropertyValue(name));
        return Number.isFinite(value) && value > 0 ? value : fallback;
    }

    function ease(name: string, fallback: string): string {
        return getComputedStyle(root).getPropertyValue(name).trim() || fallback;
    }

    const standardEase = (): string => ease("--lwc-ease-standard", "cubic-bezier(0.2, 0, 0, 1)");
    const enterEase = (): string => ease("--lwc-ease-enter", "cubic-bezier(0, 0, 0, 1)");

    const content = (): HTMLElement | null => modal.querySelector<HTMLElement>(".modal-content");
    const isOpen = (): boolean => modal.style.display === "block";
    const isClosing = (): boolean => modal.classList.contains("fade-out");
    const currentSlug = (): string => content()?.dataset["athleteSlug"] || "";

    function slugOf(row: Element): string {
        const link = row.querySelector<HTMLAnchorElement>("a.athlete-name[href], a.athlete-profile-link[href]");
        if (!link) return "";
        const match = /^\/athlete\/([^/?#]+)/.exec(new URL(link.href, location.href).pathname);
        if (!match?.[1]) return "";
        try {
            return decodeURIComponent(match[1]).toLowerCase();
        } catch {
            return match[1].toLowerCase();
        }
    }

    function shown(element: Element | null): element is HTMLElement {
        return element instanceof HTMLElement && element.getClientRects().length > 0
            && getComputedStyle(element).visibility !== "hidden";
    }

    function inViewport(rect: DOMRect, margin = 0): boolean {
        return rect.height > 0 && rect.bottom > -margin && rect.top < innerHeight + margin;
    }

    function findRow(slug: string): HTMLElement | null {
        if (!slug) return null;
        for (const row of document.querySelectorAll<HTMLElement>(rowSelector)) {
            if (!modal!.contains(row) && slugOf(row) === slug && shown(row)) return row;
        }
        return null;
    }

    // Move the actual card as one unit. A blank surface above it, with separate
    // portrait/name copies, hid useful content and detached the identity during
    // interrupted layout and paint. The card stays readable and interactive.
    let pendingSource: Source | null = null;
    let openerSlug = "";
    let generation = 0;
    let cardEntrance: Animation | null = null;
    let stepEntrance: Animation | null = null;

    function clearMotion(): void {
        generation++;
        cancelSwipe(false);
        cardEntrance?.cancel();
        cardEntrance = null;
        stepEntrance?.cancel();
        stepEntrance = null;
    }

    function playSafely(action: () => void): void {
        try { action(); } catch { clearMotion(); }
    }

    document.addEventListener("click", event => {
        if (event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        if (!(event.target instanceof Element)) return;
        const row = event.target.closest<HTMLElement>(rowSelector);
        if (!row || modal.contains(row)) return;
        const slug = slugOf(row);
        if (slug) pendingSource = { row, slug, at: performance.now() };
    }, true);

    function enterFromRow(source: Source): void {
        const card = content();
        if (!card || !isOpen() || isClosing() || !source.row.isConnected) return;
        if (card.matches(".guess-mode, .is-loading, .has-load-error") || currentSlug() !== source.slug) return;
        const rowRect = source.row.getBoundingClientRect();
        if (!inViewport(rowRect)) return;
        const cardRect = card.getBoundingClientRect();
        const dy = Math.max(-18, Math.min(18, rowRect.top - cardRect.top));
        cardEntrance?.cancel();
        cardEntrance = card.animate([
            { transform: `translateY(${dy}px) scale(.985)`, opacity: .94 },
            { transform: "none", opacity: 1 }
        ], { duration: token("--lwc-motion-slow", 280), easing: standardEase() });
    }

    function enterStep(): void {
        const card = content();
        const direction = card?.dataset["athleteStep"];
        const profile = card?.querySelector<HTMLElement>("#athlete-profile");
        stepEntrance?.cancel();
        stepEntrance = null;
        if (!profile || !direction || !enabled() || !isOpen() || isClosing()
            || card?.matches(".guess-mode, .is-loading, .has-load-error")) return;
        stepEntrance = profile.animate([
            { transform: `translateX(${direction === "previous" ? -12 : 12}px)`, opacity: .94 },
            { transform: "none", opacity: 1 }
        ], { duration: token("--lwc-motion-base", 200), easing: enterEase() });
    }

    // Closing retains the complete card throughout the existing shared fade.
    // Only the return scroll/focus target changes when paging reached a new row.
    // While the dialog is open the page is held by a fixed body offset. When
    // the athlete on screen is not where the visitor left the board, move that
    // offset so their row is in view for the return.
    function bringRowIntoView(row: HTMLElement): number | null {
        const body = document.body;
        if (body.style.position !== "fixed") return null;
        const saved = -parseFloat(body.style.top || "0") || 0;
        const rect = row.getBoundingClientRect();
        // A visible return target should stay where the visitor left it. The
        // old percentage margins re-centered visible rows near the bottom,
        // especially when the phone rank rail made names wrap onto more lines.
        const head = row.closest("table")?.tHead;
        const bannerBottom = document.querySelector("header[role='banner']")?.getBoundingClientRect().bottom ?? 0;
        const coveredTop = Math.max(0, bannerBottom, ...Array.from(head?.querySelectorAll("th") ?? [])
            .map(cell => cell.getBoundingClientRect().bottom));
        if (rect.top >= coveredTop && rect.bottom <= innerHeight) return null;
        const max = Math.max(0, body.scrollHeight - innerHeight);
        const target = Math.round(Math.min(max, Math.max(0, rect.top + saved - (innerHeight - rect.height) / 2)));
        if (Math.abs(target - saved) < 1) return null;
        body.style.top = `-${target}px`;
        return target;
    }

    function onClosing(state: CloseReturn): void {
        clearMotion();
        resetCounts();
        const slug = currentSlug();
        const row = findRow(slug);
        const scrollTo = row && slug !== openerSlug ? bringRowIntoView(row) : null;
        if (scrollTo !== null) state.scrollTop = scrollTo;
        if (row && slug !== openerSlug) {
            const link = row.querySelector<HTMLElement>("a.athlete-name") || row.querySelector<HTMLElement>("a.athlete-profile-link");
            if (shown(link)) state.returnFocusTo = link;
        }
    }

    // A horizontal swipe drags the profile and steps on release.
    let swipe: Swipe | null = null;

    function horizontallyScrollable(element: Element, boundary: Element): boolean {
        for (let node: Element | null = element; node && node !== boundary; node = node.parentElement) {
            if (node.scrollWidth > node.clientWidth + 1 && /(auto|scroll)/.test(getComputedStyle(node).overflowX)) return true;
        }
        return false;
    }

    function stepButton(direction: "previous" | "next"): HTMLButtonElement | null {
        const button = modal!.querySelector<HTMLButtonElement>(`.modal-content > .athlete-steps:not([hidden]) .athlete-step[data-step="${direction}"]`);
        return button && !button.disabled ? button : null;
    }

    let swipeReturn: Animation | null = null;
    function cancelSwipe(animate = true): void {
        swipeReturn?.cancel();
        swipeReturn = null;
        if (!swipe) return;
        const { profile, active, id } = swipe;
        swipe = null;
        window.LwcTouch?.release(profile);
        if (profile.hasPointerCapture(id)) profile.releasePointerCapture(id);
        if (!active) return;
        const from = profile.style.translate;
        profile.style.translate = "";
        profile.style.opacity = "";
        if (from && animate && enabled()) {
            swipeReturn = profile.animate([{ translate: from }, { translate: "0 0" }], { duration: token("--lwc-motion-fast", 140), easing: standardEase() });
        }
    }

    modal.addEventListener("pointerdown", event => {
        if (event.pointerType === "touch" && !event.isPrimary) { cancelSwipe(); return; }
        if (event.pointerType !== "touch" || !event.isPrimary || !(event.target instanceof Element)) return;
        cancelSwipe(false);
        const card = content();
        const profile = event.target.closest<HTMLElement>("#athlete-profile");
        if (!card || !profile || !isOpen() || isClosing() || card.matches(".guess-mode, .is-loading, .has-load-error")) return;
        if (event.clientX < 20 || event.clientX > innerWidth - 20 || !window.getSelection()?.isCollapsed) return;
        if (event.target.closest(swipeExclusions) || horizontallyScrollable(event.target, profile)) return;
        if (!stepButton("previous") && !stepButton("next")) return;
        swipe = { id: event.pointerId, x: event.clientX, y: event.clientY, t: performance.now(), dx: 0, active: false, profile };
    }, { passive: true });

    modal.addEventListener("pointermove", event => {
        if (!swipe || event.pointerId !== swipe.id) return;
        const dx = event.clientX - swipe.x;
        const dy = event.clientY - swipe.y;
        if (!swipe.active) {
            if (Math.abs(dy) > 12 && Math.abs(dy) > Math.abs(dx)) {
                swipe = null;
                return;
            }
            if (Math.abs(dx) < 12 || Math.abs(dx) < Math.abs(dy) * 1.5) return;
            if (window.LwcTouch && !window.LwcTouch.claim(swipe.profile)) { cancelSwipe(false); return; }
            swipe.active = true;
            // Capture only after horizontal intent; native vertical pan and
            // pinch still cancel the gesture through pointercancel.
            if (event.isTrusted) swipe.profile.setPointerCapture(event.pointerId);
        }
        swipe.dx = dx;
        if (!enabled()) return;
        // Resist where there is no neighbour to reach.
        const shift = stepButton(dx < 0 ? "next" : "previous") ? dx * 0.6 : dx * 0.18;
        swipe.profile.style.translate = `${shift}px 0`;
        swipe.profile.style.opacity = String(1 - Math.min(0.3, Math.abs(shift) / 600));
    }, { passive: true });

    modal.addEventListener("touchmove", event => {
        // Once a horizontal swipe owns the gesture, do not leave the browser
        // panning underneath it and consuming the next tap to stop scrolling.
        // Vertical intent and multiple fingers retain their native behavior.
        if (swipe?.active && event.touches.length === 1 && event.cancelable) event.preventDefault();
    }, { passive: false });

    modal.addEventListener("pointerup", event => {
        if (!swipe || event.pointerId !== swipe.id) return;
        const { dx, t, active, profile } = swipe;
        const velocity = Math.abs(dx) / Math.max(1, performance.now() - t);
        const button = active && (Math.abs(dx) > 64 || (Math.abs(dx) > 24 && velocity > 0.45))
            ? stepButton(dx < 0 ? "next" : "previous") : null;
        if (!button) {
            cancelSwipe();
            return;
        }
        // The dragged athlete leaves from where the finger let go.
        button.click();
        cancelSwipe(false);
    });
    modal.addEventListener("pointercancel", () => cancelSwipe());
    modal.addEventListener("lostpointercapture", event => {
        // Touch starts with implicit capture on the hit child. Moving capture
        // to the profile releases that child without ending this gesture.
        if (swipe && event.pointerId === swipe.id && !swipe.profile.hasPointerCapture(event.pointerId)) cancelSwipe();
    });
    window.addEventListener("blur", () => cancelSwipe(false));
    document.addEventListener("lwc:gesture-cancel", () => cancelSwipe(false));
    document.addEventListener("visibilitychange", () => { if (document.hidden) cancelSwipe(false); });

    // --- Ages count in --------------------------------------------------------------

    const counts = new WeakMap<Element, number>();
    let shownText = new WeakMap<Element, string>();
    const waiting = new Set<Element>();
    const watcher = "IntersectionObserver" in window ? new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            watcher?.unobserve(entry.target);
            if (waiting.delete(entry.target) && entry.target instanceof HTMLElement) countIn(entry.target, null);
        }
    }) : null;

    function parseFigure(text: string): Figure | null {
        const match = /([+\-−]?)(\d+(?:\.\d+)?)/.exec(text);
        if (!match?.[2]) return null;
        const sign = match[1] || "";
        const magnitude = parseFloat(match[2]);
        return {
            prefix: text.slice(0, match.index),
            sign,
            value: sign === "-" || sign === "−" ? -magnitude : magnitude,
            decimals: match[2].split(".")[1]?.length ?? 0,
            suffix: text.slice(match.index + match[0].length)
        };
    }

    // Biological ages start at the calendar age and count to the result;
    // reductions grow from zero; pace of aging settles from 1.
    function startValue(figure: Element, value: number): number {
        const chrono = parseFloat(modal!.querySelector("#chronologicalAge")?.textContent || "");
        switch (figure.id) {
            case "lowestBortzAge":
            case "lowestPhenoAge":
            case "crowdAge":
            case "ageRadarCenterBio":
                return Number.isFinite(chrono) ? chrono : value;
            case "paceOfAging":
            case "bortzPaceOfAging":
                return 1;
            case "chronologicalAge":
                return value;
            default:
                return 0;
        }
    }

    function stopCount(figure: HTMLElement): void {
        const frame = counts.get(figure);
        if (frame !== undefined) cancelAnimationFrame(frame);
        counts.delete(figure);
        delete figure.dataset["lwcCount"];
        figure.style.removeProperty("--lwc-count-color");
    }

    // The figure keeps its real text throughout; only a drawn overlay counts,
    // so reading, copying and assistive technology always get the result.
    function countIn(figure: HTMLElement, previous: string | null): void {
        const text = figure.textContent || "";
        const target = parseFigure(text);
        stopCount(figure);
        shownText.set(figure, text);
        if (!target || !enabled() || !isOpen() || isClosing()) return;
        const before = previous === null ? null : parseFigure(previous);
        const from = before ? before.value : startValue(figure, target.value);
        if (Math.abs(from - target.value) < Math.pow(10, -target.decimals) / 2) return;
        const duration = Math.max(token("--lwc-motion-slow", 280) * 1.6, 420);
        const start = performance.now();
        const format = (value: number): string => {
            const sign = value < 0 ? (target.sign === "−" ? "−" : "-") : (target.sign === "+" && value > 0 ? "+" : "");
            return `${target.prefix}${sign}${Math.abs(value).toFixed(target.decimals)}${target.suffix}`;
        };
        const tick = (now: number): void => {
            const progress = Math.min(1, (now - start) / duration);
            if (progress >= 1 || !enabled() || !isOpen() || isClosing() || !figure.isConnected || (figure.textContent || "") !== text) {
                stopCount(figure);
                return;
            }
            figure.dataset["lwcCount"] = format(from + (target.value - from) * (1 - Math.pow(1 - progress, 3)));
            counts.set(figure, requestAnimationFrame(tick));
        };
        figure.style.setProperty("--lwc-count-color", getComputedStyle(figure).color);
        figure.dataset["lwcCount"] = format(from);
        counts.set(figure, requestAnimationFrame(tick));
    }

    // `fresh` counts from the figure's starting rule; otherwise a changed
    // figure counts on from the value it showed before.
    function scheduleCount(figure: HTMLElement, fresh: boolean): void {
        const text = figure.textContent || "";
        const previous = fresh ? null : shownText.get(figure) ?? null;
        if (!fresh && text === previous) return;
        if (!enabled() || !isOpen() || isClosing() || content()?.classList.contains("is-loading")) {
            shownText.set(figure, text);
            return;
        }
        if (shown(figure) && inViewport(figure.getBoundingClientRect())) {
            waiting.delete(figure);
            countIn(figure, previous);
        } else if (watcher) {
            shownText.set(figure, text);
            stopCount(figure);
            waiting.add(figure);
            watcher.observe(figure);
        }
    }

    function resetCounts(): void {
        waiting.forEach(figure => watcher?.unobserve(figure));
        waiting.clear();
        modal!.querySelectorAll<HTMLElement>("[data-lwc-count]").forEach(stopCount);
        shownText = new WeakMap();
    }

    // --- Dialog lifecycle --------------------------------------------------------------

    let wasOpen = isOpen();
    let observedContent: HTMLElement | null = null;
    let figuresFor = "";

    const contentObserver = new MutationObserver(records => {
        const card = content();
        if (!card) return;
        const own = (name: string) => records.some(record => record.type === "attributes" && record.target === card && record.attributeName === name);
        if (own("class")) {
            if (card.classList.contains("is-loading")) {
                resetCounts();
                figuresFor = "";
                return;
            }
            if (figuresFor !== currentSlug()) {
                // A profile has just finished loading: its figures count in fresh.
                figuresFor = currentSlug();
                card.querySelectorAll<HTMLElement>(figureSelector).forEach(figure => scheduleCount(figure, true));
                return;
            }
        }
        const changed = new Set<HTMLElement>();
        for (const record of records) {
            if (record.type === "attributes") continue;
            const node = record.target instanceof Element ? record.target : record.target.parentElement;
            const figure = node?.closest<HTMLElement>(figureSelector);
            if (figure && card.contains(figure)) changed.add(figure);
        }
        changed.forEach(figure => scheduleCount(figure, false));
    });

    function observeContent(): void {
        const card = content();
        if (!card || card === observedContent) return;
        contentObserver.disconnect();
        observedContent = card;
        contentObserver.observe(card, { attributes: true, attributeFilter: ["data-athlete-step", "class"], childList: true, characterData: true, subtree: true });
    }

    new MutationObserver(() => {
        const open = isOpen();
        if (open && !wasOpen) {
            observeContent();
            const source = pendingSource && performance.now() - pendingSource.at < 1500 ? pendingSource : null;
            pendingSource = null;
            openerSlug = source?.slug || currentSlug();
            const currentGeneration = generation;
            if (source && enabled()) requestAnimationFrame(() => {
                if (currentGeneration === generation && enabled()) playSafely(() => enterFromRow(source));
            });
        } else if (!open && wasOpen) {
            clearMotion();
            resetCounts();
            figuresFor = "";
        }
        wasOpen = open;
    }).observe(modal, { attributes: true, attributeFilter: ["style", "class"] });
    modal.addEventListener("athlete-dialog-loading", event => {
        if (!isOpen() || isClosing()) openerSlug = (event as CustomEvent<{ slug: string }>).detail.slug;
        clearMotion();
        resetCounts();
        figuresFor = "";
    });
    modal.addEventListener("athlete-dialog-closing", event => onClosing((event as CustomEvent<CloseReturn>).detail));
    modal.addEventListener("athlete-dialog-closed", clearMotion);
    modal.addEventListener("athlete-dialog-ready", () => playSafely(enterStep));
    observeContent();

    reduced.addEventListener("change", () => {
        if (!reduced.matches) return;
        gateOpen = false;
        clearMotion();
        resetCounts();
    });
})();
