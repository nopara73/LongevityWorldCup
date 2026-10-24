internal static partial class ScoringExplainers
{
    // Every hidden or rewound state is keyed to a numbered step that only the
    // script sets, so the markup's data-step="final" always reads complete.
    public const string Style = """

    <style>
        .scoring-explainer {
            --scoring-ease: var(--lwc-ease-standard, cubic-bezier(0.2, 0.8, 0.2, 1));
            --scoring-pop: var(--lwc-ease-pop, cubic-bezier(0.34, 1.56, 0.64, 1));
            box-sizing: border-box;
            margin: 1.75rem 0 2rem;
            padding: clamp(0.5rem, 1.4vw, 0.85rem);
            border: 1px solid var(--lwc-border, rgba(15, 23, 42, 0.12));
            border-radius: var(--lwc-radius-lg, 8px);
            background: var(--lwc-surface-muted, #eef2f5);
            -webkit-tap-highlight-color: transparent;
        }

        .scoring-plate {
            position: relative;
            padding: clamp(0.85rem, 3vw, 1.5rem) clamp(0.6rem, 3vw, 1.75rem);
            border-radius: var(--lwc-radius-md, 6px);
            background: var(--lwc-surface, #ffffff);
            overflow: hidden;
        }

        .scoring-explainer svg {
            display: block;
            width: 100%;
            max-width: 31rem;
            height: auto;
            margin: 0 auto;
            overflow: visible;
            user-select: none;
            -webkit-user-select: none;
        }

        .scoring-explainer.is-live svg,
        .scoring-explainer.is-live .grid-cell,
        .scoring-explainer.is-live .grid-head,
        .scoring-explainer.is-live .grid-strip {
            cursor: pointer;
        }

        .scoring-explainer.scoring-instant,
        .scoring-explainer.scoring-instant * {
            transition: none !important;
        }

        /* ------------------------------------------------ age-gap gauge */
        .gauge-zone {
            fill: none;
            stroke: var(--lwc-surface-accent, #e7f7f8);
            stroke-width: 26;
            stroke-linecap: round;
        }

        .gauge-tick {
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 2;
            stroke-linecap: round;
            transition: stroke 160ms var(--scoring-ease);
        }

        .gauge-tick.is-lit {
            stroke: var(--lwc-accent, #087685);
        }

        .scoring-gauge.is-over .gauge-tick.is-lit {
            stroke: var(--lwc-warning, #9a6700);
        }

        .gauge-track,
        .gauge-gap {
            fill: none;
            stroke-width: 14;
            stroke-linecap: round;
        }

        .gauge-track {
            stroke: var(--lwc-border, #d7e0e6);
        }

        .gauge-gap {
            stroke: var(--lwc-accent, #087685);
            filter: drop-shadow(0 0 5px color-mix(in srgb, var(--lwc-accent, #087685) 40%, transparent));
        }

        .scoring-gauge.is-over .gauge-gap {
            stroke: var(--lwc-warning, #9a6700);
            filter: none;
        }

        .gauge-chrono-line {
            stroke: var(--lwc-muted, #52606d);
            stroke-width: 1.5;
            stroke-dasharray: 3 5;
            opacity: 0.55;
        }

        .gauge-chrono {
            fill: var(--lwc-surface, #ffffff);
            stroke: var(--lwc-ink, #17212b);
            stroke-width: 3.5;
        }

        .gauge-chrono-notch {
            fill: var(--lwc-ink, #17212b);
        }

        .gauge-needle line {
            stroke: var(--lwc-ink, #17212b);
            stroke-width: 4;
            stroke-linecap: round;
        }

        .gauge-needle-glow {
            fill: var(--lwc-accent, #087685);
            opacity: 0.16;
        }

        .gauge-needle-tip {
            fill: var(--lwc-accent, #087685);
            stroke: var(--lwc-surface, #ffffff);
            stroke-width: 3;
        }

        .scoring-gauge.is-over .gauge-needle-tip,
        .scoring-gauge.is-over .gauge-needle-glow {
            fill: var(--lwc-warning, #9a6700);
        }

        .gauge-hub {
            fill: var(--lwc-ink, #17212b);
        }

        .gauge-hub-dot {
            fill: var(--lwc-surface, #ffffff);
        }

        .scoring-gauge.is-live .gauge-needle-glow {
            transform-box: fill-box;
            transform-origin: center;
            animation: scoring-breathe 2.4s var(--scoring-ease) infinite;
        }

        @keyframes scoring-breathe {
            0%, 100% { transform: scale(0.85); opacity: 0.12; }
            50% { transform: scale(1.18); opacity: 0.24; }
        }

        /* ------------------------------------------------ season timeline */
        .season-svg {
            color: var(--lwc-muted, #52606d);
        }

        .season-window {
            fill: var(--lwc-surface-accent, #e7f7f8);
        }

        .season-year {
            fill: var(--lwc-muted, #52606d);
            font: 700 11px/1 inherit;
            font-family: inherit;
            font-size: 11px;
            font-weight: 700;
            letter-spacing: 0.08em;
        }

        .season-month {
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 1.5;
        }

        .season-month.is-quarter {
            stroke: var(--lwc-muted, #52606d);
            opacity: 0.5;
        }

        .season-before {
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 2;
            stroke-dasharray: 2 4;
            stroke-linecap: round;
        }

        .season-bar {
            fill: color-mix(in srgb, var(--lwc-accent, #087685) 22%, transparent);
        }

        .season-bar-fill {
            fill: var(--lwc-accent, #087685);
        }

        .season-grace {
            stroke: var(--lwc-accent, #087685);
            stroke-width: 3;
            stroke-dasharray: 2 4;
            stroke-linecap: round;
            opacity: 0.7;
        }

        .season-best {
            fill: none;
            stroke: var(--lwc-accent, #087685);
            stroke-width: 3;
            stroke-linejoin: round;
            stroke-linecap: round;
        }

        .season-stem {
            stroke: var(--lwc-accent, #087685);
            stroke-width: 1.5;
            stroke-dasharray: 2 3;
            opacity: 0.35;
        }

        .season-result circle {
            fill: var(--lwc-surface, #ffffff);
            stroke: var(--lwc-accent, #087685);
            stroke-width: 2.5;
        }

        .season-result.is-void circle {
            fill: var(--lwc-surface-muted, #eef2f5);
            stroke: var(--lwc-muted, #52606d);
        }

        .season-result.is-void .season-stem {
            stroke: var(--lwc-muted, #52606d);
        }

        .season-void {
            stroke: var(--lwc-muted, #52606d);
            stroke-width: 2;
            stroke-linecap: round;
        }

        .season-result,
        .alltime-result {
            transform-box: fill-box;
            transform-origin: center bottom;
            transition: opacity 220ms var(--scoring-ease), transform 420ms var(--scoring-pop);
        }

        .season-result.is-void {
            opacity: 0.6;
        }

        .scoring-season.is-live .season-result:not(.is-in),
        .scoring-season.is-live .alltime-result:not(.is-in) {
            opacity: 0;
            transform: translateY(-22px) scale(0.6);
        }

        .season-final {
            fill: var(--lwc-gold, #f5c542);
            stroke: var(--lwc-surface, #ffffff);
            stroke-width: 3;
            transform-box: fill-box;
            transform-origin: center;
            transition: opacity 200ms var(--scoring-ease), transform 460ms var(--scoring-pop);
        }

        .scoring-season[data-locked="false"] .season-final {
            opacity: 0;
            transform: scale(0.2);
        }

        .season-lock-body {
            fill: var(--lwc-ink, #17212b);
            transition: fill 200ms var(--scoring-ease);
        }

        .season-lock-shackle {
            fill: none;
            stroke: var(--lwc-ink, #17212b);
            stroke-width: 2.5;
            stroke-linecap: round;
            transition: transform 360ms var(--scoring-pop), stroke 200ms var(--scoring-ease);
        }

        .scoring-season[data-locked="false"] .season-lock-shackle {
            stroke: var(--lwc-muted, #52606d);
            transform: translate(5px, -5px) rotate(18deg);
        }

        .scoring-season[data-locked="false"] .season-lock-body {
            fill: var(--lwc-muted, #52606d);
        }

        .season-playhead {
            stroke: var(--lwc-ink, #17212b);
            stroke-width: 1.5;
            stroke-dasharray: 3 3;
            opacity: 0;
            transition: opacity 220ms var(--scoring-ease);
        }

        .scoring-season.is-live[data-locked="false"] .season-playhead {
            opacity: 0.55;
        }

        .alltime-lane {
            color: var(--lwc-muted, #52606d);
            stroke-width: 2;
            opacity: 0.6;
        }

        .alltime-result {
            fill: var(--lwc-muted, #52606d);
            opacity: 0.7;
        }

        .alltime-result.is-best {
            fill: var(--lwc-gold, #f5c542);
            stroke: var(--lwc-surface, #ffffff);
            stroke-width: 2.5;
            opacity: 1;
        }

        .alltime-runner {
            fill: var(--lwc-accent, #087685);
            opacity: 0;
        }

        .scoring-season.is-live .alltime-runner {
            animation: scoring-run 3.6s linear infinite;
        }

        @keyframes scoring-run {
            0% { transform: translateX(8px); opacity: 0; }
            10% { opacity: 0.9; }
            90% { opacity: 0.9; }
            100% { transform: translateX(352px); opacity: 0; }
        }

        /* ------------------------------------------------ ranking ladder */
        .ladder-grid {
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 1;
            stroke-dasharray: 2 4;
        }

        .ladder-grid.is-major {
            stroke-dasharray: none;
            opacity: 0.7;
        }

        .ladder-row {
            transform: translateY(calc(26px + var(--rank) * 42px));
            transition: transform 620ms var(--scoring-pop) calc(var(--rank) * 45ms), opacity 200ms var(--scoring-ease);
        }

        .scoring-ladder:is([data-step="0"], [data-step="1"], [data-step="2"]) .ladder-row {
            transform: translateY(calc(26px + var(--start) * 42px));
            transition-delay: 0ms;
        }

        .ladder-hit {
            fill: transparent;
            transition: fill 160ms var(--scoring-ease);
        }

        .ladder-row.is-focus .ladder-hit {
            fill: var(--lwc-surface-accent, #e7f7f8);
        }

        .scoring-ladder[data-focus] .ladder-row:not(.is-focus) {
            opacity: 0.3;
        }

        .ladder-medal {
            fill: var(--lwc-surface, #ffffff);
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 2;
            transform-box: fill-box;
            transform-origin: center;
            transition: fill 240ms var(--scoring-ease) 300ms, stroke 240ms var(--scoring-ease) 300ms, transform 420ms var(--scoring-pop) 300ms;
        }

        .ladder-medal[data-medal="gold"] { fill: var(--lwc-gold, #f5c542); stroke: var(--lwc-gold, #f5c542); }
        .ladder-medal[data-medal="silver"] { fill: var(--lwc-silver, #c9d2da); stroke: var(--lwc-silver, #c9d2da); }
        .ladder-medal[data-medal="bronze"] { fill: var(--lwc-bronze, #dba06d); stroke: var(--lwc-bronze, #dba06d); }

        .scoring-ladder:is([data-step="0"], [data-step="1"], [data-step="2"]) .ladder-medal {
            fill: var(--lwc-surface, #ffffff);
            stroke: var(--lwc-border, #d7e0e6);
            transform: scale(0.7);
            transition-delay: 0ms;
        }

        .ladder-track {
            stroke: var(--lwc-border, #d7e0e6);
            stroke-width: 2.5;
            stroke-linecap: round;
        }

        .ladder-test {
            fill: var(--lwc-muted, #52606d);
            opacity: 0.4;
            transform-box: fill-box;
            transform-origin: center;
            transition: opacity 220ms var(--scoring-ease) calc(var(--i) * 110ms), transform 420ms var(--scoring-pop) calc(var(--i) * 110ms);
        }

        .scoring-ladder[data-step="1"] .ladder-test {
            opacity: 0.85;
        }

        .scoring-ladder[data-step="0"] .ladder-test,
        .scoring-ladder:is([data-step="0"], [data-step="1"]) .ladder-best {
            opacity: 0;
            transform: translateY(-14px) scale(0.4);
        }

        .ladder-gap {
            fill: color-mix(in srgb, var(--lwc-accent, #087685) 30%, transparent);
            transform-box: fill-box;
            transform-origin: right center;
            transition: transform 520ms var(--scoring-ease) 120ms, fill 160ms var(--scoring-ease);
        }

        .ladder-row.is-focus .ladder-gap {
            fill: color-mix(in srgb, var(--lwc-accent, #087685) 55%, transparent);
        }

        .scoring-ladder:is([data-step="0"], [data-step="1"]) .ladder-gap {
            transform: scaleX(0);
        }

        .ladder-best {
            fill: var(--lwc-accent, #087685);
            stroke: var(--lwc-surface, #ffffff);
            stroke-width: 2.5;
            transform-box: fill-box;
            transform-origin: center;
            transition: opacity 220ms var(--scoring-ease) calc(var(--i) * 110ms), transform 460ms var(--scoring-pop) calc(var(--i) * 110ms);
        }

        .ladder-chrono {
            fill: var(--lwc-surface, #ffffff);
            stroke: var(--lwc-ink, #17212b);
            stroke-width: 2.5;
            transform-box: fill-box;
            transform-origin: center;
        }

        .ladder-tie {
            fill: none;
            stroke: var(--lwc-muted, #52606d);
            stroke-width: 2;
            stroke-linecap: round;
            stroke-dasharray: 80;
            stroke-dashoffset: 0;
            transition: stroke-dashoffset 420ms var(--scoring-ease), opacity 200ms var(--scoring-ease);
        }

        .scoring-ladder:not([data-step="4"]):not([data-step="final"]) .ladder-tie {
            stroke-dashoffset: 80;
            opacity: 0;
        }

        .scoring-ladder.is-live[data-step="4"] .ladder-row.is-elder .ladder-chrono {
            animation: scoring-pulse 900ms var(--scoring-pop) 200ms 2;
        }

        .scoring-ladder[data-step="4"] .ladder-row.is-elder .ladder-chrono,
        .scoring-ladder[data-step="final"] .ladder-row.is-elder .ladder-chrono {
            stroke-width: 3.5;
        }

        @keyframes scoring-pulse {
            0%, 100% { transform: scale(1); }
            45% { transform: scale(1.55); }
        }

        /* ------------------------------------------------ division grid */
        .scoring-grid .scoring-plate {
            display: grid;
            gap: 0.85rem;
            max-width: 31rem;
            margin: 0 auto;
            padding-inline: clamp(0.6rem, 3vw, 1.5rem);
        }

        .scoring-grid .scoring-plate {
            max-width: none;
        }

        .grid-strip,
        .grid-board {
            box-sizing: border-box;
            width: 100%;
            max-width: 29rem;
            margin: 0 auto;
        }

        .grid-strip {
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 2px;
            padding: 0.55rem 0.6rem;
            border-radius: var(--lwc-radius-pill, 999px);
            background: var(--lwc-surface-muted, #eef2f5);
        }

        .grid-dot {
            flex: none;
            width: clamp(0.4rem, 1.7vw, 0.6rem);
            aspect-ratio: 1;
            border-radius: 50%;
            background: var(--lwc-muted, #52606d);
            opacity: 0.45;
            transition: opacity 200ms var(--scoring-ease), transform 380ms var(--scoring-pop), background-color 240ms var(--scoring-ease), box-shadow 200ms var(--scoring-ease);
        }

        .grid-strip .grid-dot[data-medal="gold"] { background: var(--lwc-gold, #f5c542); opacity: 1; }
        .grid-strip .grid-dot[data-medal="silver"] { background: var(--lwc-silver, #c9d2da); opacity: 1; }
        .grid-strip .grid-dot[data-medal="bronze"] { background: var(--lwc-bronze, #dba06d); opacity: 1; }

        .grid-board {
            display: grid;
            grid-template-columns: 2.4rem repeat(3, minmax(0, 1fr));
            gap: 0.35rem;
        }

        .grid-corner,
        .grid-head {
            display: flex;
            align-items: center;
            justify-content: center;
            min-height: 2rem;
            border-radius: var(--lwc-radius-md, 6px);
            color: var(--lwc-muted, #52606d);
            font-size: 0.95rem;
            transition: color 180ms var(--scoring-ease), background-color 180ms var(--scoring-ease), transform 240ms var(--scoring-pop);
        }

        .grid-corner {
            color: var(--lwc-accent, #087685);
        }

        .grid-head.is-match {
            color: var(--lwc-accent, #087685);
            background: var(--lwc-surface-accent, #e7f7f8);
            transform: scale(1.08);
        }

        .grid-cell {
            box-sizing: border-box;
            display: flex;
            flex-wrap: wrap;
            align-items: center;
            justify-content: center;
            align-content: center;
            gap: 4px;
            min-height: 2.25rem;
            padding: 0.35rem;
            border: 1px solid var(--lwc-border, #d7e0e6);
            border-radius: var(--lwc-radius-md, 6px);
            background: var(--lwc-surface, #ffffff);
            transition: opacity 200ms var(--scoring-ease), border-color 180ms var(--scoring-ease), background-color 180ms var(--scoring-ease), transform 240ms var(--scoring-pop);
        }

        .grid-cell.is-empty {
            border-style: dashed;
            background: transparent;
        }

        .grid-cell .grid-dot {
            width: 0.62rem;
            background: var(--lwc-accent, #087685);
            opacity: 0.55;
            transition-delay: calc(var(--c) * 22ms + var(--i) * 45ms);
        }

        .grid-cell .grid-dot.is-lead {
            width: 0.78rem;
            background: var(--lwc-gold, #f5c542);
            opacity: 1;
            box-shadow: 0 0 0 2px var(--lwc-surface, #ffffff), 0 0 0 3px color-mix(in srgb, var(--lwc-gold, #f5c542) 70%, transparent);
            order: -1;
        }

        .scoring-grid[data-step="0"] .grid-cell .grid-dot {
            opacity: 0;
            transform: scale(0);
            transition-delay: 0ms;
        }

        .scoring-grid:is([data-step="0"], [data-step="1"]) .grid-cell .grid-dot.is-lead {
            width: 0.62rem;
            background: var(--lwc-accent, #087685);
            box-shadow: none;
        }

        .scoring-grid[data-step="0"] .grid-cell:not(.is-empty) {
            background: transparent;
        }

        .scoring-grid.has-focus .grid-cell:not(.is-match) {
            opacity: 0.3;
        }

        .scoring-grid.has-focus .grid-cell.is-match {
            border-color: var(--lwc-accent, #087685);
            background: var(--lwc-surface-accent, #e7f7f8);
        }

        .scoring-grid.has-focus .grid-strip .grid-dot:not(.is-match) {
            opacity: 0.14;
        }

        .scoring-grid.has-focus .grid-strip .grid-dot.is-match {
            opacity: 1;
            transform: scale(1.35);
        }

        .scoring-grid.has-focus .grid-strip .grid-dot.is-match[data-medal="none"] {
            background: var(--lwc-accent, #087685);
        }

        @media (max-width: 520px) {
            .scoring-explainer {
                margin-inline: -0.25rem;
            }

            .grid-board {
                grid-template-columns: 2rem repeat(3, minmax(0, 1fr));
                gap: 0.3rem;
            }

            .grid-cell .grid-dot {
                width: 0.5rem;
            }

            .grid-cell .grid-dot.is-lead {
                width: 0.64rem;
            }
        }

        @media (prefers-reduced-motion: reduce) {
            .scoring-explainer *,
            .scoring-explainer *::before,
            .scoring-explainer *::after {
                transition: none !important;
                animation: none !important;
            }
        }
    </style>
""";

    public const string Script = """

    <script>
        (() => {
            const figures = Array.from(document.querySelectorAll(".scoring-explainer"));
            if (figures.length === 0) {
                return;
            }

            const reduced = window.matchMedia("(prefers-reduced-motion: reduce)");
            const live = () => !reduced.matches;
            const clamp = (value, low = 0, high = 1) => Math.min(high, Math.max(low, value));
            const easeOut = t => 1 - Math.pow(1 - t, 3);
            const range = (p, from, to) => clamp((p - from) / (to - from));

            // 0 as the figure's top enters the lower viewport, 1 once its middle
            // has risen to a comfortable reading line.
            const progressOf = figure => {
                const rect = figure.getBoundingClientRect();
                const height = window.innerHeight || 1;
                return clamp((height * 0.92 - rect.top) / (height * 0.42 + rect.height * 0.5));
            };

            const svgPoint = (svg, event) => {
                const matrix = svg.getScreenCTM();
                return matrix ? new DOMPoint(event.clientX, event.clientY).matrixTransform(matrix.inverse()) : null;
            };

            const running = new Set();
            let frameQueued = false;
            const tick = () => {
                frameQueued = false;
                for (const animator of Array.from(running)) {
                    if (animator()) {
                        running.delete(animator);
                    }
                }
                if (running.size > 0) {
                    queue();
                }
            };
            const queue = () => {
                if (!frameQueued) {
                    frameQueued = true;
                    requestAnimationFrame(tick);
                }
            };
            const animate = animator => {
                running.add(animator);
                queue();
            };

            const stepper = (figure, thresholds, onStep) => {
                let current = null;
                return p => {
                    const step = thresholds.filter(t => p >= t).length;
                    if (step !== current) {
                        const previous = current;
                        current = step;
                        figure.dataset.step = String(step);
                        onStep?.(step, previous);
                    }
                };
            };

            // Touch taps toggle; a mouse follows hover.
            const bindFocus = (element, enter, leave, toggle) => {
                element.addEventListener("pointerenter", event => event.pointerType === "mouse" && enter());
                element.addEventListener("pointerleave", event => event.pointerType === "mouse" && leave());
                element.addEventListener("pointerup", event => event.pointerType !== "mouse" && toggle());
            };

            function gauge(figure) {
                const svg = figure.querySelector("svg");
                const needle = figure.querySelector(".gauge-needle");
                const gap = figure.querySelector(".gauge-gap");
                const ticks = Array.from(figure.querySelectorAll(".gauge-tick"));
                const chrono = Number(figure.dataset.chrono);
                const rest = Number(figure.dataset.bio);
                const cx = Number(figure.dataset.cx);
                const cy = Number(figure.dataset.cy);
                let value = rest;
                let scrollTarget = rest;
                let override = null;
                let release = 0;

                const draw = v => {
                    needle.setAttribute("transform", `rotate(${(v * 180).toFixed(2)} ${cx} ${cy})`);
                    const low = Math.min(v, chrono);
                    const high = Math.max(v, chrono);
                    gap.setAttribute("stroke-dasharray", `${((high - low) * 100).toFixed(2)} 200`);
                    gap.setAttribute("stroke-dashoffset", (-low * 100).toFixed(2));
                    gap.style.opacity = high - low < 0.004 ? "0" : "1";
                    for (const tickMark of ticks) {
                        const at = Number(tickMark.dataset.v);
                        tickMark.classList.toggle("is-lit", high - low > 0.004 && at >= low - 0.001 && at <= high + 0.001);
                    }
                    figure.classList.toggle("is-over", v > chrono + 0.004);
                };

                const step = () => {
                    const target = override ?? scrollTarget;
                    const delta = target - value;
                    if (!live() || Math.abs(delta) < 0.0008) {
                        value = target;
                        draw(value);
                        return true;
                    }
                    value += delta * 0.14;
                    draw(value);
                    return false;
                };

                const valueAt = event => {
                    const point = svgPoint(svg, event);
                    if (!point) {
                        return null;
                    }
                    let angle = Math.atan2(cy - point.y, point.x - cx);
                    if (angle < 0) {
                        angle = point.x < cx ? Math.PI : 0;
                    }
                    return clamp(1 - angle / Math.PI, 0.03, 0.97);
                };

                svg.addEventListener("pointermove", event => {
                    if (event.pointerType !== "mouse") {
                        return;
                    }
                    override = valueAt(event);
                    animate(step);
                });
                svg.addEventListener("pointerleave", event => {
                    if (event.pointerType === "mouse") {
                        override = null;
                        animate(step);
                    }
                });
                svg.addEventListener("pointerup", event => {
                    if (event.pointerType === "mouse") {
                        return;
                    }
                    override = valueAt(event);
                    animate(step);
                    clearTimeout(release);
                    release = setTimeout(() => {
                        override = null;
                        animate(step);
                    }, 1800);
                });

                return (p, instant) => {
                    scrollTarget = chrono + (rest - chrono) * easeOut(range(p, 0.06, 0.9));
                    if (instant) {
                        value = scrollTarget;
                        draw(value);
                    } else {
                        animate(step);
                    }
                };
            }

            function season(figure) {
                const svg = figure.querySelector("svg");
                const start = Number(figure.dataset.start);
                const close = Number(figure.dataset.close);
                const clip = figure.querySelector(".season-clip");
                const playhead = figure.querySelector(".season-playhead");
                const results = Array.from(figure.querySelectorAll("[data-x]"));
                let value = close;
                let scrollTarget = close;
                let override = null;
                let replaying = false;

                const draw = x => {
                    clip.setAttribute("width", Math.max(0, x).toFixed(1));
                    playhead.setAttribute("transform", `translate(${x.toFixed(1)} 0)`);
                    for (const result of results) {
                        result.classList.toggle("is-in", Number(result.dataset.x) <= x + 0.5);
                    }
                    figure.dataset.locked = String(x >= close - 0.5);
                };

                const step = () => {
                    const target = override ?? scrollTarget;
                    const delta = target - value;
                    if (!live() || Math.abs(delta) < 0.3) {
                        value = target;
                        replaying = false;
                        draw(value);
                        return true;
                    }
                    const limit = replaying ? 3.2 : 60;
                    value += clamp(delta * 0.14, -limit, limit);
                    draw(value);
                    return false;
                };

                svg.addEventListener("pointermove", event => {
                    if (event.pointerType !== "mouse" || replaying) {
                        return;
                    }
                    const point = svgPoint(svg, event);
                    if (point) {
                        override = clamp(point.x, start, close);
                        animate(step);
                    }
                });
                svg.addEventListener("pointerleave", event => {
                    if (event.pointerType === "mouse") {
                        override = null;
                        animate(step);
                    }
                });
                // A click or tap replays the season from before January 1.
                svg.addEventListener("click", () => {
                    override = null;
                    replaying = true;
                    value = start;
                    draw(value);
                    animate(step);
                });

                return (p, instant) => {
                    scrollTarget = start + (close - start) * range(p, 0.04, 0.86);
                    if (instant) {
                        value = scrollTarget;
                        draw(value);
                    } else {
                        animate(step);
                    }
                };
            }

            function ladder(figure) {
                const rows = Array.from(figure.querySelectorAll(".ladder-row"));
                const focus = row => {
                    if (row) {
                        figure.dataset.focus = row.dataset.row;
                    } else {
                        delete figure.dataset.focus;
                    }
                    rows.forEach(other => other.classList.toggle("is-focus", other === row));
                };
                rows.forEach(row => bindFocus(row,
                    () => focus(row),
                    () => focus(null),
                    () => focus(figure.dataset.focus === row.dataset.row ? null : row)));
                const setStep = stepper(figure, [0.1, 0.3, 0.52, 0.78]);
                return p => setStep(p);
            }

            function grid(figure) {
                const cells = Array.from(figure.querySelectorAll(".grid-cell"));
                const heads = Array.from(figure.querySelectorAll(".grid-head"));
                const strip = Array.from(figure.querySelectorAll(".grid-strip .grid-dot"));
                let key = null;
                let demo = 0;

                const focus = (row, col) => {
                    const has = row !== null || col !== null;
                    key = has ? `${row}|${col}` : null;
                    figure.classList.toggle("has-focus", has);
                    const match = (r, c) => (row === null || r === row) && (col === null || c === col);
                    cells.forEach(cell => cell.classList.toggle("is-match", has && match(Number(cell.dataset.row), Number(cell.dataset.col))));
                    heads.forEach(head => head.classList.toggle("is-match", has && (
                        (head.dataset.row !== undefined && Number(head.dataset.row) === row) ||
                        (head.dataset.col !== undefined && Number(head.dataset.col) === col))));
                    strip.forEach(dot => {
                        const [r, c] = dot.dataset.cell.split("-").map(Number);
                        dot.classList.toggle("is-match", has && match(r, c));
                    });
                };

                const bind = (element, row, col) => bindFocus(element,
                    () => {
                        clearTimeout(demo);
                        focus(row, col);
                    },
                    () => focus(null, null),
                    () => {
                        clearTimeout(demo);
                        key === `${row}|${col}` ? focus(null, null) : focus(row, col);
                    });

                cells.forEach(cell => bind(cell, Number(cell.dataset.row), Number(cell.dataset.col)));
                heads.forEach(head => bind(head,
                    head.dataset.row !== undefined ? Number(head.dataset.row) : null,
                    head.dataset.col !== undefined ? Number(head.dataset.col) : null));
                strip.forEach(dot => {
                    const [r, c] = dot.dataset.cell.split("-").map(Number);
                    bind(dot, r, c);
                });

                // Once the leagues fill, one generation lights up across the
                // global line, then lets go.
                const setStep = stepper(figure, [0.12, 0.36, 0.62], (stepNumber, previous) => {
                    if (stepNumber === 3 && previous === 2 && key === null && live()) {
                        demo = setTimeout(() => {
                            focus(3, null);
                            demo = setTimeout(() => focus(null, null), 1500);
                        }, 450);
                    }
                });
                return p => setStep(p);
            }

            const builders = { gauge, season, ladder, grid };
            const controllers = figures
                .map(figure => ({ figure, scroll: builders[figure.dataset.scoring]?.(figure) }))
                .filter(entry => entry.scroll);

            const update = instant => {
                for (const { figure, scroll } of controllers) {
                    scroll(progressOf(figure), instant);
                }
            };

            if (!live()) {
                return;
            }

            for (const { figure } of controllers) {
                figure.classList.add("is-live", "scoring-instant");
            }
            update(true);
            requestAnimationFrame(() => requestAnimationFrame(() => {
                controllers.forEach(({ figure }) => figure.classList.remove("scoring-instant"));
            }));

            let scrollQueued = false;
            const onScroll = () => {
                if (scrollQueued || !live()) {
                    return;
                }
                scrollQueued = true;
                requestAnimationFrame(() => {
                    scrollQueued = false;
                    update(false);
                });
            };
            window.addEventListener("scroll", onScroll, { passive: true });
            window.addEventListener("resize", onScroll, { passive: true });
        })();
    </script>
""";
}
