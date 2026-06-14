# Visual Surface Audit

Scope: color, opacity, transparency, shadows, borders, gradients, overlays, glow, contrast, and surface hierarchy only. This is an audit, not an implementation plan. It does not introduce a new palette.

Sources used:

- Production site: `https://longevityworldcup.com`
- Current local site: `http://127.0.0.1:5017`
- `DESIGN.md`
- `ART-DIRECTION.md`
- Shared CSS tokens in `LongevityWorldCup.Website/wwwroot/partials/header.html`
- Surface-heavy local CSS in homepage, leaderboard, docs generator, onboarding, play, media, event, and badge files

Screenshot references live in `.artifacts/visual-surface-audit/`.

## Inferred Surface System

The intended system is already clear:

- Dark chrome is reserved for header, hero, footer, athlete modal, entry posters, and sport-summary bands. It should be near-black/green-black, off-white ink, faint green sheen, and hairline borders.
- Light content sits on warm paper: `#f4f3ea` page, `#fffefa` cards, `#1c2722` ink, `#0b7d45` links/active accents.
- Bright green `#43ee83` is the signature score/CTA/focus color.
- Amber `#8f6a0e` / `rgba(212,175,55,...)` is secondary competition status: Amateur, crowd, prize, new athlete.
- Shadows should be low, warm, and tokenized: `--surface-shadow` and `--surface-shadow-lifted`. Heavy black blur belongs only to top-level overlays, and even there should be controlled.
- Borders should usually be hairlines, either warm ink alpha on light paper or off-white alpha on dark chrome.
- Blue/cyan/pink are legacy drift unless they are literal third-party/platform colors or a clearly defined biomarker/category exception.

## Screenshot Index

- Homepage: `local-desktop-home.png`, `local-mobile-home.png`, `production-desktop-home.png`, `production-mobile-home.png`
- Leaderboard: `local-desktop-leaderboard.png`, `local-mobile-leaderboard.png`, `production-desktop-leaderboard.png`, `production-mobile-leaderboard.png`
- Athlete modal: `local-desktop-athlete-modal.png`, `production-desktop-athlete-modal.png`
- Docs: `local-desktop-ruleset.png`, `local-mobile-ruleset.png`, `production-desktop-ruleset.png`
- Play entry: `local-desktop-play.png`, `local-mobile-play.png`
- Onboarding/application: `local-mobile-apply.png`, `local-desktop-bortz-age.png`, `local-mobile-bortz-age.png`
- Events/challenge/media/proofs: `local-desktop-events.png`, `local-desktop-longevitymaxxing.png`, `local-desktop-media.png`, `local-mobile-proofs.png`

## Implementation Status

Implemented in the visual-surface pass:

- Findings 2, 5, 6, 7, 8, 9, 11, 13, 14, 15, 16, 17, 18, 20, 21, 22, 23, 24, 25, 29, and 30 are addressed with shared surface/CTA/chrome/overlay tokens, reduced shadow drift, tokenized docs/onboarding/product surfaces, and removal of obvious blue/cyan Pheno badge drift.
- Findings 1 and 3 are preserved as directional references: production's cyan/pink/gray visual language is intentionally not preserved, while the current local dark hero remains the dark-chrome benchmark.
- Findings 4, 10, 12, 19, 26, 27, and 28 are partially addressed or intentionally constrained. The pass tokenized recurring dark cards, event/challenge/media bands, badges, and proof-adjacent surfaces, but preserved semantic category colors, podium metals, danger/status colors, and screenshot-specific hero composition rather than redesigning them.
- Generated docs pages will pick up the template fix after the next build; direct edits to `wwwroot/misc-pages/about.html`, `history.html`, and `ruleset.html` remain out of scope because those files are generated.

Additional visual-system harmony sweep:

- Screenshot references: before set in `.artifacts/visual-surface-harmony-before/`; final after set in `.artifacts/visual-surface-harmony-after/`.
- Addressed remaining muted-tone drift on homepage copy, generated docs navigation/source text, and onboarding privacy notes by moving cold slate grays to the warm paper ink family.
- Addressed remaining CTA and control drift in onboarding/proof upload, personal badge renderers, event-board embed shadows, Guess My Age shared modal surfaces, and the internal social post manager.
- Replaced leftover raw cyan/blue/slate/pink scan hits and high-alpha bespoke black shadows in the audited public surface files. The final targeted scan returns no hits for the old cyan/blue/slate/pink values, cold row tint, heavy blur recipes, or `rgba(0,0,0,.4)` shadow exceptions.
- No new palette was introduced. `DESIGN.md` did not need a new rule in this sweep; the fixes enforce the existing green CTA, warm paper, dark chrome, hairline border, and role-based shadow rules.

## Findings

### 1. Production legacy accent drift should not be preserved

- Route/component: Production homepage and leaderboard
- Viewport/state: Desktop first viewport/table
- Screenshot reference: `production-desktop-home.png`, `production-desktop-leaderboard.png`
- Problem: Cyan table chrome, pink portrait/media accents, gray hero slab, and broad white cards dominate key surfaces.
- Type: Color, gradient, shadow, surface hierarchy
- Why disharmonious: These colors feel like old Material UI and generic dashboard styling, not the current green/amber scoreboard identity.
- Proposed fix direction: Preserve production's product reality, podium, and dense table composition, but reject its cyan/pink/gray visual language. Use the local token direction as the source of truth.

### 2. Local surface tokens are strong, but not yet enforced everywhere

- Route/component: Whole site CSS
- Viewport/state: All
- Screenshot reference: All local screenshots; source scan of `leaderboard-content.html`, onboarding, docs generator, badges
- Problem: Shared tokens in `partials/header.html` are coherent, but many components still define local raw `rgba(...)`, hard-coded shadows, and one-off gradients. `leaderboard-content.html` alone has hundreds of local surface values.
- Type: Color, opacity, shadow, border
- Why disharmonious: The rendered site often looks aligned, but maintenance risk is high: future edits can easily reintroduce cyan/pink, dirty overlays, or mismatched paper surfaces because components do not consistently consume shared roles.
- Proposed fix direction: No palette change. Consolidate common alpha roles into existing tokens or token-adjacent helpers: light surface border, light hover tint, dark chrome border, dark overlay, green focus ring, amber status tint.

### 3. Homepage dark hero is the best current dark-chrome reference

- Route/component: Local homepage hero
- Viewport/state: Desktop and mobile first viewport
- Screenshot reference: `local-desktop-home.png`, `local-mobile-home.png`
- Problem: Not an issue; this is the strongest reference surface.
- Type: Surface hierarchy
- Why disharmonious: Not disharmonious; this is a preserve/reference pattern. It would become disharmonious only if other dark surfaces drift away from this restrained near-black/green-black treatment.
- Proposed fix direction: Preserve this as the benchmark for other dark bands, especially event/challenge/media headers and modal chrome.

### 4. Homepage live-standing tiles are close, but use many local dark alphas

- Route/component: Homepage live standings arena
- Viewport/state: Desktop/mobile hero
- Screenshot reference: `local-desktop-home.png`, `local-mobile-home.png`
- Problem: Tile borders/backgrounds use several independent off-white alpha values. It works visually now, but the tile stack is not expressed as reusable dark-surface roles.
- Type: Opacity, border, surface hierarchy
- Why disharmonious: Similar dark cards elsewhere use different alpha and border strengths, so the site can feel like multiple dark systems.
- Proposed fix direction: Keep the appearance, but define the arena pattern as the canonical dark card surface: dark panel fill, off-white hairline, selected/leader border, and green score.

### 5. Leaderboard table direction is strong, but zebra tint is slightly cold

- Route/component: Local leaderboard table rows
- Viewport/state: Desktop default standings
- Screenshot reference: `local-desktop-leaderboard.png`
- Problem: Zebra rows use a cool `rgba(240,244,248,.5)` tint against warm paper.
- Type: Color, surface hierarchy
- Why disharmonious: The cool gray-blue row tint is subtle, but it sits outside the warm paper/green/amber system.
- Proposed fix direction: Use warm paper alternation derived from `--paper`, `--card-bg`, or a low-alpha ink/green tint. Keep the density and row hierarchy.

### 6. Leaderboard badge colors still feel like a separate legacy system

- Route/component: Leaderboard badge strips
- Viewport/state: Desktop table rows
- Screenshot reference: `local-desktop-leaderboard.png`
- Problem: Badge colors include black, bright blue, silver/gray, orange, brown, and green with mixed gradients and shadows.
- Type: Color, gradient, shadow
- Why disharmonious: Badges are visually louder and more varied than the table surface, so they compete with rank, athlete, and score.
- Proposed fix direction: Keep badges compact, but remap them into disciplined families: rank medals, clock type, domain category, utility/social, and status. Use green/amber/neutral first; allow blue only if Pheno is explicitly standardized as a clock color.

### 7. Pheno badge blue is the clearest remaining legacy color

- Route/component: Shared badge renderer
- Viewport/state: Leaderboard, events, athlete modal
- Screenshot reference: `local-desktop-leaderboard.png`, `local-desktop-events.png`, `local-desktop-athlete-modal.png`
- Problem: `badges.js` still uses `#3b82f6`, `#1d4ed8`, and `#1e40af` for Pheno badges.
- Type: Color, gradient
- Why disharmonious: Design rules explicitly retire blue/purple except for literal platform colors. This blue reads like generic dashboard color, not a sport clock system.
- Proposed fix direction: Decide whether clock colors are allowed as semantic exceptions. If not, move Pheno to a green/neutral variant and reserve amber for Amateur/crowd/prize. If yes, document Pheno blue as a narrow exception in `DESIGN.md`.

### 8. Badge shadows are heavier than the rest of the light-surface system

- Route/component: Badge CSS and badge hover/detail states
- Viewport/state: Leaderboard/events/modal
- Screenshot reference: `local-desktop-leaderboard.png`, `local-desktop-events.png`
- Problem: Badge styles include `0 10px 24px rgba(0,0,0,.26-.28)` and bright inset highlights.
- Type: Shadow, glow
- Why disharmonious: The site’s card shadow is intentionally low (`0 4px 10px rgba(28,39,34,.08)`), while badges can look like glossy app icons.
- Proposed fix direction: Keep hover lift, but cap badge shadows to the compact-token system. Use border/ring or one small shadow instead of glossy depth.

### 9. Event board dark summary band uses a different dark-surface recipe

- Route/component: Events page summary band
- Viewport/state: Desktop
- Screenshot reference: `local-desktop-events.png`
- Problem: The event summary band is dark and useful, but its internal card borders/backgrounds do not match the homepage arena or modal cards.
- Type: Surface hierarchy, border, opacity
- Why disharmonious: It feels like a separate dashboard module rather than the same official sport chrome.
- Proposed fix direction: Align event summary cards to the homepage dark arena pattern: same near-black fill, off-white hairline, green numerals, and restrained dividers.

### 10. Event row badges add color noise to the sports ticker

- Route/component: Events page row badges
- Viewport/state: Desktop event archive
- Screenshot reference: `local-desktop-events.png`
- Problem: Small badges in text rows introduce teal, gray, orange, brown, and green in quick succession.
- Type: Color, contrast
- Why disharmonious: The event board should feel like a sports ticker; too many badge colors make it read like an icon feed.
- Proposed fix direction: Use monochrome/neutral mini-badges in event rows by default, with full-color badges reserved for athlete modal or hover/detail contexts.

### 11. Athlete modal backdrop feels dirty and heavy

- Route/component: Athlete modal overlay
- Viewport/state: Desktop opened modal
- Screenshot reference: `local-desktop-athlete-modal.png`
- Problem: Backdrop uses black alpha plus blur, producing a gray smear over the leaderboard. The underlying page remains visually noisy behind the modal.
- Type: Overlay, opacity, backdrop-filter
- Why disharmonious: Dark chrome should feel lit and deliberate, while this overlay feels like a browser blur layer.
- Proposed fix direction: Use a simpler near-black transparent overlay with less blur, or darken more with less backdrop filtering. Keep modal above page chrome, but reduce dirty gray wash.

### 12. Athlete modal has too many competing dark panels

- Route/component: Athlete modal header/summary/badge panels
- Viewport/state: Desktop opened modal
- Screenshot reference: `local-desktop-athlete-modal.png`
- Problem: Portrait area, modal background, summary cards, evidence strip, and badge panel all use similar dark translucent fills and visible borders.
- Type: Surface hierarchy, border, shadow
- Why disharmonious: Rank/score/proof should dominate, but nested panels compete with one another.
- Proposed fix direction: Define three dark levels only: modal shell, primary score summary, and secondary evidence sections. Quiet borders on secondary panels and keep the score panel as the brightest surface.

### 13. Modal/proof overlay blur values are inconsistent

- Route/component: Athlete modal, proof viewer, top-level overlays
- Viewport/state: Open modal/proof states
- Screenshot reference: `local-desktop-athlete-modal.png`; source hotspots in `leaderboard-content.html`
- Problem: Overlay code uses multiple `backdrop-filter` values: 2px, 3px, 4px, 10px saturate, and 14px.
- Type: Overlay, opacity
- Why disharmonious: Different overlay types may feel unrelated even though they are all part of the same modal system.
- Proposed fix direction: Standardize overlay slots: page modal backdrop, modal shell material, proof viewer backdrop. Keep one blur/alpha recipe per slot.

### 14. Header/sticky chrome has scattered backdrop and shadow recipes

- Route/component: Header, sticky header, mobile nav/backdrop
- Viewport/state: Desktop and mobile scrolled states
- Screenshot reference: `local-desktop-ruleset.png`, `local-mobile-home.png`; source hotspots in `partials/header.html`
- Problem: Header code uses heavy black shadow (`0 4px 15px rgba(0,0,0,.4)`) and several blur overlays.
- Type: Shadow, overlay, glow
- Why disharmonious: The final direction prefers hairline separation and subtle green sheen, not floating heavy bars.
- Proposed fix direction: Keep dark chrome, but make sticky/header separation mostly border and faint sheen. Reserve heavy shadow for overlays, not fixed chrome.

### 15. Docs pages are aligned, but generated white translucency should be tokenized

- Route/component: Generated docs pages, contents nav and article card
- Viewport/state: Desktop/mobile docs
- Screenshot reference: `local-desktop-ruleset.png`, `local-mobile-ruleset.png`
- Problem: Docs look good overall, but generator CSS still uses local `rgba(255,255,255,.58)` and several hand-picked border alphas.
- Type: Opacity, border, surface hierarchy
- Why disharmonious: Docs are generated from a separate template, so token drift can silently reappear when pages regenerate.
- Proposed fix direction: Keep the docs look; express article card, index panel, active chip, table border, and mobile chip surfaces through shared paper/border tokens in the generator template.

### 16. Onboarding/application panels are too translucent and soft

- Route/component: `/apply` athlete details step
- Viewport/state: Mobile initial form
- Screenshot reference: `local-mobile-apply.png`
- Problem: The form shell and fieldset read as translucent white panels over warm paper, with very soft borders.
- Type: Surface hierarchy, contrast, border
- Why disharmonious: Task flow surfaces should feel like official entry paperwork, not ghost cards.
- Proposed fix direction: Use `--card-bg`, `--surface-border`, and a firmer fieldset surface. Keep the layout, but make the entry panel more solid and less glassy.

### 17. Disabled application buttons look inactive but also washed out

- Route/component: `/apply`, `/proofs`, onboarding steps
- Viewport/state: Disabled `Next` / `Select athlete`
- Screenshot reference: `local-mobile-apply.png`, `local-mobile-proofs.png`
- Problem: Disabled controls are pale beige with muted text. They communicate disabled state, but they can look low-contrast and unfinished.
- Type: Contrast, disabled state, surface hierarchy
- Why disharmonious: `DESIGN.md` says disabled pills should look switched off, not broken. Current disabled states are close, but too floaty against the paper.
- Proposed fix direction: Keep `--disabled-bg`, `--disabled-text`, `--disabled-border`, but use firmer border and consistent no-shadow treatment across all disabled pills.

### 18. Calculator/onboarding gradients are hard-coded variants of green/amber

- Route/component: `/bortz-age`, `/pheno-age`
- Viewport/state: Desktop calculator
- Screenshot reference: `local-desktop-bortz-age.png`
- Problem: Calculator pages use hard-coded gradients such as `#2edb6f -> #0b9b53`, `#0b7d45 -> #169a58`, and `#c77f0a -> #9a6309`.
- Type: Gradient, color
- Why disharmonious: The colors are near the brand family, but the variants are not shared with the CTA/result system and can drift.
- Proposed fix direction: Keep green result numerals and amber warnings, but reference the same action/status tokens used elsewhere. Avoid adding more gradient variants.

### 19. Calculator form cards are cleaner than application cards but still nested-soft

- Route/component: `/bortz-age` form card
- Viewport/state: Desktop age/date step
- Screenshot reference: `local-desktop-bortz-age.png`
- Problem: The calculator has a solid central flow, but outer card, fieldset, segmented control, and input frames all use light borders and pale fills with limited hierarchy.
- Type: Surface hierarchy, border
- Why disharmonious: It feels calmer than the sport surfaces, but a little closer to generic form UI than official ranking machinery.
- Proposed fix direction: Preserve readability; strengthen the top-level form shell and use subtler nested borders so the active task area is clearer.

### 20. Challenge board day pills and empty state use a separate beige/green mix

- Route/component: `/longevitymaxxing`
- Viewport/state: Desktop empty challenge board
- Screenshot reference: `local-desktop-longevitymaxxing.png`
- Problem: Day chips, table header, empty-state panel, and small legend pills combine beige, green, red, and mint fills with their own border strengths.
- Type: Color, opacity, surface hierarchy
- Why disharmonious: The challenge is related but separate; however, its board should still feel like a Longevity World Cup scoreboard, not a habit tracker module.
- Proposed fix direction: Keep day/status semantics, but align board header and empty state with the leaderboard/event-table surface system. Use amber/green status fills sparingly and standardize chip borders.

### 21. Media kit cards look like placeholder panels

- Route/component: `/media` asset cards
- Viewport/state: Desktop
- Screenshot reference: `local-desktop-media.png`
- Problem: File preview blocks use a gray-green translucent gradient and large blank preview areas.
- Type: Gradient, surface hierarchy, contrast
- Why disharmonious: The media header feels official, but the asset cards below feel like generic placeholder UI.
- Proposed fix direction: Use official-record card surfaces: `--card-bg`, `--surface-border`, small file badges, and less preview-panel tint. Keep the dark press-room header.

### 22. Media CTA green shadow is stronger than standard action depth

- Route/component: `/media` download CTA
- Viewport/state: Desktop
- Screenshot reference: `local-desktop-media.png`; source `misc-pages/media.html`
- Problem: Download CTA uses a stronger green shadow than the normal action system.
- Type: Shadow, glow
- Why disharmonious: It competes with the dark press-room header and differs from the shared CTA hover/focus depth.
- Proposed fix direction: Use the bright green CTA fill and standard focus/hover ring; reduce green drop shadow to match action tokens.

### 23. Proof/athlete selection image frame is too heavy

- Route/component: `/proofs` athlete selection
- Viewport/state: Mobile
- Screenshot reference: `local-mobile-proofs.png`
- Problem: The large silhouette image frame uses a thick dark border and heavy shadow relative to the quiet task UI above.
- Type: Border, shadow, surface hierarchy
- Why disharmonious: The primary task is selecting an athlete; the image support frame visually outweighs the input and disabled action.
- Proposed fix direction: Keep the image, but use `--surface-border`/`--surface-shadow-lifted` or move it lower/quiet it with a smaller frame. Avoid black frame dominance on light paper task flows.

### 24. Light cards sometimes pair border plus broad shadow

- Route/component: Cards across docs, media, challenge, leaderboard, onboarding
- Viewport/state: Desktop/mobile
- Screenshot reference: `local-desktop-media.png`, `local-desktop-longevitymaxxing.png`, `local-mobile-ruleset.png`
- Problem: Some surfaces still combine a visible 1px border with broad shadows or large translucent panels.
- Type: Shadow, border
- Why disharmonious: The design rules specifically avoid ghost-card language; official-record surfaces should be compact and crisp.
- Proposed fix direction: For normal cards, choose token border plus low token shadow. For dense tables, use border/dividers more than floaty shadow.

### 25. Focus states mostly use green, but some active/focus feedback leans amber

- Route/component: Leaderboard filters/sidebar and active filter sections
- Viewport/state: Hover/focus/active filter states
- Screenshot reference: `local-desktop-leaderboard.png`; source `leaderboard-content.html`
- Problem: Some focus-like rings and active shadows use amber glow (`rgba(212,175,55,...)`) rather than green.
- Type: Glow, color, focus state
- Why disharmonious: Amber should mean competition secondary status, not generic focus. Mixed focus colors can make keyboard interaction feel inconsistent.
- Proposed fix direction: Use green focus rings everywhere. Keep amber for active Amateur/crowd/prize/new-athlete semantic states.

### 26. Search/filter controls can compete with the table

- Route/component: Leaderboard search and filter controls
- Viewport/state: Desktop default standings
- Screenshot reference: `local-desktop-leaderboard.png`
- Problem: Search border, filter icon, active ranking pill, table header, and score links all use strong green/black at once.
- Type: Contrast, surface hierarchy
- Why disharmonious: The hierarchy is mostly good, but the controls can feel as visually important as the table data.
- Proposed fix direction: Keep active view strong; quiet inactive controls with token borders and reserve full green fill/ring for focus, selected, or primary actions.

### 27. Production docs are useful as content structure, not as final surface language

- Route/component: Production docs/rules pages
- Viewport/state: Desktop/mobile
- Screenshot reference: `production-desktop-ruleset.png`, `production-mobile-ruleset.png`
- Problem: Production docs carry the old generic header/gray-card feel more than the current official-dossier language.
- Type: Surface hierarchy, color
- Why disharmonious: Local generated docs now better match the sport identity; production is mainly useful as content/IA reference.
- Proposed fix direction: Preserve production docs completeness and readability, but keep local's official-dossier chrome, index, and light paper surfaces.

### 28. Footer/header chrome direction should be preserved

- Route/component: Header/footer chrome
- Viewport/state: Docs, play, mobile home
- Screenshot reference: `local-desktop-play.png`, `local-mobile-proofs.png`, `local-desktop-ruleset.png`
- Problem: Not a major issue; current dark bookends are mostly aligned.
- Type: Surface hierarchy
- Why disharmonious: Not disharmonious; this is a preserve/reference pattern. The risk is future chrome changes adding heavier shadows, blur, or light surfaces that weaken the clean bookend system.
- Proposed fix direction: Preserve this pattern. Future fixes should reduce stray shadows/blur values without making chrome flat or light.

### 29. Green has three roles that should remain distinct

- Route/component: Whole site
- Viewport/state: CTAs, scores, links, focus, status panels
- Screenshot reference: `local-desktop-home.png`, `local-desktop-leaderboard.png`, `local-mobile-apply.png`
- Problem: Green currently covers action, score, link, focus, success, and some status fills. It works because values are close, but the roles can blur.
- Type: Color, contrast
- Why disharmonious: If every green surface is equally saturated, primary action and score lose hierarchy.
- Proposed fix direction: Keep existing palette, but enforce role strength: bright green for scores/primary CTA/focus ring, dark green for links/table accents, low-alpha green for panels/hover/success backgrounds.

### 30. Amber needs stricter semantic boundaries

- Route/component: Amateur rows, challenge chips, prize panels, new-athlete markers, filters
- Viewport/state: Default/active states
- Screenshot reference: `local-desktop-home.png`, `local-desktop-leaderboard.png`, `local-desktop-longevitymaxxing.png`
- Problem: Amber appears in prize, Amateur, new athlete, active filters, and some focus-like glows.
- Type: Color, glow, surface hierarchy
- Why disharmonious: Amber is useful, but when it marks both meaning and interaction it becomes ambiguous.
- Proposed fix direction: Reserve amber for competition semantics: Amateur, crowd/prize, new-entry/status, selected secondary category. Do not use it for generic hover/focus.

## Highest-Value Fix Directions Later

1. Tokenize the final surface roles rather than changing the palette.
2. Normalize overlays/backdrops before polishing modal internals.
3. Rework badge/category colors into a documented semantic system.
4. Align onboarding/application/proof task panels with the official-record card surface.
5. Quiet broad shadows and translucent white panels on media/challenge/docs generated surfaces.
6. Keep production's product structure, but do not bring back production's cyan/pink/gray visual language.
