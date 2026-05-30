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
            openApiSchema.Description = "A hydrated public longevity athlete record. The live endpoint may include additional public fields as the competition data model evolves.";
            openApiSchema.AdditionalPropertiesAllowed = true;
        }
        else if (context.Type == typeof(PublicBiomarkerRecordApiDocument))
        {
            openApiSchema.Description = "One dated biomarker record. Athletes may have Pheno Age-only records, Bortz Age records, or records that contain fields for both clocks.";
            openApiSchema.AdditionalPropertiesAllowed = true;
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
