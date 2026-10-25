// Surface and depth: sticky bars rise once content slides under them.
// depth-system.css owns every level; this classic script only reports which
// sticky bars are currently pinned, as data-lwc-stuck, so the bar can ease
// from flat to raised. A bar resting in its normal place stays flat.
//
// Known sticky bars are adopted by selector; any page can opt in another one
// with a data-lwc-sticky attribute. Work happens once per animation frame on
// scroll and resize, and only reads the bars' own boxes.

(() => {
    const STICKY_SELECTORS = [
        '[data-lwc-sticky]',
        'main[data-leaderboard-page="full"] .leaderboard table thead',
        '.event-board-page .events-board tbody tr.events-month-row',
        '.lmx-board-row.header',
        '.lmx-dashboard-category',
        '.lmx-feed-updates',
        '.sidebar-heading',
        ':is(#phenoAgeForm, #bortzAgeForm) :is(#lwc-step-1, #lwc-step-2) > fieldset > legend'
    ].join(', ');

    const tracked = new Set<HTMLElement>();
    let queued = false;

    function adopt(root: ParentNode): void {
        root.querySelectorAll<HTMLElement>(STICKY_SELECTORS).forEach((element) => {
            if (tracked.has(element)) return;
            tracked.add(element);
            if (!element.hasAttribute('data-lwc-sticky')) element.setAttribute('data-lwc-sticky', '');
        });
    }

    function pinnedTop(element: HTMLElement): number | null {
        const style = getComputedStyle(element);
        if (style.position !== 'sticky') return null;
        const top = parseFloat(style.top);
        return Number.isFinite(top) ? top : null;
    }

    function scrollParentTop(element: HTMLElement): { top: number; scrolled: boolean } {
        let parent = element.parentElement;
        while (parent && parent !== document.body) {
            const overflow = getComputedStyle(parent).overflowY;
            if (overflow === 'auto' || overflow === 'scroll') {
                return { top: parent.getBoundingClientRect().top, scrolled: parent.scrollTop > 0 };
            }
            parent = parent.parentElement;
        }
        return { top: 0, scrolled: window.scrollY > 0 };
    }

    function update(): void {
        queued = false;
        tracked.forEach((element) => {
            if (!element.isConnected) {
                tracked.delete(element);
                return;
            }
            const top = pinnedTop(element);
            let stuck = false;
            if (top !== null && element.getClientRects().length > 0) {
                const container = scrollParentTop(element);
                const offset = element.getBoundingClientRect().top - container.top;
                stuck = container.scrolled && Math.abs(offset - top) < 1.5;
            }
            if (stuck !== element.hasAttribute('data-lwc-stuck')) {
                element.toggleAttribute('data-lwc-stuck', stuck);
            }
        });
    }

    function queue(): void {
        if (queued) return;
        queued = true;
        requestAnimationFrame(update);
    }

    function start(): void {
        adopt(document);
        queue();
        // Capture catches inner scrollers such as the profile dialog.
        document.addEventListener('scroll', queue, { capture: true, passive: true });
        window.addEventListener('resize', queue, { passive: true });
        // Boards and feeds render their rows after load.
        let adoptQueued = false;
        new MutationObserver(() => {
            if (adoptQueued) return;
            adoptQueued = true;
            requestAnimationFrame(() => {
                adoptQueued = false;
                adopt(document);
                queue();
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
