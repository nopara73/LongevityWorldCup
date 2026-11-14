// The site footer's arrival and touch (footer-feel.css owns the look).
//
// - Every follow icon gets its index, so the CSS can deal them in one after
//   another as the footer scrolls into view (a scroll timeline where the
//   browser has one, a one-time class otherwise).
// - When the footer has fully arrived, the crest gives a single pulse.
// - Pressing the crest launches it a little before site-footer.js scrolls
//   the page back to the top.
//
// site-footer.js keeps the rail glide and the crest's scroll-to-top. Nothing
// here changes wording; reduced motion keeps every state and drops travel.

(() => {
    const footer = document.querySelector<HTMLElement>("footer.footer");
    if (!footer) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    footer.dataset.feel = "";

    footer.querySelectorAll<HTMLElement>(".footer-follow-links .footer-link, .footer-subscriptions .footer-link").forEach((link, index) => {
        link.style.setProperty("--follow-i", String(index));
    });
    footer.querySelectorAll<HTMLElement>(".footer-column--explore .footer-link").forEach((link, index) => {
        link.style.setProperty("--rail-i", String(index));
    });

    const crest = footer.querySelector<HTMLElement>(".footer-crest");
    if ("IntersectionObserver" in window) {
        const seen = new IntersectionObserver(entries => {
            if (!entries.some(e => e.isIntersecting)) return;
            footer.classList.add("is-arrived");
            seen.disconnect();
        }, { threshold: 0.6 });
        seen.observe(footer);
    } else {
        footer.classList.add("is-arrived");
    }

    crest?.addEventListener("pointerdown", () => {
        if (reduced.matches) return;
        crest.classList.remove("is-launching");
        void crest.offsetWidth;
        crest.classList.add("is-launching");
        window.setTimeout(() => crest.classList.remove("is-launching"), 700);
    });
})();
