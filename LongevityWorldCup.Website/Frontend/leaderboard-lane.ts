// Desktop leaderboard rows: one lane that glides between athletes.
//
// leaderboard-lane.css owns the look; this classic script only measures:
// - A single lane follows the pointer (or keyboard focus) from row to row: it
//   glides and stretches to the next row instead of each row lighting on its own.
// - The lane carries a reach bar: how far this athlete's figure reaches toward
//   the leading figure of the current view (the best figure in the field, not
//   the first row; a search narrows the rows, not the field). It shows its
//   final length at once, so moving between rows compares the figures;
//   nothing fills from zero.
//
// Desktop pointers only; touch keeps the existing row press. Nothing here
// changes wording, order or what a row does. Reduced motion keeps the lane
// and the reach and drops the glide.

(() => {
    const board = document.getElementById("athleteLeaderboard");
    const table = board?.querySelector<HTMLTableElement>(":scope > table");
    if (!board || !table || typeof window.matchMedia !== "function") return;
    const fine = window.matchMedia("(hover: hover) and (pointer: fine) and (min-width: 769px)");
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    const lane = document.createElement("div");
    lane.className = "lb-lane";
    lane.setAttribute("aria-hidden", "true");
    lane.innerHTML = '<span class="lb-lane-reach"></span>';
    // Before the table, so a focused control in the row can sit above the lane.
    table.before(lane);
    const reach = lane.querySelector<HTMLElement>(".lb-lane-reach")!;
    document.documentElement.dataset.lbLane = "";

    // leaderboard-page.js keeps the view's unrounded figures (years; more
    // negative is better) and the field's leading figure.
    type Figure = { readonly figure: number; readonly leading: number | null };
    const figureOf = (row: HTMLTableRowElement): Figure | null =>
        (window as unknown as { getLeaderboardFigure?: (name: string) => Figure | null }).getLeaderboardFigure?.(row.dataset.athleteName || "") ?? null;
    // A standings row on screen whose athlete is in the current field; not a
    // separator, a loading placeholder or a row from before the standings load.
    const isAthleteRow = (row: Element | null): row is HTMLTableRowElement =>
        row instanceof HTMLTableRowElement && row.hasAttribute("data-athlete-name") && row.getClientRects().length > 0 && figureOf(row) !== null;
    const shareOf = (row: HTMLTableRowElement): number => {
        const value = figureOf(row);
        const mine = value?.figure ?? NaN;
        const top = value?.leading ?? NaN;
        // Only reductions reach toward the leader; a figure of zero or above, or none, has no reach.
        if (!(top < 0) || !(mine < 0)) return 0;
        return Math.min(1, mine / top);
    };
    const focusedRow = (): HTMLTableRowElement | null => {
        const active = document.activeElement;
        const row = active && board.contains(active) && active.matches(":focus-visible") ? active.closest("tbody tr") : null;
        return isAthleteRow(row) ? row : null;
    };
    // Layout offsets from the board's padding edge, where the lane is
    // positioned. Unlike rects they ignore a row's entrance or hover
    // transform and page scroll.
    const offsetIn = (element: HTMLElement, edge: "offsetTop" | "offsetLeft"): number => {
        let sum = 0;
        for (let node: Element | null = element; node instanceof HTMLElement && node !== board; node = node.offsetParent) sum += node[edge];
        return sum;
    };

    let current: HTMLTableRowElement | null = null;
    let frame = 0;
    function place(row: HTMLTableRowElement | null, instant = false): void {
        if (!row) { lane.classList.remove("is-on"); current?.classList.remove("is-laned"); current = null; return; }
        const rowTable = row.closest("table")!;
        const snap = instant || !lane.classList.contains("is-on") || reduced.matches;
        if (snap) lane.classList.add("is-snapping");
        lane.style.setProperty("--lane-x", `${offsetIn(rowTable, "offsetLeft")}px`);
        lane.style.setProperty("--lane-w", `${rowTable.offsetWidth}px`);
        lane.style.setProperty("--lane-y", `${offsetIn(row, "offsetTop")}px`);
        lane.style.setProperty("--lane-h", `${row.offsetHeight}px`);
        if (row !== current) {
            current?.classList.remove("is-laned");
            row.classList.add("is-laned");
            current = row;
        }
        reach.style.setProperty("--reach", shareOf(row).toFixed(3));
        lane.classList.add("is-on");
        // Commit a snap before the glide returns; a glide needs no extra style pass.
        if (snap) { void lane.offsetWidth; lane.classList.remove("is-snapping"); }
    }
    const cancelFrame = (): void => { if (frame) cancelAnimationFrame(frame); frame = 0; };

    // pointerover also fires when a scroll moves another row under a resting
    // mouse, so the lane stays on the row the pointer is over.
    board.addEventListener("pointerover", event => {
        if (event.pointerType !== "mouse" || !fine.matches) return;
        const target = event.target as Element | null;
        const row = target?.closest?.("tbody tr") ?? null;
        cancelFrame();
        frame = requestAnimationFrame(() => {
            frame = 0;
            if (isAthleteRow(row)) { if (row !== current) place(row); }
            else if (!target?.closest?.("table")) place(focusedRow());
        });
    });
    // A move queued just before leaving must not bring the lane back.
    board.addEventListener("pointerleave", () => { cancelFrame(); place(focusedRow()); });

    // Keyboard: the lane follows focus within a row, as the pointer does.
    board.addEventListener("focusin", event => {
        if (!fine.matches) return;
        const target = event.target as Element | null;
        if (!target?.matches?.(":focus-visible")) return;
        const row = target.closest("tbody tr");
        if (isAthleteRow(row)) place(row);
    });
    board.addEventListener("focusout", () => requestAnimationFrame(() => {
        if (!board.contains(document.activeElement) && !board.matches(":hover")) place(null);
    }));

    // Filtering, re-sorting, hydration and reflow move rows or change the
    // figures: put the lane on the row under the mouse or with focus, at once.
    let refreshFrame = 0;
    const refresh = (): void => {
        if (!current || refreshFrame) return;
        refreshFrame = requestAnimationFrame(() => {
            refreshFrame = 0;
            if (!current) return;
            const hovered = board.querySelector("tbody tr[data-athlete-name]:hover");
            place(fine.matches ? (isAthleteRow(hovered) ? hovered : focusedRow()) : null, true);
        });
    };
    document.addEventListener("lwc:standings-ready", refresh);
    const body = table.tBodies[0];
    if (body && typeof MutationObserver === "function") new MutationObserver(refresh).observe(body, { childList: true });
    if (typeof ResizeObserver === "function") new ResizeObserver(refresh).observe(board);
    else window.addEventListener("resize", refresh, { passive: true });
    fine.addEventListener?.("change", refresh);
})();
