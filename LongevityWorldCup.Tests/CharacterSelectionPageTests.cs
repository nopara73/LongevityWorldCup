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
        Assert.Contains("if (!setRequiredSessionItem('selectedAthlete', JSON.stringify(currentAthlete)))", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("customAlert('Browser storage is unavailable. Enable storage and try again.');", html);
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.Contains("window.location.href = '/dashboard';", html);
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
        Assert.Contains("setLocalItem('selectedAthleteName', currentAthlete.Name);", html);
        Assert.DoesNotContain("const prevName = localStorage.getItem('selectedAthleteName');", html);
    }

    [Fact]
    public async Task AthleteSelection_AthleteListFailureShowsRetryableInputError()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/character-selection.html");

        Assert.Contains("let athleteLoadPromise = null;", html);
        Assert.Contains("function retryAthleteLoad()", html);
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
        Assert.Contains("return `${athlete.Name || ''} ${getAthleteDisplayName(athlete)}`.toLowerCase();", html);
        Assert.Contains("if (terms.every(term => searchText.includes(term)))", html);
        Assert.Contains("const displayName = getAthleteDisplayName(a);", html);
        Assert.Contains("appendHighlightedText(item, displayName, first);", html);
        Assert.Contains("item.dataset.value = a.Name;", html);
        Assert.Contains("const displayName = (a.DisplayName && a.DisplayName.trim()) ? a.DisplayName.trim() : a.Name;", html);
        Assert.Contains("athleteInput.value = displayName;", html);
        Assert.Contains("image.alt = `${displayName} headshot`;", html);
        Assert.Contains("document.querySelector('picture').replaceChildren(image);", html);
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
        Assert.Contains("return (athlete.Name || '').trim().toLowerCase() === query", html);
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
        Assert.Contains("return (athlete.Name || '').trim().toLowerCase() === query", html);
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
        Assert.Contains("document.querySelector('picture').replaceChildren(webpSource, jpegSource, image);", html);
        Assert.Contains("clearCurrentAthleteSelectionIfInputChanged(this.value);", inputBody);
    }
}
