using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EditProfilePageTests
{
    [Fact]
    public async Task InvalidPersonalLink_RemainsEditableAfterValidationFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var validationStart = html.IndexOf("function validatePersonalLink(value)", StringComparison.Ordinal);
        var validationEnd = html.IndexOf("function validateMediaContact(value)", StringComparison.Ordinal);

        Assert.True(validationStart >= 0);
        Assert.True(validationEnd > validationStart);

        var validationBody = html[validationStart..validationEnd];
        Assert.Contains("customAlert('Please enter a valid URL for your personal link.');", validationBody);
        Assert.DoesNotContain("restorePersonalLinkToOriginal();", validationBody);
    }
}
