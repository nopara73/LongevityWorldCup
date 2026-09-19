# Ubiquitous Language

## Terms and Ranking

- **Longevity athlete**: approved participant with biological age data; **Applicant**: pre-approval.
- **Track**: Pro (eligible bortz age) or Amateur (non-bortz). **League**: ranking view. **Ultimate League** ranks Pro before Amateur.
- **Rank**: current computed order; **Placement**: stored/historical position.
- Leaderboard search preserves ranks and displayed score precision within the selected ranking view and league filters. Searching or limiting visible rows never renumbers the matching athletes or substitutes their Ultimate League ranks. Displayed scores and the existing one-decimal score form are searchable.
- **Biological Age Difference**: biological minus chronological age, lower is better; **Age Reduction** is its favorable public label. Compute with unrounded ages; round only for display.
- **Effective Age Reduction**: Ultimate League score, Bortz for Pro, otherwise pheno.
- **Pheno Age**, **Bortz Age**, and **Crowd Age** are distinct clocks/views. Calculator rank previews use only the selected clock's field. Pheno and Bortz competition ranks compare age reduction, not raw biological age. Profiles show Ultimate League rank beside the name when it is the best or tied-best ranking view. Only a strictly better global rank in another eligible view produces a separate row with Ultimate League and that view. Select the lowest global rank among eligible Bortz Age, Pheno Age, Bortz Improvement, Pheno Improvement, and Crowd Age views, using that order for equal ranks. Tracks, demographic leagues, flags, and pace-of-aging statistics do not compete for this second link. Default sharing uses Ultimate League rank.
- Albumin is capped at 54 g/L after unit conversion in both biological-age calculations and domain contributions. Preserve original stored/displayed lab values; higher values confer no scoring benefit.
- **Proof**: evidence for an athlete, profile, or result. **Profile picture**: public display image.
- **Event**: persisted public/social output; **Custom Event**: admin-created. **Badge**: computed award. **Social post**: copy for X, Threads, Facebook, Slack, or future integrations.

Use lowercase pheno age, bortz age, crowd age, age reduction, and effective age reduction in prose; reserve `PhenoAge`, `BortzAge`, and `CrowdAge` for code, serialization, external names, or quoted legacy data. Keep clock, calculator, and result distinct.

## Crowd Age

- **Crowd Count** counts accepted realistic guesses for the current image. Qualification requires at least 100 guesses; rank by `CrowdAge - chronologicalAge`, then higher Crowd Count, earlier date of birth, and name.
- Guesses belong to exact published image content via `ProfileImageId`. Byte-identical uploads restore that image's history; any changed bytes, including re-encoding, start with zero active guesses. Preserve older histories.
- Qualification, placements, and badges use current-image guesses. Image changes silently recompute placements; published Events remain historical. The raw crowd age badge and on-site BadgeAward Events remain visible but produce no social posts.
- In social placement copy, "younger" and "older" compare crowd age with chronological age.
- Crowd age top-10 entries and upward moves within the top 10 can produce placement Events, including moves such as 8th to 6th. Preserve one announcement per athlete/place and require the athlete's own accepted guesses to cause the change. Keep live ranks current after every accepted guess.
- Publish crowd age placement Events at least 24 hours apart per athlete. The first eligible announcement can publish immediately; during the cooldown, retain the strongest pending climb and its original movement context for the next announcement. Measure the cooldown from actual Event publication, not the time of the underlying guess or a calendar-day boundary. Pending announcements and publication times survive restarts; discard pending announcements if their profile image changes. Published Events remain historical. Platform-specific posting cooldowns can impose additional spacing.

## Acquisition Measurement

- An **AI-attributed visit** is a recorded statistics session identified by an allowlisted AI app referrer or an exact recognized first-touch source tag. Campaign evidence takes precedence. It is neither a verified human nor a crawl or citation; absent evidence remains direct/unknown.
- **Calculator use** means user input, change, or submit; **Application start** means user input or change on the new-application form. Keep these distinct from page views and calculator results, and expose the start of their measurement coverage.
- An **Application conversion** is a session with a server-confirmed successful full application, before athlete approval or payment. Exclude result/profile updates and unknown submission types from this rate. Count each converting session once; retain raw events separately. Window/filter denominators and first-touch semantics are documented in [AI referral reporting](LongevityWorldCup.Documentation/AiReferralReporting.md).

## Payments

An existing Amateur's first eligible bortz result uses Pro-upgrade pricing. Other result/profile updates are free. The server classifies submissions from existing athlete and submitted clock data and sets authoritative entry/upgrade pricing; browser payment state is only a handoff hint.

Manual application payment review uses reasonable confidence. Credible payment evidence is sufficient despite imperfect identifiers or unavailable corroboration; the user accepts occasional unpaid applicants passing this gate to avoid wrongly blocking honest ones. Record inferred matches honestly and stop checking once reasonably satisfied. This discretion clears the manual review gate without changing the provider's recorded payment state.

Application and Pro-upgrade payment detection is server-owned and survives a closed checkout browser. BTCPay `Settled` status or a positive `paidAmount` counts as paid, including partial payments. Persist that observation and notify the internal reviewer once per invoice; absence of the email does not establish nonpayment. An uncertain mail delivery requires reconciliation before replay. Newly tracked orders are reconciled automatically; historical invoices require an explicit recovery decision.

## Events and Improvement

- **Test date** is the laboratory measurement date. **First public announcement** is available only where an accepted-result Event records it; older untracked publication dates remain unknown. **Observed content change** is when the application detects changed public facts or definitions, distinct from response generation, deployment, and cache refresh. Missing values are unavailable, not zero. Current leaders and ranks are not completed-season winners or historical placements.

- Accepted-test Events appear only in athlete profile highlights, including partial, non-improving, and backfilled results. Identity is athlete plus test date across clocks; corrections, added markers, reordering, reloads, and restarts cannot duplicate them. Date the Event at first observed publication and identify the test date in its text. Silently baseline existing results when introducing tracking; atomically remember subsequent results and save Events, including startup discoveries. Exclude these Events from shared highlights and social queues.
- Biological-age improvement Events are chronologically new personal bests, dated to the result; older backfills predating the previous best create none.
- Pheno/Bortz best-improvement badges compare latest with first eligible result. Separate improvement leaderboards and placement Events rank `latest eligible age - worst eligible age` for that clock. Keep biological-age improvement, Crowd Age placement, and Pheno/Bortz Improvement placement Events distinct.
- Homepage highlights are curated: preserve fresh-Event athlete de-duplication, stale-event handling, and fourth-visit highlights-before-podium ordering.
- Search-engine change notifications follow public canonical content visibility. Profile-only Events can change the athlete profile without announcing a shared-highlight change. A notification of a removed public URL remains appropriate after it returns 404/410. Submission acceptance does not establish indexing.

## Longevitymaxxing Challenge

- Challenge scoring is separate from Ultimate League, biological-age placements, and athlete badges. Signup and daily check-ins continue indefinitely after Day 14 on the same global leaderboard; eligibility starts at local signup date.
- **Ongoing Challenge rank**: one plus the number of participants with strictly more points, so equal scores share ranks (1, 1, 3). Secondary ordering rules only position rows within a tie. Ranks use the entire leaderboard and do not change when Resting entries are shown or hidden. The one-time original challenge awards retain their published ordinal placements.
- **Full marks**: all 14 closed days in the shared scoring window have check-ins and their points reach the global maximum for those dates. This includes the existing allowance for a qualifying slip after an actually perfect previous day. A shorter personal participation period or an incomplete window does not earn full marks.
- **Closed reporting day**: a habit date closes at 12:00 UTC two calendar dates later, after every timezone has had the full following local day to report. The leaderboard scores the latest 14 closed Challenge days. Points, checked-in-day and streak tie-breaks, latest scored check-in, category badges, and Resting grouping all use that shared cutoff. Open check-ins remain visible but cannot change standings. Eligible late catch-ups can revise closed days; reporting closure does not lock submissions. Personal habit gardens still update immediately.
- The first eligible check-in is practice: it counts toward checked-in days/streak, never habit points, category badges, point tie-breaks, or missed-scored-day reminder stops. Daily reminders continue until three consecutive missed scored days, excluding practice and pre-signup days.
- Allow the two latest local check-in dates, plus the oldest missed eligible day from the start of the current 14-day scoring window until saved, including open reporting days. Present the oldest due day first.
- A repeated check-in submission returns the current state without attaching photos again or overwriting a later edit. Retries of an accepted submission remain valid after its catch-up day closes; a new submission must meet the usual eligibility rules.
- **Resting**: inactive leaderboard grouping; retain check-ins/discussions. Eligible catch-up check-ins can clear missed-day resting.
- Avatars prioritize linked Longevity athlete pictures over challenge-only uploads and Gravatar fallbacks. Linked discussion avatars/names open the athlete profile.
- **Habit garden**: persistent visualization, independent of scoring. Replay all saved answers in day order, including practice, from vitality `0`: Somewhat is neutral; Yes closes `2.5%` of remaining growth; No retains `65%` of vitality. Later Yes answers regrow; pending answers preview without replacing history.
- Community-call announcements are social-only Custom Events queued about an hour before selected calls. Public call URLs are allowed; participant access/stop links are not. Call emails have a separate opt-out and require a local start time of 07:00–20:59; stopping them leaves daily emails enabled.

## Challenge Discussions

- A **Discussion thread** begins with optional check-in text/photos or a welcome post created once at first signup confirmation. Welcome posts announce joining and allow replies immediately; they are not check-ins, scores, Events, or social posts.
- Check-in opening **Discussion posts** allow one editable post per participant/day. Each **Reply** belongs beneath a specific post and never creates/edits a check-in post.
- Opening discussion text and replies allow up to 240 characters after trimming outer whitespace. Reject excess text without shortening it or saving a partial message.
- New replies may include up to four photos, with optional text. Photo replies use the same image preparation and size limits as check-in photos, remain independent of check-ins, and appear in every public thread and reply view. Text edits retain attachments; deleting the reply removes its photos. Repeated submissions must match the ordered photo contents as well as the text and source reference.
- Order threads by `log2(reply count + 1) - 4 × days since latest activity`, then latest activity; no votes. Seven replies offset 18 hours of inactivity, so active conversations get a boost without crowding out fresh daily posts for days. Post edits/new replies advance activity. Reply edits preserve activity time and show an edited marker. Show replies oldest first.
- Authors edit/delete only their own replies. Edits reconcile pending mentions; deletion removes pending activity for that reply. Delivered emails cannot be recalled.
- Public discussion permalinks identify the opening check-in or welcome post and optionally a reply. They remain readable after the thread leaves the main feed, subject to the same confirmed-author and public-history limits as the public discussion. A post timestamp reflects its latest discussion edit; a reply timestamp reflects creation, with edit time shown separately.
- Replies notify the post author; new `@Display Name` mentions notify confirmed participants with unique public names. Self-mentions/repeated saves enqueue nothing; mentioning the post author in a reply adds no duplicate notification. Allow at most five other participants per post/reply. Style mentions distinctly; link only those with athlete profiles.
- Replying to a commenter adds their unique public mention to a reply in the same discussion thread. It does not create a nested thread or change the opening post. If the participant has no unique public name, or the draft has reached its mention or character capacity, retain the draft and explain why the mention could not be added.
- A reply can reference an existing confirmed-author comment in its own thread. Show the source's current public name and text for context; deleting the source preserves the response and marks its context unavailable. The reference does not change chronology or notification rules. Repeated submissions must match both the text and the source reference.
- Deliver accumulated discussion activity only in the next otherwise-eligible daily check-in email, respecting reminder eligibility/opt-out. Mark only included notifications delivered after successful sending; failures leave them pending, and stopped/auto-stopped emails cause no alternative delivery.
