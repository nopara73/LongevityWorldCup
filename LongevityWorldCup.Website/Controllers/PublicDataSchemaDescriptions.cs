using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LongevityWorldCup.Website.Controllers;

public sealed class PublicDataSchemaDescriptions : ISchemaFilter
{
    public void Apply(IOpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema is not OpenApiSchema openApiSchema)
        {
            return;
        }

        if (context.Type == typeof(PublicAthleteApiDocument))
        {
            openApiSchema.Description = "A hydrated leaderboard profile. ProfileType distinguishes approved athletes from unranked OpenData subjects who did not apply. The live endpoint may include additional public fields as the data model evolves.";
            openApiSchema.AdditionalPropertiesAllowed = true;

            if (openApiSchema.Properties is not null &&
                openApiSchema.Properties.TryGetValue("ProfileType", out var profileType) &&
                profileType is OpenApiSchema profileTypeSchema)
            {
                profileTypeSchema.Enum = [JsonValue.Create("Athlete"), JsonValue.Create("OpenData")];
            }
        }
        else if (context.Type == typeof(PublicBiomarkerRecordApiDocument))
        {
            openApiSchema.Description = "One dated biomarker record. Approved athletes may have Pheno Age-only records, Bortz Age records, or records that contain fields for both clocks. OpenData records include age at draw and a complete nine-marker Pheno panel cited to self-published bloodwork.";
            openApiSchema.AdditionalPropertiesAllowed = true;
        }
        else if (context.Type == typeof(PublicOpenDataMetadataApiDocument))
        {
            openApiSchema.Description = "Mandatory non-participation disclosure and reviewed provenance for an unranked OpenData profile.";
        }
        else if (context.Type == typeof(PublicOpenDataSourceApiDocument))
        {
            openApiSchema.Description = "An HTTPS identity or bloodwork source supporting an OpenData transcription.";

            if (openApiSchema.Properties is not null &&
                openApiSchema.Properties.TryGetValue("Kind", out var sourceKind) &&
                sourceKind is OpenApiSchema sourceKindSchema)
            {
                sourceKindSchema.Enum = [JsonValue.Create("Bloodwork"), JsonValue.Create("Identity")];
            }
        }
        else if (context.Type == typeof(PhenoAgeCalculationRequest))
        {
            openApiSchema.Description = "Inputs for calculating the Longevity World Cup Pheno Age result. Units match the public athlete biomarker JSON.";
        }
        else if (context.Type == typeof(BortzAgeCalculationRequest))
        {
            openApiSchema.Description = "Inputs for calculating the Longevity World Cup Bortz Age result. Units match the public athlete biomarker JSON; monocyte and neutrophil counts are derived from WBC and percentage values.";
        }
        else if (context.Type == typeof(PhenoAgeCalculationResult))
        {
            openApiSchema.Description = "Calculated Pheno Age result and derived values useful for rank previews.";
        }
        else if (context.Type == typeof(BortzAgeCalculationResult))
        {
            openApiSchema.Description = "Calculated Bortz Age result, biological age acceleration, and derived count inputs.";
        }
        else if (context.Type == typeof(HypotheticalRankRequest))
        {
            openApiSchema.Description = "Inputs for previewing where a hypothetical biological age result would rank in the current Ultimate League field.";
            openApiSchema.Example = JsonNode.Parse("""
                {
                  "calculator": "pheno",
                  "chronologicalAge": 45.5,
                  "biologicalAge": 38.2,
                  "birthYear": 1980,
                  "birthMonth": 6,
                  "birthDay": 15
                }
                """)!;

            if (openApiSchema.Properties is not null &&
                openApiSchema.Properties.TryGetValue("calculator", out var calculator) &&
                calculator is OpenApiSchema calculatorSchema)
            {
                calculatorSchema.Description = "Biological aging clock to preview: `pheno` for Amateur or `bortz` for Pro.";
                calculatorSchema.Enum = [JsonValue.Create("pheno"), JsonValue.Create("bortz")];
            }
        }
    }
}
