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

        Assert.Contains("const athleteImage = document.createElement('img');", html);
        Assert.Contains("athleteImage.src = athlete.ProfilePic;", html);
        Assert.Contains("athleteImage.alt = `${athleteDisplayName} headshot`;", html);
        Assert.Contains("athleteImage.className = 'illustration';", html);
        Assert.Contains("athleteImage.loading = 'lazy';", html);
        Assert.Contains("document.querySelector('picture').replaceChildren(athleteImage);", html);
        Assert.DoesNotContain("document.querySelector('picture').innerHTML", html);
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
