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
}
