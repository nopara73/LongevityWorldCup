---
name: process-athlete-applications
description: Review and process Longevity World Cup athlete submission emails from Gmail, including full applications, biological-age result uploads, and profile-update requests. Use when Codex needs to find LWC submission emails, download ZIP attachments into the repo athlete folder, run LongevityWorldCup.ApplicationReviewer, inspect athlete.json and proofs, prepare draft requester replies, summarize the human approval decision, and after explicit approval commit/push accepted athlete changes and send the welcome or update email.
---

# Process Athlete Submissions

## Operating Rules

- Read `UBIQUITOUS_LANGUAGE.md` before judging applications, results, rankings, athlete onboarding, badges, Events, or competition copy.
- Use Gmail connector tools when available. If Gmail tools are not loaded, search for them with `tool_search` before using browser workarounds.
- Keep temporary downloads, OCR output, screenshots, and notes under `.artifacts/` unless the ZIP must be placed in the athlete folder for the reviewer.
- Never send email, commit, or push until the user explicitly approves the prepared summary and draft.
- Never stage unrelated work. If the worktree is dirty, identify unrelated changes and leave them alone.
- Do not add Node tooling for browser checks; this repo is a .NET solution.

## Find The Submission Email

Search Gmail flexibly. Do not depend on one exact sender or subject because applicants sometimes reply from personal accounts.

Strong signals:

- Any message where `longevityworldcup@gmail.com` or any address ending in `@longevityworldcup.com` appears in from, to, cc, bcc, reply-to, or body.
- ZIP attachments whose names look like athlete folder keys.
- Audit body text such as `[LWC26] Application:`, `Archive folder key:`, `Payment due:`, `Submitted biomarkers/results summary:`, `New biological age result posted.`, or `Update profile request...`.

When multiple candidates exist, prefer the most recent unprocessed thread with a ZIP attachment or a payment-follow-up/application-audit pair. Summarize ambiguity instead of guessing silently.

Extract from the thread when available:

- applicant/requester contact email, preferring `Reply-To` or `Account email`
- athlete name and expected folder key
- update type: full application, results submission, or profile metadata update
- payment due and whether a payment confirmation/follow-up appears in the same thread
- submitted biomarker record count and proof file count
- attachment filename

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
- Check that client phone numbers, client addresses, client ID numbers, patient IDs, order IDs, accession numbers, barcodes, QR codes, and similar nonessential personal identifiers are censored. The applicant's name and test date may remain visible when needed to link the proof to the applicant.
- Confirm the proof belongs to the applicant when the document exposes a name or other safe identity signal.

Pheno age requires one record with:

`AlbGL`, `CreatUmolL`, `GluMmolL`, `CrpMgL`, `Wbc1000cellsuL`, `LymPc`, `McvFL`, `RdwPc`, `AlpUL`.

Bortz age requires one record with:

`AlbGL`, `AlpUL`, `UreaMmolL`, `CholesterolMmolL`, `CreatUmolL`, `CystatinCMgL`, `Hba1cMmolMol`, `CrpMgL`, `GgtUL`, `Rbc10e12L`, `McvFL`, `RdwPc`, `MonocytePc`, `NeutrophilPc`, `LymPc`, `AltUL`, `ShbgNmolL`, `VitaminDNmolL`, `GluMmolL`, `MchPg`, `ApoA1GL`.

`MonocytePc` and `NeutrophilPc` are stored as percentages; the site derives counts from WBC.

## Blocking Issues

Mark the submission blocked and prepare a draft reply when any of these are true:

- Required biomarker values are missing from proofs.
- JSON values do not match visible proof values or dates.
- A single JSON biomarker record combines multiple tests, dates, or reports.
- Proofs expose uncensored private identifiers beyond what is needed to verify the result.
- Payment is due and no payment confirmation/follow-up is visible in Gmail.
- The ZIP/reviewer output modifies the wrong athlete folder or creates an unexpected folder key.
- The applicant's identity, test date, or result ownership cannot be reasonably verified.

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
- `The proof still shows private identifiers such as {identifier type}. Please upload a censored version.`
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
