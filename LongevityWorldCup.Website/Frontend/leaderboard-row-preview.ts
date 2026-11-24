// The homepage and full standings share the same athlete preview.
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
    peek.setAttribute("role", "group");
    peek.hidden = true;
    const peekRegion = document.createElement("div");
    peekRegion.className = "lb-peek-region";
    peekRegion.dataset["lwcTipHost"] = "";
    peekRegion.append(peek);
    document.body.append(peekRegion);
    let peekTimer = 0;
    let peekRow: HTMLTableRowElement | null = null;
    let pendingPeekRow: HTMLTableRowElement | null = null;
    let peekContentKey = "";
    let refreshing = false;
    let pointerX = 0;
    let peekScrollX = 0;
    let peekScrollY = 0;

    function fact(className: string, label: string, explanation: string, ...children: (Node | string)[]): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "button";
        button.className = className;
        button.setAttribute("aria-label", label);
        button.dataset["lwcTip"] = explanation;
        button.append(...children);
        return button;
    }

    function sparkline(placements: readonly (number | null)[], rank: number): HTMLButtonElement | null {
        const periods = ["A year ago", "A month ago", "A week ago", "Yesterday", "Today"];
        const history = [placements[3], placements[2], placements[1], placements[0], rank];
        const points = history.flatMap((value, index) => value != null && Number.isFinite(value) && value > 0 ? [{ value, index }] : []);
        if (points.length < 2) return null;
        const ns = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(ns, "svg");
        svg.setAttribute("viewBox", "0 0 120 32");
        svg.setAttribute("class", "lb-peek-spark");
        svg.setAttribute("aria-hidden", "true");
        const best = Math.min(...points.map(point => point.value));
        const range = Math.max(1, Math.max(...points.map(point => point.value)) - best);
        const line = document.createElementNS(ns, "polyline");
        line.setAttribute("points", points.map(point => `${4 + point.index * 28},${4 + (point.value - best) / range * 24}`).join(" "));
        svg.append(line);
        const positions = periods.map((period, i) => {
            const point = points.find(point => point.index === i);
            return `${period} ${point ? `#${point.value}` : "(no record)"}`;
        });
        return fact("lb-peek-history", "Ultimate League rank history",
            `Ultimate League rank from left to right. Higher is closer to #1. ${positions.join(" · ")}.`, svg);
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
        const rowLink = row.querySelector<HTMLAnchorElement>("a.athlete-name[href]");
        if (!rowLink) return false;
        const displayName = rowLink.textContent?.trim() || name;
        // Reuse the page's calculated clock values. Display metrics can be
        // percentages or improvements, and must never be parsed back into ages.
        const athlete = (window as unknown as { getLeaderboardPeek?: (name: string) => PeekAthlete | null }).getLeaderboardPeek?.(name);
        const rank = row.querySelector(".rank")?.textContent?.trim() || "";
        const metric = row.querySelector(".age-reduction")?.textContent?.trim() || "";
        const portrait = row.querySelector<HTMLImageElement>("img.portrait");
        const view = main.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked")?.value || "ultimate";
        const league = main.querySelector(".collapsed-title")?.getAttribute("aria-label") || "Current standings";
        const photo = portrait?.currentSrc || portrait?.src;
        const contentKey = JSON.stringify([name, displayName, rowLink.href, rank, metric, view, league, photo, athlete]);
        if (row === peekRow && contentKey === peekContentKey) return true;
        peekContentKey = contentKey;
        const improvement = view === "improvement" || view === "bortz-improvement";
        const labScore = !improvement && view !== "crowd";
        const bortz = view === "bortz" || view === "bortz-improvement" || (view === "ultimate" && athlete?.bortzRankResult != null);
        const clock = bortz ? "bortz" : "pheno";
        const result = bortz ? athlete?.bortzRankResult : athlete?.phenoRankResult;
        const content = document.createDocumentFragment();
        peek.setAttribute("aria-label", `${displayName} leaderboard preview`);

        const top = document.createElement("a");
        top.className = "lb-peek-top";
        top.href = rowLink.href;
        top.setAttribute("aria-label", `Open ${displayName}’s profile, ranked #${rank}`);
        top.dataset["lwcTip"] = `${league} · Rank #${rank}. Open ${displayName}’s profile.`;
        // Follow the row's profile flow and return focus there when it closes.
        // Modified clicks and the link menu use the canonical href directly.
        top.addEventListener("click", event => {
            if (event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
            event.preventDefault();
            hidePeek();
            row.querySelector<HTMLAnchorElement>("a.athlete-name")?.click();
        });
        if (photo) {
            const picture = document.createElement("img");
            picture.src = photo;
            picture.alt = "";
            top.append(picture);
        }
        top.append(cell("lb-peek-id", cell("lb-peek-rank", rank), cell("lb-peek-name", displayName)));
        content.append(top);
        if (metric) {
            const label = improvement ? `${clock} age improvement` : "Age reduction";
            const estimate = improvement ? `The latest ${clock} age`
                : view === "crowd" ? "The crowd’s age estimate"
                : result ? `The ${clock} biological age estimate` : "The biological age estimate";
            const reference = improvement ? `this athlete’s highest recorded ${clock} age`
                : view === "crowd" ? "the athlete’s age today" : "the athlete’s age at the blood draw";
            let explanation = `${estimate} minus ${reference} comes to ${metric}.`;
            if (metric.startsWith("-") || metric.startsWith("+")) {
                explanation = `${estimate} was ${metric.slice(1)} ${metric.startsWith("-") ? "below" : "above"} ${reference}.`;
            }
            if (labScore && athlete && !result) {
                explanation = `The leaderboard assigns ${metric} when there is no eligible ${clock} result.`;
            } else if (labScore && result) {
                explanation += " The score uses the unrounded ages.";
            }
            content.append(fact("lb-peek-metric", `${label}, ${metric}`, explanation, metric));
        }

        const facts = cell("lb-peek-facts");
        if (athlete) {
            const age = labScore ? result?.chrono : athlete.chronologicalAge;
            const bio = labScore ? result?.bio : null;
            if (age != null && Number.isFinite(age)) {
                const years = age.toFixed(1);
                const ageText = labScore ? `${displayName} was ${years} years old at the blood draw.` : `${displayName} is ${years} years old today.`;
                const children = [icon("fa-hourglass-half"), cell("lb-peek-age", years)];
                if (bio != null && Number.isFinite(bio)) {
                    const estimate = bio.toFixed(1);
                    children.push(icon("fa-arrow-right-long"), cell("lb-peek-age is-bio", estimate));
                    facts.append(fact("lb-peek-ages", `Age at test ${years}, ${clock} age ${estimate}`,
                        `${ageText} The ${clock} clock estimated a biological age of ${estimate} years from that sample.`, ...children));
                } else {
                    facts.append(fact("lb-peek-ages", `Chronological age ${years}`, ageText, ...children));
                }
            }
        }
        if (athlete && athlete.crowdCount > 0 && athlete.crowdAge != null && Number.isFinite(athlete.crowdAge)) {
            const years = athlete.crowdAge.toFixed(1);
            const guesses = athlete.crowdCount === 1 ? "one accepted guess" : `${athlete.crowdCount.toLocaleString("en-US")} accepted guesses`;
            facts.append(fact("lb-peek-crowd", `Crowd age ${years}`,
                `Visitors guessed this athlete’s age from their current profile photo. The median of ${guesses} is ${years} years.`,
                icon("fa-people-group"), cell("lb-peek-age", years)));
        }
        if (facts.childNodes.length) content.append(facts);
        if (athlete && view === "ultimate") {
            const spark = sparkline(athlete.placements, Number(row.dataset["rank"]));
            if (spark) content.append(spark);
        }
        const focused = peek.contains(document.activeElement) ? document.activeElement?.classList[0] : null;
        refreshing = true;
        peek.replaceChildren(content);
        if (focused) (peek.querySelector<HTMLElement>("." + CSS.escape(focused)) ?? top).focus({ preventScroll: true });
        refreshing = false;
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
        if (clientX !== undefined && room < preferred) peek.style.maxWidth = `${Math.max(0, room)}px`;
        const width = peek.offsetWidth;
        const left = right ? x + 12 : x - 12 - width;
        const height = peek.offsetHeight;
        const above = rowBox.top - height - 8;
        const header = table.tHead;
        const stickyTop = parseFloat(getComputedStyle(main).getPropertyValue("--site-sticky-height")) || 52;
        const below = above <= stickyTop + (header?.getBoundingClientRect().height ?? 0);
        const top = below ? rowBox.bottom + 8 : above;
        peekRegion.classList.toggle("to-right", right);
        peekRegion.classList.toggle("below", below);
        peekRegion.style.left = `${Math.max(12, Math.min(window.innerWidth - width - 12, left))}px`;
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

    function closeCard(): void {
        if (!peekRow) return;
        peekRow.querySelector(".athlete-name")?.removeAttribute("aria-details");
        peekRow = null;
        peek.classList.remove("is-open");
        peek.hidden = true;
        peek.replaceChildren();
    }

    function hidePeek(): void {
        cancelPeekTimer();
        closeCard();
    }

    function rowAvailable(row: HTMLTableRowElement): boolean {
        return row.parentElement === body && !row.hidden && row.style.display !== "none";
    }

    function checkRows(): void {
        if (pendingPeekRow && !rowAvailable(pendingPeekRow)) cancelPeekTimer();
        if (peekRow && !rowAvailable(peekRow)) closeCard();
    }

    function showPeek(row: HTMLTableRowElement, clientX?: number): void {
        cancelPeekTimer();
        if (!rowAvailable(row) || (clientX !== undefined && !row.matches(":hover")) || !fillPeek(row)) {
            hidePeek();
            return;
        }
        peekRow?.querySelector(".athlete-name")?.removeAttribute("aria-details");
        peek.hidden = false;
        peek.classList.add("is-open");
        peekRow = row;
        rememberPeekScroll();
        row.querySelector(".athlete-name")?.setAttribute("aria-details", peek.id);
        placePeek(row, clientX);
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
    function schedulePeekDismiss(event: PointerEvent): void {
        if (event.pointerType !== "mouse") return;
        cancelPeekTimer();
        peekTimer = window.setTimeout(() => {
            if (!peekRegion.matches(":hover, :focus-within") && !peekRow?.querySelector(".athlete-name:focus-visible")) hidePeek();
        }, 160);
    }
    body.addEventListener("pointerleave", schedulePeekDismiss);
    peekRegion.addEventListener("pointerenter", cancelPeekTimer);
    peekRegion.addEventListener("pointerleave", schedulePeekDismiss);
    // A finger can inspect the rank or score directly. The name, portrait and
    // remaining row area retain their usual one-tap profile action.
    body.addEventListener("click", event => {
        if (document.documentElement.dataset["lwcInput"] !== "touch" || !(event.target instanceof Element)
            || event.target.closest("a, button") || !event.target.closest(".rank-td, .age-reduction-td")) return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        if (!row) return;
        event.stopPropagation();
        showPeek(row);
    }, true);
    body.addEventListener("focusin", event => {
        if (!(event.target instanceof Element) || !event.target.matches(".athlete-name:focus-visible")
            || document.documentElement.dataset["lwcInput"] === "touch") return;
        const row = event.target.closest<HTMLTableRowElement>(rowSelector);
        if (row && row !== peekRow) showPeek(row);
    });
    body.addEventListener("focusout", event => { if (!peekRegion.contains(event.relatedTarget as Node | null)) hidePeek(); });
    peekRegion.addEventListener("focusout", event => {
        if (!refreshing && !peekRegion.contains(event.relatedTarget as Node | null) && event.relatedTarget !== peekRow?.querySelector(".athlete-name")) hidePeek();
    });
    document.addEventListener("pointerdown", event => { if (!peekRegion.contains(event.target as Node | null)) hidePeek(); }, true);
    window.addEventListener("resize", hidePeek);
    window.addEventListener("pagehide", hidePeek);
    finePointer.addEventListener("change", hidePeek);
    document.addEventListener("lwc:standings-ready", event => {
        if ((event as CustomEvent<{ selectionChanged: boolean }>).detail?.selectionChanged) { hidePeek(); return; }
        checkRows();
        if (peekRow) {
            if (!fillPeek(peekRow)) { closeCard(); return; }
            peekRow.querySelector(".athlete-name")?.setAttribute("aria-details", peek.id);
        }
    });
    // Hydration may insert, remove or move siblings while this athlete is still under the pointer.
    new MutationObserver(checkRows).observe(body, { childList: true });
    if (typeof ResizeObserver === "function") {
        new ResizeObserver(() => {
            // Other rows can finish laying out during a dwell or while the card is being read.
            if (!peekRow || pendingPeekRow) return;
            if (!peekRow.getClientRects().length) { hidePeek(); return; }
            if (peekRegion.matches(":hover, :focus-within")) return;
            if (peekRow.matches(":focus-within") || document.documentElement.dataset["lwcInput"] === "touch") placePeek(peekRow);
            else if (peekRow.matches(":hover")) placePeek(peekRow, pointerX);
            else hidePeek();
        }).observe(table);
    }
    window.addEventListener("scroll", () => {
        // A queued scroll event can arrive after the pointer entered at the new position.
        if ((peekRow || pendingPeekRow) && (window.scrollX !== peekScrollX || window.scrollY !== peekScrollY)) hidePeek();
    }, { passive: true });
    document.addEventListener("keydown", event => {
        if (event.defaultPrevented) return;
        const rowLink = peekRow?.querySelector<HTMLAnchorElement>("a.athlete-name");
        const active = document.activeElement;
        if (event.key === "Escape") {
            if (peekRegion.contains(active)) rowLink?.focus({ preventScroll: true });
            hidePeek();
        } else if (rowLink && event.key === "Tab" && !event.altKey && !event.ctrlKey && !event.metaKey) {
            // The floating card lives outside the table. Read its controls in
            // sequence after the row name, then resume the row's native tab order.
            const controls = peek.querySelectorAll<HTMLElement>("a[href], button");
            const first = controls[0];
            if (active === rowLink && !event.shiftKey && first) {
                event.preventDefault();
                first.focus({ preventScroll: true });
            } else if ((event.shiftKey && active === first) || (!event.shiftKey && active === controls[controls.length - 1])) {
                if (event.shiftKey) event.preventDefault();
                rowLink.focus({ preventScroll: true });
            }
        }
    });
}

(() => {
    const main = document.querySelector<HTMLElement>("main.homepage-main, main[data-leaderboard-page='full']");
    const table = main?.querySelector<HTMLTableElement>("#athleteLeaderboard > table");
    const body = table?.tBodies[0];
    if (main && table && body) initializeLeaderboardRowPreview(main, table, body);
})();
