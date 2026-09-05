# Frontend TypeScript

Source for reusable `wwwroot/js` scripts. Output stays readable and unbundled, preserving filenames, globals, route-specific loading, `window.modulesReady`, version hashes, and public URLs.

## Build

Use the repository's `.node-version`. From `LongevityWorldCup.Website`:

- `npm ci` after dependency changes or a fresh checkout.
- `npm run check` for strict no-emit type checking.
- `npm run build` after TypeScript changes; clears stale output, compiles, and verifies exact source/output parity. Never commit generated JavaScript.

Normal `dotnet build` invokes the compiler. CI builds assets once and verifies they are untracked. The Node-free production host receives that exact artifact in temporary source and publishes with `BuildFrontend=false`; see [ServerDeployment.md](../../LongevityWorldCup.Documentation/ServerDeployment.md).

Keep strict null, unchecked-index, exact-optional-property, and erasable-syntax checks. Do not bundle, minify, reorder, or rename globals.

## Loading

`HtmlInjectionMiddleware` dynamically imports these ES modules (an empty emitted export is allowed): `misc`, `flags`, `leagueIcons`, `pheno-age`, `bortz-age`, `badges`, `age-visualization`, `play-athlete-flow`, `proof-helpers`, `pro-discounts`, `play-menu`, `bioage-rank-preview`.

HTML rendering reads only the page's referenced partials and required nested dialog fragments. `HtmlAssetPlaceholders` resolves asset tokens once after page assembly, reusing each URL's version within that response. Keep asset mappings there and resolve versions again for each response so file edits remain visible.

Keep these classic scripts free of imports/exports: `flow-action-dock`, `bioage-flow`, `custom-event-markup`, `longevitymaxxing`, `site-statistics-tracking`, `site-statistics`.

The head partial defines `navigateToFlowDestination` synchronously so inline Back handlers work before the asynchronous modules finish. Application Next starts disabled until initialization binds stage validation.

Shared type-only contracts belong in `types/*.d.ts`. Runtime entry points stay self-contained to preserve request order, cache coverage, and independent failure. Ranking fallbacks and athlete-picture transitions have distinct failure, privacy, and timing behavior; consolidation requires equivalence and browser coverage.

## Inline Scripts

Page/partial scripts remain inline where they depend on server placeholders/JSON, injected DOM, exact bootstrap timing, classic globals, or inline handlers. Moving them requires migrating those contracts together with browser coverage, outside unrelated frontend work.

The Markdown page generator owns scripts in generated About, History, and Ruleset pages; edit the generator rather than generated output. The head partial's JSON-LD is structured data, not application JavaScript.
