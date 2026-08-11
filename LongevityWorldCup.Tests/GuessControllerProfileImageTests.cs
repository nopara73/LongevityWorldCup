using System.Net;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class GuessControllerProfileImageTests
{
    private const string AthleteSlug = "ron_lugbill";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-sha256-hash")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdeg")]
    public async Task AthleteAgeGuess_WithMissingOrMalformedProfileImageId_ReturnsBadRequestWithoutActualAge(
        string? profileImageId)
    {
        using var factory = new TestWebApplicationFactory();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        var actualAge = athletes.GetActualAge(AthleteSlug);
        var before = athletes.GetCrowdStats(AthleteSlug);
        using var client = CreateClient(factory);

        var profileImageQuery = profileImageId is null ? string.Empty : $"&profileImageId={profileImageId}";
        using var response = await client.PostAsync(
            $"/api/Guess/athlete-age?athleteName={AthleteSlug}&ageGuess={actualAge}{profileImageQuery}",
            new StringContent(""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, athletes.GetCrowdStats(AthleteSlug));
        Assert.DoesNotContain("actualAge", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AthleteAgeGuess_WithUnknownAthlete_ReturnsNotFoundWithoutActualAge()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            "/api/Guess/athlete-age?athleteName=not_an_athlete&ageGuess=40&profileImageId=" + new string('a', 64),
            new StringContent(""));
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("actualAge", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AthleteAgeGuess_WithStaleProfileImageId_ReturnsConflictWithoutRecordingGuess()
    {
        using var factory = new TestWebApplicationFactory();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));
        Assert.Equal(64, currentImageId.Length);

        var staleImageId = (currentImageId[0] == '0' ? '1' : '0') + currentImageId[1..];
        var actualAge = athletes.GetActualAge(AthleteSlug);
        var before = athletes.GetCrowdStats(AthleteSlug);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            $"/api/Guess/athlete-age?athleteName={AthleteSlug}&ageGuess={actualAge}&profileImageId={staleImageId}",
            new StringContent(""));
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("profile_image_changed", json.RootElement.GetProperty("code").GetString());
        Assert.Equal(before, athletes.GetCrowdStats(AthleteSlug));
        Assert.DoesNotContain("actualAge", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AthleteAgeGuess_WithUppercaseCurrentProfileImageId_IsAcceptedAndNormalized()
    {
        using var factory = new TestWebApplicationFactory();
        var athletes = factory.Services.GetRequiredService<AthleteDataService>();
        Assert.True(athletes.TryGetProfileImageId(AthleteSlug, out var currentImageId));

        var actualAge = athletes.GetActualAge(AthleteSlug);
        var before = athletes.GetCrowdStats(AthleteSlug);
        using var client = CreateClient(factory);

        using var response = await client.PostAsync(
            $"/api/Guess/athlete-age?athleteName={AthleteSlug}&ageGuess={actualAge}&profileImageId={currentImageId.ToUpperInvariant()}",
            new StringContent(""));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(json.RootElement.GetProperty("guessAccepted").GetBoolean());
        Assert.Equal(actualAge, json.RootElement.GetProperty("actualAge").GetInt32());
        Assert.Equal(currentImageId, json.RootElement.GetProperty("profileImageId").GetString());
        Assert.Equal(before.Item2 + 1, athletes.GetCrowdStats(AthleteSlug).Item2);
    }

    private static HttpClient CreateClient(TestWebApplicationFactory factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
