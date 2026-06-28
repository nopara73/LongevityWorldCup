using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SelectedAthleteBootstrapPageTests
{
    [Theory]
    [InlineData("/onboarding/pheno-age.html", "if (isUpdate && !hasSelectedAthlete)")]
    [InlineData("/onboarding/bortz-age.html", "if (isUpdate && !hasSelectedAthlete)")]
    [InlineData("/play/proof-upload.html", "if (!isValidSelectedAthlete(athlete))")]
    [InlineData("/dashboard", "if (!isValidSelectedAthlete(athlete))")]
    [InlineData("/play/edit-profile.html", "if (!isValidSelectedAthlete(originalAthlete))")]
    public async Task SelectedAthleteRecovery_UsesSafeStorageCleanup(string path, string guard)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        if (path == "/dashboard")
        {
            var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
            var recoveryStart = flow.IndexOf("function readRequiredSelectedAthlete()", StringComparison.Ordinal);
            var redirectIndex = flow.IndexOf("window.location.replace(\"/select-athlete\");", recoveryStart, StringComparison.Ordinal);

            Assert.Contains("flow.readRequiredSelectedAthlete();", html);
            Assert.True(recoveryStart >= 0);
            Assert.True(redirectIndex > recoveryStart);

            var recoveryBody = flow[recoveryStart..redirectIndex];

            Assert.Contains("function removeSessionItem(key)", flow);
            Assert.Contains("removeSessionItem(\"selectedAthlete\");", recoveryBody);
            Assert.Contains("removeSessionItem(\"tempAthlete\");", recoveryBody);
            return;
        }

        var inlineRedirectIndex = html.IndexOf("window.location.replace('/select-athlete');", StringComparison.Ordinal);
        var inlineRecoveryStart = html.LastIndexOf(guard, inlineRedirectIndex, StringComparison.Ordinal);

        Assert.True(inlineRecoveryStart >= 0);
        Assert.True(inlineRedirectIndex > inlineRecoveryStart);

        var inlineRecoveryBody = html[inlineRecoveryStart..inlineRedirectIndex];

        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("removeSessionItem('selectedAthlete');", inlineRecoveryBody);
        Assert.Contains("removeSessionItem('tempAthlete');", inlineRecoveryBody);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    [InlineData("/play/proof-upload.html")]
    [InlineData("/dashboard")]
    [InlineData("/play/edit-profile.html")]
    public async Task SelectedAthleteValidation_RejectsArrays(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var validationSource = path == "/dashboard"
            ? await client.GetStringAsync("/js/play-athlete-flow.js")
            : html;

        if (path == "/dashboard")
        {
            Assert.Contains("flow.readRequiredSelectedAthlete();", html);
        }

        Assert.Contains("function isValidSelectedAthlete(value)", validationSource);
        Assert.Contains("!Array.isArray(value)", validationSource);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    [InlineData("/play/proof-upload.html")]
    [InlineData("/dashboard")]
    [InlineData("/play/edit-profile.html")]
    public async Task SelectedAthleteValidation_RejectsBlankNames(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);
        var validationSource = path == "/dashboard"
            ? await client.GetStringAsync("/js/play-athlete-flow.js")
            : html;

        if (path == "/dashboard")
        {
            Assert.Contains("flow.readRequiredSelectedAthlete();", html);
        }

        Assert.Contains("typeof value.Name === \"string\"", validationSource.Replace('\'', '"'));
        Assert.Contains("value.Name.trim()", validationSource);
    }

    [Theory]
    [InlineData("/onboarding/pheno-age.html")]
    [InlineData("/onboarding/bortz-age.html")]
    public async Task UpdateBioageSelectedAthleteValidation_RequiresUsableDateOfBirthParts(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("hasSelectedAthleteDateOfBirth(value.DateOfBirth)", html);
        Assert.Contains("function hasSelectedAthleteDateOfBirth(value)", html);
        Assert.Contains("if (!value || typeof value !== 'object' || Array.isArray(value)) return false;", html);
        Assert.Contains("const year = toSelectedAthleteDatePart(value.Year, 1, 9999);", html);
        Assert.Contains("const month = toSelectedAthleteDatePart(value.Month, 1, 12);", html);
        Assert.Contains("const day = toSelectedAthleteDatePart(value.Day, 1, 31);", html);
        Assert.Contains("const date = new Date(0);", html);
        Assert.Contains("date.setUTCFullYear(year, month - 1, day);", html);
        Assert.Contains("date.setUTCHours(0, 0, 0, 0);", html);
        Assert.Contains("date.getUTCFullYear() === year", html);
        Assert.Contains("date.getUTCMonth() === month - 1", html);
        Assert.Contains("date.getUTCDate() === day", html);
        Assert.Contains("function toSelectedAthleteDatePart(value, min, max)", html);
        Assert.Contains("if (typeof value === 'boolean' || value === null || value === undefined) return null;", html);
        Assert.Contains("? number", html);
        Assert.Contains(": null", html);
        Assert.DoesNotContain("typeof value.DateOfBirth === 'string'", html);
        Assert.DoesNotContain("value.DateOfBirth.trim()", html);
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
