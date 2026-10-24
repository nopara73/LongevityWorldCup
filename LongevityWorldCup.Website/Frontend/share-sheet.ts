// One share sheet for every place an athlete, a result or the site can be shared.
//
// Any element marked [data-lwc-share] opens it. The element may name what to share
// with data-share-url, data-share-title, data-share-text and data-share-image; anything
// left out comes from the page (canonical link, og:title, og:image). Phones with a
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
            if (!/^\/(og|assets|generated)\//.test(url.pathname)) return null;
            return url.pathname + url.search;
        } catch {
            return null;
        }
    }

    function imageForPath(path: string): string | null {
        const athlete = path.match(/^\/athlete\/([^/]+)$/);
        if (athlete) return `/og/athlete/${athlete[1]}.png`;
        const league = path.match(/^\/league\/([^/]+)$/);
        if (league) return `/og/league/${league[1]}.png`;
        return null;
    }

    function readPayload(trigger: HTMLElement): LwcSharePayload {
        const data = trigger.dataset;
        const canonical = document.querySelector<HTMLLinkElement>('link[rel="canonical"]')?.href;
        let url: URL;
        try {
            url = new URL(data["shareUrl"] || canonical || window.location.pathname + window.location.search, canonicalOrigin());
        } catch {
            url = new URL(window.location.pathname, canonicalOrigin());
        }
        if (url.origin !== canonicalOrigin()) url = new URL(url.pathname + url.search + url.hash, canonicalOrigin());

        const title = data["shareTitle"] || meta("og:title") || document.title;
        return {
            url: url.toString(),
            title,
            text: data["shareText"] || title,
            image: localImage(data["shareImage"] || "") || imageForPath(url.pathname) || localImage(meta("og:image"))
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
        link.addEventListener("click", () => close(false));
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
        recovery.setAttribute("aria-label", "Profile link");
        recovery.hidden = true;

        const status = document.createElement("span");
        status.className = "visually-hidden";
        status.setAttribute("role", "status");
        status.setAttribute("aria-live", "polite");

        root.append(handle, figure, copy, recovery, destinations, status);
        root.addEventListener("keydown", onKeydown);
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
            requestAnimationFrame(() => figure.classList.add("is-loaded"));
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
        const top = fitsBelow || above < pad ? below : above;
        const centre = anchor.left + (anchor.width / 2);
        const left = Math.min(Math.max(centre - (width / 2), pad), window.innerWidth - width - pad);
        root.classList.toggle("is-above", !(fitsBelow || above < pad));
        root.style.left = `${Math.round(left)}px`;
        root.style.top = `${Math.round(Math.max(pad, top))}px`;
        // The sheet grows out of the trigger it came from.
        root.style.setProperty("--lwc-share-origin-x", `${Math.round(centre - left)}px`);
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
        return typeof nav.canShare !== "function" || nav.canShare(data);
    }

    async function openNative(trigger: HTMLElement, payload: LwcSharePayload): Promise<void> {
        trigger.setAttribute("aria-busy", "true");
        try {
            await window.navigator.share({ title: payload.title, text: payload.text, url: payload.url });
        } catch (error: unknown) {
            if (!(error instanceof DOMException && error.name === "AbortError")) openSheet(trigger, payload);
        } finally {
            trigger.removeAttribute("aria-busy");
        }
    }

    function openSheet(trigger: HTMLElement, payload: LwcSharePayload): void {
        const root = ensureSheet();
        window.clearTimeout(closeTimer);
        window.clearTimeout(feedbackTimer);
        current?.trigger.setAttribute("aria-expanded", "false");
        current = { trigger, payload, version: ++version };

        root.classList.remove("is-copied", "is-closing", "is-recovering");
        root.style.removeProperty("--lwc-share-drag");
        const manual = root.querySelector<HTMLInputElement>(".lwc-share__manual");
        if (manual) manual.hidden = true;
        links(root, payload);
        loadPreview(root, payload.image, version);

        root.hidden = false;
        if (scrim) scrim.hidden = !isSheetMode();
        place(root, trigger);
        trigger.setAttribute("aria-expanded", "true");
        root.querySelector<HTMLButtonElement>(".lwc-share__copy")?.focus({ preventScroll: true });
        requestAnimationFrame(() => {
            if (current?.trigger === trigger) place(root, trigger);
        });
    }

    function close(restoreFocus = false): void {
        if (!sheet || sheet.hidden || !current) return;
        const { trigger } = current;
        const root = sheet;
        current = null;
        window.clearTimeout(feedbackTimer);
        trigger.setAttribute("aria-expanded", "false");
        if (restoreFocus && trigger.isConnected) trigger.focus({ preventScroll: true });

        const finish = () => {
            root.hidden = true;
            root.classList.remove("is-closing");
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
            }
        });
    }

    async function copyLink(): Promise<void> {
        const root = sheet;
        const open = current;
        if (!root || !open) return;
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
            manual.focus({ preventScroll: true });
            manual.select();
        }
    }

    // The trigger that opened the sheet answers too: its icon turns into a check.
    function pulseTrigger(trigger: HTMLElement): void {
        trigger.classList.remove("is-copied");
        void trigger.offsetWidth;
        trigger.classList.add("is-copied");
        window.setTimeout(() => trigger.classList.remove("is-copied"), feedbackMs + 900);
    }

    // ---- Keyboard and drag ----------------------------------------------------

    function onKeydown(event: KeyboardEvent): void {
        if (!sheet) return;
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
        if (event.shiftKey && document.activeElement === first) {
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
        const surface = [handle, root.querySelector(".lwc-share__preview")];
        root.addEventListener("pointerdown", event => {
            if (!root.classList.contains("is-sheet") || !surface.some(element => element?.contains(event.target as Node))) return;
            dragging = true;
            startY = event.clientY;
            root.classList.add("is-dragging");
            root.setPointerCapture(event.pointerId);
        });
        root.addEventListener("pointermove", event => {
            if (!dragging) return;
            root.style.setProperty("--lwc-share-drag", `${Math.max(0, event.clientY - startY)}px`);
        });
        const release = (event: PointerEvent) => {
            if (!dragging) return;
            dragging = false;
            root.classList.remove("is-dragging");
            const travelled = Math.max(0, event.clientY - startY);
            if (travelled > root.offsetHeight / 3) close(true);
            else root.style.removeProperty("--lwc-share-drag");
        };
        root.addEventListener("pointerup", release);
        root.addEventListener("pointercancel", release);
    }

    // ---- Entry points ---------------------------------------------------------

    function open(trigger: HTMLElement): void {
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
        if (sheet && !sheet.hidden && target && !sheet.contains(target)) close(false);
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
