---
name: process-athlete-applications
description: Review and process Longevity World Cup athlete submission emails from unread Gmail messages, including full applications, biological-age result uploads, and profile-update requests. Use when Codex needs to find LWC submission emails, process all unprocessed athlete submissions when invoked without a named target, security-gate existing athlete updates with a draft-only fast path before ZIP processing, understand related email history when needed, download ZIP attachments through the bundled raw Gmail helper only when processing is allowed, run LongevityWorldCup.ApplicationReviewer, inspect athlete.json and proofs, prepare concise requester or athlete security-verification drafts, summarize the human approval decision, and after explicit approval commit/push accepted athlete changes and send the welcome or update email.
---

# Process Athlete Submissions

## Operating Rules

- Read `UBIQUITOUS_LANGUAGE.md` before judging applications, results, rankings, athlete onboarding, badges, Events, or competition copy.
- Use Gmail connector tools when available. If Gmail tools are not loaded, search for them with `tool_search` before using browser workarounds.
- For Gmail ZIP attachments, do not use Chrome, Computer Use, the Gmail web UI, visible attachment controls, attachment URLs, or browser downloads. Use the raw Gmail attachment downloader bundled in this skill. If that path fails, diagnose or fix that path and stop with the blocker; do not invent a browser fallback.
- When invoked without additional instructions, process all unprocessed athlete submissions by scanning unread Gmail submission candidates until none remain or human direction is required.
- Never mark Gmail submission messages read, remove `UNREAD`, add `UNREAD`, archive, trash, or otherwise change Gmail labels/state. The user marks submission emails manually. Reading/searching messages and creating draft replies is allowed; label mutation is not.
- Keep temporary downloads, OCR output, screenshots, notes, and the processing ledger under `.artifacts/` unless the ZIP must be placed in the athlete folder for the reviewer.
- Do not create or update `.artifacts/lwc-submission-processing-ledger.jsonl` for security-verification-only drafts. Use active Gmail drafts in the submission thread to avoid duplicate security drafts.
- Never send email, commit, or push until the user explicitly approves the prepared summary and draft.
- Never stage unrelated work. If the worktree is dirty, identify unrelated changes and leave them alone.
- Do not add Node tooling for browser checks; this repo is a .NET solution.

## Default Submission Scope

If the user invokes this skill or asks to process submissions without naming a specific athlete, thread, message, folder key, count, or `next` limit, process every unread Gmail submission candidate that appears unprocessed.

Before processing any candidate, complete the unread candidate sweep and group all unique candidate messages by athlete/folder key. Then work candidates one at a time. Check the processed ledger before heavy work for each candidate, skip unchanged processed threads only when thread/message identity proves no newer unread message exists, and continue until no unread unprocessed candidates remain.

When the user asks for `next`, process only the next eligible candidate. When the user names a specific athlete, thread, message, or folder key, process that target even if the ledger suggests it may already be processed.

For existing-athlete result submissions or profile metadata updates, perform the security-verification gate before ZIP download, reviewer runs, proof review, redaction, local athlete-file edits, ledger writes, folder opens, or full review summaries. If the exact current update is not already confirmed by the athlete/trusted requester, draft the short security confirmation and stop that candidate. Continue to the next unread candidate when the requested scope includes more candidates.

## Find The Submission Email

Search unread Gmail messages flexibly. Do not depend on one exact sender, recipient, domain, or subject because applicant and athlete conversations may happen through personal email threads that do not touch `longevityworldcup@gmail.com` or any `@longevityworldcup.com` address.

Treat unread Gmail messages matching LWC submission signals as the only unprocessed athlete queue. When there are no unread Gmail messages matching LWC submission signals, report that there are no unprocessed athletes and do not fall back to read messages. Use read messages only as related history after an unread candidate has been found, or when the user explicitly names a specific athlete, thread, message, or folder key for reprocessing or historical inspection.

Do not process the first match before finishing discovery. Run a complete unread sweep across these query families, paging through results when Gmail returns `next_page_token`, then union by message id/thread id:

- `is:unread -in:spam -in:trash` with audit terms such as `[LWC26]`, `Archive folder key`, `Payment due`, and `Submitted biomarkers/results summary`
- `is:unread -in:spam -in:trash` with update terms such as `New biological age result posted` and `Update profile request`
- `is:unread -in:spam -in:trash filename:zip`
- `is:unread -in:spam -in:trash` with LWC identity terms such as `longevityworldcup` and `longevityworldcup.com`

Read enough metadata/body for every unique unread candidate to extract athlete name, folder key, profile URL, update type, attachment filename, message id, thread id, and timestamp. If multiple identities are present, keep all of them in the queue; do not stop after one athlete or one thread.

Strong signals:

- ZIP attachments whose names look like athlete folder keys.
- Audit body text such as `[LWC26] Application:`, `Archive folder key:`, `Payment due:`, `Submitted biomarkers/results summary:`, `New biological age result posted.`, or `Update profile request...`.
- Identity anchors such as athlete name, display name, folder key, profile slug, profile URL, known personal email addresses, social handles, personal websites, invoice IDs, or submission IDs.
- Any message where `longevityworldcup@gmail.com` or any address ending in `@longevityworldcup.com` appears in from, to, cc, bcc, reply-to, or body. Treat this as useful, not required.

When selecting a single candidate because the user requested `next` or otherwise limited the scope, prefer the most recent unread unprocessed thread with a ZIP attachment or a payment-follow-up/application-audit pair. Summarize ambiguity instead of guessing silently.

Extract from the thread when available:

- applicant/requester contact email, preferring `Reply-To` or `Account email`
- athlete name and expected folder key
- update type: full application, results submission, or profile metadata update
- payment due and whether a payment confirmation/follow-up appears in the same thread
- submitted biomarker record count and proof file count
- attachment filename

## Skip Already Processed Threads

Before downloading attachments or running the reviewer, check `.artifacts/lwc-submission-processing-ledger.jsonl`. Treat it as local private state; do not commit it.

The Gmail unread state is the primary unprocessed signal. Use the ledger to avoid duplicate heavy work, but do not let an old ledger entry hide a still-unread candidate unless the unchanged thread/message identity proves it was already reviewed and the user asked for next/all submissions generally. If a candidate is unread and the ledger is missing, malformed, or ambiguous, inspect it.

Each processed summary should append one JSON object with:

- `processedAtUtc`
- `status`: `approved`, `blocked`, `needs-human`, `drafted`, or `finalized`
- `athleteName`, `folderKey`, `profileUrl`
- `gmailThreadIds` and `gmailMessageIds` reviewed
- `latestMessageAt` and, when available, the latest Gmail message id/internal date for every reviewed thread
- `requesterEmails` and other identity anchors used
- `attachmentNames` and optional attachment checksums if already computed
- `summary`: one short sentence explaining the decision or blocker

When scanning candidates, compute the same thread/message identity before doing heavy work. Skip immediately when all are true:

- the candidate or related thread appears in the ledger,
- the latest Gmail message id/date for the thread is unchanged from the ledger, and
- the user asked generally for next/all submissions rather than naming this athlete, thread, message, or folder key.

When skipping, report the skip reason and the ledger summary in one or two lines, then continue to the next candidate when the requested scope includes more candidates. Do not download the ZIP, run the reviewer, create drafts, or re-review proofs for unchanged skipped work.

Reprocess despite a ledger hit when:

- any related thread has a newer message than the recorded `latestMessageAt` or latest message id,
- a new ZIP/payment/context email is found for the same identity,
- the user explicitly asks to process/reprocess a named athlete, thread, message, or folder key, or
- the ledger entry is ambiguous, malformed, or missing the thread identifiers needed to prove it is unchanged.

After presenting the human approval summary, append/update the ledger before stopping, except for security-verification-only drafts. For security-verification-only drafts, do not create or update local artifact files; rely on the active Gmail draft and unchanged unread message. After final approval actions, append a new `finalized` entry with the commit hash, push status, and email-send status.

## Review Related Email History

For existing-athlete result/profile updates that need security verification, do not do broad related-history research just to decide whether to draft. If the unread submission identifies the existing profile/folder and provides a reliable recipient such as `Account email` or `Reply-To`, create the concise security draft immediately. Search related history only when the recipient is missing/ambiguous, the current thread may already contain an active equivalent draft, or the user explicitly asks you to investigate confirmation.

Before deciding, drafting, or concluding that context is missing, build a small identity map for the athlete/requester and search Gmail for related history. Do not assume all relevant messages are in the ZIP thread or from the same email address.

Use every anchor you can infer:

- athlete name, display name, folder key, profile slug, profile URL, and attachment filename
- `Account email`, `Reply-To`, sender, recipients, cc, and any email addresses mentioned in bodies
- personal links, social handles, website domains, invoice IDs, submission IDs, and prior athlete folder keys
- alternate spellings from underscores, hyphens, accents, nicknames, or casing

Search Gmail broadly enough to catch separate threads:

- exact athlete name and display name
- folder key with underscores and hyphens
- known email addresses one by one
- personal-link domains and social handles
- invoice/submission IDs when payment or checkout context matters
- LWC domain participants combined with the athlete name or slug, when present; do not stop if no LWC-domain thread exists

Read all likely related threads, including sent replies and old follow-ups. If results are numerous, narrow by the identity anchors above, but still summarize what was searched and why excluded hits were not relevant.

Carry forward facts from earlier messages: prior missing-proof requests, promised corrections, alternate contact addresses, payment explanations, previous submissions, prior rejection reasons, and any human decisions. If related history contradicts the current submission, mark the decision as needs human judgment unless the contradiction is clearly resolved in later messages.

## Existing Athlete Security Verification

For an existing athlete submission, such as a new biological-age result upload or profile metadata change for an athlete folder that already exists, decide whether a security verification draft is required before doing any ZIP/file processing.

Default: draft a verification email to the athlete and stop that candidate before downloading ZIPs, running `LongevityWorldCup.ApplicationReviewer`, reviewing proofs, redacting files, editing `athlete.json`, committing, or pushing.

Fast path: when an unread audit email says `Submission kind: Update`, `Update type: Results submission`, or `Update type: Profile metadata update`, and the profile URL/folder key points to an existing athlete, do only this:

1. Extract the athlete first name, current-submission types, thread/message ids, and best recipient from the unread message.
2. Check active Gmail drafts for the same thread or recipient so you do not create a duplicate equivalent security draft.
3. Create the concise security draft if no equivalent draft already exists.
4. Stop that candidate with a short summary. Do not download ZIPs, run the reviewer, inspect proofs, read or edit `athlete.json`, open Explorer, write the processing ledger, or produce a full proof checklist.

Related history may identify the best athlete contact address for verification, but it is not enough by itself to skip verification. Do not skip the verification draft merely because the sender, `Reply-To`, `Account email`, public profile, social handle, or prior direct correspondence matches a previously accepted athlete address.

Skip the verification draft only when the current submission is already confirmed in the current/newer conversation, or when the user explicitly says to skip verification for this named submission. The skip must cite evidence, such as:

- the current submission is part of an ongoing direct conversation where the athlete requested or confirmed this exact result/profile change,
- a later message in the related history confirms this exact current submission from the athlete or trusted requester, or
- the user explicitly says to skip verification for this named submission.

If no reliable athlete contact address is known, mark the decision as needs human judgment and explain which possible contact addresses were found.

When one athlete has multiple unread current submissions, such as a profile change followed by new results, combine them into one security draft. Use the shortest clear body and do not include proof findings, correction details, a long explanation, or a signature unless the user explicitly asks. Draft this verification email when required, adjusting only the first name and submitted item phrase:

```text
Hi {firstName}, for security reasons can you confirm you've submitted {the new results/the change request/the new results and the change request}?
```

Examples:

- Result plus profile/update request: `Hi David, for security reasons can you confirm you've submitted the new results and the change request?`
- Result only: `Hi David, for security reasons can you confirm you've submitted the new results?`
- Profile/update request only: `Hi Cher, for security reasons can you confirm you've submitted the change request?`

Do not download, review, redact, edit local files, open the athlete folder, write the ledger, commit, push, send an update/welcome email, or mutate Gmail labels for an existing athlete submission while verification is pending, unless the user explicitly overrides after seeing the summary.

## Prepare The Repo Files

Do not enter this section for an existing-athlete result/profile update until the security-verification gate has been satisfied by current/newer confirmation or by explicit user override. For an unconfirmed existing-athlete update, the correct action is only to create the concise security draft and stop that candidate.

Download ZIP attachments through the raw Gmail connector helper, not through Chrome, Computer Use, Gmail `read_attachment`, the Gmail web UI, or a browser-controlled attachment URL. `application/zip` returning `read_attachment_supported: false`, `unsupported_attachment_type`, or an oversized raw MIME payload in normal chat/tool output is expected; these are not reasons to use a browser.

Save full application ZIPs and result/profile ZIPs into:

```powershell
LongevityWorldCup.Website\wwwroot\athletes\
```

First search/read the parent Gmail message with connector tools and copy the Gmail message id plus the exact attachment filename from message metadata. Prefer `--filename` for ZIPs because Gmail attachment ids may be reissued between connector reads; use `--attachment-id` only when filenames are absent or ambiguous. Then run from the solution root:

```powershell
node .\.codex\skills\process-athlete-applications\scripts\download-gmail-attachment-raw.mjs --message-id {gmail_message_id} --filename "{attachment_filename}" --out .\LongevityWorldCup.Website\wwwroot\athletes\
```

If the helper cannot infer the current Codex thread, add `--thread-id <codex-thread-id>`. The downloader starts a temporary local `codex app-server`, calls Gmail `read_email` with `include_raw_mime=true`, writes the selected attachment bytes directly to the requested local path, and prints JSON containing `savedPath`, `filename`, `mimeType`, `size`, and `sha256`.

If the helper fails, fix or diagnose the helper/app-server/Gmail connector path. If it still cannot download the ZIP, stop and report the blocker. Do not open Gmail in Chrome, do not click attachment controls, do not use browser `fetch`/XHR/downloads, and do not ask the user to manually download the ZIP unless they explicitly choose that fallback after seeing the blocker.

After downloading, verify `mimeType` is `application/zip`, the size is plausible, and the saved file opens as a ZIP locally before running the reviewer.

Preserve the attachment filename unless doing so would overwrite an unrelated ZIP. If a same-named ZIP or athlete folder already exists, inspect before replacing because result uploads merge into existing athlete folders.

Run the reviewer from the solution root:

```powershell
dotnet run --project .\LongevityWorldCup.ApplicationReviewer\LongevityWorldCup.ApplicationReviewer.csproj
```

The reviewer scans `LongevityWorldCup.Website\wwwroot\athletes` for `*.zip`, extracts or merges the athlete folder, deletes the ZIP, starts the website on `https://localhost:7080` if needed, opens the athlete page in Chrome incognito, and opens the athlete folder in Explorer. If it fails because projects are not built, build the solution or relevant projects, then rerun the reviewer.

After the reviewer runs, identify the changed athlete folder from the email folder key, ZIP filename, or `git status`.

## Review athlete.json

Open the extracted `athlete.json` and parse it as JSON. Compare it with the email audit summary and the visible proofs.

Required checks:

- Name, display name, division, flag, personal link, media contact, and "Why" text are plausible and not obviously malformed.
- `DateOfBirth` is present for full applications and produces a plausible chronological age for the test dates.
- Treat `DateOfBirth` month/day `12/31` as an allowed privacy placeholder when the year is plausible. Do not "correct" a submitted December 31 DOB to an exact DOB visible on proof documents. The onboarding flow explicitly allows leaving the birthday at December 31 as a privacy precaution, at a slight competitive disadvantage.
- `Biomarkers` records have ISO-like dates and numeric values in the repository's expected units.
- New result submissions append new biomarker records rather than replacing unrelated existing history.
- Profile image filename matches the athlete folder key with an allowed image extension.
- Public proof files are named `proof_*.ext` and are present for result-bearing submissions.

Use code as source of truth when uncertain:

- `LongevityWorldCup.Website\Business\ApplicantData.cs` for submitted field names.
- `LongevityWorldCup.Website\Controllers\ApplicationController.cs` for audit email fields and ZIP creation.
- `LongevityWorldCup.Website\Tools\PhenoStatsCalculator.cs` for which biomarker fields count toward pheno age and bortz age.

## Proof Review Checklist

Review every proof image/PDF page visually. OCR may help, but do not accept solely from OCR when the proof is visible.

For each JSON biomarker record:

- Match the proof date to the JSON `Date`.
- Match each recorded value to a visible proof value, accounting for explicit unit conversion only when the source unit is clear.
- Confirm all values in the record are from one blood draw or one coherent lab report for the same test date. Do not accept a single JSON record assembled from different blood tests, different dates, or unrelated documents.
- Confirm that required biomarkers for the claimed result are actually supported.
- Correct obvious JSON clerical mismatches locally instead of blocking. Examples: use the blood draw/collection date instead of a report/submission date when the report clearly shows both; fix a mistyped numeric value such as `ShbgNmolL` when the proof value and unit are unambiguous. Report the correction in the human approval summary.
- Censor proof files locally instead of blocking when nonessential identifiers are visible. Redact client phone numbers, client addresses, client ID numbers, patient IDs, order IDs, accession/specimen numbers, barcodes, QR codes, and similar identifiers while preserving the applicant name, test date, and biomarker values needed for verification.
- When `athlete.json` uses a `12/31` DOB privacy placeholder and a proof exposes the exact DOB, censor the exact DOB on the proof. Preserve only the applicant name, test date, and required biomarker values; do not use the proof DOB to overwrite the JSON placeholder.
- Confirm the proof belongs to the applicant when the document exposes a name or other safe identity signal.

Pheno age requires one record with:

`AlbGL`, `CreatUmolL`, `GluMmolL`, `CrpMgL`, `Wbc1000cellsuL`, `LymPc`, `McvFL`, `RdwPc`, `AlpUL`.

Bortz age requires one record with:

`AlbGL`, `AlpUL`, `UreaMmolL`, `CholesterolMmolL`, `CreatUmolL`, `CystatinCMgL`, `Hba1cMmolMol`, `CrpMgL`, `GgtUL`, `Rbc10e12L`, `McvFL`, `RdwPc`, `MonocytePc`, `NeutrophilPc`, `LymPc`, `AltUL`, `ShbgNmolL`, `VitaminDNmolL`, `GluMmolL`, `MchPg`, `ApoA1GL`.

`MonocytePc` and `NeutrophilPc` are stored as percentages; the site derives counts from WBC.

## Blocking Issues

Mark the submission blocked and prepare a draft reply when any of these are true:

- Required biomarker values are missing from proofs.
- JSON values or dates do not match visible proof values and cannot be confidently corrected from the proof.
- A single JSON biomarker record combines multiple tests, dates, or reports.
- Proofs expose uncensored private identifiers that cannot be safely censored locally without hiding verification evidence.
- Payment is due and no payment confirmation/follow-up is visible in Gmail.
- The ZIP/reviewer output modifies the wrong athlete folder or creates an unexpected folder key.
- The applicant's identity, test date, or result ownership cannot be reasonably verified.
- Related email history was not searched, or it contains unresolved contradictions about proof, payment, identity, or requested changes.
- The processed ledger was not checked before ZIP download/reviewer work, unless the user explicitly requested a named submission.
- Existing athlete result/profile-change submissions do not have a drafted security verification email, current/newer confirmation of the exact submission, or an explicit user override.
- Existing athlete result/profile-change submissions were downloaded, reviewed, redacted, or applied locally before security verification was satisfied.

For obvious issues, draft the email directly. For uncertain medical/unit interpretation issues, explain the uncertainty in the summary and wait for the human decision.

## Draft Replies

Draft replies in Gmail, do not send them before approval.

When blocked, keep the message concise and specific:

```text
Hi {name},

Thanks for your Longevity World Cup submission. I reviewed the materials, but I need one update before I can approve it:

{specific issue}

Please reply with updated proof or a corrected submission. The proof should show the relevant biomarker values and test date, while censoring nonessential private identifiers such as phone numbers, addresses, ID numbers, patient IDs, order IDs, accession numbers, barcodes, and QR codes.

Best,
Longevity World Cup
```

Common `{specific issue}` examples:

- `The proof does not show {missing biomarkers}, which are needed for the submitted {pheno age/bortz age} result.`
- `The submitted biomarker record appears to combine values from different test dates. Each result record needs to come from one blood draw or one coherent lab report for the same test date.`
- `I could not safely censor {identifier type} without hiding the proof values needed for verification. Please upload a censored version.`
- `The submission still shows a payment due, and I do not see a payment confirmation yet. Please complete the payment or reply if you believe it was already paid.`

When accepted and the user approves finalization, send a welcome/update reply:

```text
Hi {name},

Your Longevity World Cup profile is live:
{profileUrl}

Welcome to the Longevity World Cup. Please reply if you spot anything that needs a correction.

Best,
Longevity World Cup
```

For result uploads or profile updates, replace the welcome sentence with `Your Longevity World Cup profile has been updated.`

## Human Approval Summary

Stop after review and present a summary before any send/commit/push. Include:

For an existing-athlete security-verification-only draft, keep the summary short: athlete name/profile, Gmail thread/message id, recipient, exact draft text or existing draft id, and `No ZIP downloaded, no files inspected or changed, no ledger written, Gmail labels untouched.` Do not include JSON highlights, proof checklist, files changed, or folder-open status for this fast path.

- Recommended decision: approve, block, or needs human judgment.
- Athlete folder path and public profile URL. Folder keys use underscores; profile URLs use hyphens.
- Gmail thread/message used and whether a draft reply was created.
- Related email history reviewed: search anchors used, additional threads found, alternate requester addresses, and relevant prior context.
- Processed ledger: whether this was new, reprocessed because of newer email, or explicitly overridden by the user.
- Existing-athlete security verification: draft created, skipped with cited evidence, pending athlete reply, or explicitly overridden by the user.
- Payment status from the audit email and any confirmation found.
- Files changed from `git status --short`.
- JSON highlights: name, division, flag, date of birth, biomarker record dates, pheno/bortz availability.
- Proof checklist: for each biomarker record, list proof files reviewed, date match, value match, same-test status, censoring status, and any missing evidence. For existing-athlete submissions stopped at the security gate, say proof/files were intentionally not downloaded or reviewed pending confirmation.
- Exact next actions you will take if the user says to approve.

If the athlete folder is not already open and this is not a security-verification-only draft, open it in Explorer before presenting the summary:

```powershell
explorer .\LongevityWorldCup.Website\wwwroot\athletes\{folder_key}
```

## Finalize After Explicit Approval

Only after the user approves:

1. Recheck `git status --short`.
2. Stage only the accepted athlete files and any intentional supporting changes.
3. Commit with a short message such as `Add {Name} athlete` or `Update {Name} athlete results`.
4. Push `origin master` only when on `master` and the user approved pushing. If not on `master`, ask before switching or pushing.
5. Send the approved Gmail reply in the original thread with the athlete link.
6. Leave Gmail unread/read labels and other message labels unchanged; the user marks submission emails manually.
7. Report the commit hash, pushed branch, profile URL, email-send status, and confirmation that Gmail labels were left untouched.
