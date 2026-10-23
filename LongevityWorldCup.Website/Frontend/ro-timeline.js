(() => {
    const list = document.currentScript?.previousElementSibling;
    if (!list?.matches('.ro-milestones')) return;

    const items = [...list.children];
    // Date-only milestones include their entire UTC calendar day, independently
    // of the visitor's timezone. Keep the authored dates as the source of truth.
    const ends = items.map(item => Date.parse(item.querySelector('time')?.dateTime) + 864e5);
    if (!items.length || ends.some((end, i) => !Number.isFinite(end) || (i > 0 && end <= ends[i - 1]))) return;

    const labels = { past: 'Deadline passed.', next: 'Next deadline.', future: 'Upcoming deadline.' };
    let timer;
    const refresh = () => {
        clearTimeout(timer);
        const now = Date.now();
        if (!Number.isFinite(now)) return;
        const next = ends.findIndex(end => now < end);
        items.forEach((item, i) => {
            const state = next === -1 || i < next ? 'past' : i === next ? 'next' : 'future';
            if (item.dataset.roState !== state) {
                item.dataset.roState = state;
                item.setAttribute('aria-label', labels[state]);
                if (state === 'next') item.setAttribute('aria-current', 'step');
                else item.removeAttribute('aria-current');
            }
            const leg = i + 1 < ends.length ? (now - ends[i]) / (ends[i + 1] - ends[i]) : 0;
            item.style.setProperty('--ro-leg', String(Math.min(1, Math.max(0, leg))));
        });
        list.dataset.roLive = '';
        // Refresh progress while open, and meet the next deadline exactly.
        if (next !== -1) timer = setTimeout(refresh, Math.min(60000, ends[next] - now));
    };
    refresh();
    window.addEventListener('pageshow', refresh);
    document.addEventListener('visibilitychange', () => {
        if (!document.hidden) refresh();
    });
})();
