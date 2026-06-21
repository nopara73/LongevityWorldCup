using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterSelectionPageTests
{
    [Fact]
    public async Task AthleteSelection_NavigatesOnlyAfterRequiredSessionStorageSucceeds()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("<button disabled id=\"confirmBtn\" class=\"option-button green\">", html);
        Assert.DoesNotContain("onclick=\"window.location.href='/dashboard'\"", html);
        Assert.Contains("if (!currentAthlete || !currentAthlete.Name) return;", html);
        Assert.Contains("const prevName = getLocalItem('selectedAthleteName');", html);
        Assert.Contains("sessionStorage.setItem('selectedAthlete', JSON.stringify(currentAthlete));", html);
        Assert.Contains("sessionStorage.removeItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.Contains("window.location.href = '/dashboard';", html);
    }

    [Fact]
    public async Task AthleteSelection_RememberedAthletePrefillUsesOptionalStorageRead()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("function setLocalItem(key, value)", html);
        Assert.Contains("function removeLocalItem(key)", html);
        Assert.Contains("const saved = getLocalItem('selectedAthleteName');", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.DoesNotContain("const prevName = localStorage.getItem('selectedAthleteName');", html);
    }

    [Fact]
    public async Task AthleteSelection_AthleteListFailureShowsRetryableInputError()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("let athleteLoadPromise = null;", html);
        Assert.Contains("function retryAthleteLoad()", html);
        Assert.Contains("athleteInput.addEventListener('focus', retryAthleteLoad);", html);
        Assert.Contains("athleteInput.addEventListener('input', retryAthleteLoad);", html);
        Assert.Contains("console.error('Error fetching athletes:', error);", html);
        Assert.Contains("athleteError.textContent = 'Athlete list could not load. Check your connection and try again.';", html);
        Assert.Contains("loadAthletes().catch(() => {});", html);
        Assert.DoesNotContain(".catch(console.error);", html);
    }
}
