# DESIGN AUDIT — local vs production (longevityworldcup.com)

Date: 2026-06-12. Method: side-by-side screenshots, local (`localhost:5017`, current working tree) vs production, captured with the repo Playwright driver at 320, 390, 768, 1024×768, 1366×768, 1440×900, 1920×1080. State captures: leaderboard filter drawer, empty search, Crowd Age view, athlete modal (top/mid/proof viewer), onboarding step 1/step 2/result, disabled pills, footer, 404. Screenshots in `.artifacts/audit3/` (this round) and `.artifacts/audit2/` (state coverage earlier the same day).

Verdict in one line: local is structurally and stylistically ahead of production on almost every page, but the homepage hero — the single most important screen — is currently **shipping raw `{{HOME_HERO_*}}` template placeholders**, which is worse than anything production does anywhere.

---

## Homepage — first viewport (all 7 widths)

**Worse than production (catastrophic):**
- The hero renders literal `{{HOME_HERO_TOP_SCORE}}`, `{{HOME_HERO_ATHLETE_COUNT}}`, `{{HOME_HERO_PRO_COUNT}}`, `{{HOME_HERO_CLOCK_COUNT}}` at every viewport. Confirmed in served HTML (`curl` shows 3 unreplaced tokens). At 320–768 the stats placeholders overlap each other into garbled green text soup; at 1920 the score placeholder overflows the right edge of the screen. This destroys trust completely — it screams "broken staging site". Production, whatever its faults, always shows real numbers.
- Root-cause note for the fix phase (observed, not coded): the injection worked earlier today on a long-running server process and is broken on the freshly restarted one, so the replacement appears to depend on warmed leaderboard state with no fallback. A cold server must never serve raw tokens.

**Worse than production (conversion):**
- Production opens with an explicit promise: "Too old for your sport? Not this one. Reverse your age and **rise on the leaderboard!**" Local's hero copy is "TOP SCORE −22.1 years" + "longevity leaderboards". The poster is more striking, but the *pitch* — what this is and why I should care — is gone. A first-time visitor sees a number with no promise attached. The persuasion line was deleted with the old hero and never re-homed.

**Better than production:**
- (When data resolves) the TOP SCORE poster with Anton display type, green-on-black scoreboard, stats hairline row and pinned CTA is dramatically stronger and more brand-distinct than production's centered-logo-plus-tagline header, which looks like a generic dark Bootstrap jumbotron with a left-to-right gray gradient.
- Local's slim 37px chrome bar at desktop leaves the poster full-bleed; production stacks logo/wordmark/subtitle/CTA in dead vertical space.

**Cheap/template signals on production for reference (do not copy back):** gray L→R gradient banner, text-shadowed headline, Material-green CTA.

---

## Homepage — full scroll (390, 1440)

**Better than production:**
- Sections below the hero (podium, embedded leaderboard, Highlights, Swag, Hall of Fame, FAQ, Contribute, Newsletter) share one warm-paper surface with green/amber accents and Anton section headers. Production mixes cyan links, pink avatar rings, pink envelope icons, teal Hall-of-Fame table accents, blue buttons, and cool-gray panels — five accent families on one page.
- Local "VIEW ALL ATHLETES (206)" ink pill is a clearer next action than production's teal pill.
- Local footer link colors (green BTC address, ink Subscribe) match the system; production's cyan link + flat blue Subscribe button do not.

**Worse than production:**
- Nothing below the fold is worse than production. The regression is confined to the hero data.

**Inconsistent/unfinished (local):**
- Highlights date stamps render in the visitor's locale (e.g. "jún. 9.") inside an otherwise English UI. Defensible (it is the user's locale), but the mixed-language feel on an English page is noticeable. Decide deliberately; don't leave it accidental.

---

## Leaderboard (320, 390, 768, 1440, 1920 + states)

**Better than production:**
- Title: Anton "LONGEVITY LEADERBOARD" with clear air under the chrome vs production's text-shadowed Roboto title glued to the header.
- Color discipline: production uses cyan column headers, cyan chips, pink envelope icons, pink/cyan vertical "ULTIMATE LEAGUE" rail; local uses green/amber consistently, amber envelopes, green rail.
- Rank-movement arrows on avatars (green up-chips) read clearly at all widths.
- Filter drawer: local "Leagues" panel is green-themed and consistent; production's is cyan with the same layout.
- Empty search state: identical structure both sides, but local's green text + outline recovery button matches the system; production's cyan version doesn't.
- Crowd Age league view: amber theming, trophy rail, info banner — consistent and intentional on local.

**Worse than production:**
- Nothing identified at any width.

**Polish (local):**
- Filter chips still carry pink emoji glyphs (🩸 droplet, 🌸 wavy) for Bortz/Pheno categories. Emoji are not styleable; they read slightly off-palette inside an otherwise disciplined green UI. Tolerable as "emoji language", but a custom glyph set would be tighter.
- Search input focus ring is amber while everything else focuses green (seen in earlier state captures). One focus color should win.

---

## Athlete modal / profile (320, 390, 768, 1440; top, mid, proof viewer)

**Better than production:**
- Athlete name in display type ("SIIM LAND (#1)") vs production's plain Roboto.
- Diagonal charcoal gradient surface reads as a lit card; production's flat dark gray is duller.
- Proof viewer overlay (close target, readable document) equal to production.

**Worse than production (major):**
- At desktop, the site header band stays visible and opaque **above** the modal, and the modal's sticky mini-header ("Siim Land (#1) PRO" + progress bar) tucks underneath it, visually clipped at the top edge. Production's modal floats above everything with clean space around it. Local's arrangement makes the modal feel embedded in the page rather than层 above it, and the mini-header crop looks broken on scroll.

**Trust/copy (both environments):**
- "Crowd Age: 31 years, **1 guesses**" — singular/plural bug in the guesses count (visible locally with dev data; the same template presumably ships in production where counts are larger and the bug hides).

---

## Onboarding — join-game, pheno, bortz, convergence (390, 1440 + driven states)

**Better than production:**
- "HOW BAD DO YOU WANT IT?" / "THIS IS BLOOD SPORT" in Anton vs production's text-shadowed Roboto headlines.
- Amateur/Professional card titles in display type; production's are plain serif-weight Roboto.
- Year select focus/border green vs production's cyan.
- Result moment (driven end-to-end locally): Anton numeral with brand-green gradient, green rank-preview chip, green "Next →" gradient pill. Production's result number is bold Roboto with Material-green, rank chip teal — local is clearly stronger at the single most emotional moment of the flow.
- Disabled "Next →"/"Select →" pills are now unmistakably switched-off gray-beige; production's pale blue-gray disabled pills look broken/half-loaded.
- Error state ("result seems wrong… rush to a hospital") presents in a clean red panel; equivalent on both, acceptable.

**Worse than production:**
- Nothing identified.

**Polish (local):**
- Step-2 "Back" pill floats alone, centered above the form with the step dots — slightly orphaned composition at 1440. Production has the same structure, so this is parity, not regression; still a candidate for refinement.
- Rank-preview "Top 54%" percentile text is blue-leaning in the result card while everything around it is green (partially addressed; re-verify after server restart).

---

## Play hub & application flow (390, 768, 1440)

**Better than production:**
- CTA pair: local brand-gradient green "I'm a new athlete" + sage "I'm already an athlete" vs production's Material green + slate blue (two foreign accents).
- Athlete selection: local disabled "Select →" clearly disabled; production's looks like a rendering mistake.

**Worse than production:**
- Nothing identified.

**Unfinished feeling (both):**
- The "JUST TRACK IT" poster card is a large dark rectangle with faint shapes at tablet/desktop; it reads sparse on both environments. Parity, but neither side feels finished.

---

## Docs — about / ruleset / history / media (768, 1440)

**Better than production:**
- Anton page titles ("RULESET", "ABOUT LONGEVITY WORLD CUP", "MEDIA KIT") vs production's text-shadowed Roboto.
- Active contents-nav item green vs production cyan.
- Media kit's green download buttons vs production's teal.

**Worse than production:**
- Nothing identified.

**Risk note for the fix phase:**
- The cyan→green sweep on about/ruleset/history has been silently reverted twice by `git stash` cycles during testing. It is in the working tree now, but treat these three files as fragile until committed.

---

## 404 (390, 1440)

**Better than production:** Anton "404 NOT FOUND" vs production's text-shadowed Roboto title. Same Harold photo and joke caption — good on both.
**Worse than production:** nothing.

---

## Longevitymaxxing (390, 768, 1440)

**Better than production:**
- "LIVE LEADERBOARD" / "NEED YOUR CHECK-IN LINK?" in Anton; production uses bold Roboto, which makes the page feel like a different product.
- Legend chips/score pills consistent on both; local board header hierarchy slightly stronger.

**Worse than production:**
- Production shows a live board (14 people, practice pills); local dev data is empty. Environmental, not a design regression, but it means the *populated* board state could not be compared this round. The local empty state ("No one has joined yet" + hatched cells) is clear and acceptable.

**Inconsistent (both):**
- The blue "P" (practice) pill on production's board is off-palette; local equivalent unverified against populated data.

---

## Header / footer / nav

**Better than production:**
- Header band: faint green sheen + Anton wordmark vs production's gray L→R gradient (the gradient reads 2012-Bootstrap).
- Footer: local mirrors the header sheen; production footer is flat black-gray. Link/icon rows identical.
- Sticky PLAY pill: identical placement; local's gradient green is the brand CTA, production's flat green is generic Material.

**Worse than production:**
- Nothing identified.

---

## Forms / buttons / disabled / empty states (cross-cutting)

**Better than production:**
- One CTA system (bright green gradient = forward, ink = primary-neutral, sage = secondary, outline = back) vs production's mix of Material green, slate blue, pale blue-gray.
- Disabled = gray-beige + muted text; production disabled = pale blue tint that looks broken.
- Empty leaderboard state, search-clear button, info banners all green-system on local, cyan on production.

**Worse than production:**
- Nothing identified.

---

# Severity-sorted punch list

## 1. Catastrophic
1. **FIXED — Homepage hero serves raw `{{HOME_HERO_*}}` placeholders.** Root cause was not a code gap: `ApplyHomeHeroStats` replaces tokens unconditionally with live-or-fallback values, but the dev server was running a binary built *before* that call existed (`dotnet run --no-build` + stale `bin`). Fixed by rebuilding/restarting, and pinned by a new regression test (`HtmlPlaceholderInjectionTests`) that fails if any injected page serves `{{TOKEN}}` text or raw partial markers, and that asserts the hero score renders as a number on a cold start. Verified: cold-start `/` serves `-22.1 / 206 / 24` at all seven audit widths.

## 2. Major
2. **FIXED — Hero lost the conversion pitch.** The pitch line was present in the markup but suppressed by a `body:has(.lwc-home-hero) .game-description { display:none }` rule from an earlier redesign — that suppression was the weak edit, so it was deleted. The pitch now reads as the first line on the paper surface above the podium with clear air below the dark hero block (verified at 320/390/768/1024×768/1366×768/1440/1920).
3. **FIXED — Athlete modal sat underneath the fixed header band at desktop.** System fix, not an instance hack: the modal sat at `z-index: 3` while chrome lives at 1000–1002. The layering scale is now explicit (chrome 1000–1002, athlete modal 9000, proof viewer/guess-my-age 10000+) and documented in DESIGN.md. Verified at 1440 and 390: the overlay owns the full viewport and the sticky mini-header rides above the chrome on scroll.

## 3. Medium
4. **FIXED** — "1 guesses" pluralization in the modal's Crowd Age line: the unit is now a span set to "guess"/"guesses" by the crowd container updater.
5. **FIXED** — Leaderboard search focus ring moved from amber to the green focus system (green border + `rgba(67,238,131,.25)` ring); amber stays reserved for crowd features.
6. **VERIFIED** — Rank-preview percentile color is green (`#0b7d45`) in the current build of `bioageform.css`.
7. **FIXED AT THE SOURCE** — Docs cyan→green sweep (about/ruleset/history). Root cause found: these three pages are **generated** by `LongevityWorldCup.MarkdownPageGenerator` on every Website build, so direct edits to `wwwroot/misc-pages/*.html` were silently overwritten on each build/test run (the earlier "stash revert" theory was wrong). The cyan palette lived in the generator's HTML template; it is now swept to the green system in `MarkdownPageGenerator/Program.cs` and the regenerated pages verified green after a fresh build + test run.
8. **FIXED** — Mixed-locale dates ("jún. 9.") inside English UI on Highlights: list dates are pinned to `en-US` in `event-board-content.html`; verified rendering "Jun 9 / May 30".

## 4. Polish
9. **FIXED** — Pink emoji glyphs in leaderboard filter chips: league filter labels (aging clocks, tracks, divisions, generations, Prosperan) now render monochrome Font Awesome glyphs that inherit the label color through hover/active states. Country-flag emoji stay — flags are their own identity. Form `<option>` emoji helpers in `leagueIcons.js` are unchanged (options can't render HTML).
10. **FIXED** — "JUST TRACK IT" play-hub poster: the flat JPG was replaced with a brand-language CSS poster (dark lit surface, phoenix watermark, Anton "JUST TRACK IT." with green accent word, "Pick your path" kicker). Verified at 390/768/1440.
11. Onboarding step-2 lone centered "Back" pill above the form is compositionally orphaned (parity with production).
12. Longevitymaxxing populated-board state unverified locally (empty dev data); confirm pill palette against the live board before calling the page done.
13. Blue "P" practice pill (production board) off-palette; check local equivalent with populated data.

---

# Adversarial QA round (same day, post-fix)

Method: deliberately hostile re-review across less-traveled surfaces (privacy policy, media kit, error pages, play application flow, expanded filter drawer, landscape phone, 320/768/1280/1920) repeated until two consecutive full passes found nothing worth fixing.

Found and fixed:

- **Docs pages regenerated cyan on every build** — generator template swept (see item 7).
- **Broken scroll landing on the play application flow** (`character-selection`, `character-customization`, `proof-upload`, `edit-profile`): the auto-scroll landed titles 4.5rem down, leaving a half-clipped band of the tall scrolling header above the page title. These pages have no fixed bar, so they now use `scroll-margin-top: 0.9rem` locally and land with the slim chrome band + breathing title. (Override needs `body main > h1` specificity because header partial styles are injected later in the body.)
- **Privacy policy violated the surface law** — full content card on a dark page. Converted to the paper architecture (paper background, white card, ink text, green accents, uppercase display title) while staying standalone.
- **Sidebar emoji glyphs** replaced with monochrome icons (see item 9).
- **History page mobile nav chips overflowed the viewport** — long timeline entries had `white-space: nowrap` in the generator's mobile styles; chips now wrap inside the panel.

Passes 4 and 5 (homepage 320/390/1440, leaderboard + expanded filters at 390/1440/1920, docs at 390/1440, play hub + application flow, onboarding join/pheno/convergence, athlete modal top/mid/deep, longevitymaxxing, media kit, 404, contribute/newsletter/footer, landscape phone): no findings.

Still open (honest): punch-list items 11–13 (orphaned step-2 Back pill — production parity; populated longevitymaxxing board unverified with live data; practice-pill palette on a populated board unverified locally — CSS is green, not blue).

---

# What should be deleted / reverted / redesigned

- **Nothing should be reverted to production styling.** Production's gray-gradient chrome, text-shadow headlines, cyan/pink accent soup, and Material/slate buttons are the weaker system on every page compared.
- **Redesign/fix forward:** hero placeholder pipeline (1), pitch line (2), modal layering (3).
- **Delete:** nothing — no local element identified this round is worth less than its production counterpart, except the broken hero data which must be fixed, not removed.
