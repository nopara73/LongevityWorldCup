// Type system feel: section headings settle in the first time they scroll
// into view.
//
// Page titles settle with a CSS animation as the page paints. Headings below
// the fold are marked pending while they are still offscreen, so nothing that
// is already visible ever disappears, and settle when the reader reaches them.
// Each heading settles once. Long-form pages already reveal their own blocks,
// so headings inside those reveals are left alone. prefers-reduced-motion
// turns all of it off.

(() => {
    const root = document.documentElement;
    if (root.dataset["lwcTypeSystem"] === "on") return;
    root.dataset["lwcTypeSystem"] = "on";

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    if (reduced.matches || !("IntersectionObserver" in window)) return;

    const headingSelector = [
        "main h2:not(.visually-hidden)",
        "main h3:not(.visually-hidden)",
        ".media-kit-group-title"
    ].join(",");
    const eyebrowSelector = [
        ".homepage-main .section-container h2",
        ".lmx-habit-heading",
        ".proof-review-heading"
    ].join(",");
    // Owners of their own entrance motion.
    const skipSelector = [
        ".documentation-reveal",
        "[data-lwc-reveal]",
        "dialog",
        "[role='dialog']",
        ".modal",
        ".sidebar",
        "[hidden]",
        "[aria-hidden='true']"
    ].join(",");

    const settled = new WeakSet<Element>();
    const observer = new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            const heading = entry.target as HTMLElement;
            observer.unobserve(heading);
            heading.dataset["lwcSettle"] = "in";
            heading.addEventListener("animationend", () => {
                delete heading.dataset["lwcSettle"];
            }, { once: true });
            // A heading that never animates (display changes, detached) still clears.
            window.setTimeout(() => {
                if (heading.dataset["lwcSettle"] === "in") delete heading.dataset["lwcSettle"];
            }, 1200);
        }
    }, { threshold: 0.35, rootMargin: "0px 0px -6% 0px" });

    function arm(heading: HTMLElement): void {
        if (settled.has(heading)) return;
        settled.add(heading);
        if (heading.closest(skipSelector)) return;
        const rect = heading.getBoundingClientRect();
        // Already visible, or not rendered: leave it exactly as it is.
        if (rect.height === 0 || rect.top < window.innerHeight) return;
        if (heading.matches(eyebrowSelector)) heading.classList.add("lwc-eyebrow-settle");
        heading.dataset["lwcSettle"] = "pending";
        observer.observe(heading);
    }

    function scan(): void {
        document.querySelectorAll<HTMLElement>(headingSelector).forEach(arm);
    }

    reduced.addEventListener("change", () => {
        if (!reduced.matches) return;
        observer.disconnect();
        document.querySelectorAll<HTMLElement>("[data-lwc-settle]").forEach(heading => {
            delete heading.dataset["lwcSettle"];
        });
    });

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", scan, { once: true });
    } else {
        scan();
    }
    // Content that arrives later (boards, feeds) gets one more pass.
    window.addEventListener("load", scan, { once: true });
})();
