using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LongevityWorldCup.Website.Controllers;

public sealed class PublicDataSwaggerExamples : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var path = "/" + (context.ApiDescription.RelativePath ?? "").Trim('/');

        switch (path)
        {
            case "/api/events":
                operation.OperationId = "listEvents";
                operation.Summary = "List public competition Events";
                operation.Description = """
                    Returns the complete persisted website-visible Event history as a JSON array, newest `OccurredAt` first.
                    No authentication, pagination, query filters, or implicit result limit. An empty history returns `[]`.
                    Equal timestamps use Event-type priority; order within the same timestamp and type is unspecified.
                    IDs are opaque stable identifiers, not necessarily UUIDs. Records can be corrected or hidden later;
                    this is a current snapshot of public history, not an immutable append-only log.

                    **Visibility:** includes profile-only `TestResultAccepted` Events (13). Exclude those from shared
                    highlights; use them in the referenced athlete's profile history. Social-only and hidden Events
                    are excluded. Social delivery state, retries, and platform queues are not public fields.
                    `VisibleOnWebsite` is always true here and does not mean an Event belongs in shared highlights.
                    The homepage applies additional curation; this feed is not its selected highlights.

                    **Payloads:** `Text` is stored data, not rendered HTML. Most types use space-separated `key[value]`
                    tokens. Parse by key, not token position; tolerate unknown keys/types and absent or empty optional
                    values. Missing values are unavailable, not zero. `slug`, `prev`, and comma-separated `prevs`
                    refer to public athlete slugs from `/api/data/athletes`; `prev`/`prevs` identify replaced athletes.
                    Escape free text before rendering it in HTML. Numbers use a decimal point; ages and differences
                    are in years, `rank`/`place`/`prevPlace` are one-based, and `sats` is integer satoshis (100,000,000 per BTC).
                    `Relevance` is an editorial weight, not a competition score or sorting key for this feed.

                    | Type | Name | Meaning and Text payload |
                    | --- | --- | --- |
                    | 0 | General | General announcement; free text. |
                    | 1 | Joined | Athlete joined; `slug`. |
                    | 2 | NewRank | Historical Ultimate League placement; `slug`, `rank`, optional `prev`. Not a current live rank. |
                    | 3 | DonationReceived | Bitcoin donation; `tx` (transaction ID), `sats` (amount). |
                    | 4 | AthleteCountMilestone | Participation milestone; `athletes` (count). |
                    | 5 | BadgeAward | Computed badge award; `slug`, `badge` (label), `cat` (league category), `val` (league value, may be empty), `place` (may be empty), optional `solo[1]`, `prev` or `prevs`. Best-improvement badges compare latest with first eligible result. |
                    | 6 | CustomEvent | Admin-created public announcement; title followed by a blank line and body. Optional markup includes `[bold](text)`, `[strong](text)`, `[label](https://...)`, and `[mention](athlete_slug)`. |
                    | 7 | SeasonFinalResult | Completed-season placement; `slug`, `season`, `place`, `clock`, `ageDiff` (biological minus chronological age; lower is better). |
                    | 8 | LongevitymaxxingChallengeResult | Original Challenge award; `challenge`, `pid` (public participant ID), `name`, `place`, `checkedIn` (days), `points`, `days` (duration), optional `slug` and `completed[1]`. Separate from current ongoing Challenge ranks. |
                    | 9 | BecamePro | Athlete qualified for Pro; `slug`. |
                    | 10 | BiologicalAgeImproved | Chronologically new personal-best biological age; `slug`, `clock` (`pheno` or `bortz`), `from`, `to` (biological ages). Older backfills create no improvement Event. |
                    | 11 | CrowdAgeTop10Change | Entry or upward move in the crowd age top 10; `slug`, `place`, `crowdAge`, `crowdCount`, optional `prevPlace` and `prev`. Historical published placement, not live rank. |
                    | 12 | AgeImprovementTop10Change | Pheno/Bortz improvement top-10 placement; `slug`, `clock`, `place`, `improvement` (latest eligible age minus worst eligible age), `ageReduction` (biological minus chronological age; lower is better), optional `prevPlace` and `prev`. |
                    | 13 | TestResultAccepted | Profile-only accepted test; `slug`, `date` (ISO laboratory date). Includes partial, non-improving, and backfilled results; one Event per athlete/test date across clocks. |

                    **Dates:** `OccurredAt` is an ISO 8601 UTC Event timestamp, not response generation time.
                    For type 13 it is first observed public announcement, while `date` is the laboratory measurement date.
                    Existing results were silently baselined when tracking began, so older publication dates remain unknown.
                    Type 10 uses the result date; type 7 uses season close; type 11 uses publication time after any cooldown.
                    Historical placements and badges do not assert the athlete's current standing.
                    """;
                SetResponseDescription(operation, "200", "Complete website-visible Event history, newest first; includes profile-only accepted results. Property names are case-sensitive.");
                SetResponseExample(operation, "200", """
                    [
                      {"Id":"accepted-result:example_athlete:2026-01-15","Type":13,"Text":"slug[example_athlete] date[2026-01-15]","OccurredAt":"2026-01-20T12:00:00Z","Relevance":5,"VisibleOnWebsite":true},
                      {"Id":"example-improvement-place","Type":12,"Text":"slug[example_athlete] clock[pheno] place[3] prevPlace[5] improvement[-2.5] ageReduction[-7.3]","OccurredAt":"2026-01-19T12:00:00Z","Relevance":10,"VisibleOnWebsite":true},
                      {"Id":"example-crowd","Type":11,"Text":"slug[example_athlete] place[6] prevPlace[8] crowdAge[39] crowdCount[128]","OccurredAt":"2026-01-18T12:00:00Z","Relevance":8,"VisibleOnWebsite":true},
                      {"Id":"example-improved","Type":10,"Text":"slug[example_athlete] clock[pheno] from[40.7] to[38.2]","OccurredAt":"2026-01-15T00:00:00Z","Relevance":9,"VisibleOnWebsite":true},
                      {"Id":"example-pro","Type":9,"Text":"slug[example_athlete]","OccurredAt":"2026-01-14T12:00:00Z","Relevance":9,"VisibleOnWebsite":true},
                      {"Id":"longevitymaxxing-result-example","Type":8,"Text":"challenge[longevitymaxxing] pid[example] name[Example Athlete] place[1] checkedIn[14] points[104] days[14] slug[example_athlete] completed[1]","OccurredAt":"2026-01-13T12:00:00Z","Relevance":9,"VisibleOnWebsite":true},
                      {"Id":"example-season","Type":7,"Text":"slug[example_athlete] season[2025] place[2] clock[pheno] ageDiff[-7.30]","OccurredAt":"2026-01-01T00:00:00Z","Relevance":2,"VisibleOnWebsite":true},
                      {"Id":"example-custom","Type":6,"Text":"A new year, a younger you!\n\nWelcome [bold](longevity athletes). [See the leaderboard](https://longevityworldcup.com/leaderboard)","OccurredAt":"2025-12-31T12:00:00Z","Relevance":15,"VisibleOnWebsite":true},
                      {"Id":"example-badge","Type":5,"Text":"slug[example_athlete] badge[PhenoAge Best Improvement] cat[Global] val[] place[1] solo[1]","OccurredAt":"2025-12-30T12:00:00Z","Relevance":8,"VisibleOnWebsite":true},
                      {"Id":"example-milestone","Type":4,"Text":"athletes[250]","OccurredAt":"2025-12-29T12:00:00Z","Relevance":8,"VisibleOnWebsite":true},
                      {"Id":"example-donation","Type":3,"Text":"tx[0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef] sats[100000]","OccurredAt":"2025-12-28T12:00:00Z","Relevance":9,"VisibleOnWebsite":true},
                      {"Id":"example-rank","Type":2,"Text":"slug[example_athlete] rank[5] prev[another_athlete]","OccurredAt":"2025-12-27T12:00:00Z","Relevance":10,"VisibleOnWebsite":true},
                      {"Id":"example-joined","Type":1,"Text":"slug[example_athlete]","OccurredAt":"2025-12-26T12:00:00Z","Relevance":5,"VisibleOnWebsite":true},
                      {"Id":"example-general","Type":0,"Text":"Welcome to Longevity World Cup!","OccurredAt":"2025-12-25T12:00:00Z","Relevance":5,"VisibleOnWebsite":true}
                    ]
                    """);
                break;

            case "/api/data/flags":
                operation.OperationId = "listFlags";
                operation.Summary = "List selectable flags";
                operation.Description = "Returns the complete public flag list accepted by athlete profile and onboarding flows.";
                SetResponseDescription(operation, "200", "Ordered list of selectable flag names.");
                SetResponseExample(operation, "200", """
                    [
                      "United States",
                      "Hungary",
                      "Prospera"
                    ]
                    """);
                break;

            case "/api/data/divisions":
                operation.OperationId = "listDivisions";
                operation.Summary = "List competition divisions";
                operation.Description = "Returns the public division names used to group longevity athletes.";
                SetResponseDescription(operation, "200", "Ordered list of division names.");
                SetResponseExample(operation, "200", """
                    [
                      "Men's",
                      "Women's",
                      "Open"
                    ]
                    """);
                break;

            case "/api/data/athletes":
                operation.OperationId = "listAthletes";
                operation.Summary = "List public longevity athlete data";
                operation.Description = "Returns the hydrated public athlete snapshot used by the website. Records include profile fields, public biomarker records, crowd age fields, generated asset URLs, and computed badges. The response may gain additional public fields over time.";
                SetResponseDescription(operation, "200", "Current public athlete snapshot.");
                SetResponseExample(operation, "200", """
                    [
                      {
                        "Name": "Example Athlete",
                        "DisplayName": "Example Athlete",
                        "MediaContact": "https://example.com/example-athlete",
                        "DateOfBirth": {
                          "Year": 1980,
                          "Month": 6,
                          "Day": 15
                        },
                        "Biomarkers": [
                          {
                            "Date": "2026-01-15",
                            "AlbGL": 46,
                            "CreatUmolL": 88.4,
                            "GluMmolL": 5.05,
                            "CrpMgL": 0.8,
                            "LymPc": 25.5,
                            "McvFL": 96.7,
                            "RdwPc": 12.5,
                            "AlpUL": 51,
                            "Wbc1000cellsuL": 6.3
                          }
                        ],
                        "Division": "Open",
                        "Flag": "United States",
                        "AthleteSlug": "example_athlete",
                        "CrowdAge": 39,
                        "CrowdCount": 128,
                        "IsNew": false,
                        "ProfileImageId": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        "ProfilePic": "/generated/profiles/athletes/example_athlete_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.webp?v=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        "ProfilePicThumb": "/generated/thumbs/athletes/example_athlete_thumb_sm_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.webp?v=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        "ProfilePicLeaderboardThumb": "/generated/thumbs/athletes/example_athlete_thumb_md_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef.webp?v=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                        "Proofs": [
                          "/athletes/example_athlete/proof_1.webp?v=123"
                        ],
                        "Badges": []
                      }
                    ]
                    """);
                break;

            case "/api/data/pheno-age":
                operation.OperationId = "calculatePhenoAge";
                operation.Summary = "Calculate Pheno Age";
                operation.Description = "Calculates a Pheno Age result from chronological age and the nine required blood biomarkers. The result includes Biological Age Difference, which can be passed to the hypothetical rank endpoint as the submitted biological age.";
                SetRequestExample(operation, """
                    {
                      "chronologicalAge": 45.5,
                      "albGL": 46,
                      "creatUmolL": 88.4,
                      "gluMmolL": 5.05,
                      "crpMgL": 0.8,
                      "wbc1000cellsuL": 6.3,
                      "lymPc": 25.5,
                      "mcvFL": 96.7,
                      "rdwPc": 12.5,
                      "alpUL": 51
                    }
                    """);
                SetResponseDescription(operation, "200", "Calculated Pheno Age result, Biological Age Difference, pace of aging, and domain contribution values.");
                SetResponseDescription(operation, "400", "Invalid request body or out-of-range Pheno Age input.");
                SetBadRequestSchema(operation, context);
                SetResponseExample(operation, "400", """
                    {
                      "message": "CRP must be greater than 0."
                    }
                    """);
                SetResponseExample(operation, "200", """
                    {
                      "biologicalAge": 38.82,
                      "biologicalAgeDifference": -6.68,
                      "paceOfAging": 0.85,
                      "domainContributions": {
                        "liver": -16.07,
                        "kidney": 9.31,
                        "metabolic": 10.94,
                        "inflammation": -2.67,
                        "immune": 75.05
                      }
                    }
                    """);
                break;

            case "/api/data/bortz-age":
                operation.OperationId = "calculateBortzAge";
                operation.Summary = "Calculate Bortz Age";
                operation.Description = "Calculates a Bortz Age result from chronological age and the public biomarker panel used by the Pro clock. Monocyte and neutrophil counts are derived from WBC and percentage inputs, matching the website calculation.";
                SetRequestExample(operation, """
                    {
                      "chronologicalAge": 45.5,
                      "albGL": 46,
                      "alpUL": 51,
                      "ureaMmolL": 5.4,
                      "cholesterolMmolL": 4.8,
                      "creatUmolL": 88.4,
                      "cystatinCMgL": 0.85,
                      "hba1cMmolMol": 34,
                      "crpMgL": 0.8,
                      "ggtUL": 22,
                      "rbc10e12L": 4.7,
                      "mcvFL": 96.7,
                      "rdwPc": 12.5,
                      "wbc1000cellsuL": 6.3,
                      "monocytePc": 7.2,
                      "neutrophilPc": 58.1,
                      "lymPc": 25.5,
                      "altUL": 24,
                      "shbgNmolL": 45,
                      "vitaminDNmolL": 85,
                      "gluMmolL": 5.05,
                      "mchPg": 31.5,
                      "apoA1GL": 1.55
                    }
                    """);
                SetResponseDescription(operation, "200", "Calculated Bortz Age result, Biological Age Difference, biological age acceleration, and derived count inputs.");
                SetResponseDescription(operation, "400", "Invalid request body or out-of-range Bortz Age input.");
                SetBadRequestSchema(operation, context);
                SetResponseExample(operation, "400", """
                    {
                      "message": "CRP, GGT, ALT, SHBG, and Vitamin D must be greater than 0."
                    }
                    """);
                SetResponseExample(operation, "200", """
                    {
                      "biologicalAge": 39.73,
                      "biologicalAgeDifference": -5.77,
                      "paceOfAging": 0.87,
                      "biologicalAgeAcceleration": -5.77,
                      "derivedMonocyteCount10e9L": 0.45,
                      "derivedNeutrophilCount10e9L": 3.66
                    }
                    """);
                break;

            case "/api/data/hypothetical-rank":
                operation.OperationId = "previewHypotheticalRank";
                operation.Summary = "Preview a hypothetical Ultimate League rank";
                operation.Description = "Calculates where a hypothetical biological age result would rank in the current Ultimate League field. Use `pheno` for an Amateur Pheno Age preview and `bortz` for a Pro Bortz Age preview.";
                SetRequestExample(operation, """
                    {
                      "calculator": "pheno",
                      "chronologicalAge": 45.5,
                      "biologicalAge": 38.2,
                      "birthYear": 1980,
                      "birthMonth": 6,
                      "birthDay": 15
                    }
                    """);
                SetResponseDescription(operation, "200", "Hypothetical rank, field sizes, signed age difference, and nearby rows.");
                SetResponseDescription(operation, "400", "Invalid request body, unsupported calculator, invalid date of birth, or out-of-range age value.");
                SetBadRequestSchema(operation, context);
                SetResponseExample(operation, "400", """
                    {
                      "message": "Calculator must be pheno or bortz."
                    }
                    """);
                SetResponseExample(operation, "200", """
                    {
                      "rank": 12,
                      "fieldSize": 61,
                      "currentFieldSize": 60,
                      "leagueName": "Ultimate League",
                      "category": "Amateur",
                      "ageDifference": -7.3,
                      "nearby": [
                        {
                          "rank": 10,
                          "name": "Nearby Athlete",
                          "category": "Amateur",
                          "ageDifference": -8.1,
                          "isHypothetical": false
                        },
                        {
                          "rank": 12,
                          "name": "Your result",
                          "category": "Amateur",
                          "ageDifference": -7.3,
                          "isHypothetical": true
                        }
                      ]
                    }
                    """);
                break;
        }
    }

    private static void SetBadRequestSchema(OpenApiOperation operation, OperationFilterContext context)
    {
        if (operation.Responses is null ||
            !operation.Responses.TryGetValue("400", out var response) ||
            response.Content is null ||
            !response.Content.TryGetValue("application/json", out var mediaType))
        {
            return;
        }

        var validationProblemSchema = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);
        var messageSchema = context.SchemaGenerator.GenerateSchema(typeof(PublicDataErrorResponse), context.SchemaRepository);
        mediaType.Schema = new OpenApiSchema
        {
            OneOf = [validationProblemSchema, messageSchema]
        };
    }

    private static void SetRequestExample(OpenApiOperation operation, string json)
    {
        if (operation.RequestBody?.Content is not null &&
            operation.RequestBody.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(json)!;
        }
    }

    private static void SetResponseDescription(OpenApiOperation operation, string statusCode, string description)
    {
        if (operation.Responses is not null &&
            operation.Responses.TryGetValue(statusCode, out var response))
        {
            response.Description = description;
        }
    }

    private static void SetResponseExample(OpenApiOperation operation, string statusCode, string json)
    {
        if (operation.Responses is not null &&
            operation.Responses.TryGetValue(statusCode, out var response) &&
            response.Content is not null &&
            response.Content.TryGetValue("application/json", out var mediaType))
        {
            mediaType.Example = JsonNode.Parse(json)!;
        }
    }
}
