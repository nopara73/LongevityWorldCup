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
                operation.Description = "Returns approved applicants only. Records include profile fields, public biomarker records, crowd age fields, generated asset URLs, and computed badges. Unranked OpenData profiles are available only from the leaderboard-profiles endpoint.";
                SetResponseDescription(operation, "200", "Current public athlete snapshot.");
                SetResponseExample(operation, "200", """
                    [
                      {
                        "ProfileType": "Athlete",
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
                        "ProfilePic": "/athletes/example_athlete/example_athlete.webp?v=123",
                        "ProfilePicThumb": "/generated/thumbs/athletes/example_athlete_thumb_sm.webp?v=123",
                        "ProfilePicLeaderboardThumb": "/generated/thumbs/athletes/example_athlete_thumb_md.webp?v=123",
                        "Proofs": [
                          "/athletes/example_athlete/proof_1.webp?v=123"
                        ],
                        "Badges": []
                      }
                    ]
                    """);
                break;

            case "/api/data/leaderboard-profiles":
                operation.OperationId = "listLeaderboardProfiles";
                operation.Summary = "List approved athletes and unranked open-data profiles";
                operation.Description = "Returns the combined leaderboard display feed. ProfileType=Athlete records are approved applicants. ProfileType=OpenData records are capped, unranked transcriptions of complete nine-marker Pheno panels from linked bloodwork self-published or explicitly authorized by the subject; their subjects did not apply and do not participate in competition outcomes.";
                SetResponseDescription(operation, "200", "Current combined leaderboard profile snapshot.");
                SetResponseExample(operation, "200", """
                    [
                      {
                        "ProfileType": "Athlete",
                        "Name": "Example Athlete",
                        "AthleteSlug": "example_athlete",
                        "Biomarkers": []
                      },
                      {
                        "ProfileType": "OpenData",
                        "Name": "Example Public Figure",
                        "AthleteSlug": "example_public_figure",
                        "OpenData": {
                          "SubjectDidNotApply": true,
                          "ReviewedAt": "2026-07-11",
                          "Aliases": ["Example Channel Name"],
                          "Sources": [
                            {
                              "Id": "bloodwork-2026",
                              "Kind": "Bloodwork",
                              "Title": "Public bloodwork dashboard",
                              "Url": "https://example.com/public-bloodwork",
                              "AccessedOn": "2026-07-11",
                              "SubjectAuthorization": {
                                "Kind": "SelfPublished"
                              }
                            },
                            {
                              "Id": "official-biography",
                              "Kind": "Identity",
                              "Title": "Official biography",
                              "Url": "https://example.com/biography",
                              "AccessedOn": "2026-07-11"
                            }
                          ],
                          "Notability": {
                            "Summary": "A globally recognized public figure with an established body of work.",
                            "SourceIds": ["official-biography"]
                          },
                          "Portrait": {
                            "SourcePageUrl": "https://commons.wikimedia.org/wiki/File:Example_portrait.jpg",
                            "OriginalUrl": "https://upload.wikimedia.org/example-portrait.jpg",
                            "Author": "Example Photographer",
                            "LicenseName": "CC BY 4.0",
                            "LicenseUrl": "https://creativecommons.org/licenses/by/4.0/",
                            "EditNote": "Cropped to a square and converted to WebP.",
                            "AssetUrl": "/public-data/example-public-figure/portrait?v=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                          },
                          "IdentitySourceIds": ["bloodwork-2026"]
                        },
                        "Biomarkers": [
                          {
                            "Date": "2026-01-15",
                            "DateBasis": "Collection",
                            "AgeYears": 45.5,
                            "SourceIds": ["bloodwork-2026"],
                            "AlbGL": 46,
                            "CreatUmolL": 80,
                            "GluMmolL": 5.1,
                            "CrpMgL": 0.4,
                            "LymPc": 31,
                            "McvFL": 89,
                            "RdwPc": 12.3,
                            "AlpUL": 58,
                            "Wbc1000cellsuL": 5.2
                          }
                        ],
                        "CurrentPlacement": null,
                        "Placements": [],
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
