// Guess My Age: the reveal as a race with a photo finish.
//
// While the real age travels, the player's guess hangs under the track as a
// small numbered tab with the crowd's guess as a wedge behind it, and a taut line runs from the player's
// mark to the travelling answer; a mark flinches when the answer passes it.
// When the answer lands, the ruler zooms in about it like a photo-finish
// camera: the answer holds still under its bubble, the year ticks spread out,
// and the two guesses slide out to their magnified places on two lanes, each
// with a line to the answer. The shorter line lights up and the other steps
// back, so the payoff that stamps in beside it ("You beat the crowd!") shows
// its working. On the hand-off
// back to the profile, the finish fades with the card.
//
// gma-race.css owns the look. guess-my-age.js keeps every behaviour; this
// classic script only follows its elements (#gmaRealBubble and its classes)
// and the crowd row it already updates. Nothing here adds words. Reduced
// motion shows the finish already zoomed, without the flinch or the glide.

(() => {
    const card = document.getElementById("guessAgeContainer");
    const wrap = card?.querySelector<HTMLElement>(".gma-slider-wrap") ?? null;
    const range = document.getElementById("gmaRange") as HTMLInputElement | null;
    const guessBubble = document.getElementById("gmaBubble");
    const content = document.querySelector<HTMLElement>("#detailsModal .modal-content");
    if (!card || !wrap || !range || !guessBubble || !content) return;

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const svgNs = "http://www.w3.org/2000/svg";
    const ZOOM_MS = 640;

    type Finish = {
        readonly panel: HTMLElement;
        readonly needle: HTMLElement;
        readonly you: HTMLElement;
        readonly youLine: HTMLElement;
        readonly them: HTMLElement | null;
        readonly themLine: HTMLElement | null;
        readonly truth: number;
        ppy: number;
        frame: number;
    };
    type Race = {
        readonly answer: HTMLElement;
        readonly guess: number;
        readonly crowd: number | null;
        readonly youMark: HTMLElement;
        readonly themMark: HTMLElement | null;
        readonly tension: HTMLElement;
        readonly watcher: MutationObserver;
        lastAge: number;
        finish: Finish | null;
        finishing: boolean;
    };
    let race: Race | null = null;

    // ------------------------------------------------ geometry (slider wrap coordinates)
    const bounds = (): { min: number; max: number } => ({ min: Number(range!.min) || 1, max: Number(range!.max) || 130 });
    const thumb = (): number => Number.parseFloat(getComputedStyle(range!).getPropertyValue("--gma-thumb-size")) || 64;
    // Mirrors guess-my-age.js: the thumb centre travels between half a thumb from each end.
    const laneLeft = (): number => range!.offsetLeft + thumb() / 2;
    const laneWidth = (): number => Math.max(1, range!.offsetWidth - thumb());
    const basePpy = (): number => { const { min, max } = bounds(); return laneWidth() / Math.max(1, max - min); };
    function xFor(age: number): number {
        const { min, max } = bounds();
        const clamped = Math.min(max, Math.max(min, age));
        return laneLeft() + (clamped - min) * basePpy();
    }
    const trackBottom = (): number => range!.offsetTop + range!.offsetHeight;

    // ------------------------------------------------ the crowd's figure
    // guess-my-age.js writes the server's crowd age into the profile row before the answer travels.
    function readCrowd(): number | null {
        const age = Number.parseFloat(document.getElementById("crowdAge")?.textContent || "");
        const count = Number.parseFloat(document.getElementById("crowdCount")?.textContent || "");
        // With one guess in the aggregate, the crowd is only this player.
        return Number.isFinite(age) && age > 0 && Number.isFinite(count) && count > 1 ? age : null;
    }

    function peopleGlyph(): SVGSVGElement {
        const svg = document.createElementNS(svgNs, "svg");
        svg.setAttribute("viewBox", "0 0 20 14");
        svg.setAttribute("aria-hidden", "true");
        svg.classList.add("gma-race-glyph");
        for (const cx of [6, 14]) {
            const head = document.createElementNS(svgNs, "circle");
            head.setAttribute("cx", String(cx));
            head.setAttribute("cy", "4");
            head.setAttribute("r", "2.6");
            svg.appendChild(head);
        }
        for (const x of [1.2, 9.2]) {
            const body = document.createElementNS(svgNs, "path");
            body.setAttribute("d", `M${x} 13.5c0-3 2.2-5 4.8-5s4.8 2 4.8 5z`);
            svg.appendChild(body);
        }
        return svg;
    }

    function el(cls: string, parent: HTMLElement): HTMLElement {
        const node = document.createElement("span");
        node.className = cls;
        node.setAttribute("aria-hidden", "true");
        parent.appendChild(node);
        return node;
    }

    function chip(kind: "you" | "crowd", age: number, parent: HTMLElement): HTMLElement {
        const node = el(`gma-race-chip gma-race-chip--${kind}`, parent);
        if (kind === "crowd") node.appendChild(peopleGlyph());
        const figure = document.createElement("span");
        figure.className = "gma-race-figure";
        figure.textContent = String(Math.round(age));
        node.appendChild(figure);
        return node;
    }

    function span(node: HTMLElement, from: number, to: number): void {
        node.style.left = `${Math.min(from, to)}px`;
        node.style.width = `${Math.abs(to - from)}px`;
        node.dataset.from = from <= to ? "left" : "right";
    }

    // ------------------------------------------------ during the travel
    function placeTravel(): void {
        if (!race) return;
        wrap!.style.setProperty("--gma-race-top", `${trackBottom()}px`);
        const youX = xFor(race.guess);
        race.youMark.style.left = `${youX}px`;
        if (race.themMark && race.crowd !== null) race.themMark.style.left = `${xFor(race.crowd)}px`;
        const answerAge = Number(race.answer.dataset.age);
        if (Number.isFinite(answerAge)) span(race.tension, youX, xFor(answerAge));
    }

    function flinch(node: HTMLElement | null): void {
        if (!node || reduced.matches) return;
        node.classList.remove("is-passed");
        void node.offsetWidth;
        node.classList.add("is-passed");
    }

    // ------------------------------------------------ the photo finish
    // The zoom keeps the answer where it landed and scales every other age about it.
    function targetPpy(truth: number, marks: number[]): number {
        const xT = xFor(truth);
        const left = xT - laneLeft();
        const right = laneLeft() + laneWidth() - xT;
        const chipRoom = 34;
        let ppy = laneWidth() / 10;
        for (const mark of marks) {
            const distance = Math.abs(mark - truth);
            if (distance < 0.5) continue;
            const room = (mark < truth ? left : right) - chipRoom;
            ppy = Math.min(ppy, Math.max(basePpy(), room / distance));
        }
        return Math.max(basePpy(), ppy);
    }

    function renderFinish(): void {
        const finish = race?.finish;
        if (!race || !finish) return;
        const panelLeft = range!.offsetLeft;
        const xT = xFor(finish.truth) - panelLeft;
        const at = (age: number): number => xT + (age - finish.truth) * finish.ppy;
        finish.panel.style.setProperty("--gma-race-ppy", `${finish.ppy}px`);
        // Ticks: one per year, a stronger one every five, both aligned to whole ages.
        const minorOffset = ((xT % finish.ppy) + finish.ppy) % finish.ppy;
        const five = Math.round(finish.truth / 5) * 5;
        const major = 5 * finish.ppy;
        const majorOffset = (((xT + (five - finish.truth) * finish.ppy) % major) + major) % major;
        finish.panel.style.setProperty("--gma-race-minor-x", `${minorOffset}px`);
        finish.panel.style.setProperty("--gma-race-major-x", `${majorOffset}px`);
        finish.needle.style.left = `${xT}px`;
        const clampChip = (x: number): number => Math.min(range!.offsetWidth - 26, Math.max(26, x));
        const youX = at(race.guess);
        finish.you.style.left = `${clampChip(youX)}px`;
        span(finish.youLine, youX, xT);
        if (finish.them && finish.themLine && race.crowd !== null) {
            const themX = at(race.crowd);
            finish.them.style.left = `${clampChip(themX)}px`;
            span(finish.themLine, themX, xT);
        }
    }

    function openFinish(truth: number): void {
        if (!race || race.finish) return;
        const panel = el("gma-race-finish", wrap!);
        panel.style.left = `${range!.offsetLeft}px`;
        panel.style.top = `${range!.offsetTop}px`;
        panel.style.width = `${range!.offsetWidth}px`;
        panel.style.height = `${Math.max(range!.offsetHeight, wrap!.clientHeight - range!.offsetTop)}px`;
        const youLine = el("gma-race-line gma-race-line--you", panel);
        const themLine = race.crowd !== null ? el("gma-race-line gma-race-line--crowd", panel) : null;
        const needle = el("gma-race-needle", panel);
        const you = chip("you", race.guess, panel);
        const them = race.crowd !== null ? chip("crowd", race.crowd, panel) : null;
        const finish: Finish = { panel, needle, you, youLine, them, themLine, truth, ppy: basePpy(), frame: 0 };
        race.finish = finish;
        card!.classList.add("gma-race--finish");

        // The verdict: strictly closer wins, the same rule as the payoff.
        const mine = Math.abs(truth - race.guess);
        const theirs = race.crowd !== null ? Math.abs(truth - race.crowd) : Infinity;
        const youWin = mine < theirs;
        you.classList.add(youWin || !them ? "is-closer" : "is-further");
        youLine.classList.add(youWin || !them ? "is-closer" : "is-further");
        them?.classList.add(youWin ? "is-further" : "is-closer");
        themLine?.classList.add(youWin ? "is-further" : "is-closer");
        card!.dataset.gmaRace = youWin ? "you" : "crowd";

        const marks = race.crowd !== null ? [race.guess, race.crowd] : [race.guess];
        const target = targetPpy(truth, marks);
        if (reduced.matches) {
            finish.ppy = target;
            renderFinish();
            return;
        }
        // The camera pushes in about the answer.
        const from = finish.ppy;
        const started = performance.now();
        const step = (now: number): void => {
            if (race?.finish !== finish) return;
            const t = Math.min(1, (now - started) / ZOOM_MS);
            const eased = 1 - Math.pow(1 - t, 3);
            finish.ppy = from + (target - from) * eased;
            renderFinish();
            finish.frame = t < 1 ? requestAnimationFrame(step) : 0;
        };
        renderFinish();
        finish.frame = requestAnimationFrame(step);
    }

    // ------------------------------------------------ the race
    function start(answer: HTMLElement): void {
        stop(false);
        const guess = Number.parseFloat(guessBubble!.dataset.age || guessBubble!.textContent || "");
        if (!Number.isFinite(guess)) return;
        const crowd = readCrowd();
        const tension = el("gma-race-tension", wrap!);
        const youMark = el("gma-race-mark gma-race-mark--you", wrap!);
        youMark.textContent = String(Math.round(guess));
        const themMark = crowd !== null ? el("gma-race-mark gma-race-mark--crowd", wrap!) : null;
        card!.classList.add("gma-race");
        const watcher = new MutationObserver(() => follow());
        race = { answer, guess, crowd, youMark, themMark, tension, watcher, lastAge: guess, finish: null, finishing: false };
        watcher.observe(answer, { attributes: true, attributeFilter: ["style", "class"] });
        placeTravel();
        // Reduced motion settles the answer in the same task that adds it.
        follow();
    }

    function follow(): void {
        if (!race) return;
        const age = Number(race.answer.dataset.age);
        if (Number.isFinite(age) && age !== race.lastAge) {
            // A mark flinches when the answer passes over it.
            const crossed = (mark: number): boolean => (race!.lastAge - mark) * (age - mark) <= 0 && race!.lastAge !== mark;
            if (crossed(race.guess)) flinch(race.youMark);
            if (race.crowd !== null && crossed(race.crowd)) flinch(race.themMark);
            race.lastAge = age;
        }
        placeTravel();
        if (!race.finishing && race.answer.classList.contains("is-settled") && Number.isFinite(age)) {
            // Let the answer's own landing bounce play before the camera pushes in.
            race.finishing = true;
            const current = race;
            window.setTimeout(() => { if (race === current) openFinish(age); }, reduced.matches ? 0 : 150);
        }
    }

    function stop(animated: boolean): void {
        const current = race;
        race = null;
        card!.classList.remove("gma-race", "gma-race--finish");
        delete card!.dataset.gmaRace;
        if (!current) return;
        current.watcher.disconnect();
        if (current.finish?.frame) cancelAnimationFrame(current.finish.frame);
        const parts = [current.youMark, current.themMark, current.tension, current.finish?.panel]
            .filter((node): node is HTMLElement => Boolean(node));
        if (!animated || reduced.matches) {
            parts.forEach(node => node.remove());
            return;
        }
        parts.forEach(node => node.classList.add("is-leaving"));
        window.setTimeout(() => parts.forEach(node => node.remove()), 320);
    }

    // The answer bubble's arrival starts a race; its removal (retry, new round) ends it.
    new MutationObserver(records => {
        for (const record of records) {
            record.addedNodes.forEach(node => {
                if (node instanceof HTMLElement && node.id === "gmaRealBubble") start(node);
            });
            record.removedNodes.forEach(node => {
                if (race && node === race.answer) stop(false);
            });
        }
    }).observe(wrap, { childList: true });

    // The card folding away hands back to the profile: the finish fades with it.
    new MutationObserver(() => {
        if (race && card!.classList.contains("gma-done")) stop(true);
    }).observe(card, { attributes: true, attributeFilter: ["class"] });
    new MutationObserver(() => {
        if (race && !content!.classList.contains("guess-mode")) stop(false);
    }).observe(content, { attributes: true, attributeFilter: ["class"] });

    if (typeof ResizeObserver === "function") {
        new ResizeObserver(() => {
            placeTravel();
            const finish = race?.finish;
            if (!finish) return;
            finish.panel.style.left = `${range!.offsetLeft}px`;
            finish.panel.style.top = `${range!.offsetTop}px`;
            finish.panel.style.width = `${range!.offsetWidth}px`;
            finish.panel.style.height = `${Math.max(range!.offsetHeight, wrap!.clientHeight - range!.offsetTop)}px`;
            finish.ppy = targetPpy(finish.truth, race!.crowd !== null ? [race!.guess, race!.crowd] : [race!.guess]);
            renderFinish();
        }).observe(wrap);
    }
})();
