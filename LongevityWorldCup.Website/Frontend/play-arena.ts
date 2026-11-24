// /play title screen as an arena (play-arena.css owns the look).
//
// - Behind the "Just track it" title an arena stands: two floodlight beams
//   from the top corners, a haze where the crest sits and a running track
//   that recedes into the distance under it, its lane marks drifting toward
//   the viewer as if the race is already on.
// - The arena leans with the pointer (desktop): beams, haze and track sit at
//   different depths and drift against it.
// - Choosing a door moves the camera forward: the track rushes in, the beams
//   flare and the arena falls away behind the next panel, which is visible
//   and usable from the first frame.
//
// play-menu.ts keeps every behaviour (intro, panels, routes). The arena is
// aria-hidden decoration, shown only while body.play-start-active. Nothing
// here adds words; reduced motion keeps the still arena and drops travel.

(() => {
    const panel = document.getElementById("playStartPanel");
    // The arena follows the title through the body's classes. Without a way
    // to watch them it stays out, and the title shows as it always has.
    if (!panel || typeof MutationObserver !== "function") return;
    const media = (query: string): MediaQueryList | null =>
        typeof window.matchMedia === "function" ? window.matchMedia(query) : null;
    const reduced = media("(prefers-reduced-motion: reduce)");
    const fine = media("(hover: hover) and (pointer: fine)");
    const body = document.body.classList;
    const onTitle = (): boolean => body.contains("play-start-active") && !body.contains("play-start-preintro");

    const arena = document.createElement("div");
    arena.className = "play-arena";
    arena.setAttribute("aria-hidden", "true");
    arena.innerHTML = `
        <span class="play-arena-beam play-arena-beam--left"></span>
        <span class="play-arena-beam play-arena-beam--right"></span>
        <span class="play-arena-haze"></span>
        <span class="play-arena-floor"><span class="play-arena-track"></span></span>
        <span class="play-arena-vignette"></span>`;
    document.body.prepend(arena);
    document.documentElement.dataset.playArena = "";

    // Depth: the arena drifts against the pointer.
    let frame = 0;
    window.addEventListener("pointermove", event => {
        if (event.pointerType !== "mouse" || reduced?.matches || !fine?.matches) return;
        if (!body.contains("play-start-active")) return;
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            const px = (event.clientX / window.innerWidth - 0.5) * 2;
            const py = (event.clientY / window.innerHeight - 0.5) * 2;
            arena.style.setProperty("--arena-px", px.toFixed(3));
            arena.style.setProperty("--arena-py", py.toFixed(3));
        });
    }, { passive: true });

    // Choosing a door moves the camera into the arena as the next panel comes
    // up. A second click joins the move already running.
    const dollyMs = 820; // arena-dolly-out in play-arena.css
    let dollyTimer = 0;
    let dollyDeadline = 0;
    function dolly(): void {
        if (reduced?.matches || !body.contains("play-start-active")) return;
        if (arena.classList.contains("is-dollying")) return;
        arena.classList.add("is-dollying");
        dollyDeadline = performance.now() + dollyMs * 4;
        dollyTimer = window.setTimeout(settleDolly, dollyMs);
    }
    ["newGameBtn", "continueGameBtn"].forEach(id => {
        document.getElementById(id)?.addEventListener("click", dolly, true);
    });
    function stopDolly(): void {
        window.clearTimeout(dollyTimer);
        dollyTimer = 0;
        arena.classList.remove("is-dollying");
    }
    // The move ends with its own animation, so slowed captures and real time
    // agree. Its end is also checked when it is due: with every other arena
    // motion on the compositor, an idle page may not deliver animationend
    // until something else redraws. A move still running (or paused, or not
    // yet started) is checked again, for at most four times its length.
    function settleDolly(): void {
        const moving = typeof arena.getAnimations === "function"
            && arena.getAnimations().some(animation => animation.playState === "running" || animation.playState === "paused");
        if (moving && performance.now() < dollyDeadline) {
            dollyTimer = window.setTimeout(settleDolly, 100);
            return;
        }
        stopDolly();
        sync();
    }
    // A cancelled move (reduced motion switched on mid-way) ends too.
    const endDolly = (event: AnimationEvent): void => {
        if (event.target !== arena || event.animationName !== "arena-dolly-out") return;
        if (!arena.classList.contains("is-dollying")) return;
        stopDolly();
        sync();
    };
    arena.addEventListener("animationend", endDolly);
    arena.addEventListener("animationcancel", endDolly);

    // The arena lights up with the title intro (after its pre-intro hold), on a
    // later frame so its fades run. Away from the title it goes dark, which
    // also stops its looping beams and track.
    let lightFrame = 0;
    let wasActive = body.contains("play-start-active");
    function sync(): void {
        const active = body.contains("play-start-active");
        // Back on the title before the move ends: the camera is back at once,
        // and no move keeps running behind the title.
        if (active && !wasActive) stopDolly();
        wasActive = active;
        if (!onTitle()) {
            cancelAnimationFrame(lightFrame);
            lightFrame = 0;
            if (!arena.classList.contains("is-dollying")) arena.classList.remove("is-lit");
            return;
        }
        if (arena.classList.contains("is-lit") || lightFrame) return;
        lightFrame = requestAnimationFrame(() => {
            lightFrame = requestAnimationFrame(() => {
                lightFrame = 0;
                if (onTitle()) arena.classList.add("is-lit");
            });
        });
    }
    new MutationObserver(sync).observe(document.body, { attributes: true, attributeFilter: ["class"] });
    sync();
})();
