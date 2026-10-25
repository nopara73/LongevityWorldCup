// One share sheet for every place an athlete, a result or the site can be shared.
//
// Any element marked [data-lwc-share] opens it. The element may name what to share
// with data-share-url, data-share-title, data-share-text and data-share-image; anything
// left out comes from the page (current leaderboard selection or canonical metadata). Phones with a
// native share sheet use that instead. Everywhere else a popover opens from the
// trigger (a bottom sheet on narrow screens) with the 1200x630 card developing in,
// one copy action whose link glyph morphs into a check, and the usual destinations.
//
// The athlete dialog keeps its own share menu and only borrows this file's styles.
// Motion follows motion.css and switches off under prefers-reduced-motion.

interface LwcSharePayload {
    url: string;
    title: string;
    text: string;
    image: string | null;
}

interface LwcShareApi {
    readonly open: (trigger: HTMLElement) => void;
    readonly close: (restoreFocus?: boolean) => void;
    readonly mount: (host: Element, position?: InsertPosition) => HTMLButtonElement;
}

interface Window {
    LwcShare: LwcShareApi;
}

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const narrow = window.matchMedia("(max-width: 600px)");
    const coarse = window.matchMedia("(pointer: coarse)");
    const svgNs = "http://www.w3.org/2000/svg";
    const feedbackMs = 1100;

    let sheet: HTMLElement | null = null;
    let scrim: HTMLElement | null = null;
    let current: { trigger: HTMLElement; payload: LwcSharePayload; version: number } | null = null;
    let version = 0;
    let closeTimer = 0;
    let feedbackTimer = 0;
    let pendingNative: { trigger: HTMLElement; version: number } | null = null;
    let pendingCopy = false;
    const inertSiblings = new Map<HTMLElement, boolean>();
    const layoutObserver = new ResizeObserver(() => {
        if (sheet && current) place(sheet, current.trigger);
    });

    // ---- What is being shared -------------------------------------------------

    function canonicalOrigin(): string {
        const host = window.location.hostname;
        const local = host === "localhost" || host === "127.0.0.1" || host === "[::1]" || host === "::1";
        if (!local) return window.location.origin;
        const canonical = document.querySelector<HTMLLinkElement>('link[rel="canonical"]');
        try {
            return canonical?.href ? new URL(canonical.href).origin : "https://longevityworldcup.com";
        } catch {
            return "https://longevityworldcup.com";
        }
    }

    function meta(property: string): string {
        return document.querySelector<HTMLMetaElement>(`meta[property="${property}"], meta[name="${property}"]`)?.content.trim() || "";
    }

    // Preview images always load from this site, whatever host the meta tag names.
    function localImage(raw: string): string | null {
        if (!raw) return null;
        try {
            const url = new URL(raw, window.location.href);
            if (!/^https?:$/.test(url.protocol) || url.username || url.password) return null;
            if (!/^\/(og|assets|generated)\//.test(url.pathname)) return null;
            return url.pathname + url.search;
        } catch {
            return null;
        }
    }

    const rankingViews = new Set(["bortz", "pheno", "improvement", "bortz-improvement", "crowd"]);

    function imageForUrl(url: URL): string | null {
        const path = url.pathname;
        const athlete = path.match(/^\/athlete\/([^/]+)$/);
        if (athlete) return `/og/athlete/${athlete[1]}.png`;
        const league = path.match(/^\/league\/([^/]+)$/);
        const view = url.searchParams.get("view") || league?.[1] || "";
        if (rankingViews.has(view)) return `/og/page/view-${view}.png`;
        if (league) return `/og/league/${league[1]}.png`;
        if (path === "/pheno-age" || path === "/bortz-age") return `/og/page${path}.png`;
        if (path === "/leaderboard") return "/og/league/ultimate.png";
        if (path.startsWith("/flag/")) return "/assets/og-image.png";
        if (path === "/") return "/og/page/home.png";
        return null;
    }

    function readPayload(trigger: HTMLElement): LwcSharePayload {
        const data = trigger.dataset;
        const canonical = document.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href;
        const leaderboard = !data["shareUrl"] && !!trigger.closest("#leaderboardTitle");
        const fallback = leaderboard ? window.location.pathname + window.location.search : canonical || window.location.pathname;
        let url: URL;
        try {
            url = new URL(data["shareUrl"] || fallback, canonicalOrigin());
            if (!/^https?:$/.test(url.protocol) || url.username || url.password) throw new Error("Invalid share URL");
        } catch {
            url = new URL(fallback, canonicalOrigin());
        }
        if (url.origin !== canonicalOrigin()) url = new URL(url.pathname + url.search + url.hash, canonicalOrigin());
        if (leaderboard) {
            const meaningful = new Set(["filters", "view", "search", "group", "sort", "dir", "then", "thenDir"]);
            for (const key of [...url.searchParams.keys()]) if (!meaningful.has(key)) url.searchParams.delete(key);
        }

        const title = data["shareTitle"] || (leaderboard ? document.title : meta("og:title")) || document.title;
        const routeImage = imageForUrl(url);
        const metadataImage = localImage(meta("og:image"));
        // Keep the server's signed card when it still represents this selection.
        const image = metadataImage && (!routeImage || metadataImage.split("?")[0] === routeImage) ? metadataImage : routeImage;
        return {
            url: url.toString(),
            title,
            text: data["shareText"] || title,
            image: localImage(data["shareImage"] || "") || image
        };
    }

    // ---- Building the sheet ---------------------------------------------------

    function icon(classes: string): HTMLElement {
        const element = document.createElement("i");
        element.className = classes;
        element.setAttribute("aria-hidden", "true");
        return element;
    }

    // The link glyph and the check share one SVG: the two chain halves slide
    // together and fade while the check draws itself through the same space.
    function morphGlyph(): SVGSVGElement {
        const svg = document.createElementNS(svgNs, "svg");
        svg.setAttribute("viewBox", "0 0 24 24");
        svg.setAttribute("aria-hidden", "true");
        svg.classList.add("lwc-share-glyph");
        const paths: Array<[string, string]> = [
            ["lwc-share-glyph__link lwc-share-glyph__link--a", "M10 14a4.5 4.5 0 0 0 6.4 0l3-3a4.5 4.5 0 0 0-6.4-6.4l-1.2 1.2"],
            ["lwc-share-glyph__link lwc-share-glyph__link--b", "M14 10a4.5 4.5 0 0 0-6.4 0l-3 3a4.5 4.5 0 0 0 6.4 6.4l1.2-1.2"],
            ["lwc-share-glyph__check", "M5 12.5l4.6 4.6L19 7.5"]
        ];
        for (const [className, d] of paths) {
            const path = document.createElementNS(svgNs, "path");
            path.setAttribute("d", d);
            path.setAttribute("class", className);
            path.setAttribute("pathLength", "1");
            svg.appendChild(path);
        }
        return svg;
    }

    function destination(network: string, iconClasses: string, label: string, ariaLabel?: string): HTMLAnchorElement {
        const link = document.createElement("a");
        link.className = "lwc-share__destination";
        link.dataset["network"] = network;
        link.href = "#";
        if (network !== "email") {
            link.target = "_blank";
            link.rel = "noopener";
        }
        if (ariaLabel) link.setAttribute("aria-label", ariaLabel);
        const glyph = document.createElement("span");
        glyph.className = "lwc-share__destination-icon";
        glyph.appendChild(icon(iconClasses));
        link.append(glyph, document.createTextNode(label));
        link.addEventListener("click", () => close(true));
        return link;
    }

    function build(): HTMLElement {
        const root = document.createElement("div");
        root.className = "lwc-share";
        root.setAttribute("role", "dialog");
        root.setAttribute("aria-label", "Share");
        root.hidden = true;

        const handle = document.createElement("span");
        handle.className = "lwc-share__handle";
        handle.setAttribute("aria-hidden", "true");

        const figure = document.createElement("figure");
        figure.className = "lwc-share__preview";
        figure.setAttribute("aria-hidden", "true");
        const glint = document.createElement("span");
        glint.className = "lwc-share__glint";
        figure.appendChild(glint);

        const copy = document.createElement("button");
        copy.type = "button";
        copy.className = "lwc-share__copy";
        const label = document.createElement("span");
        label.className = "lwc-share__copy-label";
        const rest = document.createElement("span");
        rest.textContent = "Copy link";
        const done = document.createElement("span");
        done.textContent = "Copied";
        done.setAttribute("aria-hidden", "true");
        label.append(rest, done);
        copy.append(morphGlyph(), label);
        copy.addEventListener("click", event => {
            event.preventDefault();
            void copyLink();
        });

        const destinations = document.createElement("div");
        destinations.className = "lwc-share__destinations";
        destinations.append(
            destination("x", "fa-brands fa-x-twitter", "Post", "Share on X"),
            destination("facebook", "fa-brands fa-facebook", "Facebook"),
            destination("linkedin", "fa-brands fa-linkedin", "LinkedIn"),
            destination("email", "fa fa-envelope", "Email"));

        const recovery = document.createElement("input");
        recovery.className = "lwc-share__manual";
        recovery.type = "text";
        recovery.readOnly = true;
        recovery.spellcheck = false;
        recovery.setAttribute("aria-label", "Share link");
        recovery.hidden = true;

        const status = document.createElement("span");
        status.className = "visually-hidden";
        status.setAttribute("role", "status");
        status.setAttribute("aria-live", "polite");

        root.append(handle, figure, copy, recovery, destinations, status);
        bindDrag(root, handle);
        document.body.appendChild(root);

        const shade = document.createElement("div");
        shade.className = "lwc-share-scrim";
        shade.hidden = true;
        shade.addEventListener("click", () => close(true));
        document.body.appendChild(shade);
        scrim = shade;
        return root;
    }

    function ensureSheet(): HTMLElement {
        return sheet ??= build();
    }

    // ---- Preview --------------------------------------------------------------

    function loadPreview(root: HTMLElement, image: string | null, openVersion: number): void {
        const figure = root.querySelector<HTMLElement>(".lwc-share__preview");
        if (!figure) return;
        figure.querySelector("img")?.remove();
        figure.classList.remove("is-loaded");
        figure.hidden = !image;
        if (!image) return;

        const img = document.createElement("img");
        img.alt = "";
        img.width = 1200;
        img.height = 630;
        img.decoding = "async";
        img.addEventListener("load", () => {
            if (current?.version !== openVersion) return;
            // A cached card still "develops", one frame after the sheet has painted.
            requestAnimationFrame(() => {
                if (current?.version === openVersion && img.isConnected) figure.classList.add("is-loaded");
            });
        });
        img.addEventListener("error", () => {
            if (current?.version === openVersion) figure.hidden = true;
        });
        figure.insertBefore(img, figure.firstChild);
        img.src = image;
    }

    // ---- Position -------------------------------------------------------------

    function isSheetMode(): boolean {
        return narrow.matches;
    }

    function place(root: HTMLElement, trigger: HTMLElement): void {
        root.classList.toggle("is-sheet", isSheetMode());
        root.setAttribute("aria-modal", String(isSheetMode()));
        if (scrim) scrim.hidden = !isSheetMode();
        syncInert(isSheetMode());
        root.style.removeProperty("left");
        root.style.removeProperty("top");
        if (isSheetMode()) return;

        const gap = 10;
        const pad = 12;
        const anchor = trigger.getBoundingClientRect();
        const width = root.offsetWidth;
        const height = root.offsetHeight;
        const below = anchor.bottom + gap;
        const above = anchor.top - gap - height;
        const fitsBelow = below + height <= window.innerHeight - pad;
        const top = Math.min(Math.max(pad, fitsBelow || above < pad ? below : above), window.innerHeight - height - pad);
        const centre = anchor.left + (anchor.width / 2);
        const left = Math.min(Math.max(centre - (width / 2), pad), window.innerWidth - width - pad);
        root.classList.toggle("is-above", !(fitsBelow || above < pad));
        root.style.left = `${Math.round(left)}px`;
        root.style.top = `${Math.round(Math.max(pad, top))}px`;
        // The sheet grows out of the trigger it came from.
        root.style.setProperty("--lwc-share-origin-x", `${Math.round(centre - left)}px`);
    }

    function syncInert(active: boolean): void {
        if (!active) {
            for (const [element, wasInert] of inertSiblings) element.inert = wasInert;
            inertSiblings.clear();
            return;
        }
        for (const element of document.body.children) {
            if (!(element instanceof HTMLElement) || element === sheet || element === scrim || inertSiblings.has(element)) continue;
            inertSiblings.set(element, element.inert);
            element.inert = true;
        }
    }

    // ---- Open / close ---------------------------------------------------------

    function links(root: HTMLElement, payload: LwcSharePayload): void {
        const url = encodeURIComponent(payload.url);
        const text = encodeURIComponent(payload.text);
        const hrefs: Record<string, string> = {
            x: `https://twitter.com/intent/tweet?text=${text}&url=${url}`,
            facebook: `https://www.facebook.com/sharer/sharer.php?u=${url}`,
            linkedin: `https://www.linkedin.com/sharing/share-offsite/?url=${url}`,
            email: `mailto:?subject=${encodeURIComponent(payload.title)}&body=${encodeURIComponent(`${payload.text}\n\n${payload.url}`)}`
        };
        root.querySelectorAll<HTMLAnchorElement>(".lwc-share__destination").forEach(link => {
            link.href = hrefs[link.dataset["network"] || ""] || "#";
        });
    }

    function wantsNative(payload: LwcSharePayload): boolean {
        const nav = window.navigator;
        if (typeof nav.share !== "function") return false;
        const touch = coarse.matches || (Number(nav.maxTouchPoints) > 0 && /Android|iPhone|iPad|iPod/i.test(nav.userAgent));
        if (!touch) return false;
        const data = { title: payload.title, text: payload.text, url: payload.url };
        try { return typeof nav.canShare !== "function" || nav.canShare(data); }
        catch { return false; }
    }

    async function openNative(trigger: HTMLElement, payload: LwcSharePayload): Promise<void> {
        close(false);
        const request = { trigger, version: ++version };
        pendingNative = request;
        trigger.setAttribute("aria-busy", "true");
        try {
            await window.navigator.share({ title: payload.title, text: payload.text, url: payload.url });
        } catch (error: unknown) {
            if (pendingNative === request && trigger.isConnected && !(error instanceof DOMException && error.name === "AbortError")) openSheet(trigger, payload);
        } finally {
            if (pendingNative === request) {
                pendingNative = null;
                trigger.removeAttribute("aria-busy");
            }
        }
    }

    function openSheet(trigger: HTMLElement, payload: LwcSharePayload): void {
        const root = ensureSheet();
        window.clearTimeout(closeTimer);
        window.clearTimeout(feedbackTimer);
        if (pendingNative) pendingNative.trigger.removeAttribute("aria-busy");
        pendingNative = null;
        current?.trigger.setAttribute("aria-expanded", "false");
        current = { trigger, payload, version: ++version };
        const openVersion = version;
        pendingCopy = false;
        root.querySelector(".lwc-share__copy")?.removeAttribute("aria-busy");
        const status = root.querySelector<HTMLElement>('[role="status"]');
        if (status) status.textContent = "";

        root.classList.remove("is-copied", "is-closing", "is-recovering", "is-dragging");
        scrim?.classList.remove("is-closing");
        root.style.removeProperty("--lwc-share-drag");
        const manual = root.querySelector<HTMLInputElement>(".lwc-share__manual");
        if (manual) manual.hidden = true;
        links(root, payload);
        loadPreview(root, payload.image, version);

        root.hidden = false;
        root.scrollTop = 0;
        if (scrim) scrim.hidden = !isSheetMode();
        place(root, trigger);
        layoutObserver.observe(root);
        trigger.setAttribute("aria-expanded", "true");
        root.querySelector<HTMLButtonElement>(".lwc-share__copy")?.focus({ preventScroll: true });
        requestAnimationFrame(() => {
            if (current?.version === openVersion) place(root, trigger);
        });
    }

    function close(restoreFocus = false): void {
        if (pendingNative) pendingNative.trigger.removeAttribute("aria-busy");
        pendingNative = null;
        if (!sheet || sheet.hidden || !current) return;
        const { trigger } = current;
        const root = sheet;
        current = null;
        layoutObserver.disconnect();
        syncInert(false);
        window.clearTimeout(feedbackTimer);
        trigger.setAttribute("aria-expanded", "false");
        if (restoreFocus && trigger.isConnected) trigger.focus({ preventScroll: true });

        const finish = () => {
            root.hidden = true;
            root.classList.remove("is-closing");
            scrim?.classList.remove("is-closing");
            root.style.removeProperty("--lwc-share-drag");
            root.querySelector(".lwc-share__preview img")?.remove();
            if (scrim) scrim.hidden = true;
        };
        if (reduced.matches) {
            finish();
            return;
        }
        root.classList.add("is-closing");
        scrim?.classList.add("is-closing");
        closeTimer = window.setTimeout(() => {
            scrim?.classList.remove("is-closing");
            finish();
        }, 180);
    }

    // ---- Copy -----------------------------------------------------------------

    function copyText(text: string): Promise<void> {
        if (navigator.clipboard && typeof navigator.clipboard.writeText === "function") {
            return navigator.clipboard.writeText(text);
        }
        return new Promise((resolve, reject) => {
            const focused = document.activeElement;
            const area = document.createElement("textarea");
            area.value = text;
            area.setAttribute("readonly", "");
            area.style.position = "fixed";
            area.style.left = "-9999px";
            document.body.appendChild(area);
            area.select();
            try {
                if (document.execCommand("copy")) resolve();
                else reject(new Error("copy failed"));
            } catch (error: unknown) {
                reject(error);
            } finally {
                area.remove();
                if (focused instanceof HTMLElement && focused.isConnected) focused.focus({ preventScroll: true });
            }
        });
    }

    async function copyLink(): Promise<void> {
        const root = sheet;
        const open = current;
        if (!root || !open || pendingCopy) return;
        window.clearTimeout(feedbackTimer);
        pendingCopy = true;
        const copy = root.querySelector<HTMLButtonElement>(".lwc-share__copy");
        copy?.setAttribute("aria-busy", "true");
        const status = root.querySelector<HTMLElement>('[role="status"]');
        try {
            await copyText(open.payload.url);
            if (current?.version !== open.version) return;
            root.classList.remove("is-copied");
            void root.offsetWidth;
            root.classList.add("is-copied");
            if (status) status.textContent = "Link copied.";
            pulseTrigger(open.trigger);
            // The check holds long enough to be read, then the sheet steps away.
            feedbackTimer = window.setTimeout(() => {
                if (status) status.textContent = "";
                if (current?.version === open.version) close(root.contains(document.activeElement));
            }, feedbackMs);
        } catch {
            if (current?.version !== open.version) return;
            const manual = root.querySelector<HTMLInputElement>(".lwc-share__manual");
            if (!manual) return;
            manual.value = open.payload.url;
            manual.hidden = false;
            root.classList.add("is-recovering");
            place(root, open.trigger);
            manual.focus({ preventScroll: true });
            manual.select();
        } finally {
            if (current?.version === open.version) {
                pendingCopy = false;
                copy?.removeAttribute("aria-busy");
            }
        }
    }

    // The trigger that opened the sheet answers too: its icon turns into a check.
    function pulseTrigger(trigger: HTMLElement): void {
        trigger.classList.add("is-copied");
        const done = trigger.querySelector<HTMLElement>(".lwc-share-trigger__done");
        if (done) window.LwcIcons?.replay(done);
        window.setTimeout(() => trigger.classList.remove("is-copied"), feedbackMs + 900);
    }

    // ---- Keyboard and drag ----------------------------------------------------

    function onKeydown(event: KeyboardEvent): void {
        if (!sheet || !current) return;
        if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            close(true);
            return;
        }
        if (event.key !== "Tab") return;
        const focusable = Array.from(sheet.querySelectorAll<HTMLElement>("button, a[href], input:not([hidden])"))
            .filter(element => element.getClientRects().length > 0);
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (!first || !last) return;
        if (!sheet.contains(document.activeElement)) {
            event.preventDefault();
            first.focus();
        } else if (event.shiftKey && document.activeElement === first) {
            event.preventDefault();
            last.focus();
        } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first.focus();
        }
    }

    // On phones the sheet follows a downward drag and lets go past a third of its height.
    function bindDrag(root: HTMLElement, handle: HTMLElement): void {
        let startY = 0;
        let dragging = false;
        let dragVersion = 0;
        root.addEventListener("pointerdown", event => {
            if (!current || !event.isPrimary || event.button !== 0 || !root.classList.contains("is-sheet") || !handle.contains(event.target as Node)) return;
            dragging = true;
            dragVersion = current.version;
            startY = event.clientY;
            root.classList.add("is-dragging");
            root.setPointerCapture(event.pointerId);
        });
        root.addEventListener("pointermove", event => {
            if (!dragging || current?.version !== dragVersion) return;
            root.style.setProperty("--lwc-share-drag", `${Math.max(0, event.clientY - startY)}px`);
        });
        root.addEventListener("touchmove", event => {
            // A handled drag must finish its native touch sequence too, or the
            // browser can consume the next tap as a request to stop panning.
            if (dragging && current?.version === dragVersion && event.touches.length === 1 && event.cancelable) event.preventDefault();
        }, { passive: false });
        const release = (event: PointerEvent) => {
            if (!dragging) return;
            dragging = false;
            root.classList.remove("is-dragging");
            const travelled = Math.max(0, event.clientY - startY);
            if (root.hasPointerCapture(event.pointerId)) root.releasePointerCapture(event.pointerId);
            if (current?.version === dragVersion && event.type === "pointerup" && travelled > root.offsetHeight / 3) close(true);
            else root.style.removeProperty("--lwc-share-drag");
        };
        root.addEventListener("pointerup", release);
        root.addEventListener("pointercancel", release);
    }

    // ---- Entry points ---------------------------------------------------------

    function open(trigger: HTMLElement): void {
        if (pendingNative?.trigger === trigger) return;
        if (current?.trigger === trigger && sheet && !sheet.hidden) {
            close(true);
            return;
        }
        const payload = readPayload(trigger);
        if (wantsNative(payload)) {
            void openNative(trigger, payload);
            return;
        }
        openSheet(trigger, payload);
    }

    function mount(host: Element, position: InsertPosition = "beforeend"): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "lwc-share-trigger";
        button.setAttribute("data-lwc-share", "");
        button.setAttribute("aria-label", "Share");
        button.setAttribute("aria-haspopup", "dialog");
        button.setAttribute("aria-expanded", "false");
        button.append(icon("fa fa-share-nodes lwc-share-trigger__share"), icon("fa fa-check lwc-share-trigger__done"));
        host.insertAdjacentElement(position, button);
        return button;
    }

    document.addEventListener("click", event => {
        const target = event.target instanceof Element ? event.target : null;
        const trigger = target?.closest<HTMLElement>("[data-lwc-share]");
        if (trigger) {
            event.preventDefault();
            open(trigger);
            return;
        }
        if (sheet && !sheet.hidden && target && !sheet.contains(target)) {
            close(sheet.contains(document.activeElement) || document.activeElement === document.body);
        }
    });
    document.addEventListener("keydown", onKeydown, true);
    window.addEventListener("popstate", () => close(false));
    reduced.addEventListener("change", () => {
        if (reduced.matches && sheet?.classList.contains("is-closing")) {
            window.clearTimeout(closeTimer);
            sheet.hidden = true;
            sheet.classList.remove("is-closing");
            if (scrim) { scrim.hidden = true; scrim.classList.remove("is-closing"); }
        }
    });

    window.addEventListener("resize", () => {
        if (sheet && !sheet.hidden && current) {
            if (scrim) scrim.hidden = !isSheetMode();
            place(sheet, current.trigger);
        }
    });
    window.addEventListener("scroll", () => {
        if (sheet && !sheet.hidden && current && !isSheetMode()) place(sheet, current.trigger);
    }, { passive: true });

    // Result pages share what they show: the leaderboard, a league or a flag.
    function mountPageEntries(): void {
        const title = document.getElementById("leaderboardTitle");
        if (title && !title.querySelector("[data-lwc-share]")) {
            title.classList.add("lwc-share-host");
            mount(title);
        }
    }

    window.LwcShare = { open, close, mount };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", mountPageEntries, { once: true });
    else mountPageEntries();
})();
