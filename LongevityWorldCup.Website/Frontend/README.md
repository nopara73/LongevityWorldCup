# Frontend TypeScript

This directory is the source of every reusable script served from `wwwroot/js`. The TypeScript compiler emits readable, unbundled JavaScript with the same file names so the middleware's route-specific loading, `window.modulesReady`, asset version hashes, and public `/js/*.js` URLs remain unchanged.

## Build contract

- Use the Node version pinned in the repository's `.node-version` file.
- Run `npm ci` in `LongevityWorldCup.Website` after dependency changes or a fresh checkout.
- Run `npm run check` for a no-emit strict type check.
- Run `npm run build` after changing TypeScript and commit the corresponding `wwwroot/js` output.
- Normal `dotnet build` invokes the same compiler and verifies source/output file parity. CI rebuilds the assets and rejects modified or untracked output, preventing stale generated JavaScript from being merged.
- The production host is intentionally .NET-only. Its documented publish command passes `BuildFrontend=false` and consumes the generated JavaScript that CI already verified.

The compiler uses strict null checking, unchecked-index checks, exact optional properties, and erasable TypeScript syntax only. The build does not bundle, minify, reorder, or rename globals.

## Loading contracts

These files are dynamically imported as ES modules by `HtmlInjectionMiddleware` and may contain an emitted empty export: `misc`, `flags`, `leagueIcons`, `pheno-age`, `bortz-age`, `badges`, `age-visualization`, `play-athlete-flow`, `proof-helpers`, `pro-discounts`, `play-menu`, and `bioage-rank-preview`.

These files must remain classic scripts with no import or export syntax because their timing or direct `<script>` use is part of the page contract: `flow-action-dock`, `bioage-flow`, `custom-event-markup`, `longevitymaxxing`, `site-statistics-tracking`, and `site-statistics`.

## Intentionally retained inline JavaScript

HTML templates still contain page-local inline scripts. They are not duplicate reusable assets and remain inline by design in this migration because moving them would change one or more established runtime contracts:

- server placeholders and injected JSON are evaluated in the template at request time;
- head bootstraps must run at their exact position to avoid theme, navigation, or layout flicker;
- biological-age forms expose classic globals to existing inline event attributes;
- leaderboard, event board, onboarding, profile, proof, and internal-tool scripts are tightly coupled to injected partial DOM and script ordering;
- the Markdown page generator owns the shared script emitted into generated About, History, and Ruleset pages.

Converting those blocks safely requires migrating the template data boundary and inline handlers together, with dedicated browser coverage. Treating them as modules or copying them mechanically into external files would risk startup ordering, unresolved server tokens, or missing global callbacks. They should not be moved opportunistically as part of unrelated frontend work.

The retained executable-script inventory is intentionally limited to these owners:

- shared boot and injected-partial behavior in `partials/head.html`, `partials/header.html`, the progress-bar partials, and `HtmlInjectionMiddleware`;
- page-coupled behavior in the home page, leaderboard, Event boards, Guess My Age, onboarding calculators and application review, play profile/proof pages, unsubscribe, the Hungarian challenge page, media page, and the custom Event designer;
- the Markdown page generator's source block and its generated About, History, and Ruleset output.

The structured-data block in `partials/head.html` is JSON-LD rather than application JavaScript. Generated documentation pages should be changed through `LongevityWorldCup.MarkdownPageGenerator`, not by editing their emitted inline script independently.
