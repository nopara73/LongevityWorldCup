using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SelectedAthleteBootstrapPageTests
{
    [Theory]
    [InlineData("/onboarding/pheno-age.html", "if (isUpdate && !hasSelectedAthlete)")]
    [InlineData("/onboarding/bortz-age.html", "if (isUpdate && !hasSelectedAthlete)")]
    [InlineData("/play/proof-upload.html", "if (!isValidSelectedAthlete(athlete))")]
    [InlineData("/play/character-customization.html", "if (!isValidSelectedAthlete(athlete))")]
    [InlineData("/play/edit-profile.html", "if (!isValidSelectedAthlete(originalAthlete))")]
    public async Task SelectedAthleteRecovery_UsesSafeStorageCleanup(string path, string guard)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var redirectIndex = html.IndexOf("window.location.replace('/select-athlete');", StringComparison.Ordinal);
        var recoveryStart = html.LastIndexOf(guard, redirectIndex, StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0);
        Assert.True(redirectIndex > recoveryStart);

        var recoveryBody = html[recoveryStart..redirectIndex];

        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("removeSessionItem('selectedAthlete');", recoveryBody);
        Assert.Contains("removeSessionItem('tempAthlete');", recoveryBody);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    [InlineData("/play/proof-upload.html")]
    [InlineData("/play/character-customization.html")]
    [InlineData("/play/edit-profile.html")]
    public async Task SelectedAthleteValidation_RejectsArrays(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("function isValidSelectedAthlete(value)", html);
        Assert.Contains("!Array.isArray(value)", html);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    [InlineData("/play/proof-upload.html")]
    [InlineData("/play/character-customization.html")]
    [InlineData("/play/edit-profile.html")]
    public async Task SelectedAthleteValidation_RejectsBlankNames(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("typeof value.Name === 'string'", html);
        Assert.Contains("value.Name.trim()", html);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    public async Task UpdateBioageSelectedAthleteValidation_RejectsBlankDatesOfBirth(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("typeof value.DateOfBirth === 'string'", html);
        Assert.Contains("value.DateOfBirth.trim()", html);
    }

    [Fact]
    public async Task EditProfileTempAthleteFallback_UsesSafeStorageCleanup()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var tempReadIndex = html.IndexOf("const tempAthleteJson = getSessionItem('tempAthlete');", StringComparison.Ordinal);
        var tempCatchIndex = html.IndexOf("tempAthlete = null;", tempReadIndex, StringComparison.Ordinal);

        Assert.True(tempReadIndex >= 0);
        Assert.True(tempCatchIndex >= 0);

        var fallbackStart = html.LastIndexOf("} catch {", tempCatchIndex, StringComparison.Ordinal);
        var fallbackEnd = html.IndexOf("tempAthlete = null;", fallbackStart, StringComparison.Ordinal);

        Assert.True(fallbackStart >= 0);
        Assert.True(fallbackEnd > fallbackStart);

        var fallbackBody = html[fallbackStart..fallbackEnd];

        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("removeSessionItem('tempAthlete');", fallbackBody);
    }
}
