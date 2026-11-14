// Homepage hero as a stage: the feel layer.
//
// hero-stage.css owns the look; this classic script only measures and times:
// - The "longevity leaderboards" line becomes the podium card's own header
//   band, so it is sized to the podium's exact edges (it lives outside the
//   podium's container in the markup).
// - When the podium first lands, a spotlight sweeps in from the side and
//   settles on the champion (once per visit, after homepage.js's count-up
//   starts; the podium itself is untouched).
// - On desktop the three athletes sit at slightly different depths and drift
//   a few pixels against the pointer.
//
// Nothing here changes wording or the podium's own behaviour. Reduced motion
// keeps the band and the stage light, and drops the sweep and the drift.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)");

    function start(): void {
        const main = document.querySelector<HTMLElement>("main.homepage-main");
        const kicker = main?.querySelector<HTMLElement>(":scope > .game-description");
        if (!main || !kicker) return;

        let podium: HTMLElement | null = null;
        let watched: HTMLElement | null = null;
        // The podium settles its width after hydration and images, so it is
        // watched directly rather than through the page.
        const podiumObserver = new ResizeObserver(() => schedule());
        function fit(): void {
            podium = main!.querySelector<HTMLElement>(".podium");
            if (podium && podium !== watched) {
                if (watched) podiumObserver.unobserve(watched);
                podiumObserver.observe(podium);
                watched = podium;
            }
            if (!podium || !podium.getClientRects().length) { main!.removeAttribute("data-hero-stage"); return; }
            // Measure from where the band sits without its own offset, so the
            // parent's padding is accounted for.
            const r = podium.getBoundingClientRect();
            const current = kicker!.getBoundingClientRect().left - (parseFloat(getComputedStyle(kicker!).marginLeft) || 0);
            kicker!.style.setProperty("--hero-left", `${r.left - current}px`);
            kicker!.style.setProperty("--hero-width", `${r.width}px`);
            main!.dataset.heroStage = "";
        }

        // The spotlight sweeps in once the podium has landed.
        let lit = false;
        function light(): void {
            if (lit || !podium) return;
            if (!podium.querySelector(".podium-item.first")) return;
            lit = true;
            podium.classList.add("hero-lit");
            if (!reduced.matches) podium.classList.add("hero-sweep");
        }

        let queued = false;
        function schedule(): void {
            if (queued) return;
            queued = true;
            requestAnimationFrame(() => { queued = false; fit(); light(); });
        }
        new ResizeObserver(schedule).observe(main);
        new MutationObserver(schedule).observe(main, { childList: true, subtree: false });
        const content = main.querySelector(".content");
        if (content) new MutationObserver(schedule).observe(content, { childList: true });
        schedule();

        // Desktop depth: the athletes drift a few pixels against the pointer.
        let frame = 0;
        main.addEventListener("pointermove", event => {
            if (!podium || event.pointerType !== "mouse" || reduced.matches || !finePointer.matches) return;
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                const r = podium!.getBoundingClientRect();
                if (event.clientY < r.top - 80 || event.clientY > r.bottom + 40) return;
                const px = ((event.clientX - r.left) / r.width - 0.5) * 2;
                const py = ((event.clientY - r.top) / r.height - 0.5) * 2;
                podium!.style.setProperty("--hero-px", Math.max(-1, Math.min(1, px)).toFixed(3));
                podium!.style.setProperty("--hero-py", Math.max(-1, Math.min(1, py)).toFixed(3));
            });
        });
        main.addEventListener("pointerleave", () => {
            podium?.style.setProperty("--hero-px", "0");
            podium?.style.setProperty("--hero-py", "0");
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
