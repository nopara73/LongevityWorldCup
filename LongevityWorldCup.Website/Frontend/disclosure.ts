// Shared disclosure layer: how anything on the site opens and closes.
//
// - Every <details> grows and shrinks to its new height instead of snapping,
//   and its content settles in a short stagger. Closing plays the same move
//   backwards before the element really closes, so `toggle` listeners and
//   saved open state still see the native value.
// - "Show all" toggles that reveal hidden siblings (profile proofs) grow their
//   region the same way and deal the new items in.
// - Flag teams get the signature move: the stacked faces in the team row drop
//   into the roster when it opens and climb back into the stack when it
//   closes. Each member row also carries a thin bar for its share of the team
//   total, so the roster reads as who carried the flag.
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

    type Running = { anim: Animation; opening: boolean; extras: Animation[] };
    const running = new WeakMap<HTMLElement, Running>();

    const visible = (el: Element): boolean => el.getClientRects().length > 0 && getComputedStyle(el).visibility !== "hidden";

    function stop(el: HTMLElement): number {
        const height = el.getBoundingClientRect().height;
        const current = running.get(el);
        if (current) {
            current.anim.cancel();
            current.extras.forEach(a => a.cancel());
            running.delete(el);
        }
        return height;
    }

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
            const ghost = face.cloneNode(false) as HTMLImageElement;
            ghost.removeAttribute("id");
            ghost.setAttribute("aria-hidden", "true");
            ghost.className = "lwc-disclosure-ghost";
            Object.assign(ghost.style, {
                left: `${to.left}px`, top: `${to.top}px`, width: `${to.width}px`, height: `${to.height}px`,
                border: cs.border, borderRadius: cs.borderRadius, objectFit: cs.objectFit, background: cs.backgroundColor,
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

    // Each member's share of the team total, as a 0–1 custom property.
    function measureShares(scope: ParentNode): void {
        scope.querySelectorAll<HTMLElement>(".flag-team").forEach(team => {
            const rows = Array.from(team.querySelectorAll<HTMLElement>(".flag-team-athletes > li"));
            const values = rows.map(li => {
                const n = parseFloat((li.querySelector(".flag-athlete-metric")?.textContent || "").replace(/[^\d.+-]/g, ""));
                return Number.isFinite(n) ? Math.max(0, -n) : 0;
            });
            const best = values.reduce((sum, v) => sum + v, 0);
            rows.forEach((li, i) => {
                li.style.setProperty("--lwc-share", best > 0 ? ((values[i] ?? 0) / best).toFixed(3) : "0");
                li.style.setProperty("--lwc-i", String(i));
            });
        });
    }

    // ------------------------------------------------------------ <details>
    // motion.css already grows and folds the panel itself (::details-content
    // block-size), so this only adds what rides on top: the face hand-off, the
    // dealt-in rows and a state attribute that keeps seats empty while closing.
    function toggle(details: HTMLDetailsElement, open = !(running.get(details)?.opening ?? details.open)): void {
        stop(details);
        if (reduced.matches) {
            details.open = open;
            return;
        }
        const isTeam = details.classList.contains("flag-team");
        const duration = open ? OPEN_MS : CLOSE_MS;
        const extras: Animation[] = [];
        if (!open && isTeam) extras.push(...flyFaces(details, false, duration));
        details.dataset.disclosure = open ? "opening" : "closing";
        details.open = open;
        if (open) {
            if (isTeam) extras.push(...flyFaces(details, true, duration));
            contentOf(details).forEach(c => extras.push(...dealItems(c)));
        }
        // An empty effect is the clock, so it follows the page's animation rate.
        const anim = details.animate([], { duration: duration + (isTeam ? (open ? 220 : 70) : 40) });
        running.set(details, { anim, opening: open, extras });
        anim.onfinish = () => {
            if (running.get(details)?.anim !== anim) return;
            running.delete(details);
            extras.forEach(a => a.cancel());
            delete details.dataset.disclosure;
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
        if (reduced.matches) return;
        const button = event.target instanceof Element ? event.target.closest<HTMLElement>(REVEAL) : null;
        const id = button?.getAttribute("aria-controls");
        const region = id ? document.getElementById(id) : null;
        if (!button || !region || region.closest(SKIP)) return;
        const start = stop(region);
        const before = new Set(Array.from(region.children).filter(visible));
        requestAnimationFrame(() => {
            const end = region.getBoundingClientRect().height;
            if (Math.abs(end - start) < 1) return;
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
            running.set(region, { anim, opening, extras });
            anim.onfinish = () => {
                if (running.get(region)?.anim !== anim) return;
                running.delete(region);
                region.style.overflow = "";
            };
        });
    }, true);

    // Flag rosters are re-rendered as filters change; keep their shares current.
    function refresh(scope: ParentNode = document): void {
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
