using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PublicRequestTimeoutTests
{
    [Fact]
    public void PublicWorkTimeoutPolicy_IsConfigured()
    {
        using var factory = new TestWebApplicationFactory();

        var options = factory.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value;

        Assert.True(options.Policies.TryGetValue(PublicRequestTimeoutPolicies.PublicWork, out var policy));
        Assert.Equal(PublicRequestTimeoutPolicies.PublicWorkTimeout, policy.Timeout);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, policy.TimeoutStatusCode);
        Assert.NotNull(policy.WriteTimeoutResponse);
    }

    [Theory]
    [InlineData("api/Application/application")]
    [InlineData("api/data/hypothetical-rank")]
    [InlineData("api/custom-event-preview/image")]
    [InlineData("api/longevitymaxxing/check-in")]
    [InlineData("og/athlete/{slug}.png")]
    [InlineData("api/Bitcoin/btcusd")]
    [InlineData("ai/leaderboard.md")]
    public void ExpensivePublicEndpoints_UsePublicWorkTimeoutPolicy(string routePattern)
    {
        using var factory = new TestWebApplicationFactory();
        using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            var timeout = endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>();
            Assert.NotNull(timeout);
            Assert.Equal(PublicRequestTimeoutPolicies.PublicWork, timeout.PolicyName);
        });
    }
}
