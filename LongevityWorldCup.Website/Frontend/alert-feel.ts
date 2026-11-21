// The shared alert (#custom-alert): how it arrives, speaks and hands over.
//
// site-header.js keeps every behaviour (queue, focus, scroll lock, tone
// inference). alert-feel.css owns the look; this classic script only times:
// - Each tone answers in its own way: danger shakes its head, warning swings
//   once like a sign in the wind, info breathes. Success keeps the shared
//   celebration it already has.
// - The tone bar on the panel's top edge draws in from the centre.
// - When a queued alert follows, the panel stays put and hands over instead
//   of vanishing and reappearing: it is dealt off the deck and the medallion
//   turns over from the previous tone to the new one.
// - Alerts still waiting in the queue show as a small deck behind the panel.
//
// Nothing here changes wording or the accessible text. Reduced motion keeps
// the deck and shows every alert at rest.

declare const customAlertQueue: readonly unknown[] | undefined;

(() => {
    const dialog = document.getElementById("custom-alert");
    const panel = dialog?.querySelector<HTMLElement>(".custom-alert-panel");
    if (!dialog || !panel) return;
    const reduced = typeof window.matchMedia === "function" ? window.matchMedia("(prefers-reduced-motion: reduce)") : null;
    const moving = (): boolean => !reduced?.matches && typeof panel.animate === "function";
    dialog.dataset.feel = "";

    const deck = document.createElement("div");
    deck.className = "custom-alert-deck";
    deck.setAttribute("aria-hidden", "true");
    deck.innerHTML = "<span></span><span></span>";
    panel.before(deck);

    const queued = (): number => {
        try { return typeof customAlertQueue !== "undefined" ? customAlertQueue.length : 0; } catch { return 0; }
    };

    function syncDeck(): void {
        const n = Math.min(2, queued());
        dialog!.classList.toggle("has-queue", n > 0);
        dialog!.classList.toggle("has-queue-2", n > 1);
        if (dialog!.hidden) return;
        // Layout offsets, not rects: the panel may still be mid-entrance.
        Object.assign(deck.style, { left: `${panel!.offsetLeft}px`, top: `${panel!.offsetTop}px`, width: `${panel!.offsetWidth}px` });
    }
    if (typeof ResizeObserver === "function") new ResizeObserver(syncDeck).observe(panel);
    window.addEventListener("resize", syncDeck, { passive: true });

    // ---- The tone's voice: a short gesture, then the panel is at rest ----
    let speakTimer = 0;
    let speakDelay = 0;
    function quiet(): void {
        window.clearTimeout(speakTimer);
        window.clearTimeout(speakDelay);
        panel!.classList.remove("is-speaking");
        delete panel!.dataset.speakVia;
    }
    // via "deal": the message is already on screen, so only the medallion speaks.
    function speak(tone: string, via: "arrival" | "deal"): void {
        quiet();
        if (!moving()) return;
        void panel!.offsetWidth;
        panel!.dataset.speak = tone;
        panel!.dataset.speakVia = via;
        panel!.classList.add("is-speaking");
        // The longest voice (info's breath) ends 1.2s after it starts.
        speakTimer = window.setTimeout(quiet, 1300);
    }

    // ---- The hand-over: dealt off the deck, the medallion turns over ----
    const markIcon = (): HTMLElement | null => panel.querySelector<HTMLElement>(".custom-alert-mark i");
    // The alert really on screen; the flip only borrows the icon briefly.
    let shown = { tone: "", mark: "" };
    let running: Animation[] = [];
    function stopHandOver(restoreIcon: boolean): void {
        for (const animation of running) animation.cancel();
        running = [];
        delete panel!.dataset.flipTone;
        const icon = markIcon();
        if (restoreIcon && icon && shown.mark) icon.className = shown.mark;
    }
    function handOver(previous: { tone: string; mark: string }): void {
        if (!moving()) return;
        running.push(panel!.animate(
            [{ opacity: .6, transform: "translateY(-12px) scale(.94)" }, { opacity: 1, transform: "none" }],
            { duration: 380, easing: "cubic-bezier(.2,0,0,1)" }));
        const mark = panel!.querySelector<HTMLElement>(".custom-alert-mark");
        const icon = markIcon();
        const next = shown;
        if (!mark || !icon || !previous.tone || previous.tone === next.tone) return;
        icon.className = previous.mark;
        panel!.dataset.flipTone = previous.tone;
        const half = mark.animate([{ transform: "rotateY(0)" }, { transform: "rotateY(90deg)" }],
            { duration: 160, delay: 60, easing: "cubic-bezier(.3,0,1,1)", fill: "forwards" });
        half.onfinish = () => {
            delete panel!.dataset.flipTone;
            icon.className = next.mark;
            half.cancel();
            running.push(mark.animate([{ transform: "rotateY(-90deg)" }, { transform: "rotateY(0)" }],
                { duration: 300, easing: "cubic-bezier(.34,1.4,.64,1)" }));
        };
        running.push(half);
    }

    // close() hides the alert and opens the next queued one a microtask later,
    // so the two changes reach this observer separately. A reopen before any
    // frame was painted is a hand-over; the reader never saw the panel go.
    let paintedSinceHide = true;
    new MutationObserver(records => {
        syncDeck();
        if (!records.some(record => record.attributeName === "hidden")) return;
        if (dialog.hidden) {
            quiet();
            stopHandOver(true);
            paintedSinceHide = false;
            requestAnimationFrame(() => { paintedSinceHide = true; });
            return;
        }
        stopHandOver(false);
        const previous = shown;
        shown = { tone: dialog.dataset.tone || "info", mark: markIcon()?.className || "" };
        if (!paintedSinceHide && previous.tone) {
            quiet();
            handOver(previous);
            const tone = shown.tone;
            speakDelay = window.setTimeout(() => speak(tone, "deal"), 260);
        } else {
            speak(shown.tone, "arrival");
        }
    }).observe(dialog, { attributes: true, attributeFilter: ["hidden", "data-tone"] });

    // Alerts can be queued while one is open.
    const original = (window as unknown as { customAlert?: (m: unknown, o?: unknown) => Promise<void> }).customAlert;
    if (typeof original === "function") {
        (window as unknown as { customAlert: typeof original }).customAlert = function (message: unknown, options?: unknown): Promise<void> {
            const result = original(message, options);
            requestAnimationFrame(syncDeck);
            return result;
        };
    }
})();
