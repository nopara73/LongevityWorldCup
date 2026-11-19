// Grids glide instead of jumping. When a shared grid changes its column
// count (the window crosses a breakpoint, a phone rotates) or its items are
// filtered in or out, every visible item slides from its old cell to its new
// one and new items fade in. Presentation only: no layout is written, and
// reduced motion turns it off.
(() => {
    const selectors = [
        '[data-lwc-reflow]', '.lwc-grid', '.lwc-grid--cards', '.lwc-grid--tiles',
        '.media-kit-tiles', '.media-kit-featured', '.lwc-merch-grid', '.homepage-faq-grid',
        '.lmx-habit-grid', '.badge-gallery', '.athlete-badges-grid'
    ].join(',');
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
    const maxItems = 160;

    type Box = { x: number; y: number };
    type Grid = { tracks: number; boxes: Map<Element, Box>; visible: number };
    const grids = new Map<HTMLElement, Grid>();
    const moving = new Map<Element, { animation: Animation; dx: number; dy: number }>();

    function duration(): number {
        const raw = getComputedStyle(document.documentElement).getPropertyValue('--lwc-reflow-duration').trim();
        const value = parseFloat(raw);
        return Number.isFinite(value) ? (raw.endsWith('ms') ? value : value * 1000) : 260;
    }
    function easing(): string {
        return getComputedStyle(document.documentElement).getPropertyValue('--lwc-reflow-ease').trim() || 'ease';
    }
    function tracks(grid: HTMLElement): number {
        const columns = getComputedStyle(grid).gridTemplateColumns;
        return columns && columns !== 'none' ? columns.split(' ').length : 1;
    }
    function shown(item: Element): boolean {
        return item instanceof HTMLElement && !item.hidden && item.getClientRects().length > 0;
    }
    // Layout coordinates exclude hover transforms and our own in-flight FLIP.
    // Reading a settled destination must not cancel an animation to measure it.
    function position(item: HTMLElement): Box {
        let x = 0;
        let y = 0;
        for (let parent: HTMLElement | null = item; parent; parent = parent.offsetParent as HTMLElement | null) {
            x += parent.offsetLeft;
            y += parent.offsetTop;
        }
        return { x, y };
    }
    function snapshot(grid: HTMLElement): Map<Element, Box> {
        const origin = position(grid);
        const boxes = new Map<Element, Box>();
        for (const item of Array.from(grid.children)) {
            if (!(item instanceof HTMLElement) || !shown(item)) continue;
            const box = position(item);
            boxes.set(item, { x: box.x - origin.x, y: box.y - origin.y });
        }
        return boxes;
    }
    function inView(box: DOMRect): boolean {
        return box.bottom > -200 && box.top < window.innerHeight + 200;
    }

    // Slide each surviving item from its previous cell; fade in newcomers.
    function play(grid: HTMLElement, before: Grid, after: Map<Element, Box>, entering: boolean): void {
        if (reduced.matches || after.size > maxItems) return;
        const origin = grid.getBoundingClientRect();
        if (!inView(origin)) return;
        const time = duration();
        const ease = easing();
        for (const [item, box] of after) {
            const previous = before.boxes.get(item);
            if (!previous) {
                if (entering && before.visible > 0 && item instanceof HTMLElement) {
                    item.setAttribute('data-lwc-reflow-enter', '');
                    const clearEnter = () => {
                        item.removeAttribute('data-lwc-reflow-enter');
                        item.removeEventListener('animationend', clearEnter);
                        item.removeEventListener('animationcancel', clearEnter);
                    };
                    item.addEventListener('animationend', clearEnter, { once: true });
                    item.addEventListener('animationcancel', clearEnter, { once: true });
                }
                continue;
            }
            let dx = previous.x - box.x;
            let dy = previous.y - box.y;
            if (Math.abs(dx) < 1 && Math.abs(dy) < 1) continue;
            if (!inView(item.getBoundingClientRect())) continue;
            const active = moving.get(item);
            if (active) {
                const remaining = 1 - (active.animation.effect?.getComputedTiming().progress ?? 1);
                dx += active.dx * remaining;
                dy += active.dy * remaining;
                active.animation.cancel();
            }
            const animation = item.animate(
                [{ transform: `translate(${dx}px, ${dy}px)` }, { transform: 'translate(0, 0)' }],
                { duration: time, easing: ease, composite: 'add' }
            );
            animation.id = 'lwc-reflow';
            moving.set(item, { animation, dx, dy });
            const forget = () => {
                if (moving.get(item)?.animation === animation) moving.delete(item);
            };
            animation.finished.then(forget, forget);
        }
    }

    function record(grid: HTMLElement): void {
        const boxes = snapshot(grid);
        grids.set(grid, { tracks: tracks(grid), boxes, visible: boxes.size });
    }

    // Column changes: compare against the last settled layout.
    const resize = new ResizeObserver((entries) => {
        for (const entry of entries) {
            const grid = entry.target as HTMLElement;
            const before = grids.get(grid);
            const count = tracks(grid);
            const after = snapshot(grid);
            if (before && before.tracks !== count) play(grid, before, after, false);
            grids.set(grid, { tracks: count, boxes: after, visible: after.size });
        }
    });

    // Filtering: items added, removed, hidden or shown.
    const mutations = new MutationObserver((records) => {
        const touched = new Set<HTMLElement>();
        for (const change of records) {
            const target = change.target as HTMLElement;
            if (change.type === 'childList' && grids.has(target)) touched.add(target);
            const parent = target.parentElement;
            if (change.type === 'attributes' && parent && grids.has(parent)) touched.add(parent);
        }
        for (const grid of touched) {
            const before = grids.get(grid);
            const after = snapshot(grid);
            if (before) play(grid, before, after, true);
            grids.set(grid, { tracks: tracks(grid), boxes: after, visible: after.size });
        }
    });

    function adopt(root: ParentNode): void {
        const found: HTMLElement[] = [];
        if (root instanceof HTMLElement && root.matches(selectors)) found.push(root);
        root.querySelectorAll<HTMLElement>(selectors).forEach((grid) => found.push(grid));
        for (const grid of found) {
            if (grids.has(grid)) continue;
            record(grid);
            resize.observe(grid);
            mutations.observe(grid, { childList: true, attributes: true, attributeFilter: ['hidden'], subtree: true });
        }
    }

    function start(): void {
        adopt(document);
        // Grids rendered later by page scripts join once they exist.
        new MutationObserver((records) => {
            for (const change of records) {
                change.addedNodes.forEach((node) => {
                    if (node instanceof HTMLElement) adopt(node);
                });
            }
            for (const grid of grids.keys()) {
                if (grid.isConnected) continue;
                resize.unobserve(grid);
                grids.delete(grid);
            }
            for (const [item, active] of moving) {
                if (!item.isConnected) active.animation.cancel();
            }
        }).observe(document.body, { childList: true, subtree: true });
    }

    reduced.addEventListener('change', () => {
        if (reduced.matches) for (const active of moving.values()) active.animation.cancel();
    });

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
    else start();
})();
