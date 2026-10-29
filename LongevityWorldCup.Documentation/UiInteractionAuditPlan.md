# Public UI interaction audit and implementation plan

Planning date: 15 October 2026 (Asia/Singapore). Implementation followed in the same worktree; see the execution record below. The audit observations are retained as the original baseline.

## Baseline and scope

Worktree: `C:\Users\user\.codex\worktrees\237c\LongevityWorldCup`.
Task branch: `codex/sitewide-ui-plan`.
Initial source audit baseline: `8f6eb6c651e962f2f0bb9aaabd6f9838ab417713`.
The branch was subsequently fast-forwarded to `d622f5bfa7e05ed070eb1d62f3c7ea0baba88999`.

Implementation reconciliation includes master through `4bbb06eaa8bd0d2ac9728c9dc6e22aa557e0dc16`, retaining result comparisons, scroll-lock width compensation, continuous sidebar panel motion, result insets, unified hover/expanded panel height, dated radar results and play-entry updates. Those master changes were merged normally and retained. The PR adds no separate sidebar redesign.

The originating chat independently fixed the screenshot-specific leaderboard sidebar clipping in `d622f5bf` (Hide clipped sidebar controls). It reported 43 passing focused motion/responsive tests. That result is a handoff report, not a test run performed by this audit. Do not duplicate or undo that fix. Preserve `8dc723d2` and `125e0f2e` motion work and `8f6eb6c6` unavailable-clock behavior. Fetch latest master again before implementation and inspect intervening changes.

The objective is reliable, restrained existing interactions, not a redesign. Preserve graphite chrome, neutral surfaces, teal accents, Roboto/Orbitron roles, names, humor, approved copy, scoring, and application requirements. No new UI explanation text is needed for the confirmed fixes. Do not edit AGENTS.md, DESIGN.md, UBIQUITOUS_LANGUAGE.md, or generated `wwwroot/js`.

Implement confirmed defects below. For conditional work, first reproduce the specified failure in a local deterministic browser fixture; only then make the smallest justified change. A comprehensive audit does not mean changing every page. Preserve good behavior and finish with an explicit result for each risk gate.

## Evidence and limits

Live public UI was inspected in the Codex browser at 1440x900, 390x844, 320x740, and 844x390. Source and existing browser-test contracts were inspected in this worktree. Screenshots and DOM snapshots are under ignored `.artifacts/ui-audit/`; `git check-ignore` confirmed that location is ignored. The source revision of each live response was not independently established, so these are live observations, not proof that every served asset equals the checkout.

Key evidence:

| Artifact | What it establishes |
| --- | --- |
| `home-desktop.png` | Initial homepage visual baseline; panels and shared controls |
| `collapsed-highlight-keyboard.png` | Visible collapsed Highlight after keyboard focus entered its hidden artwork link; the focus target was independently read from `document.activeElement` |
| `profile-focus-leak.txt`, `profile-focus-leak.png` | Direct athlete page open; Tab focuses the obscured site logo outside the dialog |
| `profile-mobile.png`, `proof-mobile.png` | Mobile athlete dialog and nested proof viewer layouts |
| `drawer-320.png` | Narrow mobile filter drawer, full-width labels/counts, reachable close/action controls |
| `join-mobile-expanded.png`, `join-landscape.png` | Expanded biomarker list and docked actions across portrait/landscape |
| `about-landscape-resize.png` | Sticky navigation remains usable in the sampled scrolled landscape state |
| `route-audit.json`, `desktop-audit.json` | Measured page widths and route-resolution observations; diagnostic data, not a complete pass/fail suite |
| `*-mobile.png`, `*-desktop.png`, selected `*-mobile.txt` | Additional public-route baseline captures |

The two supplied clipboard screenshots were inspected. They show headings and checkbox edges exposed in the narrow desktop rail, precisely the defect reserved for the originating chat.

Important audit limitations:

- No local automated suite was run in this planning phase. Existing tests were read, not assumed to pass.
- Browser CDP emulation attempts for reduced motion and enlarged text hit dispatch deadlines. No successful reduced-motion or 200% text run is claimed. These must be exercised with the repository's Playwright fixtures during implementation.
- Native browser clicks worked for disclosure, drawer, and proof-viewer checks when some Playwright locator actions timed out. Tool timeouts are not classified as website defects.
- The full event feed was too tall for one full-page screenshot; its viewport capture and DOM snapshot are available. Do not interpret that screenshot failure as page failure.
- Stateful play routes can resolve through remembered application/athlete state. `/proofs` and `/edit-profile` resolved to athlete selection without a selected athlete. A desktop `/apply` visit resolved to a payment handoff in the browser's existing state. No payment, application, email, subscription, guess, check-in, or upload was submitted. Use isolated fresh browser contexts and explicit fixture storage for reproducible flow tests; never clear a user's browser data to obtain them.
- Desktop/mobile screenshot capture is broad baseline coverage, not proof of every interaction, loading race, assistive-technology mode, or touch gesture. Physical-device touch and WebKit remain implementation verification tasks.

## Route and component inventory

Canonical page inventory comes from `LongevityWorldCup.Website/Middleware/RouteCanonicalization.cs`. Dynamic profile/league/flag routes share leaderboard rendering. Aliases should redirect to their canonical route; do not test each alias as a separate visual design.

Paths below are relative to `LongevityWorldCup.Website` unless stated otherwise.

| Route family | Main source / shared owner | Audit coverage and implementation checks |
| --- | --- | --- |
| `/`, `/contribute` | `wwwroot/index.html`, `Frontend/homepage.js`, `wwwroot/css/homepage.css` | Desktop/mobile baseline; highlights disclosure keyboard defect confirmed. Check podium hydration, merch dots, contribute anchor, copy feedback, newsletter loading/error without sending mail. Preserve layout while prize requests fail. |
| `/leaderboard`, `/league/{slug}`, `/flag/{slug}`, view/search/filter/group query variants | `Frontend/leaderboard-page.js`, `wwwroot/partials/leaderboard-content.html`, `wwwroot/css/leaderboard-content.css` | Desktop/mobile baseline; Australia and Men's dynamic routes sampled; 320px drawer open/Escape restored toggle focus. Sidebar clipping already fixed elsewhere. Check By Flag native details, long team names, table reflow, search clear/chips, empty results, reload/Back restoration, rank anchors. |
| `/athlete/{slug}` and profile entry from homepage/events/challenge | Same leaderboard files, `Frontend/age-visualization.ts`, `Frontend/guess-my-age.js`, relevant CSS | Mobile direct profile, proof viewer and normal close sequence sampled. Parent focus leak confirmed. Include chart tabs, badge popovers, share menu, Results over time disclosures, proofs expansion, nested image/PDF viewers, embedded events, profile-to-profile navigation, loading/error/Retry, and Guess My Age loading/result transitions. |
| `/events`, `event-board-embed.html` | `Frontend/events-page.js`, `wwwroot/partials/event-board-content.html`, `wwwroot/css/event-board.css` | Desktop/mobile feed, collapsed custom content, profile's event embed sampled. Check expansion/reversal, image arrival, deep event links, paging/append, scroll stability, retry, keyboard and reduced motion. Do not subscribe to push notifications on production. |
| `/play`, `/join`, `/select-athlete`, `/dashboard` | `wwwroot/play/menu.html`, `Frontend/play-menu.ts`, `Frontend/play-athlete-flow.ts`, play CSS | Start/new transition and track disclosure sampled; portrait/landscape dock visible. Check returning-athlete selection/autocomplete, no-result/loading/discount states, action order, Back and storage restoration. |
| `/pheno-age`, `/bortz-age` | `wwwroot/onboarding/pheno-age.html`, `bortz-age.html`, `Frontend/bioage-flow.ts`, `Frontend/flow-action-dock.ts`, `Frontend/field-validation.ts`, `wwwroot/css/bioageform.css` | Initial desktop/mobile forms captured. Fixture tests must cover all steps, unit menus, keyboard viewport, calculated result, draft/update return, raw values and silent validation. Do not enter real medical data. |
| `/apply`, `/review`, `/proofs`, `/edit-profile` | `wwwroot/onboarding/convergence.html`, `application-review.html`, `wwwroot/play/proof-upload.html`, `edit-profile.html`; shared initial-view, validation, proof and dock code | Entry/recovery states captured, stateful later steps not live-submitted. Test fixture-only stages, image crop/review, file cancellation/error/retry, submission-in-progress, accepted-response handoff, email and profile draft preservation. |
| `/longevitymaxxing` | `Frontend/longevitymaxxing.ts`, `wwwroot/longevitymaxxing/longevitymaxxing.html`, `wwwroot/css/longevitymaxxing.css` | Public desktop/mobile initial/loaded layout captured. Test sign-in tabs, drawer/check-in/profile dialogs, timezone suggestions, board horizontal scroll, resting rows, discussion paging/editor/replies, photo viewer and YouTube placeholders with fixtures. No live signup/check-in. |
| `/about`, `/history`, `/ruleset` | Generated `wwwroot/misc-pages/*.html`; generator owns content/script output | Desktop/mobile baseline; About scrolled landscape sampled. Check sticky contents navigation, anchor offsets, active section, long tables and inline links at 200%. Edit generator if behavior embedded in generated pages needs fixing. |
| `/media`, `/rejuvenation-olympics` | `wwwroot/misc-pages/media.html`, `rejuvenation-olympics.html`, `wwwroot/css/ro-guide.css` | Desktop/mobile baseline without measured horizontal overflow. Check media loading/failure and expandable guidance/anchors. |
| `/privacy`, `/reddit-privacy.html`, `/reddit-terms.html`, `/unsubscribe` | Corresponding root HTML | Privacy/unsubscribe mobile and desktop, Reddit documents desktop captured. Check text reflow, focus, missing/invalid unsubscribe token and navigation only. Do not execute live unsubscribe. |
| 404/502/503/504 fallback pages | `wwwroot/error/*.html`, `wwwroot/css/error-system.css` | Source inventory only. Locally render each fallback at narrow/enlarged sizes and check recovery links; do not simulate production outages. |
| Legacy physical files / experimental mortality calculator | Route catalog, legacy middleware tests, `wwwroot/onboarding/mortality-age.html` | Source inventory only; establish whether each is publicly reachable before changing it. Preserve existing redirects/experimental status. Do not invent a public mortality route or promote it. |

Shared controls across those routes: `partials/header.html`, `Frontend/site-header.js`, `Frontend/site-footer.js`, header/footer CSS; `initial-view.ts`/CSS; `main-progress-bar.js`, `sub-progress-bar.js`; `flow-action-dock.ts`/`flow-controls.css`; `field-validation.ts`; `flags.ts`; `proof-helpers.ts`; `badges.ts`; `aesthetic-system.css` and `mobile-roughness.css`.

Internal statistics, custom-event designer, Swagger/admin views, external merch/payment sites, APIs, RSS and research PDFs are not public UI redesign targets. Smoke shared behavior in their existing tests only if shared changes affect them. Do not change backend models or public ranking math for presentation fixes.

## Confirmed findings and concrete fixes

### UI-01 — P1: athlete modal does not own keyboard focus

**Confirmed live and in source.** At 390x844, navigate directly to `/athlete/benjamin-garden`, wait for the profile, press Tab. Focus moves from the document to the obscured site-logo link behind the open profile. The native accessibility tree still contains the underlying page. `profile-focus-leak.txt` records the focused logo. The modal is a div with `role="dialog" aria-modal="true"`; those attributes alone do not enforce modality.

Source: `Frontend/leaderboard-page.js`: `resetModalForLoading` (around 2538), `setupModalKeyboardNavigation` (2797), open/fetch flows (around 4622 and 5104), `closeModal` (around 2900), and direct server-rendered hydration. `wwwroot/partials/leaderboard-content.html`: `#detailsModal` around 249. The parent keyboard handler handles Home/End but no parent Tab containment. Nested viewer focus containment already exists and must remain correct.

Proposed fix:

1. Introduce one explicit parent-modal activation/deactivation path used by direct server-rendered profiles, loading/error profiles, click-opened profiles and Guess My Age. Keep the existing markup/history model unless a small local change requires otherwise; do not convert every dialog to native `<dialog>` in this PR.
2. On activation, isolate background branches with `inert`, saving their prior states. Because the dialog is nested under `#athleteDialogRuntime`, isolate siblings along the ancestor path rather than making an ancestor of the dialog inert. Never blindly mark all body children inert.
3. Put initial focus on the existing visible close button for ordinary/loading profiles, or a suitable existing game control for the active game. Use `preventScroll`; do not reset the modal scroll on hydration or steal focus after the user has started interacting. A server-rendered open dialog must be protected before optional athlete data resolves.
4. Contain Tab/Shift+Tab in the currently active layer. Exclude hidden, inert, disabled, zero-layout controls, including the duplicate sticky close button. Yield to the image viewer/share menu when active. Home/End handling must not hijack text-editing or nested viewer controls.
5. On close, cancel pending focus/motion callbacks, release exactly this modal's isolation, preserve body-scroll restoration, and restore the initiating connected control. For direct links without an opener, choose a visible logical page target; never focus a hidden modal descendant or stale removed row.
6. Preserve Guess My Age's existing unresolved-round dismissal rules and privacy/loading behavior. Its nested viewer remains independently closable.

Acceptance: direct and click-opened profiles have immediate visible focus; no background tab stops; Tab and Shift+Tab wrap at the topmost layer; loading-to-loaded and failure-to-Retry do not move focus unexpectedly; proof close returns to thumbnail; final profile close restores opener and exact page scroll; Back/Forward agrees with visible state. No page remains inert or scroll-locked after rapid close/reopen or navigation.

Tests: extend `AthleteDialogLinkBrowserTests`, `AthleteReturnNavigationBrowserTests`, `LeaderboardProofViewerBrowserTests`, `AthleteShareBrowserTests`, and relevant `GuessMyAgeBrowserTests` cases. Include an iframe boundary and profile-to-profile transition. Add a focused parent-dialog regression class if existing classes become unwieldy.

### UI-02 — P2: collapsed Highlight links remain keyboard reachable

**Confirmed live and in source.** On `/`, expand CrossFit vs HYROX, collapse it, then press Tab from its expander. `document.activeElement` becomes the clipped `.custom-event-artwork` link. Its containing `.custom-event-content` has height 0 but computed `visibility: visible`; the collapsed details remain exposed in the accessibility tree. `/events` uses the same renderer and exhibits the same exposed collapsed subtree.

Source: `Frontend/events-page.js` around 1225–1293 (`detailsTr`, `toggleOpen`, requested-event expansion); `wwwroot/css/event-board.css` around 765–791. Current visibility uses `max-height:0; opacity:0; overflow:hidden`; toggling only changes `.is-open` and button ARIA.

Proposed fix:

1. Make a single `setCustomEventExpanded` operation own visual state, `aria-expanded`, panel interaction state and requested-event expansion. The deep-link path currently duplicates open-state code; route it through the same function.
2. Collapsed panels must be inert and absent from the accessibility tree. Remove interaction immediately on closing. If focus is inside a panel being collapsed, return it to its expander before hiding the subtree.
3. Retain the existing expander and row-click semantics, labels, artwork and zebra striping. Do not hide the whole event or change external-link targets.
4. Replace the arbitrary 1400px expanded ceiling with intrinsic or measured height. This ceiling is a source-confirmed limit, but a real long-content clipping example was not reproduced during this audit. Use a local long-content fixture to confirm it, then remove the cap in this same disclosure fix. A simple inner wrapper with intrinsic expansion is preferable to a global animation framework. If measuring height, account for image load, font load, text scale, resize and interrupted transitions, then release to natural height when open.
5. Ensure rapid open/close/open ends in the final requested state. Reduced motion must switch state immediately without waiting for a transition event that will not fire. Normal motion should retain the established short timing, without an exaggerated max-height travel distance.

Acceptance: Tab skips all collapsed links; expanded content is fully readable at 320px and 200% text, including late-loading artwork; expanders retain focus; state/ARIA agree after reversals, append/refresh and requested-event navigation; no clipped bottom text, unexplained residual gap or delayed collapse. Homepage, full feed and embed share this behavior.

Tests: extend `EventBoardBrowserTests`, `HomepagePanelsBrowserTests` and `EventFeedBrowserTests` where appropriate. Use deterministic short, long, linked, artwork and no-details events. Test actual keyboard traversal, not only `aria-expanded` or final pixel height.

### UI-03 — P2: deep-link scroll requests ignore reduced motion

**Confirmed source defect; visual reduced-motion reproduction pending.** Explicit `behavior:'smooth'` calls bypass the preference used elsewhere:

- `Frontend/events-page.js`, requested-event scroll around 1292.
- `Frontend/leaderboard-page.js`, incoming rank anchor around 1746. Unlike the selected/search positioning path, this calls `ScrollGuard.trustedScrollIntoView` directly without a reduced-motion guard.
- `Frontend/main-progress-bar.js`, delayed initial content positioning around 40; this path excludes `play-flow-route`, so verify an actually eligible consumer before changing/testing it.

The delayed selected/search positioning around 1731 is **already protected** by `canAutoScroll()` (around 701), which returns false under reduced motion. Preserve that suppression; do not turn it into a new automatic jump. The ScrollGuard wrapper around 619–655 forwards explicit options and does not normalize motion preference. CSS animation overrides alone do not fix explicit JavaScript smooth-scroll requests.

Proposed fix: choose instant/auto versus smooth at the call site using the current media preference. Preserve block alignment, sticky offsets, URL/hash, and the existing guard against late automatic scrolling after user interaction. Use a tiny local helper only where it avoids repetition without introducing a new import/load dependency. Do not globally monkey-patch scrolling or remove intentional normal-motion navigation.

Acceptance: reduced-motion direct rank/event links land at the correct destination without animated travel; selected/search views retain their existing suppression of delayed automatic scrolling; ordinary motion remains restrained; user scrolling/typing before delayed completion is never overridden; changing preference before a subsequent action is honored. Test scroll intention and resulting position, not an arbitrary sleep.

Tests: `LeaderboardSelectionBrowserTests`, `LeaderboardRouteBrowserTests`, `EventBoardBrowserTests`, `FlowProgressBrowserTests` for a verified active progress consumer.

## Conditional risk gates (not yet confirmed defects)

These are bounded reproduction tasks, not permission to refactor all listed files. Record pass or a reproducible failure. If passing, preserve implementation.

| ID / priority | Evidence and precise reproduction gate | Proposed change only if failing |
| --- | --- | --- |
| R1 / P2: resized sticky header state | `Frontend/site-header.js` runs `handleScroll` initially and on scroll; its decisions depend on header height, CSS visibility and compact-landscape media query. Resize at a scroll position between old/new header thresholds, without another scroll; rotate portrait/landscape and change root text size. The sampled scrolled About rotation worked, so no blanket defect claim. | Coalesce resize/media/font/layout updates through the existing animation-frame scheduler, observing only relevant dimensions. Preserve threshold behavior, avoid ResizeObserver loops and repeated style writes. Extend `HomepageChromeRegressionBrowserTests.Header`/`ResponsiveAction` and `FooterResponsiveBrowserTests`. |
| R2 / P2: nested viewer close/reopen races | Athlete `closeEnlargedView` and challenge `closeNotePhotoViewer` release parent interaction before a 220ms hidden cleanup. Existing open paths cancel timers; normal proof close passed. Reopen within 0/50/150ms, press Escape twice, navigate Back/Forward, rotate while closing, and switch reduced motion mid-transition. | Make cleanup generation-owned/cancelable and align inert/pointer state with the active layer. Prefer local lifecycle helpers over merging unrelated viewer implementations. Never let a stale timer hide a new viewer or release another overlay's lock. Extend proof-viewer and `LongevitymaxxingChallengeBrowserTests.DiscussionPhotos` cases. |
| R3 / P2: alert/loading overlap | `Frontend/site-header.js` independently adds/removes `body.no-scroll` for custom alerts and loading; focus restoration uses the previous element. Reproduce a real fixture error while another supported dialog owns scroll lock. | Track ownership of locks/isolation, restore only connected visible focus, and sequence loading-to-alert handoff. Do not introduce a sitewide modal manager unless at least two real overlapping consumers require it. Extend `ApplicationErrorBrowserTests`, `FlowAccessibilityBrowserTests`. |
| R4 / P2: loading/return focus and dock jumps | Existing README contracts and `initial-view.ts`, `flow-action-dock.ts` already handle many races. Delay athlete/prize/discount/flag requests; Back during loading; reload stored raw draft; show keyboard then dock actions; fail data and Retry. | Fix the specific missing readiness/state ownership in its existing helper. Do not gate useful content on optional requests, clear drafts, animate hidden pending views, or reintroduce premature validation. Use existing InitialView, BioageUpdateDraft, FlowActionDock, FlowNavigationReadiness, VisualViewportFlow and FormValidation browser suites. |
| R5 / P2: discussion/board continuity | Challenge board auto-scroll has repeated delayed attempts but checks manual scroll. Test manual horizontal scroll during font/data arrival, discussion paging while editing, open photo then parent refresh, mentions/timezone picker at viewport edges. | Preserve user scroll/draft/focus and cancel only obsolete work. Use `LongevitymaxxingChallengeBrowserTests` continuity, loading, discussion and viewport partials; avoid changing scoring or APIs. |
| R6 / P2: badges, chart tabs and result rows | Badge wrappers contain actionable links; Results over time controls dynamically expand tables; share menu has its own focus handling. Test Tab/Enter/Space/Escape, nested link activation once, long labels at 200%, focused result collapse, switched clocks and pointer/touch popovers. | Separate competing action handlers or correct ownership/focus only where reproduced. Preserve badge names, chart data, row identities, expansion state and compact visual treatment. Extend BiomarkerHistory, AthleteActionsResponsive, AthleteStatsResponsive and AthleteShare suites. |
| R7 / P2: By Flag/table hydration | Native team `<details>` and responsive row reconciliation coexist with search/ranking changes. Expand several teams, switch clock, clear filter, Back/Forward, resize at 640/768/1024 boundaries and hydrate slow server-rendered content. | Reconcile keyed rows/selection without removing focused nodes or carrying incompatible clock ranks. Extend LeaderboardRowReflow, LeaderboardSelection and PublicPageRendering suites. |
| R8 / P3: long static content and fallback states | Baseline info pages fit at 390px; full enlarged-text/keyboard/recovery checks remain. Check 320px, 200% text, landscape, anchor destinations and footer links; locally render error pages and invalid-token unsubscribe. | Narrow wrapping/spacing/scroll-margin changes in the owning stylesheet/generator. Do not hide content or globally apply overflow:hidden to mask overflow. |

## Architecture and sequencing

1. **Baseline:** fetch/reconcile latest master normally on this branch; retain `d622f5bf`. Check working tree for concurrent edits. Establish a local `BrowserTestApp` fixture with isolated contexts. Reproduce UI-01 and UI-02 before modifying source; retain failing behavior evidence.
2. **Parent dialog ownership:** implement UI-01 with focused tests. Keep the existing scroll/history implementation and nested viewer boundaries. This is the highest-impact fix and informs R2/R3 testing.
3. **Disclosure state:** implement UI-02 in the event renderer/CSS, including a long-content fixture. This is independent of the sidebar rail; do not reuse rail-width timing for event heights.
4. **Motion preference:** implement UI-03 after validating actual active call paths. No broad transition-duration normalization.
5. **Run risk gates:** R1–R8 in related batches. Add fixes only for confirmed failures. Each added finding needs steps, expected/actual behavior, exact owner and a focused regression. Update this plan's status table or final PR description with outcomes.
6. **Integration:** run focused affected tests, frontend checks/build and the normal CI-required checks. Review rendered desktop/mobile before/after evidence, then commit/push/open PR as below.

Useful shared abstraction: a small parent-modal isolation/focus lifecycle inside the existing classic script, reused by that script's entry paths. A cross-file helper is justified only if real consumers share the same contract and the loader/asset mapping is updated with tests. Keep classic scripts classic; no imports/exports or timing changes. Avoid bundling, replacing CSS architecture, broad variable renames, moving all inline scripts, or creating a new component framework.

## Verification matrix and acceptance

Use existing Playwright fixtures and deterministic local data. Production observations are for visual comparison; mutation/error/latency scenarios belong locally.

| Axis | Minimum representative cases |
| --- | --- |
| Desktop | 1440x900; 1024px transition boundary where relevant; mouse hover plus keyboard |
| Mobile | 390x844 and 320x740; touch-enabled context, tap outside, nested scrolling, no hover dependency |
| Landscape | 844x390; rotate with open drawer/dialog and expanded content; reachable close/actions and no trapped scroll |
| Text | 100% and 200% root text; separately inspect browser zoom/reflow where fixture supports it; preserve whole labels and controls |
| Motion | `ReducedMotion.Reduce` and normal; sample intermediate frames, not only settled screenshots; rapid reversals at 0/50/150ms |
| Theme | Light and dark for changed surfaces; existing contrast checks where tokens/focus rings change |
| Keyboard | Tab/Shift+Tab, Enter/Space, Escape, Home/End where supported; focus remains visible and in active layer; no hidden tab stops |
| Lifecycle | Cold/direct URL, hydrated server HTML, click-open, Back/Forward, refresh, missing/stale data, retry, close before response, reopening during cleanup |
| Data | Long names/content, empty result, single item, large proof list, late/failed artwork, multiple expanded teams/results |
| Browser | Chromium focused tests and WebKit for fixed-position/scroll-lock/viewport changes; real touch spot check if device available |

Global acceptance:

- Visible state, focusability, pointer interaction and ARIA agree during transitions and after interruption.
- No accidental clipped controls, page-wide horizontal overflow, overlay leaks, unexplained layout jumps, stale timer mutations or focus resets.
- Open content grows naturally when images/fonts/text size change. Deliberate horizontal data scrollers remain usable; do not confuse them with page overflow.
- A closing overlay cannot mutate a newly opened overlay. The topmost layer handles dismiss/focus; underlying layers preserve state.
- Scroll restoration and browser history preserve selection and context. Late hydration cannot reopen a deliberately closed profile.
- Reduced motion is immediate and fully functional; no transitionend-only deadlocks or forced smooth travel.
- Existing copy, routes, scoring, drafts, form validation timing and no-JavaScript public rendering remain intact.

Proportionate command sequence (use repository-pinned Node/runtime): from `LongevityWorldCup.Website`, `npm ci` if dependencies are absent, `npm run check`, `npm run build`; then focused `dotnet test` filters for changed behavior. Use normal build/test fixture setup, not a hand-maintained static-site clone. Run broader required CI once the focused cases pass; do not repeatedly run the entire suite after unchanged evidence. Check `git diff --check` and ensure generated JS remains untracked.

Tests must assert user-visible behavior and state ownership: actual tab traversal, latest interaction wins, exact focus/scroll return, content not clipped, final URL/selection. Do not add brittle source-string tests as a substitute, arbitrary long sleeps, or a vast viewport Cartesian product. Keep screenshots/traces in `.artifacts` and capture key intermediate animation frames for review.

## Publication and handoff

This planning phase intentionally stops after saving this document in the selected worktree. Implementation, tests, commit, remote publication and PR belong to the requested Sol phase. Do not merge or deploy.

Sol completion checklist:

1. Read this plan and the current AGENTS/design/glossary/frontend instructions; fetch latest master, reconcile without losing concurrent edits, and retain the dedicated `codex/` branch workflow.
2. Implement UI-01–03 and only reproduced conditional findings. Record any disproven or already-fixed item explicitly rather than forcing a change.
3. Verify source, behavior, focused tests, responsive screenshots and required CI. Keep source changes narrow and check both server-rendered and hydrated profile paths.
4. Commit only intended files/hunks with a short message. Include this plan if useful to the PR; no ignored audit dumps, generated assets, user session/payment details or unrelated files.
5. Push the task branch normally; verify remote SHA. Open a PR targeting `master`, verify head/base/URL, and attach it to this chat. Describe the concrete final behavior and validation. Use a closes directive only for an issue actually fully resolved; mention related issues only after verifying their identity. No issue number was assigned by this audit.
6. Follow CI and review to completion, resolve actionable findings, and verify the updated remote branch. Do not force-push, merge, deploy, or push directly to master without explicit instruction.
7. Final report: PR link, commit, confirmed fixes, tests/CI outcome, visual evidence and any remaining real limitation. Do not claim production is live from a PR or local screenshot.

## Implementation and gate outcomes

The implementation keeps the existing visual design and copy. Ordinary and server-rendered athlete dialogs now isolate background branches, establish initial focus, contain keyboard navigation through their same-origin event frame, preserve the opener through browser Forward, and restore focus/scroll when closed. Empty same-origin frames are excluded from the focus loop, and wrapping focus reveals its target in the scroll area. Nested image viewers become inert as soon as closing starts and reactivate when reopened, in both profiles and the challenge.

Highlight details now use intrinsic grid expansion instead of the 1400px ceiling. One state setter controls the expander, accessibility state, interaction and deep-link opening. Closed details cannot receive focus. Event and rank deep links honor reduced motion. The delayed selected/search positioning retains its existing reduced-motion suppression. All current main-progress consumers are `play-flow-route`, so its excluded legacy initial scroll path was left unchanged.

| Gate | Outcome / evidence |
| --- | --- |
| R1 — resized header | Passed resize, root text-size and landscape checks in `UiInteractionBrowserTests.StickyHeader_RecalculatesAfterResizeAndTextChangesWithoutScrolling`; no header rewrite. |
| R2 — viewer reversal | 0/50/150ms reopen checks passed; existing proof history/navigation tests passed. Closing viewers previously remained focusable during their 220ms fade; both viewer owners now set `inert` on close and clear it on open. Further regressions reproduced overlapping parent close callbacks and interrupted close/reopen losing the original opener; dismissal is now idempotent while fading, and the opener is retained until closing actually completes. Profile and challenge checks assert these lifecycles. |
| R3 — alert/loading ownership | Supported loading-to-alert and crop/submission handoffs already close the prior owner first. The loading-to-alert keyboard/focus/scroll regression and application error scenarios passed; no global modal-manager replacement. |
| R4 — readiness and docking | Saved application restoration, failed profile recovery, delayed direct-game close, result resize/scroll and visual viewport checks exercised. The viewport probe originally ran as soon as the controller existed, before the source task was ready; it now waits for task readiness. No product dock change was needed. |
| R5 — challenge continuity | Manual board/history scrolling, discussion draft paging, background reordering, pending reply preservation and the reply/photo viewer flow exercised with local fixtures. Existing draft and scroll guards retained. |
| R6 — profile controls | Existing share pending/retry/viewport and biomarker history/clock/tooltip tests exercised. Parent isolation yields to nested share/viewer owners. No badge copy or chart-data changes. |
| R7 — grouped rankings | Flag search, cohort changes, hydration expansion/focus, grouped clock/reload and failed hydration tests exercised. A five-second initial load assertion failed once under host load and passed in isolation; no flag logic change justified. |
| R8 — static and recovery content | Eleven document/recovery routes passed at 320px and 200% text with keyboard entry. Existing document contents navigation checks also exercised. Visual inspection of the event disclosure capture revealed that the Highlights heading split its single word at 320px/200%; its narrow-screen type scale now keeps that heading whole, with a rendered text-range regression. |

Verification records and screenshots are ignored under `.artifacts/ui-implementation`, `.artifacts/ui-tests`, `.artifacts/ui-gates`, and subsequent focused-run directories. Strict frontend checking and asset build passed. The first related-behavior batch passed 127/130; its remaining findings were the opener restoration regression (fixed), task-readiness probe (corrected), and the load assertion noted above. A subsequent batch exposed a test's premature zero-height animation settle check; it was corrected to require full intrinsic content height. The focused batch passed all 28 cases, and 72 profile/biomarker/disclosure/history checks passed after integrating master commit `700c537f`. Final upstream reconciliation and CI results are recorded with the PR.

A separate local WebKit probe verified desktop/mobile initial focus, reverse/forward keyboard movement and direct-close focus restoration, with screenshots `webkit-1440.png` and `webkit-390.png`. Its copied application harness omitted the IndexNow manifest, causing background freshness diagnostics; those diagnostics are a probe-packaging limitation, not a production UI failure. Physical-device touch testing was not performed. A local browser process also encountered `ERR_NETWORK_IO_SUSPENDED` during host suspension; affected navigation results were discarded and rerun in a fresh process.

A final visibility check found reverse Tab could focus an empty pending frame off screen. All four Chromium regressions failed at both widths for loaded and delayed frames: containment alone did not guarantee visible focus. The focus loop now skips empty accessible frame documents and allows the browser to reveal the destination when wrapping; all four regressions passed after correction. Screenshots capture initial entry before traversing the long profile. Final related checks and CI results are recorded on the PR.

The overlapping-close regression failed before the idempotent dismissal guard and passed afterward with 38 related history, reversal and motion checks. After the sidebar motion merge, all 72 combined dialog/disclosure/responsive checks passed. Final diff review added a guard for deep links to short announcements without details; all 16 event/motion checks passed with both simple and expandable announcements. A later integration batch passed 112/113 cases; its immediate row-color assertion failed once and all four viewport variants passed in isolation. That assertion now explicitly checks retained focus and retries the browser's CSS state; all four variants passed again. Final combined verification and CI are tracked on [PR #974](https://github.com/nopara73/LongevityWorldCup/pull/974). Bugbot could not review because its account usage limit was reached; this is distinct from a passing review.
