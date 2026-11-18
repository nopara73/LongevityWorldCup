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
// This classic script loads from the shared head, so it is listening before
// the first frame of the arriving page. route-motion.css owns the look.
// Reduced motion keeps the plain page change (the site turns cross-document
// transitions off). Nothing here adds words.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const storeKey = "lwc-route-handover";
    const handoverName = "lwc-athlete-handover";
    const maxAgeMs = 30 * 60 * 1000;

    type Box = { readonly x: number; readonly y: number; readonly w: number; readonly h: number };
    type Record = { readonly origin: string; readonly slug: string; readonly at: number; readonly from?: Box | null; readonly scrollY?: number; readonly vw?: number };
    type Activation = { readonly entry?: { readonly url?: string } | null; readonly from?: { readonly url?: string } | null; readonly navigationType?: string };
    type SwapEvent = Event & { readonly viewTransition?: ViewTransition | null; readonly activation?: Activation | null };

    const pageKey = (url: URL): string => `${url.pathname.replace(/\/+$/, "") || "/"}${url.search}`.toLowerCase();
    const slugOf = (url: URL): string | null => {
        const match = /^\/athlete\/([^/?#]+)\/?$/i.exec(url.pathname);
        return match ? decodeURIComponent(match[1]!).toLowerCase() : null;
    };
    const typesOf = (transition: ViewTransition): Set<string> | null =>
        (transition as ViewTransition & { types?: Set<string> }).types ?? null;

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

    // The athlete's portrait on a page that lists them, nearest the last press.
    let lastPress: Element | null = null;
    window.addEventListener("pointerdown", event => { lastPress = event.target instanceof Element ? event.target : null; }, { capture: true, passive: true });
    window.addEventListener("keydown", event => { if (event.key === "Enter") lastPress = document.activeElement; }, { capture: true, passive: true });

    function listPortrait(slug: string): HTMLElement | null {
        const escaped = CSS.escape(slug);
        const selectors = [`[data-athlete-slug="${escaped}" i] img`, `a[href$="/athlete/${escaped}" i] img`];
        const pressedRow = lastPress?.closest("tr, li, article, [data-athlete-slug]") ?? null;
        for (const scope of [pressedRow, document]) {
            if (!scope) continue;
            for (const selector of selectors) {
                const found = Array.from(scope.querySelectorAll<HTMLElement>(selector)).find(inView);
                if (found) return found;
            }
        }
        return null;
    }

    // The profile's portrait on an athlete page, with its PRO tag and flag, so
    // the moving portrait never covers them.
    function profilePortrait(): HTMLElement | null {
        const pic = document.querySelector<HTMLElement>("#detailsModal #modalProfilePic");
        if (!pic || !inView(pic)) return null;
        return pic.closest<HTMLElement>(".portrait-wrapper") ?? pic;
    }

    // Lists that draw their portraits after load (the RO landing) have none yet
    // when the page is revealed. The portrait then lands on a pad where it left
    // from, when the page is back at the same scroll and width.
    function landingPad(record: Record): HTMLElement | null {
        const box = record.from;
        if (!box || record.vw !== innerWidth || Math.abs((record.scrollY ?? -1) - scrollY) > 4) return null;
        const pad = document.createElement("div");
        pad.setAttribute("aria-hidden", "true");
        pad.style.cssText = `position:fixed;left:${box.x}px;top:${box.y}px;width:${box.w}px;height:${box.h}px;border-radius:50%;pointer-events:none;`;
        (document.body ?? document.documentElement).appendChild(pad);
        return pad;
    }

    function name(el: HTMLElement | null, transition: ViewTransition, remove = false): void {
        if (!el) return;
        el.style.viewTransitionName = handoverName;
        const clear = (): void => { if (remove) el.remove(); else el.style.viewTransitionName = ""; };
        transition.finished.then(clear, clear);
    }

    function claim(transition: ViewTransition, type: string): boolean {
        const types = typesOf(transition);
        if (!types) return false;
        try { types.add(type); return true; } catch { return false; }
    }

    // Leaving a page.
    window.addEventListener("pageswap", raw => {
        const event = raw as SwapEvent;
        const transition = event.viewTransition;
        const target = event.activation?.entry?.url;
        if (!transition || !target || reduced.matches) return;
        const destination = new URL(target);
        if (destination.origin !== location.origin) return;
        const here = new URL(location.href);
        const opening = slugOf(destination);
        const current = slugOf(here);

        if (opening && !current) {
            // Into an athlete: remember where they were opened from, and where
            // the tapped portrait sat, for the way back.
            const portrait = listPortrait(opening);
            const box = portrait?.getBoundingClientRect();
            write({
                origin: pageKey(here), slug: opening, at: Date.now(),
                from: box ? { x: box.left, y: box.top, w: box.width, h: box.height } : null,
                scrollY, vw: innerWidth
            });
            if (claim(transition, "lwc-athlete-into")) name(portrait, transition);
            return;
        }

        const record = read();
        if (current && record && record.slug === current && pageKey(destination) === record.origin) {
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
        if (!transition || !fromUrl || reduced.matches || activation?.navigationType === "reload") return;
        const from = new URL(fromUrl);
        if (from.origin !== location.origin) return;
        const record = read();
        if (!record) return;
        const here = new URL(location.href);
        const current = slugOf(here);
        const leaving = slugOf(from);

        if (current && current === record.slug && pageKey(from) === record.origin) {
            if (claim(transition, "lwc-athlete-into")) name(profilePortrait(), transition);
            return;
        }
        if (!current && leaving === record.slug && pageKey(here) === record.origin) {
            if (!claim(transition, "lwc-athlete-return")) return;
            // Browser back restores this scroll anyway, but only after the first
            // frame; restoring it now keeps the page from jumping mid-handover.
            if (activation?.navigationType === "traverse" && typeof record.scrollY === "number" && record.vw === innerWidth) {
                window.scrollTo({ top: record.scrollY, left: 0, behavior: "instant" });
            }
            const portrait = listPortrait(record.slug);
            if (portrait) name(portrait, transition);
            else name(landingPad(record), transition, true);
        }
    });

    // Pages are revealed before they finish loading. For a handover, hold the
    // first frame until there is somewhere to land: the profile's portrait
    // (just before #athleteName) on the way in, the whole page on the way back
    // (an id that never appears holds until parsing ends).
    (() => {
        const activation = (window as Window & { navigation?: { activation?: Activation } }).navigation?.activation;
        const fromUrl = activation?.from?.url;
        const record = read();
        if (!fromUrl || !record || reduced.matches || activation?.navigationType === "reload") return;
        const from = new URL(fromUrl);
        if (from.origin !== location.origin) return;
        const here = new URL(location.href);
        const current = slugOf(here);
        let until = "";
        if (current && current === record.slug && pageKey(from) === record.origin) until = "#athleteName";
        else if (!current && slugOf(from) === record.slug && pageKey(here) === record.origin) until = "#lwc-route-handover-parsed";
        if (!until) return;
        const expect = document.createElement("link");
        expect.rel = "expect";
        expect.href = until;
        expect.setAttribute("blocking", "render");
        document.head.appendChild(expect);
    })();
})();
