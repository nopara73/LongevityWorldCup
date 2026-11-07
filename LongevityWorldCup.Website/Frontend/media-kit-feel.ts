// Media kit as a press room: the feel layer.
//
// media-kit-feel.css owns the look; this classic script only adds:
// - Taking the whole kit (the ZIP link) sends one wave across every tile,
//   folder by folder, and stamps them all as collected for the visit.
// (Tiles arriving is scroll-choreography's job; this layer leaves it alone.)
//
// The download itself is untouched. Nothing here adds words, and reduced
// motion keeps the stamps without the wave.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    function tiles(): HTMLElement[] {
        return Array.from(document.querySelectorAll<HTMLElement>("#fileList .media-file-tile"));
    }

    function wave(list: HTMLElement[]): void {
        if (reduced.matches) return;
        list.forEach((tile, index) => {
            const delay = Math.min(index, 40) * 28;
            {
                tile.animate(
                    [
                        { boxShadow: "0 0 0 0 color-mix(in srgb, var(--lwc-accent) 0%, transparent)", transform: "none" },
                        { boxShadow: "0 0 0 3px var(--lwc-accent)", transform: "translateY(-3px)", offset: 0.35 },
                        { boxShadow: "0 0 0 0 transparent", transform: "none" }
                    ],
                    { duration: 620, delay, easing: "cubic-bezier(.2,0,0,1)" }
                );
            }
        });
    }

    function start(): void {
        const kit = document.querySelector<HTMLAnchorElement>(".media-kit-primary-download");
        kit?.addEventListener("click", () => {
            const list = tiles();
            kit.dataset.taken = "";
            wave(list);
            window.setTimeout(() => list.forEach(tile => { tile.dataset.collected = ""; }), reduced.matches ? 0 : 360);
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
