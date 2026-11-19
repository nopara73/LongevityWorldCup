// The site footer's arrival and touch (footer-feel.css owns the look).
//
// - Every rail tile and follow icon gets its index, so the CSS can deal them
//   in one after another the first time the footer scrolls into view on a
//   page view. The crest pulses once as it arrives.
// - Footer links are only hidden while the footer is off screen and still
//   to arrive. A footer already on screen when the page opens (short pages,
//   a restored scroll), keyboard focus reaching it first, or a browser
//   without IntersectionObserver all show the footer settled, at once.
// - Pressing the crest gives it a quick hop before site-footer.js scrolls
//   the page back to the top.
//
// site-footer.js keeps the rail glide and the crest's scroll-to-top. Nothing
// here changes wording; reduced motion keeps every state and drops travel.

(() => {
    const footer = document.querySelector<HTMLElement>("footer.footer");
    if (!footer) return;
    const reduced = typeof window.matchMedia === "function" ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
    footer.dataset.feel = "";

    const follow = Array.from(footer.querySelectorAll<HTMLElement>(".footer-follow-links .footer-link, .footer-subscriptions .footer-link"));
    follow.forEach((link, index) => link.style.setProperty("--follow-i", String(index)));
    const rail = Array.from(footer.querySelectorAll<HTMLElement>(".footer-column--explore .footer-link"));
    rail.forEach((link, index) => link.style.setProperty("--rail-i", String(index)));
    // The rail's current-page marker and the subscriptions' divider arrive
    // with the tile and the icon they belong to.
    footer.style.setProperty("--glide-i", String(Math.max(0, rail.findIndex(link => link.getAttribute("aria-current") === "page"))));
    footer.style.setProperty("--sub-i", String(Math.max(0, follow.findIndex(link => link.closest(".footer-subscriptions")))));

    // The footer starts as it ends. The observer's first report (taken after
    // layout, so nothing is measured here) decides: already on screen means
    // settled with no arrival; off screen means the parts wait, hidden only
    // while nobody can see them, and arrive once.
    const settle = (): void => {
        footer.classList.remove("is-waiting");
        footer.classList.add("is-arrived", "is-settled");
    };
    if (!("IntersectionObserver" in window)) {
        settle();
    } else {
        let first = true;
        const seen = new IntersectionObserver(entries => {
            const entry = entries[entries.length - 1];
            if (!entry) return;
            if (first) {
                first = false;
                if (entry.isIntersecting) { seen.disconnect(); settle(); return; }
                footer.classList.add("is-waiting");
                return;
            }
            if (!entry.isIntersecting) return;
            // Arrive once most of the footer is in view, or as much of it as
            // the screen or the end of the page allows (tall footers, large text).
            if (entry.intersectionRatio < 0.6 && entry.intersectionRect.height < window.innerHeight * 0.4
                && window.scrollY + window.innerHeight < document.documentElement.scrollHeight - 4) return;
            seen.disconnect();
            footer.classList.remove("is-waiting");
            footer.classList.add("is-arrived");
            // The deal is about a second; retire it so nothing replays later.
            window.setTimeout(() => footer.classList.add("is-dealt"), 1600);
        }, { threshold: [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6] });
        seen.observe(footer);
        footer.addEventListener("focusin", () => {
            if (footer.classList.contains("is-arrived")) return;
            seen.disconnect();
            settle();
        });
    }

    const crest = footer.querySelector<HTMLElement>(".footer-crest");
    if (!crest) return;
    // The arrival pulse plays once; a press or its end retires it for good.
    crest.addEventListener("animationend", event => {
        if (event.animationName === "footer-crest-pulse") footer.dataset.pulsed = "";
        if (event.animationName === "footer-crest-launch") crest.classList.remove("is-launching");
    });
    crest.addEventListener("pointerdown", event => {
        footer.dataset.pulsed = "";
        if (event.button !== 0 || reduced?.matches) return;
        crest.classList.remove("is-launching");
        void crest.offsetWidth;
        crest.classList.add("is-launching");
    });
})();
