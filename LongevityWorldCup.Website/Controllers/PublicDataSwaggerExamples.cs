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
                        "ProfilePic": "/athletes/example_athlete/example_athlete.webp?v=123",
                        "ProfilePicThumb": "/generated/profile-thumbs/example_athlete_thumb_sm.webp?v=123",
                        "ProfilePicLeaderboardThumb": "/generated/profile-thumbs/example_athlete_thumb_md.webp?v=123",
                        "Proofs": [
                          "/athletes/example_athlete/proof_1.webp?v=123"
                        ],
                        "Badges": []
                      }
                    ]
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
