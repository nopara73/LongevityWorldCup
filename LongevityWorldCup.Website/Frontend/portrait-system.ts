// One crafted treatment for every athlete portrait on the site.
// portrait-system.css owns how each state looks; this classic script only
// decides which state a portrait is in.
//
// - Tiers: each portrait is sized into xs / sm / md / lg by its rendered
//   width, so the frame, rim and motion follow the size, not the page.
// - Loading: a portrait that is still on its way shows a soft shimmer in its
//   own frame, then "develops" into the photo once decoded.
//   Anything already decoded appears at once.
// - Fallback: the white logo stand-in becomes a monogram (initials, or a
//   silhouette when there is no name) drawn in the site's teal.
// - Identity: page-owned whole-card motion handles athlete changes; a portrait
//   never overlays the previous athlete on the new profile.
// - Crisp: generated thumbnails get a srcset/sizes pair, so phones and retina
//   screens pick the sharp variant and tiny avatars skip the big one.
// - Depth: larger portraits drift a little towards the pointer and zoom on
//   press. Reduced motion keeps every state, without the movement.
//
// Nothing here replaces a page's own handlers; it never blocks input and
// without scripts every portrait renders exactly as before.

interface Window {
    LwcPortrait?: {
        readonly enhance: (img: HTMLImageElement) => void;
        readonly monogram: (name: string) => string;
        readonly scan: (root?: ParentNode) => void;
    };
}

(() => {
    const root = document.documentElement;
    if (!("MutationObserver" in window) || root.dataset["lwcPortraits"] === "on") return;
    root.dataset["lwcPortraits"] = "on";

    const SELECTOR = [
        "img.portrait",
        "img.podium-portrait",
        "img#modalProfilePic",
        "img.sticky-athlete-portrait",
        ".flag-team-portraits img",
        ".events-board img.avatar",
        "img.event-athlete-preview-portrait",
        ".lmx-participant-avatar img",
        ".lmx-mention-avatar img",
        ".lmx-quote-portrait img",
        ".lmx-discussion-quick-avatar img",
        ".lmx-discussion-author-avatar img",
        ".bioage-rank-row-avatar img",
        "img.athlete-option-thumb",
        "img.ro-row-portrait",
        ".ro-step-portrait img",
        "img.gma-avatar"
    ].join(",");

    // Pictures with their own illustrated placeholder or their own transition.
    const OWN_MOTION = ".athlete-picture-frame img, img.athlete-picture-placeholder:not(.athlete-option-thumb)";
    const FALLBACK_PATHS = ["/assets/HdLogo_thumb_sm.png", "/assets/content-images/play-athlete-placeholder"];
    const THUMB = /^(.*\/generated\/thumbs\/athletes\/.+)_thumb_(sm|md)_([0-9a-f]+\.webp)(\?.*)?$/i;
    const THUMB_WIDTH: Record<string, number> = { sm: 96, md: 320 };

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)");
    const known = new WeakSet<HTMLImageElement>();
    const tracked = new WeakMap<HTMLImageElement, { source: string; version: number; timer?: number; decoding?: boolean }>();
    const failedThumbs = new WeakMap<HTMLImageElement, string>();
    const ownSrcset = new WeakMap<HTMLImageElement, string>();
    const monogramCache = new Map<string, string>();

    // --- Tiers --------------------------------------------------------------

    function tierFor(width: number): string {
        if (width < 36) return "xs";
        if (width < 84) return "sm";
        if (width < 180) return "md";
        return "lg";
    }

    const sizer = "ResizeObserver" in window
        ? new ResizeObserver(entries => {
            for (const entry of entries) {
                const img = entry.target as HTMLImageElement;
                const width = entry.borderBoxSize?.[0]?.inlineSize ?? img.getBoundingClientRect().width;
                if (width > 0) applySize(img, width);
            }
        })
        : null;

    // Lazy portraits outside the viewport can remain pending indefinitely.
    // Keep their loading state, but spend animation work only where it is seen.
    const visibility = "IntersectionObserver" in window
        ? new IntersectionObserver(entries => {
            for (const entry of entries) {
                const img = entry.target as HTMLImageElement;
                if (img.isConnected && known.has(img)) {
                    img.toggleAttribute("data-lwc-portrait-in-view", entry.isIntersecting);
                }
            }
        })
        : null;

    // Small portraits are oversampled: a 32px avatar on a 3x phone is exactly
    // 96 device pixels, which reads soft once the ring and rim crop it.
    function slot(width: number): string {
        return `${Math.max(1, Math.round(width < 84 ? width * 1.5 : width))}px`;
    }

    function applySize(img: HTMLImageElement, width: number): void {
        const tier = tierFor(width);
        if (img.dataset["lwcPortraitTier"] !== tier) img.dataset["lwcPortraitTier"] = tier;
        if (ownSrcset.get(img) === img.getAttribute("srcset")) {
            const sizes = slot(width);
            if (img.sizes !== sizes) img.sizes = sizes;
        }
    }

    // --- Crisp sources -----------------------------------------------------------

    // Generated thumbnails come in a 96px and a 320px square; offer both and
    // let the browser pick by the portrait's real size and pixel density.
    function syncSrcset(img: HTMLImageElement): void {
        const src = img.getAttribute("src") || "";
        const match = THUMB.exec(src);
        const mine = ownSrcset.get(img);
        if (!match || failedThumbs.get(img) === match[1]) {
            releaseSources(img);
            return;
        }
        // A page that writes its own srcset keeps it.
        const current = img.getAttribute("srcset");
        if (current && current !== mine) return;

        const [, stem, , file, query = ""] = match;
        const srcset = ["sm", "md"].map(size => `${stem}_thumb_${size}_${file}${query} ${THUMB_WIDTH[size]}w`).join(", ");
        ownSrcset.set(img, srcset);
        if (!img.sizes) img.sizes = slot(Number(img.getAttribute("width")) || 64);
        if (current !== srcset) img.setAttribute("srcset", srcset);
    }

    function releaseSources(img: HTMLImageElement): void {
        const mine = ownSrcset.get(img);
        if (mine !== undefined && img.getAttribute("srcset") === mine) {
            img.removeAttribute("srcset");
            img.removeAttribute("sizes");
        }
        ownSrcset.delete(img);
    }

    // --- Fallback monograms -------------------------------------------------------

    function nameOf(img: HTMLImageElement): string {
        const owner = img.closest<HTMLElement>("[data-athlete-name], [data-display-name]");
        const fromOwner = owner?.dataset["athleteName"] || owner?.dataset["displayName"];
        if (fromOwner) return fromOwner;
        if (img.id === "modalProfilePic") {
            const title = document.getElementById("athleteName");
            const text = title?.querySelector(".athlete-name-text")?.textContent || title?.firstChild?.textContent || "";
            if (text.trim()) return text.trim();
        }
        return (img.alt || "")
            .replace(/\b(portrait|headshot|profile picture|avatar|photo)\b/gi, "")
            .replace(/^(loading )?athlete\s*$/i, "")
            .trim();
    }

    function initials(name: string): string {
        const words = name
            .replace(/[^\p{L}\p{N}\s'-]/gu, " ")
            .split(/\s+/)
            .filter(word => word && /\p{L}/u.test(word));
        if (!words.length) return "";
        const first = words[0] ?? "";
        const last = words.length > 1 ? words[words.length - 1] ?? "" : "";
        const letters = (Array.from(first)[0] ?? "") + (last ? Array.from(last)[0] ?? "" : "");
        return letters.toLocaleUpperCase();
    }

    function hash(text: string): number {
        let value = 2166136261;
        for (let i = 0; i < text.length; i++) {
            value ^= text.charCodeAt(i);
            value = Math.imul(value, 16777619);
        }
        return value >>> 0;
    }

    // Teal family only: accent, accent-hover, accent-bright and the success green.
    const TONES: ReadonlyArray<readonly [string, string]> = [
        ["#19c3d1", "#087685"],
        ["#087685", "#065d68"],
        ["#0f9aa8", "#1f7a38"],
        ["#19c3d1", "#065d68"]
    ];

    function monogram(name: string): string {
        const letters = initials(name);
        const key = letters || "-";
        const seed = hash(name || key);
        const cacheKey = `${key}|${seed % TONES.length}|${seed % 4}`;
        const cached = monogramCache.get(cacheKey);
        if (cached) return cached;

        const [light, deep] = TONES[seed % TONES.length] ?? TONES[0]!;
        const angle = [0, 1, 2, 3][seed % 4] ?? 0;
        const [x1, y1, x2, y2] = [["0", "0", "1", "1"], ["1", "0", "0", "1"], ["0.2", "0", "0.8", "1"], ["0", "0.2", "1", "0.8"]][angle]!;
        const figure = `<g fill="#fff" opacity="${letters ? 0.12 : 0.62}"><circle cx="50" cy="41" r="17"/><path d="M15 100c2-21 17-34 35-34s33 13 35 34z"/></g>`;
        const text = letters
            ? `<text x="50" y="50" dy=".36em" text-anchor="middle" font-family="Roboto, 'Segoe UI', Arial, sans-serif" font-weight="700" font-size="${letters.length > 1 ? 36 : 42}" letter-spacing="1" fill="#fff">${letters.replace(/[<&>"]/g, "")}</text>`
            : "";
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100">`
            + `<defs><linearGradient id="t" x1="${x1}" y1="${y1}" x2="${x2}" y2="${y2}"><stop offset="0" stop-color="${light}"/><stop offset="1" stop-color="${deep}"/></linearGradient>`
            + `<radialGradient id="s" cx=".3" cy=".22" r=".85"><stop offset="0" stop-color="#fff" stop-opacity=".34"/><stop offset=".55" stop-color="#fff" stop-opacity="0"/></radialGradient></defs>`
            + `<rect width="100" height="100" fill="url(#t)"/>${figure}<rect width="100" height="100" fill="url(#s)"/>${text}</svg>`;
        const url = `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
        monogramCache.set(cacheKey, url);
        return url;
    }

    function isFallbackSrc(src: string): boolean {
        if (!src) return false;
        try {
            const path = new URL(src, location.href).pathname;
            return FALLBACK_PATHS.some(fallback => path.startsWith(fallback));
        } catch {
            return false;
        }
    }

    function useMonogram(img: HTMLImageElement): void {
        if (img.matches(OWN_MOTION)) return;
        // Responsive sources supplied by a page keep that page's recovery path.
        if (img.srcset && img.getAttribute("srcset") !== ownSrcset.get(img)) return;
        const url = monogram(nameOf(img));
        img.dataset["lwcPortraitFallback"] = "";
        releaseSources(img);
        if (img.getAttribute("src") !== url) img.src = url;
    }

    // --- Loading and current-image readiness ------------------------------------

    function setState(img: HTMLImageElement, state: "loading" | "developing" | "ready"): void {
        if (img.dataset["lwcPortraitState"] !== state) img.dataset["lwcPortraitState"] = state;
    }

    function loaded(img: HTMLImageElement): boolean {
        return img.complete && img.naturalWidth > 0;
    }

    function signature(img: HTMLImageElement): string {
        return `${img.getAttribute("src") || ""}|${img.getAttribute("srcset") || ""}`;
    }

    function finishDecode(img: HTMLImageElement, instant = false): void {
        const state = tracked.get(img);
        if (!state || state.decoding || !loaded(img)) return;
        const version = state.version;
        const source = signature(img);
        state.decoding = true;
        void img.decode().then(() => {
            if (!img.isConnected || tracked.get(img) !== state || state.version !== version
                || signature(img) !== source || !loaded(img)) return;
            state.decoding = false;
            setState(img, instant || reduced.matches ? "ready" : "developing");
            if (!instant && !reduced.matches) {
                state.timer = window.setTimeout(() => {
                    if (tracked.get(img) === state && signature(img) === source) setState(img, "ready");
                }, 400);
            }
        }, () => {
            if (tracked.get(img) === state) state.decoding = false;
        });
    }

    function track(img: HTMLImageElement): void {
        const src = img.getAttribute("src") || "";
        // Clear owned candidates even when the page removes src during a step.
        syncSrcset(img);
        if (isFallbackSrc(src)) {
            useMonogram(img);
            return;
        }
        if (!src.startsWith("data:")) delete img.dataset["lwcPortraitFallback"];
        const source = signature(img);
        const previous = tracked.get(img);
        if (previous?.source === source) return;
        if (previous?.timer) clearTimeout(previous.timer);
        tracked.set(img, { source, version: (previous?.version || 0) + 1 });
        setState(img, "loading");
        if (src && loaded(img)) finishDecode(img, true);
        else if (src && img.complete && img.naturalWidth === 0) {
            // The error may have fired before this deferred layer adopted the
            // image. Give page handlers their turn, then recover a still-failed
            // source just as the captured error listener would.
            queueMicrotask(() => {
                if (img.isConnected && signature(img) === source && img.complete && img.naturalWidth === 0
                    && img.dataset["lwcPortraitFallback"] === undefined) useMonogram(img);
            });
        }
    }

    function onLoad(event: Event): void {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !known.has(img)) return;
        // Page handlers can reject a tiny image or select another candidate.
        // Start decode only after those handlers have finished.
        queueMicrotask(() => {
            if (!img.isConnected) return;
            track(img);
            finishDecode(img);
        });
    }

    function onError(event: Event): void {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !known.has(img)) return;
        const failed = img.getAttribute("src") || "";
        const match = THUMB.exec(failed);
        if (match) failedThumbs.set(img, match[1]!);
        // A failed generated srcset must not override the page's next candidate.
        releaseSources(img);
        queueMicrotask(() => {
            if (!img.isConnected || !failed || img.getAttribute("src") !== failed) return;
            if (img.dataset["lwcPortraitFallback"] !== undefined) return;
            if (img.complete && img.naturalWidth === 0) useMonogram(img);
        });
    }

    // --- Depth: pointer drift and press zoom ------------------------------------------

    let driftTarget: HTMLImageElement | null = null;
    let driftFrame = 0;
    let driftX = 0;
    let driftY = 0;

    function deep(img: Element | null): img is HTMLImageElement {
        return img instanceof HTMLImageElement
            && known.has(img)
            && (img.dataset["lwcPortraitTier"] === "md" || img.dataset["lwcPortraitTier"] === "lg");
    }

    function writeDrift(): void {
        driftFrame = 0;
        if (!driftTarget) return;
        driftTarget.style.setProperty("--lwc-portrait-dx", driftX.toFixed(3));
        driftTarget.style.setProperty("--lwc-portrait-dy", driftY.toFixed(3));
    }

    function onPointerMove(event: PointerEvent): void {
        if (event.pointerType !== "mouse" || reduced.matches || !finePointer.matches) return;
        const img = event.target instanceof Element ? event.target : null;
        if (!deep(img)) {
            if (driftTarget) clearDrift();
            return;
        }
        if (driftTarget && driftTarget !== img) clearDrift();
        driftTarget = img;
        const rect = img.getBoundingClientRect();
        driftX = Math.max(-1, Math.min(1, ((event.clientX - rect.left) / rect.width) * 2 - 1));
        driftY = Math.max(-1, Math.min(1, ((event.clientY - rect.top) / rect.height) * 2 - 1));
        if (!driftFrame) driftFrame = requestAnimationFrame(writeDrift);
    }

    function clearDrift(): void {
        if (!driftTarget) return;
        driftTarget.style.removeProperty("--lwc-portrait-dx");
        driftTarget.style.removeProperty("--lwc-portrait-dy");
        driftTarget = null;
    }

    // --- Registry ------------------------------------------------------------

    function enhance(img: HTMLImageElement): void {
        if (img.dataset["lwcPortraitGhost"] !== undefined || img.matches(OWN_MOTION)
            || img.closest("[data-athlete-motion-copy]")) return;
        if (!known.has(img)) {
            known.add(img);
            img.dataset["lwcPortrait"] = "";
            sizer?.observe(img);
            if (visibility) visibility.observe(img);
            else img.setAttribute("data-lwc-portrait-in-view", "");
            if (!sizer) applySize(img, Number(img.getAttribute("width")) || 64);
        }
        track(img);
    }

    function scan(node: ParentNode = document): void {
        if (node instanceof HTMLImageElement && node.matches(SELECTOR)) enhance(node);
        node.querySelectorAll?.<HTMLImageElement>(SELECTOR).forEach(enhance);
    }

    document.addEventListener("load", onLoad, true);
    document.addEventListener("error", onError, true);
    document.addEventListener("pointermove", onPointerMove, { passive: true });
    document.addEventListener("pointerleave", clearDrift);
    document.addEventListener("animationend", event => {
        const img = event.target;
        if (img instanceof HTMLImageElement && event.animationName === "lwc-portrait-develop") setState(img, "ready");
    }, true);

    function release(img: HTMLImageElement): void {
        sizer?.unobserve(img);
        visibility?.unobserve(img);
        img.removeAttribute("data-lwc-portrait-in-view");
        known.delete(img);
        const state = tracked.get(img);
        if (state?.timer) clearTimeout(state.timer);
        tracked.delete(img);
        if (driftTarget === img) clearDrift();
    }

    new MutationObserver(records => {
        const changed = new Set<HTMLImageElement>();
        for (const record of records) {
            if (record.type === "attributes") {
                const target = record.target;
                if (target instanceof HTMLImageElement && (known.has(target) || target.matches(SELECTOR))) {
                    changed.add(target);
                } else if (target instanceof Element && ["data-athlete-name", "data-display-name"].includes(record.attributeName || "")) {
                    target.querySelectorAll<HTMLImageElement>(SELECTOR).forEach(img => changed.add(img));
                }
                continue;
            }
            record.removedNodes.forEach(node => {
                if (!(node instanceof Element) || node.isConnected) return;
                if (node instanceof HTMLImageElement && known.has(node)) release(node);
                node.querySelectorAll<HTMLImageElement>("img[data-lwc-portrait]").forEach(release);
            });
            record.addedNodes.forEach(node => {
                if (node instanceof Element && node.isConnected) scan(node);
            });
        }
        for (const img of changed) {
            if (!img.isConnected) continue;
            if (img.dataset["lwcPortraitFallback"] !== undefined && img.src.startsWith("data:")) useMonogram(img);
            enhance(img);
        }
    }).observe(root, { childList: true, subtree: true, attributes: true,
        attributeFilter: ["src", "srcset", "alt", "data-athlete-name", "data-display-name"] });

    reduced.addEventListener("change", () => {
        if (reduced.matches) {
            clearDrift();
            document.querySelectorAll<HTMLImageElement>('img[data-lwc-portrait-state="developing"]').forEach(img => setState(img, "ready"));
        }
    });
    scan();
    window.LwcPortrait = { enhance, monogram, scan };
})();
