// Shared disclosure layer: how anything on the site opens and closes.
//
// - Every <details> grows and shrinks to its new height instead of snapping,
//   and its content settles in a short stagger. The native open state changes
//   immediately, so `toggle` listeners and saved state follow the user's action.
// - "Show all" toggles that reveal hidden siblings (profile proofs) grow their
//   region the same way and deal the new items in.
// - Flag teams get the signature move: the stacked faces in the team row drop
//   into the roster when it opens and climb back into the stack when it
//   closes. Each member row also carries a thin bar for its share of the team
//   total, so the roster reads as who carried the flag; the team row carries
//   the same pieces stacked into one strip, as long as the team's total is
//   against the best team on its clock, and opening drops each piece into
//   its bar.
//
// disclosure.css owns how every state looks. Nothing here changes wording or
// what a control does. Reduced motion keeps every behaviour and drops travel.

interface LwcDisclosureApi {
    readonly toggle: (details: HTMLDetailsElement, open?: boolean) => void;
    readonly refresh: (root?: ParentNode) => void;
}

interface Window {
    LwcDisclosure: LwcDisclosureApi;
}

(() => {
    if (window.LwcDisclosure) return;
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    root.dataset.lwcDisclosure = "";

    const OPEN_MS = 280;
    const CLOSE_MS = 220;
    const EASE_OPEN = "cubic-bezier(0.2, 0, 0, 1)";
    const EASE_CLOSE = "cubic-bezier(0.3, 0, 0.2, 1)";
    const EASE_POP = "cubic-bezier(0.34, 1.32, 0.64, 1)";

    // Pages that already animate their own disclosures opt out here or with
    // data-disclosure="off" on the element.
    const SKIP = "[data-disclosure='off'], .documentation-nav, #site-menu";

    type Running = { anim: Animation; opening: boolean; extras: Animation[]; cleanup: () => void };
    const running = new Map<HTMLElement, Running>();
    const pendingReveals = new Map<HTMLElement, number>();
    const canAnimate = (): boolean => !reduced.matches && typeof Element.prototype.animate === "function";

    const visible = (el: Element): boolean => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";

    // Flag strips fill in the first time the board shows them in a visit.
    // After that (filters, search, coming back) they are simply there.
    const STRIPS_FILLED = "lwc:flag-strips-filled";
    const settleStrips = (): void => { root.dataset.lwcStripsFilled = ""; };
    let stripsFilling = false;
    try { if (sessionStorage.getItem(STRIPS_FILLED)) settleStrips(); } catch { /* storage unavailable */ }
    document.addEventListener("animationstart", event => {
        if (stripsFilling || root.hasAttribute("data-lwc-strips-filled") || event.animationName !== "lwc-strip-fill") return;
        stripsFilling = true;
        try { sessionStorage.setItem(STRIPS_FILLED, "1"); } catch { /* storage unavailable */ }
        // The last row's last piece lands within 1.5 s.
        window.setTimeout(settleStrips, 1600);
    }, true);

    function stop(el: HTMLElement): number {
        const height = el.getBoundingClientRect().height;
        const pending = pendingReveals.get(el);
        if (pending !== undefined) cancelAnimationFrame(pending);
        pendingReveals.delete(el);
        const current = running.get(el);
        if (current) {
            running.delete(el);
            current.anim.onfinish = current.anim.oncancel = null;
            current.anim.cancel();
            current.extras.forEach(a => {
                a.cancel();
                // Cancel events are asynchronous; remove copies in this task.
                const target = a.effect instanceof KeyframeEffect ? a.effect.target : null;
                if (target instanceof Element && target.matches('.lwc-disclosure-ghost')) target.remove();
            });
            current.cleanup();
        }
        return height;
    }

    function stopAll(): void {
        for (const el of new Set([...running.keys(), ...pendingReveals.keys()])) stop(el);
    }
    reduced.addEventListener('change', () => { if (reduced.matches) stopAll(); });
    window.addEventListener('resize', stopAll);
    window.addEventListener('pagehide', stopAll);
    // Fixed copies cannot follow document or nested scroll containers.
    document.addEventListener('scroll', () => {
        for (const el of running.keys()) if (el.matches('.flag-team')) stop(el);
    }, { capture: true, passive: true });

    function contentOf(details: HTMLDetailsElement): HTMLElement[] {
        return Array.from(details.children).filter((c): c is HTMLElement => c instanceof HTMLElement && c.tagName !== "SUMMARY");
    }

    // Items inside the revealed content settle one after another.
    function dealItems(container: HTMLElement): Animation[] {
        const list = container.matches("ul, ol") ? container : container.querySelector<HTMLElement>(":scope > ul, :scope > ol");
        const items = list ? Array.from(list.children).slice(0, 14) : [container];
        return items.filter(visible).map((item, i) => item.animate(
            [{ opacity: 0, transform: "translateY(-6px)" }, { opacity: 1, transform: "none" }],
            { duration: 220, delay: 40 + i * 32, easing: EASE_OPEN, fill: "backwards" }
        ));
    }

    // ------------------------------------------------------------ flag teams
    function teamFaces(details: HTMLDetailsElement): { stack: HTMLElement[]; roster: HTMLElement[] } {
        const stack = Array.from(details.querySelectorAll<HTMLElement>(".flag-team-portraits > img"));
        const roster = Array.from(details.querySelectorAll<HTMLElement>(".flag-athlete-link > img"));
        return { stack, roster };
    }

    function flyFaces(details: HTMLDetailsElement, opening: boolean, duration: number): Animation[] {
        const { stack, roster } = teamFaces(details);
        const out: Animation[] = [];
        // The stack is hidden on narrow screens; the roster then just deals in.
        if (!stack.length || !stack.every(visible)) return out;
        // The panel clips its content, so each face travels as a fixed ghost
        // above the page while the real one waits in its row.
        roster.forEach((face, i) => {
            const seat = stack[i];
            if (!seat) return;
            const from = seat.getBoundingClientRect();
            const to = face.getBoundingClientRect();
            if (!to.width || !from.width) return;
            const cs = getComputedStyle(face);
            // A fresh image keeps responsive sources and loading effects on the real portrait.
            const ghost = document.createElement("img");
            // Keep a decoded portrait even when a cold roster is closed mid-flight.
            const portrait = face as HTMLImageElement;
            const source = !opening && portrait.naturalWidth ? portrait : seat as HTMLImageElement;
            ghost.src = source.currentSrc || source.src;
            ghost.loading = "eager";
            ghost.decoding = "sync";
            ghost.alt = "";
            ghost.setAttribute("aria-hidden", "true");
            ghost.className = "lwc-disclosure-ghost";
            Object.assign(ghost.style, {
                left: `${to.left}px`, top: `${to.top}px`, width: `${to.width}px`, height: `${to.height}px`,
                border: cs.border, borderRadius: cs.borderRadius, objectFit: cs.objectFit, objectPosition: cs.objectPosition,
                outline: cs.outline, outlineOffset: cs.outlineOffset, background: cs.backgroundColor,
            });
            document.body.appendChild(ghost);
            const dx = from.left + from.width / 2 - (to.left + to.width / 2);
            const dy = from.top + from.height / 2 - (to.top + to.height / 2);
            const s = from.width / to.width;
            const away = `translate(${dx}px, ${dy}px) scale(${s})`;
            const delay = opening ? i * 45 : (roster.length - 1 - i) * 25;
            const travel = opening ? duration + 120 : duration;
            const flight = ghost.animate(
                opening ? [{ transform: away }, { transform: "none" }] : [{ transform: "none" }, { transform: away }],
                { duration: travel, delay, easing: opening ? EASE_POP : EASE_CLOSE, fill: "both" }
            );
            flight.oncancel = () => ghost.remove();
            out.push(flight);
            // Ghosts hold on their seat until the toggle clock cancels them.
            out.push(face.animate([{ opacity: 0 }, { opacity: 0 }], { duration: travel + delay, fill: "both" }));
        });
        return out;
    }

    // Where a member's roster bar sits on screen, at its share of the track.
    // The bar is pinned by its bottom and inline edges; some engines report
    // the opposite insets of a pseudo-element as "auto", so derive them.
    function shareBar(link: HTMLElement): DOMRect | null {
        const cs = getComputedStyle(link, "::after");
        if (cs.content === "none" || cs.display === "none") return null;
        const box = link.getBoundingClientRect();
        const width = parseFloat(cs.width), height = parseFloat(cs.height);
        const inset = (near: string, far: string, size: number, span: number): number => {
            const a = parseFloat(near);
            if (Number.isFinite(a)) return a;
            return span - parseFloat(far) - size;
        };
        const left = inset(cs.left, cs.right, width, box.width);
        const top = inset(cs.top, cs.bottom, height, box.height);
        if (![left, top, width, height].every(Number.isFinite)) return null;
        const share = parseFloat(link.parentElement?.style.getPropertyValue("--lwc-share") || "") || 0;
        return new DOMRect(box.left + left, box.top + top, width * share, height);
    }

    // The team row's strip is the roster in miniature: opening drops each
    // member's piece into their own bar, closing stacks them back up.
    function flyShares(details: HTMLDetailsElement, opening: boolean, duration: number): Animation[] {
        const pieces = Array.from(details.querySelectorAll<HTMLElement>(":scope > summary > .flag-team-strip > i"));
        const links = Array.from(details.querySelectorAll<HTMLElement>(".flag-athlete-link"));
        const out: Animation[] = [];
        if (!pieces.length || !pieces.every(visible)) {
            delete details.dataset.shareFlown;
            return out;
        }
        links.forEach((link, i) => {
            const piece = pieces[i];
            const to = shareBar(link);
            if (!piece || !to) return;
            const from = piece.getBoundingClientRect();
            if (!from.width || !to.width || !to.height) return;
            const ghost = document.createElement("span");
            ghost.className = "lwc-disclosure-ghost lwc-disclosure-ghost--share";
            ghost.setAttribute("aria-hidden", "true");
            const look = getComputedStyle(piece);
            Object.assign(ghost.style, {
                left: `${to.left}px`, top: `${to.top}px`, width: `${to.width}px`, height: `${to.height}px`,
                backgroundColor: look.backgroundColor, backgroundImage: look.backgroundImage,
            });
            document.body.appendChild(ghost);
            const dx = from.left + from.width / 2 - (to.left + to.width / 2);
            const dy = from.top + from.height / 2 - (to.top + to.height / 2);
            const away = `translate(${dx}px, ${dy}px) scale(${from.width / to.width}, ${from.height / to.height})`;
            const delay = opening ? 70 + i * 60 : (links.length - 1 - i) * 30;
            const travel = opening ? duration + 180 : duration;
            const flight = ghost.animate(
                opening ? [{ transform: away }, { transform: "none" }] : [{ transform: "none" }, { transform: away }],
                { duration: travel, delay, easing: opening ? EASE_POP : EASE_CLOSE, fill: "both" }
            );
            flight.oncancel = () => ghost.remove();
            out.push(flight);
        });
        if (out.length) details.dataset.shareFlown = "";
        else delete details.dataset.shareFlown;
        return out;
    }

    // Each member's years as a 0–1 share (--lwc-share) and each team's strip
    // against the best team on the same clock (--lwc-reach), so Pro and
    // Amateur sections each read as their own stacked bar chart. Members who
    // took years off are the solid pieces; a member who aged faster is a
    // hatched piece laid back over the end (--lwc-debt), so the solid part
    // ends at the team's real net total and the strips keep the standings.
    function measureShares(scope: ParentNode): void {
        const teams = Array.from(scope.querySelectorAll<HTMLElement>(".flag-team")).map(team => {
            const rows = Array.from(team.querySelectorAll<HTMLElement>(".flag-team-athletes > li"));
            const values = rows.map(li => {
                const n = parseFloat((li.querySelector(".flag-athlete-metric")?.textContent || "").replace(/[^\d.+-]/g, ""));
                return Number.isFinite(n) ? n : 0;
            });
            const gained = values.reduce((sum, v) => sum + Math.max(0, -v), 0);
            const lost = values.reduce((sum, v) => sum + Math.max(0, v), 0);
            const most = Math.max(gained, lost);
            const shares = values.map(v => most > 0 ? Math.abs(v) / most : 0);
            rows.forEach((li, i) => {
                li.style.setProperty("--lwc-share", (shares[i] ?? 0).toFixed(3));
                li.style.setProperty("--lwc-i", String(i));
                if ((values[i] ?? 0) > 0) li.dataset.shareDebt = "";
                else delete li.dataset.shareDebt;
            });
            return { team, values, gained, clock: team.dataset.clock || "" };
        });
        const scale = new Map<string, number>();
        teams.forEach(t => scale.set(t.clock, Math.max(scale.get(t.clock) || 0, t.gained)));
        teams.forEach(({ team, values, gained, clock }, i) => {
            const top = scale.get(clock) || 0;
            team.style.setProperty("--lwc-reach", top > 0 ? Math.min(1, gained / top).toFixed(4) : "0");
            team.style.setProperty("--lwc-row", String(Math.min(i, 14)));
            drawStrip(team, values, gained, top);
        });
    }

    function drawStrip(team: HTMLElement, values: number[], gained: number, top: number): void {
        const summary = team.querySelector<HTMLElement>(":scope > summary");
        if (!summary) return;
        let strip = summary.querySelector<HTMLElement>(":scope > .flag-team-strip");
        if (!strip) {
            strip = document.createElement("span");
            strip.className = "flag-team-strip";
            strip.setAttribute("aria-hidden", "true");
            summary.appendChild(strip);
        }
        // Only add or drop pieces when the roster size changes, so the board's
        // observer settles after one pass.
        while (strip.children.length > values.length) strip.lastElementChild?.remove();
        while (strip.children.length < values.length) strip.appendChild(document.createElement("i"));
        let taken = 0;
        values.forEach((v, i) => {
            const piece = strip.children[i] as HTMLElement;
            if (v > 0) {
                // Debts stack back from the solid end, never past its start.
                const debt = top > 0 ? Math.min(v, Math.max(0, gained - taken)) / top : 0;
                piece.dataset.debt = "";
                piece.style.setProperty("--lwc-debt-at", (taken / (top || 1)).toFixed(4));
                piece.style.setProperty("--lwc-debt", debt.toFixed(4));
                piece.style.removeProperty("--lwc-share");
                taken += v;
            } else {
                delete piece.dataset.debt;
                piece.style.removeProperty("--lwc-debt-at");
                piece.style.removeProperty("--lwc-debt");
                piece.style.setProperty("--lwc-share", (gained > 0 ? -v / gained : 0).toFixed(4));
            }
        });
    }

    // ------------------------------------------------------------ <details>
    // motion.css already grows and folds the panel itself (::details-content
    // block-size), so this only adds what rides on top: the face hand-off, the
    // dealt-in rows and a state attribute that keeps seats empty while closing.
    function toggle(details: HTMLDetailsElement, open = !(running.get(details)?.opening ?? details.open)): void {
        stop(details);
        if (!canAnimate()) {
            delete details.dataset.shareFlown;
            details.open = open;
            return;
        }
        const isTeam = details.classList.contains("flag-team");
        const duration = open ? OPEN_MS : CLOSE_MS;
        const extras: Animation[] = [];
        if (!open && isTeam) extras.push(...flyFaces(details, false, duration), ...flyShares(details, false, duration));
        details.dataset.disclosure = open ? "opening" : "closing";
        details.open = open;
        if (open) {
            if (isTeam) extras.push(...flyFaces(details, true, duration), ...flyShares(details, true, duration));
            contentOf(details).forEach(c => extras.push(...dealItems(c)));
        }
        // Follow the page's animation rate and let the last staggered item land.
        const end = Math.max(duration + (isTeam ? (open ? 220 : 70) : 40),
            ...extras.map(a => Number(a.effect?.getComputedTiming().endTime) || 0));
        const anim = details.animate([], { duration: end });
        running.set(details, { anim, opening: open, extras, cleanup: () => {
            delete details.dataset.disclosure;
            if (!open) delete details.dataset.shareFlown;
        } });
        anim.oncancel = () => {
            if (running.get(details)?.anim !== anim) return;
            stop(details);
        };
        anim.onfinish = () => {
            if (running.get(details)?.anim !== anim) return;
            // Equal end times can still settle in different rendering turns.
            void Promise.all(extras.map(a => a.finished.catch(() => {}))).then(() => {
                if (running.get(details)?.anim === anim) stop(details);
            });
        };
    }

    document.addEventListener("click", event => {
        if (event.defaultPrevented || event.button !== 0 || event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
        const target = event.target instanceof Element ? event.target : null;
        const summary = target?.closest("summary");
        if (!summary) return;
        const details = summary.parentElement;
        if (!(details instanceof HTMLDetailsElement) || details.querySelector(":scope > summary") !== summary) return;
        // Links and controls inside a summary keep their own click.
        if (target !== summary && target?.closest("a, button, input, select, textarea, label") && summary.contains(target.closest("a, button, input, select, textarea, label"))) return;
        if (details.matches(SKIP) || details.closest(SKIP)) return;
        event.preventDefault();
        toggle(details);
    });

    // ------------------------------------------------- reveal-more toggles
    // Buttons that unhide more siblings in a region (aria-controls) grow it.
    const REVEAL = ".proofs-toggle, [data-disclosure='reveal']";
    document.addEventListener("click", event => {
        if (!canAnimate()) return;
        const button = event.target instanceof Element ? event.target.closest<HTMLElement>(REVEAL) : null;
        const id = button?.getAttribute("aria-controls");
        const region = id ? document.getElementById(id) : null;
        if (!button || !region || region.closest(SKIP)) return;
        const start = stop(region);
        const before = new Set(Array.from(region.children).filter(visible));
        pendingReveals.set(region, requestAnimationFrame(() => {
            pendingReveals.delete(region);
            if (reduced.matches || !region.isConnected) return;
            const end = region.getBoundingClientRect().height;
            if (Math.abs(end - start) < 1) return;
            const overflow = region.style.overflow;
            region.style.overflow = "clip";
            const opening = end > start;
            const anim = region.animate(
                [{ height: `${start}px` }, { height: `${end}px` }],
                { duration: opening ? OPEN_MS + 60 : CLOSE_MS, easing: opening ? EASE_OPEN : EASE_CLOSE }
            );
            const fresh = Array.from(region.children).filter(c => !before.has(c) && visible(c)) as HTMLElement[];
            const extras = fresh.slice(0, 24).map((item, i) => item.animate(
                [{ opacity: 0, transform: "translateY(8px) scale(.94)" }, { opacity: 1, transform: "none" }],
                { duration: 240, delay: 30 + i * 28, easing: EASE_OPEN, fill: "backwards" }
            ));
            running.set(region, { anim, opening, extras, cleanup: () => { region.style.overflow = overflow; } });
            anim.oncancel = () => { if (running.get(region)?.anim === anim) stop(region); };
            anim.onfinish = () => {
                if (running.get(region)?.anim !== anim) return;
                region.style.overflow = overflow;
                // Keep staggered tiles cancellable after the height has settled.
                void Promise.all(extras.map(a => a.finished.catch(() => {}))).then(() => {
                    if (running.get(region)?.anim === anim) stop(region);
                });
            };
        }));
    }, true);

    // Flag rosters are re-rendered as filters change; keep their shares current.
    function refresh(scope: ParentNode = document): void {
        for (const el of running.keys()) if (!el.isConnected) stop(el);
        measureShares(scope);
    }
    const watch = (): void => {
        refresh();
        const board = document.getElementById("flagLeaderboard");
        if (board) new MutationObserver(() => refresh(board)).observe(board, { childList: true, subtree: true, characterData: true });
    };
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", watch, { once: true });
    else watch();

    window.LwcDisclosure = { toggle, refresh };
})();
