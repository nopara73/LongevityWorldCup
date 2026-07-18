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
- **Habit garden** is the participant's persistent Challenge growth visualization, separate from leaderboard scoring. Each category replays all saved answers in Challenge-day order, including practice, from a seedling vitality of `0`; Somewhat is neutral. Each Yes closes `2.5%` of the remaining distance to full growth, so early growth is visibly gradual and gains diminish. Each No retains `65%` of current vitality, so a mature plant loses much more absolute growth than a seedling and consecutive losses diminish. Later Yes answers can regrow the plant. A pending answer previews its projected vitality without replacing the accumulated history.
- Challenge pledge is optional and is not collected on signup. Participants can set a pledge from the participant menu whenever profile editing is available. If no pledge is set, the check-in flow may prompt for one only when commitment enforcement is about to become relevant; no pledge means the participant continues normally and no Challenge commitment payment can trigger.

## Naming

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialized fields, external names, or quoted legacy data. Do not collapse clock, calculator, and result.

## Events

- Biological-age improvement Events represent chronologically new personal bests, use the result date as the Event date, and are not created when an older backfilled result predates the athlete's previous personal best.

## Longevitymaxxing Challenge

- Public community-call social announcements are social-only Custom Events queued about one hour before each selected call; they may include the public video call URL, but never participant access or stop links.
- Community-call reminder emails have their own opt-out; stopping them does not stop daily Challenge emails.
