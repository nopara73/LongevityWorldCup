---
name: process-athlete-applications
description: Review and process Longevity World Cup athlete submission emails from Gmail, including full applications, biological-age result uploads, and profile-update requests. Use when Codex needs to find LWC submission emails or related personal-email conversations, understand related email history even across multiple requester addresses, download ZIP attachments into the repo athlete folder, run LongevityWorldCup.ApplicationReviewer, inspect athlete.json and proofs, prepare draft requester or athlete security-verification replies, summarize the human approval decision, and after explicit approval commit/push accepted athlete changes and send the welcome or update email.
---

# Process Athlete Submissions

## Operating Rules

- Read `UBIQUITOUS_LANGUAGE.md` before judging applications, results, rankings, athlete onboarding, badges, Events, or competition copy.
- Use Gmail connector tools when available. If Gmail tools are not loaded, search for them with `tool_search` before using browser workarounds.
- Keep temporary downloads, OCR output, screenshots, notes, and the processing ledger under `.artifacts/` unless the ZIP must be placed in the athlete folder for the reviewer.
- Never send email, commit, or push until the user explicitly approves the prepared summary and draft.
- Never stage unrelated work. If the worktree is dirty, identify unrelated changes and leave them alone.
- Do not add Node tooling for browser checks; this repo is a .NET solution.

## Find The Submission Email

Search Gmail flexibly. Do not depend on one exact sender, recipient, domain, or subject because applicant and athlete conversations may happen through personal email threads that do not touch `longevityworldcup@gmail.com` or any `@longevityworldcup.com` address.

Strong signals:

- ZIP attachments whose names look like athlete folder keys.
- Audit body text such as `[LWC26] Application:`, `Archive folder key:`, `Payment due:`, `Submitted biomarkers/results summary:`, `New biological age result posted.`, or `Update profile request...`.
- Identity anchors such as athlete name, display name, folder key, profile slug, profile URL, known personal email addresses, social handles, personal websites, invoice IDs, or submission IDs.
- Any message where `longevityworldcup@gmail.com` or any address ending in `@longevityworldcup.com` appears in from, to, cc, bcc, reply-to, or body. Treat this as useful, not required.

When multiple candidates exist, prefer the most recent unprocessed thread with a ZIP attachment or a payment-follow-up/application-audit pair. Summarize ambiguity instead of guessing silently.

Extract from the thread when available:

- applicant/requester contact email, preferring `Reply-To` or `Account email`
- athlete name and expected folder key
- update type: full application, results submission, or profile metadata update
- payment due and whether a payment confirmation/follow-up appears in the same thread
- submitted biomarker record count and proof file count
- attachment filename

## Skip Already Processed Threads

Before downloading attachments or running the reviewer, check `.artifacts/lwc-submission-processing-ledger.jsonl`. Treat it as local private state; do not commit it.

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
- the user asked for the next submission generally rather than naming this athlete, thread, message, or folder key.

When skipping, report the skip reason and the ledger summary in one or two lines, then continue to the next candidate if the user asked for "next". Do not download the ZIP, run the reviewer, create drafts, or re-review proofs for unchanged skipped work.

Reprocess despite a ledger hit when:

- any related thread has a newer message than the recorded `latestMessageAt` or latest message id,
- a new ZIP/payment/context email is found for the same identity,
- the user explicitly asks to process/reprocess a named athlete, thread, message, or folder key, or
- the ledger entry is ambiguous, malformed, or missing the thread identifiers needed to prove it is unchanged.

After presenting the human approval summary, append/update the ledger before stopping. After final approval actions, append a new `finalized` entry with the commit hash, push status, and email-send status.

## Review Related Email History

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

For an existing athlete submission, such as a new biological-age result upload or profile metadata change for an athlete folder that already exists, decide whether a security verification draft is required before finalizing.

Default: draft a verification email to the athlete and stop for human approval before committing/pushing the change.

Related history may identify the best athlete contact address for verification, but it is not enough by itself to skip verification. Do not skip the verification draft merely because the sender, `Reply-To`, `Account email`, public profile, social handle, or prior direct correspondence matches a previously accepted athlete address.

Skip the verification draft only when the current submission is already confirmed in the current/newer conversation, or when the user explicitly says to skip verification for this named submission. The skip must cite evidence, such as:

- the current submission is part of an ongoing direct conversation where the athlete requested or confirmed this exact result/profile change,
- a later message in the related history confirms this exact current submission from the athlete or trusted requester, or
- the user explicitly says to skip verification for this named submission.

If no reliable athlete contact address is known, mark the decision as needs human judgment and explain which possible contact addresses were found.

Draft this verification email when required:

```text
Hi {name},

I received a Longevity World Cup submission that would update your athlete profile:

{short description of submitted result or profile change}

For security, please reply to confirm that you submitted this and that the update should be applied.

Best,
Longevity World Cup
```

Do not commit, push, or send an update/welcome email for an existing athlete submission while verification is pending, unless the user explicitly overrides after seeing the summary.

## Prepare The Repo Files

Download the ZIP attachment into:

```powershell
LongevityWorldCup.Website\wwwroot\athletes\
```

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

- Recommended decision: approve, block, or needs human judgment.
- Athlete folder path and public profile URL. Folder keys use underscores; profile URLs use hyphens.
- Gmail thread/message used and whether a draft reply was created.
- Related email history reviewed: search anchors used, additional threads found, alternate requester addresses, and relevant prior context.
- Processed ledger: whether this was new, reprocessed because of newer email, or explicitly overridden by the user.
- Existing-athlete security verification: draft created, skipped with cited evidence, pending athlete reply, or explicitly overridden by the user.
- Payment status from the audit email and any confirmation found.
- Files changed from `git status --short`.
- JSON highlights: name, division, flag, date of birth, biomarker record dates, pheno/bortz availability.
- Proof checklist: for each biomarker record, list proof files reviewed, date match, value match, same-test status, censoring status, and any missing evidence.
- Exact next actions you will take if the user says to approve.

If the athlete folder is not already open, open it in Explorer before presenting the summary:

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
6. Report the commit hash, pushed branch, profile URL, and email-send status.
