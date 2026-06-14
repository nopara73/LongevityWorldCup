# Design TODO — remaining items after the adversarial QA round (2026-06-12)

The catastrophic/major/medium punch list in `DESIGN_AUDIT.md` is fully fixed and verified. These are the honest leftovers — none block shipping, all are worth doing.

## Needs live data to verify

- [ ] **Populated longevitymaxxing board.** Local dev data renders only the empty state ("No one has joined yet"). Verify the populated board against production data: pill palette (the practice "P" cell is green in local CSS — confirm it renders green, not production's blue), row density, and the day-ramp legend at 320/390/768/1440.

## Composition refinements (production parity, not regressions)

- [ ] **Onboarding step-2 orphaned "Back" pill.** The lone centered Back pill above the form (next to the step dots) reads compositionally stranded at 1440. Production has the same structure. Candidate: dock it left of the step dots or into the sticky progress bar.
- [ ] **"JUST TRACK IT" poster depth.** The play-hub poster is now brand-language CSS (watermark + Anton + kicker), much better than the old JPG, but at 1440 the card still has dead space in the upper third. A stat line or live participant count could earn that space.

## System hygiene

- [ ] **Commit the working tree.** The cyan→green docs fix lives in the `MarkdownPageGenerator` template now (safe from regeneration), but everything else in this design overhaul is uncommitted working-tree state.
- [ ] **`shotter --help` crashes** (`IndexOutOfRangeException` when fewer than 2 args). Harmless for scripted use; fix when next touching the tool.

## Watch items (no action yet)

- The 404/502 Harold meme images show a blue LWC logo on the shirt/flag. Off-palette, but it is the joke's identity and matches production; redo only if a green-wardrobe Harold shoot ever materializes.
- `pro-discounts.js` and `badges.js` keep a cyan gradient for the "personal" badge tier. It is a badge identity color (categorical), not chrome; revisit only if the badge tier palette is redesigned as a set.
