// The shared alert (#custom-alert): how it arrives, speaks and hands over.
//
// site-header.js keeps every behaviour (queue, focus, scroll lock, tone
// inference). alert-feel.css owns the look; this classic script only times:
// - Each tone answers in its own way: danger shakes its head, warning swings
//   once like a sign in the wind, info breathes. Success keeps the shared
//   celebration it already has.
// - The tone bar on the panel's top edge draws in from the centre.
// - When a queued alert follows, the panel stays put and hands over instead
//   of vanishing and reappearing: its height glides, the medallion turns over
//   to the new tone and the message crossfades.
// - Alerts still waiting in the queue show as a small deck behind the panel.
//
// Nothing here changes wording. Reduced motion keeps the deck and drops travel.

declare const customAlertQueue: readonly unknown[] | undefined;

(() => {
    const dialog = document.getElementById("custom-alert");
    const panel = dialog?.querySelector<HTMLElement>(".custom-alert-panel");
    if (!dialog || !panel) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    dialog.dataset.feel = "";

    const deck = document.createElement("div");
    deck.className = "custom-alert-deck";
    deck.setAttribute("aria-hidden", "true");
    deck.innerHTML = "<span></span><span></span>";
    panel.before(deck);

    const queued = (): number => {
        try { return typeof customAlertQueue !== "undefined" ? customAlertQueue.length : 0; } catch { return 0; }
    };

    // Last settled look of the panel, for the hand-over.
    let lastHeight = 0;
    let lastTone = "";
    let lastMark = "";
    let lastMessage = "";
    function remember(): void {
        if (dialog!.hidden) return;
        lastHeight = panel!.getBoundingClientRect().height;
        lastTone = dialog!.dataset.tone || "";
        lastMark = panel!.querySelector(".custom-alert-mark i")?.className || "";
        lastMessage = panel!.querySelector("#custom-alert-message")?.innerHTML || "";
    }
    new ResizeObserver(() => { remember(); syncDeck(); }).observe(panel);

    function syncDeck(): void {
        const n = Math.min(2, queued());
        dialog!.style.setProperty("--alert-queue", String(n));
        dialog!.classList.toggle("has-queue", n > 0);
        dialog!.classList.toggle("has-queue-2", n > 1);
        if (dialog!.hidden) return;
        // Layout offsets, not rects: the panel may still be mid-entrance.
        Object.assign(deck.style, { left: `${panel!.offsetLeft}px`, top: `${panel!.offsetTop}px`, width: `${panel!.offsetWidth}px` });
    }

    function speak(tone: string): void {
        if (reduced.matches) return;
        panel!.classList.remove("is-speaking");
        void panel!.offsetWidth;
        panel!.dataset.speak = tone;
        panel!.classList.add("is-speaking");
        window.setTimeout(() => panel!.classList.remove("is-speaking"), 900);
    }

    // The next queued alert is dealt off the deck: it rises from where the
    // waiting card sat, and its medallion turns over from the previous tone.
    function handOver(): void {
        if (reduced.matches) return;
        const mark = panel!.querySelector<HTMLElement>(".custom-alert-mark");
        panel!.classList.add("is-handing-over");
        const deal = panel!.animate(
            [{ opacity: .6, transform: "translateY(-12px) scale(.94)" }, { opacity: 1, transform: "none" }],
            { duration: 380, easing: "cubic-bezier(.2,0,0,1)" }
        );
        deal.onfinish = deal.oncancel = () => panel!.classList.remove("is-handing-over");
        if (mark && lastTone && lastTone !== dialog!.dataset.tone) {
            const icon = mark.querySelector("i");
            const nextIcon = icon?.className || "";
            const nextTone = dialog!.dataset.tone || "";
            if (icon && lastMark) icon.className = lastMark;
            panel!.dataset.flipTone = lastTone;
            const half = mark.animate([{ transform: "rotateY(0)" }, { transform: "rotateY(90deg)" }], { duration: 160, delay: 60, easing: "cubic-bezier(.3,0,1,1)", fill: "forwards" });
            half.onfinish = () => {
                delete panel!.dataset.flipTone;
                if (icon) icon.className = nextIcon;
                half.cancel();
                mark.animate([{ transform: "rotateY(-90deg)" }, { transform: "rotateY(0)" }], { duration: 300, easing: "cubic-bezier(.34,1.4,.64,1)" });
            };
            void nextTone;
        }
    }

    // close() hides the alert and opens the next queued one a microtask later,
    // so a reopen right after a close is a hand-over, not a fresh arrival.
    let closedAt = -1;
    let queuedAtClose = false;
    new MutationObserver(records => {
        syncDeck();
        const toggled = records.some(r => r.attributeName === "hidden");
        if (dialog!.hidden) {
            if (toggled) { closedAt = performance.now(); queuedAtClose = queued() > 0; }
            return;
        }
        const tone = dialog!.dataset.tone || "info";
        const wasClosedNow = records.some(r => r.attributeName === "hidden" && r.oldValue === null);
        if (wasClosedNow || (toggled && queuedAtClose && closedAt >= 0 && performance.now() - closedAt < 1200)) {
            handOver();
            window.setTimeout(() => speak(tone), 260);
        } else if (toggled) {
            speak(tone);
        }
        closedAt = -1;
        queuedAtClose = false;
        requestAnimationFrame(remember);
    }).observe(dialog, { attributes: true, attributeFilter: ["hidden", "data-tone"], attributeOldValue: true });

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
