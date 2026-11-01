// Scroll choreography: one small vocabulary of scroll entrances shared by every page.
//
//   data-lwc-enter            fade-rise once, the first time the element scrolls into view
//   data-lwc-stagger[="sel"]  the children (or matches of sel) rise in a short cascade;
//                             only items arriving together are staggered, so a long list
//                             never makes its fortieth row wait
//   data-lwc-count-in         a figure counts up from zero when it comes into view
//   data-lwc-parallax         hero media drifts gently against the scroll (CSS only)
//   data-lwc-handoff          a section whose heading sticks while it is read, then hands
//                             off to the next section's heading as it is pushed away
//
// Nothing is hidden in the HTML. Only elements still below the fold when this script runs
// are held back, so no-JS readers, crawlers and the first viewport always see everything.
// Motion is opacity and the individual `translate` property only (no layout shift, and it
// composes with components' own transforms). Reduced motion shows everything at once.
(() => {
    const root = document.documentElement;
    if (root.hasAttribute('data-lwc-choreo')) return;
    root.setAttribute('data-lwc-choreo', '');

    const reduce = window.matchMedia('(prefers-reduced-motion: reduce)');
    const STAGGER_CAP = 5;

    // Shared recipe for existing public markup, so pages adopt the vocabulary without
    // per-page edits. Explicit attributes in HTML always win over these.
    const recipe: [string, 'enter' | 'stagger' | 'count-in' | 'handoff' | 'parallax', string?][] = [
        // Dead AOS hooks from the old library come back as the shared fade-rise.
        ['[data-aos]', 'enter'],
        // Homepage
        ['.home-page .section-container', 'enter'],
        ['.home-page .archive-mobile-card-winners', 'stagger'],
        ['.home-page .archive-table tbody', 'stagger'],
        ['.home-page .homepage-faq-grid', 'stagger'],
        ['.home-page .homepage-panel-pair', 'stagger'],
        ['.home-page #lwc-countdown-box', 'enter'],
        ['.home-page .lwc-merch-grid', 'stagger'],
        ['#hall-of-fame .age-reduction, #hall-of-fame .archive-mobile-card-age-reduction', 'count-in'],
        ['#current-btc, #goal-btc', 'count-in'],
        // Leaderboard (home and /leaderboard): rows cascade in as the table scrolls up.
        ['#athleteLeaderboard > table > tbody', 'stagger', ':scope > tr:not(.leaderboard-skeleton-row)'],
        ['.podium', 'stagger', ':scope > *'],
        // Media kit: each group's heading hands off to the next; tiles cascade.
        ['.media-kit-group', 'handoff'],
        ['.media-kit-tiles', 'stagger', ':scope > li'],
        // Long-form pages: the cover image is the hero and drifts gently.
        ['.documentation-document > p:has(> img:only-child)', 'parallax'],
        // Longevitymaxxing landing
        ['.lmx-card, .lmx-board-wrap', 'enter'],
    ];

    const pending = new Set<HTMLElement>();
    const seen = new WeakSet<HTMLElement>();
    const settling = new Map<HTMLElement, number>();
    let pace = 1;
    let observer: IntersectionObserver | null = null;

    function belowFold(element: Element): boolean {
        const rect = element.getBoundingClientRect();
        return rect.top >= window.innerHeight && rect.height > 0;
    }

    function hold(element: HTMLElement): void {
        if (seen.has(element) || element.closest('dialog, [role="dialog"]')) return;
        seen.add(element);
        if (!observer || reduce.matches || location.hash || element.contains(document.activeElement)) return;
        if (!belowFold(element)) return;
        element.setAttribute('data-lwc-enter-state', 'wait');
        pending.add(element);
        observer?.observe(element);
    }

    function release(element: HTMLElement, index: number): void {
        if (element.getAttribute('data-lwc-enter-state') !== 'wait') return;
        observer?.unobserve(element);
        pending.delete(element);
        // A jump that lands many items at once compresses the cascade so the screen fills fast.
        element.style.setProperty('--lwc-enter-i', String(Math.min(index * pace, STAGGER_CAP)));
        element.setAttribute('data-lwc-enter-state', 'go');
        // Clear once settled so the element's own transitions take over again.
        settling.set(element, window.setTimeout(() => clearEntrance(element), 900 + Math.min(index, STAGGER_CAP) * 80));
        // A section lands first; whatever of its contents is already in view follows it in.
        const bottom = window.innerHeight;
        let next = index + 1;
        element.querySelectorAll<HTMLElement>('[data-lwc-enter-state="wait"]').forEach(child => {
            if (child.getBoundingClientRect().top < bottom) release(child, next++);
        });
    }

    function clearEntrance(element: HTMLElement): void {
        observer?.unobserve(element);
        pending.delete(element);
        window.clearTimeout(settling.get(element));
        settling.delete(element);
        element.removeAttribute('data-lwc-enter-state');
        element.style.removeProperty('--lwc-enter-i');
    }

    function releaseAll(): void {
        pending.forEach(clearEntrance);
        settling.forEach((_, element) => clearEntrance(element));
        countObserver?.disconnect();
        pendingCounts.clear();
    }

    function staggerItems(container: HTMLElement): HTMLElement[] {
        const selector = container.getAttribute('data-lwc-stagger');
        const items = selector ? container.querySelectorAll(selector) : container.children;
        return Array.from(items).filter((item): item is HTMLElement =>
            item instanceof HTMLElement && !item.hidden && item.getClientRects().length > 0);
    }

    // ---- counters ---------------------------------------------------------------------
    // Figures reuse the charts system's counter (an overlay, so the real text never
    // changes and nothing reflows); this layer only decides *when* they count.
    let countObserver: IntersectionObserver | null = null;
    const seenCounts = new WeakSet<HTMLElement>();
    const pendingCounts = new Set<HTMLElement>();

    function watchCount(element: HTMLElement): void {
        // Explicit chart figures already have their own observer and update lifecycle.
        if (seenCounts.has(element) || element.hasAttribute('data-lwc-number')) return;
        seenCounts.add(element);
        if (countObserver && !reduce.matches && !location.hash && belowFold(element)) {
            pendingCounts.add(element);
            countObserver.observe(element);
        }
    }

    // ---- sticky hand-off ---------------------------------------------------------------
    const handoffs = new Set<HTMLElement>();
    let handoffQueued = false;

    function handoffHead(section: HTMLElement): HTMLElement | null {
        return section.querySelector<HTMLElement>('[data-lwc-handoff-head]')
            ?? section.querySelector<HTMLElement>(':scope > [class$="-head"], :scope > header, :scope > h2');
    }

    function adoptHandoff(section: HTMLElement): void {
        const head = handoffHead(section);
        if (!head || handoffs.has(section)) return;
        head.setAttribute('data-lwc-handoff-head', '');
        // The depth system lifts the heading while it is stuck.
        head.setAttribute('data-lwc-sticky', '');
        handoffs.add(section);
        if (handoffs.size === 1) {
            window.addEventListener('scroll', queueHandoff, { passive: true });
            window.addEventListener('resize', queueHandoff, { passive: true });
        }
        queueHandoff();
    }

    function queueHandoff(): void {
        if (handoffQueued) return;
        handoffQueued = true;
        requestAnimationFrame(updateHandoffs);
    }

    function updateHandoffs(): void {
        handoffQueued = false;
        const writes: [HTMLElement, number][] = [];
        handoffs.forEach(section => {
            const head = handoffHead(section);
            if (!head || !section.isConnected) { handoffs.delete(section); return; }
            const sectionRect = section.getBoundingClientRect();
            const top = parseFloat(getComputedStyle(head).top) || 0;
            const height = head.offsetHeight;
            // 0 while the heading has room; 1 as the section's end pushes it away.
            // Measure the untransformed sticky position, not our previous frame's lift.
            const room = sectionRect.bottom - top - height;
            const distance = Math.max(48, height * 1.25);
            writes.push([head, sectionRect.top > top ? 0 : Math.min(1, Math.max(0, 1 - room / distance))]);
        });
        writes.forEach(([head, progress]) => {
            const value = progress.toFixed(3);
            if (head.style.getPropertyValue('--lwc-handoff') !== value) head.style.setProperty('--lwc-handoff', value);
        });
        if (!handoffs.size) {
            window.removeEventListener('scroll', queueHandoff);
            window.removeEventListener('resize', queueHandoff);
        }
    }

    // ---- adoption ----------------------------------------------------------------------
    function adopt(scope: ParentNode): void {
        const each = (selector: string, fn: (element: HTMLElement) => void): void => {
            if (scope instanceof Element && scope.matches(selector)) fn(scope as HTMLElement);
            scope.querySelectorAll<HTMLElement>(selector).forEach(fn);
        };

        recipe.forEach(([selector, kind, value]) => {
            if (kind === 'parallax') {
                each(selector, hero => {
                    if (!parallaxAdopted && hero.getBoundingClientRect().top + window.scrollY < window.innerHeight * 1.2) {
                        hero.setAttribute('data-lwc-parallax', '');
                        parallaxAdopted = true;
                    }
                });
                return;
            }
            each(selector, element => {
                const attribute = 'data-lwc-' + kind;
                if (element.hasAttribute(attribute)) return;
                element.setAttribute(attribute, value ?? '');
            });
        });

        each('[data-lwc-handoff]', adoptHandoff);
        each('[data-lwc-enter]', hold);
        each('[data-lwc-stagger]', container => staggerItems(container).forEach(hold));
        each('[data-lwc-count-in]', watchCount);
    }

    let parallaxAdopted = !!document.querySelector('[data-lwc-parallax]');

    function start(): void {
        if ('IntersectionObserver' in window) {
            observer = new IntersectionObserver(entries => {
                // Items arriving together cascade top-to-bottom, then left-to-right.
                const arriving = entries
                    .filter(entry => entry.isIntersecting)
                    .sort((a, b) => {
                        const ra = a.boundingClientRect;
                        const rb = b.boundingClientRect;
                        return Math.abs(ra.top - rb.top) > 8 ? ra.top - rb.top : ra.left - rb.left;
                    });
                pace = arriving.length > 5 ? 0.5 : 1;
                arriving.forEach((entry, index) => release(entry.target as HTMLElement, index));
            }, { rootMargin: '0px 0px 22px 0px', threshold: 0 });

            countObserver = new IntersectionObserver(entries => {
                entries.forEach(entry => {
                    if (!entry.isIntersecting || !pendingCounts.has(entry.target as HTMLElement)) return;
                    countObserver?.unobserve(entry.target);
                    // Let the figure's own card land first, then roll the digits.
                    const figure = entry.target as HTMLElement;
                    window.setTimeout(() => {
                        if (pendingCounts.delete(figure) && !reduce.matches && figure.isConnected) window.LwcCharts?.count(figure, 0);
                    }, 160);
                });
            }, { threshold: 0.6 });
        }

        adopt(document);

        // Late content (leaderboard rows, highlight cards, prize totals) joins the same vocabulary.
        new MutationObserver(records => {
            const changedLists = new Set<HTMLElement>();
            if (records.some(record => record.removedNodes.length)) {
                pending.forEach(element => { if (!element.isConnected) clearEntrance(element); });
                settling.forEach((_, element) => { if (!element.isConnected) clearEntrance(element); });
                pendingCounts.forEach(element => {
                    if (!element.isConnected) { countObserver?.unobserve(element); pendingCounts.delete(element); }
                });
                if (handoffs.size) queueHandoff();
            }
            for (const record of records) {
                record.addedNodes.forEach(node => {
                    if (!(node instanceof HTMLElement)) return;
                    adopt(node);
                    const container = node.parentElement?.closest<HTMLElement>('[data-lwc-stagger]');
                    if (container) changedLists.add(container);
                });
            }
            changedLists.forEach(container => staggerItems(container).forEach(hold));
        }).observe(document.body, { childList: true, subtree: true });

        // Never stand between a reader and the text: jumps, find-in-page, printing and a
        // switch to reduced motion all show everything at once.
        window.addEventListener('hashchange', releaseAll);
        window.addEventListener('beforeprint', releaseAll);
        document.addEventListener('beforematch', releaseAll, true);
        document.addEventListener('focusin', releaseAll, true);
        document.addEventListener('pointerdown', releaseAll, { capture: true, passive: true });
        reduce.addEventListener?.('change', () => { if (reduce.matches) releaseAll(); });
        window.addEventListener('pageshow', event => { if (event.persisted) releaseAll(); });
        window.addEventListener('pagehide', releaseAll);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
