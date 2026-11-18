// Desktop leaderboard rows: one lane that glides between athletes.
//
// leaderboard-lane.css owns the look; this classic script only measures:
// - A single lane follows the pointer (or keyboard focus) from row to row: it
//   glides and stretches to the next row instead of each row lighting on its own.
// - The lane carries a reach bar: how far this athlete's figure reaches toward
//   the leading figure of the current view (the best figure on the board, not
//   the first row). It shows its final length at once, so moving between rows
//   compares the figures; nothing fills from zero.
//
// Desktop pointers only; touch keeps the existing row press. Nothing here
// changes wording, order or what a row does. Reduced motion keeps the lane
// and the reach and drops the glide.

(() => {
    const board = document.getElementById("athleteLeaderboard");
    if (!board) return;
    const fine = window.matchMedia("(hover: hover) and (pointer: fine) and (min-width: 769px)");
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    const lane = document.createElement("div");
    lane.className = "lb-lane";
    lane.setAttribute("aria-hidden", "true");
    lane.innerHTML = '<span class="lb-lane-reach"></span>';
    board.appendChild(lane);
    const reach = lane.querySelector<HTMLElement>(".lb-lane-reach")!;
    document.documentElement.dataset.lbLane = "";

    // The figure as printed, with its sign: an age reduction or improvement in
    // years, where more negative is better.
    const figure = (row: Element | null): number => {
        const text = row?.querySelector(".age-reduction")?.textContent || "";
        const n = parseFloat(text.replace(/[^\d.,+\-−]/g, "").replace("−", "-").replace(",", "."));
        return Number.isFinite(n) ? n : NaN;
    };
    const isAthleteRow = (row: Element | null): row is HTMLTableRowElement =>
        row instanceof HTMLTableRowElement && !row.classList.contains("tier-separator") && !!row.querySelector(".age-reduction") && row.getClientRects().length > 0;

    // The leading figure of the current view: the most negative figure on the
    // board, whatever the order or track the rows are shown in.
    function leader(): number {
        let best = NaN;
        for (const row of board!.querySelectorAll("table tbody tr")) {
            if (!isAthleteRow(row)) continue;
            const value = figure(row);
            if (Number.isFinite(value) && !(value >= best)) best = value;
        }
        return best;
    }
    const shareOf = (row: HTMLTableRowElement): number => {
        const top = leader();
        const mine = figure(row);
        // Only reductions reach toward the leader; a figure of zero or above has no reach.
        if (!(top < 0) || !(mine < 0)) return 0;
        return Math.max(0, Math.min(1, mine / top));
    };

    let current: HTMLTableRowElement | null = null;
    let frame = 0;
    function place(row: HTMLTableRowElement | null, instant = false): void {
        if (!row) { lane.classList.remove("is-on"); current?.classList.remove("is-laned"); current = null; return; }
        const br = board!.getBoundingClientRect();
        const rr = row.getBoundingClientRect();
        const table = row.closest("table")!.getBoundingClientRect();
        if (instant || !lane.classList.contains("is-on") || reduced.matches) lane.classList.add("is-snapping");
        lane.style.setProperty("--lane-x", `${table.left - br.left}px`);
        lane.style.setProperty("--lane-w", `${table.width}px`);
        lane.style.setProperty("--lane-y", `${rr.top - br.top}px`);
        lane.style.setProperty("--lane-h", `${rr.height}px`);
        if (row !== current) {
            current?.classList.remove("is-laned");
            row.classList.add("is-laned");
            current = row;
        }
        reach.style.setProperty("--reach", shareOf(row).toFixed(3));
        lane.classList.add("is-on");
        void lane.offsetWidth;
        lane.classList.remove("is-snapping");
    }
    const cancelFrame = (): void => { if (frame) cancelAnimationFrame(frame); frame = 0; };

    board.addEventListener("pointermove", event => {
        if (event.pointerType !== "mouse" || !fine.matches) return;
        const row = (event.target as Element | null)?.closest?.("tbody tr") ?? null;
        cancelFrame();
        frame = requestAnimationFrame(() => {
            frame = 0;
            if (isAthleteRow(row)) { if (row !== current) place(row); }
            else if (!(event.target as Element | null)?.closest?.("table")) place(null);
        });
    });
    // A move queued just before leaving must not bring the lane back.
    board.addEventListener("pointerleave", () => { cancelFrame(); place(null); });

    // Keyboard: the lane follows focus within a row, as the pointer does.
    board.addEventListener("focusin", event => {
        if (!fine.matches) return;
        const target = event.target as Element | null;
        if (!target?.matches?.(":focus-visible")) return;
        const row = target.closest("tbody tr");
        if (isAthleteRow(row)) place(row);
    });
    board.addEventListener("focusout", () => requestAnimationFrame(() => {
        if (!board!.contains(document.activeElement) && !board!.matches(":hover")) place(null);
    }));

    // Filtering, re-sorting and scrolling move rows under a resting pointer.
    const refresh = (): void => { if (current?.isConnected && isAthleteRow(current)) place(current, true); else place(null); };
    window.addEventListener("scroll", () => { if (current) requestAnimationFrame(refresh); }, { passive: true });
    window.addEventListener("resize", refresh, { passive: true });
    new MutationObserver(() => { if (current) requestAnimationFrame(refresh); })
        .observe(board, { childList: true, subtree: true });
})();
