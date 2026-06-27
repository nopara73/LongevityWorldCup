using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterCustomizationPageTests
{
    [Fact]
    public async Task CharacterCustomization_RendersSelectedAthleteImageWithoutInnerHtml()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");

        Assert.Contains("typeof athlete?.DisplayName === 'string'", html);
        Assert.Contains("return typeof athlete?.Name === 'string' ? athlete.Name : '';", html);
        Assert.Contains("id=\"characterDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("aspect-ratio: 1 / 1;", html);
        Assert.Contains("object-fit: contain;", html);
        Assert.Contains(".athlete-picture-frame .athlete-picture-placeholder", html);
        Assert.Contains("object-fit: cover;", html);
        Assert.Contains("transform: scale(1.035);", html);
        Assert.Contains("function getAthletePictureImageSrc(value)", html);
        Assert.Contains("value.ProfilePic || value.ProfilePicLeaderboardThumb || value.ProfilePicThumb", html);
        Assert.Contains("const athleteImage = document.createElement('img');", html);
        Assert.Contains("athleteImage.src = getAthletePictureImageSrc(athlete);", html);
        Assert.Contains("athleteImage.alt = `${athleteDisplayName} headshot`;", html);
        Assert.Contains("athleteImage.className = 'illustration';", html);
        Assert.Contains("athleteImage.loading = 'lazy';", html);
        Assert.Contains("athleteImage.decoding = 'async';", html);
        Assert.Contains("document.getElementById('characterDashboardPicture').replaceChildren(athleteImage);", html);
        Assert.DoesNotContain("document.querySelector('picture').innerHTML", html);
        Assert.DoesNotContain("document.querySelector('picture').replaceChildren(athleteImage);", html);
    }

    [Fact]
    public async Task CharacterCustomization_RendersActionsWhenModuleReadinessRejects()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");

        Assert.Contains("const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});", html);
        Assert.Contains("ready.then(() => ensureProDiscountsLoaded()).catch(() => {}).then(() => {", html);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", html);
        Assert.DoesNotContain("ready.then(() => ensureProDiscountsLoaded()).then(() => {", html);
    }
}
