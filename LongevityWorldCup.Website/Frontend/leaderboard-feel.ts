// Working the full leaderboard table by hand.
//
// - Column heads condense once they stick under the site header, and a hairline
//   under them tracks how far through the board you are.
// - The ordered column's underline glides from the previous column, and its
//   arrow grows in or turns over instead of swapping.
// - The ranking tabs share one ink bar that slides to the chosen tab.
// - Row changes retain readable names while the controller settles their new order.
// - Resting the pointer on a row peeks at that athlete's key numbers.
// - On phones a rank rail on the edge shows where you are in the long list and
//   scrubs or jumps to rank anchors and the Pro/Amateur boundary.
// - Switching By Flag updates immediately with a short settling cue.
//
// Classic script for the full /leaderboard page only. It never replaces the
// page's own handlers: rows keep their identity and nothing waits on an animation. Motion follows
// the shared gate (first trusted press) and prefers-reduced-motion keeps all of
// it off; the peek and rail still work without motion.

function initializeLeaderboardFeel(): void {
    const main = document.querySelector<HTMLElement>('main[data-leaderboard-page="full"]');
    const board = document.getElementById("athleteLeaderboard");
    const table = board?.querySelector<HTMLTableElement>(":scope > table");
    if (!main || !board || !table || !table.tHead || !table.tBodies[0]) return;
    const head = table.tHead;
    const body = table.tBodies[0];
    const byFlag = document.getElementById("group-flags") as HTMLInputElement | null;

    type PeekAthlete = {
        readonly chronologicalAge: number;
        readonly lowestBortzAge: number | null;
        readonly chronoAtLowestBortzAge: number | null;
        readonly lowestPhenoAge: number;
        readonly chronoAtLowestPhenoAge: number;
        readonly crowdAge: number | null;
        readonly crowdCount: number;
        readonly placements: readonly (number | null)[];
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
        if ((board!.dataset["headStuck"] === "true") !== stuck) {
            // Folding the sponsor note must not move the rows beneath a pointer
            // or keyboard focus. Keep the head's flow height while its contents
            // condense; natural content growth can still enlarge the table row.
            head.style.height = stuck ? `${headHeight}px` : "";
            board!.dataset["headStuck"] = String(stuck);
        }
        if (!stuck) return;
        const travelled = top + headHeight - box.top;
        const span = Math.max(1, box.height - window.innerHeight + top + headHeight);
        const fraction = Math.min(1, Math.max(0, travelled / span));
        progress.style.left = `${box.left}px`;
        progress.style.width = `${box.width}px`;
        progress.style.top = `${top + headHeight - 2}px`;
        progress.style.setProperty("--lb-progress", fraction.toFixed(4));
    }

    function queueHead(): void {
        if (!headFrame) headFrame = requestAnimationFrame(syncHead);
    }

    window.addEventListener("scroll", queueHead, { passive: true });
    window.addEventListener("resize", () => {
        // Re-measure the expanded head at the new viewport/zoom in one frame.
        head.style.height = "";
        board.dataset["headStuck"] = "false";
        queueHead();
    }, { passive: true });
    queueHead();

    // 2) Ordered column: the underline glides from the previous column and the
    //    arrow grows in or turns over.
    let primary = head.querySelector<HTMLElement>("th[data-order-level='1']");
    let primaryBox = primary?.getBoundingClientRect() ?? null;
    let primaryDirection = primary?.dataset["orderDirection"] ?? "";

    function glideHead(): void {
        const next = head.querySelector<HTMLElement>("th[data-order-level='1']");
        const nextDirection = next?.dataset["orderDirection"] ?? "";
        const before = primary?.getBoundingClientRect() ?? null;
        const was = primary;
        const wasDirection = primaryDirection;
        primary = next;
        primaryDirection = nextDirection;
        primaryBox = next?.getBoundingClientRect() ?? null;
        if (!moving() || !next || !primaryBox || typeof next.animate !== "function") return;

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
        new MutationObserver(records => {
            if (records.some(record => record.target !== ink)) requestAnimationFrame(placeInk);
        }).observe(chips, { subtree: true, attributes: true, attributeFilter: ["checked", "class"] });
        if (typeof ResizeObserver === "function") new ResizeObserver(() => { inkPlaced = false; placeInk(); }).observe(chips);
        document.addEventListener("click", () => requestAnimationFrame(placeInk), true);
        window.addEventListener("pageshow", () => requestAnimationFrame(placeInk));
        window.addEventListener("popstate", () => requestAnimationFrame(placeInk));
        placeInk();
    }

    // Row motion is owned by the leaderboard controller on both board surfaces.

    // 5) Peek: rest the pointer on a row to see that athlete's key numbers.
    const peek = document.createElement("div");
    peek.className = "lb-peek";
    peek.id = "leaderboard-peek";
    peek.setAttribute("role", "tooltip");
    peek.hidden = true;
    document.body.append(peek);
    let peekTimer = 0;
    let peekRow: HTMLTableRowElement | null = null;
    let peekAnimation: Animation | null = null;

    function sparkline(placements: readonly (number | null)[], rank: number): SVGSVGElement | null {
        // API order is yesterday, week, month, year. Draw oldest to newest,
        // retaining missing observations instead of closing their gaps.
        const history = [...placements.slice(0, 4)].reverse().concat(rank);
        const points = history.flatMap((value, index) => value != null && Number.isFinite(value) && value > 0 ? [{ value, index }] : []);
        if (points.length < 2) return null;
        const ns = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(ns, "svg");
        svg.setAttribute("viewBox", "0 0 120 32");
        svg.setAttribute("class", "lb-peek-spark");
        svg.setAttribute("role", "img");
        svg.setAttribute("aria-label", "Ultimate rank history, oldest to newest: " + history.map(value => value ?? "unavailable").join(", "));
        const best = Math.min(...points.map(point => point.value));
        const range = Math.max(1, Math.max(...points.map(point => point.value)) - best);
        const line = document.createElementNS(ns, "polyline");
        line.setAttribute("points", points.map(point => `${4 + point.index * 28},${4 + (point.value - best) / range * 24}`).join(" "));
        svg.append(line);
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
        // Reuse the page's calculated clock values. Display metrics can be
        // percentages or improvements, and must never be parsed back into ages.
        const athlete = (window as unknown as { getLeaderboardPeek?: (name: string) => PeekAthlete | null }).getLeaderboardPeek?.(name);
        const rank = row.querySelector(".rank")?.textContent?.trim() || "";
        const metric = row.querySelector(".age-reduction")?.textContent?.trim() || "";
        const portrait = row.querySelector<HTMLImageElement>("img.portrait");
        peek.replaceChildren();

        const top = cell("lb-peek-top");
        if (portrait?.currentSrc || portrait?.src) {
            const picture = document.createElement("img");
            picture.src = portrait.currentSrc || portrait.src;
            picture.alt = "";
            top.append(picture);
        }
        top.append(cell("lb-peek-id", cell("lb-peek-rank", rank), cell("lb-peek-name", row.querySelector(".athlete-name")?.textContent?.trim() || name)));
        peek.append(top);
        if (metric) peek.append(cell("lb-peek-metric", metric));

        const facts = cell("lb-peek-facts");
        if (athlete) {
            const view = main!.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value;
            const bortz = view === "bortz" || (view === "ultimate" && athlete.lowestBortzAge != null);
            const clock = bortz || view === "pheno" || view === "ultimate";
            const age = clock ? (bortz ? athlete.chronoAtLowestBortzAge : athlete.chronoAtLowestPhenoAge) : athlete.chronologicalAge;
            const bio = clock ? (bortz ? athlete.lowestBortzAge : athlete.lowestPhenoAge) : null;
            if (age != null && Number.isFinite(age)) {
                const ages = cell("lb-peek-ages", icon("fa-hourglass-half"), cell("lb-peek-age", age.toFixed(1)));
                ages.setAttribute("aria-label", clock ? "Chronological age at test" : "Chronological age");
                if (bio != null && Number.isFinite(bio)) {
                    const value = cell("lb-peek-age is-bio", bio.toFixed(1));
                    value.setAttribute("aria-label", bortz ? "bortz age" : "Pheno age");
                    ages.append(icon("fa-arrow-right-long"), value);
                }
                facts.append(ages);
            }
        }
        if (athlete && athlete.crowdCount > 0 && athlete.crowdAge != null && Number.isFinite(athlete.crowdAge)) {
            const crowd = cell("lb-peek-crowd", icon("fa-people-group"), cell("lb-peek-age", athlete.crowdAge.toFixed(1)));
            crowd.setAttribute("aria-label", "Crowd age");
            facts.append(crowd);
        }
        if (facts.childNodes.length) peek.append(facts);
        if (athlete && main!.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value === "ultimate") {
            const spark = sparkline(athlete.placements, Number(row.dataset["rank"]));
            if (spark) peek.append(spark);
        }
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
        peek.style.top = `${Math.max(8, Math.min(window.innerHeight - height - 8, top))}px`;
    }

    function hidePeek(): void {
        window.clearTimeout(peekTimer);
        peekRow?.querySelector(".athlete-name")?.removeAttribute("aria-describedby");
        peekRow = null;
        peekAnimation?.cancel();
        peekAnimation = null;
        peek.classList.remove("is-open");
        peek.hidden = true;
    }

    function showPeek(row: HTMLTableRowElement): void {
        if (phone.matches || !row.isConnected || row.style.display === "none" || !fillPeek(row)) return;
        peek.hidden = false;
        peek.classList.add("is-open");
        peek.tabIndex = peek.scrollHeight > peek.clientHeight + 1 ? 0 : -1;
        peekRow = row;
        row.querySelector(".athlete-name")?.setAttribute("aria-describedby", peek.id);
        placePeek(row);
        if (moving() && typeof peek.animate === "function") {
            peekAnimation?.cancel();
            try {
                peekAnimation = peek.animate(
                    [{ opacity: 0, translate: "0 6px", scale: "0.97" }, { opacity: 1, translate: "0 0", scale: "1" }],
                    { duration: token("--lwc-motion-base", 200), easing: ease("--lwc-ease-enter") });
            } catch { peekAnimation = null; }
        }
    }

    body.addEventListener("pointerover", event => {
        if (!finePointer.matches || event.pointerType !== "mouse" || !(event.target instanceof Element)) return;
        // Badge explainers and other row links own their own hover surfaces.
        if (event.target.closest(".badge-class, .badge-section, .athlete-name-links, button")) { hidePeek(); return; }
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        if (!row || row === peekRow || !body.contains(row)) return;
        hidePeek();
        peekRow = row;
        // Already peeking: move straight to the next row; otherwise wait for a rest.
        peekTimer = window.setTimeout(() => { if (peekRow === row) showPeek(row); }, 420);
    });
    body.addEventListener("pointerleave", () => {
        window.clearTimeout(peekTimer);
        peekTimer = window.setTimeout(() => { if (!peek.matches(":hover")) hidePeek(); }, 160);
    });
    peek.addEventListener("pointerenter", () => window.clearTimeout(peekTimer));
    peek.addEventListener("pointerleave", hidePeek);
    body.addEventListener("focusin", event => {
        if (!(event.target instanceof Element) || !event.target.matches(".athlete-name:focus-visible")) return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        hidePeek();
        if (row) showPeek(row);
    });
    body.addEventListener("focusout", event => { if (!peek.contains(event.relatedTarget as Node | null)) hidePeek(); });
    peek.addEventListener("focusout", hidePeek);
    document.addEventListener("pointerdown", event => { if (!peek.contains(event.target as Node | null)) hidePeek(); }, true);
    window.addEventListener("resize", hidePeek);
    reduced.addEventListener("change", hidePeek);
    window.addEventListener("scroll", () => { if (!peek.hidden || peekRow) hidePeek(); }, { passive: true });
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        if (peek.contains(document.activeElement)) peekRow?.querySelector<HTMLElement>(".athlete-name")?.focus({ preventScroll: true });
        hidePeek();
    });

    // 6) Phone rank rail: where you are in the long list, and a way to jump.
    const rail = document.createElement("div");
    rail.className = "lb-rail";
    rail.setAttribute("aria-label", "Rank navigation");
    rail.setAttribute("role", "navigation");
    rail.inert = true;
    const track = document.createElement("div");
    track.className = "lb-rail-track";
    const thumb = document.createElement("span");
    thumb.className = "lb-rail-thumb";
    thumb.setAttribute("aria-hidden", "true");
    rail.append(track, thumb);
    document.body.append(rail);
    let railRows: HTMLTableRowElement[] = [];
    let railFrame = 0;
    let railHide = 0;
    let scrubbing = false;
    let railDirty = true;

    function hideRail(): void {
        window.clearTimeout(railHide);
        rail.classList.remove("is-visible", "is-scrubbing");
        rail.inert = true;
        scrubbing = false;
    }

    function rankOf(row: HTMLTableRowElement): string {
        return row.querySelector(".rank")?.textContent?.trim() || row.dataset["rank"] || "";
    }

    function buildRail(): void {
        railRows = shownRows();
        railDirty = false;
        track.replaceChildren();
        const count = railRows.length;
        const fits = phone.matches && window.innerWidth >= 360 && window.innerHeight >= 500
            && parseFloat(getComputedStyle(root).fontSize) <= 20 && !byFlag?.checked;
        board!.classList.toggle("has-rank-rail", count >= 30 && fits);
        if (count < 30 || !fits) { hideRail(); return; }
        const stops = [0, 0.25, 0.5, 0.75, 1].map(fraction => Math.min(count - 1, Math.round(fraction * (count - 1))));
        for (const index of new Set(stops)) {
            const row = railRows[index];
            if (!row) continue;
            const tick = document.createElement("button");
            tick.type = "button";
            tick.className = "lb-rail-tick";
            tick.textContent = rankOf(row);
            tick.setAttribute("aria-label", `Jump to rank ${rankOf(row)}`);
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
                mark.className = "lb-rail-tick is-section";
                mark.setAttribute("aria-label", `Jump to Amateur, rank ${rankOf(railRows[after]!)}`);
                mark.style.top = `${(after / (count - 1)) * 100}%`;
                mark.dataset["index"] = String(after);
                track.append(mark);
            }
        }
        // Keep boundary and rank targets distinct even when their ranks are
        // close. Scrubbing still uses the continuous row index scale.
        const ticks = Array.from(track.querySelectorAll<HTMLButtonElement>("button"))
            .sort((a, b) => Number(a.dataset["index"]) - Number(b.dataset["index"]));
        const positions = ticks.map(tick => Number(tick.dataset["index"]) / (count - 1) * track.clientHeight);
        for (let i = 1; i < positions.length; i++) positions[i] = Math.max(positions[i]!, positions[i - 1]! + 44);
        if (positions.length) positions[positions.length - 1] = Math.min(positions[positions.length - 1]!, track.clientHeight);
        for (let i = positions.length - 2; i >= 0; i--) positions[i] = Math.min(positions[i]!, positions[i + 1]! - 44);
        ticks.forEach((tick, i) => { tick.style.top = `${positions[i]}px`; track.append(tick); });
    }

    function railActive(): boolean {
        if (!board!.classList.contains("has-rank-rail") || byFlag?.checked
            || document.querySelector("dialog[open], .modal[style*='display: block'], .sidebar-overlay.active")) return false;
        const box = table!.getBoundingClientRect();
        return box.top < window.innerHeight * 0.5 && box.bottom > window.innerHeight * 0.6;
    }

    function syncRail(): void {
        railFrame = 0;
        if (railDirty || !railRows[0]?.isConnected) buildRail();
        if (!railActive()) {
            hideRail();
            return;
        }
        if (railRows.length < 30) { hideRail(); return; }
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
        thumb.style.top = `${12 + (low / (railRows.length - 1)) * track.clientHeight}px`;
        rail.classList.add("is-visible");
        rail.inert = false;
        window.clearTimeout(railHide);
        if (!scrubbing && !rail.contains(document.activeElement)) railHide = window.setTimeout(hideRail, 1600);
    }

    function queueRail(): void {
        if (!railFrame) railFrame = requestAnimationFrame(syncRail);
    }

    function jumpTo(index: number, smooth: boolean): void {
        if (railDirty) buildRail();
        const row = railRows[Math.max(0, Math.min(railRows.length - 1, index))];
        if (!row || !row.getClientRects().length) return;
        const offset = stickyTop() + head.getBoundingClientRect().height + 4;
        const y = row.getBoundingClientRect().top + window.scrollY - offset;
        window.scrollTo({ top: y, behavior: smooth && moving() ? "smooth" : "instant" });
    }

    function indexAt(clientY: number): number {
        const box = track.getBoundingClientRect();
        const fraction = Math.min(1, Math.max(0, (clientY - box.top) / Math.max(1, box.height)));
        return Math.round(fraction * (railRows.length - 1));
    }

    rail.addEventListener("pointerdown", event => {
        if (!(event.target instanceof Element)) return;
        const tick = event.target.closest<HTMLElement>(".lb-rail-tick");
        if (tick) return;
        scrubbing = true;
        rail.classList.add("is-scrubbing");
        rail.setPointerCapture(event.pointerId);
        jumpTo(indexAt(event.clientY), false);
        event.preventDefault();
    });
    rail.addEventListener("click", event => {
        const tick = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("button.lb-rail-tick") : null;
        if (tick) jumpTo(Number(tick.dataset["index"]), true);
    });
    rail.addEventListener("focusin", () => window.clearTimeout(railHide));
    rail.addEventListener("focusout", queueRail);
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
    rail.addEventListener("lostpointercapture", endScrub);

    window.addEventListener("scroll", queueRail, { passive: true });
    const invalidateRows = () => { railDirty = true; hidePeek(); queueRail(); queueHead(); requestAnimationFrame(placeInk); };
    new MutationObserver(invalidateRows).observe(body, { childList: true, subtree: true, attributes: true, attributeFilter: ["style", "hidden"], characterData: true });
    window.addEventListener("resize", invalidateRows, { passive: true });
    if (typeof ResizeObserver === "function") new ResizeObserver(invalidateRows).observe(table);
    queueRail();

    // By Flag applies its native change immediately. Keep feedback on the
    // resulting surface so a document snapshot cannot swallow the next click.
    let flagAnimation: Animation | null = null;
    const finishFlagMotion = () => { flagAnimation?.cancel(); flagAnimation = null; };
    reduced.addEventListener("change", finishFlagMotion);
    window.addEventListener("pagehide", finishFlagMotion);
    byFlag?.addEventListener("change", () => {
        invalidateRows();
        finishFlagMotion();
        if (moving() && typeof board.animate === "function") {
            flagAnimation = board.animate(
                [{ opacity: .85, translate: "0 4px" }, { opacity: 1, translate: "0 0" }],
                { duration: token("--lwc-motion-base", 200), easing: ease("--lwc-ease-enter") });
        }
    });
    invalidateRows();
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeLeaderboardFeel, { once: true });
} else {
    initializeLeaderboardFeel();
}
