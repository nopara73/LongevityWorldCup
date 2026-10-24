// One illustration system for every empty, zero-result and "nothing yet" state.
//
// Pages keep rendering their own empty markup and wording. This classic script
// watches for those hosts (the registry below), gives each one a small inline
// SVG scene drawn in the site palette, and owns the motion around it:
//
// - idle: the scene bobs and its shadow breathes on one shared clock, so a
//   host that is re-rendered keeps its phase instead of jumping;
// - entrance: a host that appears after the visitor has interacted pops in,
//   then its words and call to action rise after it;
// - return: when results come back the scene floats away as a ghost while the
//   returning content fades in, so empty and filled crossfade instead of cut.
//
// Re-rendering the same empty state in place (a search that still matches
// nothing) is treated as staying, not leaving and arriving again.
//
// Decorations are aria-hidden and never replace text. Entrance and return
// follow the shared motion gate (html[data-lwc-motion="on"]); reduced motion
// keeps the scenes still. Pages may also call LwcEmptyState.decorate(host,
// kind) for markup the registry does not know yet.

type LwcEmptyKind =
    | "search" | "filter" | "flag" | "proofs" | "calendar"
    | "podium" | "check" | "discussion" | "chart" | "glyph";

interface LwcEmptyStateApi {
    readonly decorate: (host: Element, kind: LwcEmptyKind) => void;
    readonly kinds: readonly LwcEmptyKind[];
}

interface Window {
    LwcEmptyState: LwcEmptyStateApi;
}

(() => {
    // Pages that embed two boards load this twice; one observer is enough.
    if (window.LwcEmptyState) return;
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    // One clock for every scene: matches --lwc-empty-loop in empty-state.css.
    const loopMs = 3600;
    const marker = "data-lwc-empty";

    interface Entry {
        readonly selector: string;
        readonly kind: LwcEmptyKind | ((host: Element) => LwcEmptyKind);
        readonly accepts?: (host: Element) => boolean;
    }

    function leaderboardKind(): LwcEmptyKind {
        const search = document.querySelector<HTMLInputElement>("#athleteSearch, input[type='search']");
        return search && search.value.trim() ? "search" : "filter";
    }

    // Every empty state the site renders today. Keep selectors exact: the host
    // is the element that already owns the message and its spacing.
    const registry: readonly Entry[] = [
        { selector: ".no-results-empty-state", kind: leaderboardKind },
        { selector: ".flag-empty", kind: "flag" },
        { selector: ".proofs-empty", kind: "proofs" },
        { selector: "tr.events-empty-row > td", kind: "calendar" },
        { selector: ".lmx-empty-row > .lmx-name", kind: "podium" },
        { selector: ".lmx-empty-state", kind: "check" },
        {
            selector: "#lmxNotes > .lmx-note",
            kind: "discussion",
            // The discussion's empty note is a lone strong line, unlike real notes.
            accepts: host => host.parentElement?.childElementCount === 1
                && host.childElementCount === 1
                && host.firstElementChild?.tagName === "STRONG",
        },
        { selector: ".empty-state", kind: "chart", accepts: host => !!host.closest("main, body") && !host.closest(".lwc-empty") },
        { selector: ".lmx-mention-empty, .lmx-athlete-empty, .lmx-timezone-empty", kind: "glyph" },
        {
            selector: ".error-message.athlete-search-empty",
            kind: "glyph",
            accepts: host => (host.textContent || "").trim().length > 0,
        },
    ];
    const selectorList = registry.map(entry => entry.selector).join(",");

    // Shared scene pieces: halo, ground shadow, a floating body, a face.
    const eyes = (x1: number, x2: number, y: number) =>
        `<g class="lwc-empty__eyes"><circle cx="${x1}" cy="${y}" r="2.4"/><circle cx="${x2}" cy="${y}" r="2.4"/></g>`;
    const spark = (x: number, y: number, s: number, delay: number) =>
        `<path class="lwc-empty__spark" style="--d:${delay}ms" d="M${x} ${y - s}q${s * 0.18} ${s * 0.82} ${s} ${s}q-${s * 0.82} ${s * 0.18}-${s} ${s}q-${s * 0.18}-${s * 0.82}-${s}-${s}q${s * 0.82}-${s * 0.18} ${s}-${s}z"/>`;

    const bodies: Record<Exclude<LwcEmptyKind, "glyph">, string> = {
        search: `
            <path class="lwc-empty__stroke lwc-empty__handle" d="M67 57l13 13"/>
            <circle class="lwc-empty__body" cx="54" cy="44" r="19"/>
            <path class="lwc-empty__shine" d="M42 38a13 13 0 0 1 8-8"/>
            <g class="lwc-empty__look">${eyes(48, 60, 43)}<circle class="lwc-empty__mouth-o" cx="54" cy="51" r="2"/></g>`,
        filter: `
            <path class="lwc-empty__body" d="M33 24h54l-20 24v15l-14 8V48z"/>
            <path class="lwc-empty__stroke lwc-empty__rim" d="M37 31h46"/>
            ${eyes(54, 66, 39)}
            <path class="lwc-empty__drop" d="M60 74c-3 4-3 7 0 8c3-1 3-4 0-8z"/>`,
        flag: `
            <path class="lwc-empty__stroke lwc-empty__pole" d="M42 18v62"/>
            <circle class="lwc-empty__knob" cx="42" cy="17" r="3"/>
            <g class="lwc-empty__wave"><path class="lwc-empty__body" d="M44 22c11-6 19 6 32 0v24c-13 6-21-6-32 0z"/>${eyes(55, 66, 33)}</g>`,
        proofs: `
            <rect class="lwc-empty__body" x="36" y="20" width="48" height="56" rx="7"/>
            <path class="lwc-empty__stroke" d="M43 66l11-13 8 8 6-6 9 11"/>
            <circle class="lwc-empty__sun" cx="72" cy="33" r="4.5"/>
            ${eyes(51, 61, 36)}`,
        calendar: `
            <rect class="lwc-empty__body" x="32" y="26" width="56" height="48" rx="9"/>
            <path class="lwc-empty__band" d="M32 35a9 9 0 0 1 9-9h38a9 9 0 0 1 9 9v5H32z"/>
            <path class="lwc-empty__stroke lwc-empty__ring" d="M46 20v11M74 20v11"/>
            ${eyes(52, 68, 53)}
            <path class="lwc-empty__stroke lwc-empty__mouth" d="M56 62h8"/>
            <path class="lwc-empty__ear" d="M88 64l-10 10h4a6 6 0 0 0 6-6z"/>`,
        podium: `
            <rect class="lwc-empty__body" x="30" y="52" width="20" height="24" rx="3"/>
            <rect class="lwc-empty__body" x="70" y="58" width="20" height="18" rx="3"/>
            <rect class="lwc-empty__body lwc-empty__top" x="50" y="42" width="20" height="34" rx="3"/>
            ${eyes(56, 64, 52)}
            <path class="lwc-empty__star" d="M60 18l3.5 7.2 7.9 1.1-5.7 5.6 1.3 7.9-7-3.7-7 3.7 1.3-7.9-5.7-5.6 7.9-1.1z"/>`,
        check: `
            <circle class="lwc-empty__body lwc-empty__calm" cx="60" cy="44" r="21"/>
            <path class="lwc-empty__tick" d="M50 45l7 7 14-15"/>`,
        discussion: `
            <path class="lwc-empty__body" d="M42 22h36a12 12 0 0 1 12 12v12a12 12 0 0 1-12 12H58l-11 9v-9h-5a12 12 0 0 1-12-12V34a12 12 0 0 1 12-12z"/>
            <g class="lwc-empty__typing"><circle cx="49" cy="40" r="3"/><circle cx="60" cy="40" r="3"/><circle cx="71" cy="40" r="3"/></g>`,
        chart: `
            <path class="lwc-empty__stroke lwc-empty__axis" d="M32 22v50h56"/>
            <rect class="lwc-empty__bar" style="--d:0ms" x="40" y="62" width="9" height="10" rx="2"/>
            <rect class="lwc-empty__bar" style="--d:160ms" x="55" y="58" width="9" height="14" rx="2"/>
            <rect class="lwc-empty__bar" style="--d:320ms" x="70" y="64" width="9" height="8" rx="2"/>
            <path class="lwc-empty__stroke lwc-empty__flat" d="M38 44h46"/>`,
    };

    function sceneMarkup(kind: Exclude<LwcEmptyKind, "glyph">): string {
        return `<svg class="lwc-empty__art" viewBox="14 6 92 86" aria-hidden="true" focusable="false">
            <circle class="lwc-empty__halo" cx="60" cy="46" r="34"/>
            <ellipse class="lwc-empty__shadow" cx="60" cy="86" rx="24" ry="3.5"/>
            ${spark(92, 22, 5, 0)}${spark(26, 34, 3.5, 900)}${spark(96, 62, 3, 1700)}
            <g class="lwc-empty__float">${bodies[kind]}</g>
        </svg>`;
    }

    // Pickers and inline hints get a single bobbing lens beside the words.
    const glyphMarkup = `<svg class="lwc-empty__glyph" viewBox="0 0 24 24" aria-hidden="true" focusable="false">
        <g class="lwc-empty__float"><circle class="lwc-empty__body" cx="10" cy="10" r="6.5"/><path class="lwc-empty__stroke" d="M15 15l5 5"/>
        <circle class="lwc-empty__pupil" cx="8.4" cy="9.6" r="1.1"/><circle class="lwc-empty__pupil" cx="11.8" cy="9.6" r="1.1"/></g>
    </svg>`;

    function motionOn(): boolean {
        return root.dataset["lwcMotion"] === "on" && !reduced.matches;
    }

    function syncPhase(host: HTMLElement): void {
        // A negative delay puts a fresh scene on the shared clock.
        host.style.setProperty("--lwc-empty-phase", `${-(performance.now() % loopMs)}ms`);
    }

    function decorate(host: Element, kind: LwcEmptyKind, enter = motionOn()): void {
        if (!(host instanceof HTMLElement) || host.hasAttribute(marker)) return;
        host.setAttribute(marker, kind);
        host.classList.add("lwc-empty", kind === "glyph" ? "lwc-empty--glyph" : "lwc-empty--scene");
        host.insertAdjacentHTML("afterbegin", kind === "glyph" ? glyphMarkup : sceneMarkup(kind));
        syncPhase(host);
        if (!enter) return;
        host.classList.add("lwc-empty--enter");
        const clear = (event: AnimationEvent) => {
            if (event.target !== host && !(event.target as Element).classList?.contains("lwc-empty__art")) return;
            host.classList.remove("lwc-empty--enter");
            host.removeEventListener("animationend", clear);
        };
        host.addEventListener("animationend", clear);
        window.setTimeout(() => host.classList.remove("lwc-empty--enter"), 900);
    }

    function entryFor(host: Element): Entry | undefined {
        return registry.find(entry => host.matches(entry.selector) && (!entry.accepts || entry.accepts(host)));
    }

    function upgrade(host: Element, enter: boolean): void {
        const entry = entryFor(host);
        if (!entry) return;
        decorate(host, typeof entry.kind === "function" ? entry.kind(host) : entry.kind, enter);
    }

    function hostsIn(node: Node): Element[] {
        if (!(node instanceof Element)) return [];
        const found = node.matches(selectorList) ? [node] : [];
        return found.concat(Array.from(node.querySelectorAll(selectorList)));
    }

    function decoratedIn(node: Node): HTMLElement[] {
        if (!(node instanceof HTMLElement)) return [];
        const found = node.hasAttribute(marker) ? [node] : [];
        return found.concat(Array.from(node.querySelectorAll<HTMLElement>(`[${marker}]`)));
    }

    // The scene drifts up and away from where it stood.
    function ghost(art: Element, rect: DOMRect): void {
        if (!rect.width || !rect.height) return;
        const copy = art.cloneNode(true) as SVGElement;
        copy.classList.add("lwc-empty__ghost");
        Object.assign(copy.style, {
            left: `${rect.left}px`, top: `${rect.top}px`, width: `${rect.width}px`, height: `${rect.height}px`,
        });
        document.body.appendChild(copy);
        const done = () => copy.remove();
        const motion = copy.animate([
            { opacity: 1, transform: "translateY(0) scale(1)" },
            { opacity: 0, transform: "translateY(-14px) scale(0.86)" },
        ], { duration: 240, easing: "cubic-bezier(0.4, 0, 1, 1)", fill: "forwards" });
        motion.onfinish = done;
        motion.oncancel = done;
    }

    // Returning content rises in a short cascade, nearest first.
    function welcomeBack(container: Element): void {
        const items = Array.from(container.children)
            .filter((child): child is HTMLElement => child instanceof HTMLElement
                && !child.hasAttribute(marker) && child.getClientRects().length > 0)
            .slice(0, 14);
        items.forEach((item, index) => {
            item.animate([
                { opacity: 0, transform: "translateY(6px)" },
                { opacity: 1, transform: "none" },
            ], { duration: 240, delay: 60 + index * 18, easing: "cubic-bezier(0.2, 0, 0, 1)", fill: "backwards" });
        });
    }

    // Leaving hosts are gone from the DOM when the observer runs, so remember
    // where each scene sat while it was still on the page.
    const lastRects = new WeakMap<Element, DOMRect>();
    function remember(): void {
        document.querySelectorAll(`[${marker}] > svg`).forEach(art => lastRects.set(art, art.getBoundingClientRect()));
    }
    ["pointerdown", "keydown", "input", "change", "click"].forEach(type =>
        document.addEventListener(type, remember, { capture: true, passive: true }));

    function handle(records: MutationRecord[]): void {
        const leaving = new Map<Node, { kind: string; art: Element | null }[]>();
        const arriving: Element[] = [];
        const touched = new Set<Element>();

        for (const record of records) {
            const target = record.target instanceof Element ? record.target : record.target.parentElement;
            // Class flips, text edits and textContent swaps on a hint slot.
            const inPlace = record.type !== "childList" || record.target === target;
            if (target && inPlace && (target.hasAttribute(marker) || target.matches(selectorList))) touched.add(target);
            record.removedNodes.forEach(node => {
                decoratedIn(node).forEach(host => {
                    const list = leaving.get(record.target) || [];
                    list.push({ kind: host.getAttribute(marker) || "", art: host.querySelector(":scope > svg") });
                    leaving.set(record.target, list);
                });
            });
            record.addedNodes.forEach(node => arriving.push(...hostsIn(node)));
        }

        // Hint slots reuse one element: follow its words and class.
        touched.forEach(host => {
            if (!(host instanceof HTMLElement)) return;
            const entry = entryFor(host);
            const decorated = host.hasAttribute(marker);
            if (!entry && decorated) {
                host.removeAttribute(marker);
                host.classList.remove("lwc-empty", "lwc-empty--glyph", "lwc-empty--scene", "lwc-empty--enter");
                host.querySelector(":scope > svg")?.remove();
            } else if (entry && !decorated) {
                upgrade(host, motionOn());
            } else if (entry && decorated && !host.querySelector(":scope > svg")) {
                // New words replaced the scene: put it back in front of them.
                host.removeAttribute(marker);
                upgrade(host, false);
            }
        });

        const stayed = new Set<Node>();
        for (const host of arriving) {
            if (!host.isConnected || host.hasAttribute(marker)) continue;
            const entry = entryFor(host);
            if (!entry) continue;
            // Re-rendered in place: same parent lost a host this batch.
            const sameParent = Array.from(leaving.keys()).find(parent => parent === host.parentNode
                || (parent instanceof Element && parent.contains(host)));
            if (sameParent) stayed.add(sameParent);
            upgrade(host, sameParent ? false : motionOn());
        }

        if (!motionOn()) return;
        leaving.forEach((hosts, parent) => {
            if (stayed.has(parent)) return;
            hosts.forEach(({ art }) => {
                const rect = art && lastRects.get(art);
                if (art && rect) ghost(art, rect);
            });
            if (parent instanceof Element && parent.isConnected) welcomeBack(parent);
        });
    }

    function start(): void {
        hostsIn(document.body).forEach(host => upgrade(host, false));
        new MutationObserver(handle).observe(document.body, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["class"] });
    }

    window.LwcEmptyState = {
        decorate: (host, kind) => decorate(host, kind),
        kinds: ["search", "filter", "flag", "proofs", "calendar", "podium", "check", "discussion", "chart", "glyph"],
    };

    if (document.body) start();
    else document.addEventListener("DOMContentLoaded", start, { once: true });
})();
