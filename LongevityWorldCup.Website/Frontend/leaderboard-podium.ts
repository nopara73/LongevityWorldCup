// The top of the board as a podium moment, and standings that visibly move.
//
// - Full /leaderboard pages get a stage above the board: the first three rows
//   of the current selection stand on gold, silver and bronze plinths. It rises
//   in on load and, when a division, generation or ranking changes who leads,
//   athletes who stay glide to their new plinth while the others sink out and
//   their replacements rise in. Search, By Flag and custom orderings fold it
//   away, because their first three are not a podium.
// - Scrolling lets the board slide over the stage while it sinks and dims
//   (CSS scroll timeline); the first three rows then land in the table.
// - #1 wears a gold halo; one glint runs around the ring after it arrives. The
//   homepage podium gets the same halo and its prize plinths rise in.
// - Rows whose rank differs from last month's roll their rank number from the
//   old place to the new one, nudging up or down with a short tint, once, as
//   they first come into view (Ultimate ranking, unfiltered only).
//
// Classic script, loaded on the full leaderboard and on the homepage. The stage
// is a decorative mirror of rows that stay in the table: it is aria-hidden and
// its links forward to the rows' own profile handlers. Nothing here owns the
// rows or waits on an animation. prefers-reduced-motion keeps every movement
// off; the stage, halo and tints still render.

function initializeLeaderboardPodium(): void {
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const body = document.querySelector<HTMLTableSectionElement>(".leaderboard table tbody");
    if (!body) return;

    type Standing = {
        readonly rank: number;
        readonly lastMonthRank: number | null;
        readonly isNew: boolean;
        readonly flag: string;
    };
    type Lead = {
        readonly name: string;
        readonly row: HTMLTableRowElement;
        readonly place: string;
        readonly score: string;
        readonly href: string;
        readonly src: string;
        readonly alt: string;
        readonly label: string;
    };
    const standingOf = (name: string): Standing | null =>
        (window as unknown as { getLeaderboardStanding?: (n: string) => Standing | null }).getLeaderboardStanding?.(name) ?? null;
    const flagIcon = (flag: string): string =>
        (window as unknown as { LwcFlags?: { renderFlagIcon: (f: unknown, c?: string) => string } }).LwcFlags?.renderFlagIcon(flag, "lb-stage-flag") ?? "";

    const still = (): boolean => reduced.matches;

    function ms(name: string, fallback: number): number {
        const raw = getComputedStyle(root).getPropertyValue(name).trim();
        const value = parseFloat(raw) * (raw.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(value) && value >= 0 ? value : fallback;
    }

    const easeOut = "cubic-bezier(.2,.8,.2,1)";
    const easeSpring = "cubic-bezier(.34,1.45,.55,1)";

    function visibleRows(): HTMLTableRowElement[] {
        return [...body!.querySelectorAll<HTMLTableRowElement>(":scope > tr[data-athlete-name]")]
            .filter(row => row.style.display !== "none" && !row.classList.contains("extra-athlete"));
    }

    // ---------------------------------------------------------------- #1 glint
    function glint(host: HTMLElement, ring: Element): void {
        if (still()) return;
        const hostBox = host.getBoundingClientRect();
        const box = ring.getBoundingClientRect();
        if (!box.width) return;
        const sweep = document.createElement("span");
        sweep.className = "lb-glint";
        sweep.setAttribute("aria-hidden", "true");
        sweep.style.left = `${box.left - hostBox.left}px`;
        sweep.style.top = `${box.top - hostBox.top}px`;
        sweep.style.width = `${box.width}px`;
        sweep.style.height = `${box.height}px`;
        host.append(sweep);
        const run = sweep.animate(
            [{ transform: "rotate(-40deg)", opacity: 0 }, { opacity: 1, offset: .2 }, { opacity: 1, offset: .75 }, { transform: "rotate(320deg)", opacity: 0 }],
            { duration: 1100, easing: "cubic-bezier(.45,0,.25,1)" });
        run.onfinish = run.oncancel = () => sweep.remove();
    }

    // -------------------------------------------------------- rank-change cues
    const cued = new Set<string>();
    const watcher = "IntersectionObserver" in window
        ? new IntersectionObserver(entries => entries.forEach(entry => {
            if (!entry.isIntersecting) return;
            watcher!.unobserve(entry.target);
            cue(entry.target as HTMLTableRowElement);
        }), { threshold: .6 })
        : null;

    function ultimateView(): boolean {
        const checked = document.querySelector<HTMLInputElement>("input[name='leaderboardView']:checked");
        return !checked || checked.value === "ultimate";
    }

    function movement(row: HTMLTableRowElement): { from: number; to: number } | null {
        const name = row.dataset["athleteName"];
        const standing = name ? standingOf(name) : null;
        if (!standing || standing.isNew || standing.lastMonthRank == null || standing.lastMonthRank === standing.rank) return null;
        const shown = row.querySelector(".rank")?.textContent?.trim();
        if (shown !== String(standing.rank) || !ultimateView()) return null;
        return { from: standing.lastMonthRank, to: standing.rank };
    }

    function cue(row: HTMLTableRowElement): void {
        const name = row.dataset["athleteName"] ?? "";
        if (cued.has(name) || row.style.display === "none") return;
        const move = movement(row);
        if (!move) return;
        cued.add(name);
        const up = move.from > move.to;
        row.dataset["lbMoved"] = up ? "up" : "down";
        if (still()) return;
        const rank = row.querySelector<HTMLElement>(".rank");
        if (!rank) return;
        rank.dataset["was"] = String(move.from);
        rank.dataset["now"] = rank.textContent?.trim() ?? String(move.to);
        rank.classList.add("lb-rolling", up ? "is-up" : "is-down");
        let rolled = false;
        const done = () => {
            if (rolled) return;
            rolled = true;
            rank.classList.remove("lb-rolling", "is-up", "is-down");
            delete rank.dataset["was"];
            delete rank.dataset["now"];
        };
        rank.addEventListener("animationend", event => { if (event.pseudoElement === "::after") done(); });
        window.setTimeout(done, 4000);
        const cells = [...row.cells];
        cells.forEach(cell => cell.animate(
            [{ transform: `translateY(${up ? 10 : -10}px)` }, { transform: "none" }],
            { duration: 420, easing: easeSpring }));
        row.classList.add("lb-move-flash");
        const settle = () => row.classList.remove("lb-move-flash");
        cells[0]?.addEventListener("animationend", event => { if (event.animationName === "lb-move-tint") settle(); }, { once: false });
        window.setTimeout(settle, 6000);
    }

    function watchMoves(): void {
        if (!watcher) return;
        for (const row of body!.querySelectorAll<HTMLTableRowElement>(":scope > tr[data-athlete-name]")) {
            const name = row.dataset["athleteName"] ?? "";
            if (cued.has(name) || row.dataset["lbWatched"] === "1") continue;
            if (!movement(row)) continue;
            row.dataset["lbWatched"] = "1";
            watcher.observe(row);
        }
    }

    // ---------------------------------------------------- homepage podium polish
    const homePodium = document.querySelector<HTMLElement>(".podium");
    let homePlayed = false;
    let homeGlinted = false;
    function polishHomePodium(): void {
        if (!homePodium || homePodium.style.display === "none") return;
        const items = [...homePodium.querySelectorAll<HTMLElement>(".podium-item:not(.podium-skeleton-item)")];
        if (!items.length) return;
        homePodium.classList.add("lb-podium-lit");
        if (!homePlayed && !still()) {
            homePlayed = true;
            const order: Record<string, number> = { third: 0, second: 1, first: 2 };
            items.forEach(item => {
                const step = item.querySelector<HTMLElement>(".podium-item-lower");
                const place = ["first", "second", "third"].find(cls => item.classList.contains(cls)) ?? "third";
                step?.animate([{ transform: "scaleY(.2)", opacity: .4 }, { transform: "none", opacity: 1 }],
                    { duration: 520, delay: 160 + (order[place] ?? 0) * 110, easing: easeOut, fill: "backwards" });
            });
        }
        const first = homePodium.querySelector<HTMLElement>(".podium-item.first:not(.podium-skeleton-item)");
        const ring = first?.querySelector("img.podium-portrait");
        if (first && ring && !homeGlinted) {
            homeGlinted = true;
            const run = () => glint(first, ring);
            if ((ring as HTMLImageElement).complete) window.setTimeout(run, 900); else ring.addEventListener("load", () => window.setTimeout(run, 500), { once: true });
        }
    }

    // ---------------------------------------------------------- full-page stage
    const full = document.querySelector('main[data-leaderboard-page="full"]');
    const controls = document.querySelector<HTMLElement>(".leaderboard-controls");
    const stage = full && controls ? buildStage() : null;
    const slots = stage ? [...stage.querySelectorAll<HTMLElement>(".lb-stage-slot")] : [];
    let shown: (Lead | null)[] = [null, null, null];
    let introPlayed = false;

    function buildStage(): HTMLElement {
        const section = document.createElement("div");
        section.className = "lb-stage";
        section.setAttribute("aria-hidden", "true");
        section.dataset["state"] = "empty";
        section.innerHTML = `<div class="lb-stage-clip"><div class="lb-stage-floor">${[2, 1, 3].map(place =>
            `<div class="lb-stage-slot" data-slot="${place}"><div class="lb-stage-cards"></div><div class="lb-stage-plinth"><span class="lb-stage-place"></span></div></div>`).join("")}</div></div>`;
        controls!.before(section);
        return section;
    }

    function slotFor(index: number): HTMLElement {
        return slots.find(slot => slot.dataset["slot"] === String(index + 1))!;
    }

    function readLead(row: HTMLTableRowElement): Lead {
        const img = row.querySelector<HTMLImageElement>("img.portrait");
        const nameLink = row.querySelector<HTMLAnchorElement>("a.athlete-name");
        return {
            name: row.dataset["athleteName"] ?? "",
            row,
            place: row.querySelector(".rank")?.textContent?.trim() ?? "",
            score: row.querySelector(".age-reduction")?.textContent?.trim() ?? "",
            href: nameLink?.getAttribute("href") ?? "#",
            src: img?.currentSrc || img?.getAttribute("src") || "",
            alt: img?.alt ?? "",
            label: nameLink?.textContent?.trim() ?? ""
        };
    }

    function stageWanted(): boolean {
        const search = document.getElementById("athleteSearch") as HTMLInputElement | null;
        const byFlag = document.getElementById("group-flags") as HTMLInputElement | null;
        const ordered = document.getElementById("leaderboardOrderReset");
        return !search?.value.trim() && !byFlag?.checked && (!ordered || ordered.hidden === true);
    }

    function card(lead: Lead, index: number): HTMLElement {
        const element = document.createElement("a");
        element.className = "lb-stage-card";
        element.href = lead.href;
        element.tabIndex = -1;
        element.dataset["athleteName"] = lead.name;
        const crown = index === 0 ? '<i class="fa-solid fa-crown lb-stage-crown" aria-hidden="true"></i>' : "";
        element.innerHTML = `${crown}<span class="lb-stage-portrait"><img alt="" decoding="async"><span class="lb-stage-flagmark"></span></span><span class="lb-stage-name"></span><span class="lb-stage-score"></span>`;
        const img = element.querySelector("img")!;
        img.src = lead.src;
        img.addEventListener("error", () => { img.style.visibility = "hidden"; }, { once: true });
        fill(element, lead);
        element.addEventListener("click", event => {
            if (event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
            const target = lead.row.querySelector<HTMLElement>("a.athlete-name");
            if (!target) return;
            event.preventDefault();
            target.click();
        });
        return element;
    }

    function fill(element: HTMLElement, lead: Lead): void {
        element.querySelector(".lb-stage-name")!.textContent = lead.label;
        const score = element.querySelector<HTMLElement>(".lb-stage-score")!;
        if (score.dataset["final"] !== undefined) {
            score.dataset["final"] = lead.score;
        } else if (score.textContent !== lead.score) {
            const had = score.textContent !== "";
            score.textContent = lead.score;
            if (had && !still()) score.animate([{ opacity: 0, transform: "translateY(6px)" }, { opacity: 1, transform: "none" }], { duration: 220, easing: easeOut });
        }
        const mark = element.querySelector<HTMLElement>(".lb-stage-flagmark")!;
        if (!mark.childElementCount) {
            const standing = standingOf(lead.name);
            if (standing?.flag) {
                mark.innerHTML = flagIcon(standing.flag);
                if (mark.childElementCount && introPlayed && !still()) mark.animate([{ transform: "scale(0)" }, { transform: "none" }], { duration: 260, easing: easeSpring });
            }
        }
    }

    function countUp(element: HTMLElement, delay: number): void {
        const text = element.textContent ?? "";
        const match = /(\d+(?:\.(\d+))?)/.exec(text);
        if (!match || still()) return;
        const target = parseFloat(match[1]!);
        const decimals = match[2]?.length ?? 0;
        const before = text.slice(0, match.index);
        const after = text.slice(match.index + match[1]!.length);
        const length = 700;
        let start = 0;
        // fill() writes newer scores to data-final while the count runs.
        element.dataset["final"] = text;
        const tick = (time: number) => {
            if (!start) start = time + delay;
            const t = Math.min(1, Math.max(0, (time - start) / length));
            const eased = 1 - Math.pow(1 - t, 3);
            if (t < 1 && element.dataset["final"] === text) {
                element.textContent = before + (target * eased).toFixed(decimals) + after;
                requestAnimationFrame(tick);
                return;
            }
            element.textContent = element.dataset["final"] ?? text;
            delete element.dataset["final"];
        };
        element.textContent = before + (0).toFixed(decimals) + after;
        requestAnimationFrame(tick);
    }

    function intro(): void {
        if (!stage || introPlayed) return;
        introPlayed = true;
        if (still()) return;
        // Third, then second, then the winner: the stage builds to #1.
        const sequence = [2, 1, 0];
        sequence.forEach((index, step) => {
            const slot = slotFor(index);
            const plinth = slot.querySelector<HTMLElement>(".lb-stage-plinth")!;
            const cardEl = slot.querySelector<HTMLElement>(".lb-stage-card");
            const delay = 80 + step * 120;
            plinth.animate([{ transform: "scaleY(0)" }, { transform: "none" }], { duration: 460, delay, easing: easeOut, fill: "backwards" });
            plinth.querySelector(".lb-stage-place")?.animate([{ opacity: 0, transform: "translateY(8px)" }, { opacity: 1, transform: "none" }],
                { duration: 240, delay: delay + 260, easing: easeOut, fill: "backwards" });
            if (!cardEl) return;
            cardEl.querySelector(".lb-stage-portrait")?.animate(
                [{ opacity: 0, transform: "translateY(34px) scale(.82)" }, { opacity: 1, transform: "none" }],
                { duration: 520, delay: delay + 160, easing: easeSpring, fill: "backwards" });
            for (const part of cardEl.querySelectorAll(".lb-stage-name, .lb-stage-score")) {
                part.animate([{ opacity: 0, transform: "translateY(10px)" }, { opacity: 1, transform: "none" }],
                    { duration: 300, delay: delay + 300, easing: easeOut, fill: "backwards" });
            }
            const score = cardEl.querySelector<HTMLElement>(".lb-stage-score");
            if (score) countUp(score, delay + 300);
            if (index === 0) {
                cardEl.querySelector(".lb-stage-crown")?.animate(
                    [{ opacity: 0, transform: "translate(-50%, -18px) rotate(-14deg) scale(.6)" }, { opacity: 1, transform: "translate(-50%, 0) rotate(0) scale(1)" }],
                    { duration: 520, delay: delay + 460, easing: easeSpring, fill: "backwards" });
                const ring = cardEl.querySelector(".lb-stage-portrait");
                if (ring) window.setTimeout(() => glint(cardEl, ring), delay + 820);
            }
        });
    }

    function swap(next: (Lead | null)[]): void {
        const previous = shown;
        shown = next;
        const animate = introPlayed && !still();
        // Measure cards that will stay so they can glide between plinths.
        const before = new Map<string, DOMRect>();
        const oldCards = new Map<string, HTMLElement>();
        for (const slot of slots) {
            const existing = slot.querySelector<HTMLElement>(".lb-stage-card:not(.is-leaving)");
            if (existing) {
                oldCards.set(existing.dataset["athleteName"] ?? "", existing);
                before.set(existing.dataset["athleteName"] ?? "", existing.getBoundingClientRect());
            }
        }
        const keep = new Set(next.filter((lead): lead is Lead => Boolean(lead)).map(lead => lead.name));
        next.forEach((lead, index) => {
            const slot = slotFor(index);
            slot.querySelector<HTMLElement>(".lb-stage-place")!.textContent = lead?.place ?? "";
            slot.classList.toggle("is-empty", !lead);
        });
        // Leaving cards sink into their plinth.
        oldCards.forEach((element, name) => {
            if (keep.has(name)) return;
            if (!animate) { element.remove(); return; }
            const slot = element.closest<HTMLElement>(".lb-stage-slot")!;
            const box = element.getBoundingClientRect();
            const slotBox = slot.getBoundingClientRect();
            element.classList.add("is-leaving");
            element.style.left = `${box.left - slotBox.left}px`;
            element.style.top = `${box.top - slotBox.top}px`;
            element.style.width = `${box.width}px`;
            const out = element.animate([{ opacity: 1, transform: "none" }, { opacity: 0, transform: "translateY(42px) scale(.86)" }],
                { duration: 200, easing: "cubic-bezier(.4,0,1,1)", fill: "forwards" });
            out.onfinish = out.oncancel = () => element.remove();
        });
        next.forEach((lead, index) => {
            if (!lead) return;
            const holder = slotFor(index).querySelector<HTMLElement>(".lb-stage-cards")!;
            let element = oldCards.get(lead.name);
            const wasAt = previous.findIndex(item => item?.name === lead.name);
            if (element) {
                fill(element, lead);
                const crown = element.querySelector(".lb-stage-crown");
                if (index === 0 && !crown) element.insertAdjacentHTML("afterbegin", '<i class="fa-solid fa-crown lb-stage-crown" aria-hidden="true"></i>');
                if (index !== 0) crown?.remove();
                if (element.parentElement !== holder) holder.append(element);
                const from = before.get(lead.name);
                const to = element.getBoundingClientRect();
                if (animate && from && wasAt !== index) {
                    const scale = from.width / Math.max(1, to.width);
                    element.animate([
                        { transform: `translate(${from.left - to.left}px, ${from.top - to.top}px) scale(${scale})` },
                        { transform: "none" }
                    ], { duration: 340, easing: "cubic-bezier(.3,.7,.2,1)" });
                    bump(index, 60);
                }
                return;
            }
            element = card(lead, index);
            holder.append(element);
            if (animate) {
                element.animate([{ opacity: 0, transform: "translateY(46px) scale(.84)" }, { opacity: 1, transform: "none" }],
                    { duration: 320, delay: 140 + (2 - index) * 50, easing: easeSpring, fill: "backwards" });
                bump(index, 120);
                if (index === 0) {
                    const ring = element.querySelector(".lb-stage-portrait");
                    const host = element;
                    if (ring) window.setTimeout(() => glint(host, ring), 560);
                }
            }
        });
    }

    function bump(index: number, delay: number): void {
        slotFor(index).querySelector(".lb-stage-plinth")?.animate(
            [{ transform: "none" }, { transform: "scaleY(.9)" }, { transform: "none" }],
            { duration: 300, delay, easing: "ease-out" });
    }

    function syncStage(): void {
        if (!stage) return;
        const wanted = stageWanted();
        const leads = wanted ? visibleRows().slice(0, 3).map(readLead) : [];
        const next: (Lead | null)[] = [leads[0] ?? null, leads[1] ?? null, leads[2] ?? null];
        const state = leads.length ? "on" : "off";
        const same = next.every((lead, index) => (lead?.name ?? "") === (shown[index]?.name ?? ""));
        if (!same || state === "on") {
            if (same) next.forEach((lead, index) => {
                const element = lead && slotFor(index).querySelector<HTMLElement>(".lb-stage-card:not(.is-leaving)");
                if (element && lead) { fill(element, lead); slotFor(index).querySelector(".lb-stage-place")!.textContent = lead.place; }
            });
            else if (leads.length) swap(next);
        }
        if (stage.dataset["state"] !== state) {
            stage.dataset["state"] = state;
            if (state === "off") shown = [null, null, null];
        }
        if (state === "on" && !introPlayed) {
            if (stage.getBoundingClientRect().top < window.innerHeight) requestAnimationFrame(() => requestAnimationFrame(intro));
            else introPlayed = true;
        }
    }

    // --------------------------------------------------------------- wiring
    let pending = 0;
    function schedule(): void {
        if (pending) return;
        pending = requestAnimationFrame(() => {
            pending = 0;
            syncStage();
            polishHomePodium();
            watchMoves();
        });
    }
    const updates = new MutationObserver(schedule);
    updates.observe(body, { childList: true, subtree: true, attributes: true, attributeFilter: ["style", "data-athlete-name", "src"] });
    if (homePodium) updates.observe(homePodium, { childList: true, attributes: true, attributeFilter: ["style"] });
    document.getElementById("athleteSearch")?.addEventListener("input", schedule);
    document.addEventListener("change", schedule);
    const ordered = document.getElementById("leaderboardOrderReset");
    if (ordered) updates.observe(ordered, { attributes: true, attributeFilter: ["hidden"] });
    // Standings (flags, last month's rank) arrive with the client data.
    let polls = 0;
    const waitForStandings = window.setInterval(() => {
        polls += 1;
        const first = body.querySelector<HTMLTableRowElement>(":scope > tr[data-athlete-name]");
        if ((first && standingOf(first.dataset["athleteName"] ?? "")) || polls > 60) {
            window.clearInterval(waitForStandings);
            schedule();
        }
    }, 250);
    schedule();
}

if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initializeLeaderboardPodium, { once: true });
} else {
    initializeLeaderboardPodium();
}
