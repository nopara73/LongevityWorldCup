using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EditProfilePageTests
{
    [Fact]
    public async Task InvalidProfileFields_RemainEditableAfterValidationFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        AssertValidatorDoesNotRestore(html, "function validateFlagDisplay(value)", "function validatePersonalLink(value)", "restoreFlagToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validatePersonalLink(value)", "function validateMediaContact(value)", "restorePersonalLinkToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validateMediaContact(value)", "function validateWhyDisplay(value)", "restoreMediaContactToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validateWhyDisplay(value)", "</script>", "restoreWhyDisplayToOriginal();");
    }

    private static void AssertValidatorDoesNotRestore(string html, string startMarker, string endMarker, string restoreCall)
    {
        var validationStart = html.IndexOf(startMarker, StringComparison.Ordinal);
        var validationEnd = html.IndexOf(endMarker, validationStart, StringComparison.Ordinal);

        Assert.True(validationStart >= 0);
        Assert.True(validationEnd > validationStart);

        var validationBody = html[validationStart..validationEnd];
        Assert.DoesNotContain(restoreCall, validationBody);
    }

    [Fact]
    public async Task EditProfileFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("window.readApplicationErrorMessage(response).then(txt =>", html);
        Assert.DoesNotContain("response.text().then(txt =>", html);
    }
}
