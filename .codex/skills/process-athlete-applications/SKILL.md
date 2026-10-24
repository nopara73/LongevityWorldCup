---
name: process-athlete-applications
description: Review LWC athlete application, result, and profile-update emails; verify existing-athlete requests, inspect proofs, and prepare replies while preserving unread state and exact Gmail threading. Process all unprocessed submissions unless a target or limit is specified. Finalize accepted changes and send replies only after explicit approval.
---

# Process Athlete Submissions

## Operating Invariants

- Read `UBIQUITOUS_LANGUAGE.md`. Use Gmail connector tools for discovery, history, metadata, and draft scaffolds; discover unloaded tools before browser workarounds. Use the Codex in-app Browser for send-as selection and the bundled raw Gmail helper for ZIPs.
- Record the current submission's Gmail `thread_id` as `expectedThreadId`. Saved-draft, send-result, and sent-message thread IDs must each equal it exactly. Recipient, subject, and `SENT` labels are insufficient. Never fall back to Compose, an unthreaded draft, or historical correspondence as the reply destination. Stop on missing/mismatched thread IDs; after a send, report the incident without claiming success or retrying without fresh approval.
- Keep new or unapproved current submission messages `UNREAD` through review and approval. After an explicitly approved reply is sent and its exact thread is verified, mark only its automated LWC submission notice read if the reply is not answering an athlete-authored message. Actual athlete correspondence, uncertain message types, and failed/unverified sends stay `UNREAD`; verify/restore that state. Preserve previously approved read states of unchanged automated notices, including ledger-only reassessments. These are the only permitted label mutations; never archive, trash, remove other labels, or change related-history unread state.
- Never send, commit, or push without explicit approval of the prepared summary and draft. Preserve unrelated work and stage only accepted changes.
- Keep temporary outputs and the private ledger in `.artifacts/`. Before any approval stop or blocked new-application handoff, move its untracked athlete folder to `.artifacts/pending-athlete-reviews/{folder_key}/` and verify it is absent from `wwwroot/athletes/`. Existing tracked athlete folders stay in place.

## Discover the Queue

Default to all unread, unprocessed submission candidates, plus the latest ledger cases needing a proof-first backfill or payment-only reassessment below, even if their automated notice is now read. Finish discovery before processing: page every query family through `next_page_token`, union message/thread IDs, and group by athlete/folder key. Then work candidates individually. `next` selects the newest eligible unread thread with a ZIP or payment/audit pair; an explicit athlete/thread/message/folder target overrides ledger skips and permits historical inspection.

Search `is:unread -in:spam -in:trash` with each family:

- Audit terms: `[LWC26]`, `Archive folder key`, `Payment due`, `Submitted biomarkers/results summary`.
- Update terms: `New biological age result received`, `Update profile request`, plus legacy `New biological age result posted` and `Profile update received`.
- `filename:zip`.
- LWC identity terms: `longevityworldcup`, `longevityworldcup.com`.

Personal threads may omit every LWC sender/domain. Use ZIP folder-key names, audit text, names, profile slugs/URLs, contact addresses, handles, sites, invoice/submission IDs, and alternate spellings as signals. Read enough of every unique candidate to capture name, folder key/profile URL, submission type, message/thread IDs and timestamp, attachment filename, payment due/confirmation, and biomarker/proof counts. Keep distinct identities in scope; report ambiguity.

No matching unread messages and no eligible ledger reassessments means no unprocessed athletes; do not otherwise fall back to read messages. Read history only for discovered/ledger candidates or explicitly named historical work. If one candidate requires human direction, continue the other candidates within the requested scope.

## Ledger and Related History

Before heavy work, check `.artifacts/lwc-submission-processing-ledger.jsonl`; never commit it. Use the latest entry per athlete/thread. Skip only general next/all candidates whose thread and latest message ID/date prove unchanged since review. Missing, malformed, or ambiguous entries require inspection. New messages, ZIPs, payment/context emails, or an explicit named request require reprocessing. Report skips briefly and continue.

Prior payment-blocked/unverified cases need one complete proof review if that was previously skipped. Once proofs otherwise support acceptance, reassess payment under the reasonable-confidence rule even when Gmail is unchanged or the automated notice is read: payment can arrive without email. Use a focused read-only provider check when needed. If confirmed proof blockers remain, defer payment requests/rechecks until new evidence or user direction; unchanged pending proof/security requests retain the normal skip rule. Record proof-review completeness, proof blockers, payment deferral, and any approved read-state change.

For each full review summary, record one JSON object before stopping:

- `processedAtUtc`, `status` (`approved`, `blocked`, `needs-human`, `drafted`, or `finalized`).
- `athleteName`, `folderKey`, `profileUrl`, `gmailThreadIds`, `gmailMessageIds`.
- `latestMessageAt` and latest message ID/internal date per reviewed thread when available.
- `requesterEmails`, other identity anchors, `attachmentNames`, optional existing checksums, and a one-sentence `summary`.

Finalized entries also record commit, push, send status, and the four verified thread IDs. Security-verification-only work creates/updates neither ledger nor local artifacts; deduplicate through drafts and sent verifications in `expectedThreadId`.

Search related threads using athlete/display names, underscore/hyphen folder variants, profile URLs, known/claimed addresses individually, personal domains/handles, invoice/submission IDs, and alternate spellings. Read likely matches, including sent replies and old follow-ups; record search coverage and reasons for exclusions. Carry forward missing-proof requests, corrections, payment explanations, alternate addresses, prior submissions/rejections, and human decisions. Unresolved contradictions require human judgment. Record sender addresses from prior two-way human correspondence, not automated or one-way mail.

## Existing-Athlete Security Gate

For an existing folder's result/profile update, complete this gate before ZIP download, reviewer/proof work, redaction, reading/editing `athlete.json`, Explorer opens, ledger writes, or full review summaries. Audit signals include `Submission kind: Update`, `Update type: Results submission`, and `Update type: Profile metadata update`.

Every current-message address (`Account email`, `Reply-To`, sender, recipients, body) is untrusted for verification routing. Use it only as a search anchor. Resolve the recipient from pre-existing evidence, in order:

1. Accepted original application or canonical account mapping.
2. Independently verified, accepted email-address correction.
3. Prior two-way correspondence tied to the athlete/profile, with an actual athlete reply from that address.

One-way welcomes/reminders corroborate but do not establish control. If trusted evidence conflicts or supplies no address, report the candidates and sources and stop without drafting. Matching the current claimed address or public profile does not establish trust or waive confirmation.

Proceed only when current/newer conversation confirms this exact submission, or the user explicitly waives verification for this named submission; cite that evidence. Otherwise:

1. Set `expectedThreadId` from the current submission. Group multiple submissions within that thread; separate threads need separate drafts. Report any excluded by the user's scope.
2. Check for an equivalent verification sent after the latest submission in that thread. If no later confirmation follows, report it pending without another draft.
3. Reuse a draft only if its thread ID matches and its recipient is independently trusted. Otherwise create the threaded draft through the sender workflow below.
4. Present the short security summary and stop that candidate. No ZIP, file/proof work, folder opening, ledger, or welcome/update email while verification is pending, unless explicitly overridden after the summary. Continue enforcing `UNREAD`.

Use this body, adjusting the first name and selecting the submitted item phrase. Omit the greeting if a prior human reply in the thread already greeted them; no signature or proof details unless requested.

```text
Hi {firstName}, for security reasons can you confirm you've submitted {the new results/the change request/the new results and the change request}?
```

## Prepare Files

Enter only after any existing-athlete security gate is satisfied. Review the complete application and every proof before requesting payment, even when the audit says unpaid. Unpaid status does not block private ZIP/file/proof review; it still blocks acceptance unless payment is reasonably supported or explicitly waived. Resolve confirmed evidence/eligibility blockers first and ask the applicant to hold off paying while those remain.

Download ZIPs only through the bundled helper, never Gmail `read_attachment`, Chrome, Computer Use, web attachment controls/URLs, or browser fetch/XHR/downloads. Unsupported ZIP responses and oversized MIME output are expected connector limitations, not fallback authorization. Diagnose/fix the helper path; if still blocked, report it. Manual download requires the user to choose that fallback.

Save ZIPs under `LongevityWorldCup.Website/wwwroot/athletes/`. Inspect same-named ZIPs/folders before replacement; never overwrite unrelated work. For a returning new applicant, restore its pending folder only if no tracked or unrelated destination exists.

Read the parent message with the connector for the message ID and exact filename. Prefer `--filename`; use `--attachment-id` only if filenames are missing/ambiguous because attachment IDs may change between reads. From the solution root:

```powershell
node .\.codex\skills\process-athlete-applications\scripts\download-gmail-attachment-raw.mjs --message-id {gmail_message_id} --filename "{attachment_filename}" --out .\LongevityWorldCup.Website\wwwroot\athletes\
```

Add `--thread-id <codex-thread-id>` if inference fails. The helper starts temporary `codex app-server`, requests Gmail `read_email` with `include_raw_mime=true`, writes attachment bytes locally, and reports `savedPath`, `filename`, `mimeType`, `size`, and `sha256`. Require `application/zip`, plausible size, and a locally readable ZIP.

```powershell
dotnet run --project .\LongevityWorldCup.ApplicationReviewer\LongevityWorldCup.ApplicationReviewer.csproj
```

The reviewer scans every `*.zip` in the athletes directory, extracts/merges folders, deletes ZIPs, starts `https://localhost:7080` if needed, and opens Chrome incognito and Explorer. Build relevant projects if needed and retry. Identify changed folders against the email key, ZIP name, and `git status`. After review/validation, move unapproved new folders back to the pending path and recheck status.

## JSON and Proofs

Before redacting an existing athlete's new proofs, check their prior privacy requests, private ledger, and published censored proofs. Carry forward the same user-approved stricter privacy scope unless explicitly changed; never revert to the ordinary defaults below. An approved athlete-specific exception may hide DOB, provider metadata, or non-required clinical rows while retaining exact competition evidence. Record what remains visible and what is masked in the private ledger for future submissions. Legacy redactions alone do not establish a new privacy preference; without an explicit approved exception, retain all clinical content as required below.

Parse `athlete.json`; compare email audit fields and visible proofs. Check identity/profile metadata (name/display name, division, flag, personal link, media contact, Why), plausible DOB/test chronology, numeric biomarkers in expected units, profile-image filename matching the folder key, and present `proof_*.ext` evidence for results. Append new records without replacing unrelated history.

Before approval/publication, review `Name` and `DisplayName` casing. Correct accidental all-caps personal names in the canonical JSON fields, using natural casing supported by the application and human correspondence; do not merely hide the problem with CSS or a display-name override. Preserve deliberate handles/acronyms, already-correct casing, diacritics, initials, apostrophes, hyphens, and name particles. Never blindly title-case every name or invent spelling/accents. Keep folder keys, profile slugs, asset filenames, contact addresses, source proofs, and quoted correspondence unchanged. Record the before/after name in the private review and approval summary; after publication verify that the profile, roster, and highlights resolve the corrected name.

A plausible-year December 31 DOB is an allowed privacy placeholder; never replace it with the exact DOB on a proof. Full applications require DOB. Use these sources when uncertain:

- `LongevityWorldCup.Website/Business/ApplicantData.cs`: submitted fields.
- `LongevityWorldCup.Website/Controllers/ApplicationController.cs`: audit/ZIP generation.
- `LongevityWorldCup.Website/Tools/PhenoStatsCalculator.cs`: clock requirements.
- `LongevityWorldCup.Documentation/Ruleset.md`: competition rules.

Visually inspect every proof image/PDF page; OCR is supplementary. For each record:

- Verify ownership, collection/test date, each value/unit, and required biomarkers against coherent same-day evidence. Required markers may be documented across reports from that day; do not impose a same-blood-draw requirement or combine different test dates or unrelated evidence. Convert units only when the source unit is clear.
- Correct unambiguous clerical errors locally (collection versus report date, mistyped value); disclose substantive corrections. Keep tiny conversion/rounding adjustments in the private review, not athlete emails. Uncertain values, units, or medical interpretation need human judgment.
- For limit-qualified results, read and apply the current detection-limit section of `LongevityWorldCup.Documentation/Ruleset.md`. Use the LoD only when the lab confirms the sample's result was below it. For a result reported below the LoQ, use the LoQ unless the lab also confirms the result was below the LoD. Both limits must apply to the assay used for that sample. Accept a numerical result instead only when the lab validates it as quantitatively reliable; an unvalidated raw instrument reading is not sufficient.
- Do not substitute a lower limit based only on generic instrument specifications, another test, historical results, or a better projected rank. A `<0.1 mg/L` report alone does not justify using an LoD of `0.06 mg/L`. Do not interpret `0` or `<0` as a measured zero or as confirmation that the sample was below a particular LoD. If CRP's applicable limit is unknown, retain the competition default `1 mg/L`. A usable stated limit remains valid (CRP `<0.5 mg/L` becomes `"CrpMgL": 0.5`); do not reject/remove a result solely because of its qualifier.
- Record the original result/qualifier and units, supporting report or lab confirmation, chosen scoring input, and whether it is a validated numerical result, LoD substitution, LoQ substitution, or CRP default in the private review. Substituted limits are scoring inputs, not measured concentrations. Disclose changed submission values and their basis in the approval summary and substantive correction reply; keep the normal approval gates.
- Retain every complete submitted report page and all clinical content, including unused analytes, units/ranges, flags, comments, diagnoses/referrals, and specimen/report dates. Public proof is not a competition-marker excerpt.
- Crop screenshots to the report-page boundaries before redaction. Remove OS/browser/viewer chrome, filenames, toolbars, external canvas, and duplicated partial adjacent pages while preserving readable report headers/footers, page numbers, provenance, dates, and all clinical rows. Floating controls over blank margins or generic footer boilerplate may be removed with a small opaque cleanup mask. Never reconstruct covered text or let cleanup erase a result, unit, reference range, identity/date evidence, or patient-specific clinical comment.
- Treat cosmetic screenshot defects as editing work, not missing evidence. Do not block acceptance or request an original PDF solely because of phone UI, floating buttons, imperfect margins, or an obscured generic footer when the coherent report still verifies ownership, collection date, and every required value/unit and all visible clinical content can be preserved. Request replacement proof only for a specific unreadable/missing piece of clinical or verification evidence, or a privacy problem that cannot be safely cleaned; name that actual gap in the review and draft.
- Redact private identifiers and unique administrative tokens for patients, clinicians, labs, providers, facilities, insurers, and other organizations: personal phone/email/home address; client/patient/medical-record/health/insurance/member IDs; license/registry/provider/facility/organization/staff codes; order/accession/specimen/report/account/payment numbers; encoding barcodes/QR codes.
- Preserve the applicant's name, age, sex, clinical content, human-readable lab/provider names and logos, clinician/referrer/validator/technician/signer names/titles, facility addresses/contacts, doctor phone numbers, and renderer/footer metadata. Provider registry/code identifiers still require redaction.
- With a non-December-31 JSON DOB, leave the full proof DOB visible. With the `12/31` placeholder, redact only proof DOB month/day everywhere, retaining the birth year and all other evidence.
- Inspect every final page at readable size for remaining viewer UI, partial neighboring pages, and text fragments at redaction edges. For cropped/re-encoded proofs, compare decoded pixels with the corresponding source crop and require zero changes outside the intended privacy-redaction and localized nonclinical viewer-UI cleanup regions.

Pheno age requires one record containing:

`AlbGL`, `CreatUmolL`, `GluMmolL`, `CrpMgL`, `Wbc1000cellsuL`, `LymPc`, `McvFL`, `RdwPc`, `AlpUL`.

Bortz age requires one record containing:

`AlbGL`, `AlpUL`, `UreaMmolL`, `CholesterolMmolL`, `CreatUmolL`, `CystatinCMgL`, `Hba1cMmolMol`, `CrpMgL`, `GgtUL`, `Rbc10e12L`, `McvFL`, `RdwPc`, `MonocytePc`, `NeutrophilPc`, `LymPc`, `AltUL`, `ShbgNmolL`, `VitaminDNmolL`, `GluMmolL`, `MchPg`, `ApoA1GL`.

`MonocytePc` and `NeutrophilPc` are stored percentages; the site derives counts from WBC.

Block and draft a specific reply for missing/irreconcilable evidence, mixed tests, unsafe redaction, wrong/unexpected folders, unverifiable ownership, or unresolved history contradictions. Defer payment requests until the proof/data review otherwise supports acceptance. Missing required history/ledger/security checks must be resolved before continuing; named submissions may bypass ledger skips. If processing occurred before security confirmation, report it and stop. Keep uncertain secondary findings in the human summary.

## Review Payment After Proofs

Only after the proof/data review otherwise supports acceptance, assess payment with reasonable confidence, not certainty: the user accepts occasional unpaid applicants slipping through rather than wrongly blocking honest ones. A credible receipt or payment confirmation, including an applicant's confirmation with consistent supporting context, or a provider payment reasonably matching the applicant and amount/time is sufficient. Exact invoice/order/submission-ID linkage and corroboration from both BTCPay and Stripe are helpful, not mandatory. Missing Gmail confirmation, imperfect metadata, or a different payer email alone must not block acceptance when payment is reasonably supported. Stop checking once reasonably satisfied and continue without a payment-chasing draft. Record evidence and inferred matches honestly. Apply the Payments policy in `UBIQUITOUS_LANGUAGE.md`, including qualifying partial payments.

When existing evidence is insufficient, make a focused read-only BTCPay check, using Stripe or server records only as needed. If reasonably confident the fee remains unpaid, prepare a contextual payment-support draft after the completed review, not instead of it. If access fails, prominently report the failed check and missing access, but proceed when other credible evidence reasonably supports payment. Seek a concise human decision only for substantive contradictions or when no reasonable basis for accepting payment remains; do not pursue exhaustive fraud checks merely to eliminate residual doubt. Payment review does not authorize notification replay, refunds, provider-state changes, or unapproved replies.

## Choose and Save the Draft Sender

Explicit user sender instructions override this hierarchy:

1. Reply from the address directly receiving the latest athlete/requester-authored inbound message when it is `hi@longevityworldcup.com`, `adam.ficsor73@gmail.com`, or `adam@longevityworldcup.com`.
2. Otherwise reuse the sender from the most recent two-way human exchange with this requester among those addresses. One-way/automated mail establishes no route.
3. Default to `adam@longevityworldcup.com`.

For ordinary/full-application replies, prefer requester `Reply-To` or `Account email`; security recipients must come from the trusted-history gate.

1. Select a current `anchorMessageId` in `expectedThreadId`. Check active drafts; for security drafts, also recheck exact-submission confirmation and pending sent verification.
2. Create the connector scaffold with `reply_message_id: anchorMessageId`; verify the returned thread ID immediately. Stop on failure/mismatch, without Compose or another thread as fallback.
3. Open that exact draft from Gmail Drafts in the Codex in-app Browser under `adam.ficsor73@gmail.com`. Identify the displayed account email, not a fixed `/u/0` or `/u/1`; do not open the source Inbox message or create a browser replacement draft.
4. Expand headers, select `From`, and confirm recipient/subject/body. Save and close without sending.
5. Verify `From` and exact thread ID using `list_drafts` or underlying message metadata. Stop if the profile/send-as route is unavailable, sender is wrong, or thread identity cannot be proved; do not present an incorrect draft as ready.
6. Verify each current submission's read state under the Operating Invariants, restoring `UNREAD` where required, and report both expected and saved draft thread IDs.

## Reply Content

Continue the latest direct exchange in a warm, concise human voice. The conversation and user-approved wording take precedence over a generic welcome template. Match demonstrated sender style from actual correspondence (brevity, punctuation, emoji, headings, calls to action). Avoid stacked prose praise; repeated completion emoji may fit established style. Greet only in the first direct human reply in a thread. Acknowledge relevant supplied proof/corrections, not unrelated side topics. Before saving, compare the reply with the latest inbound and prior human replies, including related threads; remove repeated introductions, already-covered requests, and unrelated promotional copy.

For simple informational or pre-application questions, answer only the question and relevant eligibility condition. Do not turn them into an application review by adding document requests, missing-attachment commentary, payment instructions, or other next steps unless explicitly requested or necessary for an actual active submission. A general inquiry without an attachment is not a proof-blocked application.

Do not use em dashes in newly written replies. Use periods or commas instead; preserve original quoted correspondence unchanged.

Language priority: explicit user/athlete instruction; otherwise a clearly non-English `Why` field; otherwise latest direct exchange; otherwise English. Localize the entire reply, retaining names, URLs, biomarker labels, and exact values. Lab-report language alone does not establish preference.

For new applications blocked by missing evidence, introduce Adam on first contact and ask only about the confirmed blocker. If unpaid, tell them to hold off paying until the evidence can be verified; if already paid and the evidence may not exist, offer a refund. Request only the missing markers, same-day evidence, or safe proof needed, not a premature payment. Use the Ruleset's same-day wording in replies, not a stricter same-blood-draw requirement.

An introduction belongs only on first direct human contact when needed, not automatically at acceptance. Never repeat Adam's name/founder introduction after requested corrections or when prior correspondence has already introduced him. Do not restart a welcome script because the application has become acceptable.

For accepted applications, lead with a brief, warm confirmation suited to the preceding exchange, then the new profile link. Follow with substantive review changes when relevant. Invite corrections only when useful; do not pad the reply with branding or generic praise.

When review substantively corrects athlete-submitted information, include `Changes I made during the review:` with one short bullet per substantive change, showing submitted versus stored values/units where relevant. Place it after the warm confirmation and any useful profile link, not as an abrupt standalone opening. Never mention tiny unit-conversion or rounding differences in athlete emails (for example, 71.99 → 72 U/L or 35.39 → 35.4 U/L); record them only in the private review/ledger. Omit the section when no substantive corrections occurred. Exclude routine processing, redaction, filenames, internal details, and normal result additions; never invent changes.

Existing-athlete replies normally omit repeated greeting, generic profile-updated text, unchanged/known URL, founder introduction, Slack invitation, signature, and unnecessary calls to action. Include links only for new/changed profiles, when requested, or when useful to resolve the request. With no correction/action, a brief sender-appropriate completion line is sufficient.

For example, only when these are the actual correction and demonstrated voice:

```text
🚀🚀🚀

Changes I made during the review:

- Your MCV was submitted as 84.7 fL, but the proof shows 86.7 fL, so I stored 86.7 fL.
```

Do not automatically append Slack/community invitations or other promotional copy to acceptance replies. Include a community invitation only when the user explicitly asks or it directly answers the athlete's question. Remove sentences adding neither relevant information/action nor demonstrated human voice.

## Approval Summary

Before sending/committing/pushing, present the decision and exact proposed actions with:

Always include the relevant athlete-authored inbound email verbatim, with sender and date, clearly separated from the proposed reply. Show each relevant new message when several inform the reply; a summary alone is not enough. This applies to follow-ups and security-only handoffs too. If only an automated submission notice exists, label it as automated and quote the submitted request/changed fields instead of inventing personal correspondence.

- Athlete/profile and actual folder path (underscore folder keys, hyphenated URL slugs); new applicants stay under `.artifacts/pending-athlete-reviews/`.
- Current message ID, `expectedThreadId`, saved draft thread ID, explicit equality, draft text/ID, verified sender, unread status/restorations.
- Related-history search/evidence, ledger/new-message status, security confirmation/override, payment evidence, and blockers.
- Changed files from `git status --short`; identity/DOB, record dates and available clocks; per-record proof/date/value/same-test/privacy checks and missing evidence.

Open the actual athlete/pending folder in Explorer if not already open before a full summary. Write the full-review ledger entry before stopping.

For security-only work, instead give a short summary: athlete/profile, message and thread IDs, draft ID/text, trusted recipient and its pre-existing source, whether the update claimed that address, selected sender and reason, pending/confirmation status, and verified unread/restorations. Confirm no ZIP download, local file inspection/change, or ledger write. No Explorer, JSON highlights, or proof checklist.

## Finalize After Explicit Approval

Security-only approval authorizes the verified security email, not ZIP/reviewer work, athlete edits, Git operations, or ledger writes. Recheck exact-submission confirmation and same-thread pending verifications before sending; a newly confirmed submission needs no verification email, and pending verification needs no duplicate.

For approved accepted changes:

1. Restore a new applicant's reviewed pending folder only into an absent destination; verify the restored files match the review.
2. Recheck `git status`, stage only accepted files/supporting changes, and commit with a short message.
3. Push `origin master` only from `master` with approval to push; ask before switching/pushing from another branch.

For either email branch:

1. Re-read the current submission anchor and approved draft. Require its thread ID to equal `expectedThreadId`; recheck `From`, recipient, subject, and body before sending.
2. Send and verify both send-result and sent-message thread IDs against `expectedThreadId`. On mismatch, report the incident and stop without retrying or claiming success.
3. Apply the Operating Invariants read-state rule: only a verified, explicitly approved send that is not answering athlete-authored mail permits marking its exact automated submission notice read. Otherwise verify/restore current submissions' `UNREAD`. Report all four thread IDs, sender, send status, and verified read/unread state or restorations.
4. For accepted changes, append the finalized ledger entry and report commit, pushed branch, and profile URL. For security-only sending, confirm no repository/ledger change and that processing remains pending athlete confirmation.
