// The athlete dialog's header as one stage.
//
// profile-stage.css owns the look; this classic script only times and measures:
// - Arrival: each time a profile fills in (opening, or stepping to another
//   athlete), the portrait develops inside a cream halo that draws itself,
//   the flag pops on, the PRO tag stamps down, the name rises and the action
//   rail deals out from its middle.
// - Scrolling: the big header condenses as it leaves (the portrait shrinks
//   toward the top and the halo turns), and the sticky bar catches it with
//   its own small portrait.
// - The action rail has one highlight that glides to the action in hand.
//
// leaderboard-page.js still fills every element and owns the sticky bar and
// the Guess My Age round; this script stays out of guess mode. Nothing here
// adds words. Reduced motion keeps the halo and the rail and drops the rest.

(() => {
    const modal = document.getElementById("detailsModal");
    const content = modal?.querySelector<HTMLElement>(".modal-content");
    const profile = document.getElementById("athlete-profile");
    const name = document.getElementById("athleteName");
    const portrait = profile?.querySelector<HTMLElement>(".portrait-wrapper");
    const rail = profile?.querySelector<HTMLElement>(".athlete-links");
    if (!modal || !content || !profile || !name || !portrait || !rail) return;

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    profile.dataset.profileStage = "";
    modal.dataset.profileStage = "";

    // ------------------------------------------------ halo and glide
    const halo = document.createElement("span");
    halo.className = "profile-stage-halo";
    halo.setAttribute("aria-hidden", "true");
    portrait.appendChild(halo);

    const glide = document.createElement("span");
    glide.className = "profile-stage-glide";
    glide.setAttribute("aria-hidden", "true");
    rail.prepend(glide);

    const guessing = (): boolean => content.classList.contains("guess-mode");

    // ------------------------------------------------ arrival
    let arrivalTimer = 0;
    function arrive(): void {
        if (guessing() || reduced.matches) return;
        window.clearTimeout(arrivalTimer);
        profile!.classList.remove("is-arriving");
        void profile!.offsetWidth;
        profile!.classList.add("is-arriving");
        arrivalTimer = window.setTimeout(() => { profile!.classList.remove("is-arriving"); measureRail(); }, 1500);
    }

    // Measure once the header has laid out, with or without the arrival.
    function settle(): void {
        requestAnimationFrame(() => { measureRail(); arrive(); });
        // The rest of the profile keeps filling in under the header for a moment.
        [450, 1100, 2400].forEach(ms => window.setTimeout(measureRail, ms));
    }

    // The name is the last header field leaderboard-page.js writes for a profile.
    let lastName = "";
    new MutationObserver(() => {
        const now = (name.textContent || "").trim();
        if (!now || now === lastName) return;
        lastName = now;
        settle();
    }).observe(name, { childList: true, characterData: true, subtree: true });

    // ------------------------------------------------ scroll condense
    let frame = 0;
    function condense(): void {
        frame = 0;
        if (guessing() || reduced.matches) { profile!.style.removeProperty("--stage-p"); return; }
        const reach = Math.max(120, name!.offsetTop - 24);
        const p = Math.min(1, Math.max(0, content!.scrollTop / reach));
        profile!.style.setProperty("--stage-p", p.toFixed(3));
    }
    content.addEventListener("scroll", () => { if (!frame) frame = requestAnimationFrame(condense); }, { passive: true });

    // ------------------------------------------------ rail and glide
    // Rings are measured with layout offsets (relative to the rail), so arrival
    // transforms never skew the numbers.
    const items = (): HTMLElement[] => Array.from(rail.querySelectorAll<HTMLElement>(":scope > a[href], :scope > .athlete-share-wrapper > button"))
        .filter(el => el.getClientRects().length > 0);
    const ringOf = (el: HTMLElement): HTMLElement => el.querySelector<HTMLElement>(":scope > i") ?? el;
    // A dealing host becomes its ring's offsetParent, so walk the chain up to the rail.
    const centre = (el: HTMLElement): number => {
        const ring = ringOf(el);
        let x = ring.offsetWidth / 2;
        for (let node: HTMLElement | null = ring; node && node !== rail; node = node.offsetParent as HTMLElement | null) x += node.offsetLeft;
        return x;
    };

    function glideTo(el: HTMLElement | null): void {
        if (!el) { rail!.classList.remove("is-gliding"); return; }
        if (!rail!.classList.contains("is-gliding")) glide.classList.add("is-snapping");
        glide.style.setProperty("--glide-x", `${centre(el)}px`);
        rail!.classList.add("is-gliding");
        void glide.offsetWidth;
        glide.classList.remove("is-snapping");
    }
    const itemOf = (target: EventTarget | null): HTMLElement | null => {
        const el = (target as Element | null)?.closest?.<HTMLElement>(".athlete-links > a, .athlete-share-wrapper > button") ?? null;
        return el && items().includes(el) ? el : null;
    };
    rail.addEventListener("pointerover", event => { const el = itemOf(event.target); if (el) glideTo(el); });
    rail.addEventListener("pointerleave", event => { if (event.pointerType === "mouse") glideTo(null); });
    rail.addEventListener("focusin", event => glideTo(itemOf(event.target)));
    rail.addEventListener("focusout", () => requestAnimationFrame(() => {
        if (!rail!.contains(document.activeElement) && !rail!.matches(":hover")) glideTo(null);
    }));

    // The rail runs from the first ring's centre to the last one's.
    let measured = "";
    function measureRail(): void {
        const list = items();
        let next = "none";
        if (list.length > 1) {
            const from = centre(list[0]!);
            const to = centre(list[list.length - 1]!);
            const sameRow = Math.abs(ringOf(list[0]!).offsetTop - ringOf(list[list.length - 1]!).offsetTop) < 4;
            if (sameRow && to > from) next = `${from}|${to - from}`;
        }
        // Write only on a change: the observers below also see the rail.
        if (next === measured) return;
        measured = next;
        rail!.classList.toggle("has-rail", next !== "none");
        if (next === "none") return;
        const [from, len] = next.split("|");
        rail!.style.setProperty("--rail-from", `${from}px`);
        rail!.style.setProperty("--rail-len", `${len}px`);
    }
    new MutationObserver(() => requestAnimationFrame(measureRail)).observe(rail, { attributes: true, subtree: true, attributeFilter: ["hidden", "href"] });
    new MutationObserver(() => requestAnimationFrame(measureRail)).observe(name, { childList: true, subtree: true });
    const resized = new ResizeObserver(() => measureRail());
    resized.observe(rail);
    resized.observe(profile);
    rail.querySelectorAll<HTMLElement>(":scope > a, :scope > .athlete-share-wrapper").forEach(host => resized.observe(host));
    rail.addEventListener("pointerenter", measureRail);
    document.fonts?.ready.then(measureRail).catch(() => undefined);
    window.addEventListener("load", measureRail, { once: true });
    measureRail();
})();
