// Apply flow, later steps: the athlete card that assembles itself.
//
// apply-card.css owns the look; this classic script only mirrors values the
// applicant already gave (no new words):
// - From the portrait step on, the stage header carries a small athlete card:
//   the cropped portrait, the name and the flag, like a podium card.
// - On "Final details" and "Application" its chips light one by one as the
//   link, the media contact and the email become valid: link, envelope and
//   the rocket from the Apply button, the same icon language as the podium.
// - The motivation from "Finding your why" rides along as a one-line quote.
// - Sending the application stamps the card.
//
// The card is aria-hidden decoration: every value is still in its own field.
// convergence.html keeps all validation and navigation.

(() => {
    const reduced = typeof window.matchMedia === "function" ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
    const moving = (element: Element): boolean => !reduced?.matches && typeof element.animate === "function";

    interface LwcFlagsLike { getFlagIconCode?: (flag: unknown) => string }
    // convergence.html's own silent predicates, the ones that enable Next and Apply.
    interface ApplyPageLike {
        isOptionalUrl?: (value: string) => boolean;
        normalizeContactEmail?: (value: string) => string | null;
    }

    function start(): void {
        const head = document.querySelector<HTMLElement>(".convergence-stage-head");
        const title = document.getElementById("applicationStageTitle");
        if (!head || !title) return;

        const card = document.createElement("div");
        card.className = "apply-card";
        card.setAttribute("aria-hidden", "true");
        card.innerHTML = `
            <span class="apply-card-portrait"><img alt=""></span>
            <span class="apply-card-id">
                <span class="apply-card-name-row"><span class="apply-card-flag"></span><strong class="apply-card-name"></strong></span>
                <span class="apply-card-why"></span>
            </span>
            <span class="apply-card-chips">
                <i class="fa fa-link" data-chip="link"></i>
                <i class="fas fa-envelope" data-chip="media"></i>
                <i class="fas fa-rocket" data-chip="email"></i>
            </span>`;
        title.after(card);

        const $ = <T extends HTMLElement>(id: string): T | null => document.getElementById(id) as T | null;
        const portrait = card.querySelector<HTMLImageElement>(".apply-card-portrait img")!;
        const nameEl = card.querySelector<HTMLElement>(".apply-card-name")!;
        const flagEl = card.querySelector<HTMLElement>(".apply-card-flag")!;
        const whyEl = card.querySelector<HTMLElement>(".apply-card-why")!;
        const chips = {
            link: card.querySelector<HTMLElement>('[data-chip="link"]')!,
            media: card.querySelector<HTMLElement>('[data-chip="media"]')!,
            email: card.querySelector<HTMLElement>('[data-chip="email"]')!,
        };

        // A chip lights only when its field would pass the page's own check, not
        // when its error text happens to be empty (errors only show after blur).
        const page = window as unknown as ApplyPageLike;
        const value = (id: string): string => ($<HTMLInputElement>(id)?.value || "").trim();
        const linkReady = (): boolean => {
            const link = value("personalLink");
            return !!link && typeof page.isOptionalUrl === "function" && page.isOptionalUrl(link);
        };
        const mediaReady = (): boolean => !!value("mediaContact");
        const emailReady = (): boolean => typeof page.normalizeContactEmail === "function" && !!page.normalizeContactEmail(value("accountEmail"));

        function lightChip(chip: HTMLElement, on: boolean): void {
            const was = chip.classList.contains("is-lit");
            chip.classList.toggle("is-lit", on);
            if (on && !was && moving(chip)) {
                chip.animate(
                    [{ transform: "scale(.6)", opacity: .4 }, { transform: "scale(1.25)", opacity: 1, offset: .55 }, { transform: "scale(1)", opacity: 1 }],
                    { duration: 460, easing: "cubic-bezier(.34,1.4,.64,1)" }
                );
            }
        }

        let shownStage = 0;
        function sync(): void {
            const stage = Number(document.body.dataset.convergenceStage) || 1;
            // From the portrait step on, once there is someone to show.
            const profile = $<HTMLImageElement>("profileImage");
            const src = profile?.getAttribute("src") || "";
            const name = ($<HTMLInputElement>("name")?.value || "").trim();
            const show = stage >= 4 && (!!name || !!src);
            const appearing = show && !card.classList.contains("is-shown");
            card.classList.toggle("is-shown", show);
            head!.classList.toggle("is-carded", show);

            if (src && portrait.getAttribute("src") !== src) portrait.src = src;
            card.classList.toggle("has-portrait", !!src);
            nameEl.textContent = name;
            const flag = ($<HTMLInputElement>("flag")?.value || "").trim();
            const code = (window as unknown as { LwcFlags?: LwcFlagsLike }).LwcFlags?.getFlagIconCode?.(flag) || "";
            flagEl.className = "apply-card-flag" + (code ? ` fi fi-${code}` : "");
            const why = ($<HTMLTextAreaElement>("why")?.value || "").trim();
            whyEl.textContent = why ? `“${why}”` : "";

            // Chips speak only from the steps that ask for them.
            const late = stage >= 6;
            card.classList.toggle("has-chips", late);
            lightChip(chips.link, late && linkReady());
            lightChip(chips.media, late && mediaReady());
            lightChip(chips.email, stage >= 7 && emailReady());

            if (appearing && moving(card)) {
                card.animate(
                    [{ opacity: 0, transform: "translateY(10px) scale(.96)" }, { opacity: 1, transform: "none" }],
                    { duration: 420, easing: "cubic-bezier(.2,0,0,1)" }
                );
            }
            if (stage !== shownStage && shownStage && show && !appearing && moving(card)) {
                card.animate([{ transform: "translateY(-3px)" }, { transform: "none" }], { duration: 300, easing: "cubic-bezier(.2,0,0,1)" });
            }
            shownStage = stage;
        }

        // Stamp the card when the application is really sent. The page disables
        // Apply in the same click only once submission starts; a refused click
        // (a missing step, an alert) leaves it enabled and the card unstamped.
        // Capture runs before the page's own handler, so it sees the button as
        // it was when pressed.
        const apply = document.getElementById("nextButton") as HTMLButtonElement | null;
        document.addEventListener("click", event => {
            if (!apply || !(event.target instanceof Node) || !apply.contains(event.target)) return;
            if (Number(document.body.dataset.convergenceStage) !== 7 || apply.disabled) return;
            window.setTimeout(() => {
                if (!apply.disabled || Number(document.body.dataset.convergenceStage) !== 7) return;
                card.classList.remove("is-stamped");
                void card.offsetWidth;
                card.classList.add("is-stamped");
            }, 0);
        }, true);

        let queued = false;
        const schedule = (): void => { if (!queued) { queued = true; requestAnimationFrame(() => { queued = false; sync(); }); } };
        new MutationObserver(schedule).observe(document.body, { attributes: true, attributeFilter: ["data-convergence-stage"] });
        const profile = $("profileImage");
        if (profile) new MutationObserver(schedule).observe(profile, { attributes: true, attributeFilter: ["src"] });
        ["input", "change", "blur"].forEach(type => document.addEventListener(type, schedule, true));
        schedule();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
