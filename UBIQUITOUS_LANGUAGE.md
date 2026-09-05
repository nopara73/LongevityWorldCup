# Ubiquitous Language

## Terms and Ranking

- **Longevity athlete**: approved participant with biological age data; **Applicant**: pre-approval.
- **Track**: Pro (eligible bortz age) or Amateur (non-bortz). **League**: ranking view. **Ultimate League** ranks Pro before Amateur.
- **Rank**: current computed order; **Placement**: stored/historical position.
- **Biological Age Difference**: biological minus chronological age, lower is better; **Age Reduction** is its favorable public label. Compute with unrounded ages; round only for display.
- **Effective Age Reduction**: Ultimate League score, Bortz for Pro, otherwise pheno.
- **Pheno Age**, **Bortz Age**, and **Crowd Age** are distinct clocks/views. Calculator rank previews use only the selected clock's field.
- Albumin is capped at 54 g/L after unit conversion in both biological-age calculations and domain contributions. Preserve original stored/displayed lab values; higher values confer no scoring benefit.
- **Proof**: evidence for an athlete, profile, or result. **Profile picture**: public display image.
- **Event**: persisted public/social output; **Custom Event**: admin-created. **Badge**: computed award. **Social post**: copy for X, Threads, Facebook, Slack, or future integrations.

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose; reserve `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialization, external names, or quoted legacy data. Keep clock, calculator, and result distinct.

## Crowd Age

- **Crowd Count** counts accepted realistic guesses for the current image. Qualification requires at least 100 guesses; rank by `CrowdAge - chronologicalAge`, then higher Crowd Count, earlier date of birth, and name.
- Guesses belong to exact published image content via `ProfileImageId`. Byte-identical uploads restore that image's history; any changed bytes, including re-encoding, start with zero active guesses. Preserve older histories.
- Qualification, placements, and badges use current-image guesses. Image changes silently recompute placements; published Events remain historical. The raw crowd age badge and on-site BadgeAward Events remain visible but produce no social posts.

## Payments

An existing Amateur's first eligible bortz result uses Pro-upgrade pricing. Other result/profile updates are free. The server classifies submissions from existing athlete and submitted clock data and sets authoritative entry/upgrade pricing; browser payment state is only a handoff hint.

## Events and Improvement

- Accepted-test Events appear only in athlete profile highlights, including partial, non-improving, and backfilled results. Identity is athlete plus test date across clocks; corrections, added markers, reordering, reloads, and restarts cannot duplicate them. Date the Event at first observed publication and identify the test date in its text. Silently baseline existing results when introducing tracking; atomically remember subsequent results and save Events, including startup discoveries. Exclude these Events from shared highlights and social queues.
- Biological-age improvement Events are chronologically new personal bests, dated to the result; older backfills predating the previous best create none.
- Pheno/Bortz best-improvement badges compare latest with first eligible result. Separate improvement leaderboards and placement Events rank `latest eligible age - worst eligible age` for that clock. Keep biological-age improvement, Crowd Age placement, and Pheno/Bortz Improvement placement Events distinct.
- Homepage highlights are curated: preserve fresh-Event athlete de-duplication, stale-event handling, and fourth-visit highlights-before-podium ordering.

## Longevitymaxxing Challenge

- Challenge scoring is separate from Ultimate League, biological-age placements, and athlete badges. Signup and daily check-ins continue indefinitely after Day 14 on the same global leaderboard; eligibility starts at local signup date.
- The first eligible check-in is practice: it counts toward checked-in days/streak, never habit points, category badges, point tie-breaks, or missed-scored-day reminder stops. Daily reminders continue until three consecutive missed scored days, excluding practice and pre-signup days.
- Allow the two latest local check-in dates, plus the oldest missed day in the current 14-day scoring window until saved. Present the oldest due day first.
- A repeated check-in submission returns the current state without attaching photos again or overwriting a later edit. Retries of an accepted submission remain valid after its catch-up day closes; a new submission must meet the usual eligibility rules.
- **Resting**: inactive leaderboard grouping; retain check-ins/discussions. Eligible catch-up check-ins can clear missed-day resting.
- Avatars prioritize linked Longevity athlete pictures over challenge-only uploads and Gravatar fallbacks. Linked discussion avatars/names open the athlete profile.
- **Habit garden**: persistent visualization, independent of scoring. Replay all saved answers in day order, including practice, from vitality `0`: Somewhat is neutral; Yes closes `2.5%` of remaining growth; No retains `65%` of vitality. Later Yes answers regrow; pending answers preview without replacing history.
- Community-call announcements are social-only Custom Events queued about an hour before selected calls. Public call URLs are allowed; participant access/stop links are not. Call emails have a separate opt-out and require a local start time of 07:00–20:59; stopping them leaves daily emails enabled.

## Challenge Discussions

- A **Discussion thread** begins with optional check-in text/photos or a welcome post created once at first signup confirmation. Welcome posts announce joining and allow replies immediately; they are not check-ins, scores, Events, or social posts.
- Check-in opening **Discussion posts** allow one editable post per participant/day. Each **Reply** belongs beneath a specific post and never creates/edits a check-in post.
- Order threads by `log2(reply count + 1) - days since latest activity`, then latest activity; no votes. Post edits/new replies advance activity. Reply edits preserve activity time and show an edited marker. Show replies oldest first.
- Authors edit/delete only their own replies. Edits reconcile pending mentions; deletion removes pending activity for that reply. Delivered emails cannot be recalled.
- Replies notify the post author; new `@Display Name` mentions notify confirmed participants with unique public names. Self-mentions/repeated saves enqueue nothing; mentioning the post author in a reply adds no duplicate notification. Allow at most five other participants per post/reply. Style mentions distinctly; link only those with athlete profiles.
- Deliver accumulated discussion activity only in the next otherwise-eligible daily check-in email, respecting reminder eligibility/opt-out. Mark only included notifications delivered after successful sending; failures leave them pending, and stopped/auto-stopped emails cause no alternative delivery.
