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
        var isBioagePage = path is "/onboarding/pheno-age.html" or "/onboarding/bortz-age.html";
        if (path == "/dashboard")
        {
            var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
            var recoveryStart = flow.IndexOf("function getStoredSelectedAthlete()", StringComparison.Ordinal);
            var recoveryEnd = flow.IndexOf("function isValidSelectedAthlete(value)", recoveryStart, StringComparison.Ordinal);

            Assert.Contains("flow.getStoredSelectedAthlete();", html);
            Assert.True(recoveryStart >= 0);
            Assert.True(recoveryEnd > recoveryStart);

            var recoveryBody = flow[recoveryStart..recoveryEnd];

            Assert.Contains("function removeSessionItem(key)", flow);
            Assert.Contains("removeSessionItem(\"selectedAthlete\");", recoveryBody);
            Assert.Contains("removeSessionItem(\"tempAthlete\");", recoveryBody);
            return;
        }

        if (isBioagePage)
        {
            var flow = await client.GetStringAsync("/js/bioage-flow.js");
            var recoveryStart = flow.IndexOf("function redirectMissingSelectedAthlete(removeItem)", StringComparison.Ordinal);
            var redirectIndex = flow.IndexOf("window.location.replace('/select-athlete');", recoveryStart, StringComparison.Ordinal);

            Assert.Contains("bioageFlow.redirectMissingSelectedAthlete(removeSessionItem);", html);
            Assert.True(recoveryStart >= 0);
            Assert.True(redirectIndex > recoveryStart);

            var recoveryBody = flow[recoveryStart..redirectIndex];

            Assert.Contains("const removeSessionItem = bioageFlow.removeSessionItem;", html);
            Assert.Contains("remove('selectedAthlete');", recoveryBody);
            Assert.Contains("remove('tempAthlete');", recoveryBody);
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
        var isBioagePage = path is "/onboarding/pheno-age.html" or "/onboarding/bortz-age.html";
        var validationSource = path == "/dashboard"
            ? await client.GetStringAsync("/js/play-athlete-flow.js")
            : isBioagePage
                ? await client.GetStringAsync("/js/bioage-flow.js")
                : html;

        if (path == "/dashboard")
        {
            Assert.Contains("flow.getStoredSelectedAthlete();", html);
        }
        else if (isBioagePage)
        {
            Assert.Contains("const isValidSelectedAthlete = bioageFlow.isValidSelectedAthlete;", html);
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
        var isBioagePage = path is "/onboarding/pheno-age.html" or "/onboarding/bortz-age.html";
        var validationSource = path == "/dashboard"
            ? await client.GetStringAsync("/js/play-athlete-flow.js")
            : isBioagePage
                ? await client.GetStringAsync("/js/bioage-flow.js")
                : html;

        if (path == "/dashboard")
        {
            Assert.Contains("flow.getStoredSelectedAthlete();", html);
        }
        else if (isBioagePage)
        {
            Assert.Contains("const isValidSelectedAthlete = bioageFlow.isValidSelectedAthlete;", html);
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
        var flow = await client.GetStringAsync("/js/bioage-flow.js");

        Assert.Contains("const isValidSelectedAthlete = bioageFlow.isValidSelectedAthlete;", html);
        Assert.Contains("hasSelectedAthleteDateOfBirth(value.DateOfBirth)", flow);
        Assert.Contains("function hasSelectedAthleteDateOfBirth(value)", flow);
        Assert.Contains("if (!value || typeof value !== 'object' || Array.isArray(value)) return false;", flow);
        Assert.Contains("const year = toSelectedAthleteDatePart(value.Year, 1, 9999);", flow);
        Assert.Contains("const month = toSelectedAthleteDatePart(value.Month, 1, 12);", flow);
        Assert.Contains("const day = toSelectedAthleteDatePart(value.Day, 1, 31);", flow);
        Assert.Contains("const date = new Date(0);", flow);
        Assert.Contains("date.setUTCFullYear(year, month - 1, day);", flow);
        Assert.Contains("date.setUTCHours(0, 0, 0, 0);", flow);
        Assert.Contains("date.getUTCFullYear() === year", flow);
        Assert.Contains("date.getUTCMonth() === month - 1", flow);
        Assert.Contains("date.getUTCDate() === day", flow);
        Assert.Contains("function toSelectedAthleteDatePart(value, min, max)", flow);
        Assert.Contains("if (typeof value === 'boolean' || value === null || value === undefined) return null;", flow);
        Assert.Contains("? number", flow);
        Assert.Contains(": null", flow);
        Assert.DoesNotContain("typeof value.DateOfBirth === 'string'", flow);
        Assert.DoesNotContain("value.DateOfBirth.trim()", flow);
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
