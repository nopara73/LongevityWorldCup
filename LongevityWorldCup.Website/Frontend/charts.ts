// Shared charts and numbers: one look and one feel for every chart, bar,
// sparkline and figure on the site.
//
// - Chart.js charts get one theme (type, grid, tooltip shape), a crosshair
//   that follows the tooltip as it glides between points, a halo on the
//   point being read, a draw-in sweep when the chart scrolls into view, and
//   quiet single-point and empty states. This works through a global
//   Chart.js plugin, so existing charts adopt it without being edited.
// - SVG charts call `crosshair()` for the same guide and tooltip.
// - `count()` rolls a figure to its value with a drawn overlay. The element
//   keeps its real text throughout, so reading, copying, tests and assistive
//   technology always get the result.
// - `reveal()` draws lines, bars and meters in when they enter view.
// - `format()` writes numbers one way: true minus signs, grouped thousands.
//
// Motion never delays content or input and prefers-reduced-motion turns all
// of it off. Charts already on screen while the page loads stay still: only
// charts the visitor scrolls to or opens draw in.

interface LwcChartsFormatOptions {
    readonly decimals?: number;
    readonly sign?: boolean;
    readonly unit?: string;
}

interface LwcCrosshairPoint {
    readonly x: number;
    readonly y: number;
    readonly title: string;
    readonly lines?: readonly string[];
    readonly key?: string;
}

interface LwcCrosshairOptions {
    readonly points: () => readonly LwcCrosshairPoint[];
    readonly onPick?: (key: string) => void;
}

interface LwcChartsApi {
    readonly format: (value: number, options?: LwcChartsFormatOptions) => string;
    readonly count: (element: HTMLElement, from?: number) => void;
    readonly reveal: (element: Element) => void;
    readonly crosshair: (svg: SVGSVGElement, options: LwcCrosshairOptions) => () => void;
    readonly token: (name: string, scope?: Element | null) => string;
    readonly motion: () => boolean;
    readonly install: (chart: unknown) => void;
}

interface Window {
    LwcCharts: LwcChartsApi;
}

(() => {
    if (window.LwcCharts) return;
    const root = document.documentElement;
    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
    // Keep the observed state: reading matches during an animation frame can
    // consume Chromium's pending change before the cleanup listener receives it.
    let prefersReducedMotion = reduced.matches;
    const loadedAt = performance.now();
    const minus = "−";

    function token(name: string, scope?: Element | null): string {
        return getComputedStyle(scope || root).getPropertyValue(name).trim();
    }

    function ms(name: string, fallback: number, scope?: Element | null): number {
        const value = token(name, scope);
        const parsed = parseFloat(value) * (value.endsWith("ms") ? 1 : 1000);
        return Number.isFinite(parsed) && parsed >= 0 ? parsed : fallback;
    }

    function motion(): boolean {
        return !prefersReducedMotion;
    }

    // A chart that is already visible during the first moments of a page load
    // is part of the page, not something arriving: it stays still.
    function arriving(element: Element): boolean {
        if (!motion()) return false;
        if (root.dataset["lwcMotion"] === "on" || performance.now() - loadedAt > 900) return true;
        const box = element.getBoundingClientRect();
        return box.bottom < 0 || box.top > window.innerHeight;
    }

    // --- Numbers ------------------------------------------------------------------

    const grouping = new Map<number, Intl.NumberFormat>();
    function format(value: number, options: LwcChartsFormatOptions = {}): string {
        if (!Number.isFinite(value)) return "—";
        const decimals = options.decimals ?? 0;
        let formatter = grouping.get(decimals);
        if (!formatter) {
            formatter = new Intl.NumberFormat("en-US", { minimumFractionDigits: decimals, maximumFractionDigits: decimals });
            grouping.set(decimals, formatter);
        }
        const rounded = Number(value.toFixed(decimals));
        const magnitude = formatter.format(Math.abs(rounded));
        const sign = rounded < 0 ? minus : options.sign && rounded > 0 ? "+" : options.sign && rounded === 0 ? "±" : "";
        return sign + magnitude + (options.unit ? " " + options.unit : "");
    }

    interface Figure { prefix: string; sign: string; value: number; decimals: number; grouped: boolean; suffix: string }

    function parseFigure(text: string): Figure | null {
        const match = /([+\-−–]?)(\d{1,3}(?:,\d{3})+|\d+)(\.\d+)?/.exec(text);
        if (!match?.[2]) return null;
        const sign = match[1] || "";
        const magnitude = parseFloat(match[2].replace(/,/g, "") + (match[3] || ""));
        return {
            prefix: text.slice(0, match.index),
            sign,
            value: sign && sign !== "+" ? -magnitude : magnitude,
            decimals: match[3] ? match[3].length - 1 : 0,
            grouped: match[2].includes(","),
            suffix: text.slice(match.index + match[0].length)
        };
    }

    const counting = new Map<HTMLElement, number>();
    function stopCount(element: HTMLElement): void {
        const frame = counting.get(element);
        if (frame !== undefined) cancelAnimationFrame(frame);
        counting.delete(element);
        delete element.dataset["lwcCount"];
        element.style.removeProperty("--lwc-count-color");
    }

    function count(element: HTMLElement, from?: number): void {
        const text = element.textContent || "";
        const target = parseFigure(text);
        stopCount(element);
        if (!target || !motion() || !element.isConnected || !element.checkVisibility()) return;
        const start = from !== undefined && Number.isFinite(from) ? from : 0;
        if (Math.abs(start - target.value) < Math.pow(10, -target.decimals) / 2) return;
        const duration = Math.min(1100, 520 + Math.log10(1 + Math.abs(target.value - start)) * 160);
        const began = performance.now();
        const render = (value: number): string => {
            const magnitude = Math.abs(value);
            const digits = target.grouped
                ? new Intl.NumberFormat("en-US", { minimumFractionDigits: target.decimals, maximumFractionDigits: target.decimals }).format(magnitude)
                : magnitude.toFixed(target.decimals);
            const negative = value < 0 && Number(magnitude.toFixed(target.decimals)) !== 0;
            const sign = negative ? (target.sign && target.sign !== "+" ? target.sign : minus) : target.sign === "+" ? "+" : "";
            return target.prefix + sign + digits + target.suffix;
        };
        const tick = (now: number): void => {
            const progress = Math.min(1, (now - began) / duration);
            if (progress >= 1 || !motion() || !element.isConnected || !element.checkVisibility() || (element.textContent || "") !== text) {
                stopCount(element);
                return;
            }
            // Ease out quickly, then settle: the last digit lands gently.
            const eased = 1 - Math.pow(1 - progress, 4);
            element.dataset["lwcCount"] = render(start + (target.value - start) * eased);
            counting.set(element, requestAnimationFrame(tick));
        };
        element.style.setProperty("--lwc-count-color", getComputedStyle(element).color);
        element.dataset["lwcCount"] = render(start);
        counting.set(element, requestAnimationFrame(tick));
    }

    // Opt-in figures: `data-lwc-number` counts from zero (or from the value in
    // the attribute) the first time it is seen, and on from its previous
    // value whenever its text changes later.
    const shownNumber = new WeakMap<HTMLElement, number>();
    const pendingNumbers = new Set<Element>();
    const numberWatch = "IntersectionObserver" in window ? new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            numberWatch?.unobserve(entry.target);
            pendingNumbers.delete(entry.target);
            countFigure(entry.target as HTMLElement, true);
        }
    }, { threshold: 0.6 }) : null;

    function countFigure(element: HTMLElement, fresh: boolean): void {
        const figure = parseFigure(element.textContent || "");
        if (!figure) return;
        const previous = shownNumber.get(element);
        shownNumber.set(element, figure.value);
        const declared = parseFloat(element.dataset["lwcNumber"] || "");
        const from = fresh || previous === undefined ? (Number.isFinite(declared) ? declared : 0) : previous;
        count(element, from);
    }

    function armFigure(element: HTMLElement): void {
        if (shownNumber.has(element)) return;
        const figure = parseFigure(element.textContent || "");
        if (!figure) return;
        if (!arriving(element) || !numberWatch) {
            shownNumber.set(element, figure.value);
            return;
        }
        shownNumber.set(element, NaN);
        pendingNumbers.add(element);
        numberWatch.observe(element);
    }

    // --- Draw-in ------------------------------------------------------------------

    // A clipped chart counts as invisible to IntersectionObserver, so watch
    // its container and draw every chart waiting inside it.
    const drawTargets = new Map<Element, Set<HTMLElement | SVGElement>>();
    const drawWatch = "IntersectionObserver" in window ? new IntersectionObserver(entries => {
        for (const entry of entries) {
            if (!entry.isIntersecting) continue;
            drawWatch?.unobserve(entry.target);
            const targets = drawTargets.get(entry.target);
            drawTargets.delete(entry.target);
            targets?.forEach(drawIn);
        }
    }, { threshold: 0.2 }) : null;

    function drawIn(element: HTMLElement | SVGElement): void {
        if (element.dataset["lwcDraw"] !== "wait") return;
        requestAnimationFrame(() => requestAnimationFrame(() => {
            if (element.dataset["lwcDraw"] !== "wait") return;
            element.dataset["lwcDraw"] = "in";
            element.dispatchEvent(new CustomEvent("lwc-chart-drawn", { bubbles: false }));
            window.setTimeout(() => {
                if (element.dataset["lwcDraw"] === "in") delete element.dataset["lwcDraw"];
            }, ms("--lwc-chart-draw", 760) + 700);
        }));
    }

    const revealed = new WeakSet<Element>();
    function reveal(element: Element): void {
        if (!(element instanceof HTMLElement || element instanceof SVGElement)) return;
        if (revealed.has(element)) return;
        revealed.add(element);
        if (element.dataset["lwcDraw"] || !arriving(element) || !drawWatch) return;
        element.dataset["lwcDraw"] = "wait";
        const watched = element.parentElement || element;
        const targets = drawTargets.get(watched) || new Set<HTMLElement | SVGElement>();
        targets.add(element);
        drawTargets.set(watched, targets);
        drawWatch.observe(watched);
    }

    reduced.addEventListener("change", event => {
        prefersReducedMotion = event.matches;
        if (!prefersReducedMotion) return;
        document.querySelectorAll<HTMLElement>("[data-lwc-draw]").forEach(element => delete element.dataset["lwcDraw"]);
        counting.forEach((_, element) => stopCount(element));
        drawWatch?.disconnect();
        drawTargets.clear();
        charts.forEach(chart => {
            cancelAnimationFrame(chart._lwcRipple || 0);
            delete chart._lwcRippleStart;
            const tooltip = chart.options["plugins"]?.["tooltip"];
            if (tooltip) tooltip.animation = { duration: 0 };
            chart.draw();
        });
    });

    // Shared chart surfaces adopt the draw-in and figures without page code.
    const drawSelector = [
        "[data-lwc-chart]",
        ".traffic-chart svg",
        ".traffic-meter"
    ].join(",");
    const figureSelector = [
        "[data-lwc-number]",
        ".traffic-metric strong",
        ".traffic-outcome strong"
    ].join(",");

    function scan(node: ParentNode): void {
        if (node instanceof Element) {
            if (node.matches(drawSelector)) reveal(node);
            if (node instanceof HTMLElement && node.matches(figureSelector)) armFigure(node);
        }
        node.querySelectorAll(drawSelector).forEach(reveal);
        node.querySelectorAll<HTMLElement>(figureSelector).forEach(armFigure);
    }

    const changed = new Set<HTMLElement>();
    let scanFrame = 0;
    new MutationObserver(records => {
        if (records.some(record => record.removedNodes.length)) {
            pendingNumbers.forEach(element => {
                if (!element.isConnected) { numberWatch?.unobserve(element); pendingNumbers.delete(element); }
            });
            drawTargets.forEach((targets, host) => {
                targets.forEach(element => { if (!element.isConnected) targets.delete(element); });
                if (!targets.size) { drawWatch?.unobserve(host); drawTargets.delete(host); }
            });
        }
        for (const record of records) {
            if (record.type === "childList") {
                record.addedNodes.forEach(node => { if (node instanceof Element) scan(node); });
            }
            const target = record.target instanceof Element ? record.target : record.target.parentElement;
            const figure = target?.closest<HTMLElement>(figureSelector);
            if (figure && shownNumber.has(figure) && !Number.isNaN(shownNumber.get(figure))) changed.add(figure);
        }
        if (!changed.size || scanFrame) return;
        scanFrame = requestAnimationFrame(() => {
            scanFrame = 0;
            changed.forEach(figure => {
                const value = parseFigure(figure.textContent || "")?.value;
                if (value !== undefined && value !== shownNumber.get(figure)) countFigure(figure, false);
            });
            changed.clear();
        });
    }).observe(root, { childList: true, subtree: true, characterData: true });
    if (document.body) scan(document.body);
    else document.addEventListener("DOMContentLoaded", () => scan(document.body), { once: true });

    // --- SVG crosshair ------------------------------------------------------------

    const bindings = new WeakMap<SVGSVGElement, () => void>();
    let tipId = 0;
    function crosshair(svg: SVGSVGElement, options: LwcCrosshairOptions): () => void {
        bindings.get(svg)?.();
        const host = svg.parentElement;
        if (!host) return () => {};
        host.classList.add("lwc-chart-host");
        const guide = document.createElement("span");
        guide.className = "lwc-chart-guide";
        guide.setAttribute("aria-hidden", "true");
        const dot = document.createElement("span");
        dot.className = "lwc-chart-dot";
        dot.setAttribute("aria-hidden", "true");
        const tip = document.createElement("span");
        tip.className = "lwc-chart-tip";
        tip.id = `lwc-chart-tip-${++tipId}`;
        tip.setAttribute("role", "tooltip");
        tip.hidden = true;
        host.append(guide, dot, tip);
        const oldTab = svg.getAttribute("tabindex");
        const oldDescription = svg.getAttribute("aria-describedby");
        svg.setAttribute("tabindex", "0");
        svg.setAttribute("aria-describedby", [oldDescription, tip.id].filter(Boolean).join(" "));
        let selected = -1;
        let lastText = "";
        let disposed = false;

        function show(index: number): void {
            const point = options.points()[index];
            const matrix = svg.getScreenCTM();
            if (disposed || !point || !matrix) { hide(); return; }
            selected = index;
            const screen = new DOMPoint(point.x, point.y).matrixTransform(matrix);
            const box = svg.getBoundingClientRect();
            const hostBox = host!.getBoundingClientRect();
            host!.style.setProperty("--lwc-guide-x", screen.x - hostBox.left + host!.scrollLeft + "px");
            host!.style.setProperty("--lwc-guide-y", screen.y - hostBox.top + host!.scrollTop + "px");
            host!.style.setProperty("--lwc-guide-top", box.top - hostBox.top + host!.scrollTop + "px");
            host!.style.setProperty("--lwc-guide-height", box.height + "px");
            const content = JSON.stringify([point.title, point.lines]);
            if (lastText !== content) {
                lastText = content;
                const title = document.createElement("strong");
                title.textContent = point.title;
                tip.replaceChildren(title, ...(point.lines || []).map(line => {
                    const row = document.createElement("span");
                    row.textContent = line;
                    return row;
                }));
            }
            tip.hidden = false;
            const margin = 8;
            tip.style.maxWidth = Math.max(1, document.documentElement.clientWidth - margin * 2) + "px";
            tip.style.maxHeight = Math.max(1, window.innerHeight - margin * 2) + "px";
            const width = tip.offsetWidth;
            const height = tip.offsetHeight;
            const x = screen.x + width + 14 <= window.innerWidth - margin ? screen.x + 14 : screen.x - width - 14;
            tip.style.left = Math.max(margin, Math.min(x, window.innerWidth - width - margin)) + "px";
            tip.style.top = Math.max(margin, Math.min(screen.y - height / 2, window.innerHeight - height - margin)) + "px";
            tip.tabIndex = tip.scrollHeight > tip.clientHeight ? 0 : -1;
            host!.dataset["lwcReading"] = "";
        }

        function hide(): void {
            delete host!.dataset["lwcReading"];
            tip.hidden = true;
            selected = -1;
        }
        function nearest(clientX: number, clientY: number): number {
            const matrix = svg.getScreenCTM();
            if (!matrix) return -1;
            const local = new DOMPoint(clientX, clientY).matrixTransform(matrix.inverse());
            const points = options.points();
            return points.reduce((best, point, index) => best < 0 || Math.abs(point.x - local.x) < Math.abs(points[best]!.x - local.x) ? index : best, -1);
        }
        const move = (event: PointerEvent): void => { if (event.pointerType === "mouse") show(nearest(event.clientX, event.clientY)); };
        const pick = (event: MouseEvent): void => {
            show(nearest(event.clientX, event.clientY));
            const point = options.points()[selected];
            if (point?.key) options.onPick?.(point.key);
        };
        const leave = (event: PointerEvent): void => {
            if (event.pointerType === "mouse" && !(event.relatedTarget instanceof Node && host!.contains(event.relatedTarget))) hide();
        };
        const focus = (): void => show(Math.max(0, selected));
        const blur = (event: FocusEvent): void => { if (!(event.relatedTarget instanceof Node && host!.contains(event.relatedTarget))) hide(); };
        const key = (event: KeyboardEvent): void => {
            if (event.defaultPrevented || event.isComposing || event.ctrlKey || event.altKey || event.metaKey || event.shiftKey) return;
            const count = options.points().length;
            if (event.key === "Escape") { hide(); event.preventDefault(); return; }
            if (tip.contains(event.target as Node)) return;
            if (!count) return;
            if (["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) {
                event.preventDefault();
                show(event.key === "Home" ? 0 : event.key === "End" ? count - 1 : Math.max(0, Math.min(count - 1, selected + (event.key === "ArrowLeft" ? -1 : 1))));
            } else if (event.key === "Enter" || event.key === " ") {
                event.preventDefault();
                const point = options.points()[selected];
                if (point?.key) options.onPick?.(point.key);
            }
        };
        const refit = (): void => { if (selected >= 0) show(selected); };
        const resize = new ResizeObserver(refit);
        resize.observe(svg);
        svg.addEventListener("pointermove", move);
        svg.addEventListener("click", pick);
        host.addEventListener("pointerleave", leave);
        svg.addEventListener("focus", focus);
        host.addEventListener("focusout", blur);
        host.addEventListener("keydown", key);
        window.addEventListener("resize", refit);
        window.addEventListener("scroll", refit, true);
        const dispose = (): void => {
            if (disposed) return;
            disposed = true;
            resize.disconnect();
            svg.removeEventListener("pointermove", move);
            svg.removeEventListener("click", pick);
            host.removeEventListener("pointerleave", leave);
            svg.removeEventListener("focus", focus);
            host.removeEventListener("focusout", blur);
            host.removeEventListener("keydown", key);
            window.removeEventListener("resize", refit);
            window.removeEventListener("scroll", refit, true);
            oldTab === null ? svg.removeAttribute("tabindex") : svg.setAttribute("tabindex", oldTab);
            oldDescription === null ? svg.removeAttribute("aria-describedby") : svg.setAttribute("aria-describedby", oldDescription);
            guide.remove(); dot.remove(); tip.remove(); hide();
            host.classList.remove("lwc-chart-host");
            bindings.delete(svg);
        };
        bindings.set(svg, dispose);
        return dispose;
    }

    // --- Chart.js -----------------------------------------------------------------

    type Point = { x: number; y: number; skip?: boolean };
    type Meta = { type?: string; hidden?: boolean; data: Point[] };
    type ChartInstance = {
        canvas: HTMLCanvasElement;
        ctx: CanvasRenderingContext2D;
        chartArea?: { left: number; right: number; top: number; bottom: number };
        config: { type?: string };
        options: Record<string, any>;
        data: { datasets: { data: { isAthlete?: boolean; date?: string }[]; borderColor?: unknown }[] };
        scales: Record<string, unknown>;
        tooltip?: { opacity: number; caretX: number; caretY: number; options?: { borderColor?: unknown }; getActiveElements?(): { datasetIndex: number; index: number }[] };
        getDatasetMeta(index: number): Meta;
        draw(): void;
        _lwcRipple?: number;
        _lwcRippleStart?: number;
        _lwcDrawn?: () => void;
    };
    type ChartStatic = {
        register(...items: unknown[]): void;
        defaults: Record<string, any>;
        instances?: Record<string, ChartInstance>;
    };

    function chartScope(chart: ChartInstance): Element {
        return chart.canvas.closest("#detailsModal") || chart.canvas.parentElement || root;
    }

    function cartesian(chart: ChartInstance): boolean {
        return Boolean(chart.scales["x"] && chart.scales["y"]);
    }

    function points(chart: ChartInstance): number {
        return chart.data.datasets.reduce((total, dataset) => total + (Array.isArray(dataset.data) ? dataset.data.length : 0), 0);
    }

    function colorOf(value: unknown, chart: ChartInstance, fallback: string): string {
        return typeof value === "string" && value ? value : token("--lwc-chart-accent", chartScope(chart)) || fallback;
    }

    const charts = new Set<ChartInstance>();
    function ripple(chart: ChartInstance): void {
        if (!motion()) return;
        const started = performance.now();
        const step = (): void => {
            if (!motion() || !chart.canvas.isConnected || !chart.canvas.checkVisibility()) return;
            chart.draw();
            if (performance.now() - started < 1200) chart._lwcRipple = requestAnimationFrame(step);
            else chart._lwcRipple = 0;
        };
        cancelAnimationFrame(chart._lwcRipple || 0);
        chart._lwcRipple = requestAnimationFrame(step);
        (chart as any)._lwcRippleStart = started;
    }

    const plugin = {
        id: "lwcCharts",
        afterInit(chart: ChartInstance): void {
            charts.add(chart);
            const options = chart.options;
            const tooltip = options["plugins"]?.["tooltip"];
            if (tooltip) {
                // One tooltip shape everywhere; charts keep their own colours.
                tooltip.cornerRadius = 8;
                tooltip.caretSize = 6;
                tooltip.caretPadding = 8;
                tooltip.boxPadding = 4;
                tooltip.animation = { duration: motion() ? 180 : 0, easing: "easeOutCubic" };
            }
            // A chart that opts out of drawing animation still lets its tooltip glide.
            if (options["animation"] === false) options["animation"] = { duration: 0 };
            if (cartesian(chart)) {
                for (const axis of ["x", "y"]) {
                    const scale = options["scales"]?.[axis];
                    if (!scale) continue;
                    if (scale.ticks) scale.ticks.padding = scale.ticks.padding ?? 8;
                }
            }
            chart.canvas.dataset["lwcChart"] = chart.config.type || "";
            if (!chart.canvas.dataset["lwcDraw"]) reveal(chart.canvas);
            chart._lwcDrawn = () => ripple(chart);
            chart.canvas.addEventListener("lwc-chart-drawn", chart._lwcDrawn, { once: true });
        },
        beforeDatasetsDraw(chart: ChartInstance): void {
            const area = chart.chartArea;
            const tip = chart.tooltip;
            const ctx = chart.ctx;
            if (!area) return;
            if (!points(chart) && cartesian(chart)) {
                // Empty: a quiet dashed baseline keeps the frame from looking broken.
                ctx.save();
                ctx.strokeStyle = token("--lwc-chart-grid", chartScope(chart)) || "rgba(127,127,127,.35)";
                ctx.setLineDash([3, 5]);
                ctx.beginPath();
                const y = (area.top + area.bottom) / 2;
                ctx.moveTo(area.left, y);
                ctx.lineTo(area.right, y);
                ctx.stroke();
                ctx.restore();
                return;
            }
            // The athlete's own readings join into a trail, in time order, and it
            // grows from the first reading as the chart draws in.
            const ownIndex = ownDataset(chart);
            const own = ownIndex >= 0 ? chart.getDatasetMeta(ownIndex) : null;
            const trail = own && !own.hidden ? own.data.map((point, index) => ({ point, date: chart.data.datasets[ownIndex]!.data[index]?.date || "" }))
                .filter(item => !item.point.skip && Number.isFinite(item.point.x) && Number.isFinite(item.point.y))
                .sort((a, b) => a.date.localeCompare(b.date)).map(item => item.point) : [];
            if (trail.length > 1) {
                const started = (chart as any)._lwcRippleStart as number | undefined;
                const drawing = chart.canvas.dataset["lwcDraw"] === "wait";
                const progress = drawing ? 0 : started ? Math.min(1, (performance.now() - started) / 900) : 1;
                const eased = 1 - Math.pow(1 - progress, 3);
                const sorted = trail;
                ctx.save();
                ctx.beginPath();
                ctx.rect(area.left - 8, area.top - 8, (sorted.at(-1)!.x - sorted[0]!.x) * eased + sorted[0]!.x - area.left + 8, area.bottom - area.top + 16);
                ctx.clip();
                ctx.strokeStyle = colorOf(chart.data.datasets[ownIndex]?.borderColor, chart, "#5bd5dd");
                ctx.globalAlpha = 0.55;
                ctx.lineWidth = 1.75;
                ctx.lineJoin = "round";
                ctx.lineCap = "round";
                ctx.beginPath();
                sorted.forEach((point, index) => index ? ctx.lineTo(point.x, point.y) : ctx.moveTo(point.x, point.y));
                ctx.stroke();
                ctx.restore();
            }
            if (!tip || !tip.opacity || !cartesian(chart) || !tip.getActiveElements?.().length) return;
            const x = Math.max(area.left, Math.min(area.right, tip.caretX));
            const y = Math.max(area.top, Math.min(area.bottom, tip.caretY));
            ctx.save();
            ctx.globalAlpha = Math.min(1, tip.opacity) * 0.7;
            ctx.strokeStyle = colorOf(tip.options?.borderColor, chart, "rgba(127,127,127,.6)");
            ctx.lineWidth = 1;
            ctx.setLineDash([3, 4]);
            ctx.beginPath();
            ctx.moveTo(x, area.bottom);
            ctx.lineTo(x, y);
            ctx.lineTo(area.left, y);
            ctx.stroke();
            ctx.restore();
        },
        afterDatasetsDraw(chart: ChartInstance): void {
            const ctx = chart.ctx;
            const accent = (index: number): string => colorOf(chart.data.datasets[index]?.borderColor, chart, "#5bd5dd");
            // The athlete's own reading: a lone point gets a halo so it reads as the subject, not noise.
            const ownIndex = ownDataset(chart);
            const first = ownIndex >= 0 ? chart.getDatasetMeta(ownIndex) : null;
            const started = (chart as any)._lwcRippleStart as number | undefined;
            const age = started ? performance.now() - started : Infinity;
            if (first && !first.hidden && first.data.length && first.type !== "line") {
                ctx.save();
                ctx.strokeStyle = accent(ownIndex);
                for (const point of first.data) {
                    if (point.skip || !Number.isFinite(point.x) || !Number.isFinite(point.y)) continue;
                    ctx.globalAlpha = 0.28;
                    ctx.lineWidth = 1.5;
                    ctx.beginPath();
                    ctx.arc(point.x, point.y, 11, 0, Math.PI * 2);
                    ctx.stroke();
                    if (age < 1100) {
                        const t = age / 1100;
                        ctx.globalAlpha = 0.55 * (1 - t);
                        ctx.lineWidth = 2;
                        ctx.beginPath();
                        ctx.arc(point.x, point.y, 6 + t * 22, 0, Math.PI * 2);
                        ctx.stroke();
                    }
                }
                ctx.restore();
            }
            const tip = chart.tooltip;
            const active = tip?.opacity ? tip.getActiveElements?.() || [] : [];
            if (!active.length) return;
            ctx.save();
            for (const item of active) {
                const point = chart.getDatasetMeta(item.datasetIndex).data[item.index];
                if (!point) continue;
                ctx.globalAlpha = Math.min(1, tip!.opacity) * 0.22;
                ctx.fillStyle = colorOf(tip!.options?.borderColor, chart, accent(item.datasetIndex));
                ctx.beginPath();
                ctx.arc(point.x, point.y, 13, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.restore();
        },
        beforeDestroy(chart: ChartInstance): void {
            charts.delete(chart);
            cancelAnimationFrame(chart._lwcRipple || 0);
            if (chart._lwcDrawn) chart.canvas.removeEventListener("lwc-chart-drawn", chart._lwcDrawn);
            drawTargets.forEach((targets, host) => {
                targets.delete(chart.canvas);
                if (!targets.size) { drawWatch?.unobserve(host); drawTargets.delete(host); }
            });
            delete chart.canvas.dataset["lwcDraw"];
        }
    };

    function ownDataset(chart: ChartInstance): number {
        if (chart.config.type !== "scatter") return -1;
        return chart.data.datasets.findIndex(dataset => dataset.data.length > 0 && dataset.data.every(point => point?.isAthlete === true));
    }

    const installed = new WeakSet<object>();
    function install(Chart: unknown): void {
        if (typeof Chart !== "function" || installed.has(Chart)) return;
        const api = Chart as unknown as ChartStatic;
        if (typeof api.register !== "function") return;
        api.register(plugin);
        installed.add(Chart);
        const defaults = api.defaults;
        if (!defaults) return;
        const family = token("--lwc-chart-font") || "Roboto, system-ui, sans-serif";
        defaults["font"] = { ...defaults["font"], family, size: 12 };
        defaults["color"] = token("--lwc-chart-muted") || defaults["color"];
        defaults["borderColor"] = token("--lwc-chart-grid") || defaults["borderColor"];
        const tooltip = defaults["plugins"]?.["tooltip"];
        if (tooltip) {
            tooltip.backgroundColor = token("--lwc-chart-tip-bg") || tooltip.backgroundColor;
            tooltip.titleColor = token("--lwc-chart-tip-ink") || tooltip.titleColor;
            tooltip.bodyColor = token("--lwc-chart-tip-ink") || tooltip.bodyColor;
            tooltip.padding = 10;
            tooltip.displayColors = false;
            tooltip.titleFont = { family, size: 12, weight: "600" };
            tooltip.bodyFont = { family, size: 12 };
        }
        if (defaults["elements"]?.point) defaults["elements"].point.hoverRadius = 7;
        if (defaults["elements"]?.line) defaults["elements"].line.borderJoinStyle = "round";
        // An early module may finish loading Chart.js before this deferred
        // script. Adopt already-created charts as well as future lazy instances.
        Object.values(api.instances || {}).forEach(chart => {
            if (charts.has(chart)) return;
            plugin.afterInit(chart);
            chart.draw();
        });
    }

    // Lazy loaders call install after success. Do not redefine a third-party
    // global: replacement constructors, stubs and accessors retain their contract.
    install(window.Chart);
    window.LwcCharts = { format, count, reveal, crosshair, token, motion, install };
})();
