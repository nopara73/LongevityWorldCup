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
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("id=\"athleteDashboardPanel\"", html);
        Assert.Contains("id=\"athleteDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"athleteDashboardDynamicActions\"", html);
        Assert.Contains("#athleteDashboardDynamicActions", await client.GetStringAsync("/css/play-menu.css"));
        Assert.Contains("|| flow.getStoredSelectedAthlete();", playMenu);
        Assert.DoesNotContain("flow.readRequiredSelectedAthlete();", playMenu);
        Assert.Contains("flow.renderAthleteDashboardHeader(athlete, {", playMenu);
        Assert.Contains("flow.renderDashboardActions(athlete, {", playMenu);
        Assert.Contains("document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);", playMenu);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.DoesNotContain("object-fit: cover;", css);
        Assert.DoesNotContain("transform: scale(1.42);", css);
        Assert.Contains("function renderAthleteDashboardHeader(athlete, { titleElement, frameElement })", flow);
        Assert.Contains("return renderAthletePicture(frameElement, athlete, `${athleteDisplayName} headshot`);", flow);
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
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("Promise.resolve(window.modulesReady || undefined)", playMenu);
        Assert.Contains(".catch(() => {})", playMenu);
        Assert.Contains("flow.renderDashboardActions(athlete, {", playMenu);
        Assert.Contains("const ready = Promise.resolve(window.modulesReady || undefined).catch(() => {});", flow);
        Assert.Contains("return ready.catch(() => {}).then(() => {", flow);
        Assert.DoesNotContain("const ready = window.modulesReady || Promise.resolve();", html);
    }

    [Fact]
    public async Task DashboardRoute_ChangeAthleteUsesPlayShellSelectionRoute()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/dashboard");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("id=\"playDashboardBackBtn\" type=\"button\"", html);
        Assert.Contains("function navigateToSelectionPanel()", playMenu);
        Assert.Contains("navigateToPreviousPlayPanel('selection');", playMenu);
        Assert.Contains("showPlayPanel(fallbackPanelName, { historyMode: 'replace' });", playMenu);
        Assert.Contains("document.getElementById('playDashboardBackBtn').addEventListener('click', navigateToSelectionPanel);", playMenu);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }
}
