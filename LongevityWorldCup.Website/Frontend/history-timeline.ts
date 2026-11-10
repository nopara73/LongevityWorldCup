// History timeline as a living line (generated docs pages with a timeline).
//
// history-timeline.css owns the look; this classic script only measures:
// - The rail fills in accent up to the reading line as the page scrolls, and
//   stops at "today", the point between the last past entry and the first
//   announced one. Past that point the rail is dashed: the future.
// - Entries the reader has passed light their marker; the one in hand is
//   current and its date pill turns solid.
// - A large year watermark sticks beside the timeline and rolls to the year
//   of the current entry (the year comes from the entry's own date).
//
// Nothing here changes wording or the document. Reduced motion keeps the
// states and drops the travel.

interface LwcHistoryTimelineApi {
    readonly refresh: () => void;
}

interface Window {
    LwcHistoryTimeline: LwcHistoryTimelineApi;
}

(() => {
    if (window.LwcHistoryTimeline) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");

    function start(): void {
        const timeline = document.querySelector<HTMLElement>(".documentation-timeline");
        if (!timeline) return;
        const entries = Array.from(timeline.querySelectorAll<HTMLElement>(":scope > .documentation-timeline-entry"));
        if (!entries.length) return;
        timeline.dataset.live = "";

        // Decorations (all aria-hidden).
        const fill = document.createElement("span");
        fill.className = "documentation-timeline-fill";
        const future = document.createElement("span");
        future.className = "documentation-timeline-future";
        const now = document.createElement("span");
        now.className = "documentation-timeline-now";
        const yearHost = document.createElement("span");
        yearHost.className = "documentation-timeline-year";
        const yearFace = document.createElement("span");
        yearFace.className = "documentation-timeline-year-face";
        yearHost.appendChild(yearFace);
        [fill, future, now, yearHost].forEach(el => el.setAttribute("aria-hidden", "true"));
        timeline.prepend(yearHost);
        timeline.append(fill, future, now);

        const yearOf = (entry: HTMLElement): string =>
            (entry.querySelector(".documentation-timeline-date")?.textContent || "").match(/\d{4}/)?.[0] || "";
        const firstAnnounced = entries.findIndex(entry => entry.classList.contains("is-announced"));

        let shownYear = "";
        let yearAnimation: Animation | null = null;
        function settleYear(): void {
            yearAnimation?.cancel();
            yearAnimation = null;
            yearFace.textContent = shownYear;
        }
        function rollYear(year: string): void {
            if (year === shownYear) return;
            const goingUp = shownYear !== "" && year < shownYear;
            shownYear = year;
            yearAnimation?.cancel();
            if (reduced.matches || !yearFace.textContent) { settleYear(); return; }
            const out = yearFace.animate(
                [{ transform: "none", opacity: 1 }, { transform: `translateY(${goingUp ? 40 : -40}%)`, opacity: 0 }],
                { duration: 160, easing: "cubic-bezier(.3,0,1,1)" }
            );
            yearAnimation = out;
            out.onfinish = () => {
                if (yearAnimation !== out) return;
                yearFace.textContent = year;
                yearAnimation = yearFace.animate(
                    [{ transform: `translateY(${goingUp ? -40 : 40}%)`, opacity: 0 }, { transform: "none", opacity: 1 }],
                    { duration: 320, easing: "cubic-bezier(.2,0,0,1)" }
                );
            };
        }

        let queued = false;
        function measure(): void {
            queued = false;
            const tr = timeline!.getBoundingClientRect();
            const nodeY = (entry: HTMLElement): number => {
                const pill = entry.querySelector<HTMLElement>(".documentation-timeline-date");
                const r = (pill || entry).getBoundingClientRect();
                return r.top + r.height / 2 - tr.top;
            };
            const ys = entries.map(nodeY);
            const railTop = ys[0] ?? 0;
            const railEnd = ys[ys.length - 1] ?? tr.height;
            // "Today" sits between the last past entry and the first announced one.
            const lastPast = firstAnnounced > 0 ? ys[firstAnnounced - 1]! : railEnd;
            const nowY = firstAnnounced > 0 ? (lastPast + ys[firstAnnounced]!) / 2 : railEnd;
            const reading = window.innerHeight * 0.42 - tr.top;
            const progress = Math.max(railTop, Math.min(nowY, reading));
            timeline!.style.setProperty("--timeline-rail-top", `${railTop}px`);
            timeline!.style.setProperty("--timeline-progress", `${progress - railTop}px`);
            timeline!.style.setProperty("--timeline-now", `${nowY}px`);
            timeline!.style.setProperty("--timeline-end", `${railEnd}px`);
            timeline!.classList.toggle("has-future", firstAnnounced > 0);
            timeline!.classList.toggle("is-now-reached", reading >= nowY);

            let current = -1;
            ys.forEach((y, index) => { if (y <= reading) current = index; });
            entries.forEach((entry, index) => {
                entry.classList.toggle("is-passed", index <= current);
                entry.classList.toggle("is-current", index === current);
            });
            rollYear(yearOf(entries[Math.max(0, current)]!));
        }
        const schedule = (): void => { if (!queued) { queued = true; requestAnimationFrame(measure); } };
        window.addEventListener("scroll", schedule, { passive: true });
        window.addEventListener("resize", schedule, { passive: true });
        window.addEventListener("pageshow", schedule);
        window.addEventListener("pagehide", settleYear);
        reduced.addEventListener("change", () => { if (reduced.matches) settleYear(); });
        new ResizeObserver(schedule).observe(timeline);
        timeline.addEventListener("load", schedule, true);
        measure();

        window.LwcHistoryTimeline = { refresh: schedule };
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();
})();
