// Sticky depth is presentation only: batch geometry reads before attribute writes.
(() => {
    const selectors = [
        '[data-lwc-sticky]', '.event-board-page .events-month-row',
        '.lmx-board-row.header', '.lmx-dashboard-category', '.lmx-feed-updates', '.sidebar-heading',
        ':is(#phenoAgeForm, #bortzAgeForm) :is(#lwc-step-1, #lwc-step-2) > fieldset > legend'
    ].join(',');
    type Bar = { element: HTMLElement; top: number | null; scroller: HTMLElement | null };
    const bars = new Map<HTMLElement, Bar>();
    let queued = false;
    let refresh = true;

    function adopt(root: Element | Document): void {
        const add = (element: Element): void => {
            if (!(element instanceof HTMLElement) || bars.has(element)) return;
            bars.set(element, { element, top: null, scroller: null });
            element.setAttribute('data-lwc-sticky', '');
            refresh = true;
        };
        if (root instanceof Element && root.matches(selectors)) add(root);
        root.querySelectorAll(selectors).forEach(add);
    }
    function measure(bar: Bar): void {
        const style = getComputedStyle(bar.element);
        const top = parseFloat(style.top);
        bar.top = style.position === 'sticky' && Number.isFinite(top) ? top : null;
        bar.scroller = null;
        if (bar.top === null) return;
        for (let parent = bar.element.parentElement; parent && parent !== document.body; parent = parent.parentElement) {
            // hidden establishes a sticky scroll container; clip does not.
            if (/^(auto|scroll|hidden|overlay)$/.test(getComputedStyle(parent).overflowY)) {
                bar.scroller = parent;
                break;
            }
        }
    }
    function update(): void {
        queued = false;
        const changes: [HTMLElement, boolean][] = [];
        const containers = new Map<HTMLElement, { top: number; scrolled: boolean }>();
        for (const [element, bar] of bars) {
            if (!element.isConnected) { bars.delete(element); continue; }
            if (refresh) measure(bar);
            let stuck = false;
            if (bar.top !== null && element.getClientRects().length) {
                let container = { top: 0, scrolled: window.scrollY > 0 };
                if (bar.scroller) {
                    const scroller = bar.scroller;
                    let cached = containers.get(scroller);
                    if (!cached) {
                        cached = { top: scroller.getBoundingClientRect().top + scroller.clientTop, scrolled: scroller.scrollTop > 0 };
                        containers.set(scroller, cached);
                    }
                    container = cached;
                }
                stuck = container.scrolled && Math.abs(element.getBoundingClientRect().top - container.top - bar.top) < 1.5;
            }
            if (stuck !== element.hasAttribute('data-lwc-stuck')) changes.push([element, stuck]);
        }
        refresh = false;
        for (const [element, stuck] of changes) element.toggleAttribute('data-lwc-stuck', stuck);
    }
    function queue(): void {
        if (!queued) { queued = true; requestAnimationFrame(update); }
    }
    function invalidate(): void { refresh = true; queue(); }
    function start(): void {
        adopt(document);
        queue();
        document.addEventListener('scroll', queue, { capture: true, passive: true });
        window.addEventListener('resize', invalidate, { passive: true });
        window.addEventListener('pageshow', invalidate);
        document.fonts?.ready.then(invalidate);
        // Inspect inserted subtrees, not the entire populated page after every
        // search, timer, tooltip or profile mutation.
        new MutationObserver(records => {
            for (const record of records) {
                for (const node of record.addedNodes) if (node instanceof Element) adopt(node);
            }
            queue();
        }).observe(document.body, { childList: true, subtree: true });
        new ResizeObserver(invalidate).observe(document.documentElement);
        // Header measurements and enlarged text can change inherited sticky
        // insets without changing the dimensions of a bar itself.
        const root = document.documentElement;
        const rootMetrics = (): string => [root.style.getPropertyValue('--site-sticky-height'),
            root.style.getPropertyValue('--site-visible-sticky-height'), root.style.fontSize, root.className].join('|');
        let previousMetrics = rootMetrics();
        new MutationObserver(() => {
            const current = rootMetrics();
            if (current !== previousMetrics) { previousMetrics = current; invalidate(); }
        }).observe(root, { attributes: true, attributeFilter: ['style', 'class'] });
    }
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
