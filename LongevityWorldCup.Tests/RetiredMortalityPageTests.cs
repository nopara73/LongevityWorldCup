using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class RetiredMortalityPageTests(TestWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/mortality-age")]
    [InlineData("/mortality-age?utm_source=test")]
    [InlineData("/MORTALITY-AGE/")]
    [InlineData("/onboarding/mortality-age")]
    [InlineData("/onboarding/mortality-age.html")]
    [InlineData("/js/mortality-age.js")]
    [InlineData("/js/mortality-age-model.js")]
    [InlineData("/css/mortality-age.css")]
    [InlineData("/research/mortality-age-model.json")]
    [InlineData("/research/mortality-age.pdf")]
    public async Task RetiredCalculatorAndPublicArtifacts_ReturnNotFound(string path)
    {
        // Campaign parameters are stripped by the site's existing redirect policy.
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = path.Contains('?') });
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal(path.Split('?')[0], response.RequestMessage?.RequestUri?.AbsolutePath);
        Assert.DoesNotContain("Mortality Age Calculator", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task ExistingCalculators_RemainAvailable(string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }
}
