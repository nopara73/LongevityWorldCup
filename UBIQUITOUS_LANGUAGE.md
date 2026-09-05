# Ubiquitous Language

## Terms

- **Longevity athlete**: approved participant with biological age data; **Applicant** is pre-approval.
- **Track** is Pro or Amateur; **League** is a ranking view.
- **Pro** means eligible bortz age track; **Amateur** is the non-bortz track.
- **Ultimate League** is the primary overall leaderboard and ranks Pro before Amateur.
- **Rank** is current computed order; **Placement** is stored or historical position.
- **Pheno Age**, **Bortz Age**, and **Crowd Age** are distinct clocks/views.
- **Biological Age Difference** is biological age minus chronological age; lower is better. **Age Reduction** is the favorable public label.
- Biological age differences use unrounded biological and chronological ages; rounding is presentation-only.
- Albumin has a ceiling of 54 g/L in both pheno age and bortz age calculations, including domain contributions. Apply the ceiling after converting to g/L; preserve the original laboratory value in stored and displayed results. Values above the ceiling provide no further score or ranking benefit.
- **Effective Age Reduction** is the Ultimate League score: Bortz for Pro, otherwise pheno.
- **Crowd Count** is accepted realistic guesses behind Crowd Age.
- Each Crowd Age guess belongs to the exact published profile-picture content identified by `ProfileImageId`; Crowd Age and Crowd Count use only guesses for the athlete's current image.
- Historical guesses remain attached to their image. Publishing byte-identical image content preserves or restores that image's Crowd Age history, while any byte-different image, including a re-encoded version of the same picture, starts with no active guesses.
- Crowd Age qualification, placements, and badges use only the current image's guesses. An image change silently recomputes current placement state; previously published Events remain historical records.
- Crowd Age competition placements compare crowd age with chronological age. The existing raw crowd age badge and its on-site BadgeAward Events remain visible, but they must not generate social posts.
- **Proof** is evidence for an athlete, profile, or result. **Profile picture** is the public display image.
- **Event** is persisted public/social output; **Custom Event** is admin-created. **Badge** is a computed award.
- **Social post** is generated copy for X, Threads, Facebook, Slack, or future integrations.
- **Resting** means a Challenge participant is currently inactive for leaderboard grouping; saved check-ins and discussion posts remain visible, and eligible catch-up check-ins can clear missed-day resting.
- **Habit garden** is the participant's persistent Challenge growth visualization, separate from leaderboard scoring. Each category replays all saved answers in Challenge-day order, including practice, from a seedling vitality of `0`; Somewhat is neutral. Each Yes closes `2.5%` of the remaining distance to full growth, so early growth is visibly gradual and gains diminish. Each No retains `65%` of current vitality, so a mature plant loses much more absolute growth than a seedling and consecutive losses diminish. Later Yes answers can regrow the plant. A pending answer previews its projected vitality without replacing the accumulated history.

## Entry and upgrade payments

- A result submission that gives an existing Amateur their first eligible bortz age result is a Pro upgrade, not a free result update.
- Ordinary result updates and profile updates remain free. The server determines the submission class from the existing athlete and submitted clock data, then calculates the authoritative entry or upgrade price; browser payment state is only a handoff hint.

## Naming

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialized fields, external names, or quoted legacy data. Do not collapse clock, calculator, and result.

## Events

- Accepted-test Events appear only in the athlete's profile highlights when a dated biomarker result becomes public, including non-improvements, partial panels, and older backfilled tests. Each athlete and test date identifies one result across clocks; same-date corrections, additional markers, reordered records, reloads, and restarts do not create another Event. The Event date is when publication is first observed, and its text identifies the test date. Existing results are silently baselined when tracking is first introduced; subsequent results are remembered and their Events saved atomically, including results first observed at startup. These Events do not enter the shared highlights feed or social queues.
- Biological-age improvement Events represent chronologically new personal bests, use the result date as the Event date, and are not created when an older backfilled result predates the athlete's previous personal best.
- Pheno/Bortz best-improvement badges compare the latest eligible result with the first eligible result. Improvement leaderboards and their placement Events compare the latest eligible result with the worst eligible result.

## Longevitymaxxing Challenge

- Public community-call social announcements are social-only Custom Events queued about one hour before each selected call; they may include the public video call URL, but never participant access or stop links.
- Community-call reminder emails have their own opt-out; stopping them does not stop daily Challenge emails.
- Community-call reminder emails are sent only when the call starts from 07:00 through 20:59 in the participant's local timezone; calls starting from 21:00 through 06:59 receive no reminder email.
- The two most recent local check-in dates remain eligible. The oldest missed day inside the current 14-day leaderboard scoring window also remains eligible until saved, and the oldest due day is presented first so a newer check-in cannot strand it.
- A **Discussion thread** starts either from the optional public message and photos saved alongside a daily check-in, or from the system-generated welcome post created once when a participant first confirms their Challenge signup. The welcome post says that the participant joined the Challenge, is replyable immediately, and is not a check-in, score, public/social Event, or social post. A check-in's opening **Discussion post** remains limited to one editable post per participant per Challenge day, while each **Reply** is a separate message stored beneath that specific post. Saving a reply never creates or edits a check-in post.
- Discussion threads are ordered by a vote-free hot score: `log2(reply count + 1) - days since latest activity`. Post edits and new replies update latest activity; editing a reply preserves its original activity time and shows an edited marker. Ties use latest activity. Replies within a thread are displayed oldest first.
- Reply authors may edit or delete only their own replies. Editing reconciles still-pending mention activity with the saved text; deleting removes still-pending discussion activity tied to that reply. Already-delivered digest email cannot be recalled.
- Discussion post and reply authors use the same avatar priority as the Challenge leaderboard. When the participant is linked to a Longevity athlete, both the discussion avatar and name open that athlete profile.
- New replies accumulate for the post author, while new `@Display Name` mentions in posts or replies accumulate for the mentioned participant. Both are summarized only in that participant's next otherwise-eligible daily Challenge check-in email. Only the exact notifications included in a successfully sent daily email are marked delivered; a failed send leaves them pending, and stopped or auto-stopped Challenge emails cause no other delivery.
- Discussion posts and replies may mention confirmed participants with their unique public display name in the form `@Display Name`. Self-mentions and repeated saves do not enqueue activity, and explicitly mentioning the post author in a reply does not duplicate the reply notification they already receive. Displayed mentions are visually distinct, and only mentions with an attached Longevity athlete profile link to a profile.
- A discussion post or reply may mention at most five other participants. Discussion activity follows the existing Challenge reminder-email eligibility and opt-out.
