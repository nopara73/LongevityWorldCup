// Calculator biomarker step (pheno and Bortz) as a guided run.
//
// bioage-run.css owns the look; this classic script only measures and times:
// - Example values read as examples: empty fields are marked so the CSS can
//   set their sample numbers apart from entered ones.
// - A lane glides to the row in hand as focus moves down the sheet.
// - Entering a valid value pops the row's check and sends a dot flying up to
//   that row's pip in its domain band, where the pip catches it.
// - Rows deal in down the sheet each time the step opens.
// - The next empty row's circle breathes, so the eye knows where to go.
// - The dock's progress bar pulses each time it grows.
//
// bioage-flow.ts keeps every behaviour (validation, completion, live readout).
// Nothing here adds words; reduced motion keeps the states and drops travel.

(() => {
    const form = document.querySelector<HTMLFormElement>("#phenoAgeForm, #bortzAgeForm");
    const step = form?.querySelector<HTMLElement>("#lwc-step-2");
    if (!form || !step) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    form.dataset.bioageRun = "";

    const cards = (): HTMLElement[] => Array.from(step!.querySelectorAll<HTMLElement>(".biomarker-card"));
    const inputOf = (card: HTMLElement): HTMLInputElement | null => card.querySelector<HTMLInputElement>(".input-group input");

    // ------------------------------------------------ examples vs entries
    function markEmpty(input: HTMLInputElement): void {
        input.closest(".input-group")?.classList.toggle("is-sample", !input.value);
    }

    // ------------------------------------------------ the lane
    const lane = document.createElement("span");
    lane.className = "bioage-run-lane";
    lane.setAttribute("aria-hidden", "true");
    step.appendChild(lane);
    let laned: HTMLElement | null = null;
    function placeLane(card: HTMLElement | null, instant = false): void {
        if (!card || !card.getClientRects().length) { lane.classList.remove("is-on"); laned = null; return; }
        const sr = step!.getBoundingClientRect();
        const cr = card.getBoundingClientRect();
        if (instant || !lane.classList.contains("is-on") || reduced.matches) lane.classList.add("is-snapping");
        lane.style.setProperty("--lane-y", `${cr.top - sr.top}px`);
        lane.style.setProperty("--lane-x", `${cr.left - sr.left}px`);
        lane.style.setProperty("--lane-w", `${cr.width}px`);
        lane.style.setProperty("--lane-h", `${cr.height}px`);
        lane.classList.add("is-on");
        void lane.offsetWidth;
        lane.classList.remove("is-snapping");
        laned = card;
    }
    step.addEventListener("focusin", event => {
        const card = (event.target as Element | null)?.closest?.<HTMLElement>(".biomarker-card") ?? null;
        placeLane(card);
    });
    step.addEventListener("focusout", () => requestAnimationFrame(() => {
        if (!step!.contains(document.activeElement)) placeLane(null);
    }));
    const relane = (): void => { if (laned) placeLane(laned, true); };
    window.addEventListener("resize", relane, { passive: true });
    new ResizeObserver(relane).observe(step);

    // ------------------------------------------------ next empty row
    function markNext(): void {
        const list = cards().filter(card => card.getClientRects().length);
        const next = list.find(card => !card.classList.contains("biomarker-card--complete"));
        // toggle with force writes only on a real change, so the row observers stay quiet.
        cards().forEach(card => card.classList.toggle("is-next", card === next));
    }

    // ------------------------------------------------ completion flight
    function pipFor(card: HTMLElement): HTMLElement | null {
        const fieldset = card.closest("fieldset");
        if (!fieldset) return null;
        const index = Array.from(fieldset.querySelectorAll(".biomarker-card")).indexOf(card);
        return fieldset.querySelectorAll<HTMLElement>(":scope > legend .bioage-domain-pip")[index] ?? null;
    }
    function fly(card: HTMLElement): void {
        const from = card.querySelector<HTMLElement>(".toggle-icon");
        const pip = pipFor(card);
        from?.classList.remove("is-landed");
        void from?.offsetWidth;
        from?.classList.add("is-landed");
        if (reduced.matches || !from || !pip || !pip.getClientRects().length) return;
        const a = from.getBoundingClientRect();
        const b = pip.getBoundingClientRect();
        const dot = document.createElement("span");
        dot.className = "bioage-run-spark";
        dot.setAttribute("aria-hidden", "true");
        dot.style.left = `${a.left + a.width / 2}px`;
        dot.style.top = `${a.top + a.height / 2}px`;
        document.body.appendChild(dot);
        const dx = b.left + b.width / 2 - (a.left + a.width / 2);
        const dy = b.top + b.height / 2 - (a.top + a.height / 2);
        // An arc: up and over toward the band, shrinking into the pip.
        const flight = dot.animate([
            { transform: "translate(-50%, -50%) scale(1)", opacity: 1 },
            { transform: `translate(calc(-50% + ${dx * 0.55}px), calc(-50% + ${dy * 0.55 - 26}px)) scale(1.15)`, opacity: 1, offset: 0.55 },
            { transform: `translate(calc(-50% + ${dx}px), calc(-50% + ${dy}px)) scale(.55)`, opacity: 0.9 }
        ], { duration: 560, easing: "cubic-bezier(.3,0,.2,1)" });
        flight.onfinish = flight.oncancel = () => {
            dot.remove();
            pip.classList.remove("is-caught");
            void pip.offsetWidth;
            pip.classList.add("is-caught");
        };
    }
    // Completion is owned by bioage-flow.ts; follow its class.
    const complete = new WeakMap<HTMLElement, boolean>();
    let settled = false;
    function observeCards(): void {
        cards().forEach((card, i) => {
            card.style.setProperty("--run-i", String(Math.min(i, 12)));
            if (complete.has(card)) return;
            complete.set(card, card.classList.contains("biomarker-card--complete"));
            new MutationObserver(() => {
                const now = card.classList.contains("biomarker-card--complete");
                const was = complete.get(card);
                if (now === was) return;
                complete.set(card, now);
                if (now && settled) fly(card);
                markNext();
            }).observe(card, { attributes: true, attributeFilter: ["class"] });
            const input = inputOf(card);
            if (input) {
                markEmpty(input);
                input.addEventListener("input", () => markEmpty(input));
                input.addEventListener("change", () => markEmpty(input));
            }
        });
        markNext();
    }
    observeCards();
    new MutationObserver(records => {
        if (records.some(r => Array.from(r.addedNodes).some(n => n instanceof HTMLElement && n !== lane))) observeCards();
    }).observe(step, { childList: true, subtree: true });
    // Restored drafts complete rows without a visitor; only later entries fly.
    window.setTimeout(() => { settled = true; cards().forEach(c => { const i = inputOf(c); if (i) markEmpty(i); }); markNext(); }, 1200);

    // ------------------------------------------------ dock progress
    const watchProgress = (): void => {
        // bioage-flow.ts fills the bar through --bioage-entry-progress.
        const bar = document.querySelector<HTMLElement>(".bioage-live-progress");
        if (!bar) return;
        const read = (): number => parseFloat(bar.style.getPropertyValue("--bioage-entry-progress")) || 0;
        let last = read();
        new MutationObserver(() => {
            const now = read();
            const grew = now > last;
            last = now;
            if (!grew || reduced.matches) return;
            bar.classList.remove("is-growing");
            void bar.offsetWidth;
            bar.classList.add("is-growing");
        }).observe(bar, { attributes: true, attributeFilter: ["style"] });
    };
    if (document.readyState === "complete") watchProgress();
    else window.addEventListener("load", () => window.setTimeout(watchProgress, 300), { once: true });
})();
