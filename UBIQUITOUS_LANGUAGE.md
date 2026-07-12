# Ubiquitous Language

## Terms

- **Longevity athlete**: approved participant with biological age data; **Applicant** is pre-approval.
- **OpenData profile**: an unranked reference profile for a public-facing person, transcribed from linked bloodwork the subject intentionally published. The subject did not apply to or join the Longevity World Cup and is not a Longevity athlete.
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
- Challenge pledge is optional and is not collected on signup. Participants can set a pledge from the participant menu whenever profile editing is available. If no pledge is set, the check-in flow may prompt for one only when commitment enforcement is about to become relevant; no pledge means the participant continues normally and no Challenge commitment payment can trigger.

## Naming

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose. Keep `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialized fields, external names, or quoted legacy data. Do not collapse clock, calculator, and result.

## OpenData profiles

- OpenData subjects are editorially curated public-interest figures with an established public body of work. Eligibility requires an identifiable first-party publication, not a leak, scraped patient portal, or third-party repost. Inclusion is neither participation nor endorsement, and the UI must keep these profiles in a clearly labeled, neutral, unranked section with source provenance and a correction/removal path.
- OpenData profiles never receive ranks, placements, badges, prizes, crowd age guesses, athlete-count credit, or competition Events, and never affect those outcomes for Longevity athletes.
- OpenData profiles may comprise at most 10% of all profiles shown with the leaderboard. Their normalized slug and every identity name (primary name plus documented public aliases) must not match any approved athlete slug, name, or display name, or any other OpenData slug or identity. Aliases exist for identity isolation and de-duplication, not as separate subjects.
- OpenData profiles do not republish exact dates of birth or persist athlete-only/service-controlled fields at any JSON depth. Every biomarker record carries the subject's finite age at that draw with no more than two decimal places, contains all nine pheno age inputs in canonical units, passes deliberately broad unit/transcription guardrails, produces a finite reference pheno age, and cites bloodwork published by the subject. When a source publishes only the draw month, the record uses explicit month precision and the canonical first of that month; the UI must not present that canonical value as an inferred day. Profile identity separately cites at least one source published by the subject.

## Events

- Biological-age improvement Events represent chronologically new personal bests, use the result date as the Event date, and are not created when an older backfilled result predates the athlete's previous personal best.

## Longevitymaxxing Challenge

- Public community-call social announcements are social-only Custom Events queued about one hour before each selected call; they may include the public video call URL, but never participant access or stop links.
