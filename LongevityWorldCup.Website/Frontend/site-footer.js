(() => {
    const currentPath = window.location.pathname.replace(/\/$/, '') || '/';
    document.querySelectorAll('.footer-link[href^="/"]').forEach(link => {
        const linkPath = new URL(link.href, window.location.origin).pathname.replace(/\/$/, '') || '/';
        if (linkPath === currentPath) {
            link.setAttribute('aria-current', 'page');
        }
    });

    const homeLink = document.querySelector('header[role="banner"] .header-link');
    if (homeLink && currentPath === '/') {
        homeLink.setAttribute('aria-current', 'page');
    }
})();
