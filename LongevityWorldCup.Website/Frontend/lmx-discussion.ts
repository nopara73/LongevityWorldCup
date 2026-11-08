// Longevitymaxxing discussion as conversation threads.
//
// lmx-discussion.css owns how a thread looks; this classic script only
// measures and choreographs:
// - Thread lines: each post's line starts under the author's avatar and ends in
//   a curve into its last reply's avatar; every reply hooks in with its own
//   curve. Geometry is measured, so wrapped names, videos and photos never
//   leave a line floating.
// - Arrivals: earlier replies, new replies, new posts and open composers
//   settle in instead of popping. Only things the visitor has not seen yet
//   move, so re-renders and live refreshes stay still.
//
// longevitymaxxing.ts keeps every behaviour; nothing here changes wording.

interface LwcLmxDiscussionApi {
    readonly measure: () => void;
}

interface Window {
    LwcLmxDiscussion: LwcLmxDiscussionApi;
}

(() => {
    if (window.LwcLmxDiscussion) return;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const EASE = "cubic-bezier(0.2, 0, 0, 1)";

    const visible = (el: Element | null): el is HTMLElement => !!el && el instanceof HTMLElement && el.getClientRects().length > 0;

    // ------------------------------------------------------------ geometry
    function measureNote(note: HTMLElement): void {
        const avatar = note.querySelector<HTMLElement>(":scope > .lmx-discussion-post-header .lmx-discussion-author-avatar");
        const replies = note.querySelector<HTMLElement>(":scope > .lmx-discussion-replies");
        if (!visible(avatar) || !visible(replies)) return;
        // Advice has its own nested branches and archived conversations.
        // Only the flat reply list has the geometry described by this rail.
        if (replies.classList.contains("lmx-advice-replies")) return;
        const ar = avatar.getBoundingClientRect();
        const rr = replies.getBoundingClientRect();
        // On phones the post text runs under the author row, so the line starts
        // at the replies instead of cutting through the text and media.
        const body = note.querySelector<HTMLElement>(":scope > p, :scope > .lmx-note-photo-grid, :scope > .lmx-announcement-content");
        const stacked = visible(body) && body.getBoundingClientRect().left < ar.right - 2;
        const railCenter = stacked ? rr.left + 1 : ar.left + ar.width / 2;
        const items = Array.from(replies.querySelectorAll<HTMLElement>(":scope > .lmx-discussion-reply-list > .lmx-discussion-reply-item")).filter(visible);
        // The line starts just under the post avatar and ends at the last reply's curve
        // (or on the "earlier replies" node when nothing is expanded yet).
        replies.style.setProperty("--lmx-rail-reach", stacked ? "0px" : `${Math.max(0, rr.top - ar.bottom - 6)}px`);
        replies.style.setProperty("--lmx-rail-x", `${railCenter - rr.left - 1}px`);
        replies.querySelectorAll<HTMLElement>(":scope > .lmx-discussion-replies-toggle").forEach(toggle => {
            if (visible(toggle)) toggle.style.setProperty("--lmx-node-x", `${railCenter - toggle.getBoundingClientRect().left}px`);
        });
        let endY = rr.top;
        items.forEach(item => {
            const face = item.querySelector<HTMLElement>(":scope > .lmx-discussion-reply-meta .lmx-discussion-author-avatar");
            if (!visible(face)) return;
            const ir = item.getBoundingClientRect();
            const fr = face.getBoundingClientRect();
            const cy = fr.top + fr.height / 2;
            item.style.setProperty("--lmx-elbow-y", `${cy - ir.top}px`);
            item.style.setProperty("--lmx-elbow-x", `${railCenter - 1 - ir.left}px`);
            item.style.setProperty("--lmx-elbow-w", `${Math.max(6, fr.left - railCenter - 3)}px`);
            const elbowHeight = window.matchMedia("(max-width: 600px)").matches ? 10 : 14;
            endY = Math.max(endY, cy - elbowHeight);
        });
        if (!items.length) {
            const node = Array.from(replies.querySelectorAll<HTMLElement>(":scope > .lmx-discussion-replies-toggle")).find(visible);
            if (node) { const nr = node.getBoundingClientRect(); endY = nr.top + nr.height / 2; }
        }
        replies.style.setProperty("--lmx-rail-tail", `${Math.max(0, rr.bottom - endY)}px`);
        replies.dataset.lmxThread = "";
    }

    let queued = false;
    function measure(): void {
        if (queued) return;
        queued = true;
        requestAnimationFrame(() => {
            queued = false;
            const notes = document.getElementById("lmxNotes");
            if (!notes) return;
            notes.querySelectorAll<HTMLElement>(":scope > .lmx-note").forEach(measureNote);
        });
    }

    // ------------------------------------------------------------ arrivals
    const seen = new Set<string>();
    const composers = new WeakSet<HTMLElement>();
    const arrivals = new Set<Animation>();
    let settled = false;
    const keyOf = (el: HTMLElement): string | null =>
        el.querySelector<HTMLAnchorElement>(":scope > .lmx-discussion-post-header .lmx-discussion-permalink, :scope > .lmx-discussion-reply-meta .lmx-discussion-permalink")?.getAttribute("href")
        || el.dataset.discussionReplyId || el.id || null;

    function remember(scope: ParentNode): void {
        scope.querySelectorAll<HTMLElement>(".lmx-note, .lmx-discussion-reply-item").forEach(el => {
            const key = keyOf(el);
            if (key) seen.add(key);
        });
        scope.querySelectorAll<HTMLElement>("[data-discussion-draft]").forEach(el => composers.add(el));
    }

    function arrive(el: HTMLElement, index: number, kind: "reply" | "post" | "composer"): void {
        el.classList.add("lmx-arriving");
        const frames = kind === "reply"
            ? [{ opacity: 0, transform: "translateX(-10px)" }, { opacity: 1, transform: "none" }]
            : kind === "post"
                ? [{ opacity: 0, transform: "translateY(-14px) scale(.985)" }, { opacity: 1, transform: "none" }]
                : [{ opacity: 0, transform: "translateY(-6px)", clipPath: "inset(0 0 100% 0)" }, { opacity: 1, transform: "none", clipPath: "inset(0 0 0 0)" }];
        const anim = el.animate(frames, { duration: kind === "post" ? 340 : 260, delay: Math.min(index, 8) * 45, easing: EASE, fill: "backwards" });
        arrivals.add(anim);
        // Lines are measured against settled boxes, so re-measure once it lands.
        anim.onfinish = anim.oncancel = () => { arrivals.delete(anim); el.classList.remove("lmx-arriving"); measure(); };
    }

    function onMutations(records: MutationRecord[]): void {
        measure();
        const notes = document.getElementById("lmxNotes");
        if (!settled || reduced.matches) {
            // Network timing cannot turn the first populated render into arrivals.
            if (notes) { remember(notes); settled = !!notes.querySelector("article.lmx-note"); }
            return;
        }
        let replyIndex = 0;
        let postIndex = 0;
        for (const record of records) {
            record.addedNodes.forEach(node => {
                if (!(node instanceof HTMLElement) || !node.isConnected) return;
                const candidates = node.matches(".lmx-note, .lmx-discussion-reply-item")
                    ? [node]
                    : Array.from(node.querySelectorAll<HTMLElement>(".lmx-discussion-reply-item"));
                for (const el of candidates) {
                    const key = keyOf(el);
                    if (!key || seen.has(key)) continue;
                    seen.add(key);
                    if (!visible(el)) continue;
                    if (el.matches(".lmx-note")) arrive(el, postIndex++, "post");
                    else arrive(el, replyIndex++, "reply");
                }
                const slot = node.parentElement;
                if (slot?.matches(".lmx-discussion-reply-slot, .lmx-discussion-reply-editor-slot") && node.matches("[data-discussion-draft]") && !composers.has(node)) {
                    composers.add(node);
                    arrive(node, 0, "composer");
                }
                remember(node);
            });
        }
    }

    function start(): void {
        const notes = document.getElementById("lmxNotes");
        if (!notes) return;
        remember(notes);
        settled = !!notes.querySelector("article.lmx-note");
        new MutationObserver(onMutations).observe(notes, { childList: true, subtree: true, attributes: true, attributeFilter: ["hidden"] });
        const ro = new ResizeObserver(() => measure());
        ro.observe(notes);
        window.addEventListener("resize", measure, { passive: true });
        document.fonts?.ready.then(measure).catch(() => {});
        notes.addEventListener("load", measure, true);
        measure();
    }

    reduced.addEventListener("change", () => {
        if (reduced.matches) arrivals.forEach(animation => animation.cancel());
    });

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start, { once: true });
    else start();

    window.LwcLmxDiscussion = { measure };
})();
