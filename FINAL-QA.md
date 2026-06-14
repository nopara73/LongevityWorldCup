# Final Adversarial QA

Scope: adversarial review of the current local site against production screenshots in `.artifacts/screenshot-baseline/`, current local/running-app screenshots in `.artifacts/final-qa/`, `ART-DIRECTION.md`, `DESIGN.md`, and `UI-AUDIT.md`.

Reviewer stance: looking for reasons to reject the local site as embarrassing, confusing, cheap, inconsistent, broken, off-brand, or worse than production.

## Current Verdict

Local is directionally stronger than production on brand identity, hero clarity, media kit, event pulse, and first-time comprehension. I would still reject a launch candidate if the remaining open items below were in scope for release polish, especially sparse leaderboard states and returning-athlete task flow. Two safe high-impact issues were fixed in this pass.

## Findings

| # | Status | Severity | Route/state | Evidence | Problem | Why it matters | Fix direction |
|---:|---|---|---|---|---|---|---|
| 1 | Fixed now | P1 | `/join`, mobile onboarding stepper | Before: `.artifacts/final-qa/before/join-progress-labels__mobile.png`; After: `.artifacts/final-qa/after/join-progress-labels__mobile.png` | The active progress label said `Test`, which reads like internal scaffolding or a QA environment label rather than a public application step. | It appears exactly where a new athlete is deciding whether to submit blood-test data and money. This lowers trust immediately. | Changed step 1 to `Choose`, with accessible labels/title text updated to `Choose league`. |
| 2 | Fixed now | P1 | `/pheno-age` and `/bortz-age`, mobile calculator entry | Before: `.artifacts/final-qa/before/pheno-progress-labels__mobile.png`; After: `.artifacts/final-qa/after/pheno-progress-labels__mobile.png` | The calculator headline still used the slogan `This is blood sport`. Even with brand-green emphasis, it feels sensational on a biomarker/proof form. | The calculators are trust surfaces. A cold visitor should see a credible task, not a line that sounds like shock copy or medical drama. | Replaced headings with `Calculate Pheno Age` and `Calculate Bortz Age`. |
| 3 | Open | P2 | Sparse leaderboard views, desktop | Baseline: `.artifacts/screenshot-baseline/local/leaderboard-crowd__desktop.png`; `.artifacts/screenshot-baseline/local/leaderboard-bortz-improvement__desktop.png` | Sparse ranking modes can still look like missing data because there is little intentional framing around eligibility and why the table is short. | Ranking modes must feel official even when only a few athletes qualify. Otherwise visitors may assume the app failed to load. | Add compact eligibility/sparse-state framing and reduce dead table height when row count is low. |
| 4 | Open | P2 | `/select-athlete`, mobile returning-athlete flow | Baseline: `.artifacts/screenshot-baseline/local/select-athlete__mobile.png`; production: `.artifacts/screenshot-baseline/production/select-athlete__mobile.png` | Returning athletes still meet more chrome/visual weight than task clarity. The action is to find yourself, but the route does not feel as direct as a returning-user tool should. | Existing athletes are high-intent users. Anything between them and profile/result management feels like product friction. | Move search/selection higher, reduce decorative weight, and make the primary continuation path unmistakable. |
| 5 | Open | P2 | Leaderboard scrolled table, desktop | Current: `.artifacts/final-qa/after/leaderboard-sticky-cta__desktop.png` | The vertical `ULTIMATE LEAGUE` rail is distinctive but can become visually dominant and partly contextless in deep scroll positions. | It risks reading like a decorative rail rather than useful league orientation. Dense data should stay the hero. | Review the rail at scrolled positions; consider a smaller sticky section label or table-caption treatment on desktop. |
| 6 | Open | P2 | Events archive, mobile | Baseline: `.artifacts/screenshot-baseline/local/events__mobile.png`; improved pulse: `.artifacts/section-wow/after/events-pulse__mobile.png` | The public race pulse improves first glance, but the archive below remains a long unfiltered ticker. | Events are credibility and aliveness, but without time/type wayfinding the value is hard to mine. | Add lightweight event-type filters or date grouping without sacrificing row density. |
| 7 | Open | P3 | Mobile footer / long secondary navigation | Baseline: `.artifacts/screenshot-baseline/local/home__mobile.png`; `.artifacts/screenshot-baseline/local/events__mobile.png` | Footer links remain long and icon-heavy with limited grouping. | This is not launch-blocking, but the last impression feels less disciplined than the header/hero system. | Group key trust links first: Rules, About, History, Media, Contact; quiet lower-priority social links. |
| 8 | Open | P3 | Homepage comprehension bridge, desktop/mobile | Current: `.artifacts/final-qa/after/home-first-read__desktop.png`; `.artifacts/first-time-comprehension/after/home-first-read__mobile.png` | The bridge is useful, but it adds another card-like panel to a page already at risk of warm-paper/card drift. | The art direction says the brand survives when scoreboard, athletes, and numbers lead. Too many panels can soften the edge. | Keep monitoring after real-content testing; if it feels too card-like, fold the same content into a flatter table-adjacent explainer. |

## Checks Performed

- Compared production/local baseline screenshots for homepage, leaderboard, calculators, play/join flows, media, events, docs, and error pages.
- Captured current running-app QA screenshots before fixes in `.artifacts/final-qa/before/`.
- Fixed the safe high-impact issues above.
- Captured after screenshots in `.artifacts/final-qa/after/`.

## Safe Fixes Applied

- `wwwroot/partials/main-progress-bar.html`: progress labels now read `Choose`, `Calculate`, `Submit`; accessible labels/titles now describe the actual steps.
- `wwwroot/onboarding/pheno-age.html`: headline now says `Calculate Pheno Age`.
- `wwwroot/onboarding/bortz-age.html`: headline now says `Calculate Bortz Age`.
