using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class PublicRequestTimeoutTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public void PublicWorkTimeoutPolicy_IsConfigured()
    {
        var factory = sharedFactory;

        var options = factory.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value;

        Assert.True(options.Policies.TryGetValue(PublicRequestTimeoutPolicies.PublicWork, out var policy));
        Assert.Equal(PublicRequestTimeoutPolicies.PublicWorkTimeout, policy.Timeout);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, policy.TimeoutStatusCode);
        Assert.NotNull(policy.WriteTimeoutResponse);
    }

    [Fact]
    public void ApplicationSubmissionTimeoutPolicy_IsConfigured()
    {
        var factory = sharedFactory;

        var options = factory.Services.GetRequiredService<IOptions<RequestTimeoutOptions>>().Value;

        Assert.True(options.Policies.TryGetValue(PublicRequestTimeoutPolicies.ApplicationSubmission, out var policy));
        Assert.Equal(PublicRequestTimeoutPolicies.ApplicationSubmissionTimeout, policy.Timeout);
        Assert.Equal(StatusCodes.Status504GatewayTimeout, policy.TimeoutStatusCode);
        Assert.NotNull(policy.WriteTimeoutResponse);
        Assert.True(
            PublicRequestTimeoutPolicies.ApplicationSubmissionWorkTimeout
            < PublicRequestTimeoutPolicies.ApplicationSubmissionTimeout);
    }

    [Theory]
    [InlineData("api/data/hypothetical-rank")]
    [InlineData("api/custom-event-preview/image")]
    [InlineData("api/longevitymaxxing/check-in")]
    [InlineData("og/athlete/{slug}.png")]
    [InlineData("api/Bitcoin/btcusd")]
    [InlineData("ai/leaderboard.md")]
    public void ExpensivePublicEndpoints_UsePublicWorkTimeoutPolicy(string routePattern)
    {
        var factory = sharedFactory;
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

    [Fact]
    public void ApplicationSubmissionEndpoint_UsesDedicatedTimeoutPolicy()
    {
        var factory = sharedFactory;
        using var _ = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(
                endpoint.RoutePattern.RawText,
                "api/Application/application",
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint =>
        {
            var timeout = endpoint.Metadata.GetMetadata<RequestTimeoutAttribute>();
            Assert.NotNull(timeout);
            Assert.Equal(PublicRequestTimeoutPolicies.ApplicationSubmission, timeout.PolicyName);
        });
    }

    [Fact]
    public async Task ApplicationSubmissionTimeout_WaitsForDedicatedServerTimeout()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var match = Regex.Match(javascript, @"APPLICATION_SUBMISSION_TIMEOUT_MS\s*=\s*(\d+)");

        Assert.True(match.Success);
        var timeoutMs = int.Parse(match.Groups[1].Value);
        Assert.True(timeoutMs > PublicRequestTimeoutPolicies.ApplicationSubmissionTimeout.TotalMilliseconds);
    }
}
