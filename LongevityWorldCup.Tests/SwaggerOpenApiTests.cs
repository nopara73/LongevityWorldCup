using System.Net;
using System.Text.Json;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SwaggerOpenApiTests
{
    private static readonly string[] PublicPaths =
    [
        "/api/data/athletes",
        "/api/data/flags",
        "/api/data/divisions",
        "/api/data/pheno-age",
        "/api/data/bortz-age",
        "/api/data/hypothetical-rank"
    ];

    private static readonly string[] PrivatePaths =
    [
        "/api/Application/application",
        "/api/Home/subscribe",
        "/api/custom-events",
        "/api/custom-event-preview/image",
        "/api/Guess/athlete-age"
    ];

    [Fact]
    public async Task SwaggerJson_IncludesOnlyPublicDataEndpoints()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        foreach (var path in PublicPaths)
        {
            Assert.True(paths.TryGetProperty(path, out _), $"Missing public path {path}.");
        }

        foreach (var path in PrivatePaths)
        {
            Assert.False(paths.TryGetProperty(path, out _), $"Private path {path} should not be documented.");
        }
    }

    [Fact]
    public async Task SwaggerJson_DoesNotDeclareAuthRequirement()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var root = document.RootElement;

        Assert.False(root.TryGetProperty("security", out _));
        if (root.TryGetProperty("components", out var components))
        {
            Assert.False(components.TryGetProperty("securitySchemes", out _));
        }

        foreach (var operation in EnumerateOperations(root.GetProperty("paths")))
        {
            Assert.False(operation.TryGetProperty("security", out _));
        }
    }

    [Fact]
    public async Task SwaggerJson_DocumentsHypotheticalRankRequestAndResponse()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/data/hypothetical-rank")
            .GetProperty("post");

        Assert.Equal("Preview a hypothetical Ultimate League rank", operation.GetProperty("summary").GetString());
        Assert.Equal("previewHypotheticalRank", operation.GetProperty("operationId").GetString());
        Assert.Contains("Pheno Age", operation.GetProperty("description").GetString());
        Assert.Contains("Bortz Age", operation.GetProperty("description").GetString());

        var requestSchema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        AssertSchemaReferences(requestSchema, "HypotheticalRankRequest");
        Assert.Equal("pheno", operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example")
            .GetProperty("calculator")
            .GetString());

        var responseSchema = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        AssertSchemaReferences(responseSchema, "HypotheticalRankResult");
        Assert.Equal("Ultimate League", operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("example")
            .GetProperty("leagueName")
            .GetString());
        var badRequestMediaType = operation
            .GetProperty("responses")
            .GetProperty("400")
            .GetProperty("content")
            .GetProperty("application/json");
        Assert.Equal("Calculator must be pheno or bortz.", badRequestMediaType
            .GetProperty("example")
            .GetProperty("message")
            .GetString());
        Assert.Equal(2, badRequestMediaType
            .GetProperty("schema")
            .GetProperty("oneOf")
            .GetArrayLength());
    }

    [Fact]
    public async Task SwaggerJson_DocumentsAthletesAsArray()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var schema = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/data/athletes")
            .GetProperty("get")
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");

        Assert.Equal("array", schema.GetProperty("type").GetString());
        AssertSchemaReferences(schema.GetProperty("items"), "PublicAthleteApiDocument");

        var operation = document.RootElement
            .GetProperty("paths")
            .GetProperty("/api/data/athletes")
            .GetProperty("get");
        Assert.Equal("List public longevity athlete data", operation.GetProperty("summary").GetString());
        Assert.Equal("listAthletes", operation.GetProperty("operationId").GetString());
        Assert.True(operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .TryGetProperty("example", out _));
    }

    [Fact]
    public async Task SwaggerJson_DocumentsAllPublicOperationsWithDescriptionsAndExamples()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        AssertOperationDocumented(paths.GetProperty("/api/data/flags").GetProperty("get"), "listFlags", "List selectable flags");
        AssertOperationDocumented(paths.GetProperty("/api/data/divisions").GetProperty("get"), "listDivisions", "List competition divisions");
        AssertOperationDocumented(paths.GetProperty("/api/data/athletes").GetProperty("get"), "listAthletes", "List public longevity athlete data");
        AssertOperationDocumented(paths.GetProperty("/api/data/pheno-age").GetProperty("post"), "calculatePhenoAge", "Calculate Pheno Age");
        AssertOperationDocumented(paths.GetProperty("/api/data/bortz-age").GetProperty("post"), "calculateBortzAge", "Calculate Bortz Age");
        AssertOperationDocumented(paths.GetProperty("/api/data/hypothetical-rank").GetProperty("post"), "previewHypotheticalRank", "Preview a hypothetical Ultimate League rank");
    }

    [Fact]
    public async Task SwaggerJson_DocumentsBiologicalAgingClockCalculationRequestsAndResponses()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var paths = document.RootElement.GetProperty("paths");

        AssertCalculationOperation(
            paths.GetProperty("/api/data/pheno-age").GetProperty("post"),
            "PhenoAgeCalculationRequest",
            "PhenoAgeCalculationResult",
            "biologicalAgeDifference");
        AssertCalculationOperation(
            paths.GetProperty("/api/data/bortz-age").GetProperty("post"),
            "BortzAgeCalculationRequest",
            "BortzAgeCalculationResult",
            "biologicalAgeAcceleration");

        var schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.Contains("Pheno Age result", schemas.GetProperty("PhenoAgeCalculationResult").GetProperty("description").GetString());
        Assert.Contains("Bortz Age result", schemas.GetProperty("BortzAgeCalculationResult").GetProperty("description").GetString());
    }

    [Fact]
    public async Task SwaggerJson_DocumentsProductionServerAndPublicInfo()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var root = document.RootElement;

        Assert.Equal("https://longevityworldcup.com", root
            .GetProperty("servers")[0]
            .GetProperty("url")
            .GetString());
        Assert.Contains("no-auth", root.GetProperty("info").GetProperty("description").GetString());
        Assert.Equal("https://longevityworldcup.com/", root
            .GetProperty("info")
            .GetProperty("contact")
            .GetProperty("url")
            .GetString());
    }

    [Fact]
    public async Task SwaggerJson_UsesLiveAthletePropertyNamesInSchema()
    {
        using var document = await LoadSwaggerDocumentAsync();
        var athleteSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PublicAthleteApiDocument");
        var athleteProperties = athleteSchema.GetProperty("properties");

        Assert.Contains("hydrated public longevity athlete record", athleteSchema.GetProperty("description").GetString());
        Assert.True(athleteProperties.TryGetProperty("Name", out _));
        Assert.True(athleteProperties.TryGetProperty("AthleteSlug", out _));
        Assert.True(athleteProperties.TryGetProperty("Biomarkers", out _));
        Assert.False(athleteProperties.TryGetProperty("name", out _));
        Assert.False(athleteProperties.TryGetProperty("athleteSlug", out _));

        var biomarkerSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PublicBiomarkerRecordApiDocument");
        Assert.Contains("Pheno Age-only records", biomarkerSchema.GetProperty("description").GetString());

        var errorSchema = document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("PublicDataErrorResponse");
        Assert.True(errorSchema.GetProperty("properties").TryGetProperty("message", out _));
    }

    [Fact]
    public async Task SwaggerUi_Loads()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/swagger");

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect,
            $"Unexpected /swagger status code: {(int)response.StatusCode} {response.StatusCode}.");
    }

    private static async Task<JsonDocument> LoadSwaggerDocumentAsync()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var stream = await client.GetStreamAsync("/swagger/v1/swagger.json");
        return await JsonDocument.ParseAsync(stream);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        return new TestWebApplicationFactory();
    }

    private static IEnumerable<JsonElement> EnumerateOperations(JsonElement paths)
    {
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                yield return method.Value;
            }
        }
    }

    private static void AssertSchemaReferences(JsonElement schema, string schemaName)
    {
        Assert.True(schema.TryGetProperty("$ref", out var reference), $"Schema should reference {schemaName}.");
        Assert.EndsWith($"/{schemaName}", reference.GetString());
    }

    private static void AssertOperationDocumented(JsonElement operation, string operationId, string summary)
    {
        Assert.Equal(operationId, operation.GetProperty("operationId").GetString());
        Assert.Equal(summary, operation.GetProperty("summary").GetString());
        Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("description").GetString()));
        Assert.True(operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .TryGetProperty("example", out _));
    }

    private static void AssertCalculationOperation(JsonElement operation, string requestSchemaName, string responseSchemaName, string expectedResponseExampleProperty)
    {
        var requestMediaType = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json");
        AssertSchemaReferences(requestMediaType.GetProperty("schema"), requestSchemaName);
        Assert.True(requestMediaType.TryGetProperty("example", out _));

        var responseMediaType = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json");
        AssertSchemaReferences(responseMediaType.GetProperty("schema"), responseSchemaName);
        Assert.True(responseMediaType.GetProperty("example").TryGetProperty(expectedResponseExampleProperty, out _));

        var badRequestMediaType = operation
            .GetProperty("responses")
            .GetProperty("400")
            .GetProperty("content")
            .GetProperty("application/json");
        Assert.Equal(2, badRequestMediaType.GetProperty("schema").GetProperty("oneOf").GetArrayLength());
        Assert.True(badRequestMediaType.TryGetProperty("example", out _));
    }
}
