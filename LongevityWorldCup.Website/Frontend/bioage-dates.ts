// Calculator date step (pheno and Bortz): the feel layer.
//
// bioage-dates.css owns how the step looks; this classic script decides when:
// - A single accent bar glides under the Year | Month | Day strip to the
//   field in hand.
// - A field that changes answers with a short pulse.
// - The blood draw card shows the chosen date as a small calendar page that
//   flips when the date changes (an echo of the value, aria-hidden).
// - Each section band carries the same completion pip as the biomarker
//   domains, and Continue arms (one arrow nudge) once both sections are set.
//
// Nothing here changes wording, validation or navigation.

interface LwcBioageDatesApi {
    readonly refresh: () => void;
}

interface Window {
    LwcBioageDates: LwcBioageDatesApi;
}

(() => {
    if (window.LwcBioageDates) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    function start(): void {
        const form = document.querySelector<HTMLFormElement>("#phenoAgeForm, #bortzAgeForm");
        const step = form?.querySelector<HTMLElement>("#lwc-step-1");
        const dob = step?.querySelector<HTMLFieldSetElement>("#dobFieldset");
        const year = document.getElementById("dob-year") as HTMLSelectElement | null;
        const month = document.getElementById("dob-month") as HTMLSelectElement | null;
        const day = document.getElementById("dob-day") as HTMLSelectElement | null;
        const draw = document.getElementById("blood-draw-date") as HTMLInputElement | null;
        const drawSet = draw?.closest("fieldset") as HTMLFieldSetElement | null;
        const next = document.getElementById("lwcToStep2Btn");
        if (!form || !step || !dob || !year || !month || !day || !draw || !drawSet) return;
        form.dataset.bioageDates = "";

        // ------------------------------------------------ glide under the strip
        const glide = document.createElement("span");
        glide.className = "bioage-date-glide";
        glide.setAttribute("aria-hidden", "true");
        dob.appendChild(glide);
        const selects = [year, month, day];
        let current: HTMLElement | null = null;
        function place(target: HTMLElement | null, instant = false): void {
            current = target;
            if (!target) { dob!.classList.remove("has-glide"); return; }
            const fr = dob!.getBoundingClientRect();
            const tr = target.getBoundingClientRect();
            if (instant || reduced.matches) glide.style.transition = "none";
            glide.style.setProperty("--glide-x", `${tr.left - fr.left}px`);
            glide.style.setProperty("--glide-y", `${tr.bottom - fr.top - 3}px`);
            glide.style.setProperty("--glide-w", `${tr.width}px`);
            if (!dob!.classList.contains("has-glide")) {
                glide.style.transition = "none";
                dob!.classList.add("has-glide");
            }
            void glide.offsetWidth;
            glide.style.transition = "";
        }
        selects.forEach(select => {
            select.addEventListener("focus", () => place(select));
            select.addEventListener("pointerenter", () => { if (!dob!.contains(document.activeElement)) place(select); });
        });
        dob.addEventListener("pointerleave", () => { if (!dob.contains(document.activeElement)) place(null); });
        dob.addEventListener("focusout", () => requestAnimationFrame(() => { if (!dob.contains(document.activeElement)) place(null); }));
        new ResizeObserver(() => { if (current) place(current, true); }).observe(dob);

        // ------------------------------------------------ change pulse
        function pulse(el: HTMLElement): void {
            el.classList.remove("is-ticked");
            void el.offsetWidth;
            el.classList.add("is-ticked");
            window.setTimeout(() => el.classList.remove("is-ticked"), 520);
        }

        // ------------------------------------------------ calendar page
        const page = document.createElement("span");
        page.className = "bioage-cal";
        page.setAttribute("aria-hidden", "true");
        page.innerHTML = '<span class="bioage-cal-ring"></span><span class="bioage-cal-month"></span><span class="bioage-cal-day"></span>';
        drawSet.appendChild(page);
        const monthFmt = new Intl.DateTimeFormat(document.documentElement.lang || undefined, { month: "short" });
        let shown = "";
        function paintCalendar(animate: boolean): void {
            const value = draw!.value;
            const date = value ? new Date(`${value}T00:00:00`) : null;
            const valid = !!date && !Number.isNaN(date.getTime());
            page.classList.toggle("is-empty", !valid);
            const key = valid ? value : "";
            if (key === shown) return;
            shown = key;
            const m = page.querySelector<HTMLElement>(".bioage-cal-month")!;
            const d = page.querySelector<HTMLElement>(".bioage-cal-day")!;
            const apply = (): void => {
                m.textContent = valid ? monthFmt.format(date!) : "";
                d.textContent = valid ? String(date!.getDate()) : "";
            };
            if (!animate || reduced.matches) { apply(); return; }
            const out = page.animate([{ transform: "rotateX(0)" }, { transform: "rotateX(-88deg)" }], { duration: 130, easing: "cubic-bezier(.3,0,1,1)" });
            out.onfinish = () => {
                apply();
                page.animate([{ transform: "rotateX(88deg)" }, { transform: "rotateX(0)" }], { duration: 260, easing: "cubic-bezier(.34,1.4,.64,1)" });
            };
        }

        // ------------------------------------------------ completion pips + arming
        function pipFor(set: HTMLFieldSetElement): HTMLElement {
            const legend = set.querySelector<HTMLElement>(":scope > legend")!;
            let pip = legend.querySelector<HTMLElement>(":scope > .bioage-domain-pip");
            if (!pip) {
                pip = document.createElement("span");
                pip.className = "bioage-domain-pip bioage-date-pip";
                pip.setAttribute("aria-hidden", "true");
                legend.appendChild(pip);
            }
            return pip;
        }
        const dobPip = pipFor(dob);
        const drawPip = pipFor(drawSet);
        let armed = false;
        function sync(animate: boolean): void {
            const dobDone = !!year!.value;
            const drawDone = !!draw!.value && draw!.validity.valid;
            dobPip.classList.toggle("bioage-domain-pip--complete", dobDone);
            drawPip.classList.toggle("bioage-domain-pip--complete", drawDone);
            dob!.classList.toggle("is-complete", dobDone);
            drawSet!.classList.toggle("is-complete", drawDone);
            const ready = dobDone && drawDone;
            if (next) {
                next.classList.toggle("is-armed", ready);
                if (ready && !armed && animate && !reduced.matches) {
                    next.classList.remove("is-arming");
                    void next.offsetWidth;
                    next.classList.add("is-arming");
                    window.setTimeout(() => next.classList.remove("is-arming"), 700);
                }
            }
            armed = ready;
            paintCalendar(animate);
        }

        [...selects, draw].forEach(control => control.addEventListener("change", () => {
            pulse(control);
            sync(true);
        }));
        draw.addEventListener("input", () => sync(true));

        // Restored drafts and shared links fill the fields after load without events.
        sync(false);
        let checks = 0;
        const settle = window.setInterval(() => { sync(false); if (++checks > 12) window.clearInterval(settle); }, 250);
        window.LwcBioageDates = { refresh: () => sync(false) };
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
