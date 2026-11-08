// The ZIP control and collected stamps acknowledge taking the whole kit.
// media.html carries the collection state into previews that arrive later.

(() => {
    function start(): void {
        const kit = document.querySelector<HTMLAnchorElement>(".media-kit-primary-download");
        kit?.addEventListener("click", () => {
            kit.dataset.taken = "";
            document.querySelectorAll<HTMLElement>("#fileList .media-file-tile").forEach(tile => {
                tile.dataset.collected = "";
            });
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
