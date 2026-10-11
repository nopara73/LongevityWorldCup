using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class MortalityAgeTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/mortality-age")]
    [InlineData("/mortality-age?utm_source=test")]
    public async Task ResearchCalculator_IsUnlistedNoindexAndLinksItsReport(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("noindex, nofollow", Assert.Single(response.Headers.GetValues("X-Robots-Tag")));
        Assert.Contains("<meta name=\"robots\" content=\"noindex, nofollow\"", html);
        Assert.Contains("<title>Mortality Age Calculator | Longevity World Cup</title>", html);
        Assert.Contains("/research/mortality-age.pdf", html);
        Assert.Contains("Experimental full-panel estimate", html);
        Assert.Contains("id=\"sex\" required", html);
        Assert.DoesNotContain("dob-", html);
        Assert.DoesNotContain("Date of birth", html);
        Assert.DoesNotContain("yearsText", html);
        Assert.DoesNotContain("{{ASSET_", html);
        Assert.DoesNotContain("bioage-rank-preview.js", html);
        foreach (var resource in new[] { "/sitemap.xml", "/llms.txt", "/llms-full.txt", "/ai/index.md", "/.well-known/agent-card.json", "/" })
            Assert.DoesNotContain("/mortality-age", await client.GetStringAsync(resource));
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, path));
        Assert.Equal("noindex, nofollow", Assert.Single(head.Headers.GetValues("X-Robots-Tag")));
    }

    [Fact]
    public async Task ModelAndPdf_AreVersionedReproducibleNoindexArtifacts()
    {
        using var client = factory.CreateClient();
        using var modelResponse = await client.GetAsync("/research/mortality-age-model.json");
        var modelBytes = await modelResponse.Content.ReadAsByteArrayAsync();
        using var model = JsonDocument.Parse(modelBytes);
        Assert.Equal(HttpStatusCode.OK, modelResponse.StatusCode);
        Assert.Equal("noindex, nofollow", Assert.Single(modelResponse.Headers.GetValues("X-Robots-Tag")));
        Assert.False(model.RootElement.GetProperty("fullJointValidation").GetBoolean());
        Assert.Equal(2, model.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(model.RootElement.GetProperty("requiresChronologicalAge").GetBoolean());
        Assert.Equal("withheld", model.RootElement.GetProperty("panels").GetProperty("fitness").GetProperty("releaseStatus").GetString());
        var full = model.RootElement.GetProperty("full");
        Assert.Equal(9, full.GetProperty("features").GetArrayLength());
        var coefficients = full.GetProperty("coefficients");
        foreach (var feature in full.GetProperty("features").EnumerateArray())
            Assert.True(coefficients.TryGetProperty(feature.GetString() + "_sex", out _));
        var models = model.RootElement.GetProperty("panels").EnumerateObject().Select(panel => panel.Value).Prepend(full);
        foreach (var riskModel in models)
        {
            Assert.False(riskModel.GetProperty("requiresChronologicalAge").GetBoolean());
            Assert.DoesNotContain(riskModel.GetProperty("coefficients").EnumerateObject(), coefficient => coefficient.Name.Contains("age", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(riskModel.GetProperty("riskInputs").EnumerateArray(), input => input.GetString() == "age");
            Assert.Equal(2, riskModel.GetProperty("trainingAgeRange").GetArrayLength());
            Assert.True(riskModel.GetProperty("reference").GetProperty("coefficients").TryGetProperty("age_female", out _));
        }
        using var pdfResponse = await client.GetAsync("/research/mortality-age.pdf");
        var pdf = await pdfResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(HttpStatusCode.OK, pdfResponse.StatusCode);
        Assert.Equal("application/pdf", pdfResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("noindex, nofollow", Assert.Single(pdfResponse.Headers.GetValues("X-Robots-Tag")));
        Assert.True(pdf.AsSpan().StartsWith("%PDF-"u8));
        Assert.True(pdf.Length > 100_000);
        using var pdfAgain = await client.GetAsync("/research/mortality-age.pdf");
        Assert.Equal(Convert.ToHexString(SHA256.HashData(pdf)), Convert.ToHexString(SHA256.HashData(await pdfAgain.Content.ReadAsByteArrayAsync())));
    }

    [Fact]
    public async Task PhysicalCalculatorAlias_RedirectsToUnlistedCanonicalUrl()
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync("/onboarding/mortality-age.html");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("/mortality-age", response.Headers.Location?.OriginalString);
    }
}
