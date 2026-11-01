// Homepage merch as a shelf: the feel layer.
//
// merch-shelf.css owns the look; this classic script only measures:
// - Desktop: a product card leans toward the pointer (a few degrees of 3D tilt)
//   and a soft sheen follows it, like turning a product in the light.
// - Phone: the swipe strip gains depth. Each slide's distance from the snap
//   point (--shelf-d, -1..1) scales, dims and parallaxes it, so swiping feels
//   like turning a shelf rather than sliding a strip.
//
// homepage.js keeps the carousel's behaviour (dots, snapping, focus). Nothing
// here changes wording; reduced motion drops the tilt and depth.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)");

    function start(): void {
        const body = document.querySelector<HTMLElement>(".lwc-merch-body");
        if (!body) return;
        body.dataset.shelf = "";

        // ------------------------------------------------ desktop tilt + sheen
        body.querySelectorAll<HTMLElement>(".lwc-merch-item").forEach(item => {
            let frame = 0;
            item.addEventListener("pointermove", event => {
                if (event.pointerType !== "mouse" || reduced.matches || !finePointer.matches) return;
                if (frame) return;
                frame = requestAnimationFrame(() => {
                    frame = 0;
                    const r = item.getBoundingClientRect();
                    const px = Math.min(1, Math.max(0, (event.clientX - r.left) / r.width));
                    const py = Math.min(1, Math.max(0, (event.clientY - r.top) / r.height));
                    item.style.setProperty("--shelf-rx", `${((0.5 - py) * 9).toFixed(2)}deg`);
                    item.style.setProperty("--shelf-ry", `${((px - 0.5) * 11).toFixed(2)}deg`);
                    item.style.setProperty("--shelf-mx", `${(px * 100).toFixed(1)}%`);
                    item.style.setProperty("--shelf-my", `${(py * 100).toFixed(1)}%`);
                    item.dataset.shelfLean = "";
                });
            });
            item.addEventListener("pointerleave", () => {
                if (frame) cancelAnimationFrame(frame);
                frame = 0;
                delete item.dataset.shelfLean;
                ["--shelf-rx", "--shelf-ry"].forEach(name => item.style.removeProperty(name));
            });
        });

        // ------------------------------------------------ phone depth
        const track = body.querySelector<HTMLElement>(".lwc-merch-mobile-track");
        const slides = track ? Array.from(track.querySelectorAll<HTMLElement>(".lwc-merch-mobile-slide")) : [];
        if (!track || !slides.length) return;
        let queued = false;
        function depth(): void {
            queued = false;
            const maxLeft = track!.scrollWidth - track!.clientWidth;
            slides.forEach(slide => {
                const snap = Math.min(slide.offsetLeft, maxLeft);
                const width = slide.offsetWidth || 1;
                const d = Math.max(-1, Math.min(1, (snap - track!.scrollLeft) / width));
                slide.style.setProperty("--shelf-d", d.toFixed(3));
                slide.style.setProperty("--shelf-ad", Math.abs(d).toFixed(3));
            });
        }
        const schedule = (): void => { if (!queued) { queued = true; requestAnimationFrame(depth); } };
        track.addEventListener("scroll", schedule, { passive: true });
        new ResizeObserver(schedule).observe(track);
        depth();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
