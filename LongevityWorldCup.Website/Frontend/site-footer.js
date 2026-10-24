(() => {
    const currentPath = window.location.pathname.replace(/\/$/, '') || '/';
    document.querySelectorAll('.footer-link[href^="/"]').forEach(link => {
        if (link.hash) return;
        const linkPath = new URL(link.href, window.location.origin).pathname.replace(/\/$/, '') || '/';
        if (linkPath === currentPath) {
            link.setAttribute('aria-current', 'page');
        }
    });

    const homeLink = document.querySelector('header[role="banner"] .header-link');
    if (homeLink && currentPath === '/') {
        homeLink.setAttribute('aria-current', 'page');
    }

    const footer = document.querySelector('.footer');
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        footer?.querySelectorAll('.footer-column--follow .footer-link:is(:hover, :focus-visible)').forEach(link => {
            link.setAttribute('data-label-dismissed', '');
        });
    });
    for (const eventName of ['focusin', 'pointerover']) {
        footer?.addEventListener(eventName, event => {
            if (!(event.target instanceof Element)) return;
            const link = event.target.closest('.footer-column--follow .footer-link');
            if (link && !link.contains(event.relatedTarget)) link.removeAttribute('data-label-dismissed');
        });
    }
})();

// One indicator glides across a rail of pages: it rests on the current page
// and follows the pointer and keyboard focus, so moving between links reads
// as one motion instead of separate highlights blinking on and off.
(() => {
    function attach(rail) {
        if (!rail || rail.querySelector(':scope > .nav-glide')) return;
        const glide = document.createElement('span');
        glide.className = 'nav-glide';
        glide.setAttribute('aria-hidden', 'true');
        rail.append(glide);
        rail.classList.add('has-nav-glide');
        let target = null;

        function place(link, instant) {
            target = link;
            if (!link) {
                glide.classList.remove('is-shown');
                return;
            }
            // Layout offsets ignore the transforms of an opening menu.
            if (!link.offsetWidth) return;
            if (instant || !glide.classList.contains('is-shown')) glide.classList.add('is-instant');
            glide.style.setProperty('--glide-x', link.offsetLeft + 'px');
            glide.style.setProperty('--glide-y', link.offsetTop + 'px');
            glide.style.setProperty('--glide-w', link.offsetWidth + 'px');
            glide.style.setProperty('--glide-h', link.offsetHeight + 'px');
            glide.classList.toggle('is-current', link.getAttribute('aria-current') === 'page');
            glide.classList.toggle('is-focus', link.matches(':focus-visible'));
            glide.classList.add('is-shown');
            if (glide.classList.contains('is-instant')) {
                glide.getBoundingClientRect();
                requestAnimationFrame(() => glide.classList.remove('is-instant'));
            }
        }
        const current = () => rail.querySelector('.footer-link[aria-current="page"]');
        const rest = () => {
            const focused = rail.querySelector('.footer-link:focus-visible');
            place(focused || current());
        };
        rail.addEventListener('pointerover', event => {
            const link = event.target instanceof Element && event.target.closest('.footer-link');
            if (link && link !== target) place(link);
        });
        rail.addEventListener('pointerleave', () => rest());
        rail.addEventListener('focusin', event => {
            const link = event.target instanceof Element && event.target.closest('.footer-link');
            if (link) place(link);
        });
        rail.addEventListener('focusout', () => requestAnimationFrame(rest));
        new ResizeObserver(() => { if (target) place(target, true); }).observe(rail);
        place(current(), true);
    }
    window.LwcNavGlide = attach;
    attach(document.querySelector('.footer .footer-column--explore'));
})();

// The crest returns to the top of the page and hands focus to the band.
document.querySelector('.footer a.footer-crest')?.addEventListener('click', event => {
    event.preventDefault();
    const smooth = !matchMedia('(prefers-reduced-motion: reduce)').matches;
    window.scrollTo({ top: 0, behavior: smooth ? 'smooth' : 'auto' });
    const source = document.activeElement;
    const started = performance.now();
    function focusAtTop() {
        // Do not move keyboard focus off-screen during the smooth scroll, or
        // steal it if the reader has already chosen another control.
        if (document.activeElement !== source) return;
        if (window.scrollY <= 1) {
            document.querySelector('header[role="banner"] .header-link')?.focus({ preventScroll: true });
        } else if (performance.now() - started < 2000) requestAnimationFrame(focusAtTop);
    }
    focusAtTop();
});
