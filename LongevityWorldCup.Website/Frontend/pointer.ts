// Shared desktop pointer and keyboard layer: the desktop counterpart of
// touch.ts. pointer.css owns how every state looks; this classic script only
// decides when something should show.
//
// - Input modality: html[data-lwc-input] is "pointer", "keyboard" or "touch",
//   so hover styling never sticks after a finger tap on hybrid screens.
// - Tooltips: one shared tip replaces native title tooltips on icons, flags,
//   counts and truncated text. It waits before the first tip, then follows
//   quickly while the visitor skims, and flips or slides to stay on screen.
//   Badges keep their own richer popover from badges.ts.
// - Focus: the control keeps its outline while an outer halo glides between
//   keyboard stops. Modal dialogs, popovers and fields keep their own treatment.
// - Lists: arrow keys move through rows, tabs, chips and menus that do not
//   already handle them; Home and End jump to the ends. Reaching an end
//   nudges the ring, and Escape pulses it where focus returns.
//
// Nothing here changes wording, colours or what a control does: it only
// moves focus between controls that already exist. Reduced motion keeps
// every behaviour and drops the travel.

interface LwcPointerApi {
    readonly modality: () => "pointer" | "keyboard" | "touch";
    readonly refreshRing: () => void;
    readonly hideTip: () => void;
}

interface Window {
    LwcPointer: LwcPointerApi;
}

(() => {
    if (window.LwcPointer) return;
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const forcedColors = window.matchMedia("(forced-colors: active)");
    const supportsPopover = typeof HTMLElement.prototype.showPopover === "function";

    // ---------------------------------------------------------------- modality
    type Modality = "pointer" | "keyboard" | "touch";
    // Phones start as touch, so nothing hover-only is ever armed before the first tap.
    let modality: Modality = window.matchMedia("(hover: none)").matches ? "touch" : "pointer";
    function setModality(next: Modality): void {
        if (modality === next) return;
        modality = next;
        root.dataset.lwcInput = next;
        if (next !== "keyboard") hideRing();
    }
    root.dataset.lwcInput = modality;

    const navigationKeys = new Set(["Tab", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End", "PageUp", "PageDown", "Escape", "Enter", " "]);
    document.addEventListener("keydown", event => {
        if (event.metaKey || event.ctrlKey || event.altKey || event.isComposing) return;
        if (navigationKeys.has(event.key)) setModality("keyboard");
    }, true);
    document.addEventListener("pointerdown", event => {
        setModality(event.pointerType === "touch" ? "touch" : "pointer");
    }, true);
    // A mouse that moves after keyboard use takes hover back, but keeps the ring
    // until the next click so a nudge of the mouse does not lose the visitor's place.
    document.addEventListener("pointermove", event => {
        if (event.pointerType === "mouse" && modality === "touch") {
            setModality("pointer");
            pointerOver(event);
        }
    }, { passive: true, capture: true });

    // ----------------------------------------------------------------- tooltip
    const tipOwnTitle = "data-lwc-tip-title";
    // Surfaces with their own richer hover card or popover keep it.
    const tipExcluded = ".badge-class, iframe, [data-lwc-no-tip], .lwc-tip";
    // The leaderboard name already opens a preview card; its title only doubles it.
    const tipSilenced = "#athleteLeaderboard .athlete-name, .podium-item .athlete-name";
    const truncationProbe = "td, th, a, button, span, p, h1, h2, h3, h4, label, li, div";

    const tip = document.createElement("div");
    tip.className = "lwc-tip";
    tip.id = "lwc-tip";
    tip.setAttribute("role", "tooltip");
    tip.hidden = true;
    if (supportsPopover) tip.setAttribute("popover", "manual");
    const tipText = document.createElement("span");
    tipText.className = "lwc-tip-text";
    tip.append(tipText);

    let tipOwner: HTMLElement | null = null;
    let tipShowTimer = 0;
    let tipHideTimer = 0;
    let tipWarmUntil = 0;
    let tipAddedDescribedBy = false;
    let tipAddedLabel: string | null = null;
    let tipFrame = 0;
    let tipSource: "pointer" | "keyboard" = "pointer";
    let tipTouchReading = false;
    const coldDelay = 520;
    const warmDelay = 60;
    const warmWindow = 700;

    function mountTip(owner: HTMLElement): void {
        // A popover outside a modal is still inert. Keep long explanations
        // inside the modal's focus and pointer boundary.
        const host = owner.closest("dialog:modal") || document.body;
        if (tip.parentElement !== host) host.append(tip);
    }

    function textFor(element: HTMLElement): string {
        const title = element.getAttribute("title");
        if (title !== null) return title;
        const stored = element.getAttribute(tipOwnTitle);
        if (stored !== null) return stored;
        if (element.dataset.lwcTip) return element.dataset.lwcTip;
        return (element.textContent || "").replace(/\s+/g, " ").trim();
    }

    function isTruncated(element: HTMLElement): boolean {
        if (element.scrollWidth <= element.clientWidth + 1) return false;
        const style = getComputedStyle(element);
        return style.textOverflow === "ellipsis" && style.overflowX !== "visible";
    }

    function tipTargetFrom(start: EventTarget | null): HTMLElement | null {
        if (!(start instanceof Element)) return null;
        if (start.closest(tipExcluded)) return null;
        const titled = start.closest<HTMLElement>(`[title], [${tipOwnTitle}], [data-lwc-tip]`);
        if (titled && !titled.closest(tipExcluded) && !(titled instanceof SVGElement)) return titled;
        // Truncated text: look at the hovered element and a couple of ancestors.
        let probe: Element | null = start;
        for (let depth = 0; probe && depth < 3; depth++, probe = probe.parentElement) {
            if (probe instanceof HTMLElement && probe.matches(truncationProbe) && isTruncated(probe)) return probe;
        }
        return null;
    }

    // Lift the native title off while the shared tip is in charge, so the
    // browser never draws its own box on top. The accessible name survives:
    // icon-only controls get the same words as a label first.
    function adoptTitle(element: HTMLElement): void {
        const title = element.getAttribute("title");
        if (title === null) return;
        const hasName = element.hasAttribute("aria-label") || element.hasAttribute("aria-labelledby")
            || (element.textContent || "").trim().length > 0
            || !!element.querySelector("img[alt]:not([alt=''])");
        if (!hasName && title.trim()) {
            element.setAttribute("aria-label", title);
            tipAddedLabel = title;
        }
        element.setAttribute(tipOwnTitle, title);
        element.removeAttribute("title");
    }

    function releaseTitle(element: HTMLElement): void {
        const stored = element.getAttribute(tipOwnTitle);
        if (stored === null) return;
        element.removeAttribute(tipOwnTitle);
        // Page code may have set a fresh title meanwhile; that one wins.
        if (!element.hasAttribute("title")) element.setAttribute("title", stored);
        if (tipAddedLabel !== null && element.getAttribute("aria-label") === tipAddedLabel) element.removeAttribute("aria-label");
        tipAddedLabel = null;
    }

    function placeTip(owner: HTMLElement): void {
        const rect = owner.getBoundingClientRect();
        const margin = 8;
        const gap = 9;
        const viewportWidth = document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight;
        tip.style.maxWidth = `${Math.min(320, viewportWidth - margin * 2)}px`;
        tip.style.maxHeight = `${viewportHeight - margin * 2}px`;
        const width = tip.offsetWidth;
        const height = tip.offsetHeight;
        const centre = rect.left + rect.width / 2;
        const left = Math.min(Math.max(margin, centre - width / 2), viewportWidth - width - margin);
        const above = rect.top - gap - height;
        const fitsAbove = above >= margin;
        const fitsBelow = rect.bottom + gap + height <= viewportHeight - margin;
        const placeBelow = !fitsAbove && fitsBelow;
        const top = Math.max(margin, Math.min(placeBelow ? rect.bottom + gap : above, viewportHeight - height - margin));
        tip.dataset.side = placeBelow ? "bottom" : "top";
        tip.style.left = `${Math.round(left)}px`;
        tip.style.top = `${Math.round(top)}px`;
        const arrow = Math.min(Math.max(12, centre - left), width - 12);
        tip.style.setProperty("--lwc-tip-arrow", `${Math.round(arrow)}px`);
        const scrollable = tip.scrollHeight > tip.clientHeight;
        tip.toggleAttribute("data-scrollable", scrollable);
        if (scrollable) tip.tabIndex = 0;
        else tip.removeAttribute("tabindex");
    }

    function showTip(owner: HTMLElement): void {
        const text = textFor(owner);
        if (tipOwner !== owner) return;
        if (!text || !owner.isConnected || !visible(owner)) { hideTip(true); return; }
        mountTip(owner);
        window.clearTimeout(tipHideTimer);
        if (tipOwner && tipOwner !== owner) detachTipOwner();
        tipOwner = owner;
        tipText.textContent = text;
        const wasOpen = !tip.hidden;
        tip.hidden = false;
        if (supportsPopover && !tip.matches(":popover-open")) {
            try { tip.showPopover(); } catch { /* not connected yet */ }
        }
        placeTip(owner);
        const described = owner.getAttribute("aria-describedby") || "";
        if (!described.split(/\s+/).includes(tip.id)) {
            owner.setAttribute("aria-describedby", `${described} ${tip.id}`.trim());
            tipAddedDescribedBy = true;
        }
        // While skimming from one tip to the next, the tip slides instead of fading.
        tip.classList.toggle("is-gliding", wasOpen);
        cancelAnimationFrame(tipFrame);
        tipFrame = requestAnimationFrame(() => {
            tipFrame = 0;
            if (tipOwner === owner) tip.classList.add("is-open");
        });
    }

    function detachTipOwner(): void {
        tipTouchReading = false;
        if (!tipOwner) return;
        if (tipAddedDescribedBy) {
            const rest = (tipOwner.getAttribute("aria-describedby") || "").split(/\s+/).filter(id => id && id !== tip.id);
            if (rest.length) tipOwner.setAttribute("aria-describedby", rest.join(" "));
            else tipOwner.removeAttribute("aria-describedby");
        }
        tipAddedDescribedBy = false;
        releaseTitle(tipOwner);
        tipOwner = null;
    }

    function hideTip(immediate = false): void {
        window.clearTimeout(tipShowTimer);
        window.clearTimeout(tipHideTimer);
        cancelAnimationFrame(tipFrame);
        tipFrame = 0;
        if (!tip.hidden) tipWarmUntil = performance.now() + warmWindow;
        detachTipOwner();
        tip.classList.remove("is-open", "is-gliding");
        const finish = () => {
            if (tip.classList.contains("is-open")) return;
            tip.hidden = true;
            if (supportsPopover && tip.matches(":popover-open")) tip.hidePopover();
        };
        if (immediate || reduced.matches) finish();
        else tipHideTimer = window.setTimeout(finish, 120);
    }

    function scheduleTip(owner: HTMLElement, delay: number, source: "pointer" | "keyboard" = "pointer"): void {
        window.clearTimeout(tipShowTimer);
        window.clearTimeout(tipHideTimer);
        cancelAnimationFrame(tipFrame);
        if (tipOwner !== owner) detachTipOwner();
        // Own the pending delay as well as the visible tip. Every exit can now
        // cancel it and restore the title, including before the first paint.
        tipOwner = owner;
        tipSource = source;
        // Keyboard focus does not arm the browser's mouse tooltip. Preserve
        // its native name until a pointer actually takes ownership.
        if (source === "pointer") adoptTitle(owner);
        if (owner.closest(tipSilenced)) {
            // Keep the native box away without drawing a second card.
            tip.classList.remove("is-open", "is-gliding");
            tip.hidden = true;
            if (supportsPopover && tip.matches(":popover-open")) tip.hidePopover();
            return;
        }
        const wait = performance.now() < tipWarmUntil || !tip.hidden ? warmDelay : delay;
        tipShowTimer = window.setTimeout(() => {
            if (tipOwner !== owner) return;
            // Truncation requires layout. Probe only after hover intent, never
            // synchronously for every pointerover on a large leaderboard.
            const target = tipTargetFrom(owner);
            if (!target) { hideTip(true); return; }
            if (target !== owner) {
                detachTipOwner();
                tipOwner = target;
                if (source === "pointer") adoptTitle(target);
            }
            showTip(target);
        }, wait);
    }

    function leaveTip(): void {
        if (tipTouchReading && modality === "touch") return;
        window.clearTimeout(tipShowTimer);
        if (tip.hidden) { hideTip(true); return; }
        window.clearTimeout(tipHideTimer);
        // A short bridge lets the pointer enter the tooltip itself, including
        // long content with a native scrollbar.
        tipHideTimer = window.setTimeout(() => hideTip(), 160);
    }

    function pointerOver(event: PointerEvent): void {
        // Hiding a touch-owned tip can expose the stationary mouse's old target.
        // That boundary event is not new mouse intent; actual movement re-arms it.
        if (event.pointerType === "touch" || event.buttons || (event.pointerType === "mouse" && modality === "touch")) return;
        if (event.target instanceof Node && tip.contains(event.target)) {
            window.clearTimeout(tipHideTimer);
            return;
        }
        const start = event.target instanceof Element ? event.target : null;
        if (start?.closest(tipExcluded)) { hideTip(true); return; }
        const titled = start?.closest(`[title], [${tipOwnTitle}], [data-lwc-tip]`);
        const target = titled instanceof HTMLElement ? titled : start instanceof HTMLElement ? start : start?.parentElement;
        if (!target) {
            if (tipOwner && !(event.target instanceof Node && tipOwner.contains(event.target))) hideTip();
            return;
        }
        if (target === tipOwner) { tipSource = "pointer"; adoptTitle(target); return; }
        scheduleTip(target, coldDelay);
    }
    document.addEventListener("pointerover", pointerOver, { passive: true });

    document.addEventListener("pointerout", event => {
        if (!tipOwner) return;
        const next = event.relatedTarget;
        if (next instanceof Node && (tipOwner.contains(next) || tip.contains(next))) return;
        if (tipSource === "keyboard" && document.activeElement === tipOwner) return;
        leaveTip();
    }, { passive: true });

    document.addEventListener("pointerdown", event => {
        if (event.target instanceof Node && tip.contains(event.target)) {
            if (event.pointerType === "touch") tipTouchReading = true;
            window.clearTimeout(tipHideTimer);
        } else hideTip(true);
    }, true);
    // Native scrolling captures, then cancels, the finger's pointer stream.
    // The explanation owns that gesture; outside interruptions still release it.
    for (const type of ["pointercancel", "gotpointercapture"]) document.addEventListener(type, event => {
        if (!(event.target instanceof Node && tip.contains(event.target))) hideTip(true);
    }, true);
    window.addEventListener("scroll", event => {
        if (tipOwner && !(event.target instanceof Node && tip.contains(event.target))) hideTip(true);
    }, { passive: true, capture: true });
    window.addEventListener("blur", () => hideTip(true));
    window.addEventListener("resize", () => hideTip(true), { passive: true });

    // ------------------------------------------------------------- focus ring
    const ring = document.createElement("div");
    ring.className = "lwc-focus-ring";
    ring.setAttribute("aria-hidden", "true");
    ring.hidden = true;
    const ringExcludedHosts = "dialog:modal, [role='dialog'], [popover]:popover-open, .lwc-tip";
    let ringTarget: HTMLElement | null = null;
    let ringFrame = 0;
    let ringIdleFrames = 0;
    let lastRect = "";

    function ringAllowed(element: HTMLElement): boolean {
        if (forcedColors.matches) return false;
        if (element === document.body || element === document.documentElement) return false;
        if (element.closest(ringExcludedHosts)) return false;
        // Text fields keep their own focus treatment from form-controls.css.
        if (element.matches("input, textarea, select") || element.isContentEditable) return false;
        return element.matches(":focus-visible");
    }

    function hideRing(): void {
        ringTarget = null;
        root.removeAttribute("data-lwc-ring");
        ring.classList.remove("is-on");
        for (const animation of ring.getAnimations()) animation.cancel();
        cancelAnimationFrame(ringFrame);
        ringFrame = 0;
    }

    function measureRing(): boolean {
        if (!ringTarget || !ringTarget.isConnected) { hideRing(); return false; }
        const rect = ringTarget.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) { hideRing(); return false; }
        const key = `${rect.left}|${rect.top}|${rect.width}|${rect.height}`;
        if (key === lastRect) return false;
        lastRect = key;
        const offset = 3;
        ring.style.setProperty("--lwc-ring-x", `${rect.left - offset}px`);
        ring.style.setProperty("--lwc-ring-y", `${rect.top - offset}px`);
        ring.style.setProperty("--lwc-ring-w", `${rect.width + offset * 2}px`);
        ring.style.setProperty("--lwc-ring-h", `${rect.height + offset * 2}px`);
        return true;
    }

    function trackRing(): void {
        cancelAnimationFrame(ringFrame);
        ringIdleFrames = 0;
        const step = () => {
            if (!ringTarget) return;
            ringIdleFrames = measureRing() ? 0 : ringIdleFrames + 1;
            // Keep following through hover lifts and layout settling, then rest.
            if (ringIdleFrames < 24) ringFrame = requestAnimationFrame(step);
            else ringFrame = 0;
        };
        ringFrame = requestAnimationFrame(step);
    }

    function radiusFor(element: HTMLElement): string {
        const style = getComputedStyle(element);
        const radius = parseFloat(style.borderTopLeftRadius) || 0;
        if (element.matches("tr")) return "6px";
        // A circle stays a circle; everything else gets a soft corner plus the gap.
        const rect = element.getBoundingClientRect();
        if (style.borderTopLeftRadius.endsWith("%") || radius >= Math.min(rect.width, rect.height) / 2 - 1) return "999px";
        return `${Math.max(4, radius + 3)}px`;
    }

    function moveRing(element: HTMLElement): void {
        if (!ring.isConnected) document.body.append(ring);
        const glide = ringTarget !== null && ring.classList.contains("is-on") && !reduced.matches;
        ring.classList.toggle("is-gliding", glide);
        ringTarget = element;
        lastRect = "";
        ring.style.setProperty("--lwc-ring-radius", radiusFor(element));
        ring.hidden = false;
        if (!glide) {
            // Appear in place: no travel from wherever the ring last rested.
            ring.classList.remove("is-on");
            measureRing();
        } else {
            measureRing();
        }
        ring.classList.add("is-on");
        root.dataset.lwcRing = "on";
        trackRing();
    }

    function refreshRing(): void {
        const active = document.activeElement;
        if (modality === "keyboard" && active instanceof HTMLElement && ringAllowed(active)) moveRing(active);
        else hideRing();
    }

    document.addEventListener("focusin", event => {
        const target = event.target;
        if (!(target instanceof HTMLElement) || tip.contains(target)) return;
        if (modality !== "keyboard") { hideRing(); return; }
        // :focus-visible is resolved after focusin in some engines; check next frame.
        requestAnimationFrame(() => {
            if (document.activeElement !== target || modality !== "keyboard") return;
            if (ringAllowed(target)) moveRing(target);
            else hideRing();
            if (target.matches(":focus-visible") && !target.closest(tipExcluded)) scheduleTip(target, 650, "keyboard");
        });
    });
    document.addEventListener("focusout", event => {
        const next = event.relatedTarget;
        if (tipOwner && (event.target === tipOwner || event.target === tip) && !(next instanceof Node && (tip.contains(next) || tipOwner.contains(next)))) hideTip(true);
        if (!(next instanceof HTMLElement)) requestAnimationFrame(() => {
            if (!(document.activeElement instanceof HTMLElement) || document.activeElement === document.body) hideRing();
        });
    });
    const wake = (event: Event) => {
        if (!ringTarget || event.target === ring) return;
        if ((event.type === "transitionrun" || event.type === "animationstart")
            && !(event.target instanceof Element && event.target.contains(ringTarget))) return;
        ring.classList.remove("is-gliding"); lastRect = ""; trackRing();
    };
    window.addEventListener("scroll", wake, { passive: true, capture: true });
    window.addEventListener("resize", wake, { passive: true });
    document.addEventListener("transitionrun", wake, true);
    document.addEventListener("animationstart", wake, true);
    // A dialog opening or closing changes which host draws the focus.
    document.addEventListener("toggle", event => {
        if (event.target !== tip) hideTip(true);
        requestAnimationFrame(refreshRing);
    }, true);
    document.addEventListener("close", () => requestAnimationFrame(refreshRing), true);

    function pulseRing(kind: "return" | "bump", direction = 0): void {
        if (!ringTarget || reduced.matches || typeof ring.animate !== "function") return;
        for (const animation of ring.getAnimations()) animation.cancel();
        if (kind === "return") {
            ring.animate([
                { boxShadow: "0 0 0 0 var(--lwc-ring-halo)", scale: "1.06" },
                { boxShadow: "0 0 0 7px transparent", scale: "1" },
            ], { duration: 320, easing: "cubic-bezier(0, 0, 0, 1)" });
        } else {
            const distance = 4 * direction;
            ring.animate([
                { translate: "0 0" },
                { translate: `0 ${distance}px` },
                { translate: "0 0" },
            ], { duration: 220, easing: "cubic-bezier(0.2, 0, 0, 1)" });
        }
    }

    // ------------------------------------------------------------ arrow lists
    const focusable = "a[href], button:not(:disabled), [role='tab'], [role='menuitem'], [role='option'], summary, [tabindex]:not([tabindex='-1']), input[type='checkbox'], input[type='radio']";
    const editable = "input, textarea, select, [contenteditable], [role='slider'], [role='spinbutton'], [role='combobox'], [role='grid'], [role='listbox'], [role='radiogroup'], canvas, video, audio";
    const rowLists = [
        "#athleteLeaderboard > table > tbody",
        ".leaderboard table tbody",
        ".events-board table tbody",
        ".archive-table tbody",
        ".flag-leaderboard tbody",
        "[data-lwc-arrows='rows']",
    ].join(", ");
    const lineLists = [
        "[role='tablist']",
        "[role='toolbar']",
        "[role='menu']",
        "[role='menubar']",
        ".site-menu nav",
        ".footer-links",
        ".podium",
        ".lmx-tabs",
        "[data-lwc-arrows='line']",
    ].join(", ");

    function visible(element: Element): boolean {
        if (!(element instanceof HTMLElement)) return false;
        if (element.hidden || element.closest("[hidden], [inert], [aria-hidden='true']")) return false;
        return element.getClientRects().length > 0 && getComputedStyle(element).visibility === "visible";
    }

    function focusablesIn(container: Element): HTMLElement[] {
        return Array.from(container.querySelectorAll<HTMLElement>(focusable)).filter(element => visible(element) && !element.matches(":disabled") && !element.closest("[aria-disabled='true']"));
    }

    const arrivals = new Map<HTMLElement, number>();

    function settleFocus(next: HTMLElement, direction: number): void {
        next.focus({ preventScroll: true });
        if (document.activeElement !== next) return;
        const rect = next.getBoundingClientRect();
        const header = parseFloat(getComputedStyle(root).scrollPaddingTop) || 64;
        const outOfView = rect.top < header + 4 || rect.bottom > window.innerHeight - 12;
        if (outOfView) next.scrollIntoView({ block: direction < 0 ? "start" : "nearest", inline: "nearest", behavior: reduced.matches ? "auto" : "smooth" });
        else {
            const scroller = next.closest<HTMLElement>("[data-lwc-scroller], .view-switcher-chips, .leaderboard-selection-chips, .table-wrapper, .archive-table-wrapper");
            if (scroller) next.scrollIntoView({ block: "nearest", inline: "nearest", behavior: reduced.matches ? "auto" : "smooth" });
        }
        const row = next.closest<HTMLElement>("tr");
        if (row && !reduced.matches) {
            window.clearTimeout(arrivals.get(row));
            row.classList.add("lwc-key-arrive");
            arrivals.set(row, window.setTimeout(() => {
                row.classList.remove("lwc-key-arrive");
                arrivals.delete(row);
            }, 420));
        }
    }

    function moveInRows(body: Element, current: HTMLElement, key: string): boolean {
        const rows = Array.from(body.children).filter(row => row instanceof HTMLTableRowElement && visible(row) && focusablesIn(row).length > 0);
        const row = current.closest("tr");
        if (!row) return false;
        const index = rows.indexOf(row);
        if (index < 0) return false;
        const column = Math.max(0, focusablesIn(row).indexOf(current));
        let target = index;
        if (key === "ArrowDown") target = index + 1;
        else if (key === "ArrowUp") target = index - 1;
        else if (key === "Home") target = 0;
        else if (key === "End") target = rows.length - 1;
        else return false;
        if (target < 0 || target >= rows.length) { pulseRing("bump", key === "ArrowUp" ? -1 : 1); return true; }
        const nextRow = rows[target];
        if (!nextRow) return false;
        const candidates = focusablesIn(nextRow);
        const next = candidates[Math.min(column, candidates.length - 1)];
        if (!next) return false;
        settleFocus(next, target < index ? -1 : 1);
        return true;
    }

    function moveInLine(list: Element, current: HTMLElement, key: string): boolean {
        const items = focusablesIn(list).filter(item => item.closest(lineLists) === list);
        const index = items.indexOf(current);
        if (index < 0) return false;
        const vertical = list.matches("[role='menu'], .site-menu nav, [aria-orientation='vertical']");
        const forward = vertical ? "ArrowDown" : "ArrowRight";
        const backward = vertical ? "ArrowUp" : "ArrowLeft";
        let target = index;
        if (key === forward) target = index + 1;
        else if (key === backward) target = index - 1;
        else if (key === "Home") target = 0;
        else if (key === "End") target = items.length - 1;
        else return false;
        if (target < 0 || target >= items.length) { pulseRing("bump", target < 0 ? -1 : 1); return true; }
        const next = items[target];
        if (!next) return false;
        settleFocus(next, target < index ? -1 : 1);
        // Tabs follow focus the same way a click would choose them.
        if (next.getAttribute("role") === "tab" && next.getAttribute("aria-selected") !== "true") next.click();
        return true;
    }

    // Bubble phase on window: page handlers that already own these keys run
    // first and mark the event, and this layer then stays out of the way.
    window.addEventListener("keydown", event => {
        if (event.defaultPrevented || event.isComposing || event.metaKey || event.ctrlKey || event.altKey || event.shiftKey) return;
        const current = event.target;
        if (!(current instanceof HTMLElement) || current.closest(editable)) return;
        if (!window.getSelection()?.isCollapsed) return;

        if (event.key === "Escape") {
            hideTip(true);
            const before = document.activeElement;
            const disclosure = current.closest<HTMLDetailsElement>("details[open]");
            if (disclosure && !disclosure.closest("dialog, [popover]")) {
                const summary = disclosure.querySelector<HTMLElement>(":scope > summary");
                disclosure.open = false;
                summary?.focus();
                event.preventDefault();
            }
            requestAnimationFrame(() => requestAnimationFrame(() => {
                if (document.activeElement !== before || disclosure) { refreshRing(); pulseRing("return"); }
            }));
            return;
        }

        if (!["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        const line = current.closest(lineLists);
        const body = current.closest(rowLists);
        // The innermost list wins, so a tab strip inside a row still moves sideways.
        const lineInside = line && (!body || body.contains(line));
        const handled = lineInside
            ? moveInLine(line, current, event.key)
            : body ? moveInRows(body, current, event.key) : false;
        if (handled) event.preventDefault();
    });

    // Escape that a page handler already consumed still deserves the return pulse.
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape" || event.metaKey || event.ctrlKey || event.altKey || event.isComposing) return;
        const owner = tipOwner;
        const tipFocused = tip.contains(document.activeElement);
        hideTip(true);
        if (tipFocused && owner?.isConnected) { owner.focus({ preventScroll: true }); event.preventDefault(); }
        const before = document.activeElement;
        requestAnimationFrame(() => requestAnimationFrame(() => {
            if (event.defaultPrevented && document.activeElement !== before) { refreshRing(); pulseRing("return"); }
        }));
    }, true);

    // Only an overflowing tip adds a keyboard stop. Return to the owner's
    // normal tab sequence when leaving it; ordinary short tips never steal Tab.
    document.addEventListener("keydown", event => {
        if (event.key !== "Tab" || event.defaultPrevented || event.metaKey || event.ctrlKey || event.altKey || event.isComposing
            || !tipOwner || tip.hidden || !tip.hasAttribute("data-scrollable")) return;
        if (document.activeElement === tipOwner && !event.shiftKey) {
            event.preventDefault(); tip.focus({ preventScroll: true });
        } else if (document.activeElement === tip) {
            const owner = tipOwner;
            const scope = owner.closest("dialog:modal, [role='dialog']") || document.body;
            const items = Array.from(scope.querySelectorAll<HTMLElement>("a[href], button, input, textarea, select, summary, [contenteditable], [tabindex]"))
                .filter(item => item.tabIndex >= 0 && visible(item) && !item.matches(":disabled") && !tip.contains(item))
                .sort((a, b) => (a.tabIndex || Infinity) - (b.tabIndex || Infinity));
            const next = event.shiftKey ? owner : items[items.indexOf(owner) + 1];
            if (next) { event.preventDefault(); hideTip(true); next.focus(); }
        }
    });

    function clearArrivals(): void {
        for (const [row, timer] of arrivals) { window.clearTimeout(timer); row.classList.remove("lwc-key-arrive"); }
        arrivals.clear();
    }

    function clearTransientState(): void { hideTip(true); hideRing(); clearArrivals(); }
    new MutationObserver(() => {
        if (tipOwner && !tipOwner.isConnected) hideTip(true);
        if (ringTarget && !ringTarget.isConnected) hideRing();
    }).observe(root, { childList: true, subtree: true });
    window.addEventListener("pagehide", clearTransientState);
    document.addEventListener("visibilitychange", () => { if (document.hidden) clearTransientState(); });
    window.addEventListener("blur", hideRing);
    reduced.addEventListener?.("change", () => {
        ring.classList.remove("is-gliding");
        tip.classList.remove("is-gliding");
        for (const animation of [...ring.getAnimations(), ...tip.getAnimations()]) animation.cancel();
        clearArrivals();
        refreshRing();
    });
    forcedColors.addEventListener?.("change", refreshRing);

    window.LwcPointer = {
        modality: () => modality,
        refreshRing,
        hideTip: () => hideTip(true),
    };
})();
