// Guess My Age: the stopwatch bezel counts the guess.
//
// The bezel around the portrait already has 60 ticks. While the round is live,
// each tick is a year: as the slider moves, the ticks light up clockwise from
// twelve o'clock to the guess, a hand rides the ring, and past 60 a second lap
// lights over the first. The ruler under the slider lights up to the guess,
// the number in the bubble rolls up or down, and the hand clicks at each
// decade. Before the first touch the hand breathes, to invite a drag.
//
// gma-dial.css owns the look. guess-my-age.js keeps every behaviour; this
// classic script only reads the range's value. Nothing here adds words, and
// reduced motion keeps the lit ticks while dropping the roll and the glide.

(() => {
    const content = document.querySelector<HTMLElement>("#detailsModal .modal-content");
    const range = document.getElementById("gmaRange") as HTMLInputElement | null;
    const bubble = document.getElementById("gmaBubble");
    const portrait = document.querySelector<HTMLElement>("#athlete-profile .portrait-wrapper");
    const sliderWrap = range?.closest<HTMLElement>(".gma-slider-wrap") ?? null;
    if (!content || !range || !bubble || !portrait || !sliderWrap) return;

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const min = Number(range.min) || 1;
    const max = Number(range.max) || 130;

    const make = (cls: string, parent: HTMLElement): HTMLElement => {
        const el = document.createElement("span");
        el.className = cls;
        el.setAttribute("aria-hidden", "true");
        parent.appendChild(el);
        return el;
    };
    const dial = make("gma-dial", portrait);
    make("gma-dial-lap gma-dial-lap--one", dial);
    make("gma-dial-lap gma-dial-lap--two", dial);
    const hand = make("gma-dial-hand", dial);
    const ruler = make("gma-ruler-lit", sliderWrap);

    let last = Number(range.value);
    let rollTimer = 0;

    function paint(value: number): void {
        const years = Math.min(max, Math.max(min, value));
        dial.style.setProperty("--gma-years", String(years));
        hand.style.setProperty("--gma-years", String(years));
        ruler.style.setProperty("--gma-frac", ((years - min) / (max - min)).toFixed(4));
    }

    function roll(value: number): void {
        if (reduced.matches || value === last) return;
        const up = value > last;
        bubble!.classList.remove("gma-roll-up", "gma-roll-down");
        void bubble!.offsetWidth;
        bubble!.classList.add(up ? "gma-roll-up" : "gma-roll-down");
        window.clearTimeout(rollTimer);
        rollTimer = window.setTimeout(() => bubble!.classList.remove("gma-roll-up", "gma-roll-down"), 220);
        // A click at every decade the guess crosses.
        if (Math.floor(value / 10) !== Math.floor(last / 10)) {
            hand.classList.remove("is-clicking");
            void hand.offsetWidth;
            hand.classList.add("is-clicking");
        }
    }

    function onValue(): void {
        const value = Number(range!.value);
        dial.classList.add("is-touched");
        roll(value);
        paint(value);
        last = value;
    }
    range.addEventListener("input", onValue);
    range.addEventListener("change", onValue);

    // Holding the thumb tightens the hand to the finger; letting go springs it.
    const release = (): void => content.classList.remove("gma-dial-grab");
    range.addEventListener("pointerdown", () => content.classList.add("gma-dial-grab"));
    window.addEventListener("pointerup", release);
    window.addEventListener("pointercancel", release);

    // A new round (another athlete) resets the dial to the slider's start.
    let wasGuessing = content.classList.contains("guess-mode");
    new MutationObserver(() => {
        const guessing = content.classList.contains("guess-mode");
        if (guessing && !wasGuessing) {
            dial.classList.remove("is-touched");
            last = Number(range.value);
            paint(last);
        }
        wasGuessing = guessing;
    }).observe(content, { attributes: true, attributeFilter: ["class"] });

    paint(last);
})();
