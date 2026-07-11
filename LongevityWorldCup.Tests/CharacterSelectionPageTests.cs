using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CharacterSelectionPageTests
{
    [Fact]
    public async Task SelectAthleteRoute_UsesPlayShellSelectionPanel()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/select-athlete");
        var playMenu = await client.GetStringAsync("/js/play-menu.js");

        Assert.Contains("id=\"athleteSelectionPanel\"", html);
        Assert.Contains("id=\"athleteSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("<label for=\"playAthleteInput\" class=\"visually-hidden\">Athlete name</label>", html);
        Assert.Contains("id=\"playAthleteInput\"", html);
        Assert.Contains("id=\"playConfirmAthleteBtn\"", html);
        Assert.Contains("function navigateToStartPanel()", playMenu);
        Assert.Contains("flow.persistSelectedAthlete(currentAthlete)", playMenu);
        Assert.Contains("showPlayPanel('dashboard', { historyMode: 'push' });", playMenu);
        Assert.DoesNotContain("character-selection-main", html);
        Assert.DoesNotContain("id=\"confirmBtn\"", html);
        Assert.DoesNotContain("window.location.href = '/dashboard';", html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }

    [Fact]
    public async Task AthleteSelection_UsesSharedControllerForStorageHydrationAndRetryableLoading()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/select-athlete");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("/js/play-athlete-flow.js", html);
        Assert.Contains("/css/play-athlete-flow.css", html);
        Assert.Contains("/js/play-menu.js", html);
        var playMenu = await client.GetStringAsync("/js/play-menu.js");
        Assert.Contains("flow.createAthleteSelectionController({", playMenu);
        Assert.Contains("errorElement: document.getElementById('playAthleteError')", playMenu);
        Assert.Contains("}).bind();", playMenu);
        Assert.Contains("function getStoredSelectedAthlete()", flow);
        Assert.Contains("function hydrateStoredAthleteSelection()", flow);
        Assert.Contains("renderSelectedAthletePreview(storedAthlete, { transition: false });", flow);
        Assert.Contains("function loadAthletes(loadOptions = {})", flow);
        Assert.Contains("let athleteLoadPromise = null;", flow);
        Assert.Contains("fetch(athleteApiPath)", flow);
        Assert.Contains("console.error(\"Error fetching athletes:\", error);", flow);
        Assert.Contains("errorElement.textContent = \"Athlete list could not load. Check your connection and try again.\";", flow);
        Assert.Contains("if (saved && !currentAthlete && !hasUserEditedInput)", flow);
    }

    [Fact]
    public async Task AthleteSelection_SearchesDisplayNamesAndKeepsCanonicalSelectionInSharedController()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("function getAthleteDisplayName(athlete)", flow);
        Assert.Contains("function getAthleteCanonicalName(athlete)", flow);
        Assert.Contains("return `${getAthleteCanonicalName(athlete)} ${getAthleteDisplayName(athlete)}`.toLowerCase();", flow);
        Assert.Contains("typeof athlete.DisplayName === \"string\"", flow);
        Assert.Contains("return athlete && typeof athlete.Name === \"string\" ? athlete.Name : \"\";", flow);
        Assert.Contains("return athlete && typeof athlete.Name === \"string\" ? athlete.Name.trim() : \"\";", flow);
        Assert.Contains("if (terms.every(term => searchText.includes(term)))", flow);
        Assert.Contains("if (!getAthleteCanonicalName(athlete)) return;", flow);
        Assert.Contains("appendHighlightedText(item, displayName, first);", flow);
        Assert.Contains("item.dataset.value = athlete.Name;", flow);
        Assert.Contains("input.value = displayName;", flow);
        Assert.Contains("createAthletePictureImage(`${displayName} headshot`);", flow);
        Assert.DoesNotContain("item.innerHTML =", flow);
    }

    [Fact]
    public async Task AthleteSelection_EnterSelectsExactTypedMatchInSharedController()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");
        var helperStart = flow.IndexOf("function findExactAthleteMatch(value)", StringComparison.Ordinal);
        var keydownStart = flow.IndexOf("input.addEventListener(\"keydown\"", helperStart, StringComparison.Ordinal);
        var keydownEnd = flow.IndexOf("document.addEventListener(\"click\"", keydownStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(keydownStart > helperStart);
        Assert.True(keydownEnd > keydownStart);

        var keydownBody = flow[keydownStart..keydownEnd];

        Assert.Contains("const query = (value || \"\").trim().toLowerCase();", flow);
        Assert.Contains("const canonicalName = getAthleteCanonicalName(athlete);", flow);
        Assert.Contains("if (!query || !canonicalName) return false;", flow);
        Assert.Contains("return canonicalName.toLowerCase() === query", flow);
        Assert.Contains("|| getAthleteDisplayName(athlete).toLowerCase() === query", flow);
        Assert.Contains("return athletes.find(athlete => isAthleteInputValue(athlete, value)) || null;", flow);
        Assert.Contains("const exactMatch = findExactAthleteMatch(input.value);", keydownBody);
        Assert.Contains("if (exactMatch)", keydownBody);
        Assert.Contains("selectAthlete(exactMatch);", keydownBody);
        Assert.Contains("closeAllLists();", keydownBody);
        Assert.Contains("if (currentFocus > -1 && list)", keydownBody);
        Assert.Contains("list[currentFocus].dispatchEvent(new MouseEvent(\"mousedown\"));", keydownBody);
        Assert.Contains("return;", keydownBody);
    }

    [Fact]
    public async Task AthleteSelection_InputChangeClearsStaleSelectedAthleteAndTransitionsBackToPlaceholder()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("function clearCurrentAthleteSelectionIfInputChanged(value)", flow);
        Assert.Contains("let hasUserEditedInput = false;", flow);
        Assert.Contains("hasUserEditedInput = true;", flow);
        Assert.Contains("if (saved && !currentAthlete && !hasUserEditedInput)", flow);
        Assert.Contains("if (!currentAthlete || isAthleteInputValue(currentAthlete, value)) return;", flow);
        Assert.Contains("currentAthlete = null;", flow);
        Assert.Contains("confirmButton.disabled = true;", flow);
        Assert.Contains("resetAthletePreview({ titleElement, frameElement, defaultTitle });", flow);
        Assert.Contains("function resetAthletePreview({ titleElement, frameElement, defaultTitle })", flow);
        Assert.Contains("titleElement.textContent = defaultTitle;", flow);
        Assert.Contains("webpSource.srcset = getDefaultHeadshotWebp();", flow);
        Assert.Contains("jpegSource.srcset = getDefaultHeadshotJpeg();", flow);
        Assert.Contains("image.alt = \"Headshot\";", flow);
        Assert.Contains("image.className = \"illustration athlete-picture-placeholder\";", flow);
        Assert.Contains("function createDefaultAthleteImage()", flow);
        Assert.Contains("image.className = \"illustration athlete-picture-placeholder athlete-picture-next\";", flow);
        Assert.Contains("transitionAthletePicture(frameElement, createDefaultAthleteImage(), getDefaultHeadshotJpeg());", flow);
    }

    [Fact]
    public async Task AthleteSelection_ProfilePictureSwapWaitsForImageLoad()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/select-athlete");
        var css = await client.GetStringAsync("/css/play-athlete-flow.css");
        var flow = await client.GetStringAsync("/js/play-athlete-flow.js");

        Assert.Contains("id=\"athleteSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("aspect-ratio: 1 / 1;", css);
        Assert.Contains("border: 4px solid var(--dark-text-color);", css);
        Assert.Contains("box-shadow: 0 4px 15px rgba(0, 0, 0, 0.4);", css);
        Assert.Contains("object-fit: contain;", css);
        Assert.DoesNotContain("object-fit: cover;", css);
        Assert.DoesNotContain("transform: scale(1.42);", css);
        Assert.Contains("border: 0;", css);
        Assert.Contains("class=\"illustration athlete-picture-placeholder\"", html);
        Assert.Contains("const ATHLETE_PICTURE_TRANSITION_MS = 180;", flow);
        Assert.Contains("const MIN_USABLE_ATHLETE_PICTURE_SIDE = 16;", flow);
        Assert.Contains("function transitionAthletePicture(frame, image, src)", flow);
        Assert.Contains("function shouldUseDefaultForLoadedAthleteImage(image)", flow);
        Assert.Contains("function setDefaultAthleteImageSource(image)", flow);
        Assert.Contains("function watchAthleteImageLoad(image, onLoaded)", flow);
        Assert.Contains("function waitForAthletePictureFrameReady(frame)", flow);
        Assert.Contains("const pictureReadyPromises = new WeakMap();", flow);
        Assert.Contains("function getAthletePictureImageSrc(athlete)", flow);
        Assert.Contains("athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb", flow);
        Assert.Contains("let hasFinished = false;", flow);
        Assert.Contains("image.loading = \"eager\";", flow);
        Assert.Contains("image.addEventListener(\"load\", handleImageLoad);", flow);
        Assert.Contains("image.addEventListener(\"error\", handleImageError);", flow);
        Assert.Contains("frame.appendChild(image);", flow);
        Assert.Contains("currentMedia.classList.add(\"is-exiting\");", flow);
        Assert.Contains("frame.replaceChildren(image);", flow);
        Assert.DoesNotContain("document.querySelector('picture').replaceChildren(image);", html);
    }
}
