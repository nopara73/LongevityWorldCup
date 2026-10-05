(function () {
    const controllers = new WeakMap<HTMLElement, LwcFieldValidationController>();
    const pending = new Set<LwcFieldValidationController>();
    let pointerDown = false;

    document.addEventListener('pointerdown', event => {
        if (event.isPrimary) pointerDown = true;
    }, true);

    const finishPointer = (event: Event) => {
        if (event instanceof PointerEvent && !event.isPrimary) return;
        pointerDown = false;
        // Inserting an error between pointerdown and click can move the next
        // field or action away from the pointer. Let that gesture finish first.
        window.setTimeout(() => {
            if (!pointerDown) Array.from(pending).forEach(controller => controller.flush());
        }, 0);
    };
    document.addEventListener('pointerup', finishPointer, true);
    document.addEventListener('pointercancel', finishPointer, true);
    window.addEventListener('blur', finishPointer);

    function bind(
        field: HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement,
        options: LwcFieldValidationOptions
    ): LwcFieldValidationController {
        const existing = controllers.get(field);
        if (existing) return existing;

        let edited = false;
        let finishedEditing = false;
        const canReveal = () => field.isConnected && !field.disabled && field.getClientRects().length > 0
            && document.activeElement !== field && !options.isRestoring?.();
        const controller: LwcFieldValidationController = {
            validate() {
                pending.delete(controller);
                const valid = options.validate();
                options.onFeedback?.();
                return valid;
            },
            reset() {
                edited = false;
                finishedEditing = false;
                pending.delete(controller);
                options.clear();
            },
            refresh() {
                // Late constraints may update feedback for a completed edit,
                // but never interrupt typing or reveal an untouched field.
                if (!edited || !finishedEditing || !canReveal()) return;
                if (pointerDown) pending.add(controller);
                else controller.validate();
            },
            flush() {
                if (!pending.delete(controller)) return;
                if (canReveal()) controller.validate();
            }
        };

        const edit = () => {
            if (options.isRestoring?.()) return;
            edited = true;
            finishedEditing = false;
            pending.delete(controller);
            options.clear();
        };
        field.addEventListener('input', edit);
        field.addEventListener('change', edit);
        field.addEventListener('focus', () => {
            finishedEditing = false;
            pending.delete(controller);
        });
        field.addEventListener('blur', () => {
            // Focus, step autofocus, restored drafts, and background responses
            // do not establish that the user has finished editing this field.
            if (!edited || options.isRestoring?.() || field.disabled || !field.getClientRects().length) return;
            finishedEditing = true;
            if (pointerDown) pending.add(controller);
            else controller.validate();
        });
        field.form?.addEventListener('reset', () => controller.reset());
        controllers.set(field, controller);
        return controller;
    }

    window.LwcFieldValidation = { bind };
})();
