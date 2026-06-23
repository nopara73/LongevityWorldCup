using LongevityWorldCup.Website;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class GmailAuthTests
{
    [Theory]
    [InlineData(null, "secret", "refresh", "GmailClientId is not configured.")]
    [InlineData("id", null, "refresh", "GmailClientSecret is not configured.")]
    [InlineData("id", "secret", null, "GmailRefreshToken is not configured.")]
    public async Task GetAccessTokenAsync_RequiresOAuthConfigurationBeforeAuthenticating(
        string? clientId,
        string? clientSecret,
        string? refreshToken,
        string expectedMessage)
    {
        var config = new Config
        {
            GmailClientId = clientId,
            GmailClientSecret = clientSecret,
            GmailRefreshToken = refreshToken
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => GmailAuth.GetAccessTokenAsync(config));

        Assert.Equal(expectedMessage, exception.Message);
    }
}
