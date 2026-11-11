// Route-to-route page changes with a sense of place.
//
// Same-site navigations already cross-dissolve (aesthetic-system.css). This
// classic script, loaded from the shared head so it is listening before the
// first frame, names the kind of move for the arriving page:
// - lwc-forward / lwc-back: pages in the site's order slide sideways under the
//   header, the way a broadcast cuts between cameras.
// - lwc-deeper / lwc-shallower: opening an athlete (or any deeper path) zooms
//   in; coming back out zooms out.
// A cyan sweep runs under the still header in the direction of travel.
//
// route-motion.css owns the look. The entry journey and the home board's
// handoff to the full leaderboard keep their own choreography. Reduced motion
// keeps the plain page change. Nothing here adds words.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    const journey = /^\/(?:play|join|select-athlete|dashboard|pheno-age|bortz-age|apply|review)?$/i;

    // The order a visitor meets the site in: the board, what's happening, the
    // side events, then the reading pages.
    const order = [
        /^\/leaderboard$/,
        /^\/events$/,
        /^\/rejuvenation-olympics$/,
        /^\/longevitymaxxing$/,
        /^\/about$/,
        /^\/ruleset$/,
        /^\/history$/,
        /^\/privacy(?:-policy)?$/,
    ];

    const clean = (path: string): string => (path.replace(/\/+$/, "") || "/").toLowerCase();
    const depth = (path: string): number => clean(path) === "/" ? 0 : clean(path).split("/").length - 1;
    const place = (path: string): number => order.findIndex(pattern => pattern.test(clean(path)));

    type Move = "lwc-forward" | "lwc-back" | "lwc-deeper" | "lwc-shallower";
    function classify(from: string, to: string): Move | null {
        if (clean(from) === clean(to)) return null;
        const a = place(from);
        const b = place(to);
        if (a >= 0 && b >= 0) return b > a ? "lwc-forward" : "lwc-back";
        const da = depth(from);
        const db = depth(to);
        if (db > da) return "lwc-deeper";
        if (db < da) return "lwc-shallower";
        return null;
    }

    window.addEventListener("pagereveal", event => {
        const transition = (event as Event & { viewTransition?: ViewTransition | null }).viewTransition;
        const activation = (window as Window & { navigation?: { activation?: { from?: { url?: string } | null } } }).navigation?.activation;
        const fromUrl = activation?.from?.url;
        if (!transition || !fromUrl || reduced.matches) return;
        const from = new URL(fromUrl);
        if (from.origin !== location.origin) return;
        // The entry journey (home, play, join, the calculators, apply) and the home
        // board's handoff have their own choreography; same routes as site-header.js.
        if (journey.test(clean(from.pathname)) || journey.test(clean(location.pathname))) return;

        const move = classify(from.pathname, location.pathname);
        if (!move) return;
        const types = (transition as ViewTransition & { types?: Set<string> }).types;
        if (!types) return;
        try { types.add(move); } catch { return; }

        // The sweep is its own layer, so it runs above both pages under the header.
        if (!document.body) return;
        const header = document.querySelector<HTMLElement>('header[role="banner"]');
        const top = header ? Math.max(0, Math.min(header.getBoundingClientRect().bottom, 140)) : 0;
        const sweep = document.createElement("div");
        sweep.className = "lwc-route-sweep";
        sweep.setAttribute("aria-hidden", "true");
        sweep.style.top = `${top}px`;
        document.body.appendChild(sweep);
        const clear = (): void => sweep.remove();
        transition.finished.then(clear, clear);
    });
})();
