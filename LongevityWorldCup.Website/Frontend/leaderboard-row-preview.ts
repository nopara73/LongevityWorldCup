// The homepage and full standings share one preview for mouse and keyboard users.
function initializeLeaderboardRowPreview(main: HTMLElement, table: HTMLTableElement, body: HTMLTableSectionElement): void {
    type PeekAthlete = {
        readonly chronologicalAge: number;
        readonly bortzRankResult: { readonly bio: number; readonly chrono: number } | null;
        readonly phenoRankResult: { readonly bio: number; readonly chrono: number } | null;
        readonly crowdAge: number | null;
        readonly crowdCount: number;
        readonly placements: readonly (number | null)[];
    };

    const finePointer = window.matchMedia("(any-hover: hover) and (any-pointer: fine)");
    const rowSelector = "tr[data-athlete-name]";
    const peek = document.createElement("div");
    peek.className = "lb-peek";
    peek.id = "leaderboard-peek";
    peek.setAttribute("role", "tooltip");
    peek.hidden = true;
    const peekRegion = document.createElement("div");
    peekRegion.className = "lb-peek-region";
    peekRegion.append(peek);
    document.body.append(peekRegion);
    let peekTimer = 0;
    let peekRow: HTMLTableRowElement | null = null;
    let pendingPeekRow: HTMLTableRowElement | null = null;
    let pointerX = 0;
    let peekScrollX = 0;
    let peekScrollY = 0;

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
            const view = main.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value;
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
        if (athlete && main.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value === "ultimate") {
            const spark = sparkline(athlete.placements, Number(row.dataset["rank"]));
            if (spark) peek.append(spark);
        }
        return Boolean(rank || metric);
    }

    function placePeek(row: HTMLTableRowElement, clientX?: number): void {
        const rowBox = row.getBoundingClientRect();
        const x = clientX ?? (row.querySelector(".athlete-name") ?? row).getBoundingClientRect().right;
        // Leave the pointer's column clear to scan vertically in either direction.
        const roomRight = window.innerWidth - x - 24;
        const roomLeft = x - 24;
        peek.style.maxWidth = "";
        const preferred = peek.offsetWidth;
        const right = roomRight >= preferred || (roomLeft < preferred && roomRight >= roomLeft);
        const room = right ? roomRight : roomLeft;
        if (room < preferred) peek.style.maxWidth = `${Math.max(0, room)}px`;
        const left = right ? x + 12 : x - 12 - peek.offsetWidth;
        const height = peek.offsetHeight;
        const above = rowBox.top - height - 8;
        const header = table.tHead;
        const stickyTop = parseFloat(getComputedStyle(main).getPropertyValue("--site-sticky-height")) || 52;
        const below = above <= stickyTop + (header?.getBoundingClientRect().height ?? 0);
        const top = below ? rowBox.bottom + 8 : above;
        peekRegion.classList.toggle("to-right", right);
        peekRegion.classList.toggle("below", below);
        peekRegion.style.left = `${Math.max(12, Math.min(window.innerWidth - peek.offsetWidth - 12, left))}px`;
        peekRegion.style.top = `${Math.max(8, Math.min(window.innerHeight - height - 8, top))}px`;
    }

    function cancelPeekTimer(): void {
        window.clearTimeout(peekTimer);
        pendingPeekRow = null;
    }

    function rememberPeekScroll(): void {
        peekScrollX = window.scrollX;
        peekScrollY = window.scrollY;
    }

    function hidePeek(): void {
        cancelPeekTimer();
        if (!peekRow) return;
        peekRow.querySelector(".athlete-name")?.removeAttribute("aria-describedby");
        peekRow = null;
        peek.classList.remove("is-open");
        peek.hidden = true;
    }

    function showPeek(row: HTMLTableRowElement, clientX?: number): void {
        if (!row.isConnected || row.style.display === "none" || (clientX !== undefined && !row.matches(":hover")) || !fillPeek(row)) {
            hidePeek();
            return;
        }
        peekRow?.querySelector(".athlete-name")?.removeAttribute("aria-describedby");
        peek.hidden = false;
        peek.classList.add("is-open");
        peekRow = row;
        rememberPeekScroll();
        row.querySelector(".athlete-name")?.setAttribute("aria-describedby", peek.id);
        placePeek(row, clientX);
        peek.tabIndex = peek.scrollHeight > peek.clientHeight + 1 ? 0 : -1;
        if (clientX !== undefined) row.querySelector("img.portrait")?.dispatchEvent(new Event("lwc:portrait-preview"));
    }

    // Scrolling can send pointerover to a new row under a stationary mouse.
    // Only a mouse move should start a dwell and put a card beside its path.
    body.addEventListener("pointermove", event => {
        if (!finePointer.matches || event.pointerType !== "mouse" || !(event.target instanceof Element)) return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        // Badges and contact links own their own tooltips. The spaces between them belong to the row.
        if (!row || !body.contains(row) || event.target.closest(".badge-class, button, .media-contact-handle, a:not(.athlete-name, .athlete-profile-link)")) {
            hidePeek();
            return;
        }
        pointerX = event.clientX;
        if (row === pendingPeekRow) return;
        cancelPeekTimer();
        if (row === peekRow) return;
        pendingPeekRow = row;
        rememberPeekScroll();
        peekTimer = window.setTimeout(() => {
            pendingPeekRow = null;
            showPeek(row, pointerX);
        }, peek.hidden ? 420 : 160);
    }, { passive: true });
    function schedulePeekDismiss(): void {
        cancelPeekTimer();
        peekTimer = window.setTimeout(() => { if (!peekRegion.matches(":hover")) hidePeek(); }, 160);
    }
    body.addEventListener("pointerleave", schedulePeekDismiss);
    peekRegion.addEventListener("pointerenter", cancelPeekTimer);
    peekRegion.addEventListener("pointerleave", schedulePeekDismiss);
    body.addEventListener("focusin", event => {
        if (!(event.target instanceof Element) || !event.target.matches(".athlete-name:focus-visible")
            || document.documentElement.dataset["lwcInput"] === "touch") return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        hidePeek();
        if (row) showPeek(row);
    });
    body.addEventListener("focusout", event => { if (!peek.contains(event.relatedTarget as Node | null)) hidePeek(); });
    peek.addEventListener("focusout", hidePeek);
    document.addEventListener("pointerdown", event => { if (!peek.contains(event.target as Node | null)) hidePeek(); }, true);
    window.addEventListener("resize", hidePeek);
    window.addEventListener("pagehide", hidePeek);
    finePointer.addEventListener("change", hidePeek);
    document.addEventListener("lwc:standings-ready", hidePeek);
    // Row replacement can also happen during loading or recovery, before standings are ready.
    new MutationObserver(hidePeek).observe(body, { childList: true });
    if (typeof ResizeObserver === "function") {
        new ResizeObserver(() => {
            // Other rows can finish laying out during a dwell or while the card is being read.
            if (!peekRow || pendingPeekRow) return;
            if (!peekRow.getClientRects().length) { hidePeek(); return; }
            if (peekRegion.matches(":hover, :focus-within")) return;
            if (peekRow.matches(":focus-within")) placePeek(peekRow);
            else if (peekRow.matches(":hover")) placePeek(peekRow, pointerX);
            else hidePeek();
        }).observe(table);
    }
    window.addEventListener("scroll", () => {
        // A queued scroll event can arrive after the pointer entered at the new position.
        if ((peekRow || pendingPeekRow) && (window.scrollX !== peekScrollX || window.scrollY !== peekScrollY)) hidePeek();
    }, { passive: true });
    document.addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        if (peek.contains(document.activeElement)) peekRow?.querySelector<HTMLElement>(".athlete-name")?.focus({ preventScroll: true });
        hidePeek();
    });
}

(() => {
    const main = document.querySelector<HTMLElement>("main.homepage-main, main[data-leaderboard-page='full']");
    const table = main?.querySelector<HTMLTableElement>("#athleteLeaderboard > table");
    const body = table?.tBodies[0];
    if (main && table && body) initializeLeaderboardRowPreview(main, table, body);
})();
