// Calculator date step (pheno and Bortz): the feel layer.
//
// bioage-dates.css owns how the step looks; this classic script decides when:
// - A single accent bar glides under the Year | Month | Day strip to the
//   field in hand.
// - A field that changes answers with a short pulse.
// - The blood draw card shows the chosen date as a small calendar page that
//   flips when the date changes (an echo of the value, aria-hidden).
// - Continue arms (one arrow nudge) when the calculator accepts both dates.
// The shared date sheet supplies the completion pips and privacy feedback.
//
// Nothing here changes wording, validation or navigation.

interface LwcBioageDatesApi {
    readonly refresh: () => void;
}

interface Window {
    LwcBioageDates?: LwcBioageDatesApi;
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
        const monthFmt = new Intl.DateTimeFormat(document.documentElement.lang || undefined, { month: "short", timeZone: "UTC" });
        let shown: string | null = null;
        let flip: Animation | null = null;
        function paintCalendar(animate: boolean): void {
            const value = draw!.value;
            // A date input represents a calendar day, independent of local DST.
            const date = draw!.valueAsDate;
            const valid = !!date;
            page.classList.toggle("is-empty", !valid);
            const key = valid ? value : "";
            if (key === shown && animate) return;
            if (flip) { flip.onfinish = null; flip.cancel(); flip = null; }
            shown = key;
            const m = page.querySelector<HTMLElement>(".bioage-cal-month")!;
            const d = page.querySelector<HTMLElement>(".bioage-cal-day")!;
            const apply = (): void => {
                m.textContent = valid ? monthFmt.format(date!) : "";
                d.textContent = valid ? String(date!.getUTCDate()) : "";
            };
            if (!animate || reduced.matches || !valid) { apply(); return; }
            const out = flip = page.animate([{ transform: "rotateX(0)" }, { transform: "rotateX(-88deg)" }], { duration: 130, easing: "cubic-bezier(.3,0,1,1)" });
            out.onfinish = () => {
                apply();
                flip = page.animate([{ transform: "rotateX(88deg)" }, { transform: "rotateX(0)" }], { duration: 260, easing: "cubic-bezier(.34,1.4,.64,1)" });
            };
        }

        // ------------------------------------------------ completion pips + arming
        let armed = false;
        function sync(animate: boolean): void {
            const dobDone = !!year!.value && year!.validity.valid;
            const drawDone = !!draw!.value && draw!.validity.valid;
            dob!.classList.toggle("is-complete", dobDone);
            drawSet!.classList.toggle("is-complete", drawDone);
            const ready = window.LwcBioageFlow.hasValidDates(form!);
            if (next) {
                next.classList.toggle("is-armed", ready);
                if (!ready || reduced.matches) next.classList.remove("is-arming");
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

        // Bubble after the calculators have clamped days and applied minor privacy.
        form.addEventListener("change", event => {
            const control = event.target;
            if (!(control instanceof HTMLElement) || ![...selects, draw].includes(control as HTMLInputElement)) return;
            pulse(control);
            sync(true);
            if (current) place(current, true);
        });
        draw.addEventListener("input", () => sync(true));

        window.LwcBioageDates = { refresh: () => sync(false) };
        window.addEventListener("pageshow", () => sync(false));
        reduced.addEventListener("change", () => sync(false));
        sync(false);
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
