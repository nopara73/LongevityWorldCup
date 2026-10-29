const documentationCanonicalPath = document.currentScript?.dataset.canonicalPath;
if (documentationCanonicalPath) {
    history.replaceState(history.state, "", documentationCanonicalPath + window.location.search + window.location.hash);
}
const documentationReducedMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
const documentationMotionAllowed = () => !documentationReducedMotion.matches;
const documentationFolds = new Map();

// Folds (ruleset rules and FAQ answers) slide between their heights. A fold that
// is interrupted turns around from wherever it is. Closed folds stay findable.
window.LwcDocumentationFold = (region, open, animate = true) => {
    const running = typeof region.getAnimations === "function"
        ? region.getAnimations().filter(animation => animation.id === "documentation-fold")
        : [];
    const fromHeight = region.getBoundingClientRect().height;
    const fromOpacity = running.length ? getComputedStyle(region).opacity : (open ? "0" : "1");
    running.forEach(animation => animation.cancel());
    documentationFolds.delete(region);

    if (open) {
        region.removeAttribute("hidden");
    }
    region.inert = false;
    if (!animate || !documentationMotionAllowed() || typeof region.animate !== "function") {
        if (!open) {
            region.setAttribute("hidden", "until-found");
        }
        return;
    }

    const style = getComputedStyle(region);
    const full = {
        height: `${open ? region.getBoundingClientRect().height : fromHeight}px`,
        paddingTop: style.paddingTop,
        paddingBottom: style.paddingBottom,
        opacity: "1"
    };
    const from = { height: `${fromHeight}px`, opacity: fromOpacity };
    const zero = { height: "0px", paddingTop: "0px", paddingBottom: "0px", opacity: "0" };
    const travel = Math.abs(parseFloat(full.height) - fromHeight);
    const duration = Math.round(Math.min(380, 190 + travel * 0.12) * (open ? 1 : 0.8));
    const animation = region.animate(
        open
            ? [{ ...(running.length ? from : zero), overflow: "hidden", boxSizing: "border-box" }, { ...full, overflow: "hidden", boxSizing: "border-box" }]
            : [{ ...from, paddingTop: style.paddingTop, paddingBottom: style.paddingBottom, overflow: "hidden", boxSizing: "border-box" }, { ...zero, overflow: "hidden", boxSizing: "border-box" }],
        { id: "documentation-fold", duration, easing: open ? "cubic-bezier(0.2, 0, 0, 1)" : "cubic-bezier(0.4, 0, 0.9, 0.6)" });
    // While closing, its fading links are already dismissed. Once hidden,
    // remove inert so native find-in-page can still reveal the region.
    region.inert = !open;
    documentationFolds.set(region, { animation, open });
    animation.onfinish = () => {
        if (!open) region.setAttribute("hidden", "until-found");
        region.inert = false;
        documentationFolds.delete(region);
    };
};

const finishDocumentationFolds = () => {
    for (const [region, { animation, open }] of documentationFolds) {
        animation.cancel();
        if (!open) region.setAttribute("hidden", "until-found");
        region.inert = false;
    }
    documentationFolds.clear();
};
documentationReducedMotion.addEventListener("change", event => {
    if (event.matches) finishDocumentationFolds();
});
window.addEventListener("beforeprint", finishDocumentationFolds);

// The section a jump lands on is briefly emphasised.
window.LwcDocumentationArrive = target => {
    if (!target || !documentationMotionAllowed()) {
        return;
    }
    target.classList.remove("is-arriving");
    void target.offsetWidth;
    target.classList.add("is-arriving");
    clearTimeout(target.lwcArriveTimer);
    target.lwcArriveTimer = setTimeout(() => target.classList.remove("is-arriving"), 1700);
};

function setDocumentationNavOpen(documentationNav, open) {
    const documentationNavToggle = documentationNav?.querySelector(".documentation-nav-toggle");
    if (!documentationNav || !documentationNavToggle) {
        return;
    }

    // On phones the open list folds back up instead of vanishing.
    const wasOpen = documentationNav.classList.contains("is-open");
    clearTimeout(documentationNav.lwcClosingTimer);
    documentationNav.classList.remove("is-closing");
    if (wasOpen && !open && documentationMotionAllowed() && documentationNav.classList.contains("is-enhanced") &&
        window.matchMedia("(max-width: 900px)").matches) {
        documentationNav.classList.add("is-closing");
        documentationNav.lwcClosingTimer = setTimeout(() => documentationNav.classList.remove("is-closing"), 170);
    }
    documentationNav.classList.toggle("is-open", open);
    documentationNavToggle.setAttribute("aria-expanded", String(open));
    // The closing decoration must not keep an already dismissed menu in the tab order.
    const list = documentationNav.querySelector(".documentation-nav-links");
    if (list) list.inert = !open && window.matchMedia("(max-width: 900px)").matches;
    const icon = documentationNavToggle.querySelector(".documentation-nav-toggle-icon");
    if (icon) {
        icon.textContent = open ? "−" : "+";
    }
}

// This script follows the document, so the contents list is already parsed.
// Compact it now: waiting for DOMContentLoaded (which also waits for page
// modules) paints the full list first and then pulls the document up.
{
    const documentationNav = document.querySelector(".documentation-nav");
    if (documentationNav?.querySelector(".documentation-nav-toggle")) {
        documentationNav.classList.add("is-enhanced");
        setDocumentationNavOpen(documentationNav, documentationNav.classList.contains("is-open"));
    }
}

// The document has already been parsed. Bind the controls in the same turn
// that collapses the list, including when a deferred script delays DCL.
(function () {
    const documentationNav = document.querySelector(".documentation-nav");
    const documentationNavToggle = documentationNav?.querySelector(".documentation-nav-toggle");

    // The generated button works before this download finishes or if it fails.
    // Replace its small native-click fallback without toggling twice or closing
    // a contents list the reader has already opened.
    documentationNavToggle?.removeAttribute("onclick");
    documentationNavToggle?.addEventListener("click", () => {
        const open = documentationNavToggle.getAttribute("aria-expanded") !== "true";
        updateGeometry();
        setDocumentationNavOpen(documentationNav, open);
        // Open the list at the section being read.
        const list = documentationNav.querySelector(".documentation-nav-links");
        const current = documentationNav.querySelector("a.is-active");
        if (open && list && current && current.offsetParent === list) {
            list.scrollTop = current.offsetTop - (list.clientHeight - current.offsetHeight) / 2;
        }
        if (open) {
            placeInk(false);
        }
    });

    // The open list overlays the document, so it closes like other menus.
    document.addEventListener("click", event => {
        if (documentationNav?.classList.contains("is-open") && !documentationNav.contains(event.target)) {
            setDocumentationNavOpen(documentationNav, false);
        }
    });
    document.addEventListener("keydown", event => {
        if (event.key === "Escape" && documentationNav?.classList.contains("is-open")) {
            const focusWasInside = documentationNav.contains(document.activeElement);
            setDocumentationNavOpen(documentationNav, false);
            if (focusWasInside) {
                documentationNavToggle?.focus();
            }
        }
    });
    documentationNav?.addEventListener("focusout", event => {
        if (event.relatedTarget && !documentationNav.contains(event.relatedTarget)) {
            setDocumentationNavOpen(documentationNav, false);
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
    // A smooth jump in flight holds the contents on its destination.
    let jump = null;

    // One solid marker glides along the rail to the section being read.
    const linksContainer = documentationNav?.querySelector(".documentation-nav-links");
    const ink = document.createElement("span");
    ink.className = "documentation-nav-ink";
    ink.setAttribute("aria-hidden", "true");
    linksContainer?.prepend(ink);
    linksContainer?.querySelectorAll("a").forEach((link, index) => link.style.setProperty("--i", String(index)));
    let inkTimer = 0;
    const placeInk = (animate = true) => {
        if (!documentationNav || !linksContainer) {
            return;
        }
        if (!activeLink || activeLink.offsetParent !== linksContainer) {
            documentationNav.classList.remove("has-ink");
            return;
        }
        if (!animate) {
            documentationNav.classList.remove("is-ink-ready");
        }
        ink.style.setProperty("--ink-x", `${activeLink.offsetLeft}px`);
        ink.style.setProperty("--ink-y", `${activeLink.offsetTop}px`);
        ink.style.setProperty("--ink-h", `${activeLink.offsetHeight}px`);
        documentationNav.classList.add("has-ink");
        if (!documentationNav.classList.contains("is-ink-ready")) {
            requestAnimationFrame(() => requestAnimationFrame(() => documentationNav.classList.add("is-ink-ready")));
        } else if (documentationMotionAllowed()) {
            documentationNav.classList.add("is-ink-moving");
            clearTimeout(inkTimer);
            inkTimer = setTimeout(() => documentationNav.classList.remove("is-ink-moving"), 280);
        }
    };

    // Reading progress: a hairline under the site header (the phone bar has its own).
    const progressLine = document.createElement("div");
    progressLine.className = "documentation-progress";
    progressLine.setAttribute("aria-hidden", "true");
    if (documentationNav) {
        document.body.append(progressLine);
    }

    // History's rail fills up to the entry being read.
    const timelines = Array.from(document.querySelectorAll(".documentation-timeline"));
    const updateTimelines = () => {
        const line = window.innerHeight * 0.5;
        for (const timeline of timelines) {
            const bounds = timeline.getBoundingClientRect();
            const fill = Math.min(1, Math.max(0, (line - bounds.top) / Math.max(1, bounds.height)));
            timeline.style.setProperty("--timeline-fill", fill.toFixed(4));
        }
    };
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
            progressLine.style.setProperty("--documentation-progress", progress.toFixed(4));
            progressLine.classList.toggle("is-visible", window.scrollY > 24);
        }
        updateTimelines();

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
            placeInk();
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
                setDocumentationNavOpen(documentationNav, false);
                documentationNavToggle?.focus({ preventScroll: true });
            }
            // A folded rule opens before the jump measures where it lands.
            window.LwcDocumentationRevealTarget?.(target);
            revealUpTo(target);
            const top = Math.min(
                target.getBoundingClientRect().top + window.scrollY - getOffset(),
                document.documentElement.scrollHeight - window.innerHeight);
            history.pushState(history.state, "", link.getAttribute("href"));

            const land = () => {
                jump?.cleanup();
                jump = null;
                navigationDestination = { target, scrollY: window.scrollY, hash: window.location.hash };
                target.setAttribute("tabindex", "-1");
                target.focus({ preventScroll: true });
                setActive(target.id);
                window.LwcDocumentationArrive(target);
            };
            const distance = Math.abs(top - window.scrollY);
            if (!documentationMotionAllowed() || distance < 2) {
                window.scrollTo({ top, behavior: "instant" });
                land();
                return;
            }

            // Smooth jump: the contents move to the destination at once, and the
            // heading is emphasised once the page settles there.
            jump?.cleanup();
            const token = { cleanup: () => {}, land, top };
            jump = token;
            setActive(target.id);
            let arrivalFrame = 0;
            const startY = window.scrollY;
            const started = performance.now();
            const duration = Math.min(900, 250 + distance * 0.12);
            const step = now => {
                if (jump !== token) return;
                const progress = Math.min(1, (now - started) / duration);
                const eased = 1 - Math.pow(1 - progress, 3);
                window.scrollTo({ top: startY + (top - startY) * eased, behavior: "instant" });
                if (progress === 1) {
                    land();
                } else {
                    arrivalFrame = requestAnimationFrame(step);
                }
            };
            // Own the animation frame so input can stop it without a queued
            // compositor scroll or a stale scrollend moving the reader afterward.
            arrivalFrame = requestAnimationFrame(step);
            token.cleanup = () => cancelAnimationFrame(arrivalFrame);
        });
    });

    // Reader input or native history/fragment navigation supersedes a jump.
    // Otherwise its next frame can overwrite the browser's restored position.
    for (const type of ["wheel", "touchstart", "keydown", "pointerdown", "popstate", "hashchange"]) {
        window.addEventListener(type, () => {
            if (jump) {
                jump.cleanup();
                jump = null;
                navigationDestination = null;
                updateActiveFromScroll();
            }
        }, { passive: true });
    }

    // Blocks below the fold settle in as they are reached. Nothing is pending
    // with reduced motion, without IntersectionObserver, or in print.
    const pendingBlocks = new Set();
    const revealBlock = (block, delay = 0, instant = false) => {
        if (!pendingBlocks.delete(block)) {
            return;
        }
        revealObserver?.unobserve(block);
        block.style.setProperty("--reveal-delay", `${delay}ms`);
        if (instant) {
            block.classList.add("is-instant");
            requestAnimationFrame(() => requestAnimationFrame(() => block.classList.remove("is-instant")));
        }
        block.classList.add("is-revealed");
        const entry = block.parentElement?.closest(".documentation-timeline-entry.documentation-reveal-marker");
        if (entry && block.matches("h3")) {
            entry.style.setProperty("--reveal-delay", `${delay}ms`);
            if (instant) {
                entry.classList.add("is-instant");
                requestAnimationFrame(() => requestAnimationFrame(() => entry.classList.remove("is-instant")));
            }
            entry.classList.add("is-revealed");
        }
    };
    const revealObserver = documentationMotionAllowed() && "IntersectionObserver" in window && documentationArticle
        ? new IntersectionObserver(entries => {
            entries
                .filter(entry => entry.isIntersecting)
                .sort((a, b) => a.boundingClientRect.top - b.boundingClientRect.top)
                .forEach((entry, index) => revealBlock(entry.target, Math.min(index, 5) * 70));
        }, { rootMargin: "0px 0px -7% 0px" })
        : null;
    // A jump shows its whole path at once instead of fading blocks in as it flies past.
    function revealUpTo(target) {
        const limit = target.getBoundingClientRect().top + window.innerHeight * 1.1;
        for (const block of Array.from(pendingBlocks)) {
            if (block.getBoundingClientRect().top < limit) {
                revealBlock(block, 0, true);
            }
        }
    }
    const revealRestoredView = () => {
        for (const block of Array.from(pendingBlocks)) {
            if (block.getBoundingClientRect().top < window.innerHeight * 1.1) revealBlock(block, 0, true);
        }
    };
    documentationReducedMotion.addEventListener("change", event => {
        if (!event.matches) return;
        Array.from(pendingBlocks).forEach(block => revealBlock(block, 0, true));
        revealObserver?.disconnect();
        if (jump) {
            window.scrollTo({ top: jump.top, behavior: "instant" });
            jump.land();
        }
        document.querySelectorAll(".is-arriving").forEach(target => target.classList.remove("is-arriving"));
        updateActiveFromScroll();
    });
    if (revealObserver) {
        const blocks = [];
        for (const child of documentationArticle.children) {
            if (child.matches(".documentation-cover")) {
                continue;
            }
            if (child.matches(".ruleset-section:not(.documentation-next)")) {
                for (const part of child.children) {
                    if (part.matches(".ruleset-rule-body")) {
                        blocks.push(...part.children);
                    } else {
                        blocks.push(part);
                    }
                }
                continue;
            }
            if (child.matches(".documentation-timeline")) {
                for (const entry of child.querySelectorAll(":scope > .documentation-timeline-entry")) {
                    entry.classList.add("documentation-reveal-marker");
                    blocks.push(...entry.children);
                }
                continue;
            }
            blocks.push(child);
        }

        let hashTarget = null;
        try {
            hashTarget = window.location.hash ? document.getElementById(decodeURIComponent(window.location.hash.slice(1))) : null;
        } catch {
            hashTarget = null;
        }
        const limit = (hashTarget ? hashTarget.getBoundingClientRect().top : 0) + window.innerHeight * 1.05;
        for (const block of blocks) {
            const top = block.getBoundingClientRect().top;
            const entry = block.parentElement?.closest(".documentation-reveal-marker");
            if (top <= limit) {
                if (entry && block.matches("h3")) {
                    entry.classList.add("is-revealed");
                }
                continue;
            }
            if (block.matches(".documentation-table")) {
                block.querySelectorAll("tbody tr").forEach((row, index) => row.style.setProperty("--row", String(Math.min(index, 14))));
            }
            block.classList.add("documentation-reveal");
            pendingBlocks.add(block);
            revealObserver.observe(block);
        }
        // Keyboard focus never lands on a block that is still fading in.
        document.addEventListener("focusin", event => {
            const block = event.target instanceof Element ? event.target.closest(".documentation-reveal:not(.is-revealed)") : null;
            if (block) {
                revealBlock(block, 0, true);
            }
        });
        window.addEventListener("beforeprint", () => Array.from(pendingBlocks).forEach(block => revealBlock(block, 0, true)));
        window.addEventListener("hashchange", () => {
            let target = null;
            try {
                target = document.getElementById(decodeURIComponent(window.location.hash.slice(1)));
            } catch {
                target = null;
            }
            if (target) {
                revealUpTo(target);
            }
        });
    }

    const updateActiveFromScroll = () => {
        if (jump) {
            updateCurrentSection();
            return;
        }
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
            if (linksContainer) linksContainer.inert = window.matchMedia("(max-width: 900px)").matches && !documentationNav.classList.contains("is-open");
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
        placeInk(false);
    });
    window.addEventListener("hashchange", () => window.requestAnimationFrame(synchronizeNavigationFromHash));
    window.addEventListener("pageshow", event => {
        revealRestoredView();
        updateGeometry();
        // Some history restorations apply their scroll position after pageshow.
        window.requestAnimationFrame(revealRestoredView);
        if (!event.persisted && window.location.hash) {
            synchronizeNavigationFromHash();
        }
    });
    window.addEventListener("pagehide", () => {
        jump?.cleanup();
        jump = null;
        finishDocumentationFolds();
    });

    const geometryObserver = new ResizeObserver(() => {
        updateGeometry();
        placeInk(false);
    });
    for (const element of [document.getElementById("site-sticky-header"), documentationNavToggle, documentationArticle, linksContainer]) {
        if (element) geometryObserver.observe(element);
    }
    // The header publishes its measured height after its own animation frame.
    // While it is hidden, that CSS value can change without resizing its box.
    // Keep native fragment/focus clearance current after that measurement too.
    const root = document.documentElement;
    let observedHeaderHeight = getComputedStyle(root).getPropertyValue("--site-sticky-height");
    const headerHeightObserver = new MutationObserver(() => {
        const height = getComputedStyle(root).getPropertyValue("--site-sticky-height");
        if (height !== observedHeaderHeight) {
            observedHeaderHeight = height;
            updateGeometry();
        }
    });
    headerHeightObserver.observe(root, { attributes: true, attributeFilter: ["style"] });
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
})();
