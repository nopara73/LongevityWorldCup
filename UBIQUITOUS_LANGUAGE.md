# Ubiquitous Language

## Terms

- **Longevity athlete**: approved participant with biological age data; **Applicant** is pre-approval.
- **Track** is Pro or Amateur; **League** is a ranking view.
- **Pro** means eligible bortz age track; **Amateur** is the non-bortz track.
- **Ultimate League** is the primary overall leaderboard and ranks Pro before Amateur.
- **Rank** is current computed order; **Placement** is stored or historical position.
- **Pheno Age**, **Bortz Age**, and **Crowd Age** are distinct clocks/views.
- **Biological Age Difference** is biological age minus chronological age; lower is better. **Age Reduction** is the favorable public label.
- **Effective Age Reduction** is the Ultimate League score: Bortz for Pro, otherwise pheno.
- **Crowd Count** is accepted realistic guesses behind Crowd Age.
- **Proof** is evidence for an athlete, profile, or result. **Profile picture** is the public display image.
- **Event** is persisted public/social output; **Custom Event** is admin-created. **Badge** is a computed award.
- **Social post** is generated copy for X, Threads, Facebook, Slack, or future integrations.
- **Resting** means a Challenge participant is currently inactive for leaderboard grouping; saved check-ins and notes remain visible, and eligible catch-up check-ins can clear missed-day resting.
- **Pledge buffer** is Challenge-only commitment slack: the pledge triggers only when a check-in is more than two raw habit points below the raw score needed to meet the recent average. It does not change leaderboard scoring or general slip scoring.

## Naming

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialized fields, external names, or quoted legacy data. Do not collapse clock, calculator, and result.
