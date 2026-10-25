// One crafted treatment for every athlete portrait on the site.
// portrait-system.css owns how each state looks; this classic script only
// decides which state a portrait is in.
//
// - Tiers: each portrait is sized into xs / sm / md / lg by its rendered
//   width, so the frame, rim and motion follow the size, not the page.
// - Loading: a portrait that is still on its way shows a soft shimmer in its
//   own frame, then "develops" into the photo (fade + unblur) once decoded.
//   Anything already decoded appears at once.
// - Fallback: the white logo stand-in becomes a monogram (initials, or a
//   silhouette when there is no name) drawn in the site's teal.
// - Crossfade: a painted portrait that switches to another photo dissolves
//   from the old one instead of snapping.
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
        "img.lmx-participant-avatar",
        "img.lmx-mention-avatar",
        "img.lmx-quote-portrait",
        "img.lmx-discussion-quick-avatar",
        "img.lmx-discussion-author-avatar",
        "img.bioage-rank-row-avatar",
        "img.athlete-option-thumb",
        "img.ro-row-portrait",
        "img.ro-step-portrait",
        "img.gma-avatar"
    ].join(",");

    // Pictures with their own illustrated placeholder or their own transition.
    const OWN_MOTION = ".athlete-picture-frame img, img.athlete-picture-placeholder";
    const FALLBACK_PATHS = ["/assets/HdLogo_thumb_sm.png", "/assets/content-images/play-athlete-placeholder"];
    const THUMB = /^(.*\/generated\/thumbs\/athletes\/.+)_thumb_(sm|md)_([0-9a-f]+\.webp)(\?.*)?$/i;
    const THUMB_WIDTH: Record<string, number> = { sm: 96, md: 320 };

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)");
    const known = new WeakSet<HTMLImageElement>();
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

    // Small portraits are oversampled: a 32px avatar on a 3x phone is exactly
    // 96 device pixels, which reads soft once the ring and rim crop it.
    function slot(width: number): string {
        return `${Math.max(1, Math.round(width < 84 ? width * 1.5 : width))}px`;
    }

    function applySize(img: HTMLImageElement, width: number): void {
        const tier = tierFor(width);
        if (img.dataset["lwcPortraitTier"] !== tier) img.dataset["lwcPortraitTier"] = tier;
        if (ownSrcset.has(img)) {
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
        if (!match) {
            if (mine !== undefined && img.getAttribute("srcset") === mine) img.removeAttribute("srcset");
            ownSrcset.delete(img);
            return;
        }
        // A page that writes its own srcset keeps it.
        const current = img.getAttribute("srcset");
        if (current && current !== mine) return;

        const [, stem, , file, query = ""] = match;
        const srcset = ["sm", "md"].map(size => `${stem}_thumb_${size}_${file}${query} ${THUMB_WIDTH[size]}w`).join(", ");
        ownSrcset.set(img, srcset);
        const width = img.getBoundingClientRect().width;
        if (width > 0) img.sizes = slot(width);
        else if (!img.sizes) img.sizes = "64px";
        if (current !== srcset) img.setAttribute("srcset", srcset);
    }

    // --- Fallback monograms -------------------------------------------------------

    function nameOf(img: HTMLImageElement): string {
        const owner = img.closest<HTMLElement>("[data-athlete-name]");
        const fromOwner = owner?.dataset["athleteName"];
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
        const url = monogram(nameOf(img));
        img.dataset["lwcPortraitFallback"] = "";
        img.removeAttribute("srcset");
        ownSrcset.delete(img);
        if (img.getAttribute("src") !== url) img.src = url;
    }

    // --- Loading and crossfade --------------------------------------------------

    function setState(img: HTMLImageElement, state: "loading" | "developing" | "ready"): void {
        if (img.dataset["lwcPortraitState"] !== state) img.dataset["lwcPortraitState"] = state;
    }

    function loaded(img: HTMLImageElement): boolean {
        return img.complete && img.naturalWidth > 0;
    }

    function track(img: HTMLImageElement): void {
        const src = img.getAttribute("src") || "";
        if (!src) {
            setState(img, "loading");
            return;
        }
        if (isFallbackSrc(src)) {
            useMonogram(img);
            return;
        }
        if (!src.startsWith("data:")) delete img.dataset["lwcPortraitFallback"];
        syncSrcset(img);
        setState(img, loaded(img) ? "ready" : "loading");
    }

    // The old photo floats over the new one and dissolves once the new one
    // has decoded, so a portrait never blinks to empty between athletes.
    function crossfade(img: HTMLImageElement, oldSrc: string): void {
        if (reduced.matches || !oldSrc || img.matches(OWN_MOTION) || img.closest("[data-athlete-motion-copy]")) return;
        const parent = img.parentElement;
        if (!parent || img.offsetWidth === 0) return;

        const style = getComputedStyle(img);
        const restorePosition = getComputedStyle(parent).position === "static" ? parent.style.position : null;
        if (restorePosition !== null) parent.style.position = "relative";

        const ghost = document.createElement("img");
        ghost.src = oldSrc;
        ghost.alt = "";
        ghost.setAttribute("aria-hidden", "true");
        ghost.dataset["lwcPortraitGhost"] = "";
        Object.assign(ghost.style, {
            left: `${img.offsetLeft}px`,
            top: `${img.offsetTop}px`,
            width: `${img.offsetWidth}px`,
            height: `${img.offsetHeight}px`,
            borderRadius: style.borderRadius,
            borderStyle: style.borderStyle,
            borderWidth: style.borderWidth,
            borderColor: style.borderColor,
            objectFit: style.objectFit,
            objectPosition: style.objectPosition,
            transform: style.transform === "none" ? "" : style.transform
        });
        img.after(ghost);

        let done = false;
        const finish = (): void => {
            if (done) return;
            done = true;
            img.removeEventListener("load", reveal);
            img.removeEventListener("error", finish);
            ghost.remove();
            if (restorePosition !== null) parent.style.position = restorePosition;
        };
        const reveal = (): void => {
            const fade = ghost.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 280, easing: "cubic-bezier(0.2, 0, 0, 1)", fill: "forwards" });
            fade.finished.then(finish, finish);
        };
        img.addEventListener("load", reveal, { once: true });
        img.addEventListener("error", finish, { once: true });
        // Never leave a ghost behind a request that stalls.
        window.setTimeout(() => { if (!done) reveal(); }, 4000);
    }

    function onLoad(event: Event): void {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !known.has(img)) return;
        if (img.dataset["lwcPortraitState"] === "loading") {
            setState(img, reduced.matches ? "ready" : "developing");
            // A page animation can outrank the develop keyframes; never stay mid-develop.
            window.setTimeout(() => { if (img.dataset["lwcPortraitState"] === "developing") setState(img, "ready"); }, 700);
        } else {
            setState(img, "ready");
        }
    }

    function onError(event: Event): void {
        const img = event.target;
        if (!(img instanceof HTMLImageElement) || !known.has(img)) return;
        // Page handlers try their other candidates first; once they give up
        // on a stand-in (or have none) the monogram takes over.
        window.setTimeout(() => {
            if (!img.isConnected) return;
            const src = img.getAttribute("src") || "";
            if (isFallbackSrc(src) || !img.onerror || (img.complete && img.naturalWidth === 0)) useMonogram(img);
        }, 0);
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
        if (img.dataset["lwcPortraitGhost"] !== undefined) return;
        if (!known.has(img)) {
            known.add(img);
            img.dataset["lwcPortrait"] = "";
            sizer?.observe(img);
            const width = img.getBoundingClientRect().width;
            if (width > 0) applySize(img, width);
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

    new MutationObserver(records => {
        for (const record of records) {
            if (record.type === "attributes") {
                const img = record.target;
                if (!(img instanceof HTMLImageElement)) continue;
                if (!known.has(img)) {
                    if (img.matches(SELECTOR)) enhance(img);
                    continue;
                }
                if (record.attributeName === "src") {
                    const oldSrc = record.oldValue || "";
                    const wasPainted = img.dataset["lwcPortraitState"] === "ready" && !!oldSrc && !isFallbackSrc(oldSrc);
                    track(img);
                    const nowSrc = img.getAttribute("src") || "";
                    if (wasPainted && nowSrc && nowSrc !== oldSrc && !loaded(img)) crossfade(img, oldSrc);
                    if (wasPainted && nowSrc && !loaded(img)) setState(img, "ready");
                } else if (record.attributeName === "class" && img.classList.contains("portrait-fallback")) {
                    useMonogram(img);
                }
                continue;
            }
            record.addedNodes.forEach(node => {
                if (node instanceof Element) scan(node);
            });
        }
    }).observe(root, { childList: true, subtree: true, attributes: true, attributeOldValue: true, attributeFilter: ["src", "class"] });

    scan();
    window.LwcPortrait = { enhance, monogram, scan };
})();
