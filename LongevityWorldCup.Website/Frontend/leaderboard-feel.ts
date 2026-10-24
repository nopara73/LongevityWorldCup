// Working the full leaderboard table by hand.
//
// - Column heads condense once they stick under the site header, and a hairline
//   under them tracks how far through the board you are.
// - The ordered column's underline glides from the previous column, and its
//   arrow grows in or turns over instead of swapping.
// - The ranking tabs share one ink bar that slides to the chosen tab.
// - Rows a search or filter removes fade out where they stood while the rest
//   close the gap; rows that arrive fade in one after another.
// - Resting the pointer on a row peeks at that athlete's key numbers.
// - On phones a rank rail on the edge shows where you are in the long list and
//   scrubs or jumps to rank anchors and the Pro/Amateur boundary.
// - Switching to and from By Flag crossfades the two views.
//
// Classic script for the full /leaderboard page only. It never replaces the
// page's own handlers: rows keep their identity, moving copies live in inert,
// pointer-transparent layers, and nothing waits on an animation. Motion follows
// the shared gate (first trusted press) and prefers-reduced-motion keeps all of
// it off; the peek and rail still work without motion.

(() => {
    const main = document.querySelector<HTMLElement>('main[data-leaderboard-page="full"]');
    const board = document.getElementById("athleteLeaderboard");
    const table = board?.querySelector<HTMLTableElement>(":scope > table");
    if (!main || !board || !table || !table.tHead || !table.tBodies[0]) return;
    const head = table.tHead;
    const body = table.tBodies[0];

    type RawAthlete = {
        readonly Name?: string;
        readonly DateOfBirth?: { readonly Year: number; readonly Month: number; readonly Day: number };
        readonly CrowdAge?: number | null;
        readonly CrowdCount?: number | null;
        readonly Placements?: readonly number[] | null;
        readonly ProfilePicLeaderboardThumb?: string;
    };

    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const phone = window.matchMedia("(max-width: 768px)");
    const finePointer = window.matchMedia("(hover: hover) and (pointer: fine)");
    const rowSelector = "tr[data-athlete-name]";

    function moving(): boolean {
        return !reduced.matches && root.dataset["lwcMotion"] === "on";
    }

    function token(name: string, fallback: number): number {
        const raw = getComputedStyle(root).getPropertyValue(name).trim();
        const value = parseFloat(raw) * (raw.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(value) && value >= 0 ? value : fallback;
    }

    function ease(name: string): string {
        return getComputedStyle(root).getPropertyValue(name).trim() || "ease";
    }

    function stickyTop(): number {
        const th = head.rows[0]?.cells[0];
        const fromHead = th ? parseFloat(getComputedStyle(th).top) : NaN;
        const fromHeadGroup = parseFloat(getComputedStyle(head).top);
        if (Number.isFinite(fromHead)) return fromHead;
        if (Number.isFinite(fromHeadGroup)) return fromHeadGroup;
        return parseFloat(getComputedStyle(root).getPropertyValue("--site-sticky-height")) || 52;
    }

    function shownRows(): HTMLTableRowElement[] {
        return Array.from(body.querySelectorAll<HTMLTableRowElement>(rowSelector))
            .filter(row => row.style.display !== "none" && row.getClientRects().length > 0);
    }

    // 1) Column heads: condense once stuck, with a progress hairline under them.
    const progress = document.createElement("div");
    progress.className = "lb-head-progress";
    progress.setAttribute("aria-hidden", "true");
    board.append(progress);
    let headFrame = 0;

    function syncHead(): void {
        headFrame = 0;
        const top = stickyTop();
        const box = table!.getBoundingClientRect();
        const headHeight = head.getBoundingClientRect().height;
        const stuck = box.top < top - 1 && box.bottom > top + headHeight * 2;
        if ((board!.dataset["headStuck"] === "true") !== stuck) board!.dataset["headStuck"] = String(stuck);
        if (!stuck) return;
        const travelled = top + headHeight - box.top;
        const span = Math.max(1, box.height - window.innerHeight + top + headHeight);
        const fraction = Math.min(1, Math.max(0, travelled / span));
        const headBox = head.getBoundingClientRect();
        progress.style.left = `${box.left}px`;
        progress.style.width = `${box.width}px`;
        progress.style.top = `${headBox.bottom - 2}px`;
        progress.style.setProperty("--lb-progress", fraction.toFixed(4));
    }

    function queueHead(): void {
        if (!headFrame) headFrame = requestAnimationFrame(syncHead);
    }

    window.addEventListener("scroll", queueHead, { passive: true });
    window.addEventListener("resize", queueHead, { passive: true });
    queueHead();

    // 2) Ordered column: the underline glides from the previous column and the
    //    arrow grows in or turns over.
    let primary = head.querySelector<HTMLElement>("th[data-order-level='1']");
    let primaryBox = primary?.getBoundingClientRect() ?? null;
    let primaryDirection = primary?.dataset["orderDirection"] ?? "";

    function glideHead(): void {
        const next = head.querySelector<HTMLElement>("th[data-order-level='1']");
        const nextDirection = next?.dataset["orderDirection"] ?? "";
        const before = primaryBox;
        const was = primary;
        const wasDirection = primaryDirection;
        primary = next;
        primaryDirection = nextDirection;
        primaryBox = next?.getBoundingClientRect() ?? null;
        if (!moving() || !next || !primaryBox) return;

        const arrow = next.querySelector<SVGElement>(".order-indicator svg");
        if (was !== next) {
            if (before && was && was.isConnected) {
                const dx = before.left - primaryBox.left;
                const sx = before.width / Math.max(1, primaryBox.width);
                try {
                    next.animate(
                        [{ transform: `translateX(${dx}px) scaleX(${sx})` }, { transform: "none" }],
                        { duration: token("--lwc-motion-move", 260), easing: ease("--lwc-ease-standard"), pseudoElement: "::after" });
                } catch {
                    // Older engines without pseudo-element animation simply show the underline.
                }
            }
            arrow?.animate(
                [{ opacity: 0, scale: "0.2", translate: "0 -4px" }, { opacity: 1, scale: "1.15", offset: 0.6 }, { opacity: 1, scale: "1", translate: "0 0" }],
                { duration: token("--lwc-motion-slow", 280), easing: ease("--lwc-ease-standard") });
        } else if (wasDirection && wasDirection !== nextDirection && arrow) {
            arrow.animate(
                [{ scale: "1" }, { scale: "1.25", offset: 0.45 }, { scale: "1" }],
                { duration: token("--lwc-motion-slow", 280), easing: ease("--lwc-ease-standard"), composite: "add" });
        }
    }

    new MutationObserver(glideHead).observe(head, { subtree: true, attributes: true, attributeFilter: ["data-order-level", "data-order-direction"] });

    // 3) Ranking tabs: one ink bar slides between them.
    const chips = main.querySelector<HTMLElement>(".leaderboard-view-switcher .view-switcher-chips");
    const ink = document.createElement("span");
    ink.className = "lb-tab-ink";
    ink.setAttribute("aria-hidden", "true");
    let inkPlaced = false;

    function placeInk(): void {
        if (!chips) return;
        const checked = chips.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked");
        const label = checked ? chips.querySelector<HTMLElement>(`label[for='${checked.id}']`) : null;
        if (!label || !label.getClientRects().length) {
            ink.style.opacity = "0";
            return;
        }
        const animate = inkPlaced && moving();
        ink.classList.toggle("is-gliding", animate);
        ink.style.opacity = "1";
        ink.style.width = `${label.offsetWidth}px`;
        ink.style.transform = `translate(${label.offsetLeft}px, ${label.offsetTop + label.offsetHeight - 3}px)`;
        inkPlaced = true;
    }

    if (chips) {
        chips.append(ink);
        chips.classList.add("has-ink");
        chips.addEventListener("change", () => requestAnimationFrame(placeInk));
        // Restored and URL-driven selections change checked without an event.
        new MutationObserver(() => requestAnimationFrame(placeInk)).observe(chips, { subtree: true, attributes: true, attributeFilter: ["checked", "class"] });
        if (typeof ResizeObserver === "function") new ResizeObserver(() => { inkPlaced = false; placeInk(); }).observe(chips);
        document.addEventListener("click", () => requestAnimationFrame(placeInk), true);
        placeInk();
    }

    // 4) Rows leaving and arriving. The shared motion controller already glides
    //    rows that stay; leaving rows get a fading copy, arriving rows a stagger.
    const ghosts = document.createElement("div");
    ghosts.className = "lb-ghosts";
    ghosts.setAttribute("aria-hidden", "true");
    ghosts.inert = true;
    board.append(ghosts);

    function ghostOf(row: HTMLTableRowElement, rowBox: DOMRect, cellWidths: readonly number[], boardBox: DOMRect): HTMLElement {
        const copy = row.cloneNode(true) as HTMLTableRowElement;
        for (const element of [copy, ...Array.from(copy.querySelectorAll<HTMLElement>("[id], [data-athlete-name]"))]) {
            element.removeAttribute("id");
            element.removeAttribute("data-athlete-name");
        }
        copy.querySelectorAll("a, button, [tabindex]").forEach(element => element.setAttribute("tabindex", "-1"));
        Array.from(copy.cells).forEach((cell, index) => {
            const width = cellWidths[index];
            if (width !== undefined) cell.style.width = `${width}px`;
        });
        const frame = document.createElement("table");
        frame.className = "lb-ghost";
        const group = document.createElement("tbody");
        group.append(copy);
        frame.append(group);
        frame.style.left = `${rowBox.left - boardBox.left}px`;
        frame.style.top = `${rowBox.top - boardBox.top}px`;
        frame.style.width = `${rowBox.width}px`;
        frame.style.height = `${rowBox.height}px`;
        return frame;
    }

    const shared = window.LwcMotion;
    if (shared) {
        const baseFlip = shared.flip;
        const flip = (elements: readonly Element[], mutate: () => void): void => {
            if (!moving() || !elements.some(element => element.parentElement === body)) {
                baseFlip(elements, mutate);
                return;
            }
            const viewTop = -40;
            const viewBottom = window.innerHeight + 40;
            const before = new Map<HTMLTableRowElement, { box: DOMRect; widths: number[] }>();
            for (const row of shownRows()) {
                const box = row.getBoundingClientRect();
                if (box.bottom < viewTop || box.top > viewBottom) continue;
                before.set(row, { box, widths: Array.from(row.cells).map(cell => cell.getBoundingClientRect().width) });
            }
            const boardBox = board.getBoundingClientRect();
            baseFlip(elements, mutate);

            const leaving: HTMLElement[] = [];
            for (const [row, { box, widths }] of before) {
                if (row.isConnected && row.style.display !== "none" && row.getClientRects().length) continue;
                if (leaving.length >= 24) break;
                leaving.push(ghostOf(row, box, widths, boardBox));
            }
            if (leaving.length) {
                ghosts.append(...leaving);
                const duration = token("--lwc-motion-base", 200);
                leaving.forEach((copy, index) => {
                    const animation = copy.animate(
                        [{ opacity: 1, transform: "none" }, { opacity: 0, transform: "translateX(-14px) scale(0.98)" }],
                        { duration, delay: Math.min(index * 12, 96), easing: ease("--lwc-ease-exit"), fill: "forwards" });
                    const drop = () => copy.remove();
                    void animation.finished.then(drop, drop);
                });
            }

            // Arriving rows: delay the shared fade so they land top to bottom.
            let order = 0;
            for (const row of shownRows()) {
                if (before.has(row)) continue;
                const box = row.getBoundingClientRect();
                if (box.bottom < 0 || box.top > window.innerHeight) continue;
                for (const animation of row.getAnimations()) {
                    if (typeof CSSTransition !== "undefined" && animation instanceof CSSTransition) continue;
                    animation.effect?.updateTiming({ delay: Math.min(order * 22, 176), fill: "backwards" });
                }
                order++;
            }
        };
        window.LwcMotion = Object.freeze({ ...shared, flip });
    }

    // 5) Peek: rest the pointer on a row to see that athlete's key numbers.
    const peek = document.createElement("div");
    peek.className = "lb-peek";
    peek.setAttribute("aria-hidden", "true");
    peek.hidden = true;
    document.body.append(peek);
    let athletesByName: Map<string, RawAthlete> | null = null;
    let peekTimer = 0;
    let peekRow: HTMLTableRowElement | null = null;

    const loadAthletes = (): void => {
        if (athletesByName) return;
        athletesByName = new Map();
        const source = (window as unknown as { getSharedAthletes?: () => Promise<readonly RawAthlete[]> }).getSharedAthletes;
        source?.().then(list => {
            for (const athlete of list) if (athlete.Name) athletesByName!.set(athlete.Name, athlete);
        }, () => { athletesByName = null; });
    };

    function yearsSince(date: { readonly Year: number; readonly Month: number; readonly Day: number }): number {
        const born = Date.UTC(date.Year, date.Month - 1, date.Day);
        return (Date.now() - born) / (365.2425 * 24 * 3600 * 1000);
    }

    function sparkline(placements: readonly number[]): SVGSVGElement | null {
        const points = placements.filter(value => Number.isFinite(value) && value > 0);
        if (points.length < 2) return null;
        const ns = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(ns, "svg");
        svg.setAttribute("viewBox", "0 0 120 32");
        svg.setAttribute("class", "lb-peek-spark");
        const worst = Math.max(...points);
        const best = Math.min(...points);
        const range = Math.max(1, worst - best);
        const coords = points.map((value, index) => [
            4 + (index / (points.length - 1)) * 112,
            4 + ((value - best) / range) * 24
        ] as const);
        const line = document.createElementNS(ns, "polyline");
        line.setAttribute("points", coords.map(([x, y]) => `${x.toFixed(1)},${y.toFixed(1)}`).join(" "));
        svg.append(line);
        const last = coords[coords.length - 1];
        if (last) {
            const dot = document.createElementNS(ns, "circle");
            dot.setAttribute("cx", last[0].toFixed(1));
            dot.setAttribute("cy", last[1].toFixed(1));
            dot.setAttribute("r", "3");
            svg.append(dot);
        }
        return svg;
    }

    function cell(className: string, ...children: (Node | string)[]): HTMLElement {
        const element = document.createElement("span");
        element.className = className;
        element.append(...children);
        return element;
    }

    function icon(name: string): HTMLElement {
        const element = document.createElement("i");
        element.className = `fas ${name}`;
        element.setAttribute("aria-hidden", "true");
        return element;
    }

    function fillPeek(row: HTMLTableRowElement): boolean {
        const name = row.dataset["athleteName"] || "";
        const athlete = athletesByName?.get(name);
        const rank = row.querySelector(".rank")?.textContent?.trim() || "";
        const metric = row.querySelector(".age-reduction")?.textContent?.trim() || "";
        const portrait = row.querySelector<HTMLImageElement>("img.portrait");
        const flag = row.querySelector<HTMLElement>(".badge-section [class*='flag'], .flag-icon");
        peek.replaceChildren();

        const top = cell("lb-peek-top");
        if (portrait?.currentSrc || portrait?.src) {
            const picture = document.createElement("img");
            picture.src = portrait.currentSrc || portrait.src;
            picture.alt = "";
            top.append(picture);
        }
        top.append(cell("lb-peek-id", cell("lb-peek-rank", rank), cell("lb-peek-name", name)));
        if (flag) top.append(flag.cloneNode(true));
        peek.append(top);
        if (metric) peek.append(cell("lb-peek-metric", metric));

        const facts = cell("lb-peek-facts");
        if (athlete?.DateOfBirth) {
            const age = yearsSince(athlete.DateOfBirth);
            const reduction = /^([+-−]?\d+(?:\.\d+)?)\s/.exec(metric.replace("−", "-"));
            const view = main!.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value;
            const change = reduction && view === "ultimate" ? parseFloat(reduction[1] ?? "") : NaN;
            const ages = cell("lb-peek-ages", icon("fa-hourglass-half"), cell("lb-peek-age", age.toFixed(1)));
            if (Number.isFinite(change)) {
                ages.append(icon("fa-arrow-right-long"), cell("lb-peek-age is-bio", (age + change).toFixed(1)));
            }
            facts.append(ages);
        }
        if (athlete && (athlete.CrowdCount ?? 0) > 0 && Number.isFinite(athlete.CrowdAge)) {
            facts.append(cell("lb-peek-crowd", icon("fa-people-group"), cell("lb-peek-age", String(Math.round(athlete.CrowdAge ?? 0)))));
        }
        if (facts.childNodes.length) peek.append(facts);
        const spark = athlete?.Placements ? sparkline(athlete.Placements) : null;
        if (spark) peek.append(spark);
        return Boolean(rank || metric);
    }

    function placePeek(row: HTMLTableRowElement): void {
        const anchor = row.querySelector<HTMLElement>(".athlete-identity, .athlete-td") ?? row;
        const box = anchor.getBoundingClientRect();
        const rowBox = row.getBoundingClientRect();
        const width = peek.offsetWidth;
        const height = peek.offsetHeight;
        const left = Math.min(window.innerWidth - width - 12, Math.max(12, box.left + Math.min(box.width, 220) + 12));
        const above = rowBox.top - height - 8;
        const top = above > stickyTop() + head.getBoundingClientRect().height ? above : rowBox.bottom + 8;
        peek.style.left = `${left}px`;
        peek.style.top = `${Math.min(window.innerHeight - height - 8, top)}px`;
    }

    function hidePeek(): void {
        window.clearTimeout(peekTimer);
        peekRow = null;
        if (peek.hidden) return;
        peek.classList.remove("is-open");
        if (moving()) {
            const animation = peek.animate([{ opacity: 1 }, { opacity: 0, translate: "0 4px" }], { duration: token("--lwc-motion-fast", 140), easing: ease("--lwc-ease-exit") });
            const done = () => { if (!peek.classList.contains("is-open")) peek.hidden = true; };
            void animation.finished.then(done, done);
        } else {
            peek.hidden = true;
        }
    }

    function showPeek(row: HTMLTableRowElement): void {
        if (!row.isConnected || row.style.display === "none" || !fillPeek(row)) return;
        peek.hidden = false;
        peek.classList.add("is-open");
        placePeek(row);
        if (moving()) {
            peek.animate(
                [{ opacity: 0, translate: "0 6px", scale: "0.97" }, { opacity: 1, translate: "0 0", scale: "1" }],
                { duration: token("--lwc-motion-base", 200), easing: ease("--lwc-ease-enter") });
        }
    }

    body.addEventListener("pointerover", event => {
        if (!finePointer.matches || event.pointerType !== "mouse" || !(event.target instanceof Element)) return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        if (!row || row === peekRow || !body.contains(row)) return;
        loadAthletes();
        hidePeek();
        peekRow = row;
        // Already peeking: move straight to the next row; otherwise wait for a rest.
        peekTimer = window.setTimeout(() => { if (peekRow === row) showPeek(row); }, 420);
    });
    body.addEventListener("pointerleave", hidePeek);
    body.addEventListener("pointerdown", hidePeek);
    window.addEventListener("scroll", () => { if (!peek.hidden || peekRow) hidePeek(); }, { passive: true });
    document.addEventListener("keydown", event => { if (event.key === "Escape") hidePeek(); });

    // 6) Phone rank rail: where you are in the long list, and a way to jump.
    const rail = document.createElement("div");
    rail.className = "lb-rail";
    rail.setAttribute("aria-hidden", "true");
    const track = document.createElement("div");
    track.className = "lb-rail-track";
    const thumb = document.createElement("span");
    thumb.className = "lb-rail-thumb";
    rail.append(track, thumb);
    document.body.append(rail);
    let railRows: HTMLTableRowElement[] = [];
    let railFrame = 0;
    let railHide = 0;
    let scrubbing = false;

    function rankOf(row: HTMLTableRowElement): string {
        return row.querySelector(".rank")?.textContent?.trim() || row.dataset["rank"] || "";
    }

    function buildRail(): void {
        railRows = shownRows();
        track.replaceChildren();
        const count = railRows.length;
        if (count < 30) return;
        const stops = [0, 0.25, 0.5, 0.75, 1].map(fraction => Math.min(count - 1, Math.round(fraction * (count - 1))));
        for (const index of new Set(stops)) {
            const row = railRows[index];
            if (!row) continue;
            const tick = document.createElement("button");
            tick.type = "button";
            tick.tabIndex = -1;
            tick.className = "lb-rail-tick";
            tick.textContent = rankOf(row);
            tick.style.top = `${(index / (count - 1)) * 100}%`;
            tick.dataset["index"] = String(index);
            track.append(tick);
        }
        const separator = body.querySelector<HTMLTableRowElement>("tr.tier-separator");
        if (separator && separator.style.display !== "none" && separator.getClientRects().length) {
            const after = railRows.findIndex(row => row.compareDocumentPosition(separator) & Node.DOCUMENT_POSITION_PRECEDING);
            if (after > 0) {
                const mark = document.createElement("button");
                mark.type = "button";
                mark.tabIndex = -1;
                mark.className = "lb-rail-tick is-section";
                mark.style.top = `${(after / (count - 1)) * 100}%`;
                mark.dataset["index"] = String(after);
                track.append(mark);
            }
        }
    }

    function railActive(): boolean {
        if (!phone.matches || board!.dataset["byFlag"] === "true") return false;
        const box = table!.getBoundingClientRect();
        return box.top < window.innerHeight * 0.5 && box.bottom > window.innerHeight * 0.6;
    }

    function syncRail(): void {
        railFrame = 0;
        if (!railActive()) {
            rail.classList.remove("is-visible");
            return;
        }
        if (!railRows.length || !railRows[0]?.isConnected) buildRail();
        if (railRows.length < 30) return;
        const line = stickyTop() + head.getBoundingClientRect().height + 8;
        let low = 0;
        let high = railRows.length - 1;
        while (low < high) {
            const mid = (low + high) >> 1;
            if ((railRows[mid]?.getBoundingClientRect().bottom ?? 0) < line) low = mid + 1;
            else high = mid;
        }
        const current = railRows[low];
        thumb.textContent = current ? rankOf(current) : "";
        thumb.style.top = `${(low / (railRows.length - 1)) * 100}%`;
        rail.classList.add("is-visible");
        window.clearTimeout(railHide);
        if (!scrubbing) railHide = window.setTimeout(() => rail.classList.remove("is-visible"), 1100);
    }

    function queueRail(): void {
        if (!railFrame) railFrame = requestAnimationFrame(syncRail);
    }

    function jumpTo(index: number, smooth: boolean): void {
        const row = railRows[Math.max(0, Math.min(railRows.length - 1, index))];
        if (!row) return;
        const offset = stickyTop() + head.getBoundingClientRect().height + 4;
        const y = row.getBoundingClientRect().top + window.scrollY - offset;
        window.scrollTo({ top: y, behavior: smooth && !reduced.matches ? "smooth" : "instant" });
    }

    function indexAt(clientY: number): number {
        const box = track.getBoundingClientRect();
        const fraction = Math.min(1, Math.max(0, (clientY - box.top) / Math.max(1, box.height)));
        return Math.round(fraction * (railRows.length - 1));
    }

    rail.addEventListener("pointerdown", event => {
        if (!(event.target instanceof Element)) return;
        const tick = event.target.closest<HTMLElement>(".lb-rail-tick");
        if (tick) {
            jumpTo(Number(tick.dataset["index"]), true);
            return;
        }
        scrubbing = true;
        rail.classList.add("is-scrubbing");
        rail.setPointerCapture(event.pointerId);
        jumpTo(indexAt(event.clientY), false);
        event.preventDefault();
    });
    rail.addEventListener("pointermove", event => {
        if (scrubbing) jumpTo(indexAt(event.clientY), false);
    });
    const endScrub = () => {
        if (!scrubbing) return;
        scrubbing = false;
        rail.classList.remove("is-scrubbing");
        queueRail();
    };
    rail.addEventListener("pointerup", endScrub);
    rail.addEventListener("pointercancel", endScrub);

    window.addEventListener("scroll", queueRail, { passive: true });
    new MutationObserver(() => { railRows = []; }).observe(body, { childList: true, subtree: false, attributes: true, attributeFilter: ["style"] });

    // 7) By Flag: the two views crossfade instead of swapping.
    const byFlag = document.getElementById("group-flags") as HTMLInputElement | null;
    const syncFlagState = () => { board!.dataset["byFlag"] = String(Boolean(byFlag?.checked)); };
    let replaying = false;
    byFlag?.addEventListener("change", syncFlagState);
    syncFlagState();
    byFlag?.addEventListener("click", event => {
        const start = (document as Document & { startViewTransition?: (update: () => void) => { finished: Promise<void> } }).startViewTransition;
        if (replaying || !moving()) return;
        if (typeof start !== "function") {
            requestAnimationFrame(() => window.LwcMotion?.settle(board!));
            return;
        }
        event.preventDefault();
        board!.classList.add("lb-crossfade");
        const transition = start.call(document, () => {
            replaying = true;
            try { byFlag.click(); } finally { replaying = false; }
        });
        const done = () => board!.classList.remove("lb-crossfade");
        void transition.finished.then(done, done);
    });
})();
