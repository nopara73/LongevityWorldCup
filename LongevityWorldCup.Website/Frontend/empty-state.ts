// Decorative empty-state illustrations. Pages own content, focus and row motion.
// Observe only changed subtrees; decoration never changes the meaning of a host.

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
    // Idempotent even if an embedding consumer evaluates the script twice.
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
        { selector: ".flag-empty", kind: "flag", accepts: host => host.getAttribute("aria-busy") !== "true" },
        { selector: ".proofs-empty", kind: "proofs" },
        { selector: "tr.events-empty-row > td", kind: "calendar" },
        { selector: ".lmx-empty-row > .lmx-name", kind: "podium" },
        { selector: ".lmx-empty-state", kind: "check" },
        { selector: "#lmxNotes > .lmx-note-empty", kind: "discussion" },
        { selector: ".stats-page .empty-state", kind: "chart" },
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

    const manualKinds = new WeakMap<Element, LwcEmptyKind>();
    const entrances = new Map<HTMLElement, number>();
    function finishEntrance(host: HTMLElement): void {
        window.clearTimeout(entrances.get(host));
        entrances.delete(host);
        host.classList.remove("lwc-empty--enter");
    }

    function decorate(host: Element, kind: LwcEmptyKind, enter = motionOn()): void {
        if (!(host instanceof HTMLElement)) return;
        const art = host.querySelector(":scope > .lwc-empty__art, :scope > .lwc-empty__glyph");
        const previous = host.getAttribute(marker);
        if (previous === kind && art) return;
        finishEntrance(host);
        art?.remove();
        host.setAttribute(marker, kind);
        host.classList.add("lwc-empty");
        host.classList.toggle("lwc-empty--glyph", kind === "glyph");
        host.classList.toggle("lwc-empty--scene", kind !== "glyph");
        host.insertAdjacentHTML("afterbegin", kind === "glyph" ? glyphMarkup : sceneMarkup(kind));
        syncPhase(host);
        // Updating a hint or changing search/filter scenes keeps the existing host still.
        if (previous || !enter || root.hasAttribute("data-lwc-empty-restoring")
            || !host.getClientRects().length || host.closest('[aria-busy="true"]')) return;
        host.classList.add("lwc-empty--enter");
        entrances.set(host, window.setTimeout(() => finishEntrance(host), 450));
    }

    function reconcile(host: Element, enter: boolean): void {
        if (!(host instanceof HTMLElement) || !host.isConnected) return;
        const entry = registry.find(entry => host.matches(entry.selector) && (!entry.accepts || entry.accepts(host)));
        if (entry) {
            decorate(host, typeof entry.kind === "function" ? entry.kind(host) : entry.kind, enter);
        } else if (manualKinds.has(host)) {
            decorate(host, manualKinds.get(host)!, enter);
        } else if (host.hasAttribute(marker)) {
            finishEntrance(host);
            host.removeAttribute(marker);
            host.classList.remove("lwc-empty", "lwc-empty--glyph", "lwc-empty--scene");
            host.querySelector(":scope > .lwc-empty__art, :scope > .lwc-empty__glyph")?.remove();
            host.style.removeProperty("--lwc-empty-phase");
        }
    }

    const candidates = `${selectorList}, [${marker}]`;
    function hostsIn(node: Node): Element[] {
        if (!(node instanceof Element) || node instanceof SVGElement) return [];
        return [...(node.matches(candidates) ? [node] : []), ...node.querySelectorAll(candidates)];
    }

    // Mutations caused by our own synchronous decoration must not feed back into
    // the observer. No global input-time layout reads or detached-host snapshots.
    const observer = new MutationObserver(records => {
        const touched = new Set<Element>();
        for (const record of records) {
            const target = record.target instanceof Element ? record.target : record.target.parentElement;
            const host = target?.closest(candidates);
            if (host) touched.add(host);
            record.addedNodes.forEach(node => hostsIn(node).forEach(item => touched.add(item)));
            // A removed class can invalidate a previously decorated host.
            if (target?.hasAttribute(marker)) touched.add(target);
        }
        observer.disconnect();
        for (const host of entrances.keys()) if (!host.isConnected) finishEntrance(host);
        touched.forEach(host => reconcile(host, motionOn()));
        observe();
    });
    function observe(): void {
        observer.observe(document.body, {
            childList: true, subtree: true, characterData: true,
            attributes: true, attributeFilter: ["class"],
        });
    }

    function stopEntrances(): void {
        for (const host of entrances.keys()) finishEntrance(host);
    }
    const preferenceChanged = () => { if (reduced.matches) stopEntrances(); };
    if (typeof reduced.addEventListener === "function") reduced.addEventListener("change", preferenceChanged);
    else reduced.addListener?.(preferenceChanged);
    window.addEventListener("pagehide", () => {
        root.setAttribute("data-lwc-empty-restoring", "");
        stopEntrances();
    });
    window.addEventListener("pageshow", event => {
        if (event.persisted) root.setAttribute("data-lwc-empty-restoring", "");
        stopEntrances();
    });
    const resume = (event: Event) => {
        if (event.isTrusted) root.removeAttribute("data-lwc-empty-restoring");
    };
    window.addEventListener("pointerdown", resume, true);
    window.addEventListener("keydown", resume, true);

    function start(): void {
        hostsIn(document.body).forEach(host => reconcile(host, false));
        observe();
    }
    window.LwcEmptyState = {
        decorate: (host, kind) => {
            if (!registry.some(entry => host.matches(entry.selector))) manualKinds.set(host, kind);
            decorate(host, kind);
        },
        kinds: ["search", "filter", "flag", "proofs", "calendar", "podium", "check", "discussion", "chart", "glyph"],
    };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
