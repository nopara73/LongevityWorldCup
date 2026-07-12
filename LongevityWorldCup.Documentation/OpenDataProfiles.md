# OpenData profile curation

OpenData profiles are neutral, unranked references built from bloodwork that a public-facing person self-published or explicitly authorized for public release. The subject did not apply to or join the Longevity World Cup and is not a Longevity athlete. See `UBIQUITOUS_LANGUAGE.md` for the domain invariants.

## Admission checklist

Add a profile only when all of these are true:

1. The subject is unmistakably notable beyond a narrow niche: a globally recognizable public figure or a field-defining figure whose name is broadly established in sport, health, science, culture, or public life. A complete panel alone is not enough.
2. The subject either published the source or explicitly authorized its publication and intentionally participated in the public release. Explicit authorization must have a direct HTTPS evidence link and a concise evidence note. Do not use leaks, exposed patient portals, data-broker copies, or unsupported third-party reposts.
3. The source cleanly publishes all nine pheno age inputs from one draw or one clearly unified laboratory panel: albumin, creatinine, glucose, CRP, lymphocyte percentage, MCV, RDW-CV, alkaline phosphatase, and WBC.
4. The specimen date—or, when one unified report contains the complete panel but prints no unified specimen date, the report date—and the subject's age at that source date are supportable. Set `DateBasis` honestly so a report date is never presented as a draw date. Never persist or republish an exact date of birth.
5. A neutral explanation of why the subject is broadly notable fits within 280 characters and cites at least one linked `Identity` source.
6. Every transcription, conversion, qualifier, date choice, and identity variant is documented. Ambiguous candidates are rejected rather than estimated into completeness.

Inclusion is an editorial reference decision, not participation or endorsement. Every visible profile provides its sources and a correction/removal route.

## Storage and schema

Store one record at:

```text
LongevityWorldCup.Website/wwwroot/public-data-profiles/<kebab-case-slug>/profile.json
```

The file must declare `"ProfileType": "OpenData"`, `OpenData.SubjectDidNotApply: true`, a review date, sourced `OpenData.Notability` context, identity source IDs, and one or more HTTPS sources. Every biomarker record must cite a subject-authorized `Bloodwork` source. A source's `SubjectAuthorization.Kind` is either `SelfPublished` or `ExplicitlyAuthorized`; the latter also requires `EvidenceUrl` and `EvidenceNote`. `OpenData.Notability.SourceIds` must reference linked `Identity` sources. Use `OpenData.Aliases` for documented public identity variants so future athlete applications cannot silently create a duplicate identity. `PreferredForDisplay` may select at most one source for the card's source action.

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

Use `"DateBasis": "Collection"` when `Date` is the specimen date (or omit it, since collection is the default). Use `"DateBasis": "Report"` only when the source presents all nine inputs as one unified laboratory report but does not print one specimen date for the whole panel; store the published report date and explain the choice. The UI labels it as a report date, never a draw date.

If a source date has only month precision, store the first of that month and set `"DatePrecision": "Month"`; the UI will display only the month. Preserve published `<` or `>` assay boundaries in `MeasurementQualifiers` and store the published limit as the numeric value. Explain both decisions in `TranscriptionNotes`.

Store `AgeYears` with no more than two decimal places. Higher precision combined with an exact source date could encode a reconstructable birth day, defeating the rule against republishing exact dates of birth.

The validator applies deliberately broad unit/transcription bounds and then requires a finite reference pheno age. These are data-quality guardrails, not clinical reference ranges. Athlete-only and service-controlled keys, including date-of-birth variants, are rejected at every JSON depth.

## Population and competition boundary

The exact cap is:

```text
openDataCount × 10 <= athleteCount + openDataCount
```

The population rule is deliberately one-sided: OpenData profiles may comprise at most 10% of the combined leaderboard population. There is no minimum. Curators add a profile only when the subject is unmistakably notable and the evidence satisfies every provenance and completeness requirement; athlete growth never creates pressure to add filler.

OpenData records are loaded from a separate root and are exposed only through the combined display feed. They never enter athlete persistence, ranking, league, badge, Event, crowd-age, prize, sitemap, or notification paths. Invalid or temporarily conflicting OpenData records cannot block approved athlete loading, and a later valid reconciliation can restore a withheld record.

## Verification

Run at least:

```powershell
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --filter "AthleteProfilePolicyTests|OpenDataProfileFolderIntegrityTests|LeaderboardProfileApiTests|OpenDataProfileUiTests|OpenDataProfileBrowserTests"
```

The folder-integrity test validates every committed record, provenance, aliases, identities, and the population cap. Browser coverage verifies the separate unranked presentation, direct route, provenance, focus behavior, and suppression of competition-only UI.
