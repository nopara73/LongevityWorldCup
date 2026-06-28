using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EditProfilePageTests
{
    [Fact]
    public async Task EditProfile_BackButtonReturnsToDashboardWithoutHistoryFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("type=\"button\" class=\"option-button back-button flow-action flow-action--secondary flow-action--icon-left\" onclick=\"window.navigateToFlowDestination('/dashboard')\"", html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }

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

        Assert.Contains("const fallbackError = Number.isFinite(response.status) ? `HTTP ${response.status}` : 'Request failed';", html);
        Assert.Contains("window.readApplicationErrorMessage(response).catch(() => fallbackError).then(txt =>", html);
        Assert.Contains("customAlert(`Failed to submit change request. Please try again later.\\n\\n${txt}`).then(() => {\n                                    isEditProfileSubmitting = false;", html);
        Assert.DoesNotContain("response.text().then(txt =>", html);
        Assert.DoesNotContain("return Promise.reject(txt);", html);
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
        Assert.Contains("fetch('/api/data/athletes').then(r => r.ok ? r.json() : []).catch(() => [])", html);
        Assert.Contains("fetch('/api/data/flags').then(r => r.ok ? r.json() : []).catch(() => [])", html);
        Assert.Contains("if (currentFlag && !availableFlags.includes(currentFlag)) availableFlags.push(currentFlag);", html);
        Assert.Contains("console.error('Error fetching flags:', error);", html);
    }

    [Fact]
    public async Task EditProfile_RendersSelectedAthleteImageWithoutInnerHtml()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("typeof athlete?.DisplayName === 'string'", html);
        Assert.Contains("return typeof athlete?.Name === 'string' ? athlete.Name : '';", html);
        Assert.Contains("const athleteImage = document.createElement('img');", html);
        Assert.Contains("athleteImage.src = athlete.ProfilePic;", html);
        Assert.Contains("athleteImage.alt = `${athleteDisplayName} headshot`;", html);
        Assert.Contains("athleteImage.className = 'illustration';", html);
        Assert.Contains("athleteImage.loading = 'lazy';", html);
        Assert.Contains("document.querySelector('picture').replaceChildren(athleteImage);", html);
        Assert.DoesNotContain("document.querySelector('picture').innerHTML", html);
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
        Assert.Contains("const file = input.files && input.files[0];", selectionBody);
        Assert.DoesNotContain("const file = input.files[0];", selectionBody);
        Assert.Contains("input.value = '';", selectionBody);
        Assert.Contains("reader.onerror = () =>", selectionBody);
        Assert.Contains("reader.onabort = reader.onerror;", selectionBody);
        Assert.Contains("customAlert('Profile picture upload failed. Please try another image.')\n                    .then(() => changeProfileBtn.focus());", selectionBody);
    }

    [Fact]
    public async Task ProfilePictureCropper_UsesCspAllowedCdn()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("https://cdnjs.cloudflare.com/ajax/libs/cropperjs/1.5.13/cropper.min.css", html);
        Assert.Contains("https://cdnjs.cloudflare.com/ajax/libs/cropperjs/1.5.13/cropper.min.js", html);
        Assert.DoesNotContain("https://unpkg.com/cropperjs", html);
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

        Assert.Contains("id=\"profilePicInputModal\" accept=\"image/*,.heic,.heif\"", html);
        Assert.Contains("id=\"profileCameraInputModal\" accept=\"image/*,.heic,.heif\"", html);
        Assert.Contains("function isSupportedProfilePictureFile(file)", html);
        Assert.Contains("type.startsWith('image/')", html);
        Assert.Contains("extension === 'jpg'", html);
        Assert.Contains("extension === 'jpeg'", html);
        Assert.Contains("extension === 'png'", html);
        Assert.Contains("extension === 'webp'", html);
        Assert.Contains("extension === 'heic'", html);
        Assert.Contains("extension === 'heif'", html);
        Assert.Contains("input.value = '';", beforeReader);
        Assert.Contains("if (!isSupportedProfilePictureFile(file))", beforeReader);
        Assert.Contains("customAlert('Profile picture must be an image file.')\n                    .then(() => changeProfileBtn.focus());", beforeReader);
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
        Assert.Contains("const modalOverlay = document.getElementById('modalOverlay');", loadBody);
        Assert.Contains("modalOverlay.style.display = 'block';", loadBody);
        Assert.Contains("} catch (_) {", loadBody);
        Assert.Contains("try { window.changeProfileCropper.destroy(); } catch (_) { }", loadBody);
        Assert.Contains("cropperModal.style.display = 'none';", loadBody);
        Assert.Contains("modalOverlay.style.display = 'none';", loadBody);
        Assert.Contains("document.body.classList.remove('no-scroll');", loadBody);
        Assert.Contains("customAlert('Profile picture upload failed. Please try another image.')\n                        .then(() => changeProfileBtn.focus());", loadBody);
        Assert.Contains("let isChangeProfileCropProcessing = false;", html);
        Assert.Contains("const activeCropper = window.changeProfileCropper;", cropBody);
        Assert.Contains("if (isChangeProfileCropProcessing || !activeCropper) return;", cropBody);
        Assert.Contains("isChangeProfileCropProcessing = true;", cropBody);
        Assert.Contains("cropBtn.disabled = true;", cropBody);
        Assert.Contains("cancelBtn.disabled = true;", cropBody);
        Assert.Contains("activeCropper.getCroppedCanvas({", cropBody);
        Assert.Contains("activeCropper.destroy();", cropBody);
        Assert.Contains("isChangeProfileCropProcessing = false;", cropBody);
        Assert.Contains("cropBtn.disabled = false;", cropBody);
        Assert.Contains("cancelBtn.disabled = false;", cropBody);
        Assert.Contains("if (isChangeProfileCropProcessing) return;", cancelBody);
        Assert.Contains("if (activeCropper)", cancelBody);
        Assert.Contains("try { activeCropper.destroy(); } catch (_) { }", cancelBody);
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
        Assert.Contains("if (!canvas)", cropBody);
        Assert.Contains("customAlert('Profile picture crop failed. Please try another image.')\n                        .then(() => cropBtn.focus());", cropBody);
        Assert.Contains("return;", cropBody);
        Assert.Contains("let newSrc = raw;", cropBody);
        Assert.Contains("try {", cropBody);
        Assert.Contains("await window.optimizeImageClient(raw, window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS);", cropBody);
        Assert.Contains("} catch {", cropBody);
        Assert.Contains("newSrc = raw;", cropBody);
        Assert.Contains("athlete.ProfilePic = newSrc;", cropBody);
        Assert.Contains("try { activeCropper.destroy(); } catch (_) { }", cropBody);
        Assert.Contains("} catch (_) {", cropBody);
        Assert.Contains("customAlert('Profile picture crop failed. Please try another image.')\n                    .then(() => cropBtn.focus());", cropBody);
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
        Assert.Contains("rememberAthleteContactEmail(athlete && athlete.Name, reviewContactEmail);", successBody);
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
        Assert.Contains("if (typeof value !== 'string') return null;", html);
        Assert.Contains("const bracketedEmail = /<([^<>]+)>/.exec(contactEmail);", html);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", html);
        Assert.Contains("function readEditProfileContactEmail()", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("|| normalizeContactEmail(originalAthlete && originalAthlete.MediaContact)", html);
        Assert.Contains("function readStoredContactEmail()", html);
        Assert.Contains("const sessionContactEmail = normalizeContactEmail(getSessionItem('contactEmail'));", html);
        Assert.Contains("if (sessionContactEmail) return sessionContactEmail;", html);
        Assert.Contains("const localContactEmail = normalizeContactEmail(getLocalItem('contactEmail'));", html);
        Assert.Contains("if (localContactEmail)", html);
        Assert.Contains("return localContactEmail;", html);
        Assert.Contains("function readStoredAthleteContactEmail(athleteName)", html);
        Assert.Contains("return key ? normalizeContactEmail(getLocalItem(key)) : null;", html);
        Assert.Contains("function rememberAthleteContactEmail(athleteName, email)", html);
        Assert.Contains("return name ? 'contactEmailFor:' + name : null;", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("|| readStoredAthleteContactEmail(originalAthlete && originalAthlete.Name)", html);
        Assert.Contains("|| readStoredAthleteContactEmail(athlete && athlete.Name)", html);
        Assert.Contains("|| readStoredContactEmail();", html);
        Assert.Contains("accountEmail: readEditProfileContactEmail(),", submitBody);
        Assert.DoesNotContain("|| getSessionItem('contactEmail')", submitBody);
        Assert.DoesNotContain("|| getLocalItem('contactEmail')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("localStorage.getItem('contactEmail')", submitBody);
    }

    [Fact]
    public async Task EditProfileSubmit_NormalizesPersonalLinkAndEmailShapedMediaContactBeforePosting()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var submitStart = html.IndexOf("submitButton.addEventListener('click', async function ()", StringComparison.Ordinal);
        var applicantDataStart = html.IndexOf("const applicantData = {", submitStart, StringComparison.Ordinal);
        var fetchStart = html.IndexOf("fetchWithTimeout('/api/application/application'", applicantDataStart, StringComparison.Ordinal);
        var blurStart = html.IndexOf("// --- BLUR HANDLERS ---", StringComparison.Ordinal);
        var blurEnd = html.IndexOf("let skipWhyValidation = false;", blurStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(applicantDataStart > submitStart);
        Assert.True(fetchStart > applicantDataStart);
        Assert.True(blurStart >= 0);
        Assert.True(blurEnd > blurStart);

        var beforeApplicantData = html[submitStart..applicantDataStart];
        var submitBody = html[applicantDataStart..fetchStart];
        var blurBody = html[blurStart..blurEnd];

        Assert.Contains("athlete.PersonalLink = normalizeOptionalUrl(personalLinkInput.value) || '';", beforeApplicantData);
        Assert.Contains("personalLinkInput.value = athlete.PersonalLink;", beforeApplicantData);
        Assert.Contains("const normalized = normalizeOptionalUrl(this.value);", blurBody);
        Assert.Contains("if (isOptionalUrl(normalized))", blurBody);
        Assert.Contains("this.value = normalized;", blurBody);
        Assert.Contains("this.value = normalizeMediaContact(this.value);", blurBody);
        Assert.Contains("updateSubmitButtonState();", blurBody);
        Assert.Contains("function normalizeMediaContact(value)", html);
        Assert.Contains("return normalizeContactEmail(value) || normalizeEditText(value);", html);
        Assert.Contains("athlete.MediaContact = normalizeMediaContact(mediaContactInput.value);", beforeApplicantData);
        Assert.Contains("mediaContactInput.value = athlete.MediaContact;", beforeApplicantData);
        Assert.Contains("personalLink: athlete.PersonalLink || null,", submitBody);
        Assert.Contains("mediaContact: athlete.MediaContact,", submitBody);
    }

    [Fact]
    public async Task EditProfilePersonalLinkChangeState_UsesNormalizedComparison()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var personalLinkSetupStart = html.IndexOf("const restorePersonalLinkBtn = document.getElementById('restorePersonalLinkBtn');", StringComparison.Ordinal);
        var mediaContactSetupStart = html.IndexOf("const restoreMediaContactBtn = document.getElementById('restoreMediaContactBtn');", personalLinkSetupStart, StringComparison.Ordinal);
        var listenerStart = html.IndexOf("personalLinkInput.addEventListener('input', () =>", StringComparison.Ordinal);
        var listenerEnd = html.IndexOf("mediaContactInput.addEventListener('input', () =>", listenerStart, StringComparison.Ordinal);
        var stateStart = html.IndexOf("function updateSubmitButtonState()", StringComparison.Ordinal);
        var stateEnd = html.IndexOf("function validateFlagDisplay(value)", stateStart, StringComparison.Ordinal);

        Assert.True(personalLinkSetupStart >= 0);
        Assert.True(mediaContactSetupStart > personalLinkSetupStart);
        Assert.True(listenerStart >= 0);
        Assert.True(listenerEnd > listenerStart);
        Assert.True(stateStart >= 0);
        Assert.True(stateEnd > stateStart);

        var setupBody = html[personalLinkSetupStart..mediaContactSetupStart];
        var listenerBody = html[listenerStart..listenerEnd];
        var stateBody = html[stateStart..stateEnd];

        Assert.Contains("if (normalizeOptionalUrl(athlete.PersonalLink) !== normalizeOptionalUrl(originalAthlete.PersonalLink))", setupBody);
        Assert.Contains("const currentPersonalLink = personalLinkInput.value.trim();", listenerBody);
        Assert.Contains("athlete.PersonalLink = currentPersonalLink;", listenerBody);
        Assert.Contains("if (normalizeOptionalUrl(currentPersonalLink) !== normalizeOptionalUrl(originalAthlete.PersonalLink))", listenerBody);
        Assert.Contains("const currentPersonalLink = normalizeOptionalUrl(document.getElementById('personalLinkInput').value);", stateBody);
        Assert.Contains("const personalLinkChanged = currentPersonalLink !== normalizeOptionalUrl(originalAthlete.PersonalLink);", stateBody);
        Assert.DoesNotContain("const currentPersonalLink = document.getElementById('personalLinkInput').value.trim();", stateBody);
        Assert.DoesNotContain("const personalLinkChanged = currentPersonalLink !== originalAthlete.PersonalLink;", stateBody);
    }

    [Fact]
    public async Task EditProfileTextChangeState_IgnoresWhitespaceOnlyDifferences()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var flagSetupStart = html.IndexOf("const currentFlag = window.LwcFlags.getCanonicalFlagName(athlete.Flag);", StringComparison.Ordinal);
        var flagSetupEnd = html.IndexOf("const athletesForFlagOptions", flagSetupStart, StringComparison.Ordinal);
        var flagListenerStart = html.IndexOf("flagInput.addEventListener('input', function ()", StringComparison.Ordinal);
        var flagListenerEnd = html.IndexOf("const terms = this.value.trim().split", flagListenerStart, StringComparison.Ordinal);
        var selectFlagStart = html.IndexOf("function selectFlag(value)", StringComparison.Ordinal);
        var selectFlagEnd = html.IndexOf("const mediaContactInput = document.getElementById('mediaContactInput');", selectFlagStart, StringComparison.Ordinal);
        var whySetupStart = html.IndexOf("const restoreWhyDisplayBtn = document.getElementById('restoreWhyDisplayBtn');", StringComparison.Ordinal);
        var submitHandlerStart = html.IndexOf("// \u2014 SUBMIT CHANGE REQUEST \u2014", whySetupStart, StringComparison.Ordinal);
        var mediaSetupStart = html.IndexOf("const restoreMediaContactBtn = document.getElementById('restoreMediaContactBtn');", submitHandlerStart, StringComparison.Ordinal);
        var personalLinkListenerStart = html.IndexOf("personalLinkInput.addEventListener('input', () =>", mediaSetupStart, StringComparison.Ordinal);
        var mediaListenerStart = html.IndexOf("mediaContactInput.addEventListener('input', () =>", personalLinkListenerStart, StringComparison.Ordinal);
        var whyListenerEnd = html.IndexOf("let skipFlagValidation = false;", mediaListenerStart, StringComparison.Ordinal);
        var stateStart = html.IndexOf("function updateSubmitButtonState()", StringComparison.Ordinal);
        var stateEnd = html.IndexOf("function validateFlagDisplay(value)", stateStart, StringComparison.Ordinal);

        Assert.True(flagSetupStart >= 0);
        Assert.True(flagSetupEnd > flagSetupStart);
        Assert.True(flagListenerStart >= 0);
        Assert.True(flagListenerEnd > flagListenerStart);
        Assert.True(selectFlagStart >= 0);
        Assert.True(selectFlagEnd > selectFlagStart);
        Assert.True(whySetupStart >= 0);
        Assert.True(submitHandlerStart > whySetupStart);
        Assert.True(mediaSetupStart > submitHandlerStart);
        Assert.True(personalLinkListenerStart > mediaSetupStart);
        Assert.True(mediaListenerStart > personalLinkListenerStart);
        Assert.True(whyListenerEnd > mediaListenerStart);
        Assert.True(stateStart >= 0);
        Assert.True(stateEnd > stateStart);

        var flagSetupBody = html[flagSetupStart..flagSetupEnd];
        var flagListenerBody = html[flagListenerStart..flagListenerEnd];
        var selectFlagBody = html[selectFlagStart..selectFlagEnd];
        var whySetupBody = html[whySetupStart..submitHandlerStart];
        var mediaSetupBody = html[mediaSetupStart..personalLinkListenerStart];
        var mediaAndWhyListenerBody = html[mediaListenerStart..whyListenerEnd];
        var stateBody = html[stateStart..stateEnd];

        Assert.Contains("function normalizeEditText(value)", html);
        Assert.Contains("return typeof value === 'string' ? value.trim() : '';", html);
        Assert.Contains("if (normalizeEditText(athlete.Flag) !== normalizeEditText(originalAthlete.Flag))", flagSetupBody);
        Assert.Contains("if (normalizeEditText(flagInput.value) !== normalizeEditText(originalAthlete.Flag))", flagListenerBody);
        Assert.Contains("if (normalizeEditText(value) !== normalizeEditText(originalAthlete.Flag))", selectFlagBody);
        Assert.Contains("if (normalizeEditText(athlete.Why) !== normalizeEditText(originalAthlete.Why))", whySetupBody);
        Assert.Contains("if (normalizeEditText(athlete.MediaContact) !== normalizeEditText(originalAthlete.MediaContact))", mediaSetupBody);
        Assert.Contains("if (normalizeEditText(mediaContactInput.value) !== normalizeEditText(originalAthlete.MediaContact))", mediaAndWhyListenerBody);
        Assert.Contains("if (normalizeEditText(whyDisplayInput.value) !== normalizeEditText(originalAthlete.Why))", mediaAndWhyListenerBody);
        Assert.Contains("const currentFlag = normalizeEditText(document.getElementById('flagDisplayInput').value);", stateBody);
        Assert.Contains("const currentMediaContact = normalizeEditText(document.getElementById('mediaContactInput').value);", stateBody);
        Assert.Contains("const currentWhyDisplay = normalizeEditText(document.getElementById('whyDisplayInput').value);", stateBody);
        Assert.Contains("const flagChanged = currentFlag !== normalizeEditText(originalAthlete.Flag);", stateBody);
        Assert.Contains("const mediaContactChanged = currentMediaContact !== normalizeEditText(originalAthlete.MediaContact);", stateBody);
        Assert.Contains("const whyChanged = currentWhyDisplay !== normalizeEditText(originalAthlete.Why);", stateBody);
        Assert.DoesNotContain("const flagChanged = currentFlag !== originalAthlete.Flag;", stateBody);
        Assert.DoesNotContain("const mediaContactChanged = currentMediaContact !== originalAthlete.MediaContact;", stateBody);
        Assert.DoesNotContain("const whyChanged = currentWhyDisplay !== originalAthlete.Why;", stateBody);
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
        Assert.Contains("const displayError = error && error.message ? error.message : String(error);", html);
        Assert.Contains("const alertMessage = 'Change request could not be submitted. Please check your connection and try again.';", html);
        Assert.Contains("message: displayError", html);
        Assert.Contains("submitButton.innerHTML = '<span class=\"flow-action__label\">Submit change request</span><i class=\"fa fa-rocket\" aria-hidden=\"true\"></i>';\n                                    submitButton.focus();", html);
        Assert.Contains("customAlert(alertMessage).then(() => {\n                            isEditProfileSubmitting = false;", html);
        Assert.Contains("submitButton.innerHTML = '<span class=\"flow-action__label\">Submit change request</span><i class=\"fa fa-rocket\" aria-hidden=\"true\"></i>';\n                            submitButton.focus();", html);
        Assert.DoesNotContain("customAlert(`Submission failed:\\n\\n${displayError}`)", html);
    }

    [Fact]
    public async Task EditProfileSubmit_EnterKeyUsesExistingSubmitPathForSingleLineFields()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");
        var listenerStart = html.IndexOf("[flagInput, personalLinkInput, mediaContactInput].forEach(input =>", StringComparison.Ordinal);
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
        Assert.Contains("if (/^[a-z][a-z\\d+.-]*:/i.test(v)) return v;", html);
        Assert.Contains("if (/^www\\./i.test(v) || /^[^\\s/:]+\\.[^\\s/]+(?:[/?#].*)?$/i.test(v))", html);
        Assert.Contains("return `https://${v}`;", html);
        Assert.Contains("input.type = 'url';", helperBody);
        Assert.Contains("return input.checkValidity();", helperBody);
        Assert.Contains("if (!isOptionalUrl(v))", validateBody);
        Assert.Contains("customAlert('Please enter a valid URL for your personal link.').then(() => document.getElementById('personalLinkInput')?.focus());", validateBody);
        Assert.DoesNotContain("!validator.isURL(v)", validateBody);
    }

    [Fact]
    public async Task EditProfileFields_AdvertiseExistingRequiredValidation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/edit-profile.html");

        Assert.Contains("id=\"personalLinkInput\"\n                       name=\"personalLink\"\n                       inputmode=\"url\"\n                       autocomplete=\"url\"\n                       autocapitalize=\"none\"\n                       spellcheck=\"false\"\n                       placeholder=\"yourwebsite.com\"", html);
        Assert.Contains("id=\"flagDisplayInput\"\n                       name=\"flagDisplay\"\n                       required\n                       aria-required=\"true\"\n                       minlength=\"3\"\n                       maxlength=\"100\"", html);
        Assert.Contains("id=\"flagDisplayInput\"\n                       name=\"flagDisplay\"\n                       required\n                       aria-required=\"true\"\n                       minlength=\"3\"\n                       maxlength=\"100\"\n                       autocomplete=\"off\"\n                       autocapitalize=\"none\"\n                       spellcheck=\"false\"", html);
        Assert.Contains("customAlert('Flag must be at least 3 characters long.')\n                    .then(() => document.getElementById('flagDisplayInput')?.focus());", html);
        Assert.Contains("customAlert('Flag contains invalid characters.')\n                    .then(() => document.getElementById('flagDisplayInput')?.focus());", html);
        Assert.Contains("id=\"mediaContactInput\"\n                       name=\"mediaContact\"\n                       required\n                       aria-required=\"true\"", html);
        Assert.Contains("id=\"mediaContactInput\"\n                       name=\"mediaContact\"\n                       required\n                       aria-required=\"true\"\n                       inputmode=\"email\"\n                       autocomplete=\"off\"\n                       autocapitalize=\"none\"\n                       spellcheck=\"false\"\n                       placeholder=\"media@example.com or @handle\"", html);
        Assert.Contains("<textarea id=\"whyDisplayInput\"\n                          name=\"whyDisplay\"\n                          rows=\"3\"\n                          required\n                          aria-required=\"true\"", html);
        Assert.Contains("customAlert('Media contact is required.').then(() => document.getElementById('mediaContactInput')?.focus());", html);
        Assert.Contains("customAlert('Your why is the light. Don’t leave us in the dark.').then(() => document.getElementById('whyDisplayInput')?.focus());", html);
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
