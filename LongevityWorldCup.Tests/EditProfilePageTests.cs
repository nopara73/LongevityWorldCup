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

    [Fact]
    public async Task ProfilePictureSelection_ClearsInputAfterCapturingFile()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var selectionStart = html.IndexOf("function handleProfilePictureSelection(e)", StringComparison.Ordinal);
        var selectionEnd = html.IndexOf("changeProfileInput.addEventListener('change', handleProfilePictureSelection);", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(selectionEnd > selectionStart);

        var selectionBody = html[selectionStart..selectionEnd];
        Assert.Contains("const input = e.target;", selectionBody);
        Assert.Contains("const file = input.files[0];", selectionBody);
        Assert.Contains("input.value = '';", selectionBody);
    }
}
