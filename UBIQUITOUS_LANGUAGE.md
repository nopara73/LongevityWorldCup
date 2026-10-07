# Ubiquitous Language

Key terms and competition rules. Consult source and tests for detailed behavior.

## Terms

- **Longevity athlete**: an approved participant with biological age data. **Applicant**: a participant awaiting approval.
- **Track**: Pro with an eligible bortz result, otherwise Amateur. **League**: a ranking view. **Ultimate League** ranks Pro before Amateur.
- **Rank**: current computed order. **Placement**: a stored or historical position.
- **Pheno age**, **bortz age**, and **crowd age**: distinct clocks/views. Use lowercase in prose; retain code and external names where required.
- **Biological age difference**: biological minus chronological age; lower is better. **Age reduction**: its public label. **Effective age reduction**: the Ultimate League score, using bortz for Pro and pheno otherwise.
- **Proof**: evidence for a profile or result. **Profile picture**: the public display image.
- **Event**: persisted public/social output. **Custom Event**: admin-created output. **Badge**: a computed award. Publishing an Event and delivering its social posts are separate actions.
- **Reddit delivery**: Event posting through the LWC Devvit app; see [Reddit announcements](LongevityWorldCup.Documentation/RedditAutoposting.md).
- **Test date**: the laboratory measurement date. **First public announcement**: a separately recorded publication date; missing historical dates remain unknown.

## Competition

- Calculate age reduction from unrounded ages; round only for display. Pheno and bortz rankings compare age reduction, not raw biological age. Search and filtering preserve ranks within the selected view.
- Entry requires a complete same-date panel: 9 markers for Amateur; 22 for the Pro form (21 bortz model markers plus WBC). DunedinPACE alone does not qualify. Rejuvenation Olympics participants submit a fresh LWC application.
- Cap scoring albumin at 54 g/L after unit conversion in both biological-age calculations. Preserve original stored/displayed lab values.
- **Crowd Count**: accepted realistic guesses for the current image. Qualification requires 100 guesses. Rank by crowd age minus chronological age, then higher Crowd Count, earlier date of birth, and name.
- Crowd guesses belong to the exact image bytes. Identical uploads restore that image's history; changed bytes start with zero active guesses. Keep older guesses and published Events historical.
- **Improvement badges** compare latest with first eligible results. **Improvement leaderboards** compare latest with worst eligible age for the selected clock. Older backfills do not create new personal-best Events.
- An Amateur's first eligible bortz result uses Pro-upgrade pricing; other result/profile updates are free. Manual payment review accepts credible evidence with reasonable confidence despite imperfect identifiers; record inferred matches honestly.

## Longevitymaxxing Challenge

- Challenge scoring is separate from biological-age competition. Signup and check-ins continue beyond Day 14. Community calls are retired.
- The leaderboard scores the latest 14 closed days. A habit date closes at 12:00 UTC two calendar dates later. Open days do not affect standings; eligible late check-ins can revise closed days.
- Equal points share competition ranks (1, 1, 3), independent of hiding **Resting** participants. **Full marks** requires check-ins and the global maximum points across all 14 closed days, including the existing qualifying-slip allowance.
- The first eligible check-in is practice: it counts toward check-in days and streaks, not habit points or category badges. **Habit garden** is a persistent visualization independent of scoring.
- **Discussion thread**: a check-in post or welcome post and its replies. Welcome posts and replies are not check-ins, scores, Events, or social posts.
