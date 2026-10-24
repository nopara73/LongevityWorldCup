// Shared form-control feel. form-controls.css owns every look; this classic
// script only adds the parts CSS cannot see:
//   - labels learn which field is focused or invalid (data-lwc-label);
//   - segmented groups get one highlight that slides between options;
//   - [data-lwc-stepper] number fields get − / + buttons with hold-to-repeat;
//   - error slots ease their height so the form below glides, never jumps;
//   - a field that turns invalid gives one small shake;
//   - [data-lwc-dropzone] areas light up while a file is dragged over them;
//   - range inputs expose their fill as --lwc-range-fill.
//
// Motion follows LwcMotion's gate (first trusted press, reduced motion off).
// Nothing here changes values, validation or submission.

interface LwcFormControlsApi {
    readonly refresh: (root?: ParentNode) => void;
}

interface Window {
    LwcFormControls: LwcFormControlsApi;
}

(() => {
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const textField = 'input:not([type="checkbox"]):not([type="radio"]):not([type="range"]):not([type="file"]):not([type="hidden"]), select, textarea';
    const errorSlot = ".error-message, .profile-field-error";

    function motionOn(): boolean {
        if (reduced.matches) return false;
        const gate = window.LwcMotion;
        return gate ? gate.enabled() : document.documentElement.dataset["lwcMotion"] === "on";
    }

    function play(element: Element, frames: Keyframe[], options: KeyframeAnimationOptions): void {
        if (!motionOn() || typeof (element as HTMLElement).animate !== "function") return;
        try { (element as HTMLElement).animate(frames, options); }
        catch { /* Decorative feedback never interrupts the interaction. */ }
    }

    // ---------------------------------------------------------------- labels
    function labelsFor(field: Element): HTMLElement[] {
        const found = new Set<HTMLElement>();
        const labels = (field as HTMLInputElement).labels;
        if (labels) for (const label of Array.from(labels)) found.add(label);
        const ids = field.getAttribute("aria-labelledby");
        if (ids) {
            for (const id of ids.split(/\s+/)) {
                const element = id ? document.getElementById(id) : null;
                if (element) found.add(element);
            }
        }
        // Custom pickers label their wrapper rather than the hidden native field.
        const picker = field.closest("[aria-labelledby]");
        if (picker && picker !== field) {
            for (const id of (picker.getAttribute("aria-labelledby") || "").split(/\s+/)) {
                const element = id ? document.getElementById(id) : null;
                if (element) found.add(element);
            }
        }
        return Array.from(found).filter(label => label.offsetParent !== null || label.getClientRects().length > 0);
    }

    function setLabelToken(field: Element, token: string, on: boolean): void {
        for (const label of labelsFor(field)) {
            const tokens = new Set((label.dataset["lwcLabel"] || "").split(/\s+/).filter(Boolean));
            if (on) tokens.add(token); else tokens.delete(token);
            if (!label.contains(field)) tokens.add("block");
            label.dataset["lwcLabel"] = Array.from(tokens).join(" ");
        }
    }

    document.addEventListener("focusin", event => {
        const field = event.target;
        if (field instanceof Element && field.matches(textField)) setLabelToken(field, "focus", true);
    });

    document.addEventListener("focusout", event => {
        const field = event.target;
        if (field instanceof Element && field.matches(textField)) setLabelToken(field, "focus", false);
    });

    // ------------------------------------------------------ segmented glide
    interface SegmentGroup {
        readonly group: string;
        readonly item: string;
        readonly active: string;
        readonly skip?: string;
    }

    // The options must be direct children of the group. Leaderboard view
    // chips and the profile dialog's clock tabs are adopted separately.
    const segmentGroups: readonly SegmentGroup[] = [
        { group: ".lmx-tabs", item: '[role="tab"]', active: '[aria-selected="true"]' },
        { group: ".lmx-identity-options", item: "label", active: ":has(input:checked)" },
        { group: '.age-clock-switch [role="tablist"]', item: '[role="tab"]', active: '[aria-selected="true"]', skip: "#detailsModal" },
        { group: "[data-lwc-segment-group]", item: ":scope > :not(.lwc-segment-glide)", active: '[aria-selected="true"], [aria-pressed="true"], [aria-current="true"], .active, :has(> input:checked)' },
    ];

    const enhanced = new WeakMap<HTMLElement, { glide: HTMLElement; config: SegmentGroup; current: HTMLElement | null }>();
    const resize = typeof ResizeObserver === "function" ? new ResizeObserver(entries => {
        for (const entry of entries) placeGlide(entry.target as HTMLElement, false);
    }) : null;

    function itemsOf(group: HTMLElement, config: SegmentGroup): HTMLElement[] {
        return Array.from(group.children).filter((child): child is HTMLElement =>
            child instanceof HTMLElement && !child.classList.contains("lwc-segment-glide") && child.matches(config.item.replace(":scope > ", "")));
    }

    function activeOf(group: HTMLElement, config: SegmentGroup): HTMLElement | null {
        return itemsOf(group, config).find(item => !item.hidden && item.matches(config.active)) || null;
    }

    function captureSurface(glide: HTMLElement, item: HTMLElement): void {
        // Read the option's own chosen look before the glide takes it over.
        // Suspend the option's own transition so the read is its settled look.
        delete item.dataset["lwcSegmentOn"];
        const previous = item.style.transition;
        item.style.transition = "none";
        const style = getComputedStyle(item);
        glide.style.setProperty("--lwc-glide-bg", style.backgroundColor);
        glide.style.setProperty("--lwc-glide-shadow", style.boxShadow);
        glide.style.setProperty("--lwc-glide-radius", style.borderRadius);
        item.style.transition = previous;
    }

    function placeGlide(group: HTMLElement, recapture: boolean): void {
        const state = enhanced.get(group);
        if (!state) return;
        const active = activeOf(group, state.config);
        if (state.current && state.current !== active) delete state.current.dataset["lwcSegmentOn"];
        if (!active || active.offsetWidth === 0) {
            state.glide.removeAttribute("data-ready");
            state.current = null;
            return;
        }
        if (recapture || state.current !== active) captureSurface(state.glide, active);
        active.dataset["lwcSegmentOn"] = "";
        state.current = active;
        state.glide.style.setProperty("--lwc-glide-x", `${active.offsetLeft}px`);
        state.glide.style.setProperty("--lwc-glide-y", `${active.offsetTop}px`);
        state.glide.style.setProperty("--lwc-glide-w", `${active.offsetWidth}px`);
        state.glide.style.setProperty("--lwc-glide-h", `${active.offsetHeight}px`);
        if (!state.glide.hasAttribute("data-ready")) {
            // First placement lands without travelling.
            requestAnimationFrame(() => state.glide.setAttribute("data-ready", ""));
        }
    }

    function enhanceSegments(root: ParentNode): void {
        for (const config of segmentGroups) {
            for (const group of Array.from(root.querySelectorAll<HTMLElement>(config.group))) {
                if (enhanced.has(group) || (config.skip && group.closest(config.skip))) continue;
                const glide = document.createElement("span");
                glide.className = "lwc-segment-glide";
                glide.setAttribute("aria-hidden", "true");
                group.dataset["lwcSegmented"] = "";
                group.prepend(glide);
                enhanced.set(group, { glide, config, current: null });
                resize?.observe(group);
                new MutationObserver(() => placeGlide(group, false)).observe(group, {
                    subtree: true,
                    attributes: true,
                    attributeFilter: ["aria-selected", "aria-pressed", "aria-current", "class", "hidden", "checked"],
                });
                group.addEventListener("change", () => placeGlide(group, false));
                placeGlide(group, true);
            }
        }
    }

    // Theme changes alter the chosen surface; read it again.
    window.matchMedia("(prefers-color-scheme: dark)").addEventListener?.("change", () => {
        for (const group of Array.from(document.querySelectorAll<HTMLElement>("[data-lwc-segmented]"))) placeGlide(group, true);
    });

    // -------------------------------------------------------------- steppers
    function nudge(input: HTMLInputElement, direction: 1 | -1): void {
        play(input, [
            { translate: `0 ${direction * -3}px`, opacity: 0.55 },
            { translate: "0 0", opacity: 1 },
        ], { duration: 160, easing: "cubic-bezier(0.34, 1.4, 0.64, 1)" });
    }

    function step(input: HTMLInputElement, direction: 1 | -1): void {
        if (input.disabled || input.readOnly) return;
        const before = input.value;
        try {
            if (direction > 0) input.stepUp(); else input.stepDown();
        } catch {
            return;
        }
        if (input.value === before) return;
        input.dispatchEvent(new Event("input", { bubbles: true }));
        input.dispatchEvent(new Event("change", { bubbles: true }));
        nudge(input, direction);
        syncStepper(input);
    }

    function syncStepper(input: HTMLInputElement): void {
        const wrapper = input.closest(".lwc-stepper");
        if (!wrapper) return;
        const value = input.valueAsNumber;
        const min = input.min === "" ? -Infinity : Number(input.min);
        const max = input.max === "" ? Infinity : Number(input.max);
        const down = wrapper.querySelector<HTMLButtonElement>('[data-step="down"]');
        const up = wrapper.querySelector<HTMLButtonElement>('[data-step="up"]');
        if (down) down.disabled = input.disabled || (Number.isFinite(value) && value <= min);
        if (up) up.disabled = input.disabled || (Number.isFinite(value) && value >= max);
    }

    function stepperButton(input: HTMLInputElement, direction: 1 | -1): HTMLButtonElement {
        const button = document.createElement("button");
        button.type = "button";
        button.className = "lwc-stepper-button";
        button.dataset["step"] = direction > 0 ? "up" : "down";
        button.tabIndex = -1;
        button.setAttribute("aria-label", direction > 0 ? "Increase" : "Decrease");
        if (input.id) button.setAttribute("aria-controls", input.id);
        let delay = 0;
        let repeat = 0;
        const stop = () => {
            window.clearTimeout(delay);
            window.clearInterval(repeat);
        };
        button.addEventListener("pointerdown", event => {
            if (event.button !== 0) return;
            event.preventDefault();
            step(input, direction);
            delay = window.setTimeout(() => {
                repeat = window.setInterval(() => step(input, direction), 70);
            }, 380);
        });
        for (const type of ["pointerup", "pointerleave", "pointercancel"]) button.addEventListener(type, stop);
        // Keyboard and assistive activation without a pointer.
        button.addEventListener("click", event => {
            if (event.detail === 0) step(input, direction);
        });
        return button;
    }

    function enhanceSteppers(root: ParentNode): void {
        for (const input of Array.from(root.querySelectorAll<HTMLInputElement>('input[type="number"][data-lwc-stepper]'))) {
            if (input.closest(".lwc-stepper")) continue;
            const wrapper = document.createElement("span");
            wrapper.className = "lwc-stepper";
            input.replaceWith(wrapper);
            wrapper.append(stepperButton(input, -1), input, stepperButton(input, 1));
            input.addEventListener("input", () => syncStepper(input));
            syncStepper(input);
        }
    }

    // Arrow keys on any number field get the same tick as the buttons.
    document.addEventListener("keydown", event => {
        const input = event.target;
        if (!(input instanceof HTMLInputElement) || input.type !== "number") return;
        if (event.key !== "ArrowUp" && event.key !== "ArrowDown") return;
        const before = input.value;
        requestAnimationFrame(() => {
            if (input.value !== before) nudge(input, event.key === "ArrowUp" ? 1 : -1);
        });
    });

    // ---------------------------------------------------------------- ranges
    function syncRange(input: HTMLInputElement): void {
        const min = input.min === "" ? 0 : Number(input.min);
        const max = input.max === "" ? 100 : Number(input.max);
        const value = Number(input.value);
        const fill = max > min ? ((value - min) / (max - min)) * 100 : 0;
        input.style.setProperty("--lwc-range-fill", `${Math.min(100, Math.max(0, fill))}%`);
    }

    document.addEventListener("input", event => {
        const input = event.target;
        if (input instanceof HTMLInputElement && input.type === "range") syncRange(input);
    });

    // ------------------------------------------------- error slots and shake
    const slotHeights = new WeakMap<Element, number>();

    function easeSlot(slot: HTMLElement): void {
        const before = slotHeights.get(slot) ?? 0;
        const after = slot.offsetHeight;
        slotHeights.set(slot, after);
        if (before === after || !motionOn()) return;
        play(slot, [
            { height: `${before}px`, overflow: "hidden" },
            { height: `${after}px`, overflow: "hidden" },
        ], { duration: after > before ? 200 : 160, easing: after > before ? "cubic-bezier(0, 0, 0, 1)" : "cubic-bezier(0.3, 0, 1, 1)" });
    }

    function shake(field: Element): void {
        play(field, [
            { translate: "0 0" },
            { translate: "-4px 0" },
            { translate: "3px 0" },
            { translate: "-2px 0" },
            { translate: "0 0" },
        ], { duration: 280, easing: "cubic-bezier(0.2, 0, 0, 1)" });
    }

    function watchFeedback(): void {
        for (const slot of Array.from(document.querySelectorAll<HTMLElement>(errorSlot))) slotHeights.set(slot, slot.offsetHeight);
        new MutationObserver(records => {
            const slots = new Set<HTMLElement>();
            for (const record of records) {
                if (record.type === "attributes") {
                    const field = record.target as Element;
                    const invalid = field.getAttribute("aria-invalid") === "true";
                    setLabelToken(field, "invalid", invalid);
                    if (invalid && record.oldValue !== "true") shake(field);
                    continue;
                }
                const node = record.target;
                const element = node instanceof Element ? node : node.parentElement;
                const slot = element?.closest<HTMLElement>(errorSlot);
                if (slot) slots.add(slot);
            }
            for (const slot of slots) easeSlot(slot);
        }).observe(document.body, {
            subtree: true,
            childList: true,
            characterData: true,
            attributes: true,
            attributeOldValue: true,
            attributeFilter: ["aria-invalid"],
        });
    }

    // ------------------------------------------------------------ drop zones
    let dropTarget: HTMLElement | null = null;

    function setDropTarget(next: HTMLElement | null): void {
        if (dropTarget === next) return;
        if (dropTarget) dropTarget.dataset["lwcDropzone"] = "";
        dropTarget = next;
        if (dropTarget) dropTarget.dataset["lwcDropzone"] = "over";
    }

    function carriesFiles(event: DragEvent): boolean {
        return Array.from(event.dataTransfer?.types || []).includes("Files");
    }

    document.addEventListener("dragover", event => {
        if (!carriesFiles(event)) return;
        const target = event.target instanceof Element ? event.target.closest<HTMLElement>("[data-lwc-dropzone]") : null;
        setDropTarget(target);
    });
    for (const type of ["drop", "dragend"]) document.addEventListener(type, () => setDropTarget(null));
    document.addEventListener("dragleave", event => {
        if (!event.relatedTarget) setDropTarget(null);
    });

    // ------------------------------------------------------------------ init
    function refresh(root: ParentNode = document): void {
        enhanceSegments(root);
        enhanceSteppers(root);
        for (const range of Array.from(root.querySelectorAll<HTMLInputElement>('input[type="range"]'))) syncRange(range);
    }

    window.LwcFormControls = { refresh };

    function start(): void {
        refresh();
        watchFeedback();
        // Pages render some groups later (tabs after sign-in, dialogs on open).
        let pending = 0;
        new MutationObserver(() => {
            if (pending) return;
            pending = requestAnimationFrame(() => {
                pending = 0;
                enhanceSegments(document);
                enhanceSteppers(document);
            });
        }).observe(document.body, { childList: true, subtree: true });
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
