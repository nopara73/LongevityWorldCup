using LongevityWorldCup.Website.Tools;
using LongevityWorldCup.Website;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(HttpTestCollections.ReadOnly)]
public sealed class LongevitymaxxingChallengePageTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task LongevitymaxxingPublicState_OmitsRetiredCallFields()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.Contains("\"challengeName\":\"Longevitymaxxing Challenge\"", json);
        Assert.Contains("\"startDate\":\"2026-06-08\"", json);
        Assert.Contains("\"signupClosesAtUtc\":\"2026-06-09T22:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-08T13:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T06:30:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T13:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-22T16:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-09T02:00:00.0000000+00:00\"", json);
        Assert.DoesNotContain("kem-kfpt-bhs", json);
        Assert.DoesNotContain("videoCallUrl", json);
        Assert.DoesNotContain("\"calls\":", json);
        Assert.DoesNotContain("callSelectionClosesAtUtc", json);
    }

    [Fact]
    public async Task RetiredCallEmailOptOutEndpoint_IsAbsent()
    {
        using var client = sharedFactory.CreateClient();
        using var body = new StringContent("{\"token\":\"retired-link\"}", System.Text.Encoding.UTF8, "application/json");
        using var response = await client.PostAsync("/api/longevitymaxxing/stop-community-call-emails", body);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task LongevitymaxxingPublicState_OmitsCallsForFutureCompetition()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2099-06-08",
                SignupClosesAtUtc = "2099-06-09T22:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.Contains("\"startDate\":\"2099-06-08\"", json);
        Assert.DoesNotContain("\"calls\":", json);
        Assert.DoesNotContain("T13:00:00.0000000+00:00", json);
        Assert.DoesNotContain("T16:00:00.0000000+00:00", json);
        Assert.DoesNotContain("\"startsAtUtc\":\"2026-06-08T06:30:00.0000000+00:00\"", json);
    }

    [Fact]
    public async Task Homepage_DoesNotAdvertiseLongevitymaxxingChallengeWhileSignupIsOpen()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2099-06-08",
                SignupClosesAtUtc = "2099-06-08T00:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("id=\"longevitymaxxingPromo\"", html);
        Assert.DoesNotContain("Don't feel ready for the Longevity World Cup yet? Try longevitymaxxing first.", html);
        Assert.DoesNotContain("Start longevitymaxxing", html);
        Assert.DoesNotContain("href=\"/longevitymaxxing\"", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
        Assert.DoesNotContain("does not affect Ultimate League rankings", html);
        Assert.DoesNotContain("/api/longevitymaxxing/state", html);
    }

    [Fact]
    public async Task Homepage_DoesNotAdvertiseLongevitymaxxingChallengeWhileActiveSignupIsOpen()
    {
        var now = DateTimeOffset.UtcNow;
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1).ToString("yyyy-MM-dd"),
                SignupClosesAtUtc = now.AddDays(1).ToString("O")
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("id=\"longevitymaxxingPromo\"", html);
        Assert.DoesNotContain("Start longevitymaxxing", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
    }

    [Fact]
    public async Task Homepage_DoesNotAdvertiseLongevitymaxxingChallengeBeforeStartAfterConfiguredSignupClose()
    {
        var now = DateTimeOffset.UtcNow;
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = DateOnly.FromDateTime(now.UtcDateTime).AddDays(10).ToString("yyyy-MM-dd"),
                SignupClosesAtUtc = now.AddDays(-1).ToString("O")
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");
        var json = await client.GetStringAsync("/api/longevitymaxxing/state");

        Assert.DoesNotContain("id=\"longevitymaxxingPromo\"", html);
        Assert.DoesNotContain("Start longevitymaxxing", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
        Assert.Contains("\"phase\":\"signup\"", json);
        Assert.Contains("\"signupOpen\":true", json);
    }

    [Fact]
    public async Task Homepage_DoesNotAdvertiseLongevitymaxxingChallengeAfterOriginalSignupClose()
    {
        using var factory = CreateFactory(new Config
        {
            LongevitymaxxingChallenge = new LongevitymaxxingChallengeConfig
            {
                StartDate = "2000-06-08",
                SignupClosesAtUtc = "2000-06-08T00:00:00Z"
            }
        });
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("id=\"longevitymaxxingPromo\"", html);
        Assert.DoesNotContain("Don't feel ready for the Longevity World Cup yet? Try longevitymaxxing first.", html);
        Assert.DoesNotContain("Start longevitymaxxing", html);
        Assert.DoesNotContain("Separate Lifestyle challenge", html);
        Assert.DoesNotContain("does not affect Ultimate League rankings", html);
    }

    private static WebApplicationFactory<Program> CreateFactory(Config? config = null)
    {
        return new TestWebApplicationFactory(builder =>
        {
            if (config is not null)
            {
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<Config>();
                    services.AddSingleton(config);
                });
            }
        });
    }
}
