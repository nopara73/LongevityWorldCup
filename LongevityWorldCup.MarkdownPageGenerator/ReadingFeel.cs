// The moving parts of the long-form reading system, shared by every generated page:
// a reading-progress hairline, a marker that glides along the contents rail, a brief
// emphasis on the section a jump lands on, blocks that settle in as they scroll into
// view, a timeline rail that fills as it is read, an animated phone contents sheet and
// phone tables that read as compact ranked rows. Everything here is decoration on top
// of the static page: without scripts, or with reduced motion, the page reads the same.
internal static class ReadingFeel
{
    public static void Generate(string websiteRoot)
    {
        var path = System.IO.Path.Combine(websiteRoot, "wwwroot", "css", "documentation-reading.css");
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllText(path, Css);
    }

    private const string Css = """

        /* Keep fallback and downloaded font advances comparable on Linux, where
           hinted whole-pixel widths and font-specific kerning can move words. */
        .documentation-page {
            text-rendering: geometricPrecision;
            font-kerning: none;
        }

        @property --documentation-arrive-sweep {
            syntax: "<number>";
            inherits: false;
            initial-value: 1;
        }

        @property --documentation-arrive-alpha {
            syntax: "<number>";
            inherits: false;
            initial-value: 0;
        }

        /* Reading progress: a hairline under the site header that fills with the
           accent as the document is read. Phones show it on the contents bar. */
        .documentation-progress {
            position: fixed;
            top: var(--site-visible-sticky-height, 0px);
            right: 0;
            left: 0;
            z-index: 40;
            height: 3px;
            background: color-mix(in srgb, var(--lwc-border) 55%, transparent);
            opacity: 0;
            pointer-events: none;
            transition: opacity var(--lwc-duration-normal, 220ms) var(--lwc-ease, ease);
        }

        .documentation-progress::before {
            content: "";
            position: absolute;
            inset: 0;
            border-radius: 0 2px 2px 0;
            background: var(--lwc-accent);
            transform: scaleX(var(--documentation-progress, 0));
            transform-origin: left center;
        }

        .documentation-progress.is-visible {
            opacity: 1;
        }

        @media (max-width: 900px) and (min-height: 500px) {
            .documentation-progress {
                display: none;
            }
        }

        /* Contents rail: the trail up to the current section is a soft accent, and
           one solid marker glides to the section being read. */
        .documentation-nav-links {
            position: relative;
        }

        .documentation-nav.has-ink a[href^="#"]:has(~ a.is-active),
        .documentation-nav.has-ink a[href^="#"].is-active {
            border-left-color: var(--lwc-accent);
        }

        .documentation-nav a[href^="#"] {
            transition: border-color var(--lwc-motion-move, 260ms) var(--lwc-ease, ease),
                background-color var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease),
                color var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease);
        }

        .documentation-nav-ink {
            position: absolute;
            top: 0;
            left: 0;
            z-index: 1;
            width: 3px;
            height: var(--ink-h, 0px);
            border-radius: 0 2px 2px 0;
            background: var(--lwc-accent);
            box-shadow: 0 0 0 0 color-mix(in srgb, var(--lwc-accent) 0%, transparent);
            opacity: 0;
            transform: translate(var(--ink-x, 0px), var(--ink-y, 0px));
            pointer-events: none;
        }

        .documentation-nav.has-ink .documentation-nav-ink {
            opacity: 1;
        }

        .documentation-nav.is-ink-ready .documentation-nav-ink {
            transition: transform var(--lwc-motion-slow, 280ms) var(--lwc-ease-standard, ease),
                height var(--lwc-motion-slow, 280ms) var(--lwc-ease-standard, ease),
                width var(--lwc-motion-fast, 140ms) var(--lwc-ease, ease);
        }

        .documentation-nav.is-ink-moving .documentation-nav-ink {
            width: 5px;
        }

        /* A jump lands with a soft accent band sweeping under the heading, and a
           timeline marker gives a small bounce. */
        .documentation-document .is-arriving {
            background-image: linear-gradient(90deg,
                color-mix(in srgb, var(--lwc-accent-soft) calc(var(--documentation-arrive-alpha) * 100%), transparent),
                color-mix(in srgb, var(--lwc-accent-soft) calc(var(--documentation-arrive-alpha) * 100%), transparent));
            background-clip: content-box;
            background-origin: content-box;
            background-repeat: no-repeat;
            background-size: calc(var(--documentation-arrive-sweep) * 100%) 100%;
            border-radius: var(--lwc-radius-sm);
            animation: documentation-arrive 1500ms var(--lwc-ease-standard, ease) both;
        }

        .documentation-timeline-entry:has(> .is-arriving)::before {
            animation: documentation-marker-ring 900ms var(--lwc-ease-standard, ease) 120ms both;
        }

        @keyframes documentation-arrive {
            0% { --documentation-arrive-sweep: 0; --documentation-arrive-alpha: 1; }
            22% { --documentation-arrive-sweep: 1; --documentation-arrive-alpha: 1; }
            60% { --documentation-arrive-alpha: 1; }
            100% { --documentation-arrive-sweep: 1; --documentation-arrive-alpha: 0; }
        }

        @keyframes documentation-marker-ring {
            0% { box-shadow: 0 0 0 4px var(--lwc-surface), 0 0 0 4px color-mix(in srgb, var(--lwc-accent) 60%, transparent); transform: scale(1); }
            35% { transform: scale(1.35); }
            100% { box-shadow: 0 0 0 4px var(--lwc-surface), 0 0 0 14px color-mix(in srgb, var(--lwc-accent) 0%, transparent); transform: scale(1); }
        }

        /* Blocks below the fold settle in as they reach the reader. */
        .documentation-reveal {
            transition: opacity 360ms var(--lwc-ease-enter, ease-out) var(--reveal-delay, 0ms),
                transform 420ms var(--lwc-ease-enter, ease-out) var(--reveal-delay, 0ms);
        }

        .documentation-reveal:not(.is-revealed) {
            opacity: 0;
            transform: translateY(16px);
        }

        /* Focusing a link reveals its block immediately. Keep its hit target
           stationary between pointer down and click while that happens. */
        .documentation-reveal:has(a, button, input, select, textarea):not(.is-revealed) {
            transform: none;
        }

        .documentation-reveal.is-instant,
        .documentation-reveal.is-instant *,
        .documentation-reveal.is-instant::before,
        .documentation-reveal-marker.is-instant::before {
            transition: none !important;
        }

        /* Timeline markers pop onto the rail. */
        .documentation-timeline-entry.documentation-reveal-marker::before {
            transition: transform 460ms var(--lwc-ease-pop, ease) calc(var(--reveal-delay, 0ms) + 80ms);
        }

        .documentation-timeline-entry.documentation-reveal-marker:not(.is-revealed)::before {
            transform: scale(0.2);
        }

        /* Table rows count in from the top, like a leaderboard filling up. */
        .documentation-table.documentation-reveal tbody tr {
            transition: opacity 280ms var(--lwc-ease-enter, ease-out), transform 340ms var(--lwc-ease-enter, ease-out), background-color var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease);
            transition-delay: calc(var(--row, 0) * 30ms + var(--reveal-delay, 0ms) + 80ms), calc(var(--row, 0) * 30ms + var(--reveal-delay, 0ms) + 80ms), 0ms;
        }

        .documentation-table.documentation-reveal:not(.is-revealed) tbody tr {
            opacity: 0;
            transform: translateY(10px);
        }

        /* History's rail fills with the accent up to the entry being read. */
        .documentation-timeline::after {
            content: "";
            position: absolute;
            top: 0.55rem;
            bottom: 0.35rem;
            left: 0.4rem;
            width: 2px;
            border-radius: 1px;
            background: var(--lwc-accent);
            transform: scaleY(var(--timeline-fill, 0));
            transform-origin: top center;
            pointer-events: none;
        }

        .documentation-timeline-entry::before {
            z-index: 1;
        }

        /* Podium ranks wear the accent. */
        .documentation-document tbody tr:nth-child(-n + 3) td.is-numeric:first-child {
            color: var(--lwc-accent-hover);
        }

        /* Keep the whole tile stationary as a pointer or keyboard targets it. */
        .documentation-page #where-to-go-next + ul > li:is(:hover, :focus-within)::before {
            background: var(--lwc-accent);
            color: var(--lwc-on-accent);
        }

        /* The link overlay already outlines the entire destination. */
        html:has(.documentation-page #where-to-go-next + ul > li > a:focus-visible) .lwc-focus-ring {
            opacity: 0;
        }

        /* Narrow tables: each row is one compact line. Rank, then the name, with a
           three-column row's value held at the right edge. */
        @container documentation-table (min-width: 18rem) and (max-width: 24rem) {
            .documentation-document .documentation-table tbody tr:has(> td:nth-child(3):last-child) {
                grid-template-columns: 1.85rem minmax(0, 1fr) auto;
                align-items: center;
                min-height: 2.75rem;
                padding: 0.4rem 0.75rem;
            }

            .documentation-document .documentation-table tbody tr:has(> td:nth-child(3):last-child) > td:first-child {
                grid-row: 1;
            }

            .documentation-document .documentation-table tbody tr:has(> td:nth-child(3):last-child) > td:nth-child(3) {
                grid-row: 1;
                grid-column: 3;
                color: var(--lwc-ink);
                font-size: var(--lwc-type-sm, 0.875rem);
                font-weight: 700;
                text-align: right;
                white-space: nowrap;
            }

            .documentation-document .documentation-table tbody td.is-numeric:first-child {
                box-sizing: border-box;
                display: inline-grid;
                place-items: center;
                width: 1.85rem;
                min-width: 0;
                height: 1.85rem;
                border-radius: var(--lwc-radius-sm, 4px);
                background: var(--lwc-surface-muted);
                font-family: Roboto, sans-serif;
                font-size: var(--lwc-type-xs, 0.75rem);
                text-align: center;
            }

            .documentation-document .documentation-table tbody tr:nth-child(-n + 3) td.is-numeric:first-child {
                background: var(--lwc-accent);
                color: var(--lwc-on-accent);
            }
        }

        @media (max-width: 600px) {
            .documentation-timeline::after {
                left: 5px;
            }
        }

        /* Phones: the contents list drops open as a sheet, its entries following
           in a quick cascade, and the bar's section name rolls up when it changes. */
        @media (max-width: 900px) {
            .documentation-nav.is-open .documentation-nav-links {
                transform-origin: top center;
                animation: documentation-sheet-in 240ms var(--lwc-ease-enter, ease-out) both;
            }

            .documentation-nav.is-open .documentation-nav-links > a {
                animation: documentation-sheet-item 260ms var(--lwc-ease-enter, ease-out) both;
                animation-delay: calc(min(var(--i, 0), 10) * 16ms + 40ms);
            }

            .documentation-nav.documentation-nav.is-closing:not(.is-open) .documentation-nav-links {
                display: block;
                animation: documentation-sheet-out 160ms var(--lwc-ease-exit, ease-in) both;
            }

            .documentation-nav-current-text {
                animation: documentation-label-in 240ms var(--lwc-ease-enter, ease-out) both;
            }

            .documentation-nav-current[data-number]::before {
                transition: background-color var(--lwc-duration-normal, 220ms) var(--lwc-ease, ease);
            }

            .documentation-nav-toggle-icon {
                transition: transform var(--lwc-motion-move, 260ms) var(--lwc-ease-pop, ease), background-color var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease), color var(--lwc-duration-fast, 140ms) var(--lwc-ease, ease);
            }

            .documentation-nav.is-open .documentation-nav-toggle-icon {
                transform: rotate(180deg);
            }

            .documentation-nav .documentation-nav-toggle::after {
                transition: transform 120ms linear;
            }
        }

        @keyframes documentation-sheet-in {
            from { opacity: 0; transform: translateY(-10px); }
        }

        @keyframes documentation-sheet-out {
            to { opacity: 0; transform: translateY(-8px); }
        }

        @keyframes documentation-sheet-item {
            from { opacity: 0; transform: translateY(-6px); }
        }

        @keyframes documentation-label-in {
            from { opacity: 0; transform: translateY(0.55em); }
        }

        @media print {
            .documentation-progress,
            .documentation-nav-ink {
                display: none;
            }

            .documentation-reveal,
            .documentation-reveal * {
                opacity: 1 !important;
                transform: none !important;
            }

            .ruleset-rule-toggle,
            .ruleset-faq-toggle {
                display: none !important;
            }

            .ruleset-rule-body,
            .ruleset-faq-answer {
                content-visibility: visible !important;
                height: auto !important;
                opacity: 1 !important;
                overflow: visible !important;
            }
        }

        @media (prefers-reduced-motion: reduce) {
            .documentation-progress,
            .documentation-nav a[href^="#"],
            .documentation-nav.is-ink-ready .documentation-nav-ink,
            .documentation-nav-toggle-icon,
            .documentation-nav .documentation-nav-toggle::after {
                transition: none;
            }

            .documentation-document .is-arriving,
            .documentation-timeline-entry:has(> .is-arriving)::before,
            .documentation-nav.is-open .documentation-nav-links,
            .documentation-nav.is-open .documentation-nav-links > a,
            .documentation-nav-current-text {
                animation: none;
            }

            .documentation-nav.documentation-nav.is-closing:not(.is-open) .documentation-nav-links {
                display: none;
                animation: none;
            }

            .documentation-reveal,
            .documentation-reveal * {
                opacity: 1 !important;
                transform: none !important;
                transition: none !important;
            }
        }

        /* Apply the reading measure to the actual parents of body blocks.
           The cover, its caption, closing links and compact FAQ have their own type. */
        .documentation-page :is(.documentation-document, .documentation-document .documentation-timeline-entry, .documentation-document .ruleset-rule-body) {
            & > p:where(:not(:has(> img), p:has(> img) + p:has(> em:only-child))) {
                max-width: 68ch;
                line-height: 1.72;
            }

            & > h2 + p:not(:has(> img)),
            &.ruleset-rule-body > p:first-child:not(:has(> img)) {
                max-width: 61ch;
                color: var(--lwc-ink);
                font-size: 1.18rem;
                line-height: 1.6;
            }

            & > ul:not(#where-to-go-next + ul, .ruleset-term-list) {
                --rail-indent: 1.9rem;
                max-width: 68ch;
                margin-inline-start: 0;
                padding-inline-start: var(--rail-indent);
                list-style: none;

                & li {
                    position: relative;
                    margin: 0;
                    padding: .275rem 0;
                    line-height: 1.65;
                }

                & > li::before {
                    content: "";
                    position: absolute;
                    z-index: 1;
                    inset-inline-start: calc(.55rem - var(--rail-indent) - 6px);
                    top: calc(.275rem + .52em);
                    width: 12px;
                    height: 12px;
                    border: 0;
                    border-radius: 50%;
                    background: var(--lwc-surface);
                    box-shadow: inset 0 0 0 3px var(--lwc-accent), 0 0 0 3px var(--lwc-surface);
                }

                /* Each segment ends at the center of the following stop. */
                & > li:not(:last-child)::after {
                    content: "";
                    position: absolute;
                    inset-inline-start: calc(.55rem - var(--rail-indent) - 1px);
                    top: calc(.275rem + .52em + 6px);
                    bottom: calc(-.275rem - .52em - 6px);
                    width: 2px;
                    background: color-mix(in srgb, var(--lwc-accent) 35%, var(--lwc-border));
                }

                & ul {
                    margin: .3rem 0 0;
                    padding: 0;
                    padding-inline-start: 1.4rem;
                    border: 0;
                    list-style: none;
                }

                & ul > li::before {
                    inset-inline-start: -1.1rem;
                    top: calc(.275rem + .6em);
                    width: 8px;
                    height: 8px;
                    box-sizing: border-box;
                    border: 2px solid color-mix(in srgb, var(--lwc-accent) 55%, var(--lwc-border));
                    border-radius: 50%;
                    background: transparent;
                }
            }

            @media (max-width: 600px) {
                & > h2 + p:not(:has(> img)),
                &.ruleset-rule-body > p:first-child:not(:has(> img)) {
                    font-size: 1.08rem;
                }

                /* Retain the phone reading column when the list gains a rail. */
                & > ul:not(#where-to-go-next + ul, .ruleset-term-list) {
                    --rail-indent: 1.55rem;
                }
            }
        }

        .documentation-page:not(.documentation-page-about) .documentation-document :is(.documentation-timeline-entry, .ruleset-rule-body) > p:has(> img:only-child) {
            grid-template-columns: minmax(0, 1fr);
            padding: clamp(8px, 1.4vw, 14px);
            background: var(--lwc-surface);
            box-shadow: 0 1px 0 color-mix(in srgb, var(--lwc-ink) 6%, transparent), 0 18px 32px -26px color-mix(in srgb, var(--lwc-ink) 55%, transparent);
        }
""";
}
