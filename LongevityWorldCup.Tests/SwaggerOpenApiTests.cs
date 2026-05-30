using System.Net;
using System.Text.Json;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Hosting;
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

        var requestSchema = operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        AssertSchemaReferences(requestSchema, "HypotheticalRankRequest");

        var responseSchema = operation
            .GetProperty("responses")
            .GetProperty("200")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        AssertSchemaReferences(responseSchema, "HypotheticalRankResult");
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
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("EnableScheduledJobs", "false");
                builder.UseSetting("EnableStartupBadgeRefresh", "false");
            });
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
}
