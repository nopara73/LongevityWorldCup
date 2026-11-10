// /play title screen as an arena (play-arena.css owns the look).
//
// - Behind the "Just track it" title an arena stands: two floodlight beams
//   from the top corners, a haze where the crest sits and a running track
//   that recedes into the distance under it, its lane marks drifting toward
//   the viewer as if the race is already on.
// - The arena leans with the pointer (desktop): beams, haze and track sit at
//   different depths and drift against it.
// - Choosing a door moves the camera forward: the track rushes in, the beams
//   flare and the arena falls away while the next panel comes up.
//
// play-menu.ts keeps every behaviour (intro, panels, routes). The arena is
// aria-hidden decoration, shown only while body.play-start-active. Nothing
// here adds words; reduced motion keeps the still arena and drops travel.

(() => {
    const panel = document.getElementById("playStartPanel");
    if (!panel) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const fine = window.matchMedia("(hover: hover) and (pointer: fine)");

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
        if (event.pointerType !== "mouse" || reduced.matches || !fine.matches) return;
        if (!document.body.classList.contains("play-start-active")) return;
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            const px = (event.clientX / window.innerWidth - 0.5) * 2;
            const py = (event.clientY / window.innerHeight - 0.5) * 2;
            arena.style.setProperty("--arena-px", px.toFixed(3));
            arena.style.setProperty("--arena-py", py.toFixed(3));
        });
    }, { passive: true });

    // Choosing a door moves the camera into the arena as the next panel comes up.
    function dolly(): void {
        if (reduced.matches || !document.body.classList.contains("play-start-active")) return;
        arena.classList.remove("is-dollying");
        void arena.offsetWidth;
        arena.classList.add("is-dollying");
        // Ends with its own animation, so slowed captures and real time agree.
        const done = (event: AnimationEvent): void => {
            if (event.target !== arena || event.animationName !== "arena-dolly-out") return;
            arena.classList.remove("is-dollying");
            arena.removeEventListener("animationend", done);
        };
        arena.addEventListener("animationend", done);
    }
    ["newGameBtn", "continueGameBtn"].forEach(id => {
        document.getElementById(id)?.addEventListener("click", dolly, true);
    });

    // The arena lights up with the title intro, once the doors are ready.
    const lightUp = (): void => {
        if (document.body.classList.contains("play-start-active")) arena.classList.add("is-lit");
    };
    new MutationObserver(lightUp).observe(document.body, { attributes: true, attributeFilter: ["class"] });
    lightUp();
})();
