using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class NostrIdentityTests
{
    [Theory]
    [InlineData("_", true)]
    [InlineData("longevityworldcup", true)]
    [InlineData("unknown", false)]
    public async Task DomainIdentity_ReturnsOnlyPublicDataWithoutRedirectsAndAllowsClientOrigins(string name, bool found)
    {
        await using var factory = Factory(NostrRelayTests.Configured());
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/nostr.json?name=" + name);
        request.Headers.Add("Origin", "https://nostr-client.example");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(NostrProtocolTests.TestPrivateKey, json);
        using var payload = JsonDocument.Parse(json);
        var names = payload.RootElement.GetProperty("names");
        Assert.Equal(found, names.TryGetProperty(name, out var identity));
        if (found) Assert.Equal(NostrProtocolTests.TestPublicKey, identity.GetString());
        Assert.Equal(2, payload.RootElement.GetProperty("relays").GetProperty(NostrProtocolTests.TestPublicKey).GetArrayLength());
    }

    [Fact]
    public async Task UnconfiguredIdentity_IsExplicitlyUnavailableAndStillAllowsClientOrigins()
    {
        await using var factory = Factory(new Config());
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/.well-known/nostr.json?name=_");
        request.Headers.Add("Origin", "https://nostr-client.example");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("*", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task DesignerApi_QueuesNostrWithoutWebsiteOrOtherChannels()
    {
        var config = NostrRelayTests.Configured();
        config.CustomEventDesignerSecretHash = SecretHashVerifier.CreateHash("test-designer-secret");
        await using var factory = Factory(config);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/custom-events", new
        {
            secret = "test-designer-secret", title = "A sport for time", content = "New announcement", sendToNostr = true
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("nostr", Assert.Single(payload.GetProperty("queuedTargets").EnumerateArray()).GetString());
        var store = factory.Services.GetRequiredService<SocialDeliveryStore>();
        Assert.Equal(payload.GetProperty("eventId").GetString(), Assert.Single(store.GetPending(SocialDeliveryStore.Nostr, true, DateTimeOffset.UtcNow)).Event.Id);
        Assert.Empty(store.GetPending(SocialDeliveryStore.Mastodon, true, DateTimeOffset.UtcNow));
    }

    private static TestWebApplicationFactory Factory(Config config) => new(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<Config>();
        services.AddSingleton(config);
    }));
}
