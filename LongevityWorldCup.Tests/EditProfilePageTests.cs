using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EditProfilePageTests
{
    [Fact]
    public async Task InvalidProfileFields_RemainEditableAfterValidationFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        AssertValidatorDoesNotRestore(html, "function validateFlagDisplay(value)", "function validatePersonalLink(value)", "restoreFlagToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validatePersonalLink(value)", "function validateMediaContact(value)", "restorePersonalLinkToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validateMediaContact(value)", "function validateWhyDisplay(value)", "restoreMediaContactToOriginal();");
        AssertValidatorDoesNotRestore(html, "function validateWhyDisplay(value)", "</script>", "restoreWhyDisplayToOriginal();");
    }

    private static void AssertValidatorDoesNotRestore(string html, string startMarker, string endMarker, string restoreCall)
    {
        var validationStart = html.IndexOf(startMarker, StringComparison.Ordinal);
        var validationEnd = html.IndexOf(endMarker, validationStart, StringComparison.Ordinal);

        Assert.True(validationStart >= 0);
        Assert.True(validationEnd > validationStart);

        var validationBody = html[validationStart..validationEnd];
        Assert.DoesNotContain(restoreCall, validationBody);
    }

    [Fact]
    public async Task EditProfileFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("window.readApplicationErrorMessage(response).then(txt =>", html);
        Assert.DoesNotContain("response.text().then(txt =>", html);
    }

    [Fact]
    public async Task DivisionSelect_UsesFallbackWhenApiFailsOrReturnsEmpty()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("const fallbackDivisions = [\"Men's\", \"Women's\", \"Open\"];", html);
        Assert.Contains("function populateDivisionSelect(divs)", html);
        Assert.Contains("const divisionOptions = [...(Array.isArray(divs) && divs.length ? divs : fallbackDivisions)];", html);
        Assert.Contains("divisionOptions.unshift(athlete.Division);", html);
        Assert.Contains(".then(r => r.ok ? r.json() : fallbackDivisions)", html);
        Assert.Contains(".then(populateDivisionSelect)", html);
        Assert.Contains("populateDivisionSelect(fallbackDivisions);", html);
    }

    [Fact]
    public async Task FlagInput_KeepsCurrentFlagWhenOptionsRequestFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("const currentFlag = window.LwcFlags.getCanonicalFlagName(athlete.Flag);", html);
        Assert.Contains("let availableFlags = currentFlag ? [currentFlag] : [];", html);
        Assert.Contains("flagInput.value = athlete.Flag;", html);
        Assert.Contains("fetch('/api/data/athletes').then(r => r.ok ? r.json() : [])", html);
        Assert.Contains("fetch('/api/data/flags').then(r => r.ok ? r.json() : [])", html);
        Assert.Contains("if (currentFlag && !availableFlags.includes(currentFlag)) availableFlags.push(currentFlag);", html);
        Assert.Contains("console.error('Error fetching flags:', error);", html);
    }

    [Fact]
    public async Task ProfilePictureSelection_ClearsInputAfterCapturingFile()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var selectionStart = html.IndexOf("function handleProfilePictureSelection(e)", StringComparison.Ordinal);
        var selectionEnd = html.IndexOf("changeProfileInput.addEventListener('change', handleProfilePictureSelection);", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(selectionEnd > selectionStart);

        var selectionBody = html[selectionStart..selectionEnd];
        Assert.Contains("const input = e.target;", selectionBody);
        Assert.Contains("const file = input.files[0];", selectionBody);
        Assert.Contains("input.value = '';", selectionBody);
        Assert.Contains("reader.onerror = () =>", selectionBody);
        Assert.Contains("customAlert('Profile picture upload failed.');", selectionBody);
    }

    [Fact]
    public async Task ProfilePictureSelection_RejectsUnsupportedFormatsBeforeReading()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var selectionStart = html.IndexOf("function handleProfilePictureSelection(e)", StringComparison.Ordinal);
        var readerStart = html.IndexOf("const reader = new FileReader();", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(readerStart > selectionStart);

        var beforeReader = html[selectionStart..readerStart];

        Assert.Contains("id=\"profilePicInputModal\" accept=\"image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp\"", html);
        Assert.Contains("id=\"profileCameraInputModal\" accept=\"image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp\"", html);
        Assert.Contains("function isSupportedProfilePictureFile(file)", html);
        Assert.Contains("type === 'image/jpeg'", html);
        Assert.Contains("type === 'image/png'", html);
        Assert.Contains("type === 'image/webp'", html);
        Assert.Contains("extension === 'jpg'", html);
        Assert.Contains("extension === 'jpeg'", html);
        Assert.Contains("extension === 'png'", html);
        Assert.Contains("extension === 'webp'", html);
        Assert.Contains("input.value = '';", beforeReader);
        Assert.Contains("if (!isSupportedProfilePictureFile(file))", beforeReader);
        Assert.Contains("customAlert('Profile picture must be JPG, PNG, or WebP.');", beforeReader);
        Assert.Contains("return;", beforeReader);
    }

    [Fact]
    public async Task ProfilePictureSelection_ReplacesExistingCropperBeforeNewImage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var loadStart = html.IndexOf("reader.onload = (evt) =>", StringComparison.Ordinal);
        var loadEnd = html.IndexOf("reader.onerror = () =>", loadStart, StringComparison.Ordinal);
        var cropStart = html.IndexOf("cropBtn.addEventListener('click', async function ()", StringComparison.Ordinal);
        var cancelStart = html.IndexOf("cancelBtn.addEventListener('click', () =>", cropStart, StringComparison.Ordinal);

        Assert.True(loadStart >= 0);
        Assert.True(loadEnd > loadStart);
        Assert.True(cropStart >= 0);
        Assert.True(cancelStart > cropStart);

        var loadBody = html[loadStart..loadEnd];
        var cropBody = html[cropStart..cancelStart];
        var cancelBody = html[cancelStart..html.IndexOf("function updateSubmitButtonState()", cancelStart, StringComparison.Ordinal)];

        Assert.Contains("if (window.changeProfileCropper)", loadBody);
        Assert.Contains("window.changeProfileCropper.destroy();", loadBody);
        Assert.Contains("window.changeProfileCropper = null;", loadBody);
        Assert.Contains("cropperImage.src = evt.target.result;", loadBody);
        Assert.Contains("window.changeProfileCropper = new Cropper(cropperImage,", loadBody);
        Assert.Contains("window.changeProfileCropper = null;", cropBody);
        Assert.Contains("window.changeProfileCropper = null;", cancelBody);
    }

    [Fact]
    public async Task ProfilePictureCrop_FallsBackToRawCropWhenOptimizationFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var cropStart = html.IndexOf("cropBtn.addEventListener('click', async function ()", StringComparison.Ordinal);
        var cropEnd = html.IndexOf("cancelBtn.addEventListener('click', () =>", cropStart, StringComparison.Ordinal);

        Assert.True(cropStart >= 0);
        Assert.True(cropEnd > cropStart);

        var cropBody = html[cropStart..cropEnd];
        Assert.Contains("let newSrc = raw;", cropBody);
        Assert.Contains("try {", cropBody);
        Assert.Contains("await window.optimizeImageClient(raw, window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS);", cropBody);
        Assert.Contains("} catch {", cropBody);
        Assert.Contains("newSrc = raw;", cropBody);
        Assert.Contains("athlete.ProfilePic = newSrc;", cropBody);
    }

    [Fact]
    public async Task EditProfileSuccessHandoff_UsesSafeStorageBeforeNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var successStart = html.IndexOf("customAlert('Change request submitted!').then(() =>", StringComparison.Ordinal);
        var successEnd = html.IndexOf("});", html.IndexOf("window.location.href = '/review?from=edit-profile';", successStart, StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.True(successStart >= 0);
        Assert.True(successEnd > successStart);

        var successBody = html[successStart..successEnd];

        Assert.Contains("function setBrowserStorageItem(storageName, key, value)", html);
        Assert.Contains("function setSessionItem(key, value)", html);
        Assert.Contains("function setLocalItem(key, value)", html);
        Assert.Contains("const reviewContactEmail = normalizeContactEmail(applicantData.accountEmail);", successBody);
        Assert.Contains("setSessionItem('contactEmail', reviewContactEmail);", successBody);
        Assert.Contains("setLocalItem('contactEmail', reviewContactEmail);", successBody);
        Assert.Contains("setSessionItem(\"came-from\", \"edit-profile\");", successBody);
        Assert.Contains("window.location.href = '/review?from=edit-profile';", successBody);
        Assert.DoesNotContain("sessionStorage.setItem(", successBody);
        Assert.DoesNotContain("localStorage.setItem(", successBody);
    }

    [Fact]
    public async Task EditProfileSubmit_DoesNotBlockOnStoredContactEmailRead()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var submitStart = html.IndexOf("const applicantData = {", StringComparison.Ordinal);
        var submitEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", submitStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(submitEnd > submitStart);

        var submitBody = html[submitStart..submitEnd];

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", html);
        Assert.Contains("function readEditProfileContactEmail()", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("|| normalizeContactEmail(originalAthlete && originalAthlete.MediaContact)", html);
        Assert.Contains("function readStoredContactEmail()", html);
        Assert.Contains("const contactEmail = normalizeContactEmail(getSessionItem('contactEmail') || getLocalItem('contactEmail'));", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("|| readStoredContactEmail();", html);
        Assert.Contains("accountEmail: readEditProfileContactEmail(),", submitBody);
        Assert.DoesNotContain("|| getSessionItem('contactEmail')", submitBody);
        Assert.DoesNotContain("|| getLocalItem('contactEmail')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("localStorage.getItem('contactEmail')", submitBody);
    }

    [Fact]
    public async Task EditProfileSubmit_GuardsAgainstDuplicateClicks()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var handlerStart = html.IndexOf("submitButton.addEventListener('click', async function ()", StringComparison.Ordinal);
        var handlerEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);

        var handlerBeforeFetch = html[handlerStart..handlerEnd];

        Assert.Contains("let isEditProfileSubmitting = false;", html);
        Assert.Contains("if (isEditProfileSubmitting || submitButton.disabled) return;", handlerBeforeFetch);
        Assert.Contains("isEditProfileSubmitting = true;", handlerBeforeFetch);
        Assert.Contains("submitButton.disabled = true;", handlerBeforeFetch);
        Assert.Contains("customAlert(`Submission failed:\\n\\n${displayError}`).then(() => {\n                            isEditProfileSubmitting = false;", html);
    }

    [Fact]
    public async Task EditProfileSubmit_EnterKeyUsesExistingSubmitPathForSingleLineFields()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var listenerStart = html.IndexOf("[personalLinkInput, mediaContactInput].forEach(input =>", StringComparison.Ordinal);
        var listenerEnd = html.IndexOf("const restoreWhyDisplayBtn = document.getElementById('restoreWhyDisplayBtn');", listenerStart, StringComparison.Ordinal);

        Assert.True(listenerStart >= 0);
        Assert.True(listenerEnd > listenerStart);

        var listenerBody = html[listenerStart..listenerEnd];

        Assert.Contains("input.addEventListener('keydown', e =>", listenerBody);
        Assert.Contains("if (e.key === 'Enter')", listenerBody);
        Assert.Contains("e.preventDefault();", listenerBody);
        Assert.Contains("submitButton.click();", listenerBody);
        Assert.DoesNotContain("whyDisplayInput", listenerBody);
        Assert.Contains("submitButton.addEventListener('click', async function ()", html);
    }

    [Fact]
    public async Task EditProfileNoAthleteGuard_ReturnsToAthleteSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var guardStart = html.IndexOf("if (!isValidSelectedAthlete(athlete))", StringComparison.Ordinal);
        var guardEnd = html.IndexOf("const submissionId = window.createApplicationSubmissionId();", guardStart, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(guardEnd > guardStart);

        var guardBody = html[guardStart..guardEnd];

        Assert.Contains("function isValidSelectedAthlete(value)", html);
        Assert.Contains("customAlert('No athlete selected. Please return and choose your athlete.')", guardBody);
        Assert.Contains(".then(() => window.location.href = '/select-athlete');", guardBody);
    }

    [Fact]
    public async Task EditProfileSelectionHandoff_RejectsIncompleteStoredAthlete()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("function isValidSelectedAthlete(value)", html);
        Assert.Contains("typeof value.Name === 'string'", html);
        Assert.Contains("value.Name.trim()", html);
        Assert.Contains("const selectedAthleteJson = getSessionItem('selectedAthlete');", html);
        Assert.Contains("if (!isValidSelectedAthlete(originalAthlete))", html);
        Assert.Contains("removeSessionItem('selectedAthlete');", html);
        Assert.Contains("removeSessionItem('tempAthlete');", html);
        Assert.Contains("window.location.replace('/select-athlete');", html);
        Assert.Contains("const tempAthleteJson = getSessionItem('tempAthlete');", html);
        Assert.Contains("const athlete = isValidSelectedAthlete(tempAthlete)", html);
        Assert.DoesNotContain("const selectedAthleteJson = sessionStorage.getItem('selectedAthlete');", html);
        Assert.DoesNotContain("const tempAthleteJson = sessionStorage.getItem('tempAthlete');", html);
    }

    [Fact]
    public async Task EditProfileValidation_FallsBackWhenValidatorScriptIsUnavailable()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var validatorStart = html.IndexOf("function isOptionalUrl(value)", StringComparison.Ordinal);
        var validatorEnd = html.IndexOf("function validatePersonalLink(value)", validatorStart, StringComparison.Ordinal);
        var personalLinkEnd = html.IndexOf("function validateMediaContact(value)", validatorEnd, StringComparison.Ordinal);

        Assert.True(validatorStart >= 0);
        Assert.True(validatorEnd > validatorStart);
        Assert.True(personalLinkEnd > validatorEnd);

        var helperBody = html[validatorStart..validatorEnd];
        var validateBody = html[validatorEnd..personalLinkEnd];

        Assert.Contains("window.validator && typeof window.validator.isURL === 'function'", helperBody);
        Assert.Contains("const v = normalizeOptionalUrl(value);", helperBody);
        Assert.Contains("function normalizeOptionalUrl(value)", html);
        Assert.Contains("return /^www\\./i.test(v) ? `https://${v}` : v;", html);
        Assert.Contains("input.type = 'url';", helperBody);
        Assert.Contains("return input.checkValidity();", helperBody);
        Assert.Contains("if (!isOptionalUrl(v))", validateBody);
        Assert.DoesNotContain("!validator.isURL(v)", validateBody);
    }

    [Fact]
    public async Task EditProfileDraftPersistence_UsesSafeStorageHelpers()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var stateStart = html.IndexOf("function updateSubmitButtonState()", StringComparison.Ordinal);
        var stateEnd = html.IndexOf("function validateFlagDisplay(value)", stateStart, StringComparison.Ordinal);

        Assert.True(stateStart >= 0);
        Assert.True(stateEnd > stateStart);

        var stateBody = html[stateStart..stateEnd];

        Assert.Contains("function removeBrowserStorageItem(storageName, key)", html);
        Assert.Contains("function removeSessionItem(key)", html);
        Assert.Contains("setSessionItem('tempAthlete', JSON.stringify(athlete));", stateBody);
        Assert.Contains("removeSessionItem('tempAthlete');", stateBody);
        Assert.DoesNotContain("sessionStorage.setItem('tempAthlete'", stateBody);
        Assert.DoesNotContain("sessionStorage.removeItem('tempAthlete'", stateBody);
    }
}
