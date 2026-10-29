// The athlete profile reads like a story that unfolds.
//
// - Chapters (rankings, badges, why, age profile, stats, results, proofs)
//   rise into place as the dialog scrolls them into view.
// - Badges unlock one after another.
// - The age summary grows a gap gauge: chronological age sits in the middle
//   and the biological age travels away from it, younger to the left.
// - Results over time draw in row by row: trends trace themselves, dots land,
//   and each latest value counts on from the athlete's first reading.
// - The radar's result picker gains a scrubbable timeline of every blood draw;
//   dragging across it moves the radar, the summary and the gauge with it.
// - Share and personal links answer hover and press.
//
// This classic script only observes and decorates existing dialog content; it
// never replaces text, delays loading or blocks input. Decorations are
// aria-hidden, and every hidden-before-reveal state is cleared on close, on
// the next athlete and when reduced motion turns on. Motion waits for the
// visitor's first trusted press, key or wheel, matching the shared motion
// gate, and prefers-reduced-motion keeps all of it off.

(() => {
    const modal = document.getElementById("detailsModal");
    const content = modal?.querySelector<HTMLElement>(".modal-content");
    if (!modal || !content) return;
    const dialog: HTMLElement = modal;
    const scroller: HTMLElement = content;

    type ChartLike = { reset(): void; update(mode?: string): void };
    type ChartStatic = { getChart?(canvas: HTMLCanvasElement): ChartLike | undefined };

    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    const chapterSelector = "#athleteRankings, #modalBadgeStrip, #athleteBio, #ageVisualization, #athlete-stats, #athlete-biomarkers, #athlete-proofs, .events-embed";
    let gateOpen = false;
    const timers = new Set<number>();
    const frames = new Set<number>();
    const animations = new Set<Animation>();
    const counters = new Set<HTMLElement>();
    let cancelScrub = (): void => {};

    function later(callback: () => void, delay: number): void {
        const id = window.setTimeout(() => { timers.delete(id); callback(); }, delay);
        timers.add(id);
    }

    function frame(callback: FrameRequestCallback): void {
        const id = requestAnimationFrame(now => { frames.delete(id); callback(now); });
        frames.add(id);
    }

    function animate(element: HTMLElement | null, keyframes: Keyframe[], options: KeyframeAnimationOptions): void {
        if (!element) return;
        // A result can change again before its previous transition has landed.
        for (const animation of animations) {
            if ((animation.effect as KeyframeEffect | null)?.target === element) {
                animation.cancel();
                animations.delete(animation);
            }
        }
        const animation = element.animate(keyframes, options);
        animations.add(animation);
        animation.finished.then(() => animations.delete(animation), () => animations.delete(animation));
    }

    function enabled(): boolean {
        return !reduced.matches && (gateOpen || root.dataset["lwcMotion"] === "on")
            && isOpen() && !scroller.matches(".guess-mode, .is-loading, .has-load-error");
    }

    function openGate(event: Event): void {
        if (!event.isTrusted || reduced.matches || gateOpen) return;
        gateOpen = true;
        if (isOpen()) schedule();
    }
    ["pointerdown", "keydown", "wheel", "touchstart"].forEach(type =>
        window.addEventListener(type, openGate, { capture: true, passive: true }));

    function isOpen(): boolean {
        // The controller owns display explicitly. Observing a content mutation
        // must not flush style/layout just to check whether the dialog is open.
        return dialog.style.display === "block" && !dialog.hidden && !dialog.classList.contains("fade-out");
    }

    function shown(element: Element): element is HTMLElement {
        return element instanceof HTMLElement && element.getClientRects().length > 0;
    }

    // --- Chapters rise as they scroll in -------------------------------------------

    let seen = new WeakSet<Element>();
    let rowsSeen = new WeakSet<Element>();
    const chapterObserver = new IntersectionObserver(entries => entries.forEach(entry => {
        if (!entry.isIntersecting || !enabled()) return;
        chapterObserver.unobserve(entry.target);
        reveal(entry.target as HTMLElement);
    }), { root: scroller, rootMargin: "0px 0px -8% 0px", threshold: 0.06 });

    function viewportBottom(): number {
        return scroller.getBoundingClientRect().bottom;
    }

    function reveal(chapter: HTMLElement): void {
        if (!enabled() || chapter.dataset["story"] !== "wait") return;
        if (chapter.dataset["story"] === "wait") chapter.dataset["story"] = "in";
        settleLater(chapter, "story", 700);
        chapterArrived(chapter, false);
    }

    function settleLater(element: HTMLElement, key: string, ms: number): void {
        later(() => { if (element.dataset[key] === "in") delete element.dataset[key]; }, ms);
    }

    // Each chapter plays its own entrance once its section is on screen.
    function chapterArrived(chapter: HTMLElement, atOpen: boolean): void {
        if (chapter.id === "modalBadgeStrip") unlockBadges(chapter, atOpen ? 260 : 120);
        if (chapter.id === "ageVisualization") { if (!atOpen) redrawRadar(); gapFor(chapter, true); }
        if (chapter.id === "athlete-biomarkers") armRows(chapter);
    }

    function armChapters(): void {
        if (!enabled() || !isOpen()) return;
        const fold = viewportBottom();
        scroller.querySelectorAll<HTMLElement>(chapterSelector).forEach(chapter => {
            if (seen.has(chapter) || !shown(chapter) || !chapter.childElementCount && !chapter.textContent?.trim()) return;
            seen.add(chapter);
            if (chapter.getBoundingClientRect().top < fold - 24) {
                // Only a profile that has just opened plays its first screen in.
                if (performance.now() - openedAt < 1500) chapterArrived(chapter, true);
                return;
            }
            chapter.dataset["story"] = "wait";
            chapterObserver.observe(chapter);
        });
    }

    // --- Badges unlock -------------------------------------------------------------

    function unlockBadges(strip: HTMLElement, delay: number): void {
        if (!enabled()) return;
        const items = Array.from(strip.querySelectorAll<HTMLElement>(".modal-badge-item")).filter(shown);
        items.forEach((item, index) => {
            if (item.contains(document.activeElement)) return;
            item.dataset["unlock"] = "locked";
            later(() => {
                if (item.dataset["unlock"] !== "locked") return;
                item.dataset["unlock"] = "open";
                // The open state is only an entrance; a settled badge has no animation.
                later(() => { if (item.dataset["unlock"] === "open") delete item.dataset["unlock"]; }, 900);
            }, delay + index * 85);
        });
    }

    // --- The gap between biological and chronological age -------------------------

    function gauge(summary: HTMLElement): HTMLElement {
        let gap = summary.querySelector<HTMLElement>(".age-story-gap");
        if (gap) return gap;
        gap = document.createElement("span");
        gap.className = "age-story-gap";
        gap.setAttribute("aria-hidden", "true");
        ["track", "fill", "chrono", "bio"].forEach(part => {
            const piece = document.createElement("span");
            piece.className = "age-story-gap-" + part;
            gap!.appendChild(piece);
        });
        const age = summary.querySelector(".age-bio-age");
        if (age) age.before(gap);
        else summary.appendChild(gap);
        return gap;
    }

    function readNumber(element: Element | null): number {
        const text = (element?.textContent || "").trim().replace(/[−–]/g, "-");
        // Censored lab readings (e.g. <0.5) are bounds, not exact values.
        return /^[+-]?(?:\d+(?:\.\d+)?|\.\d+)(?:\s+years?)?$/.test(text) ? parseFloat(text) : NaN;
    }

    // Twenty years either side of the calendar age fills the whole track.
    function gapFor(visualization: Element | null, fromCalendar: boolean): void {
        (visualization ?? document).querySelectorAll<HTMLElement>("#ageRadarCenter, #ageRadarFallback").forEach(summary => {
            const gap = gauge(summary);
            const diff = readNumber(summary.querySelector(".age-diff"));
            gap.hidden = !Number.isFinite(diff);
            const ratio = Number.isFinite(diff) ? Math.max(-1, Math.min(1, diff / 20)) : 0;
            // A younger result wears the chosen clock's colour; an older one the summary's own.
            const canvas = document.getElementById("ageRadarChart");
            const clock = canvas?.dataset["clock"] === "pheno" ? "pheno" : "bortz";
            gap.dataset["direction"] = ratio < 0 ? "younger" : ratio > 0 ? "older" : "even";
            gap.dataset["clock"] = clock;
            const value = summary.querySelector<HTMLElement>(".age-diff");
            if (value && ratio > 0) gap.style.setProperty("--story-gap-color", getComputedStyle(value).color);
            else gap.style.removeProperty("--story-gap-color");
            const visual = document.getElementById("ageVisualization");
            if (visual) visual.dataset["storyClock"] = clock;
            if (!enabled()) {
                gap.dataset["still"] = "";
                gap.style.setProperty("--story-gap", String(ratio));
                return;
            }
            delete gap.dataset["still"];
            if (fromCalendar) {
                gap.dataset["still"] = "";
                gap.style.setProperty("--story-gap", "0");
                void gap.offsetWidth;
                delete gap.dataset["still"];
            }
            frame(() => gap.style.setProperty("--story-gap", String(ratio)));
        });
    }

    function redrawRadar(): void {
        const canvas = document.getElementById("ageRadarChart");
        const chart = canvas instanceof HTMLCanvasElement ? (window.Chart as unknown as ChartStatic | undefined)?.getChart?.(canvas) : undefined;
        if (!chart || !enabled()) return;
        chart.reset();
        chart.update();
    }

    // --- A timeline of every blood draw, scrubbable ---------------------------------

    let timeline: HTMLElement | null = null;
    let lastIndex = -1;

    function picker(): { select: HTMLSelectElement; wrap: HTMLElement } | null {
        const select = document.getElementById("ageResultSelect");
        const wrap = document.getElementById("ageResultPicker");
        return select instanceof HTMLSelectElement && wrap ? { select, wrap } : null;
    }

    function buildTimeline(): void {
        const found = picker();
        if (!found) return;
        const { select, wrap } = found;
        const options = Array.from(select.options);
        const ages = options.map(option => parseFloat((option.textContent || "").split("·").pop() || ""));
        if (!timeline) {
            timeline = document.createElement("div");
            timeline.className = "age-story-timeline";
            timeline.setAttribute("aria-hidden", "true");
            wrap.after(timeline);
            bindScrub(timeline);
        }
        const many = options.length > 1 && !wrap.hidden && !select.hidden;
        timeline.hidden = !many;
        if (!many) return;
        const finite = ages.filter(Number.isFinite);
        const low = Math.min(...finite);
        const high = Math.max(...finite);
        const spread = high - low || 1;
        const svgNs = "http://www.w3.org/2000/svg";
        const svg = document.createElementNS(svgNs, "svg");
        svg.setAttribute("viewBox", "0 0 100 28");
        svg.setAttribute("preserveAspectRatio", "none");
        const xAt = (index: number): number => options.length === 1 ? 50 : 4 + index / (options.length - 1) * 92;
        // Younger results sit higher: the story reads up as the clock turns back.
        const yAt = (age: number): number => Number.isFinite(age) ? 6 + (age - low) / spread * 16 : 14;
        const path = document.createElementNS(svgNs, "polyline");
        path.setAttribute("class", "age-story-timeline-line");
        path.setAttribute("points", options.map((_, index) => `${xAt(index)},${yAt(ages[index] ?? NaN)}`).join(" "));
        path.setAttribute("pathLength", "1");
        svg.appendChild(path);
        const dots = document.createElement("div");
        dots.className = "age-story-timeline-dots";
        options.forEach((_, index) => {
            const dot = document.createElement("span");
            dot.className = "age-story-timeline-dot";
            dot.style.left = xAt(index) + "%";
            dot.style.top = yAt(ages[index] ?? NaN) / 28 * 100 + "%";
            dot.style.setProperty("--i", String(index));
            dots.appendChild(dot);
        });
        const thumb = document.createElement("span");
        thumb.className = "age-story-timeline-thumb";
        dots.appendChild(thumb);
        timeline.replaceChildren(svg, dots);
        timeline.dataset["count"] = String(options.length);
        if (enabled()) {
            timeline.dataset["draw"] = "wait";
            frame(() => frame(() => {
                if (timeline && enabled()) {
                    timeline.dataset["draw"] = "in";
                    settleLater(timeline, "draw", 900);
                }
            }));
        } else delete timeline?.dataset["draw"];
        lastIndex = -1;
        markSelected();
    }

    function markSelected(): void {
        const found = picker();
        if (!found || !timeline || timeline.hidden) return;
        const index = found.select.selectedIndex;
        const dots = timeline.querySelectorAll<HTMLElement>(".age-story-timeline-dot");
        dots.forEach((dot, dotIndex) => dot.classList.toggle("is-selected", dotIndex === index));
        const dot = dots[index];
        const thumb = timeline.querySelector<HTMLElement>(".age-story-timeline-thumb");
        if (dot && thumb) {
            thumb.style.left = dot.style.left;
            thumb.style.top = dot.style.top;
        }
        if (lastIndex >= 0 && lastIndex !== index) stepFeel(index > lastIndex ? 1 : -1);
        lastIndex = index;
    }

    // Moving between results slides the date the way time moved.
    function stepFeel(direction: number): void {
        if (!enabled()) return;
        const date = document.getElementById("ageResultDate");
        animate(date, [
            { transform: `translateX(${direction * 14}px)`, opacity: 0 },
            { transform: "none", opacity: 1 }
        ], { duration: 220, easing: "cubic-bezier(0, 0, 0, 1)" });
        const summary = document.getElementById("ageRadarCenter");
        animate(summary, [{ transform: "scale(.985)" }, { transform: "none" }], { duration: 260, easing: "cubic-bezier(0.2, 0, 0, 1)" });
    }

    function bindScrub(track: HTMLElement): void {
        let dragging = -1;
        let startX = 0;
        let startY = 0;
        let active = false;
        cancelScrub = (): void => {
            const id = dragging;
            dragging = -1;
            active = false;
            track.classList.remove("is-scrubbing");
            if (id >= 0 && track.hasPointerCapture(id)) track.releasePointerCapture(id);
        };
        const choose = (clientX: number): void => {
            const found = picker();
            if (!found) return;
            const bounds = track.getBoundingClientRect();
            const count = found.select.options.length;
            if (count < 2 || !bounds.width) return;
            const ratio = (clientX - bounds.left) / bounds.width;
            const index = Math.max(0, Math.min(count - 1, Math.round((ratio * 100 - 4) / 92 * (count - 1))));
            if (index === found.select.selectedIndex) return;
            found.select.selectedIndex = index;
            found.select.dispatchEvent(new Event("change", { bubbles: true }));
        };
        track.addEventListener("pointerdown", event => {
            if (!event.isPrimary || dragging >= 0) { cancelScrub(); return; }
            if (event.button !== 0 || !isOpen()) return;
            // This control owns horizontal gestures; the surrounding profile
            // must not also interpret the same drag as an athlete switch.
            event.stopPropagation();
            dragging = event.pointerId;
            startX = event.clientX;
            startY = event.clientY;
            active = event.pointerType === "mouse";
            if (active) {
                track.setPointerCapture(event.pointerId);
                track.classList.add("is-scrubbing");
                choose(event.clientX);
            }
        });
        track.addEventListener("pointermove", event => {
            if (event.pointerId !== dragging) return;
            if (!active) {
                const dx = Math.abs(event.clientX - startX);
                const dy = Math.abs(event.clientY - startY);
                if (dy > 8 && dy >= dx) { cancelScrub(); return; }
                if (dx < 8 || dx < dy * 1.5) return;
                active = true;
                track.setPointerCapture(event.pointerId);
                track.classList.add("is-scrubbing");
            }
            choose(event.clientX);
        });
        const stop = (event: PointerEvent): void => {
            if (event.pointerId !== dragging) return;
            if (event.type === "pointerup") choose(event.clientX);
            cancelScrub();
        };
        track.addEventListener("pointerup", stop);
        track.addEventListener("pointercancel", stop);
        track.addEventListener("lostpointercapture", event => {
            // Touch initially captures the hit dot. Promoting that capture to
            // the track must not cancel the scrub when the dot releases it.
            if (event.target === track) stop(event);
        });
        window.addEventListener("blur", cancelScrub);
    }

    // --- Results over time draw in ---------------------------------------------------

    const rowObserver = new IntersectionObserver(entries => {
        if (!enabled()) return;
        const arriving = entries.filter(entry => entry.isIntersecting).map(entry => entry.target as HTMLElement);
        arriving.forEach((row, index) => {
            rowObserver.unobserve(row);
            later(() => drawRow(row), index * 70);
        });
    }, { root: scroller, rootMargin: "0px 0px -6% 0px", threshold: 0.2 });

    function armRows(section: HTMLElement): void {
        if (!enabled()) return;
        section.querySelectorAll<HTMLElement>("tr.result-summary").forEach(row => {
            if (rowsSeen.has(row) || !shown(row)) return;
            rowsSeen.add(row);
            if (row.contains(document.activeElement)) return;
            row.dataset["draw"] = "wait";
            row.querySelectorAll<SVGGeometryElement>(".biomarker-overview-history svg line:not(.sparkline-track):not(.sparkline-tick)")
                .forEach(line => line.setAttribute("pathLength", "1"));
            row.querySelectorAll<SVGElement>(".biomarker-overview-history svg circle, .clock-history-reading")
                .forEach((dot, index) => (dot as SVGElement | HTMLElement).style.setProperty("--i", String(index)));
            rowObserver.observe(row);
        });
    }

    function drawRow(row: HTMLElement): void {
        if (row.dataset["draw"] !== "wait" || !row.isConnected || !enabled()) return;
        row.dataset["draw"] = "in";
        countFromFirst(row);
        later(() => {
            if (row.dataset["draw"] !== "in") return;
            delete row.dataset["draw"];
            row.querySelectorAll(".biomarker-overview-history svg line[pathLength]").forEach(line => line.removeAttribute("pathLength"));
        }, 1400);
    }

    // The latest value counts on from where the athlete started.
    function countFromFirst(row: HTMLElement): void {
        const current = row.querySelector<HTMLElement>(".biomarker-overview-current");
        const change = row.querySelector<HTMLElement>(".biomarker-overview-change");
        if (!current || !enabled()) return;
        const text = current.textContent || "";
        const latest = readNumber(current);
        const delta = change ? readNumber(change) : NaN;
        if (!Number.isFinite(latest) || !Number.isFinite(delta) || delta === 0) return;
        const decimals = (text.split(".")[1] || "").replace(/\D.*$/, "").length;
        const from = latest - delta;
        const start = performance.now();
        const duration = 760;
        current.style.setProperty("--lwc-count-color", getComputedStyle(current).color);
        counters.add(current);
        const tick = (now: number): void => {
            if (!enabled() || !current.isConnected || current.textContent !== text || row.dataset["draw"] !== "in") { clearCount(current); return; }
            const progress = Math.min(1, (now - start) / duration);
            const value = from + (latest - from) * (1 - Math.pow(1 - progress, 3));
            if (progress >= 1) { clearCount(current); return; }
            current.dataset["lwcCount"] = text.replace(/[+−–-]?[0-9]+(\.[0-9]+)?/, value.toFixed(decimals));
            frame(tick);
        };
        frame(tick);
    }

    function clearCount(current: HTMLElement): void {
        delete current.dataset["lwcCount"];
        current.style.removeProperty("--lwc-count-color");
        counters.delete(current);
    }

    // --- Links answer the press ------------------------------------------------------

    dialog.addEventListener("pointerdown", event => {
        const link = (event.target as Element | null)?.closest?.<HTMLElement>("#detailsModal .athlete-links > a, #detailsModal .athlete-share-wrapper > button");
        if (!link || !enabled()) return;
        delete link.dataset["pressed"];
        void link.offsetWidth;
        link.dataset["pressed"] = "";
        later(() => delete link.dataset["pressed"], 520);
    });

    // Keyboard focus reveals content immediately, even before intersection
    // callbacks run, and never leaves its control behind an entrance effect.
    function revealForInteraction(target: Element): void {
        for (let element: Element | null = target; element && element !== dialog; element = element.parentElement) {
            if (!(element instanceof HTMLElement)) continue;
            if (element.matches(chapterSelector)) seen.add(element);
            if (element.matches("tr.result-summary")) rowsSeen.add(element);
            delete element.dataset["story"];
            delete element.dataset["unlock"];
            delete element.dataset["draw"];
            chapterObserver.unobserve(element);
            rowObserver.unobserve(element);
            element.querySelectorAll<HTMLElement>(".biomarker-overview-current").forEach(clearCount);
        }
    }
    dialog.addEventListener("focusin", event => {
        if (event.target instanceof Element) revealForInteraction(event.target);
    });
    dialog.addEventListener("pointerover", event => {
        if (event.target instanceof Element && event.target.closest(".badge-class, .athlete-links a, .athlete-share-wrapper button"))
            revealForInteraction(event.target);
    });

    // --- Lifecycle ---------------------------------------------------------------------

    function clearAll(): void {
        window.clearTimeout(pending);
        timers.forEach(id => window.clearTimeout(id));
        timers.clear();
        frames.forEach(id => cancelAnimationFrame(id));
        frames.clear();
        animations.forEach(animation => animation.cancel());
        animations.clear();
        counters.forEach(clearCount);
        chapterObserver.disconnect();
        rowObserver.disconnect();
        cancelScrub();
        scroller.querySelectorAll<HTMLElement>("[data-story]").forEach(element => {
            delete element.dataset["story"];
            chapterObserver.unobserve(element);
        });
        scroller.querySelectorAll<HTMLElement>("[data-unlock]").forEach(element => delete element.dataset["unlock"]);
        scroller.querySelectorAll<HTMLElement>("[data-draw]").forEach(element => {
            delete element.dataset["draw"];
            rowObserver.unobserve(element);
        });
        scroller.querySelectorAll(".biomarker-overview-history line[pathLength]").forEach(line => line.removeAttribute("pathLength"));
        scroller.querySelectorAll<HTMLElement>("[data-pressed]").forEach(element => delete element.dataset["pressed"]);
        lastIndex = -1;
    }

    function forget(): void {
        clearAll();
        seen = new WeakSet<Element>();
        rowsSeen = new WeakSet<Element>();
    }

    let openedAt = 0;
    let pending = 0;
    function schedule(): void {
        window.clearTimeout(pending);
        pending = window.setTimeout(() => {
            if (!isOpen()) return;
            armChapters();
            const section = document.getElementById("athlete-biomarkers");
            if (section && seen.has(section) && section.dataset["story"] !== "wait") armRows(section);
            gapFor(document.getElementById("ageVisualization"), false);
        }, 80);
    }

    new MutationObserver(records => {
        if (!isOpen() || scroller.matches(".guess-mode, .is-loading, .has-load-error")) return;
        let pickerChanged = false;
        let resultChanged = false;
        let relevant = false;
        for (const record of records) {
            const target = record.target instanceof Element ? record.target : record.target.parentElement;
            if (!target) continue;
            if (target.closest(".age-story-gap, .age-story-timeline") || target.matches?.("[data-lwc-count]")) continue;
            relevant = true;
            if (target.closest("#ageResultSelect")) pickerChanged = true;
            else if (target.closest("#ageResultDate, #ageRadarCenter, #ageRadarFallback")) resultChanged = true;
        }
        if (pickerChanged) buildTimeline();
        if (resultChanged) { markSelected(); gapFor(document.getElementById("ageVisualization"), false); }
        if (relevant) schedule();
    }).observe(scroller, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ["hidden"] });

    let unavailable = scroller.matches(".guess-mode, .is-loading, .has-load-error");
    new MutationObserver(() => {
        const blocked = scroller.matches(".guess-mode, .is-loading, .has-load-error");
        if (blocked !== unavailable) {
            if (blocked) clearAll();
            else if (isOpen()) { buildTimeline(); schedule(); }
        }
        unavailable = blocked;
    }).observe(scroller, { attributes: true, attributeFilter: ["class"] });

    let wasOpen = isOpen();
    new MutationObserver(() => {
        const open = isOpen();
        if (open && !wasOpen) { openedAt = performance.now(); forget(); schedule(); }
        if (!open && wasOpen) forget();
        wasOpen = open;
    }).observe(dialog, { attributes: true, attributeFilter: ["style", "class", "hidden"] });

    dialog.addEventListener("athlete-dialog-loading", () => { openedAt = performance.now(); forget(); schedule(); });
    dialog.addEventListener("athlete-dialog-closed", forget);
    reduced.addEventListener("change", () => {
        if (!reduced.matches) return;
        gateOpen = false;
        clearAll();
        gapFor(document.getElementById("ageVisualization"), false);
    });
    buildTimeline();
    if (isOpen()) schedule();
})();
