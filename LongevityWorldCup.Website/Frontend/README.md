# Frontend TypeScript

Source for reusable `wwwroot/js` scripts. Output stays readable and unbundled, preserving filenames, globals, route-specific loading, `window.modulesReady`, version hashes, and public URLs. TypeScript remains strictly checked. Classic `.js` entry points extracted from HTML retain their existing untyped code and global scope; the build syntax-checks and copies them byte-for-byte, then verifies output parity. Do not introduce imports/exports or silently convert them into modules.

## Build

Use the repository's `.node-version`. From `LongevityWorldCup.Website`:

- `npm ci` after dependency changes or a fresh checkout.
- `npm run check` for strict no-emit type checking.
- `npm run build` after frontend changes; clears stale output, compiles TypeScript, checks and copies classic scripts, and verifies exact source/output parity. Never commit generated JavaScript.

Normal `dotnet build` invokes the compiler. CI builds assets once and verifies they are untracked. The Node-free production host receives that exact artifact in temporary source and publishes with `BuildFrontend=false`; see [ServerDeployment.md](../../LongevityWorldCup.Documentation/ServerDeployment.md).

Keep strict null, unchecked-index, exact-optional-property, and erasable-syntax checks. Do not bundle, minify, reorder, or rename globals.

## Loading

Challenge HTML starts with a neutral busy state before any script downloads. Resolve private token, confirmation, or saved-session access before revealing its view; participant responses already include public state and must not wait for a separate public request. Release initial busy state only after panels and the focused check-in dialog agree, and expose recovery controls when resolution fails. Athlete dialogs show Guess My Age only in an active, loaded game; a skipped game cannot play an exit animation during profile loading or hydration.

Stateful task pages load the versioned `initial-view` classic script and stylesheet in the head, call `prepare`, and mark their task container with `initial-view-main`. Initialize through `run` and call `complete` only after the actual panel, identity, fields, pricing, or filters agree. Keep inline Back navigation usable during loading. Initialization errors or the fifteen-second deadline show Retry while keeping incomplete defaults hidden; reload preserves saved browser data. Accepted submission recovery calls `hold` while its existing confirmation hands off to the next page.

Calculator heads prepare this state through `LwcBioageFlow` for updates, saved drafts, shared values, and stored biomarker handoffs. Call `completeInitialView` after restoring the mode, step, and raw values, before resetting scroll or measuring the visible form. A fresh calculator without prefill stays immediately visible. Optional athlete/division directories enrich already-restored fields; they must not gate draft restoration or overwrite later edits. Play menus resolve the requested panel, returning-athlete action order, and discounts before revealing it. Review pages restore their result/edit/application source and contact address synchronously.

Public document URLs are validated and canonicalized on the server before rendering. Keep frontend legacy-route and browser-history handling consistent with [CrawlAndUrlPolicy.md](../../LongevityWorldCup.Documentation/CrawlAndUrlPolicy.md).

`HtmlInjectionMiddleware` dynamically imports these ES modules (an empty emitted export is allowed): `misc`, `flags`, `leagueIcons`, `pheno-age`, `bortz-age`, `badges`, `age-visualization`, `play-athlete-flow`, `proof-helpers`, `pro-discounts`, `play-menu`, `bioage-rank-preview`.

Homepage, leaderboard, and event pages start dynamic imports during parsing; `window.modulesReady` still gates dependent initialization. Homepage athlete and highlight data starts alongside the imports; leaderboard and event pages also start their athlete request early. Other pages preserve their deferred module bootstrap. Pages that only embed athlete dialogs initialize shared data lazily when opening a profile, preserving calculator data-loading contracts.

On shared leaderboard/highlight pages, the head owns `getSharedAthletes`, `getSharedEvents`, `getSharedPrizeFund`, and `fetchPublicJson`. Shared reads deduplicate requests and clear failed promises without invalidating newer refreshes. Public JSON GETs have a ten-second deadline through body consumption, abort a stalled transfer, and retry transport/JSON/server failures once. Client errors are not automatically retried. Exhausted athlete/event attempts reach each section's existing recovery controls. Keep this bootstrap inline so starting a data request does not require another script download.

Render podium athletes as soon as their data is ready. Prize totals and exchange rates load concurrently and update only the original prize panels; pending or unavailable amounts use a dash. The donation progress and podium share their total-received request. Prize failures must not remove athlete cards or their links.

Public profiles, the homepage preview, and leaderboard/league/flag routes include their existing visible content in the initial HTML. `PublicLeaderboardSnapshot` uses the shared calculators and competition ordering, preserving canonical row anchors and selecting score precision before applying the homepage limit. Tracking parameters do not disable rendering. Shared search links retain the client row-loading path because search also indexes computed badges, but their selected ranking, query, and rail title render on the server. Unknown filter counts use a dash. Keep the default podium hidden for selected/search views, including during hydration. Direct Guess My Age links start with a closable loading dialog and must not receive server-rendered profile answers.

The internal statistics page restores URL filters and its selected tab before revealing its controls. The social post manager restores saved destinations before its optional athlete directory, preserving destination edits made while that request is pending. Static documents, media/event loading placeholders, and public server-rendered content remain immediately readable.

Enhance the same dialog and table elements without clearing server content during loading or failed requests. A directly opened profile must be closable before its data request completes, and closing it must prevent delayed hydration from reopening it. Profile retries preserve the calling page's podium and athlete limit; zero-row loading is reserved for pages that only embed the dialog. Keep server/client rank and metric parity covered by browser tests with JavaScript disabled and enabled; do not introduce a separate crawler-only layout.

Reconcile leaderboard rows after `pageshow`/`popstate` native form restoration without rewriting the URL. Re-render only when the restored selection differs from the rendered one, preserving row identity and return focus otherwise. Restoring a ranking selection must not depend on a later prize response.

HTML rendering reads only the page's referenced partials and required nested dialog fragments. `HtmlAssetPlaceholders` resolves asset tokens once after page assembly, reusing each URL's version within that response. Keep asset mappings there and resolve versions again for each response so file edits remain visible.

Shared homepage, leaderboard, profile, highlights, header/footer, and progress code is loaded through versioned scripts and stylesheets in its original document position. The extracted classic scripts deliberately remain parser-blocking: later inline handlers and scripts may depend on their globals. Small head bootstrap scripts still start data and module requests early. Capture versioned asset configuration from the script's `data-*` attributes during evaluation; `document.currentScript` is unavailable in later callbacks.

Shared CSS lives in `wwwroot/css`. The .NET page generator also builds `css/athlete-dialog` variants from leaderboard, Guess My Age, and age-visualization CSS, using the existing `@scope (#athleteDialogRuntime)` boundary. These generated files are ignored and recreated during normal and Node-free builds. Keep the source of each rule in the shared stylesheet. Header font-face rules remain inline so font URLs retain content versions matching their preloads. See [Page weight](../../LongevityWorldCup.Documentation/PageWeight.md).

Keep these classic scripts free of imports/exports: `initial-view`, `flow-action-dock`, `bioage-flow`, `field-validation`, `custom-event-markup`, `longevitymaxxing`, `site-statistics-tracking`, `site-statistics`.

Application, profile, calculator, and internal designer pages load the versioned `field-validation` classic script before binding editable fields. `LwcFieldValidation` separates silent readiness checks from visible feedback: only an edited field's blur or an explicit validation action may show its error. New edits clear obsolete feedback. `refresh()` rechecks late constraints only after an edited field has been blurred, keeping untouched, focused, restored, and hidden fields quiet. Pending blur feedback waits for pointer gestures to finish and is canceled by refocusing, restoring a value, or leaving the step. Use `validity.valid` for silent native checks; `checkValidity()` dispatches `invalid` events and can trigger visible feedback.

The head partial defines `navigateToFlowDestination` synchronously so inline Back handlers work before the asynchronous modules finish. Application Next starts disabled until initialization binds stage validation.

Shared type-only contracts belong in `types/*.d.ts`. Runtime entry points stay self-contained to preserve request order, cache coverage, and independent failure. Ranking fallbacks and athlete-picture transitions have distinct failure, privacy, and timing behavior; consolidation requires equivalence and browser coverage.

## Inline Scripts

Keep request-specific JSON, early data/module bootstraps, and other timing-sensitive small scripts inline. Extract shared application code only when its configuration, document position, global scope, and embedded consumers are migrated together with browser coverage.

The Markdown page generator owns scripts in generated About, History, and Ruleset pages; edit the generator rather than generated output. The head partial's JSON-LD is structured data, not application JavaScript. Full leaderboard pages keep their ItemList synchronized with the displayed selection; see [public page structured data](../../LongevityWorldCup.Documentation/StructuredData.md).
