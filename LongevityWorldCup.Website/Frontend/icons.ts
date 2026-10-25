// One crafted iconography for the whole site. Every interface glyph (arrows,
// chevrons, close, check, share, copy, status, navigation) is drawn here on one
// 24-unit grid with round caps and joins, then rendered as inline SVG.
//
// Pages keep writing Font Awesome markup. This classic script upgrades each
// known <i class="fa-…"> in place: the element stays (so page code that swaps
// its class or queries it keeps working) and the SVG goes inside it. When a page
// swaps a glyph (link → check, spinner → check, share → check) the change is
// animated here instead of snapping. Brand logos and collectible badge art stay
// on their own glyphs.

interface LwcIconsApi {
    /** Markup for one glyph, for new code that skips Font Awesome entirely. */
    readonly svg: (name: string, className?: string) => string;
    /** Upgrades Font Awesome glyphs below root (the observer already does this for the document). */
    readonly upgrade: (root: ParentNode) => void;
    /** Replays feedback on an existing icon host without measuring layout. */
    readonly replay: (host: HTMLElement) => void;
    readonly names: () => readonly string[];
}

interface Window {
    LwcIcons: LwcIconsApi;
}

(() => {
    const NS = "http://www.w3.org/2000/svg";

    // Each glyph is a list of SVG elements on the 24 grid. "lwc-icon__draw" marks
    // the stroke that animates in (pathLength 1); "lwc-icon__fill" a shape that fills.
    type Part = readonly [tag: string, attrs: Readonly<Record<string, string>>];
    const p = (d: string, extra: Record<string, string> = {}): Part => ["path", { d, ...extra }];
    const c = (cx: number, cy: number, r: number, extra: Record<string, string> = {}): Part =>
        ["circle", { cx: String(cx), cy: String(cy), r: String(r), ...extra }];
    const dot = (x: number, y: number): Part => ["path", { d: `M${x} ${y}h.01`, class: "lwc-icon__dot" }];
    const bell = [p("M6 16.5V11a6 6 0 0 1 12 0v5.5l1.5 1.5h-15z"), p("M10 20.5a2 2 0 0 0 4 0")];
    const tray = p("M4 15v2.5A2.5 2.5 0 0 0 6.5 20h11a2.5 2.5 0 0 0 2.5-2.5V15");

    const glyphs: Record<string, readonly Part[]> = {
        "arrow-right": [p("M4.5 12h14.5"), p("M13 6l6 6-6 6")],
        "arrow-left": [p("M19.5 12H5"), p("M11 6l-6 6 6 6")],
        "arrow-up": [p("M12 19.5V5"), p("M6 11l6-6 6 6")],
        "arrow-down": [p("M12 4.5V19"), p("M6 13l6 6 6-6")],
        "chevron-right": [p("M9 5.5l6.5 6.5L9 18.5")],
        "chevron-left": [p("M15 5.5L8.5 12l6.5 6.5")],
        "chevron-down": [p("M5.5 9l6.5 6.5L18.5 9")],
        "chevron-up": [p("M5.5 15L12 8.5l6.5 6.5")],
        close: [p("M6.5 6.5l11 11"), p("M17.5 6.5l-11 11")],
        check: [p("M4.5 12.5l5 5L19.5 7", { pathLength: "1", class: "lwc-icon__draw" })],
        "circle-check": [c(12, 12, 9), p("M7.8 12.3l2.9 2.9 5.5-5.6", { pathLength: "1", class: "lwc-icon__draw" })],
        plus: [p("M12 5v14"), p("M5 12h14")],
        minus: [p("M5 12h14")],
        bars: [p("M4 6.5h16"), p("M4 12h16"), p("M4 17.5h16")],
        sort: [p("M4 6h10"), p("M4 12h7"), p("M4 18h4"), p("M18 5v14"), p("M15 16l3 3 3-3")],
        sliders: [p("M4 7h8.5"), p("M17.5 7H20"), p("M4 17h2.5"), p("M11.5 17H20"), c(15, 7, 2.5), c(9, 17, 2.5)],
        search: [c(10.75, 10.75, 6.25), p("M20 20l-4.6-4.6")],
        "share-nodes": [c(18, 5.5, 2.5), c(6, 12, 2.5), c(18, 18.5, 2.5), p("M8.2 10.8l7.6-4.1"), p("M8.2 13.2l7.6 4.1")],
        link: [
            p("M10 13.6a4 4 0 0 0 5.7.4l3-3a4 4 0 0 0-5.7-5.7l-1.4 1.4"),
            p("M14 10.4a4 4 0 0 0-5.7-.4l-3 3a4 4 0 0 0 5.7 5.7l1.4-1.4"),
        ],
        copy: [
            ["rect", { x: "8.5", y: "8.5", width: "11.5", height: "11.5", rx: "2.5" }],
            p("M15.5 8.5V6.5a2.5 2.5 0 0 0-2.5-2.5H6.5A2.5 2.5 0 0 0 4 6.5V13a2.5 2.5 0 0 0 2.5 2.5h2"),
        ],
        external: [
            p("M14 4h6v6"), p("M20 4l-8.5 8.5"),
            p("M18 14v3.5a2.5 2.5 0 0 1-2.5 2.5h-9A2.5 2.5 0 0 1 4 17.5v-9A2.5 2.5 0 0 1 6.5 6H10"),
        ],
        undo: [p("M4.5 4.5v5h5"), p("M5.3 14a7.5 7.5 0 1 0 1.9-7.2L4.5 9.5")],
        redo: [p("M19.5 4.5v5h-5"), p("M18.7 14a7.5 7.5 0 1 1-1.9-7.2l2.7 2.7")],
        reply: [p("M10 5.5L4 11.5l6 6"), p("M4 11.5h9a7 7 0 0 1 7 7v.5")],
        envelope: [["rect", { x: "3", y: "5", width: "18", height: "14", rx: "2.5" }], p("M3.8 7.2l8.2 6 8.2-6")],
        "paper-plane": [p("M20.5 3.5L10.5 13.5"), p("M20.5 3.5l-6.3 17-3.7-7-7-3.7z")],
        question: [c(12, 12, 9), p("M9.6 9.4a2.5 2.5 0 1 1 3.4 2.4c-.6.3-1 .8-1 1.5v.4"), dot(12, 17)],
        info: [p("M12 10.5v7"), dot(12, 6.75)],
        "circle-info": [c(12, 12, 9), p("M12 11v5.5"), dot(12, 7.75)],
        exclamation: [p("M12 5v9"), dot(12, 18.5)],
        warning: [p("M10.3 4.6a2 2 0 0 1 3.4 0l7.6 13a2 2 0 0 1-1.7 3H4.4a2 2 0 0 1-1.7-3z"), p("M12 9.5v4"), dot(12, 17)],
        bell,
        "bell-slash": [...bell, p("M3.5 3.5l17 17")],
        moon: [p("M19.5 14.6A8 8 0 0 1 9.4 4.5a8 8 0 1 0 10.1 10.1z")],
        house: [p("M3.5 11L12 3.8l8.5 7.2"), p("M6 9.2V20h12V9.2"), p("M10 20v-5.5h4V20")],
        star: [p("M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z", { class: "lwc-icon__fill" })],
        heart: [p("M12 20s-8-4.7-8-10.2A4.4 4.4 0 0 1 12 7.2a4.4 4.4 0 0 1 8 2.6C20 15.3 12 20 12 20z", { class: "lwc-icon__fill" })],
        spinner: [c(12, 12, 8.5, { class: "lwc-icon__track" }), p("M12 3.5a8.5 8.5 0 0 1 8.5 8.5", { class: "lwc-icon__arc" })],
        upload: [p("M12 15V4.5"), p("M7.5 9l4.5-4.5L16.5 9"), tray],
        download: [p("M12 4.5V15"), p("M7.5 10.5L12 15l4.5-4.5"), tray],
        camera: [p("M4.5 7.5h2.8L9 5h6l1.7 2.5h2.8A1.5 1.5 0 0 1 21 9v9a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18V9a1.5 1.5 0 0 1 1.5-1.5z"), c(12, 13, 3.5)],
        image: [["rect", { x: "3", y: "4", width: "18", height: "16", rx: "2.5" }], c(8.75, 9.25, 1.75), p("M20.5 15.5l-4.8-4.8L7 19.5")],
        pen: [p("M14.8 5.2l4 4L8.5 19.5H4.5v-4z"), p("M12.8 7.2l4 4")],
        "user-plus": [c(9.5, 8, 3.5), p("M3.5 20a6 6 0 0 1 12 0"), p("M19 8v6"), p("M16 11h6")],
        "user-pen": [c(9.5, 8, 3.5), p("M3.5 20a6 6 0 0 1 9-5.2"), p("M18 12.5l2.5 2.5-4.8 4.8h-2.5v-2.5z")],
        "sign-in": [p("M10 7l5 5-5 5"), p("M15 12H3.5"), p("M14 4h3.5A2.5 2.5 0 0 1 20 6.5v11a2.5 2.5 0 0 1-2.5 2.5H14")],
        "file-lines": [p("M14 3.5H7A2.5 2.5 0 0 0 4.5 6v12A2.5 2.5 0 0 0 7 20.5h10a2.5 2.5 0 0 0 2.5-2.5V9z"), p("M14 3.5V9h5.5"), p("M8.5 13h7"), p("M8.5 16.5h5")],
        "chart-line": [p("M4 4v16h16"), p("M7.5 14.5l3.5-3.5 3 3 5.5-5.5")],
        clock: [c(12, 12, 9), p("M12 7v5l3.2 2")],
        bolt: [p("M13.2 3L5 13.5h6.2L10.5 21 19 10.5h-6.2z")],
        rss: [p("M5 5a14 14 0 0 1 14 14"), p("M5 11a8 8 0 0 1 8 8"), dot(5.5, 18.5)],
        hashtag: [p("M9.5 4l-2 16"), p("M16.5 4l-2 16"), p("M4.5 9h15"), p("M4 15h15")],
        trash: [p("M4 7h16"), p("M9.5 7V5h5v2"), p("M6 7l1 12.5h10L18 7")],
        store: [p("M4 9.5L5.5 4.5h13L20 9.5"), p("M4 9.5a2.7 2.7 0 0 0 5.3 0 2.7 2.7 0 0 0 5.4 0 2.7 2.7 0 0 0 5.3 0"), p("M5.5 12.5V20h13v-7.5"), p("M10 20v-4.5h4V20")],
        "book-open": [p("M12 6.5C10 5 7.5 4.5 3.5 4.8v13.5c4-.3 6.5.2 8.5 1.7 2-1.5 4.5-2 8.5-1.7V4.8c-4-.3-6.5.2-8.5 1.7z"), p("M12 6.5V20")],
        scale: [p("M12 4v16"), p("M8 20h8"), p("M5 7h14"), p("M5 7l-2.5 6h5z"), p("M19 7l-2.5 6h5z"), p("M2.5 13a2.5 2.5 0 0 0 5 0"), p("M16.5 13a2.5 2.5 0 0 0 5 0")],
        podcast: [["rect", { x: "9", y: "3", width: "6", height: "11", rx: "3" }], p("M5.5 11a6.5 6.5 0 0 0 13 0"), p("M12 17.5V21")],
        flag: [p("M5.5 21V4"), p("M5.5 4.5c4-2 6 2 9.5 0s4.5 0 4.5 0v9s-1-2-4.5 0-5.5-2-9.5 0")],
        trophy: [p("M7.5 4h9v5.5a4.5 4.5 0 0 1-9 0z"), p("M7.5 6H4.5v1.2a3.5 3.5 0 0 0 3.3 3.5"), p("M16.5 6h3v1.2a3.5 3.5 0 0 1-3.3 3.5"), p("M12 14v6"), p("M8.5 20.5h7")],
    };

    // Font Awesome names (and aliases) that become crafted glyphs.
    const aliases: Record<string, string> = {
        "arrow-right": "arrow-right", "arrow-left": "arrow-left", "arrow-up": "arrow-up", "arrow-down": "arrow-down",
        "chevron-right": "chevron-right", "chevron-left": "chevron-left", "chevron-down": "chevron-down", "chevron-up": "chevron-up",
        "angle-right": "chevron-right", "angle-left": "chevron-left", "angle-down": "chevron-down", "angle-up": "chevron-up",
        times: "close", xmark: "close", close: "close", remove: "close",
        check: "check", "circle-check": "circle-check", "check-circle": "circle-check",
        plus: "plus", minus: "minus", bars: "bars", navicon: "bars",
        "arrow-down-wide-short": "sort", "sort-amount-down": "sort", "sliders-h": "sliders", sliders: "sliders",
        "magnifying-glass": "search", search: "search",
        "share-nodes": "share-nodes", "share-alt": "share-nodes",
        link: "link", copy: "copy", clone: "copy",
        "external-link-alt": "external", "external-link": "external", "arrow-up-right-from-square": "external", "up-right-from-square": "external",
        undo: "undo", "rotate-left": "undo", "undo-alt": "undo", "arrow-rotate-left": "undo",
        redo: "redo", "rotate-right": "redo", "redo-alt": "redo", "arrow-rotate-right": "redo",
        reply: "reply", envelope: "envelope", "paper-plane": "paper-plane",
        "circle-question": "question", "question-circle": "question",
        info: "info", "circle-info": "circle-info", "info-circle": "circle-info",
        exclamation: "exclamation", "triangle-exclamation": "warning", "exclamation-triangle": "warning",
        bell: "bell", "bell-slash": "bell-slash", moon: "moon", home: "house", house: "house",
        star: "star", heart: "heart",
        spinner: "spinner", "circle-notch": "spinner",
        "file-upload": "upload", upload: "upload", "cloud-arrow-up": "upload",
        download: "download", "file-download": "download",
        camera: "camera", image: "image",
        pen: "pen", pencil: "pen", "pen-to-square": "pen", edit: "pen",
        "user-plus": "user-plus", "user-pen": "user-pen", "user-edit": "user-pen",
        "right-to-bracket": "sign-in", "sign-in-alt": "sign-in",
        "file-lines": "file-lines", "file-alt": "file-lines", "file-pdf": "file-lines",
        "chart-line": "chart-line", clock: "clock", bolt: "bolt", rss: "rss", hashtag: "hashtag",
        trash: "trash", "trash-can": "trash",
        store: "store", "book-open": "book-open", "balance-scale": "scale", "scale-balanced": "scale",
        podcast: "podcast", microphone: "podcast", flag: "flag", trophy: "trophy",
    };

    const BRAND = /(^|\s)(fab|fa-brands)(\s|$)/;
    const REGULAR = /(^|\s)(far|fa-regular)(\s|$)/;
    // Collectible badge art and league marks keep their own drawing. Links that
    // sit beside a name are controls, so they join the system.
    const KEEP_OWN_ART = ".badge-class, .badge, .league-icon, [data-lwc-icon-keep]";
    const CONTROLS_IN_ART = ".athlete-name-links";
    // Pictorial glyphs often illustrate content (a sleep tile, an event type)
    // beside other solid art. They join the system only where they act as
    // controls or navigation; interface glyphs join everywhere.
    const SYMBOLS = new Set(["moon", "star", "heart", "bolt", "trophy", "flag", "clock", "house", "bell", "bell-slash", "podcast",
        "store", "book-open", "scale", "chart-line", "hashtag", "rss", "image", "camera", "user-plus", "user-pen", "file-lines", "circle-check"]);
    const SYMBOL_CONTEXT = "a, button, summary, label, [role='button'], [role='tab'], nav, footer, .footer, header, .custom-alert-mark";

    function build(name: string, className?: string): SVGSVGElement | null {
        const parts = Object.hasOwn(glyphs, name) ? glyphs[name] : undefined;
        if (!parts) return null;
        const svg = document.createElementNS(NS, "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.setAttribute("focusable", "false");
        svg.dataset["lwcGlyph"] = name;
        svg.setAttribute("class", className ? `lwc-icon ${className}` : "lwc-icon");
        for (const [tag, attrs] of parts) {
            const el = document.createElementNS(NS, tag);
            for (const [key, value] of Object.entries(attrs)) el.setAttribute(key, value);
            svg.append(el);
        }
        return svg;
    }

    function resolve(host: Element): string | null {
        const cls = host.getAttribute("class") ?? "";
        if (!cls.includes("fa-") || BRAND.test(cls)) return null;
        for (const token of cls.split(/\s+/)) {
            if (!token.startsWith("fa-")) continue;
            const key = token.slice(3);
            const name = Object.hasOwn(aliases, key) ? aliases[key] : undefined;
            if (name) return name;
        }
        return null;
    }

    // Optical tiers: small glyphs get a heavier stroke so every size reads with
    // the same visual weight next to its text.
    const pendingTier = new Set<HTMLElement>();
    let tierFrame = 0;
    const tierObserver = new ResizeObserver(entries => {
        for (const entry of entries) if (entry.target instanceof HTMLElement) queueTier(entry.target);
    });
    function queueTier(host: HTMLElement): void {
        pendingTier.add(host);
        if (!tierFrame) tierFrame = requestAnimationFrame(flushTiers);
    }
    function flushTiers(): void {
        tierFrame = 0;
        const hosts = [...pendingTier];
        pendingTier.clear();
        const sizes = hosts.map(host => (host.isConnected ? parseFloat(getComputedStyle(host).fontSize) : 0));
        hosts.forEach((host, i) => {
            const size = sizes[i] ?? 0;
            if (!size) return;
            const tier = size < 12.5 ? "xs" : size < 15 ? "s" : size < 24 ? "m" : "l";
            if (host.dataset.lwcIconTier !== tier) host.dataset.lwcIconTier = tier;
        });
    }

    // Remembers the last glyph each control showed, so a glyph that arrives by
    // innerHTML (spinner → "Saved" check) still morphs instead of snapping.
    const lastInControl = new WeakMap<Element, string>();
    let settled = false;
    const reducedMotion = matchMedia("(prefers-reduced-motion: reduce)");
    const entering = new Set<SVGSVGElement>();

    function controlOf(host: Element): Element | null {
        return host.closest("button, a, [role='button'], summary, label, .custom-alert-mark");
    }

    function clearEntry(svg: SVGSVGElement): void {
        entering.delete(svg);
        svg.removeAttribute("data-lwc-icon-enter");
    }

    function enter(svg: SVGSVGElement, state: string): void {
        if (!settled || reducedMotion.matches) return;
        // Each entry belongs to a new SVG. Inserting it starts the animation;
        // no synchronous layout read or restart delay is needed.
        svg.setAttribute("data-lwc-icon-enter", state);
        entering.add(svg);
    }

    document.addEventListener("animationend", event => {
        const target = event.target;
        if (!(target instanceof SVGElement)) return;
        const svg = target.closest<SVGSVGElement>("svg[data-lwc-icon-enter]");
        if (!svg) return;
        if (svg.dataset["lwcIconEnter"] === "draw" && event.animationName !== "lwc-icon-draw") return;
        if (event.animationName.startsWith("lwc-icon-")) clearEntry(svg);
    });
    document.addEventListener("animationcancel", event => {
        if (!(event.target instanceof SVGElement)) return;
        const svg = event.target.closest<SVGSVGElement>("svg[data-lwc-icon-enter]");
        if (svg && event.animationName.startsWith("lwc-icon-")) clearEntry(svg);
    });
    reducedMotion.addEventListener("change", () => {
        if (reducedMotion.matches) for (const svg of entering) clearEntry(svg);
    });

    function removeDrawing(host: HTMLElement): void {
        const svg = host.querySelector<SVGSVGElement>(":scope > svg.lwc-icon");
        if (svg) { clearEntry(svg); svg.remove(); }
        tierObserver.unobserve(host);
        pendingTier.delete(host);
        delete host.dataset.lwcIcon;
        delete host.dataset.lwcIconTier;
        host.removeAttribute("data-lwc-icon-filled");
    }

    function replay(host: HTMLElement): void {
        const name = host.dataset.lwcIcon;
        if (!name) return;
        const svg = build(name);
        if (!svg) return;
        const old = host.querySelector<SVGSVGElement>(":scope > svg.lwc-icon");
        if (old) { clearEntry(old); old.replaceWith(svg); }
        else host.append(svg);
        enter(svg, name === "check" || name === "circle-check" ? "draw" : name === "spinner" ? "spin" : "swap");
    }

    function apply(host: HTMLElement): void {
        if (host.hasAttribute("data-lwc-icon-typed")) {
            const control = host.parentElement;
            if (control && (!control.matches(TYPED_CONTROLS) || control.closest(KEEP_OWN_ART))) {
                const text = typedText.get(host);
                removeDrawing(host);
                if (text) host.replaceWith(document.createTextNode(text));
            } else if (!host.querySelector(":scope > svg.lwc-icon")) replay(host);
            return;
        }
        let name = resolve(host);
        if (host.closest("[data-lwc-icon-keep]") || (host.closest(KEEP_OWN_ART) && !host.closest(CONTROLS_IN_ART))) name = null;
        if (name && SYMBOLS.has(name) && !host.closest(SYMBOL_CONTEXT)) name = null;
        const current = host.dataset.lwcIcon;
        if (!name) {
            if (current) {
                // The page switched to a glyph this system does not draw: hand back to Font Awesome.
                removeDrawing(host);
            }
            return;
        }
        const filled = !REGULAR.test(host.className);
        const fillChanged = host.hasAttribute("data-lwc-icon-filled") !== filled;
        host.toggleAttribute("data-lwc-icon-filled", filled);
        if (current === name && host.querySelector(":scope > svg.lwc-icon")) {
            if (fillChanged && (name === "star" || name === "heart")) replay(host);
            return;
        }

        const svg = build(name);
        if (!svg) return;
        const control = controlOf(host);
        const previous = current ?? (control ? lastInControl.get(control) : undefined);
        const old = host.querySelector<SVGSVGElement>(":scope > svg.lwc-icon");
        if (old) { clearEntry(old); old.remove(); }
        host.dataset.lwcIcon = name;
        host.append(svg);
        if (control) lastInControl.set(control, name);
        queueTier(host);
        tierObserver.observe(host);

        // Only morph after the visitor has done something: loads never animate.
        if (settled && previous && previous !== name) {
            enter(svg, name === "check" || name === "circle-check" ? "draw" : name === "spinner" ? "spin" : "swap");
        } else if (settled && !previous && name === "check" && host.closest(".custom-alert-mark")) {
            enter(svg, "draw");
        }
    }

    // Labelled controls that type their glyph (times, angle quotes, minus, plus)
    // draw it with the same pen.
    const typed: Record<string, string> = {
        "×": "close", "✕": "close", "✖": "close",
        "‹": "chevron-left", "❮": "chevron-left", "›": "chevron-right", "❯": "chevron-right",
        "←": "arrow-left", "→": "arrow-right", "−": "minus", "+": "plus",
    };
    const TYPED_CONTROLS = "button[aria-label], a[aria-label], [role='button'][aria-label]";
    const typedText = new WeakMap<HTMLElement, string>();

    function applyTyped(control: HTMLElement): void {
        if (control.children.length || control.closest(KEEP_OWN_ART)) return;
        const name = typed[control.textContent?.trim() ?? ""];
        if (!name) return;
        const svg = build(name);
        if (!svg) return;
        const host = document.createElement("i");
        host.dataset.lwcIcon = name;
        host.dataset.lwcIconTyped = "";
        typedText.set(host, control.textContent ?? "");
        host.append(svg);
        control.replaceChildren(host);
        queueTier(host);
        tierObserver.observe(host);
        lastInControl.set(control, name);
    }

    function upgrade(root: ParentNode): void {
        if (root instanceof HTMLElement && root.tagName === "I") apply(root);
        root.querySelectorAll<HTMLElement>("i[class*='fa-'], i[data-lwc-icon]").forEach(apply);
        if (root instanceof HTMLElement && root.matches(TYPED_CONTROLS)) applyTyped(root);
        root.querySelectorAll<HTMLElement>(TYPED_CONTROLS).forEach(applyTyped);
    }

    const observer = new MutationObserver(records => {
        const roots = new Set<HTMLElement>();
        const hosts = new Set<HTMLElement>();
        for (const record of records) {
            if (record.type === "attributes") {
                const target = record.target;
                if (!(target instanceof HTMLElement)) continue;
                if (target.tagName === "I") hosts.add(target);
                else if (record.attributeName !== "class" || scopeClasses(target.className) !== scopeClasses(record.oldValue ?? "")) roots.add(target);
                continue;
            }
            const target = record.target instanceof HTMLElement ? record.target : record.target.parentElement;
            if (target?.tagName === "I" && target.hasAttribute("data-lwc-icon")) hosts.add(target);
            else if (target?.matches(TYPED_CONTROLS)) hosts.add(target);
            record.addedNodes.forEach(node => {
                if (node instanceof HTMLElement) roots.add(node);
            });
            record.removedNodes.forEach(node => {
                if (node instanceof SVGSVGElement && !node.isConnected) clearEntry(node);
                if (!(node instanceof HTMLElement) || node.isConnected) return;
                for (const icon of [node, ...node.querySelectorAll<HTMLElement>("i[data-lwc-icon]")]) {
                    tierObserver.unobserve(icon);
                    pendingTier.delete(icon);
                    const svg = icon.querySelector<SVGSVGElement>(":scope > svg.lwc-icon");
                    if (svg) clearEntry(svg);
                }
            });
        }
        // Parser and renderer batches often include both an ancestor and its
        // children. Visit each subtree once, and ignore ordinary row classes.
        for (const root of roots) {
            if (!root.isConnected) continue;
            let parent = root.parentElement;
            while (parent && !roots.has(parent)) parent = parent.parentElement;
            if (!parent) upgrade(root);
        }
        for (const host of hosts) {
            if (!host.isConnected) continue;
            if (host.tagName === "I") apply(host);
            else applyTyped(host);
        }
    });
    const scopeClasses = (value: string): string => value.split(/\s+/).filter(token =>
        ["badge-class", "badge", "league-icon", "athlete-name-links", "footer", "custom-alert-mark"].includes(token)).sort().join(" ");
    observer.observe(document.documentElement, {
        childList: true, subtree: true, characterData: true, attributes: true, attributeOldValue: true,
        attributeFilter: ["class", "role", "aria-label", "data-lwc-icon-keep"],
    });
    if (document.body) upgrade(document.body);

    // Glyph morphs wait for the first real gesture, like the shared motion gate.
    const settle = (event: Event): void => {
        if (!event.isTrusted) return;
        settled = true;
        removeEventListener("pointerdown", settle, true);
        removeEventListener("keydown", settle, true);
    };
    addEventListener("pointerdown", settle, true);
    addEventListener("keydown", settle, true);

    window.LwcIcons = {
        svg: (name, className) => build(Object.hasOwn(aliases, name) ? aliases[name]! : name, className)?.outerHTML ?? "",
        upgrade,
        replay,
        names: () => Object.keys(glyphs),
    };
})();
