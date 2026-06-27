using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PlayMenuPageTests
{
    [Fact]
    public async Task PlayMenu_HandlesUnavailableApplicationStorage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("function hasSubmittedApplication()", html);
        Assert.Contains("return getLocalItem('hasApplication') === 'true';", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const hasApp = hasSubmittedApplication();", html);
        Assert.DoesNotContain("return localStorage.getItem('hasApplication') === 'true';", html);
        Assert.DoesNotContain("const hasApp = localStorage.getItem('hasApplication') === 'true';", html);
    }

    [Fact]
    public async Task PlayMenu_AlreadyAthletePathStaysInlineAndPreservesUrls()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("id=\"continueGameBtn\" type=\"button\"", html);
        Assert.Contains("id=\"athleteSelectionPanel\"", html);
        Assert.Contains("id=\"athleteDashboardPanel\"", html);
        Assert.Contains("function showAthleteSelection(options = {})", html);
        Assert.Contains("function showAthleteDashboard(athlete, options = {})", html);
        Assert.Contains("return '/select-athlete';", html);
        Assert.Contains("return '/dashboard';", html);
        Assert.Contains("window.history.pushState(state, '', route);", html);
        Assert.Contains("window.addEventListener('popstate', showPanelForCurrentUrl);", html);
        Assert.Contains("contBtn.addEventListener('click', () => showAthleteSelection({ historyMode: 'push' }));", html);
        Assert.Contains("showAthleteDashboard(currentAthlete, { historyMode: 'push' });", html);
        Assert.DoesNotContain("onclick=\"window.location.href='/select-athlete'\"", html);
        Assert.DoesNotContain("window.location.href = '/dashboard';", html);
        Assert.DoesNotContain("function scrollPanelIntoView", html);
        Assert.DoesNotContain("requestAnimationFrame(() => scrollPanelIntoView", html);
        Assert.DoesNotContain("scrollIntoView({ behavior: 'smooth', block: 'start' });", html);
    }

    [Fact]
    public async Task PlayMenu_InlineDashboardKeepsRealTasksAsNavigations()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("onclick=\"window.location.href='/join'\"", html);
        Assert.Contains("window.location.href='/edit-profile'", html);
        Assert.Contains("'/pheno-age?update=1'", html);
        Assert.Contains("'/bortz-age?update=1'", html);
        Assert.Contains("setSessionItem('selectedAthlete', JSON.stringify(currentAthlete))", html);
        Assert.Contains("removeSessionItem('biomarkerData');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
    }

    [Fact]
    public async Task PlayMenu_AthleteSelectionTransitionsProfilePictureAfterImageLoads()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("id=\"athleteSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("id=\"athleteDashboardPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("width: min(100%, 408px);", html);
        Assert.Contains("aspect-ratio: 1 / 1;", html);
        Assert.Contains("border: 4px solid var(--dark-text-color);", html);
        Assert.Contains("box-shadow: 0 4px 15px rgba(0, 0, 0, 0.4);", html);
        Assert.Contains("object-fit: contain;", html);
        Assert.Contains(".athlete-picture-frame .athlete-picture-placeholder", html);
        Assert.Contains("object-fit: cover;", html);
        Assert.Contains("transform: scale(1.035);", html);
        Assert.Contains("border: 0;", html);
        Assert.Contains("class=\"illustration athlete-picture-placeholder\"", html);
        Assert.Contains("image.className = 'illustration athlete-picture-placeholder';", html);
        Assert.Contains("function createDefaultAthleteImage()", html);
        Assert.Contains("image.className = 'illustration athlete-picture-placeholder athlete-picture-next';", html);
        Assert.Contains("transitionAthleteSelectionImage(createDefaultAthleteImage(), '../assets/content-images/headshot.jpg');", html);
        Assert.Contains("const ATHLETE_PICTURE_TRANSITION_MS = 180;", html);
        Assert.Contains("let athletePictureTransitionToken = 0;", html);
        Assert.Contains("function transitionAthleteSelectionImage(image, src)", html);
        Assert.Contains("function getAthletePictureImageSrc(athlete)", html);
        Assert.Contains("athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb", html);
        Assert.Contains("athleteImage.src = getAthletePictureImageSrc(athlete);", html);
        Assert.Contains("let hasFinished = false;", html);
        Assert.Contains("image.loading = 'eager';", html);
        Assert.Contains("image.addEventListener('load', finishImageSwap, { once: true });", html);
        Assert.Contains("frame.appendChild(image);", html);
        Assert.Contains("currentMedia.classList.add('is-exiting');", html);
        Assert.Contains("frame.replaceChildren(image);", html);
        Assert.Contains("transitionAthleteSelectionImage(image, getAthletePictureImageSrc(athlete));", html);
        Assert.Contains("function replaceAthleteSelectionImageImmediately(image, src)", html);
    }

    [Fact]
    public async Task PlayMenu_AthleteSelectionHydratesStoredAthleteBeforePanelShows()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");
        var showStart = html.IndexOf("function showAthleteSelection(options = {})", StringComparison.Ordinal);
        var hydrate = html.IndexOf("hydrateStoredAthleteSelection();", showStart, StringComparison.Ordinal);
        var showPanel = html.IndexOf("showPanel('athleteSelectionPanel');", showStart, StringComparison.Ordinal);
        var load = html.IndexOf("loadAthletes().catch(() => {});", showStart, StringComparison.Ordinal);

        Assert.True(showStart >= 0);
        Assert.True(hydrate > showStart);
        Assert.True(showPanel > hydrate);
        Assert.True(load > showPanel);
        Assert.Contains("function hydrateStoredAthleteSelection()", html);
        Assert.Contains("const storedAthlete = currentAthlete || getStoredSelectedAthlete();", html);
        Assert.Contains("renderSelectedAthletePreview(storedAthlete, { transition: false });", html);
        Assert.Contains("replaceAthleteSelectionImageImmediately(image, getAthletePictureImageSrc(athlete));", html);
        Assert.Contains("if (currentAthlete && isAthleteInputValue(currentAthlete, athleteInput.value)) return false;", html);
        Assert.Contains("if (!currentAthlete)", html);
        Assert.Contains("renderAthleteMatches();", html);
    }

    [Fact]
    public async Task PlayMenu_LoadsInlineDashboardHelpersThroughInjectedHeadAssets()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("/js/misc.js", html);
        Assert.Contains("/js/pheno-age.js", html);
        Assert.Contains("/js/bortz-age.js", html);
        Assert.Contains("/js/badges.js", html);
        Assert.Contains("/js/proof-helpers.js", html);
        Assert.Contains("/js/pro-discounts.js", html);
    }
}
