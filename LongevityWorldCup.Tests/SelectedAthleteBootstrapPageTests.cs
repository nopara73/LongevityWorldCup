using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SelectedAthleteBootstrapPageTests
{
    [Theory]
    [InlineData("/play/proof-upload.html")]
    [InlineData("/play/character-customization.html")]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    public async Task SelectedAthleteRecovery_IgnoresCleanupStorageFailures(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var redirectIndex = html.IndexOf("window.location.replace('/select-athlete');", StringComparison.Ordinal);

        Assert.True(redirectIndex >= 0);

        var recoveryStart = html.LastIndexOf("try {", redirectIndex, StringComparison.Ordinal);
        var recoveryEnd = html.IndexOf("}", redirectIndex, StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0);
        Assert.True(recoveryEnd > redirectIndex);

        var recoveryBody = html[recoveryStart..recoveryEnd];

        Assert.Contains("sessionStorage.removeItem('selectedAthlete');", recoveryBody);
        Assert.Contains("sessionStorage.removeItem('tempAthlete');", recoveryBody);
        Assert.Contains("} catch (_) {", recoveryBody);
    }

    [Fact]
    public async Task EditProfileSelectedAthleteRecovery_UsesSafeStorageCleanup()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var redirectIndex = html.IndexOf("window.location.replace('/select-athlete');", StringComparison.Ordinal);
        var recoveryStart = html.LastIndexOf("if (!isValidSelectedAthlete(originalAthlete))", redirectIndex, StringComparison.Ordinal);

        Assert.True(recoveryStart >= 0);
        Assert.True(redirectIndex > recoveryStart);

        var recoveryBody = html[recoveryStart..redirectIndex];

        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("removeSessionItem('selectedAthlete');", recoveryBody);
        Assert.Contains("removeSessionItem('tempAthlete');", recoveryBody);
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
