// The homepage Highlights card as a small version of the /events timeline.
//
// highlights-wire.css owns the look: days are grouped (the date stands only at
// the start of each day), a spine joins the portraits, and each portrait wears
// its kind chip as on /events. This classic script only times and marks:
// - today's events are marked fresh, so their portraits wear a ring;
// - the first time the card comes into view, the spine draws down and the
//   rows tick in one after another, each date sliding in at its day's start.
//
// events-page.js keeps rendering and refreshing the rows; this script follows
// them. Nothing here adds words. Reduced motion shows the finished card.

(() => {
    const root = document.getElementById("events-root-index");
    if (!root) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    root.dataset.highlightsWire = "";

    const today = (): string => {
        const now = new Date();
        return `${now.getFullYear()}-${now.getMonth() + 1}-${now.getDate()}`;
    };

    function mark(): void {
        const rows = Array.from(root!.querySelectorAll<HTMLTableRowElement>("tbody tr.main-row"));
        const day = today();
        rows.forEach((row, index) => {
            row.style.setProperty("--wire-i", String(Math.min(index, 12)));
            const fresh = row.dataset["day"] === day;
            // classList.toggle with force writes only on a real change.
            row.classList.toggle("is-wire-fresh", fresh);
        });
    }

    let wired = false;
    function wire(): void {
        if (wired) return;
        wired = true;
        mark();
        if (reduced.matches) {
            root!.classList.add("is-wired");
            return;
        }
        root!.classList.add("is-wiring");
        // The longest stagger (12 rows) plus the slowest part.
        window.setTimeout(() => {
            root!.classList.remove("is-wiring");
            root!.classList.add("is-wired");
        }, 12 * 70 + 900);
    }

    // Rows arrive (and refresh) from events-page.js.
    new MutationObserver(() => mark()).observe(root, { childList: true, subtree: true });
    mark();

    if (typeof IntersectionObserver === "function") {
        const seen = new IntersectionObserver(entries => {
            if (!entries.some(entry => entry.isIntersecting)) return;
            // Wait for rows if the card shows up before they do.
            if (!root.querySelector("tbody tr.main-row")) return;
            seen.disconnect();
            wire();
        }, { threshold: 0.25 });
        seen.observe(root);
        // If rows land while the card is already on screen, look again.
        new MutationObserver((_, observer) => {
            if (!root.querySelector("tbody tr.main-row")) return;
            observer.disconnect();
            seen.unobserve(root);
            seen.observe(root);
        }).observe(root, { childList: true, subtree: true });
    } else {
        wire();
    }
})();
