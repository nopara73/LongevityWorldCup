using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterCustomizationPageTests
{
    [Fact]
    public async Task DashboardRoute_UsesPlayShellDashboardPanel()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/dashboard");
        var css = await client.GetStringAsync("/css/play-athlete-flow.css");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("id=\"athleteDashboardPanel\"", html);
        Assert.Contains("id=\"athleteDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"athleteDashboardDynamicActions\"", html);
        Assert.Contains("#athleteDashboardDynamicActions", html);
        Assert.Contains("flow.readRequiredSelectedAthlete();", html);
        Assert.Contains("flow.renderAthleteDashboardHeader(athlete, {", html);
        Assert.Contains("flow.renderDashboardActions(athlete, {", html);
        Assert.Contains("document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);", html);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.Contains("object-fit: cover;", css);
        Assert.Contains("transform: scale(1.035);", css);
        Assert.Contains("function renderAthleteDashboardHeader(athlete, { titleElement, frameElement })", flow);
        Assert.Contains("renderAthletePicture(frameElement, athlete, `${athleteDisplayName} headshot`);", flow);
        Assert.Contains("function getAthletePictureImageSrc(athlete)", flow);
        Assert.Contains("athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb", flow);
        Assert.DoesNotContain("character-dashboard-main", html);
        Assert.DoesNotContain("characterDashboardPicture", html);
        Assert.DoesNotContain("characterBackButton", html);
    }

    [Fact]
    public async Task DashboardRoute_RendersActionsWhenModuleReadinessRejects()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/dashboard");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("Promise.resolve(window.modulesReady || undefined)", html);
        Assert.Contains(".catch(() => {})", html);
        Assert.Contains("flow.renderDashboardActions(athlete, {", html);
        Assert.Contains("const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});", flow);
        Assert.Contains("return ready.catch(() => {}).then(() => {", flow);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", html);
    }

    [Fact]
    public async Task DashboardRoute_ChangeAthleteUsesPlayShellRouteInsteadOfHistoryFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/dashboard");

        Assert.Contains("id=\"playDashboardBackBtn\" type=\"button\"", html);
        Assert.Contains("function navigateToSelectionPanel()", html);
        Assert.Contains("showAthleteSelection({ historyMode: 'replace' });", html);
        Assert.Contains("document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);", html);
        Assert.DoesNotContain("window.history.back()", html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }
}
