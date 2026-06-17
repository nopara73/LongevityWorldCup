# Ubiquitous Language

Use this as a compact guardrail for terms agents often misuse.

## Core Terms

- **Longevity athlete**: approved participant with biological age data; **Applicant** is pre-approval.
- **Track**: Pro or Amateur. **League** is a ranking view, not a track.
- **Pro**: eligible bortz age track; **Professional** is acceptable public wording.
- **Amateur**: non-bortz track, currently centered on pheno age.
- **Ultimate League**: primary overall leaderboard; Pro always orders before Amateur.
- **Placement**: stored or historical finishing position. **Rank** is computed current order.
- **Pheno Age**: all-time Amateur biological aging clock.
- **Bortz Age**: seasonal Pro biological aging clock.
- **Biological Age Difference**: `biological age - chronological age`; lower is better.
- **Age Reduction**: user-facing label for favorable negative Biological Age Difference.
- **Effective Age Reduction**: Ultimate League score: bortz age difference for Pro, otherwise pheno age difference.
- **Proof**: evidence asset for an athlete, profile, or result submission.
- **Profile picture**: public image for profiles, leaderboards, and share cards.
- **Crowd Age**: median realistic age guessed in **Guess My Age**.
- **Crowd Count**: accepted realistic guesses behind crowd age.
- **Crowd Age Difference**: `crowd age - chronological age`; lower is better.
- **Badge**: computed award from placements, clocks, submissions, crowd metrics, or editorial rules.
- **Event**: persisted public or social update. **Custom Event** is admin-created.
- **Social post**: generated post for X, Threads, Facebook, Slack, or future integrations.
- **Longevitymaxxing Challenge**: ongoing Lifestyle challenge with daily sleep, exercise, nutrition, and vice check-ins.

## Rules To Preserve

- **Ultimate League** ranks Pro before Amateur, then applies **Effective Age Reduction** and tie breakers.
- Existing **Amateur** athletes emit a **went Pro Event** when they first gain an eligible **Bortz Age** result. New athletes who join already eligible for **Pro** only emit normal join/rank Events.
- Existing athletes emit a biological-age improvement **Event** when a changed result lowers their stored best **pheno age** or **Bortz Age**. This is an Event only; it is separate from the Pheno/Bortz Improvement leaderboard metrics.
- Homepage highlights are a curated subset of **Events**, not the raw Event feed; they may suppress repeated athlete-centric Events and keep only the most important fresh Event per athlete when enough unique highlights exist. Stale historical Events must not suppress a newer Event from the same athlete.
- **Crowd Age** top-10 placement changes emit **Events** after the initial stored placement snapshot. These follow the separate **Crowd Age leaderboard** ordering and do not affect **Ultimate League** ranking.
- **Crowd Age** top-10 placement **Events** are only for the athlete whose own accepted guess changed the leaderboard and who entered or improved by displacing a previous holder. Passive shifts caused by someone else moving, losing eligibility, or falling out are not Events.
- **Crowd Age leaderboard** requires 100 accepted guesses and orders by **Crowd Age Difference**, **Crowd Count**, date of birth, and name.
- **Guess My Age** increments **Crowd Count** only after server-side rate limiting accepts one realistic guess per client IP and athlete during the configured short window.
- Improvement leaderboards require at least two eligible submissions and are separate from the **Ultimate League**. Pheno orders by `latest eligible pheno age - worst eligible pheno age`, then pheno age reduction, date of birth, and name. Bortz uses Bortz equivalents and is route/filter-only.
- Pheno/Bortz Improvement top-10 placement changes emit **Events** after the initial stored placement snapshot. These are placement Events for the improvement leaderboards, not biological-age improvement Events.
- Pheno/Bortz Improvement top-10 placement **Events** are only for the athlete whose own result changed and who entered or improved by displacing a previous holder. Passive shifts caused by another athlete falling out or moving down are not Events.
- **Best Improvement** is a baseline-to-latest badge metric, not the worst-to-latest improvement leaderboard metric.
- **Longevitymaxxing Challenge** results may appear as **Events**, but never affect **Ultimate League** ranking, biological age placements, or athlete **Badges**.
- **Longevitymaxxing Challenge** daily check-ins continue after Day 14 on the same live leaderboard. Day 15, Day 16, and later days add to the existing daily grid; do not archive, freeze, split, or create a preserved winners board for the leaderboard.
- **Longevitymaxxing Challenge** Day 14 completion/result **Events** still emit after the existing grace window and may say participants completed the Longevitymaxxing Challenge, even though the live check-in leaderboard continues afterward.
- **Longevitymaxxing Challenge** signup stays open during the ongoing challenge. New signups join the same global leaderboard, see prior global days as empty/missed, and may only check in for days on or after their local signup date.
- **Longevitymaxxing Challenge** each participant's first eligible check-in is practice: it counts for checked-in days and streak, but not habit points, category leader badges, or point tie-breaks.
- **Longevitymaxxing Challenge** original Day 1 and each participant's personal practice day count for check-ins, streak, and consistency signals, but not habit points, category leader badges, or point tie-breaks.
- **Longevitymaxxing Challenge** habit points use a small day-weight ramp after practice: Day 2 starts at the raw 8-point maximum, the original Day 14 peak is 11 points, and later days stay capped at that peak unless scoring is explicitly redesigned.
- **Longevitymaxxing Challenge** habit points allow one daily slip only after an actually perfect previous check-in: either one `No` territory or one/two `Somewhat` territories still score that day's maximum, but a saved slip is not perfect for saving the next day.
- **Longevitymaxxing Challenge** built-in call defaults use Sunday 08:30 GMT+2 call dates for future competitions. The June 2026 finale has a one-off Sunday 08:30 GMT+2 override; the already-completed June 2026 kickoff keeps its historical selected time.
- **Longevitymaxxing Challenge** leaderboard ties after challenge performance metrics prefer participants linked to a currently placed Longevity athlete profile, then better current placement, then older linked athletes by date of birth.
- **Longevitymaxxing Challenge** call times may be selected before the challenge starts for 24-hour reminders.
- **Longevitymaxxing Challenge** daily reminder emails default to 07:00 in each participant's local timezone and may catch up later that same local day if the exact hour is missed.
- **Longevitymaxxing Challenge** daily reminder emails continue indefinitely and stop after 3 consecutive missed scored days. Practice does not count, and days before a participant's local signup date do not count.
- **Longevitymaxxing Challenge** participant check-in notes and note photos are public on the challenge page.
- **Longevitymaxxing Challenge** profile pictures are challenge-only unless a linked **Longevity athlete** profile picture exists. Uploaded challenge images outrank cached Gravatar fallbacks.

## Naming Notes

- Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in running sentences unless normal capitalization applies.
- Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code identifiers, serialized fields, external names, or quoted legacy data.
- Do not collapse **clock**, **calculator**, and **result**.
- **Bortz sex input** is unresolved in the issue tracker; do not treat current bortz age output as definitively aligned with the original paper.
