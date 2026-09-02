using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BrowserTestAppTests
{
    [Fact]
    public void SynchronousFactoryDisposalRemovesItsWorkingDirectory()
    {
        var factory = new TestWebApplicationFactory();
        _ = factory.Services;
        var workingDirectory = factory.WorkingDirectory;

        Assert.True(Directory.Exists(workingDirectory));

        factory.Dispose();

        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task AsyncFactoryDisposalRemovesItsWorkingDirectory()
    {
        var factory = new TestWebApplicationFactory();
        _ = factory.Services;
        var workingDirectory = factory.WorkingDirectory;

        Assert.True(Directory.Exists(workingDirectory));

        await factory.DisposeAsync();

        Assert.False(Directory.Exists(workingDirectory));
    }

    [Fact]
    public async Task BitcoinEndpointsUseRealApplicationBehaviorWithDeterministicProviders()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var client = app.CreateClient();

        var price = await client.GetFromJsonAsync<BitcoinUsdResponse>("/api/Bitcoin/btcusd");
        var total = await client.GetFromJsonAsync<BitcoinTotalResponse>("/api/Bitcoin/total-received");
        var donation = await client.GetFromJsonAsync<BitcoinDonationResponse>("/api/Bitcoin/donation-address");

        Assert.NotNull(price);
        Assert.Equal(65_432.10m, price.BtcToUsdRate);
        Assert.NotNull(total);
        Assert.Equal(123_456_789, total.TotalReceivedSatoshis);
        Assert.NotNull(donation);
        Assert.StartsWith("bc1", donation.Address, StringComparison.Ordinal);

        var external = app.Services.GetRequiredService<DeterministicExternalHttpClientFactory>();
        Assert.Equal(2, external.Requests.Length);
        Assert.Contains(external.Requests, request => request.Host == "api.coingecko.com");
        Assert.Contains(external.Requests, request => request.Host == "blockchain.info");
    }

    [Fact]
    public async Task UnconfiguredExternalHostsFailClosedWithoutNetworkAccess()
    {
        var external = new DeterministicExternalHttpClientFactory();
        using var client = external.CreateClient("unconfigured-provider");

        using var response = await client.GetAsync("https://unconfigured.example.test/resource");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(
            [new Uri("https://unconfigured.example.test/resource")],
            external.Requests);
    }

    [Fact]
    public async Task ParallelStartsUseUniqueKestrelAssignedPorts()
    {
        var starts = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(BrowserTestApp.StartAsync))
            .ToArray();

        try
        {
            var apps = await Task.WhenAll(starts);
            Assert.All(apps, app => Assert.True(app.BaseAddress.IsLoopback));
            Assert.Equal(apps.Length, apps.Select(app => app.BaseAddress.Port).Distinct().Count());
        }
        finally
        {
            var startedApps = starts
                .Where(start => start.IsCompletedSuccessfully)
                .Select(start => start.Result);

            await Task.WhenAll(startedApps.Select(app => app.DisposeAsync().AsTask()));
        }
    }

    private sealed record BitcoinUsdResponse(decimal BtcToUsdRate);
    private sealed record BitcoinTotalResponse(long TotalReceivedSatoshis);
    private sealed record BitcoinDonationResponse(string Address);
}
