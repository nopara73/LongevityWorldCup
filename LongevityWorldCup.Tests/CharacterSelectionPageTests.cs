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
        Assert.Contains("if (!isAthleteInputValue(currentAthlete, prevName))", html);
        Assert.Contains("if (!setRequiredSessionItem('selectedAthlete', JSON.stringify(currentAthlete)))", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.Contains("window.location.href = '/dashboard';", html);
        Assert.DoesNotContain("prevName !== currentAthlete.Name", html);
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
        Assert.Contains("const match = athletes.find(a => isAthleteInputValue(a, saved));", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.DoesNotContain("const match = athletes.find(a => a.Name === saved);", html);
        Assert.DoesNotContain("const prevName = localStorage.getItem('selectedAthleteName');", html);
    }

    [Fact]
    public async Task AthleteSelection_HydratesStoredAthleteBeforeAthleteListLoads()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");
        var hydrate = html.IndexOf("hydrateStoredAthleteSelection();", StringComparison.Ordinal);
        var load = html.IndexOf("loadAthletes().catch(() => {});", hydrate, StringComparison.Ordinal);

        Assert.True(hydrate >= 0);
        Assert.True(load > hydrate);
        Assert.Contains("function getStoredSelectedAthlete()", html);
        Assert.Contains("const storedAthlete = currentAthlete || getStoredSelectedAthlete();", html);
        Assert.Contains("renderSelectedAthletePreview(storedAthlete, { transition: false });", html);
        Assert.Contains("replaceAthleteSelectionImageImmediately(image, getAthletePictureImageSrc(athlete));", html);
        Assert.Contains("if (currentAthlete && isAthleteInputValue(currentAthlete, this.value)) return false;", html);
        Assert.Contains("if (!currentAthlete) {\n                            athleteInput.dispatchEvent(new Event('input'));\n                        }", html);
        Assert.Contains("if (saved && !currentAthlete)", html);
    }

    [Fact]
    public async Task AthleteSelection_AthleteListFailureShowsRetryableInputError()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("let athleteLoadPromise = null;", html);
        Assert.Contains("function retryAthleteLoad()", html);
        Assert.Contains("id=\"athlete\"\n                       name=\"athlete\"\n                       required\n                       aria-required=\"true\"\n                       aria-describedby=\"athleteError\"", html);
        Assert.Contains("aria-describedby=\"athleteError\"\n                       autocomplete=\"off\"\n                       autocapitalize=\"none\"\n                       spellcheck=\"false\"", html);
        Assert.Contains("<span id=\"athleteError\" class=\"error-message\" aria-live=\"polite\"></span>", html);
        Assert.Contains("athleteInput.addEventListener('focus', retryAthleteLoad);", html);
        Assert.Contains("athleteInput.addEventListener('input', retryAthleteLoad);", html);
        Assert.Contains("console.error('Error fetching athletes:', error);", html);
        Assert.Contains("athleteError.textContent = 'Athlete list could not load. Check your connection and try again.';", html);
        Assert.Contains("loadAthletes().catch(() => {});", html);
        Assert.DoesNotContain(".catch(console.error);", html);
    }

    [Fact]
    public async Task AthleteSelection_TempDraftCleanupDoesNotBlockSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");
        var clickStart = html.IndexOf("confirmBtn.addEventListener('click'", StringComparison.Ordinal);
        var clickEnd = html.IndexOf("window.location.href = '/dashboard';", clickStart, StringComparison.Ordinal);

        Assert.True(clickStart >= 0);
        Assert.True(clickEnd > clickStart);

        var clickBody = html[clickStart..clickEnd];

        Assert.Contains("function setRequiredSessionItem(key, value)", html);
        Assert.Contains("sessionStorage.setItem(key, value);", html);
        Assert.Contains("return false;", html);
        Assert.Contains("function getSessionItem(key)", html);
        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("function clearStaleTempAthlete(selectedAthleteName)", html);
        Assert.Contains("clearStaleTempAthlete(currentAthlete.Name);", clickBody);
        Assert.DoesNotContain("sessionStorage.getItem('tempAthlete')", clickBody);
        Assert.DoesNotContain("sessionStorage.removeItem('tempAthlete')", clickBody);
    }

    [Fact]
    public async Task AthleteSelection_SearchesAndShowsDisplayNamesWithoutChangingCanonicalSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("function getAthleteDisplayName(athlete)", html);
        Assert.Contains("function getAthleteCanonicalName(athlete)", html);
        Assert.Contains("return `${getAthleteCanonicalName(athlete)} ${getAthleteDisplayName(athlete)}`.toLowerCase();", html);
        Assert.Contains("typeof athlete.DisplayName === 'string'", html);
        Assert.Contains("return athlete && typeof athlete.Name === 'string' ? athlete.Name : '';", html);
        Assert.Contains("return athlete && typeof athlete.Name === 'string' ? athlete.Name.trim() : '';", html);
        Assert.Contains("if (terms.every(term => searchText.includes(term)))", html);
        Assert.Contains("if (!getAthleteCanonicalName(a)) return;", html);
        Assert.Contains("const displayName = getAthleteDisplayName(a);", html);
        Assert.Contains("appendHighlightedText(item, displayName, first);", html);
        Assert.Contains("item.dataset.value = a.Name;", html);
        Assert.Contains("const displayName = getAthleteDisplayName(a);", html);
        Assert.Contains("athleteInput.value = displayName;", html);
        Assert.Contains("createAthletePictureImage(`${displayName} headshot`);", html);
        Assert.Contains("image.alt = altText;", html);
        Assert.Contains("transitionAthleteSelectionImage(image, getAthletePictureImageSrc(athlete));", html);
        Assert.DoesNotContain("document.querySelector('picture').replaceChildren(image);", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.DoesNotContain("const name = a.Name.toLowerCase();", html);
        Assert.DoesNotContain("item.innerHTML =", html);
    }

    [Fact]
    public async Task AthleteSelection_EnterSelectsExactTypedMatch()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");
        var helperStart = html.IndexOf("function findExactAthleteMatch(value)", StringComparison.Ordinal);
        var keydownStart = html.IndexOf("athleteInput.addEventListener('keydown'", helperStart, StringComparison.Ordinal);
        var keydownEnd = html.IndexOf("function addActive(items)", keydownStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(keydownStart > helperStart);
        Assert.True(keydownEnd > keydownStart);

        var keydownBody = html[keydownStart..keydownEnd];

        Assert.Contains("const query = (value || '').trim().toLowerCase();", html);
        Assert.Contains("const canonicalName = getAthleteCanonicalName(athlete);", html);
        Assert.Contains("if (!query || !canonicalName) return false;", html);
        Assert.Contains("return canonicalName.toLowerCase() === query", html);
        Assert.Contains("|| getAthleteDisplayName(athlete).toLowerCase() === query", html);
        Assert.Contains("return athletes.find(a => isAthleteInputValue(a, value)) || null;", html);
        Assert.Contains("const exactMatch = findExactAthleteMatch(this.value);", keydownBody);
        Assert.Contains("if (exactMatch)", keydownBody);
        Assert.Contains("selectAthlete(exactMatch);", keydownBody);
        Assert.Contains("closeAllLists();", keydownBody);
        Assert.Contains("if (currentFocus > -1 && list)", keydownBody);
        Assert.Contains("list[currentFocus].dispatchEvent(new MouseEvent('mousedown'));", keydownBody);
        Assert.Contains("return;", keydownBody);
    }

    [Fact]
    public async Task AthleteSelection_InputChangeClearsStaleSelectedAthlete()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");
        var inputStart = html.IndexOf("athleteInput.addEventListener('input'", StringComparison.Ordinal);
        var inputEnd = html.IndexOf("athleteInput.addEventListener('keydown'", inputStart, StringComparison.Ordinal);

        Assert.True(inputStart >= 0);
        Assert.True(inputEnd > inputStart);

        var inputBody = html[inputStart..inputEnd];

        Assert.Contains("function isAthleteInputValue(athlete, value)", html);
        Assert.Contains("const query = (value || '').trim().toLowerCase();", html);
        Assert.Contains("const canonicalName = getAthleteCanonicalName(athlete);", html);
        Assert.Contains("if (!query || !canonicalName) return false;", html);
        Assert.Contains("return canonicalName.toLowerCase() === query", html);
        Assert.Contains("|| getAthleteDisplayName(athlete).toLowerCase() === query;", html);
        Assert.Contains("function clearCurrentAthleteSelectionIfInputChanged(value)", html);
        Assert.Contains("if (!currentAthlete || isAthleteInputValue(currentAthlete, value)) return;", html);
        Assert.Contains("currentAthlete = null;", html);
        Assert.Contains("confirmBtn.disabled = true;", html);
        Assert.Contains("resetAthletePreview();", html);
        Assert.Contains("function resetAthletePreview()", html);
        Assert.Contains("document.getElementById('character-title').textContent = 'Athlete selection';", html);
        Assert.Contains("webpSource.srcset = '../assets/content-images/headshot.webp';", html);
        Assert.Contains("jpegSource.srcset = '../assets/content-images/headshot.jpg';", html);
        Assert.Contains("image.alt = 'Headshot';", html);
        Assert.Contains("image.className = 'illustration athlete-picture-placeholder';", html);
        Assert.Contains("function createDefaultAthleteImage()", html);
        Assert.Contains("image.className = 'illustration athlete-picture-placeholder athlete-picture-next';", html);
        Assert.Contains("transitionAthleteSelectionImage(createDefaultAthleteImage(), '../assets/content-images/headshot.jpg');", html);
        Assert.DoesNotContain("document.getElementById('characterSelectionPicture').replaceChildren(createDefaultAthletePicture());", html);
        Assert.Contains("clearCurrentAthleteSelectionIfInputChanged(this.value);", inputBody);
    }

    [Fact]
    public async Task AthleteSelection_ProfilePictureSwapWaitsForImageLoad()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("id=\"characterSelectionPicture\" class=\"athlete-picture-frame\"", html);
        Assert.Contains("aspect-ratio: 1 / 1;", html);
        Assert.Contains("border: 4px solid var(--dark-text-color);", html);
        Assert.Contains("box-shadow: 0 4px 15px rgba(0, 0, 0, 0.4);", html);
        Assert.Contains("object-fit: contain;", html);
        Assert.Contains(".athlete-picture-frame .athlete-picture-placeholder", html);
        Assert.Contains("object-fit: cover;", html);
        Assert.Contains("transform: scale(1.035);", html);
        Assert.Contains("border: 0;", html);
        Assert.Contains("class=\"illustration athlete-picture-placeholder\"", html);
        Assert.Contains("const ATHLETE_PICTURE_TRANSITION_MS = 180;", html);
        Assert.Contains("let athletePictureTransitionToken = 0;", html);
        Assert.Contains("function transitionAthleteSelectionImage(image, src)", html);
        Assert.Contains("function getAthletePictureImageSrc(athlete)", html);
        Assert.Contains("athlete.ProfilePic || athlete.ProfilePicLeaderboardThumb || athlete.ProfilePicThumb", html);
        Assert.Contains("let hasFinished = false;", html);
        Assert.Contains("image.loading = 'eager';", html);
        Assert.Contains("image.addEventListener('load', finishImageSwap, { once: true });", html);
        Assert.Contains("frame.appendChild(image);", html);
        Assert.Contains("currentMedia.classList.add('is-exiting');", html);
        Assert.Contains("frame.replaceChildren(image);", html);
        Assert.Contains("transitionAthleteSelectionImage(image, getAthletePictureImageSrc(athlete));", html);
        Assert.DoesNotContain("document.querySelector('picture').replaceChildren(image);", html);
    }
}
