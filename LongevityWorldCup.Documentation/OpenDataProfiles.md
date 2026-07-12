# OpenData profile curation

OpenData profiles are neutral, unranked references built from bloodwork that a public-facing person intentionally published. The subject did not apply to or join the Longevity World Cup and is not a Longevity athlete. See `UBIQUITOUS_LANGUAGE.md` for the domain invariants.

## Admission checklist

Add a profile only when all of these are true:

1. The subject is an identifiable public-interest figure with an established public body of work.
2. A first-party page, file, post, or video connects the subject to the publication. Do not use leaks, exposed patient portals, data-broker copies, or third-party reposts.
3. The source cleanly publishes all nine pheno age inputs from one draw or one clearly unified laboratory panel: albumin, creatinine, glucose, CRP, lymphocyte percentage, MCV, RDW-CV, alkaline phosphatase, and WBC.
4. The measurement date or honest source precision and the subject's age at that measurement are supportable from the publication. Never persist or republish an exact date of birth.
5. Every transcription, conversion, qualifier, date choice, and identity variant is documented. Ambiguous candidates are rejected rather than estimated into completeness.

Inclusion is an editorial reference decision, not participation or endorsement. Every visible profile provides its sources and a correction/removal route.

## Storage and schema

Store one record at:

```text
LongevityWorldCup.Website/wwwroot/public-data-profiles/<kebab-case-slug>/profile.json
```

The file must declare `"ProfileType": "OpenData"`, `OpenData.SubjectDidNotApply: true`, a review date, identity source IDs, and one or more HTTPS sources. Every biomarker record must cite a subject-published `Bloodwork` source. Use `OpenData.Aliases` for documented public identity variants so future athlete applications cannot silently create a duplicate identity. `PreferredForDisplay` may select at most one source for the card's source action.

The schema is closed: do not add ad hoc fields. Validation rejects unknown properties at every structured depth, and the combined API reconstructs an allowlisted public projection rather than serializing the stored object. This is a privacy boundary as well as a data-quality rule.

Each profile folder is manifest-only and may contain only `profile.json`. Do not store profile pictures, proof files, source downloads, or other local assets there; the entire `/public-data-profiles/` static namespace is intentionally unavailable over HTTP.

Canonical units are:

| Field | Unit |
| --- | --- |
| `AlbGL` | g/L |
| `CreatUmolL` | µmol/L |
| `GluMmolL` | mmol/L |
| `CrpMgL` | mg/L |
| `LymPc` | % |
| `McvFL` | fL |
| `RdwPc` | RDW-CV % |
| `AlpUL` | U/L |
| `Wbc1000cellsuL` | 10³ cells/µL |

If a source publishes only a month, store the first of that month and set `"DatePrecision": "Month"`; the UI will display only the month. Preserve published `<` or `>` assay boundaries in `MeasurementQualifiers` and store the published limit as the numeric value. Explain both decisions in `TranscriptionNotes`.

Store `AgeYears` with no more than two decimal places. Higher precision combined with an exact draw date could encode a reconstructable birth day, defeating the rule against republishing exact dates of birth.

The validator applies deliberately broad unit/transcription bounds and then requires a finite reference pheno age. These are data-quality guardrails, not clinical reference ranges. Athlete-only and service-controlled keys, including date-of-birth variants, are rejected at every JSON depth.

## Population and competition boundary

The exact cap is:

```text
openDataCount × 10 <= athleteCount + openDataCount
```

The runtime cap is deliberately one-sided so approved athlete growth can never take the site down. The committed-roster integrity test separately enforces the requested 9–10% population band, prompting curators to add another fully verified profile when athlete growth pushes the checked-in roster below 9%.

OpenData records are loaded from a separate root and are exposed only through the combined display feed. They never enter athlete persistence, ranking, league, badge, Event, crowd-age, prize, sitemap, or notification paths. Invalid or temporarily conflicting OpenData records cannot block approved athlete loading, and a later valid reconciliation can restore a withheld record.

## Verification

Run at least:

```powershell
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --filter "AthleteProfilePolicyTests|OpenDataProfileFolderIntegrityTests|LeaderboardProfileApiTests|OpenDataProfileUiTests|OpenDataProfileBrowserTests"
```

The folder-integrity test validates every committed record, provenance, aliases, identities, and the population cap. Browser coverage verifies the separate unranked presentation, direct route, provenance, focus behavior, and suppression of competition-only UI.
