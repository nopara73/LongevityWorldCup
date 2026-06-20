using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterSelectionPageTests
{
    [Fact]
    public async Task AthleteSelection_NavigatesOnlyAfterStorageSucceeds()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("<button disabled id=\"confirmBtn\" class=\"option-button green\">", html);
        Assert.DoesNotContain("onclick=\"window.location.href='/dashboard'\"", html);
        Assert.Contains("if (!currentAthlete || !currentAthlete.Name) return;", html);
        Assert.Contains("localStorage.setItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.Contains("sessionStorage.setItem('selectedAthlete', JSON.stringify(currentAthlete));", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("window.location.href = '/dashboard';", html);
    }

    [Fact]
    public async Task AthleteSelection_RememberedAthletePrefillUsesOptionalStorageRead()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("const saved = getLocalItem('selectedAthleteName');", html);
        Assert.Contains("localStorage.setItem('selectedAthleteName', currentAthlete.Name);", html);
    }
}
