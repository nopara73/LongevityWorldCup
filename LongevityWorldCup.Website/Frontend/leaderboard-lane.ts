// Desktop leaderboard rows: one lane that glides between athletes.
//
// leaderboard-lane.css owns the look; this classic script only measures:
// - A single lane follows the pointer from row to row: it glides and
//   stretches to the next row instead of each row lighting on its own.
// - The lane carries a reach bar: how far this athlete's figure reaches
//   toward the leader of the current view (the first ranked row), drawn along
//   the lane's foot each time it settles on a new row.
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

    const figure = (row: Element | null): number => {
        const text = row?.querySelector(".age-reduction")?.textContent || "";
        const n = parseFloat(text.replace(/[^\d.,+\-−]/g, "").replace("−", "-").replace(",", "."));
        return Number.isFinite(n) ? Math.abs(n) : NaN;
    };
    const isAthleteRow = (row: Element | null): row is HTMLTableRowElement =>
        row instanceof HTMLTableRowElement && !row.classList.contains("tier-separator") && !!row.querySelector(".age-reduction") && row.getClientRects().length > 0;

    function leader(): number {
        const rows = board!.querySelectorAll("table tbody tr");
        for (const row of rows) if (isAthleteRow(row)) return figure(row);
        return NaN;
    }

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
        const top = leader();
        const mine = figure(row);
        const share = Number.isFinite(top) && top > 0 && Number.isFinite(mine) ? Math.max(0, Math.min(1, mine / top)) : 0;
        if (row !== current) {
            current?.classList.remove("is-laned");
            row.classList.add("is-laned");
            current = row;
            // The reach redraws from zero for each athlete.
            reach.style.setProperty("--reach", "0");
            void reach.offsetWidth;
        }
        reach.style.setProperty("--reach", share.toFixed(3));
        lane.classList.add("is-on");
        void lane.offsetWidth;
        lane.classList.remove("is-snapping");
    }

    board.addEventListener("pointermove", event => {
        if (event.pointerType !== "mouse" || !fine.matches) return;
        const row = (event.target as Element | null)?.closest?.("tbody tr") ?? null;
        if (frame) cancelAnimationFrame(frame);
        frame = requestAnimationFrame(() => {
            frame = 0;
            if (isAthleteRow(row)) { if (row !== current) place(row); }
            else if (!(event.target as Element | null)?.closest?.("table")) place(null);
        });
    });
    board.addEventListener("pointerleave", () => place(null));
    // Filtering, re-sorting and scrolling move rows under a resting pointer.
    const refresh = (): void => { if (current?.isConnected) place(current, true); else place(null); };
    window.addEventListener("scroll", () => { if (current) requestAnimationFrame(refresh); }, { passive: true });
    window.addEventListener("resize", refresh, { passive: true });
    new MutationObserver(() => { if (current) requestAnimationFrame(refresh); })
        .observe(board, { childList: true, subtree: true });
})();
