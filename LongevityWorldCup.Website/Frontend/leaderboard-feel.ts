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
        readonly bortzRankResult: { readonly bio: number; readonly chrono: number } | null;
        readonly phenoRankResult: { readonly bio: number; readonly chrono: number } | null;
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
            const bortz = view === "bortz" || (view === "ultimate" && athlete.bortzRankResult != null);
            const clock = bortz || view === "pheno" || view === "ultimate";
            const result = bortz ? athlete.bortzRankResult : athlete.phenoRankResult;
            const age = clock ? result?.chrono : athlete.chronologicalAge;
            const bio = clock ? result?.bio : null;
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
    // While scrubbing, a balloon beside the thumb shows who is under it.
    const balloon = document.createElement("div");
    balloon.className = "lb-rail-balloon";
    balloon.setAttribute("aria-hidden", "true");
    const balloonPicture = document.createElement("img");
    balloonPicture.alt = "";
    balloonPicture.decoding = "async";
    const balloonRank = document.createElement("strong");
    const balloonName = document.createElement("span");
    balloon.append(balloonPicture, balloonRank, balloonName);
    rail.append(track, thumb, balloon);
    document.body.append(rail);
    let balloonRow: HTMLTableRowElement | null = null;
    let railRows: HTMLTableRowElement[] = [];
    let railFrame = 0;
    let railHide = 0;
    let scrubbing = false;
    let scrubIndex: number | null = null;
    let railDirty = true;
    let tickKey = "";
    let activePointer: number | null = null;
    let captureTarget: HTMLElement | null = null;
    let pointerStartY = 0;
    let suppressClick = false;
    let balloonAnimation: Animation | null = null;

    function finishBalloonMotion(): void {
        balloonAnimation?.cancel();
        balloonAnimation = null;
    }

    function endScrub(event?: PointerEvent): void {
        if (event && event.pointerId !== activePointer) return;
        const pointer = activePointer;
        activePointer = null;
        if (scrubbing || event?.type === "pointercancel") suppressClick = true;
        scrubbing = false;
        scrubIndex = null;
        rail.classList.remove("is-scrubbing");
        finishBalloonMotion();
        if (pointer !== null && captureTarget?.hasPointerCapture(pointer)) captureTarget.releasePointerCapture(pointer);
        captureTarget = null;
        queueRail();
    }

    function hideRail(): void {
        window.clearTimeout(railHide);
        if (activePointer !== null) endScrub();
        finishBalloonMotion();
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
        balloonRow = null;
        const count = railRows.length;
        const fits = phone.matches && window.innerWidth >= 360 && window.innerHeight >= 500
            && parseFloat(getComputedStyle(root).fontSize) <= 20 && !byFlag?.checked;
        board!.classList.toggle("has-rank-rail", count >= 30 && fits);
        if (count < 30 || !fits) { tickKey = ""; track.replaceChildren(); hideRail(); return; }
        const stops = [0, 0.25, 0.5, 0.75, 1].map(fraction => Math.min(count - 1, Math.round(fraction * (count - 1))));
        const marks = [...new Set(stops)].map(index => ({ index, section: false }));
        const separator = body.querySelector<HTMLTableRowElement>("tr.tier-separator");
        if (separator && separator.style.display !== "none" && separator.getClientRects().length) {
            const after = railRows.findIndex(row => row.compareDocumentPosition(separator) & Node.DOCUMENT_POSITION_PRECEDING);
            if (after > 0) {
                marks.push({ index: after, section: true });
            }
        }
        marks.sort((a, b) => a.index - b.index);
        const key = JSON.stringify(marks.map(mark => [mark.index, mark.section, rankOf(railRows[mark.index]!)]));
        // Resizing the viewport or sticky header must not detach a focused tick.
        if (key === tickKey) return;
        tickKey = key;
        track.replaceChildren(...marks.map(({ index, section }) => {
            const tick = document.createElement("button");
            tick.type = "button";
            tick.className = `lb-rail-tick${section ? " is-section" : ""}`;
            const rank = rankOf(railRows[index]!);
            tick.textContent = section ? "" : rank;
            tick.setAttribute("aria-label", section ? `Jump to Amateur, rank ${rank}` : `Jump to rank ${rank}`);
            tick.dataset["index"] = String(index);
            return tick;
        }));
    }

    function placeTicks(): boolean {
        // Keep boundary and rank targets distinct even when their ranks are
        // close. Scrubbing still uses the continuous row index scale.
        const ticks = Array.from(track.querySelectorAll<HTMLButtonElement>("button"));
        if (track.clientHeight < (ticks.length - 1) * 44) return false;
        const positions = ticks.map(tick => Number(tick.dataset["index"]) / (railRows.length - 1) * track.clientHeight);
        for (let i = 1; i < positions.length; i++) positions[i] = Math.max(positions[i]!, positions[i - 1]! + 44);
        if (positions.length) positions[positions.length - 1] = Math.min(positions[positions.length - 1]!, track.clientHeight);
        for (let i = positions.length - 2; i >= 0; i--) positions[i] = Math.min(positions[i]!, positions[i + 1]! - 44);
        ticks.forEach((tick, i) => { tick.style.top = `${positions[i]}px`; });
        return true;
    }

    function railActive(): boolean {
        if (!board!.classList.contains("has-rank-rail") || byFlag?.checked
            || document.querySelector("dialog[open], .modal[style*='display: block'], .sidebar-overlay.active")) return false;
        const box = table!.getBoundingClientRect();
        const headTop = head.getBoundingClientRect().top;
        // The last jump can reach the document's scroll limit before its row
        // reaches the header. Keep engaged navigation available to return up.
        const engaged = activePointer !== null || rail.contains(document.activeElement);
        const bottomLimit = engaged ? stickyTop() + head.getBoundingClientRect().height + 1 : window.innerHeight * 0.6;
        return headTop <= stickyTop() + 2 && box.top <= stickyTop() + 2 && box.bottom > bottomLimit;
    }

    function syncRail(): void {
        railFrame = 0;
        const line = stickyTop() + head.getBoundingClientRect().height + 8;
        // Measure ticks, thumb and pointer positions against the same geometry.
        rail.style.top = `${Math.round(line + 4)}px`;
        if (railDirty || !railRows[0]?.isConnected) buildRail();
        if (!railActive() || !placeTicks()) {
            hideRail();
            return;
        }
        if (railRows.length < 30) { hideRail(); return; }
        let low = 0;
        let high = railRows.length - 1;
        while (low < high) {
            const mid = (low + high) >> 1;
            if ((railRows[mid]?.getBoundingClientRect().bottom ?? 0) < line) low = mid + 1;
            else high = mid;
        }
        // At the document's end, the selected scrub row may not be able to
        // scroll all the way to the header. Keep its label under the pointer.
        const selected = scrubIndex === null ? low : Math.min(scrubIndex, railRows.length - 1);
        const current = railRows[selected];
        thumb.textContent = current ? rankOf(current) : "";
        const thumbTop = 12 + (selected / (railRows.length - 1)) * track.clientHeight;
        thumb.style.top = `${thumbTop}px`;
        if (current && current !== balloonRow) {
            balloonRow = current;
            const portrait = current.querySelector<HTMLImageElement>("img.portrait");
            const src = portrait?.currentSrc || portrait?.src || "";
            balloonPicture.hidden = !src;
            if (src && balloonPicture.src !== src) balloonPicture.src = src;
            balloonRank.textContent = rankOf(current);
            balloonName.textContent = current.dataset["athleteName"] || "";
            finishBalloonMotion();
            if (scrubbing && moving() && typeof balloonRank.animate === "function") {
                try {
                    balloonAnimation = balloonRank.animate([{ transform: "translateY(40%)", opacity: 0.2 }, { transform: "none", opacity: 1 }],
                        { duration: 140, easing: "cubic-bezier(0.2, 0, 0, 1)" });
                } catch { /* Optional motion must not interrupt rank navigation. */ }
            }
        }
        const halfBalloon = balloon.offsetHeight / 2;
        balloon.style.top = `${Math.max(halfBalloon, Math.min(rail.clientHeight - halfBalloon, thumbTop))}px`;
        rail.classList.add("is-visible");
        rail.inert = false;
        window.clearTimeout(railHide);
        if (activePointer === null && !rail.contains(document.activeElement)) railHide = window.setTimeout(hideRail, 1600);
    }

    function queueRail(): void {
        if (!railFrame) railFrame = requestAnimationFrame(syncRail);
    }

    function jumpTo(index: number, smooth: boolean): void {
        if (railDirty) buildRail();
        const row = railRows[Math.max(0, Math.min(railRows.length - 1, index))];
        if (!row || !row.getClientRects().length) return;
        if (scrubbing) scrubIndex = Math.max(0, Math.min(railRows.length - 1, index));
        const offset = stickyTop() + head.getBoundingClientRect().height + (index === 0 ? 0 : 4);
        const y = row.getBoundingClientRect().top + window.scrollY - offset;
        window.scrollTo({ top: y, behavior: smooth && moving() ? "smooth" : "instant" });
        queueRail();
    }

    function indexAt(clientY: number): number {
        const box = track.getBoundingClientRect();
        const fraction = Math.min(1, Math.max(0, (clientY - box.top) / Math.max(1, box.height)));
        return Math.round(fraction * (railRows.length - 1));
    }

    rail.addEventListener("pointerdown", event => {
        if (!(event.target instanceof Element) || event.button !== 0 || !event.isPrimary || activePointer !== null || rail.inert) return;
        const tick = event.target.closest<HTMLElement>(".lb-rail-tick");
        activePointer = event.pointerId;
        pointerStartY = event.clientY;
        suppressClick = false;
        captureTarget = tick || rail;
        captureTarget.setPointerCapture(event.pointerId);
        window.clearTimeout(railHide);
        if (tick) return; // Keep native tap, click and keyboard activation.
        scrubbing = true;
        rail.classList.add("is-scrubbing");
        jumpTo(indexAt(event.clientY), false);
        event.preventDefault();
    });
    rail.addEventListener("click", event => {
        if (suppressClick && event.detail !== 0) { event.preventDefault(); return; }
        const tick = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("button.lb-rail-tick") : null;
        if (tick) jumpTo(Number(tick.dataset["index"]), true);
    });
    rail.addEventListener("focusin", () => window.clearTimeout(railHide));
    rail.addEventListener("focusout", queueRail);
    rail.addEventListener("pointermove", event => {
        if (event.pointerId !== activePointer) return;
        if (!scrubbing && Math.abs(event.clientY - pointerStartY) >= 4) {
            scrubbing = true;
            rail.classList.add("is-scrubbing");
        }
        if (scrubbing) jumpTo(indexAt(event.clientY), false);
    });
    rail.addEventListener("pointerup", endScrub);
    rail.addEventListener("pointercancel", endScrub);
    rail.addEventListener("lostpointercapture", endScrub);

    window.addEventListener("scroll", queueRail, { passive: true });
    const invalidateRows = () => { railDirty = true; hidePeek(); queueRail(); queueHead(); requestAnimationFrame(placeInk); };
    new MutationObserver(invalidateRows).observe(body, { childList: true, subtree: true, attributes: true, attributeFilter: ["style", "hidden"], characterData: true });
    window.addEventListener("resize", () => { endScrub(); invalidateRows(); }, { passive: true });
    window.visualViewport?.addEventListener("resize", () => { endScrub(); queueRail(); }, { passive: true });
    reduced.addEventListener("change", finishBalloonMotion);
    window.addEventListener("pagehide", hideRail);
    if (typeof ResizeObserver === "function") {
        new ResizeObserver(invalidateRows).observe(table);
        const geometry = new ResizeObserver(queueRail);
        geometry.observe(head);
        geometry.observe(track);
    }
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

    // --- League masthead ---------------------------------------------------------
    // A league (a generation, division, flag or any other) is a place people land
    // on and share, so its name becomes the board's title, beside a small podium
    // of the league's top three faces. When the league changes, those faces rise
    // out of their rows into the podium and the title rolls in.
    const leagueTitle = document.getElementById("leaderboardLeagueTitle");
    const identity = leagueTitle?.closest<HTMLElement>(".leaderboard-identity");
    if (leagueTitle && identity) {
        const crest = document.createElement("span");
        crest.className = "league-crest";
        crest.setAttribute("aria-hidden", "true");
        identity.prepend(crest);
        let shownTitle = "";
        let pending = 0;
        const topPortraits = (): HTMLImageElement[] => Array.from(body.querySelectorAll<HTMLTableRowElement>("tr[data-athlete-name]"))
            .filter(row => row.getClientRects().length > 0)
            .slice(0, 3)
            .map(row => row.querySelector<HTMLImageElement>("img.portrait"))
            .filter((image): image is HTMLImageElement => !!image);
        const settleLeague = (): void => {
            pending = 0;
            const title = leagueTitle.hidden ? "" : (leagueTitle.textContent || "").trim();
            main!.toggleAttribute("data-league", !!title);
            if (!title) {
                crest.replaceChildren();
                delete crest.dataset["signature"];
                shownTitle = "";
                return;
            }
            const sources = topPortraits();
            const signature = sources.map(image => image.currentSrc || image.src).join("|");
            const changed = title !== shownTitle;
            if (!changed && crest.dataset["signature"] === signature) return;
            crest.dataset["signature"] = signature;
            const faces = sources.map((source, index) => {
                const face = document.createElement("img");
                face.src = source.currentSrc || source.src;
                face.alt = "";
                face.decoding = "async";
                face.className = `league-crest-face league-crest-face--${index + 1}`;
                return face;
            });
            crest.replaceChildren(...faces);
            if (!changed) return;
            shownTitle = title;
            if (!moving()) return;
            const viewport = document.documentElement.clientHeight;
            faces.forEach((face, index) => {
                const from = sources[index]!.getBoundingClientRect();
                const to = face.getBoundingClientRect();
                const inView = from.width > 0 && from.bottom > 0 && from.top < viewport;
                const start = inView && to.width
                    ? `translate(${from.left + from.width / 2 - (to.left + to.width / 2)}px, ${from.top + from.height / 2 - (to.top + to.height / 2)}px) scale(${from.width / to.width})`
                    : "translateY(10px) scale(.4)";
                // Third and second land first; the winner takes the middle last.
                const order = [2, 1, 0][index] ?? 0;
                face.animate([{ transform: start, opacity: inView ? 0.7 : 0 }, { transform: "none", opacity: 1 }],
                    { duration: 560, delay: order * 80, easing: "cubic-bezier(0.34, 1.32, 0.64, 1)", fill: "backwards" });
            });
            leagueTitle.animate([
                { opacity: 0, transform: "translateY(12px)", clipPath: "inset(0 0 100% 0)" },
                { opacity: 1, transform: "none", clipPath: "inset(0 0 0 0)" },
            ], { duration: 380, easing: ease("--lwc-ease-enter") });
        };
        const scheduleLeague = (): void => {
            if (pending) return;
            pending = requestAnimationFrame(() => { pending = requestAnimationFrame(settleLeague); });
        };
        new MutationObserver(scheduleLeague).observe(leagueTitle,
            { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
        new MutationObserver(scheduleLeague).observe(body, { childList: true });
        scheduleLeague();
    }
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeLeaderboardFeel, { once: true });
} else {
    initializeLeaderboardFeel();
}
