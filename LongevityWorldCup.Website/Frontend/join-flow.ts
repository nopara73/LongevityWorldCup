// /join "Choose your track" as one flow (join-flow.css owns the look).
//
// - Arrival: the two track cards are dealt in from the middle, Amateur from
//   the left and Professional from the right, the prize ribbon swings on last
//   and the entry prices count up to their amounts.
// - Comparing: pointing at a row in one card (entry, testing window, clocks)
//   lights the same row in the other card and joins them with a beam across
//   the gap, so the two tracks read side by side, row by row.
// - Choosing: the chosen card stamps and the other folds away; the chosen card
//   is named for the cross-document view transition, so it morphs into the
//   calculator's form on the next page (entry-journey.css names the form).
//
// play-menu.ts keeps every behaviour (pricing, routes, docking). Nothing here
// changes wording; reduced motion keeps the comparison and drops travel.

(() => {
    const panel = document.getElementById("joinTrackPanel");
    const deck = panel?.querySelector<HTMLElement>(".play-join-cards");
    if (!panel || !deck) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    panel.dataset.joinFlow = "";

    const cards = Array.from(deck.querySelectorAll<HTMLElement>(".play-join-card"));

    // ------------------------------------------------ arrival
    let dealt = false;
    function countPrice(el: HTMLElement | null, delay: number): void {
        if (!el || reduced.matches) return;
        const text = (el.textContent || "").trim();
        const match = /^\$(\d+)$/.exec(text);
        if (!match) return; // discounted or free-pass prices stay as rendered
        const target = Number(match[1]);
        const clock = (): number => Number(document.timeline?.currentTime ?? performance.now());
        const start = clock() + delay;
        el.classList.add("is-counting");
        const tick = (): void => {
            if ((el.textContent || "").trim() !== text && !el.classList.contains("is-counting")) return;
            const t = Math.min(1, Math.max(0, (clock() - start) / 700));
            const eased = 1 - Math.pow(1 - t, 3);
            el.textContent = "$" + Math.round(target * eased);
            if (t < 1) requestAnimationFrame(tick);
            else { el.textContent = text; el.classList.remove("is-counting"); }
        };
        el.textContent = "$0";
        requestAnimationFrame(tick);
    }
    function deal(): void {
        if (dealt || panel!.hidden) return;
        dealt = true;
        if (reduced.matches) return;
        panel!.classList.remove("is-dealing");
        void panel!.offsetWidth;
        panel!.classList.add("is-dealing");
        countPrice(deck!.querySelector<HTMLElement>(".join-amateur-entry-cell .pro-new-price"), 260);
        countPrice(document.getElementById("joinProEntryPrice"), 340);
        const done = (event: AnimationEvent): void => {
            if (event.animationName !== "join-deal-pro") return;
            panel!.classList.remove("is-dealing");
            panel!.removeEventListener("animationend", done);
        };
        panel!.addEventListener("animationend", done);
    }
    new MutationObserver(() => { if (panel.hidden) dealt = false; else deal(); })
        .observe(panel, { attributes: true, attributeFilter: ["hidden"] });
    deal();

    // ------------------------------------------------ comparing
    const beam = document.createElement("span");
    beam.className = "join-twin-beam";
    beam.setAttribute("aria-hidden", "true");
    deck.appendChild(beam);
    const rowsOf = (card: HTMLElement): HTMLElement[] =>
        Array.from(card.querySelectorAll<HTMLElement>(".play-join-meta > dt"));
    let lit: HTMLElement[] = [];
    function clearTwin(): void {
        lit.forEach(el => el.classList.remove("is-twin-row"));
        lit = [];
        beam.classList.remove("is-on");
    }
    function lightTwin(index: number): void {
        clearTwin();
        if (cards.length < 2) return;
        const pairs = cards.map(card => {
            const dt = rowsOf(card)[index];
            return dt ? [dt, dt.nextElementSibling as HTMLElement | null] : null;
        });
        if (pairs.some(p => !p)) return;
        pairs.forEach(pair => pair!.forEach(el => { if (el) { el.classList.add("is-twin-row"); lit.push(el); } }));
        // The beam runs across the gap at the rows' height.
        const dr = deck!.getBoundingClientRect();
        const a = pairs[0]![1]?.getBoundingClientRect() ?? pairs[0]![0]!.getBoundingClientRect();
        const b = pairs[1]![0]!.getBoundingClientRect();
        const y = (a.top + a.height / 2 + b.top + b.height / 2) / 2 - dr.top;
        const left = Math.min(a.right, cards[0]!.getBoundingClientRect().right) - dr.left;
        const right = b.left - dr.left;
        if (right - left < 4 || Math.abs(a.top - b.top) > 160) return; // stacked on phones
        beam.style.setProperty("--beam-y", `${y}px`);
        beam.style.setProperty("--beam-x", `${left}px`);
        beam.style.setProperty("--beam-w", `${right - left}px`);
        beam.classList.add("is-on");
    }
    cards.forEach(card => {
        rowsOf(card).forEach((dt, index) => {
            const dd = dt.nextElementSibling as HTMLElement | null;
            [dt, dd].forEach(el => {
                el?.addEventListener("pointerenter", () => lightTwin(index));
                // Touch has no hover: a tap on a row lights its twin.
                el?.addEventListener("pointerdown", event => { if (event.pointerType !== "mouse") lightTwin(index); });
            });
        });
        card.addEventListener("pointerleave", event => { if (event.pointerType === "mouse") clearTwin(); });
    });

    // ------------------------------------------------ choosing
    function choose(card: HTMLElement): void {
        if (reduced.matches) return;
        panel!.dataset.joinChosen = card.classList.contains("play-join-card--pro") ? "pro" : "amateur";
        card.classList.remove("is-stamped");
        void card.offsetWidth;
        card.classList.add("is-stamped");
        // The chosen card carries over into the calculator's form.
        card.style.setProperty("view-transition-name", "lwc-join-ticket");
    }
    cards.forEach(card => {
        card.querySelectorAll<HTMLElement>(".flow-action").forEach(button => {
            button.addEventListener("click", () => choose(card), true);
        });
    });
    // Docked phone actions live outside the cards; map them by order.
    document.addEventListener("click", event => {
        const button = (event.target as Element | null)?.closest?.("#joinStartAmateurBtn, #joinMobileStartAmateurBtn, #joinGoProButton");
        if (!button || deck!.contains(button)) return;
        const pro = button.id === "joinGoProButton";
        const card = cards.find(c => c.classList.contains("play-join-card--pro") === pro);
        if (card) choose(card);
    }, true);
    // Returning with Back clears the pick.
    window.addEventListener("pageshow", () => {
        delete panel.dataset.joinChosen;
        cards.forEach(card => { card.classList.remove("is-stamped"); card.style.removeProperty("view-transition-name"); });
    });
})();
