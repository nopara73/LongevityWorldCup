const documentationCanonicalPath = document.currentScript?.dataset.canonicalPath;
if (documentationCanonicalPath) {
    history.replaceState(history.state, "", documentationCanonicalPath + window.location.search + window.location.hash);
}
document.addEventListener("DOMContentLoaded", () => {
    const documentationNav = document.querySelector(".documentation-nav");
    const documentationNavToggle = documentationNav?.querySelector(".documentation-nav-toggle");
    const setDocumentationNavOpen = open => {
        if (!documentationNav || !documentationNavToggle) {
            return;
        }

        documentationNav.classList.toggle("is-open", open);
        documentationNavToggle.setAttribute("aria-expanded", String(open));
        const icon = documentationNavToggle.querySelector(".documentation-nav-toggle-icon");
        if (icon) {
            icon.textContent = open ? "−" : "+";
        }
    };

    if (documentationNav && documentationNavToggle) {
        documentationNav.classList.add("is-enhanced");
        setDocumentationNavOpen(false);
    }

    documentationNavToggle?.addEventListener("click", () => {
        const open = documentationNavToggle.getAttribute("aria-expanded") !== "true";
        updateGeometry();
        setDocumentationNavOpen(open);
        // Open the list at the section being read.
        const list = documentationNav.querySelector(".documentation-nav-links");
        const current = documentationNav.querySelector("a.is-active");
        if (open && list && current && current.offsetParent === list) {
            list.scrollTop = current.offsetTop - (list.clientHeight - current.offsetHeight) / 2;
        }
    });

    // The open list overlays the document, so it closes like other menus.
    document.addEventListener("click", event => {
        if (documentationNav?.classList.contains("is-open") && !documentationNav.contains(event.target)) {
            setDocumentationNavOpen(false);
        }
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && documentationNav?.classList.contains("is-open")) {
            const focusWasInside = documentationNav.contains(document.activeElement);
            setDocumentationNavOpen(false);
            if (focusWasInside) {
                documentationNavToggle?.focus();
            }
        }
    });
    documentationNav?.addEventListener("focusout", event => {
        if (event.relatedTarget && !documentationNav.contains(event.relatedTarget)) {
            setDocumentationNavOpen(false);
        }
    });

    const navLinks = Array.from(document.querySelectorAll(".documentation-nav a[href^='#']"));
    const headings = navLinks
        .map(link => document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1))))
        .filter(Boolean);

    const getOffset = () => {
        const stickyHeader = document.getElementById("site-sticky-header");
        const measuredHeight = parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--site-sticky-height"));
        const header = (stickyHeader && stickyHeader.offsetHeight) || measuredHeight || 52;
        // On phones and tablets the folded contents bar also stays on screen.
        const bar = documentationNav?.classList.contains("is-enhanced") && getComputedStyle(documentationNav).position === "sticky"
            ? documentationNavToggle.offsetHeight + parseFloat(getComputedStyle(documentationNav).top) - (measuredHeight || header)
            : 0;
        return header + bar + 18;
    };

    let activeLink = null;
    let navigationDestination = null;
    const revealActiveLink = () => {
        const link = documentationNav?.querySelector("a:focus") || activeLink;
        if (!documentationNav || !link || !window.matchMedia("(min-width: 901px)").matches) {
            return;
        }

        const pane = documentationNav.getBoundingClientRect();
        const target = link.getBoundingClientRect();
        const padding = parseFloat(getComputedStyle(documentationNav).scrollPaddingTop) || 0;
        if (target.top < pane.top + padding) {
            documentationNav.scrollTop += target.top - pane.top - padding;
        } else if (target.bottom > pane.bottom - padding) {
            documentationNav.scrollTop += target.bottom - pane.bottom + padding;
        }
    };

    // The folded contents bar names the section being read and shows how
    // far through the document the reader is.
    const documentationNavCurrent = documentationNav?.querySelector(".documentation-nav-current");
    const documentationNavLabel = documentationNavCurrent?.textContent || "";
    const numberedLinks = navLinks.filter(link => link.matches(".documentation-nav-chapter, .documentation-nav-rule"));
    const documentationArticle = document.querySelector(".documentation-document");
    const updateCurrentSection = () => {
        if (!documentationNav || !documentationNavCurrent) {
            return;
        }

        const offset = getOffset() + 6;
        const entered = headings.length > 0 && headings[0].getBoundingClientRect().top <= offset;
        const link = entered ? activeLink : null;
        const number = link ? numberedLinks.indexOf(link) : -1;
        const text = link ? link.textContent.replace(/\s+/g, " ").trim() : documentationNavLabel;
        if (documentationNavCurrent.dataset.text !== text) {
            documentationNavCurrent.dataset.text = text;
            const label = document.createElement("span");
            label.className = "documentation-nav-current-text";
            label.textContent = text;
            documentationNavCurrent.replaceChildren(label);
        }
        if (number >= 0) {
            documentationNavCurrent.dataset.number = String(number + 1).padStart(2, "0");
        } else {
            delete documentationNavCurrent.dataset.number;
        }

        if (documentationArticle) {
            const bounds = documentationArticle.getBoundingClientRect();
            const travel = Math.max(1, bounds.height - window.innerHeight + offset);
            const progress = Math.min(1, Math.max(0, (offset - bounds.top) / travel));
            documentationNav.style.setProperty("--documentation-progress", progress.toFixed(4));
        }

        const navStyle = getComputedStyle(documentationNav);
        const stickyTop = parseFloat(navStyle.top);
        documentationNav.classList.toggle("is-stuck", navStyle.position === "sticky" && window.scrollY > 0 &&
            Number.isFinite(stickyTop) && documentationNav.getBoundingClientRect().top <= stickyTop + 0.5);
    };

    const setActive = id => {
        const previousActiveLink = activeLink;
        activeLink = null;
        navLinks.forEach(link => {
            const active = decodeURIComponent(link.getAttribute("href").slice(1)) === id;
            link.classList.toggle("is-active", active);
            if (active) {
                activeLink = link;
                link.setAttribute("aria-current", "location");
            } else {
                link.removeAttribute("aria-current");
            }
        });
        if (activeLink !== previousActiveLink) {
            revealActiveLink();
        }
        // A contents click updates aria-current synchronously. Keep its visible
        // label in the same state even before the next scroll animation frame.
        updateCurrentSection();
    };

    navLinks.forEach(link => {
        link.addEventListener("click", event => {
            if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) {
                return;
            }
            const target = document.getElementById(decodeURIComponent(link.getAttribute("href").slice(1)));
            if (!target) {
                return;
            }

            event.preventDefault();
            if (window.matchMedia("(max-width: 900px)").matches) {
                setDocumentationNavOpen(false);
            }
            const top = target.getBoundingClientRect().top + window.scrollY - getOffset();
            window.scrollTo({ top, behavior: "instant" });
            history.pushState(null, "", link.getAttribute("href"));
            navigationDestination = { target, scrollY: window.scrollY, hash: window.location.hash };
            setActive(target.id);

            target.setAttribute("tabindex", "-1");
            target.focus({ preventScroll: true });
        });
    });

    const updateActiveFromScroll = () => {
        const offset = getOffset() + 6;
        // A section near the page end may be visible without reaching the offset.
        // Honor that destination until the reader moves away from its landing position.
        if (navigationDestination && window.scrollY === navigationDestination.scrollY &&
            window.location.hash === navigationDestination.hash) {
            const bounds = navigationDestination.target.getBoundingClientRect();
            if (bounds.bottom > offset && bounds.top < window.innerHeight) {
                setActive(navigationDestination.target.id);
                updateCurrentSection();
                return;
            }
        }
        navigationDestination = null;
        let current = headings[0];

        for (const heading of headings) {
            if (heading.getBoundingClientRect().top <= offset) {
                current = heading;
            } else {
                break;
            }
        }
        if (window.scrollY > 0 && documentationArticle?.getBoundingClientRect().bottom <= window.innerHeight + 1) {
            current = headings[headings.length - 1];
        }
        if (current) {
            setActive(current.id);
        }
        updateCurrentSection();
    };

    const synchronizeNavigationFromHash = () => {
        let id;
        try {
            id = decodeURIComponent(window.location.hash.slice(1));
        } catch {
            id = "";
        }
        const target = headings.find(heading => heading.id === id);
        navigationDestination = null;
        if (target) {
            const bounds = target.getBoundingClientRect();
            if (bounds.bottom > getOffset() && bounds.top < window.innerHeight) {
                navigationDestination = { target, scrollY: window.scrollY, hash: window.location.hash };
            }
        }
        updateActiveFromScroll();
    };

    let lastOffset = getOffset();
    const updateGeometry = () => {
        const offset = getOffset();
        document.documentElement.style.setProperty("--documentation-anchor-offset", `${offset}px`);
        if (documentationNavToggle) {
            const available = Math.max(44, innerHeight - documentationNavToggle.getBoundingClientRect().bottom - 8);
            documentationNav.style.setProperty("--documentation-list-height", `${available}px`);
        }
        // Text, loaded fonts and the header action can resize without a viewport
        // event. Keep a selected heading clear while the reader remains there.
        if (offset !== lastOffset && navigationDestination && window.scrollY === navigationDestination.scrollY) {
            window.scrollTo({ top: navigationDestination.target.getBoundingClientRect().top + scrollY - offset, behavior: "instant" });
            navigationDestination.scrollY = window.scrollY;
        }
        lastOffset = offset;
        updateActiveFromScroll();
    };

    let ticking = false;
    window.addEventListener("scroll", () => {
        if (ticking) {
            return;
        }

        ticking = true;
        window.requestAnimationFrame(() => {
            updateGeometry();
            ticking = false;
        });
    }, { passive: true });
    window.addEventListener("resize", () => {
        updateGeometry();
        revealActiveLink();
    });
    window.addEventListener("hashchange", () => window.requestAnimationFrame(synchronizeNavigationFromHash));
    window.addEventListener("pageshow", event => {
        if (!event.persisted && window.location.hash) {
            synchronizeNavigationFromHash();
        }
    });

    const geometryObserver = new ResizeObserver(updateGeometry);
    for (const element of [document.getElementById("site-sticky-header"), documentationNavToggle, documentationArticle]) {
        if (element) geometryObserver.observe(element);
    }
    updateGeometry();
    if (window.location.hash) {
        document.fonts.ready.then(() => window.requestAnimationFrame(() => {
            // The browser may have placed the initial fragment before fonts or
            // the enhanced contents bar had their final height.
            const target = headings.find(heading => `#${encodeURIComponent(heading.id)}` === window.location.hash || `#${heading.id}` === window.location.hash);
            if (target) {
                window.scrollTo({ top: target.getBoundingClientRect().top + scrollY - getOffset(), behavior: "instant" });
            }
            synchronizeNavigationFromHash();
        }));
    }
});
