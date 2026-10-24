// Browsing the competition as one continuous motion.
//
// - Opening a profile from a board row or podium card grows that row into the
//   dialog: its portrait, name and rank travel into the profile header.
//   Closing shrinks the profile back into the row of the athlete on screen.
// - Stepping between athletes (buttons, arrow keys or a horizontal swipe)
//   slides the old athlete out one way while the next arrives from the other.
// - The profile's ages count in once they are on screen.
// - The shared motion controller owns board movement when rankings change.
// - Closing returns scroll and focus to the row of the athlete you ended on.
//
// This classic script observes content and receives explicit dialog lifecycle
// events for deterministic scroll and focus return. It never delays
// or replaces their own behaviour and never blocks input: every moving copy
// lives in an inert, pointer-transparent layer and the real elements stay
// interactive throughout. Motion waits for the visitor's first press, matching
// the shared motion gate, and prefers-reduced-motion keeps all of it off apart
// from the scroll and focus return.

(() => {
    const modal = document.getElementById("detailsModal");
    if (!modal) return;

    type Hold = { readonly element: HTMLElement; readonly animation: Animation };
    type Source = { readonly row: HTMLElement; readonly slug: string; readonly at: number };
    type CloseReturn = { scrollTop: number; returnFocusTo: HTMLElement | null };
    type Figure = { readonly prefix: string; readonly sign: string; readonly value: number; readonly decimals: number; readonly suffix: string };
    type Leaving = { readonly at: number; readonly copies: ReadonlyArray<readonly [HTMLElement, DOMRect, Element]> };
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
    const exitEase = (): string => ease("--lwc-ease-exit", "cubic-bezier(0.3, 0, 1, 1)");
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
            if (!modal!.contains(row) && shown(row) && slugOf(row) === slug) return row;
        }
        return null;
    }

    function portraitOf(row: Element): HTMLImageElement | null {
        const image = row.querySelector<HTMLImageElement>("img.portrait, img.podium-portrait");
        return shown(image) ? image as HTMLImageElement : null;
    }

    function nameOf(row: Element): HTMLElement | null {
        const name = row.querySelector<HTMLElement>("a.athlete-name");
        return shown(name) ? name : null;
    }

    function rankOf(row: Element): HTMLElement | null {
        const rank = row.querySelector<HTMLElement>(".rank, .podium-rank");
        return shown(rank) ? rank : null;
    }

    const dialogPortrait = (): HTMLImageElement | null => modal.querySelector<HTMLImageElement>("#modalProfilePic");
    const dialogTitle = (): HTMLElement | null => modal.querySelector<HTMLElement>("#athleteName");

    function dialogRank(): HTMLElement | null {
        for (const selector of ["#athleteName .modal-rank-text", "#athleteName .modal-rank-icon", "#athleteRankings .league-link[data-competition='ultimate'] strong"]) {
            const element = modal!.querySelector<HTMLElement>(selector);
            if (shown(element)) return element;
        }
        return null;
    }

    function firstTextRun(element: Element): Text | null {
        for (const node of element.childNodes) {
            if (node instanceof Text && node.data.trim()) return node;
        }
        return null;
    }

    // The visible extent of an element's text, or of its first text run.
    function textRect(element: Element, firstRunOnly = false): DOMRect | null {
        const range = document.createRange();
        const run = firstRunOnly ? firstTextRun(element) : element;
        if (!run) return null;
        range.selectNodeContents(run);
        const rect = range.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0 ? rect : null;
    }

    // --- Moving copies -----------------------------------------------------------

    // One fixed layer above the dialog holds every copy. It is inert, hidden
    // from assistive technology and never takes pointer input.
    let layerElement: HTMLDivElement | null = null;
    function layer(): HTMLDivElement {
        if (layerElement?.isConnected) return layerElement;
        layerElement = document.createElement("div");
        layerElement.className = "athlete-motion-layer";
        layerElement.setAttribute("aria-hidden", "true");
        layerElement.inert = true;
        Object.assign(layerElement.style, { position: "fixed", inset: "0", zIndex: "2147483000", pointerEvents: "none", overflow: "hidden" });
        document.body.append(layerElement);
        return layerElement;
    }

    function place<T extends HTMLElement>(element: T, rect: DOMRect): T {
        Object.assign(element.style, {
            position: "absolute",
            left: `${rect.left}px`,
            top: `${rect.top}px`,
            width: `${rect.width}px`,
            height: `${rect.height}px`,
            margin: "0",
            boxSizing: "border-box",
            transformOrigin: "0 0",
            pointerEvents: "none"
        });
        element.dataset["athleteMotionCopy"] = "";
        layer().append(element);
        return element;
    }

    // A copy leaves once its own animations end (or are cancelled); the
    // timer only guards against a copy whose animations never resolve.
    function retire(element: HTMLElement): void {
        const remove = (): void => element.remove();
        Promise.allSettled(element.getAnimations().map(animation => animation.finished)).then(remove);
        window.setTimeout(remove, 4000);
    }

    // The transform that puts a copy placed at `to` over `from`, scaled
    // uniformly by height so text and round portraits keep their shape.
    function travel(from: DOMRect, to: DOMRect, center = false): string {
        const scale = from.height / to.height;
        const x = center ? from.left + from.width / 2 - to.width * scale / 2 : from.left;
        const y = center ? from.top + from.height / 2 - to.height * scale / 2 : from.top;
        return `translate(${x - to.left}px, ${y - to.top}px) scale(${scale})`;
    }

    function copyText(text: string, styleSource: Element, rect: DOMRect): HTMLElement {
        const style = getComputedStyle(styleSource);
        const copy = document.createElement("span");
        copy.textContent = text;
        Object.assign(copy.style, {
            fontFamily: style.fontFamily,
            fontSize: style.fontSize,
            fontWeight: style.fontWeight,
            fontStyle: style.fontStyle,
            letterSpacing: style.letterSpacing,
            lineHeight: `${rect.height}px`,
            color: style.color,
            textShadow: style.textShadow,
            whiteSpace: "nowrap"
        });
        return place(copy, new DOMRect(rect.left, rect.top, rect.width + 2, rect.height));
    }

    function copyElement(source: HTMLElement, rect: DOMRect): HTMLElement {
        const style = getComputedStyle(source);
        const copy = source.cloneNode(true) as HTMLElement;
        copy.removeAttribute("id");
        copy.querySelectorAll("[id]").forEach(child => child.removeAttribute("id"));
        Object.assign(copy.style, { font: style.font, color: style.color, whiteSpace: "nowrap", display: "inline-block", textAlign: "center" });
        return place(copy, rect);
    }

    function copyPortrait(image: HTMLImageElement, styleSource: HTMLImageElement, rect: DOMRect): HTMLImageElement {
        const style = getComputedStyle(styleSource);
        const copy = document.createElement("img");
        copy.alt = "";
        copy.decoding = "sync";
        copy.src = image.currentSrc || image.src;
        Object.assign(copy.style, {
            objectFit: "cover",
            borderRadius: style.borderRadius || "50%",
            border: style.border,
            boxShadow: style.boxShadow,
            backgroundColor: style.backgroundColor
        });
        return place(copy, rect);
    }

    function copySurface(styleSource: HTMLElement, rect: DOMRect): HTMLElement {
        const style = getComputedStyle(styleSource);
        const copy = document.createElement("div");
        Object.assign(copy.style, {
            backgroundColor: style.backgroundColor,
            backgroundImage: style.backgroundImage,
            borderRadius: style.borderTopLeftRadius,
            boxShadow: style.boxShadow
        });
        return place(copy, rect);
    }

    function box(rect: DOMRect): Keyframe {
        return { left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px` };
    }

    // Keep a real element transparent while its copy travels. Holds end by
    // themselves, so nothing can stay hidden if a later step fails.
    const holds = new Set<Hold>();
    function hold(element: Element | null, duration: number): void {
        if (!(element instanceof HTMLElement)) return;
        const animation = element.animate([{ opacity: 0 }, { opacity: 0 }], { duration });
        const entry = { element, animation };
        holds.add(entry);
        animation.finished.catch(() => undefined).finally(() => holds.delete(entry));
    }

    // Dialog elements return at once when it closes; board rows keep their
    // hold until the returning copies have landed on them.
    function releaseHolds(all: boolean): void {
        holds.forEach(entry => {
            if (!all && !modal!.contains(entry.element)) return;
            entry.animation.cancel();
            holds.delete(entry);
        });
    }

    // --- Opening: the row grows into the profile -----------------------------------

    let pendingSource: Source | null = null;
    let openerSlug = "";
    let generation = 0;
    let cardEntrance: Animation | null = null;

    function clearMotion(): void {
        generation++;
        cancelSwipe(false);
        cardEntrance?.cancel();
        cardEntrance = null;
        releaseHolds(true);
        layerElement?.getAnimations({ subtree: true }).forEach(animation => animation.cancel());
        layerElement?.replaceChildren();
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

    function morphOpen(source: Source): void {
        const card = content();
        if (!card || !isOpen() || isClosing() || !source.row.isConnected) return;
        // A loading profile or a guessing round keeps the ordinary entrance.
        if (card.matches(".guess-mode, .is-loading, .has-load-error") || currentSlug() !== source.slug) return;
        const rowRect = source.row.getBoundingClientRect();
        if (!inViewport(rowRect)) return;

        // The morph is the entrance: retire the card's own settle-in.
        card.getAnimations().forEach(animation => {
            if (animation instanceof CSSAnimation && animation.animationName === "athlete-frame-in") animation.finish();
        });

        const duration = token("--lwc-motion-slow", 280);
        const easing = standardEase();
        const cardRect = card.getBoundingClientRect();
        const surface = copySurface(card, cardRect);
        const total = duration + 80;
        const landed = duration / total;
        const radius = surface.style.borderRadius;
        surface.dataset["athleteMotionCopy"] = "surface";
        surface.animate([
            { ...box(rowRect), borderRadius: getComputedStyle(source.row).borderTopLeftRadius || "8px", opacity: 0.55, easing },
            { opacity: 1, offset: landed * 0.35 },
            { ...box(cardRect), borderRadius: radius, opacity: 1, offset: landed },
            { ...box(cardRect), borderRadius: radius, opacity: 0 }
        ], { duration: total });
        retire(surface);
        // The real card fades in over the growing surface; it takes input throughout.
        cardEntrance = card.animate([{ opacity: 0 }, { opacity: 0, offset: 0.4 }, { opacity: 1 }], { duration });

        const title = dialogTitle();
        const pairs: Array<[HTMLElement | null, HTMLElement | null, string]> = [
            [portraitOf(source.row), dialogPortrait(), "portrait"],
            [nameOf(source.row), title, "name"],
            [rankOf(source.row), dialogRank(), "rank"]
        ];
        for (const [from, to, part] of pairs) {
            if (!from || !to || !shown(to)) continue;
            const fromRect = part === "name" ? textRect(from) : from.getBoundingClientRect();
            const toRect = part === "name" ? textRect(to, true) : to.getBoundingClientRect();
            if (!fromRect || !toRect || !inViewport(fromRect) || !toRect.width) continue;
            const run = firstTextRun(to);
            const copy = part === "portrait" ? copyPortrait(from as HTMLImageElement, to as HTMLImageElement, toRect)
                : part === "name" ? (run ? copyText(run.data.trim(), to, toRect) : null)
                : copyElement(to, toRect);
            if (!copy) continue;
            copy.dataset["athleteMotionCopy"] = part;
            const placed = copy.getBoundingClientRect();
            const color = part === "portrait" ? {} : { color: getComputedStyle(from).color };
            copy.animate([
                { transform: travel(fromRect, placed, part === "rank"), ...color },
                { transform: "none", ...(part === "portrait" ? {} : { color: copy.style.color }) }
            ], { duration, easing });
            // The rank crosses the name on its way, so it only firms up as it lands.
            copy.animate(part === "rank"
                ? [{ opacity: 0 }, { opacity: 0, offset: 0.35 }, { opacity: 1, offset: 0.75 }, { opacity: 1, offset: 0.82 }, { opacity: 0 }]
                : [{ opacity: 1 }, { opacity: 1, offset: 0.82 }, { opacity: 0 }], { duration: duration + 90 });
            retire(copy);
            hold(to, duration);
            hold(from, duration + 60);
        }
    }

    // --- Closing: the profile shrinks back into its row --------------------------

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

    function morphClose(row: HTMLElement): void {
        const card = content();
        if (!card) return;
        const rowRect = row.getBoundingClientRect();
        if (!inViewport(rowRect)) return;
        const duration = token("--lwc-motion-move", 260);
        const easing = standardEase();
        const cardRect = card.getBoundingClientRect();

        const surface = copySurface(card, cardRect);
        surface.dataset["athleteMotionCopy"] = "surface";
        surface.animate([
            { ...box(cardRect), opacity: 1, easing },
            { opacity: 0.9, offset: 0.6 },
            { ...box(rowRect), borderRadius: getComputedStyle(row).borderTopLeftRadius || "8px", opacity: 0 }
        ], { duration, fill: "forwards" });
        retire(surface);

        const pairs: Array<[HTMLElement | null, HTMLElement | null, string]> = [
            [dialogPortrait(), portraitOf(row), "portrait"],
            [dialogTitle(), nameOf(row), "name"],
            [dialogRank(), rankOf(row), "rank"]
        ];
        for (const [from, to, part] of pairs) {
            if (!shown(from) || !to) continue;
            const fromRect = part === "name" ? textRect(from, true) : from.getBoundingClientRect();
            const toRect = part === "name" ? textRect(to) : to.getBoundingClientRect();
            if (!fromRect || !toRect || !inViewport(fromRect) || !inViewport(toRect) || !fromRect.width) continue;
            const run = firstTextRun(from);
            const copy = part === "portrait" ? copyPortrait(to as HTMLImageElement, from as HTMLImageElement, fromRect)
                : part === "name" ? (run ? copyText(run.data.trim(), from, fromRect) : null)
                : copyElement(from, fromRect);
            if (!copy) continue;
            copy.dataset["athleteMotionCopy"] = part;
            copy.animate([
                { transform: "none", ...(part === "portrait" ? {} : { color: copy.style.color }), easing },
                { transform: travel(toRect, fromRect, part === "rank"), ...(part === "portrait" ? {} : { color: getComputedStyle(to).color }) }
            ], { duration, fill: "forwards" });
            copy.animate(part === "rank"
                ? [{ opacity: 1 }, { opacity: 0, offset: 0.5 }, { opacity: 0 }]
                : [{ opacity: 1 }, { opacity: 1, offset: 0.8 }, { opacity: 0 }], { duration, fill: "forwards" });
            retire(copy);
            hold(to, duration);
        }
        // The card's copy carries the motion; the real card steps aside at once.
        hold(card, duration + 240);
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
        if (row && enabled() && !content()?.matches(".guess-mode, .is-loading, .has-load-error")) playSafely(() => morphClose(row));
    }

    // --- Stepping: the old athlete leaves one way, the next arrives ----------------

    let leaving: Leaving | null = null;

    function snapshotLeaving(): void {
        const card = content();
        leaving = null;
        if (!card || !isOpen() || isClosing() || !enabled() || card.matches(".is-loading, .guess-mode")) return;
        const copies: Array<readonly [HTMLElement, DOMRect, Element]> = [];
        for (const source of [dialogPortrait(), dialogTitle()]) {
            if (!shown(source)) continue;
            const rect = source.getBoundingClientRect();
            if (!inViewport(rect)) continue;
            const copy = source.cloneNode(source.tagName !== "IMG") as HTMLElement;
            copy.removeAttribute("id");
            copy.querySelectorAll("[id]").forEach(child => child.removeAttribute("id"));
            if (copy instanceof HTMLImageElement) copy.loading = "eager";
            copies.push([copy, rect, source]);
        }
        leaving = { at: performance.now(), copies };
    }

    function playLeaving(direction: string): void {
        const snapshot = leaving;
        leaving = null;
        if (!snapshot || performance.now() - snapshot.at > 250 || !enabled()) return;
        const sign = direction === "previous" ? 1 : -1;
        const duration = token("--lwc-motion-base", 200);
        const distance = Math.min(96, innerWidth * 0.18);
        for (const [copy, rect, source] of snapshot.copies) {
            const style = getComputedStyle(source);
            Object.assign(copy.style, {
                font: style.font,
                color: style.color,
                textAlign: style.textAlign,
                borderRadius: style.borderRadius,
                border: style.border,
                boxShadow: style.boxShadow,
                objectFit: "cover",
                padding: style.padding
            });
            place(copy, rect).dataset["athleteMotionCopy"] = "leaving";
            copy.animate([
                { opacity: 1, translate: "0 0" },
                { opacity: 0, translate: `${sign * distance}px 0` }
            ], { duration, easing: exitEase(), fill: "forwards" });
            retire(copy);
        }
    }

    modal.addEventListener("click", event => {
        if (event.target instanceof Element && event.target.closest(".athlete-step:not(:disabled)")) snapshotLeaving();
    }, true);
    document.addEventListener("keydown", event => {
        if ((event.key === "ArrowLeft" || event.key === "ArrowRight") && isOpen()
            && !event.altKey && !event.ctrlKey && !event.metaKey && !event.shiftKey) snapshotLeaving();
    }, true);

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
        const direction = card.dataset["athleteStep"];
        if (own("data-athlete-step") && direction) playSafely(() => playLeaving(direction));
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
            releaseHolds(true);
            const currentGeneration = generation;
            if (source && enabled()) requestAnimationFrame(() => {
                if (currentGeneration === generation && enabled()) playSafely(() => morphOpen(source));
            });
        } else if (!open && wasOpen) {
            releaseHolds(false);
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
    modal.addEventListener("athlete-dialog-closed", () => releaseHolds(false));
    observeContent();

    reduced.addEventListener("change", () => {
        if (!reduced.matches) return;
        gateOpen = false;
        clearMotion();
        resetCounts();
    });
})();
