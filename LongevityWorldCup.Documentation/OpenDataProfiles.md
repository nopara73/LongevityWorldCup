# OpenData profile curation

OpenData profiles are neutral, non-competing references built from bloodwork that a public-facing person self-published or explicitly authorized for public release. The subject did not apply to or join the Longevity World Cup and is not a Longevity athlete. See `UBIQUITOUS_LANGUAGE.md` for the domain invariants.

## Admission checklist

Add a profile only when all of these are true:

1. The subject is unmistakably notable beyond a narrow niche: a globally recognizable public figure or a field-defining figure whose name is broadly established in sport, health, science, culture, or public life. A complete panel alone is not enough.
2. The subject either published the source or explicitly authorized its publication and intentionally participated in the public release. Explicit authorization must have a direct HTTPS evidence link and a concise evidence note. Do not use leaks, exposed patient portals, data-broker copies, or unsupported third-party reposts.
3. The source cleanly publishes all nine pheno age inputs from one draw or one clearly unified laboratory panel: albumin, creatinine, glucose, CRP, lymphocyte percentage, MCV, RDW-CV, alkaline phosphatase, and WBC.
4. The specimen date—or, when one unified report contains the complete panel but prints no unified specimen date, the report date—and the subject's age at that source date are supportable. Set `DateBasis` honestly so a report date is never presented as a draw date. Never persist or republish an exact date of birth.
5. A neutral explanation of why the subject is broadly notable fits within 280 characters and cites at least one linked `Identity` source.
6. A redistributable portrait is available under a verified license. Its author, Commons or equivalent source page, original asset URL, license name and URL, and any crop or resize are documented and shown with the image.
7. Every transcription, conversion, qualifier, date choice, and identity variant is documented. Ambiguous candidates are rejected rather than estimated into completeness.

Inclusion is an editorial reference decision, not participation or endorsement. Every visible profile provides its sources and a correction/removal route.

## Storage and schema

Store one record at:

```text
LongevityWorldCup.Website/wwwroot/public-data-profiles/<kebab-case-slug>/profile.json
LongevityWorldCup.Website/wwwroot/public-data-profiles/<kebab-case-slug>/portrait.webp
```

The file must declare `"ProfileType": "OpenData"`, `OpenData.SubjectDidNotApply: true`, a review date, sourced `OpenData.Notability` context, required licensed `OpenData.Portrait` provenance, identity source IDs, and one or more HTTPS sources. Every biomarker record must cite a subject-authorized `Bloodwork` source. A source's `SubjectAuthorization.Kind` is either `SelfPublished` or `ExplicitlyAuthorized`; the latter also requires `EvidenceUrl` and `EvidenceNote`. `OpenData.Notability.SourceIds` must reference linked `Identity` sources. Use `OpenData.Aliases` for documented public identity variants so future athlete applications cannot silently create a duplicate identity. `PreferredForDisplay` may select at most one source for the card's source action.

The schema is closed: do not add ad hoc fields. Validation rejects unknown properties at every structured depth, and the combined API reconstructs an allowlisted public projection rather than serializing the stored object. This is a privacy boundary as well as a data-quality rule.

Each profile folder contains exactly `profile.json` and one normalized `portrait.webp`: a square 640×640 WebP with stripped metadata and bounded file size. Do not add proof files, source downloads, or any other local asset. The entire `/public-data-profiles/` static namespace remains unavailable over HTTP; the application serves a portrait only through the validated, versioned `/public-data/<slug>/portrait` route for a profile that survived schema, identity, and population reconciliation. Athlete `ProfilePic` fields remain forbidden.

`OpenData.Portrait` records the portrait's `SourcePageUrl`, `OriginalUrl`, `Author`, `LicenseName`, `LicenseUrl`, and `EditNote`. These fields are photograph provenance, not bloodwork authorization and not subject identity sources. Download and normalize the licensed image instead of hotlinking it. When cropping, resizing, or converting, say so in `EditNote`; retain the original license, including share-alike terms, and render a visible author/license/modification credit on both the card and modal.

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

OpenData records are loaded from a separate root and are exposed only through the combined display feed. They never enter athlete persistence, canonical ranking, league, badge, Event, crowd-age, prize, sitemap, or notification paths. Backend rankings, field sizes, snapshots, placements, and competition outputs remain official-athlete-only.

After official ranks are fixed, the leaderboard presentation may interleave a visually distinct OpenData row with a display-only hypothetical rank. This presentation merge is not canonical ranking and does not renumber or displace official athletes. Each reference is evaluated independently against official athletes only, so OpenData profiles never consume positions for one another. Its hypothetical rank is one plus the number of eligible official rows ahead in the selected view; the unrounded relevant age reduction is compared, and an official athlete wins an exact score tie. Do not infer an exact date of birth to apply the competition tie-breaker.

A hypothetical row currently appears only in the selected Ultimate or pheno age view because the validated OpenData schema accepts complete pheno panels only. A pheno-only Ultimate reference follows all Pros and is Amateur-comparable without joining the Amateur track. OpenData rows remain absent from bortz age, crowd age, and improvement views; division, generation, flag, exclusive, and track-filtered leagues; and all podium and prize presentation. Invalid or temporarily conflicting OpenData records cannot block approved athlete loading, and a later valid reconciliation can restore a withheld record.

## Verification

Run at least:

```powershell
dotnet test LongevityWorldCup.Tests/LongevityWorldCup.Tests.csproj --filter "AthleteProfilePolicyTests|OpenDataProfileFolderIntegrityTests|LeaderboardProfileApiTests|OpenDataProfileUiTests|OpenDataProfileBrowserTests"
```

The folder-integrity test validates every committed record, licensed 640×640 WebP portrait, provenance, aliases, identities, and the population cap. Browser coverage verifies visually distinct hypothetical rows, unchanged official ranks, portrait attribution, compact disclosure, direct routes, provenance, focus behavior, supported-view limits, and suppression of competition-only UI.
