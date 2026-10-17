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
