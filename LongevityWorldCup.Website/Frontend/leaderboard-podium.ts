// Rank-change cues compare only the unfiltered Ultimate standings with the
// same historical ranking. The existing homepage podium remains the only podium.
function initializeLeaderboardPodium(): void {
    const body = document.querySelector<HTMLTableSectionElement>('.leaderboard table tbody');
    if (!body) return;
    const reduced = matchMedia('(prefers-reduced-motion: reduce)');
    const rolls = new Map<HTMLElement, number>();
    const cued = new Set<string>();
    type Standing = { rank: number; lastMonthRank: number | null; isNew: boolean; comparable: boolean; flag: string };
    const standingOf = (name: string): Standing | null =>
        (window as unknown as { getLeaderboardStanding?: (name: string) => Standing | null }).getLeaderboardStanding?.(name) ?? null;
    const moving = () => window.LwcMotion?.enabled() === true && !reduced.matches;
    const rows = () => [...body.querySelectorAll<HTMLTableRowElement>(':scope > tr[data-athlete-name]')];

    function finishRoll(rank: HTMLElement): void {
        clearTimeout(rolls.get(rank));
        rolls.delete(rank);
        rank.classList.remove('lb-rolling', 'is-up', 'is-down');
        delete rank.dataset['was'];
        delete rank.dataset['now'];
    }
    function movement(row: HTMLTableRowElement): { from: number; to: number } | null {
        const standing = standingOf(row.dataset['athleteName'] ?? '');
        if (!standing?.comparable || standing.isNew || !Number.isInteger(standing.lastMonthRank)
            || standing.lastMonthRank! < 1 || standing.lastMonthRank === standing.rank
            || row.style.display === 'none' || row.querySelector('.rank')?.textContent?.trim() !== String(standing.rank)) return null;
        return { from: standing.lastMonthRank!, to: standing.rank };
    }
    const watcher = typeof IntersectionObserver === 'function' ? new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting || !moving()) continue;
            const row = entry.target as HTMLTableRowElement;
            const move = movement(row);
            const name = row.dataset['athleteName'] ?? '';
            if (!move || cued.has(name)) continue;
            watcher?.unobserve(row);
            cued.add(name);
            const rank = row.querySelector<HTMLElement>('.rank');
            if (!rank) continue;
            // Keep the existing chip/column geometry. A wider historical number
            // retains its direction mark instead of rolling through a clipped slot.
            const style = getComputedStyle(rank);
            const measure = document.createElement('canvas').getContext('2d');
            if (!measure) continue;
            measure.font = `${style.fontWeight} ${style.fontSize} ${style.fontFamily}`;
            const oldText = String(move.from);
            const spacing = parseFloat(style.letterSpacing) || 0;
            if (measure.measureText(oldText).width + spacing * oldText.length > rank.getBoundingClientRect().width) continue;
            rank.dataset['was'] = String(move.from);
            rank.dataset['now'] = String(move.to);
            rank.classList.add('lb-rolling', move.from > move.to ? 'is-up' : 'is-down');
            rolls.set(rank, window.setTimeout(() => finishRoll(rank), 800));
        }
    }, { threshold: .6 }) : null;
    function syncMoves(): void {
        watcher?.disconnect();
        for (const rank of rolls.keys()) finishRoll(rank);
        for (const row of rows()) {
            const move = movement(row);
            if (move) row.dataset['lbMoved'] = move.from > move.to ? 'up' : 'down';
            else delete row.dataset['lbMoved'];
            if (move && moving() && !cued.has(row.dataset['athleteName'] ?? '')) watcher?.observe(row);
        }
    }
    document.addEventListener('lwc:standings-ready', syncMoves);
    const gate = new MutationObserver(() => { syncMoves(); });
    gate.observe(document.documentElement, { attributes: true, attributeFilter: ['data-lwc-motion'] });
    reduced.addEventListener('change', () => { syncMoves(); });
    window.addEventListener('pagehide', () => {
        watcher?.disconnect();
        for (const rank of rolls.keys()) finishRoll(rank);
    });
    window.addEventListener('pageshow', syncMoves);
    // Hydration emits again after the selected rows and ranks are committed.
    syncMoves();
}
if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initializeLeaderboardPodium, { once: true });
else initializeLeaderboardPodium();
