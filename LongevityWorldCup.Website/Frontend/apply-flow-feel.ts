// How the inside of the athlete application moves. apply-flow-feel.css owns the
// look of every state; this classic script only animates the changes CSS cannot
// describe on its own:
// - a stage change morphs the card's height and lets the new fields arrive in a
//   short stagger from the side the athlete is travelling toward;
// - field errors open and close their space instead of shoving the form;
// - files can be dropped on the photo and proof stages;
// - a freshly cropped portrait is revealed in the banner.
//
// Nothing here delays input or the DOM change itself, and reduced motion keeps
// every animation off (drag and drop still works).

interface LwcApplyFeelApi {
    readonly beginStage: (previousStage: number, stage: number) => () => void;
}

interface Window {
    LwcApplyFeel: LwcApplyFeelApi;
}

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const moving = (): boolean => !reduced.matches;
    const enter = "cubic-bezier(0, 0, 0, 1)";
    const standard = "cubic-bezier(0.2, 0, 0, 1)";
    const running = new WeakMap<Element, Animation>();

    function play(element: Element, keyframes: Keyframe[], options: KeyframeAnimationOptions): Animation | null {
        if (!moving() || typeof element.animate !== "function") return null;
        running.get(element)?.cancel();
        const animation = element.animate(keyframes, options);
        running.set(element, animation);
        const forget = (): void => {
            if (running.get(element) === animation) running.delete(element);
        };
        animation.addEventListener("finish", forget);
        animation.addEventListener("cancel", forget);
        return animation;
    }

    // 1) Stage changes ------------------------------------------------------------

    function visibleFieldParts(form: Element): HTMLElement[] {
        const parts: HTMLElement[] = [];
        for (const fieldset of Array.from(form.querySelectorAll<HTMLElement>(":scope > fieldset"))) {
            if (fieldset.style.display === "none" || fieldset.offsetParent === null) continue;
            const rows = fieldset.querySelectorAll<HTMLElement>(
                ":scope > label, :scope > input, :scope > select, :scope > textarea, :scope > p, :scope > ol,"
                + " :scope > .autocomplete-container, :scope > .convergence-application-copy, :scope > .char-counter,"
                + " :scope > div:not([style*='none']) > *, :scope > #proofImageContainer, :scope > #biomarker-checklist");
            for (const row of Array.from(rows)) {
                if (row.offsetParent !== null && row.getClientRects().length) parts.push(row);
            }
        }
        return parts.slice(0, 12);
    }

    function beginStage(previousStage: number, stage: number): () => void {
        const card = document.querySelector<HTMLElement>(".convergence-stage-layout");
        if (!card || !previousStage || previousStage === stage || !moving()) return () => {};
        const before = card.getBoundingClientRect().height;
        const forward = stage > previousStage;
        card.dataset.applyTravel = forward ? "forward" : "back";

        return () => {
            const after = card.getBoundingClientRect().height;
            // The card glides to its new height rather than snapping, so the dock and
            // footer never leap while the new stage settles.
            if (Math.abs(after - before) > 2) {
                play(card, [
                    { height: `${before}px`, overflow: "clip" },
                    { height: `${after}px`, overflow: "clip" }
                ], { duration: 280, easing: standard });
            }

            const inputForm = document.getElementById("inputForm");
            if (!inputForm) return;
            const shift = forward ? 18 : -18;
            visibleFieldParts(inputForm).forEach((part, index) => {
                play(part, [
                    { opacity: 0, translate: `${shift}px 0` },
                    { opacity: 1, translate: "0 0" }
                ], { duration: 240, delay: 50 + index * 28, easing: enter, fill: "backwards" });
            });
        };
    }

    window.LwcApplyFeel = { beginStage };

    // 2) Field errors open their space -----------------------------------------------

    const errorHeights = new WeakMap<HTMLElement, number>();

    function watchErrors(root: ParentNode): void {
        const errors = Array.from(root.querySelectorAll<HTMLElement>(".error-message"));
        if (!errors.length) return;
        const observer = new MutationObserver(records => {
            const seen = new Set<HTMLElement>();
            for (const record of records) {
                const target = (record.target instanceof HTMLElement ? record.target : record.target.parentElement)?.closest<HTMLElement>(".error-message");
                if (target && !seen.has(target)) seen.add(target);
            }
            seen.forEach(changed);
        });
        for (const error of errors) {
            observer.observe(error, { childList: true, characterData: true, subtree: true });
            if (error.textContent?.trim()) errorHeights.set(error, error.offsetHeight);
        }
    }

    function changed(error: HTMLElement): void {
        const hasText = Boolean(error.textContent?.trim());
        const previous = errorHeights.get(error) || 0;
        if (hasText) {
            const height = error.offsetHeight;
            errorHeights.set(error, height);
            if (!previous && height) {
                play(error, [
                    { height: "0px", opacity: 0, marginTop: "0px", marginBottom: "0px", overflow: "clip" },
                    { height: `${height}px`, opacity: 1, overflow: "clip" }
                ], { duration: 200, easing: enter });
                const field = error.previousElementSibling instanceof HTMLElement
                    && error.previousElementSibling.matches("input, select, textarea")
                    ? error.previousElementSibling
                    : error.parentElement?.querySelector<HTMLElement>("input, select, textarea");
                if (field) {
                    play(field, [
                        { translate: "0 0" }, { translate: "-3px 0" }, { translate: "3px 0" },
                        { translate: "-2px 0" }, { translate: "0 0" }
                    ], { duration: 260, easing: "ease-out" });
                }
            }
            return;
        }
        errorHeights.delete(error);
        if (!previous || !moving() || !error.parentElement) return;
        // The message is already gone; a spacer of its height closes gently instead.
        const spacer = document.createElement("span");
        spacer.className = "apply-error-spacer";
        spacer.setAttribute("aria-hidden", "true");
        error.after(spacer);
        const animation = spacer.animate([{ height: `${previous}px` }, { height: "0px" }], { duration: 180, easing: "cubic-bezier(0.3, 0, 1, 1)" });
        const remove = (): void => spacer.remove();
        animation.addEventListener("finish", remove);
        animation.addEventListener("cancel", remove);
    }

    // 3) Drop files on the photo and proof stages -------------------------------------

    function hasFiles(event: DragEvent): boolean {
        return Array.from(event.dataTransfer?.types || []).includes("Files");
    }

    function dropTarget(): { section: HTMLElement; input: HTMLInputElement; single: boolean } | null {
        const stage = document.body.dataset.convergenceStage;
        if (stage === "4") {
            const uploadPart = document.getElementById("uploadPart");
            const section = document.getElementById("profileUploadSection");
            const input = document.getElementById("profilePicInput") as HTMLInputElement | null;
            if (!section || !input || !uploadPart || uploadPart.style.display === "none") return null;
            return { section, input, single: true };
        }
        if (stage === "5") {
            const section = document.getElementById("proofUploadSection");
            const input = document.getElementById("proofPicInput") as HTMLInputElement | null;
            const button = document.getElementById("uploadProofButton") as HTMLButtonElement | null;
            if (!section || !input || input.disabled || button?.disabled) return null;
            return { section, input, single: false };
        }
        return null;
    }

    function setupDrop(): void {
        const card = document.querySelector<HTMLElement>(".convergence-stage-layout");
        if (!card) return;
        let depth = 0;
        let armed: HTMLElement | null = null;
        const disarm = (): void => {
            depth = 0;
            armed?.classList.remove("apply-drop-armed", "apply-drop-over");
            armed = null;
        };

        window.addEventListener("dragenter", event => {
            if (!hasFiles(event)) return;
            depth++;
            const target = dropTarget();
            if (!target) return;
            if (armed !== target.section) {
                armed?.classList.remove("apply-drop-armed", "apply-drop-over");
                armed = target.section;
                armed.classList.add("apply-drop-armed");
            }
        });
        window.addEventListener("dragleave", event => {
            if (!hasFiles(event)) return;
            depth = Math.max(0, depth - 1);
            if (!depth || !event.relatedTarget) disarm();
        });
        window.addEventListener("dragover", event => {
            if (!armed || !hasFiles(event)) return;
            const over = card.contains(event.target as Node);
            armed.classList.toggle("apply-drop-over", over);
            if (over) {
                event.preventDefault();
                if (event.dataTransfer) event.dataTransfer.dropEffect = "copy";
            }
        });
        window.addEventListener("drop", event => {
            const target = armed ? dropTarget() : null;
            const inside = card.contains(event.target as Node);
            disarm();
            if (!target || !inside || !event.dataTransfer?.files.length) return;
            event.preventDefault();
            const transfer = new DataTransfer();
            const files = Array.from(event.dataTransfer.files);
            for (const file of target.single ? files.slice(0, 1) : files) transfer.items.add(file);
            target.input.files = transfer.files;
            target.input.dispatchEvent(new Event("change", { bubbles: true }));
            play(target.section, [{ scale: "1" }, { scale: "0.985" }, { scale: "1" }], { duration: 260, easing: standard });
        });
        window.addEventListener("dragend", disarm);
    }

    // 4) Portrait reveal ----------------------------------------------------------------

    function watchPortrait(): void {
        let stage = document.body.dataset.convergenceStage;
        let hasPortrait = document.body.classList.contains("convergence-has-portrait");
        new MutationObserver(() => {
            const nextStage = document.body.dataset.convergenceStage;
            const nextPortrait = document.body.classList.contains("convergence-has-portrait");
            const arrived = nextStage === stage && nextPortrait && !hasPortrait;
            stage = nextStage;
            hasPortrait = nextPortrait;
            if (!arrived || !moving()) return;
            const visual = document.querySelector<HTMLElement>(".convergence-visual");
            if (!visual) return;
            visual.classList.remove("apply-portrait-arriving");
            void visual.offsetWidth;
            visual.classList.add("apply-portrait-arriving");
            window.setTimeout(() => visual.classList.remove("apply-portrait-arriving"), 900);
        }).observe(document.body, { attributes: true, attributeFilter: ["class", "data-convergence-stage"] });
    }

    function start(): void {
        const form = document.getElementById("inputForm");
        if (form) watchErrors(form);
        if (document.querySelector(".convergence-stage-layout")) {
            setupDrop();
            watchPortrait();
        }
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
