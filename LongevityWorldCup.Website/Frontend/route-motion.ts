// A visual handover between a page and an athlete opened from it.
//
// Same-site page changes keep the site's plain dissolve (aesthetic-system.css).
// Only one real navigation relationship gets direction:
// - Into an athlete: a page that shows an athlete (a Highlights row, a history
//   card...) opens /athlete/<slug>. The portrait the visitor tapped grows into
//   the profile's portrait while the page behind recedes.
// - Back to that page: browser back (or forward again), or a link to exactly
//   that page, from the athlete it opened. The profile's portrait settles back
//   into the same athlete's portrait on the page, which comes forward again.
// Nothing is inferred from a route list or from URL depth: the relationship is
// recorded when the athlete is opened and checked when the visitor leaves it.
//
// The handover only plays when its target portrait is ready. The arriving page
// waits at most holdCapMs past its site header for it; a late portrait (a slow
// page, or a list that draws its portraits after load) lets the page arrive
// with its usual motion and no portrait flight. Seeing the page comes first.
//
// This classic script loads from the shared head, so it is listening before
// the first frame of the arriving page. route-motion.css owns the look.
// Reduced motion keeps the plain page change (the site turns cross-document
// transitions off). Nothing here adds words.

(() => {
    const reduced = typeof window.matchMedia === "function" ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
    const storeKey = "lwc-route-handover";
    const handoverName = "lwc-athlete-handover";
    const maxAgeMs = 30 * 60 * 1000;
    // The longest the arriving page's first frame waits for the portrait,
    // counted from its site header (when the page could first be drawn anyway).
    const holdCapMs = 150;

    type Record = { readonly origin: string; readonly slug: string; readonly at: number; readonly scrollY?: number; readonly vw?: number };
    type Activation = { readonly entry?: { readonly url?: string } | null; readonly from?: { readonly url?: string } | null; readonly navigationType?: string };
    type SwapEvent = Event & { readonly viewTransition?: ViewTransition | null; readonly activation?: Activation | null };

    const pageKey = (url: URL): string => `${url.pathname.replace(/\/+$/, "") || "/"}${url.search}`.toLowerCase();
    const slugOf = (url: URL): string | null => {
        const match = /^\/athlete\/([^/?#]+)\/?$/i.exec(url.pathname);
        return match ? decodeURIComponent(match[1]!).toLowerCase() : null;
    };
    const typesOf = (transition: ViewTransition): Set<string> | null =>
        (transition as ViewTransition & { types?: Set<string> }).types ?? null;
    const reducedMotion = (): boolean => reduced?.matches === true;

    function read(): Record | null {
        try {
            const value = JSON.parse(sessionStorage.getItem(storeKey) || "null") as Record | null;
            return value && Date.now() - value.at < maxAgeMs ? value : null;
        } catch { return null; }
    }
    function write(record: Record): void {
        try { sessionStorage.setItem(storeKey, JSON.stringify(record)); } catch { /* storage unavailable: no handover */ }
    }

    const inView = (el: Element): boolean => {
        const box = el.getBoundingClientRect();
        return box.width > 0 && box.height > 0 && box.bottom > 0 && box.top < innerHeight && box.right > 0 && box.left < innerWidth;
    };
    // Ready to hand over: laid out on screen with its picture drawn.
    const ready = (img: HTMLImageElement): boolean => inView(img) && img.complete && img.naturalWidth > 0;

    // The athlete's portrait on a page that lists them, nearest the last press.
    let lastPress: Element | null = null;
    window.addEventListener("pointerdown", event => { lastPress = event.target instanceof Element ? event.target : null; }, { capture: true, passive: true });
    window.addEventListener("keydown", event => { if (event.key === "Enter") lastPress = document.activeElement; }, { capture: true, passive: true });

    function listPortrait(slug: string): HTMLImageElement | null {
        const escaped = CSS.escape(slug);
        const selectors = [`[data-athlete-slug="${escaped}" i] img`, `a[href$="/athlete/${escaped}" i] img`];
        const pressedRow = lastPress?.closest("tr, li, article, [data-athlete-slug]") ?? null;
        for (const scope of [pressedRow, document]) {
            if (!scope) continue;
            for (const selector of selectors) {
                const found = Array.from(scope.querySelectorAll<HTMLImageElement>(selector)).find(ready);
                if (found) return found;
            }
        }
        return null;
    }

    // The profile's portrait on an athlete page, with its PRO tag and flag, so
    // the moving portrait never covers them.
    function profilePortrait(): HTMLElement | null {
        const pic = document.querySelector<HTMLImageElement>("#detailsModal #modalProfilePic");
        if (!pic || !ready(pic)) return null;
        return pic.closest<HTMLElement>(".portrait-wrapper") ?? pic;
    }

    function name(el: HTMLElement | null, transition: ViewTransition): void {
        if (!el) return;
        el.style.viewTransitionName = handoverName;
        const clear = (): void => { el.style.viewTransitionName = ""; };
        transition.finished.then(clear, clear);
    }

    function claim(transition: ViewTransition, type: string): boolean {
        const types = typesOf(transition);
        if (!types) return false;
        try { types.add(type); return true; } catch { return false; }
    }

    // The leaving page claimed the handover; without a target here it is
    // dropped, so this page arrives with its usual motion instead.
    function drop(transition: ViewTransition, type: string): void {
        try { typesOf(transition)?.delete(type); } catch { /* fixed types: the page still arrives */ }
    }

    // Leaving a page.
    window.addEventListener("pageswap", raw => {
        const event = raw as SwapEvent;
        const transition = event.viewTransition;
        const target = event.activation?.entry?.url;
        // A page left while still loading may drop its transition before
        // site-header.js (which observes the rest) has run.
        transition?.ready.catch(() => {});
        if (!transition || !target || reducedMotion()) return;
        const destination = new URL(target);
        if (destination.origin !== location.origin) return;
        const here = new URL(location.href);
        const opening = slugOf(destination);
        const current = slugOf(here);

        if (opening && !current) {
            // Into an athlete: remember where they were opened from, and the
            // scroll there, for the way back.
            write({ origin: pageKey(here), slug: opening, at: Date.now(), scrollY, vw: innerWidth });
            if (claim(transition, "lwc-athlete-into")) name(listPortrait(opening), transition);
            return;
        }

        const record = read();
        // A profile still loading is busy enough that the browser can drop the
        // transition at the last moment; leave it to the plain page change.
        if (current && record && record.slug === current && pageKey(destination) === record.origin && document.readyState === "complete") {
            // Back to the page this athlete was opened from. The type also tells
            // site-header.js to keep this transition from a journey-styled page.
            if (claim(transition, "lwc-athlete-return")) name(profilePortrait(), transition);
        }
    });

    // Arriving at a page.
    window.addEventListener("pagereveal", raw => {
        const event = raw as SwapEvent;
        const transition = event.viewTransition;
        const activation = (window as Window & { navigation?: { activation?: Activation } }).navigation?.activation;
        const fromUrl = activation?.from?.url;
        if (!transition || !fromUrl || reducedMotion() || activation?.navigationType === "reload") return;
        const from = new URL(fromUrl);
        if (from.origin !== location.origin) return;
        const record = read();
        if (!record) return;
        const here = new URL(location.href);
        const current = slugOf(here);
        const leaving = slugOf(from);

        if (current && current === record.slug && pageKey(from) === record.origin) {
            const portrait = profilePortrait();
            if (portrait && claim(transition, "lwc-athlete-into")) name(portrait, transition);
            else drop(transition, "lwc-athlete-into");
            return;
        }
        if (!current && leaving === record.slug && pageKey(here) === record.origin) {
            // Browser back restores this scroll anyway, but only after the first
            // frame; restoring it now keeps the page from jumping mid-handover.
            // A page still being parsed may be too short yet, so leave it then.
            const traverse = activation?.navigationType === "traverse";
            if (traverse && document.readyState !== "loading" && typeof record.scrollY === "number" && record.vw === innerWidth) {
                window.scrollTo({ top: record.scrollY, left: 0, behavior: "instant" });
            }
            // On back or forward, land only once the page is at that scroll, so
            // the portrait never lands on a spot that is about to scroll away.
            const settled = !traverse || (typeof record.scrollY === "number" && Math.abs(scrollY - record.scrollY) <= 4);
            const portrait = settled ? listPortrait(record.slug) : null;
            if (portrait && claim(transition, "lwc-athlete-return")) name(portrait, transition);
            else drop(transition, "lwc-athlete-return");
        }
    });

    // Pages are revealed before they finish loading. For a handover, hold the
    // first frame for somewhere to land: the profile's portrait (just before
    // #athleteName) on the way in, the page's content (up to its footer) on the
    // way back. The hold ends holdCapMs after the site header at the latest.
    (() => {
        const activation = (window as Window & { navigation?: { activation?: Activation } }).navigation?.activation;
        const fromUrl = activation?.from?.url;
        if (!fromUrl || !("onpagereveal" in window) || reducedMotion() || activation?.navigationType === "reload") return;
        // No transition is coming (the leaving page let it go): nothing to wait for.
        if ("activeViewTransition" in document && !document.activeViewTransition) return;
        const record = read();
        if (!record) return;
        const from = new URL(fromUrl);
        if (from.origin !== location.origin) return;
        const here = new URL(location.href);
        const current = slugOf(here);
        let until = "";
        if (current && current === record.slug && pageKey(from) === record.origin) until = "#athleteName";
        else if (!current && slugOf(from) === record.slug && pageKey(here) === record.origin) until = "#lwc-site-footer";
        if (!until) return;
        const expect = document.createElement("link");
        expect.rel = "expect";
        expect.href = until;
        expect.setAttribute("blocking", "render");
        document.head.appendChild(expect);

        const release = (): void => expect.remove();
        window.addEventListener("pagereveal", release, { once: true });
        const startCap = (): void => { window.setTimeout(release, holdCapMs); };
        if (typeof MutationObserver !== "function") { startCap(); return; }
        const header = new MutationObserver(() => {
            if (!document.getElementById("lwc-site-header")) return;
            header.disconnect();
            startCap();
        });
        header.observe(document.documentElement, { childList: true, subtree: true });
        // A page without the site header is released when parsing ends.
        document.addEventListener("DOMContentLoaded", () => { header.disconnect(); release(); }, { once: true });
    })();
})();
