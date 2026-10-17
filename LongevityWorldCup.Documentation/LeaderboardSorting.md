# Leaderboard sorting implementation specification

Task: [#153 — Implement sorting the leaderboard](https://github.com/nopara73/LongevityWorldCup/issues/153). Related: [#133](https://github.com/nopara73/LongevityWorldCup/issues/133), whose relative/tiered leaderboard scope remains open. Implement the approved compact composer, incorporating the later icon-only toolbar and clickable-column refinements. This is presentation ordering, not a change to competition rules, awards, eligibility, ranks, or stored results.

## Interface and interactions

- Toolbar order: athlete search, **icon-only sorting button**, existing tools/filter button. An adjacent 44 px Clear ordering button appears only while custom ordering is active; one click restores official ranking, removes both custom levels and their columns, and retains the search and selected league. Return focus to the sorting button after clearing. Keep the actions grouped when a narrow toolbar wraps. Match existing icon size, tokens, focus and active treatment. Accessible name and tooltip: “Order athletes”. No visible “Order” caption. Closed initially; visibly active when custom ordering is selected.
- Open a compact anchored popover containing two rows: **Order by** select + direction arrow + quick-clear icon, **Then by** select + direction arrow + quick-clear icon. Native grouped selects are sufficient. Apply each change immediately. An icon-only swap button exchanges the two fields together with their directions, remaining disabled until both fields are selected. Keep it beside the rows, moving below them when width or enlarged text requires more room. No Apply, summary bar, or priority badges. Clamp popover to viewport; on narrow screens fit within the toolbar container without fixed top offsets.
- Clearing Then by removes that level and its data column immediately. The top-row clear restores official ranking and clears both fields in one click. Keep the picker open and move focus to the corresponding select after clearing. Empty levels have disabled clear controls. The existing toolbar clear also resets both levels in one click.
- Default: Order by = **Official rank**, Then by = **None**. Official rank means the selected league’s exact existing order, including its official tie-breakers. It is a reset option, not a reversible custom numeric field: selecting it or activating the existing Rank header clears both custom levels. Disable direction and secondary controls in this mode. No extra data columns in default mode.
- Choosing a custom primary enables Then by. None clears the secondary. Keep choices in stable groups; disable the primary field in Then by. Allow a secondary field to be chosen as primary and clear its duplicate secondary automatically. Choosing a different primary retains a distinct secondary and uses the new field’s default direction.
- With two custom fields selected, display both selected values in two metric columns, in primary then secondary order. With None, display only the primary custom value. Preserve rank and athlete identity. On constrained boards, hide the fixed competition score when it is only context, giving the selected values room to sit beside the athlete. Always retain that score when selected as an ordering field. Use a compact shared header/row grid and stack values only on phones or with enlarged text. Preserve sponsor/contact/badge functionality at the existing supported widths; do not reproduce the mock’s replacement masthead, sidebar, sample athletes, or explanatory status strip.
- Reuse an existing column if its semantic field is selected (Athlete name or active competition metric), instead of adding a duplicate value column. Official rank stays the original rank column. The approved two-column concept applies to the two selected custom fields, with this deduplication for existing fields.
- Each selected field’s header is a button: it reverses **its own level’s** direction, retaining primary/secondary roles. Clicking the secondary never silently promotes it. The original Athlete header selects Athlete name as primary, and the competition metric header selects the current metric as primary; if already selected at either level, reverse that level. Selecting an unselected static field retains a distinct secondary. Rank always resets.
- Use a small vector direction arrow beside each selected field's name, with its unit directly below. On extremely narrow boards or with enlarged text, give the arrow its own line so the label has room to wrap. Keep numeric headings and values aligned at their right edges and do not let a longer secondary label shift the primary label down. Header and composer always agree; no visible priority badges. For the active metric, retain existing “Age reduction”, “Improvement”, or “Bortz improvement” copy. Preserve its rules link as a separate accessible link/icon next to the heading; do not nest a link inside the sort button.
- Data-column labels include units unobtrusively. Show absent values as an em dash with accessible “Not available”. Sorting changes no athlete names, existing product copy, medals, or rank anchors.

## Supported fields and exact meanings

Use stable URL keys and grouped options. These are public data already available or derivable from the public athlete snapshot. Default asc means numeric low-to-high or deterministic text A–Z; desc means high-to-low or newest first. Compare unrounded values, then format.

| Key | Label | Source/meaning | Default |
| --- | --- | --- | --- |
| age | Chronological age | Age at a common UTC snapshot date, using the existing age calculation | asc |
| bortz | Bortz Age | Lowest eligible Bortz age, as displayed on the profile; not latest | asc |
| pheno | Pheno Age | Lowest eligible Pheno age, as displayed on the profile; not latest | asc |
| crowd | Crowd Age | Active-image crowd estimate, finite and Crowd Count > 0 | asc |
| bortzReduction | Bortz age reduction | Existing Bortz age minus chronological age at that winning result | asc |
| phenoReduction | Pheno age reduction | Existing Pheno age minus chronological age at that winning result | asc |
| crowdReduction | Crowd age reduction | Active-image crowd age minus chronological age at the common UTC snapshot date; count > 0 | asc |
| pace | Bortz pace of aging | Existing BortzPaceOfAging / lowestBortzAge divided by chronoAtLowestBortzAge; use current public definition, not DunedinPACE | asc |
| phenoPace | Pheno pace of aging | Existing PhenoPaceOfAging / lowestPhenoAge divided by chronoAtLowestPhenoAge; missing without an eligible result | asc |
| phenoImprovement | Pheno improvement | Existing latest-minus-worst eligible result, requiring at least two eligible results | asc |
| bortzImprovement | Bortz improvement | Existing latest-minus-worst eligible result, requiring at least two eligible results | asc |
| phenoPercent | Pheno age reduction (%) | Existing (1 − lowestPhenoAge / chronoAtLowestPhenoAge) × 100 | desc |
| bortzPercent | Bortz age reduction (%) | Existing (1 − lowestBortzAge / chronoAtLowestBortzAge) × 100; missing without an eligible result | desc |
| lastTest | Latest test date | Maximum valid explicit date in public Biomarkers; date-only UTC | desc |
| firstTest | First test date | Minimum valid explicit date in public Biomarkers; date-only UTC | desc |
| tests | Tests submitted | Number of public Biomarkers result entries, counting each entry once; not a sum of Pheno and Bortz eligibility counts | desc |
| crowdCount | Crowd Count | Current image’s accepted guess count; zero is valid | desc |
| name | Athlete name | Public display name, using the existing public-name selection | asc |
| flag | Flag | Canonical public flag name | asc |
| track | Track | Derived Pro/Amateur; asc = Amateur then Pro, desc = Pro then Amateur | asc |
| division | Division | Public division label | asc |
| badges | Badges earned | Count actual public award records in Badges, excluding visual utility/link/overflow badges | desc |
| albumin | Albumin | Latest dated finite AlbGL; g/L, original value without scoring cap | asc |
| alp | ALP | Latest dated finite AlpUL; U/L | asc |
| alt | ALT | Latest dated finite AltUL; U/L | asc |
| apoa1 | ApoA1 | Latest dated finite ApoA1GL; g/L | asc |
| crp | CRP | Latest dated finite CrpMgL; mg/L | asc |
| creatinine | Creatinine | Latest dated finite CreatUmolL; µmol/L | asc |
| cystatinC | Cystatin C | Latest dated finite CystatinCMgL; mg/L | asc |
| ggt | GGT | Latest dated finite GgtUL; U/L | asc |
| glucose | Glucose | Latest dated finite GluMmolL; mmol/L | asc |
| hba1c | HbA1c | Latest dated finite Hba1cMmolMol; mmol/mol | asc |
| lymphocytes | Lymphocytes | Latest dated finite LymPc; % | asc |
| mch | MCH | Latest dated finite MchPg; pg | asc |
| mcv | MCV | Latest dated finite McvFL; fL | asc |
| monocytes | Monocytes | Latest dated finite MonocytePc; stored %, not a derived absolute count | asc |
| neutrophils | Neutrophils | Latest dated finite NeutrophilPc; stored %, not a derived absolute count | asc |
| rbc | RBC | Latest dated finite Rbc10e12L; 10¹²/L | asc |
| rdw | RDW | Latest dated finite RdwPc; % | asc |
| shbg | SHBG | Latest dated finite ShbgNmolL; nmol/L | asc |
| cholesterol | Total cholesterol | Latest dated finite CholesterolMmolL; mmol/L | asc |
| urea | Urea | Latest dated finite UreaMmolL; mmol/L | asc |
| vitd | Vitamin D | Latest dated finite VitaminDNmolL; nmol/L | asc |
| wbc | WBC | Latest dated finite Wbc1000cellsuL; 10³ cells/µL | asc |
| metric | Existing active metric label | The selected ranking view’s existing metric; includes effective reduction in Ultimate | asc |

Include every supported public blood marker, not a sample subset. The shared catalog carries each marker's source property so server and browser extraction use the same mapping. A coverage test compares the catalog with every numeric property in the public BiomarkerData model. Official rank is the reset mode; None is secondary-only.

Group options as Ages, Age reduction, Progress, Activity, Profile, Biomarkers; place Official rank first. Do not offer generic “Biological age”: the three clocks are distinct. Do not invent BMI, location, sex, join date, first announcement date, or missing lab data. Division is the existing public field.

The calculator’s synthetic best-marker arrays are not latest lab panels. For each laboratory field, select the latest dated public entry containing a finite raw value; if dates tie, use the later source-array entry consistently in C# and JS. Missing a marker in the newest panel does not erase its prior known result. A tooltip/accessibility description identifies its actual test date. Do not substitute today for an absent/invalid date. First/latest test dates reflect measurement dates, not publication dates. Biomarker low/high ordering is numeric, not a health verdict.

Business/AthleteDataService.cs HydrateBadgesIntoAthletesJson populates public Badges from BadgeAwards; count that list, never DOM elements or generated “Contact”/“Personal link” decorations. Do not infer unknown ages from neutral calculator fallbacks: a missing eligible Pheno/Bortz result remains missing for its custom age, reduction, percentage, and pace fields. No API exposure of private fields is needed.

## Ordering contract

1. Produce the existing eligible athletes and official order for the selected clock.
2. Apply league/cohort filters exactly as today. Assign the existing selected-view rank, medal styling, canonical row anchor, and competition metric precision before search, custom ordering, or a display limit. Current filtered leagues legitimately have cohort ranks; do not change that behavior to global ranks.
3. Apply existing search unchanged, including computed badge text and rank/metric searching.
4. Sort a copy of the matching entries by primary, then secondary if present, then original official order, then stable athlete slug. Never mutate the canonical athleteResults array or sidebar-preview ranking arrays.
5. Apply the page’s display limit only after ordering.

Null, undefined, empty text, invalid date, NaN and infinities are missing. Missing sorts last in both directions; compare presence before applying direction. If both primary values are missing/equal, compare the secondary normally. Genuine numeric zero is present. Compare full precision; equal displayed numbers are not necessarily ties. Final official-order/slug fallback makes repeated renders stable.

Text comparison must be specified identically on server/client: normalized lowercase invariant keys compared by ordinal UTF-16 order, followed by the common official fallback. Do not pair browser default localeCompare with C# culture-dependent sorting. Date comparisons use date-only keys, independent of browser timezone. Reuse a common UTC as-of date for newly introduced age fields so initial HTML and hydration agree.

Official Ultimate order remains Pro before Amateur; custom ordering may interleave tracks. Suppress track separators for every custom order, including one that happens to group tracks. Reset restores the usual separator. Preserve track indicators and original medal identities; custom first row does not become competition winner.

## Routes, clocks, homepage and By Flag

- Add a validated presentation state, e.g. sort=pheno&dir=asc&then=age&thenDir=desc. Omit all four parameters for official mode. Omit secondary parameters for None. Unknown primary resets to official; unknown secondary becomes None; duplicate secondary is removed; invalid directions use the field default. First occurrence wins for repeated keys, consistently with current query parsing.
- Preserve existing view/filter/group/search parameters, canonical league/flag paths and athlete hashes. Include sort state in getSelectedLeaderboardState, URL builders, share/View all links, rendered-state signatures, popstate/pageshow restoration, and reset behavior.
- Explicit field/direction/header changes create one history entry each; opening/closing the panel creates none. Restore from URL on Back/Forward/reload without pushing another entry. Use the current search/filter history policy, preserving sort state. No local-storage preference that overrides a shared URL.
- Switching between Ultimate, Bortz, Pheno, Crowd and both improvement leagues keeps valid custom fields and directions. Eligibility, original ranks and the active metric are recalculated for the new view; a Bortz metric is missing for an otherwise eligible Amateur, never replaced with Pheno.
- Crowd ordering eligibility remains 100 accepted guesses for the Crowd league. Other leagues may show a finite current-image Crowd Age with fewer than 100 guesses, as profiles do; sorting by it does not grant Crowd league eligibility.
- Homepage preview: default behavior remains the existing official podium and limited table. Custom ordering is applied before its limit; hide podium and include ranks 1–3 in the sorted table, so no matching top-ranked athlete disappears. View all carries all selection/sort state. Reset restores normal podium/table composition.
- By Flag is a team competition, not an average-age view. Preserve its official team ordering and member order. Hide the individual sort button/metric columns in By Flag; retain validated individual sort parameters as dormant URL state so switching back restores them. Never apply individual numeric fields to teams or silently invent aggregates. A canonical /flag/... individual-athlete league does support sorting. Check the target branch’s current FlagTeam snapshot/renderer paths in addition to the individual path.

## Rendering and accessibility

Server HTML must show the requested custom order and values before JavaScript, with exactly the same ranks, metric labels, precision, missing values and column count as hydration. Extend the public snapshot/renderer contract rather than maintaining unrelated ranking math in a second renderer. Keep public privacy projection intact. For search URLs, retain the established client-row-loading exception because search indexes computed badges; server-render the requested composer/header state and suppress the default podium.

Render the correct initial composer selection, active icon state and custom header buttons. Hydrate the same table and row anchors without clearing readable SSR rows on delayed/failed fetches. Sorting must not depend on prize requests. Preserve focused controls, expanded athlete dialogs, return focus, and unaffected row identity. Keep ItemList ordering aligned to displayed order; its list positions follow display position while visible competition ranks remain unchanged. Preserve existing canonical/noindex policy; custom sorts do not create new leagues or sitemap entries.

Use native buttons within th elements. Set aria-sort only on the primary active column; announce secondary field, direction and role in its accessible name/description. Direction buttons announce field, current direction and next action (“Pheno Age: youngest first; switch to oldest first”), not only arrow shape. Native selects keep their ordinary arrow-key behavior. Announce completed sorting in one polite status region, without moving focus.

The popover is a nonmodal labelled group with aria-expanded/aria-controls on its trigger. Escape closes it and returns focus to the trigger. The first outside pointer gesture only dismisses the popover and returns focus to its trigger; consume that gesture so it cannot open a profile, navigate a link or change a table header underneath. The next deliberate action works normally. Transient focus loss while opening a native select must not dismiss it. Closed controls are hidden/inert and absent from tab order. Tab can leave the group without trapping focus or suppressing keyboard activation at its destination. Do not re-create focused selects/buttons when updating rows. Keep the panel within the usable viewport without animating positioning corrections.

At 320–390 px, preserve rank/name and the chosen ordering values. Hide the unselected fixed score when it would crowd those values; retain it if explicitly selected. Keep a single selected value beside the athlete and place two values in a row below the name when needed. Reflow against component width and text size, including enlarged desktop text, and stack values individually at very narrow effective widths. Avoid clipping headings inside an otherwise non-overflowing board; wrap long labels/names. Keep short names readable and athlete-change markers away from rank medals. Keep existing sponsorship/contact access. Verify touch targets, 200% text zoom, visible focus and reduced motion. Do not animate all rows on every direction change; respect existing reveal/focus contracts.

Ordering changes reuse the current matching entries while the search, cohort and athlete data are unchanged. Keep row/name/rank nodes and sidebar counts intact, update visibility only for changed membership, and move only rows whose position changes. Recompute the matching entries when a pending search or filter differs. Batch table writes before measuring the open popover; do not force layout halfway through a clear or reorder.

## Implementation map and validation

Verified source touchpoints in the task worktree at 3d0b1f86 (line regions are before implementation):
- Business/PublicLeaderboardSnapshot.cs: PublicAthlete, PublicLeaderboardSelection, Select and Order.
- Business/LeaderboardSnapshot.cs: shared snapshot row/model and builder.
- Tools/PhenoStatsCalculator.cs: eligible age results, dated source processing, pace, improvements and raw marker keys.
- Tools/LeaderboardHtmlRenderer.cs: RenderRows/AppendRow and separators.
- Middleware/HtmlInjectionMiddleware.cs: ApplyPublicContent, initial selected controls, podium and row injection.
- Frontend/leaderboard-page.js: athleteResults mapping (~1148–1553), getSelectedLeaderboardState (~3543), navigation/state signatures (~3572–3627), performFilter/getMatchingEntries (~3806 onward), individual matchingEntries/limit (~4008 onward), and finishLeaderboardFilter (~4190 onward). Apply custom sorting only after the By Flag branch returns; do not alter getMatchingEntries used by team building and sidebar counts.
- Business/FlagLeaderboard.cs, Tools/FlagLeaderboardHtmlRenderer.cs, PublicLeaderboardSelection.ByFlag/FlagTeams, and frontend buildFlagTeams/renderFlagTeams (~3696/~3732) own the team path. Middleware ApplyPublicContent branches for flags (~451) before individual SSR rows (~467). These need dormant-state/visibility compatibility, not new team metrics.
- wwwroot/partials/leaderboard-content.html and wwwroot/css/leaderboard-content.css: toolbar, table and responsive styling. Use existing Font Awesome icons/tokens.
- Existing focused suites: PublicLeaderboardSnapshotTests, LeaderboardSnapshotBuilderTests, LeaderboardHtmlRendererTests, LeaderboardSelectionBrowserTests, LeaderboardRouteBrowserTests, LeaderboardRowReflowBrowserTests, FlagLeaderboardTests, FlagLeaderboardBrowserTests and FlagLeaderboardReviewBrowserTests.

Prefer a small field/state/comparator abstraction with explicit metadata over adding another large switch throughout leaderboard-page.js. Keep classic-script loading/global contracts and URL versioning intact; never commit generated wwwroot/js.

Required meaningful fixtures and checks:
1. Deterministic primary/secondary ordering with opposite directions, full-precision near ties, both-missing primary, missing-last descending, valid zero, names/flags, and stable official fallback. Confirm original ranks/medals/anchors survive.
2. Real-data semantics: mixed Pro/Amateur; distinct best Pheno/Bortz result dates; old/latest panels with missing markers; same-date tie; invalid date; albumin above 54 retained; insufficient crowd guesses; one-result improvement missing; raw badge count excludes utility badges.
3. SSR versus hydrated parity for two custom columns and fixed as-of age data, including JS disabled and delayed/failed athlete fetch. Default SSR/podium still works.
4. Browser: toolbar order and icon-only button; immediate two-level sorting; each header reverses its own level; Rank reset; optional secondary; search and cohort changes preserve intended ranks; both improvement clocks; URL reload/Back/Forward and View all; By Flag round-trip.
5. Desktop/mobile screenshot evidence at representative wide and 390/320 px sizes; keyboard/Escape/closed-tab-order, focus preservation, 200% text and reduced-motion checks. No need for a broad unrelated site audit.

Run frontend npm run check and npm run build, plus targeted .NET/browser suites covering changed contracts. Commit only intended source, tests and this spec. The parent agent owns commit/push/PR in the selected worktree. PR closes #153 when all accepted behavior is implemented and verified, and references #133 without closing it. Do not merge or claim production deployment as part of this worktree task.

