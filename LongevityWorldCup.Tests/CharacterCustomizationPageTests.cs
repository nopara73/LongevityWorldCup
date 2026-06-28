using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterCustomizationPageTests
{
    [Fact]
    public async Task CharacterCustomization_RendersSelectedAthleteThroughSharedDashboardFlow()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");
        var css = await client.GetStringAsync("/css/play-athlete-flow.css");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("flow.readRequiredSelectedAthlete();", html);
        Assert.Contains("id=\"characterDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"characterDashboardDynamicActions\"", html);
        Assert.Contains("#characterDashboardDynamicActions", html);
        Assert.Contains("width: 100%;", html);
        Assert.Contains("align-items: stretch;", html);
        Assert.Contains("flow.renderAthleteDashboardHeader(athlete, {", html);
        Assert.Contains("flow.renderDashboardActions(athlete, {", html);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.Contains("object-fit: cover;", css);
        Assert.Contains("transform: scale(1.035);", css);
        Assert.Contains("function renderAthleteDashboardHeader(athlete, { titleElement, frameElement })", flow);
        Assert.Contains("renderAthletePicture(frameElement, athlete, `${athleteDisplayName} headshot`);", flow);
        Assert.Contains("function getAthletePictureImageSrc(athlete)", flow);
        Assert.Contains("athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb", flow);
        Assert.DoesNotContain("document.querySelector('picture').innerHTML", html);
        Assert.DoesNotContain("document.querySelector('picture').replaceChildren(athleteImage);", html);
    }

    [Fact]
    public async Task CharacterCustomization_RendersActionsWhenModuleReadinessRejects()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("Promise.resolve(window.modulesReady || undefined)", html);
        Assert.Contains(".catch(() => {})", html);
        Assert.Contains("flow.renderDashboardActions(athlete, {", html);
        Assert.Contains("const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});", flow);
        Assert.Contains("return ready.catch(() => {}).then(() => {", flow);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", html);
    }

    [Fact]
    public async Task CharacterCustomization_ChangeAthleteUsesExplicitRouteInsteadOfHistoryFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-customization.html");

        Assert.Contains("id=\"characterBackButton\" type=\"button\"", html);
        Assert.Contains("onclick=\"window.location.replace('/select-athlete')\"", html);
        Assert.Contains("<span class=\"dashboard-action-label\">Change athlete</span>", html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }
}
