// Generated reading pages (About, History, Ruleset, Privacy): the chapter feel.
//
// doc-chapters.css owns the look; this classic script only times:
// - A numbered chapter heading that scrolls into view stamps its number badge
//   and draws an accent rule out from its title, once.
// - The closing "Where to go next" deck deals its doorways in when it arrives.
// - A doorway follows the pointer with a soft light, so the deck feels like a
//   row of lit thresholds rather than a list of links.
//
// Nothing here changes wording or the document. Without the script, or with
// reduced motion, the page reads as before.

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    function start(): void {
        const page = document.querySelector<HTMLElement>(".documentation-page");
        if (!page) return;
        page.dataset.docChapters = "";

        const heads = Array.from(page.querySelectorAll<HTMLElement>("h2.documentation-chapter, .ruleset-rule > h2"));
        const deck = page.querySelector<HTMLElement>("#where-to-go-next + ul");
        if (deck) deck.classList.add("doc-doorways");

        if (reduced.matches || !("IntersectionObserver" in window)) {
            heads.forEach(head => head.classList.add("is-opened"));
            deck?.classList.add("is-dealt");
            return;
        }

        // Headings already above the fold on load are simply open.
        const viewH = window.innerHeight;
        heads.forEach(head => {
            if (head.getBoundingClientRect().top < viewH * 0.9) head.classList.add("is-opened", "is-instant");
        });

        const seen = new IntersectionObserver(entries => {
            entries.forEach(entry => {
                if (!entry.isIntersecting) return;
                const el = entry.target as HTMLElement;
                el.classList.add(el === deck ? "is-dealt" : "is-opened");
                seen.unobserve(el);
            });
        }, { rootMargin: "0px 0px -18% 0px", threshold: 0.2 });
        heads.filter(head => !head.classList.contains("is-opened")).forEach(head => seen.observe(head));
        if (deck) {
            if (deck.getBoundingClientRect().top < viewH * 0.85) deck.classList.add("is-dealt", "is-instant");
            else seen.observe(deck);
        }

        // Doorways: a light follows the pointer across each tile.
        deck?.querySelectorAll<HTMLElement>(":scope > li").forEach((door, index) => {
            door.style.setProperty("--door-i", String(index));
            door.addEventListener("pointermove", event => {
                if (event.pointerType !== "mouse") return;
                const r = door.getBoundingClientRect();
                door.style.setProperty("--door-x", `${((event.clientX - r.left) / r.width * 100).toFixed(1)}%`);
                door.style.setProperty("--door-y", `${((event.clientY - r.top) / r.height * 100).toFixed(1)}%`);
            });
        });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
