using LongevityWorldCup.Website.Tools;
using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ApplicationOnboardingPageTests
{
    [Fact]
    public async Task ApplicationRetry_ReenablesEmailFieldAfterSubmissionFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("accountEmailInput.setAttribute(\"disabled\", \"true\");", html);
        Assert.Contains("accountEmailInput.disabled = false;", html);
        Assert.DoesNotContain("accountEmailInput.setAttribute(\"disabled\", \"false\");", html);
    }

    [Fact]
    public async Task ApplicationSubmissionTimeout_WaitsForServerPublicWorkTimeout()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var match = Regex.Match(javascript, @"APPLICATION_SUBMISSION_TIMEOUT_MS\s*=\s*(\d+)");

        Assert.True(match.Success);
        var timeoutMs = int.Parse(match.Groups[1].Value);
        Assert.True(timeoutMs > PublicRequestTimeoutPolicies.PublicWorkTimeout.TotalMilliseconds);
    }

    [Fact]
    public async Task ApplicationSubmissionReport_IsTimeBoundedBecauseItIsBestEffort()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var submissionTimeoutMatch = Regex.Match(javascript, @"APPLICATION_SUBMISSION_TIMEOUT_MS\s*=\s*(\d+)");
        var reportTimeoutMatch = Regex.Match(javascript, @"APPLICATION_SUBMISSION_REPORT_TIMEOUT_MS\s*=\s*(\d+)");

        Assert.True(submissionTimeoutMatch.Success);
        Assert.True(reportTimeoutMatch.Success);
        Assert.True(int.Parse(reportTimeoutMatch.Groups[1].Value) < int.Parse(submissionTimeoutMatch.Groups[1].Value));
        Assert.Contains("const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;", javascript);
        Assert.Contains("window.setTimeout(() => controller.abort(), timeoutMs)", javascript);
        Assert.Contains("...(controller ? { signal: controller.signal } : {})", javascript);
    }

    [Fact]
    public async Task StartedSubmissionReports_DoNotDelayPrimarySubmission()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/onboarding/convergence.html",
            "/play/proof-upload.html",
            "/play/edit-profile.html"
        })
        {
            var html = await client.GetStringAsync(path);

            Assert.DoesNotContain("await window.sendApplicationSubmissionReport(", html);
            Assert.Contains("void window.sendApplicationSubmissionReport(", html);
            Assert.Contains("window.buildApplicationSubmissionReport(applicantData, submissionId, 'started', submissionKind)", html);
        }
    }

    [Fact]
    public async Task FailedSubmissionReports_DoNotDelayRetryUi()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/onboarding/convergence.html",
            "/play/proof-upload.html",
            "/play/edit-profile.html"
        })
        {
            var html = await client.GetStringAsync(path);

            var guardedFailedReports = Regex.Matches(
                html,
                @"void window\.sendApplicationSubmissionReport\(\s*window\.buildApplicationSubmissionReport\(applicantData, submissionId, 'failed'");
            var unguardedFailedReports = Regex.Matches(
                html,
                @"(?<!void )window\.sendApplicationSubmissionReport\(\s*window\.buildApplicationSubmissionReport\(applicantData, submissionId, 'failed'");

            Assert.Equal(2, guardedFailedReports.Count);
            Assert.Empty(unguardedFailedReports);
        }
    }

    [Fact]
    public async Task ApplicationFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/misc.js");
        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("window.readApplicationErrorMessage = async function (response)", javascript);
        Assert.Contains("window.extractApplicationErrorMessage = function (text, fallback)", javascript);
        Assert.Contains("Number.isFinite(response.status)", javascript);
        Assert.Contains("? `HTTP ${response.status}`", javascript);
        Assert.Contains("return fallback;", javascript);
        Assert.Contains("if (/^(?:<!doctype\\s+html\\b|<html[\\s>])/i.test(raw)) return fallback || 'Request failed';", javascript);
        Assert.Contains("if (typeof data === 'string' && data.trim())", javascript);
        Assert.Contains("if (data && typeof data.message === 'string' && data.message.trim())", javascript);
        Assert.Contains("if (data && data.errors && typeof data.errors === 'object')", javascript);
        Assert.Contains("const collectMessages = function (values)", javascript);
        Assert.Contains(".filter(value => typeof value === 'string')", javascript);
        Assert.Contains("if (Array.isArray(data))", javascript);
        Assert.Contains("if (data && typeof data === 'object' && !Array.isArray(data))", javascript);
        Assert.Contains("return messages.join('\\n');", javascript);
        Assert.DoesNotContain(".map(value => String(value || '').trim())", javascript);
        Assert.Contains("const fallbackError = Number.isFinite(response.status) ? `HTTP ${response.status}` : 'Request failed';", html);
        Assert.Contains("window.readApplicationErrorMessage(response).catch(() => fallbackError).then(badResponse =>", html);
        Assert.DoesNotContain("response.text().then(badResponse =>", html);
    }

    [Fact]
    public async Task ApplicationNetworkFailures_ShowNormalizedErrorMessage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("const displayError = error && error.message ? error.message : String(error);", html);
        Assert.Contains("const alertMessage = 'Application could not be submitted. Please check your connection and try again.';", html);
        Assert.Contains("message: displayError", html);
        Assert.Contains("customAlert(alertMessage)", html);
        Assert.DoesNotContain("customAlert(`An error occurred while submitting your application:\\n\\n${displayError}`)", html);
        Assert.DoesNotContain("customAlert(`An error occurred while submitting your application:\\n\\n${error}`)", html);
    }

    [Fact]
    public async Task ApplicationSubmit_GuardsAgainstDuplicateClicks()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var handlerStart = html.IndexOf("document.getElementById('nextButton').addEventListener('click', async function (event)", StringComparison.Ordinal);
        var handlerEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);

        var handlerBeforeFetch = html[handlerStart..handlerEnd];

        Assert.Contains("let isApplicationSubmitting = false;", html);
        Assert.Contains("const nextButton = document.getElementById('nextButton');", handlerBeforeFetch);
        Assert.Contains("if (isApplicationSubmitting || nextButton.disabled) return;", handlerBeforeFetch);
        Assert.Contains("const applyButton = nextButton;", handlerBeforeFetch);
        Assert.Contains("isApplicationSubmitting = true;", handlerBeforeFetch);
        Assert.Contains("applyButton.disabled = true;", handlerBeforeFetch);
        Assert.Contains("customAlert(`Failed to submit application. Please try again later.\\n\\n${badResponse}`).then(() => {", html);
        Assert.Contains("customAlert(alertMessage).then(() => {", html);
        Assert.Contains("isApplicationSubmitting = false;", html);
    }

    [Fact]
    public async Task ApplicationValidation_FallsBackWhenValidatorScriptIsUnavailable()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("function isEmailAddress(value)", html);
        Assert.Contains("function isOptionalUrl(value)", html);
        Assert.Contains("function normalizeOptionalUrl(value)", html);
        Assert.Contains("if (typeof value !== 'string') return null;", html);
        Assert.Contains("window.validator && typeof window.validator.isEmail === 'function'", html);
        Assert.Contains("window.validator && typeof window.validator.isURL === 'function'", html);
        Assert.Contains("const trimmed = normalizeOptionalUrl(value);", html);
        Assert.Contains("if (/^[a-z][a-z\\d+.-]*:/i.test(trimmed)) return trimmed;", html);
        Assert.Contains("if (/^www\\./i.test(trimmed) || /^[^\\s/:]+\\.[^\\s/]+(?:[/?#].*)?$/i.test(trimmed))", html);
        Assert.Contains("return `https://${trimmed}`;", html);
        Assert.Contains("input.type = 'email';", html);
        Assert.Contains("input.type = 'url';", html);
        Assert.Contains("<input type=\"text\" id=\"personalLink\" name=\"personalLink\" inputmode=\"url\" autocomplete=\"url\" autocapitalize=\"none\" spellcheck=\"false\" placeholder=\"yourwebsite.com\">", html);
        Assert.DoesNotContain("<input type=\"url\" id=\"personalLink\"", html);
        Assert.Contains("<input type=\"text\" id=\"accountEmail\" name=\"accountEmail\" required aria-required=\"true\" inputmode=\"email\" autocomplete=\"email\" autocapitalize=\"none\" spellcheck=\"false\" placeholder=\"pizza_lover@hungry.com\">", html);
        Assert.DoesNotContain("<input type=\"email\" id=\"accountEmail\"", html);
        Assert.Contains("const rawAccountEmail = accountEmailInput.value.trim();", html);
        Assert.Contains("const accountEmail = normalizeContactEmail(accountEmailInput.value);", html);
        Assert.Contains("} else if (!accountEmail) {", html);
        Assert.Contains("accountEmailInput.value = accountEmail;", html);
        Assert.Contains("if (!isOptionalUrl(personalLink)) {", html);
        Assert.Contains("if (!isOptionalUrl(personalLinkValue)) {", html);
        Assert.Contains("if (normalizeContactEmail(accountEmailInput.value)) {", html);
        Assert.DoesNotContain("!validator.isEmail(accountEmail)", html);
        Assert.DoesNotContain("!validator.isURL(personalLink)", html);
        Assert.DoesNotContain("!validator.isURL(personalLinkValue)", html);
    }

    [Fact]
    public async Task ApplicationFinalValidation_StopsAfterFirstInvalidField()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var finalValidationStart = html.IndexOf("// Final stage requires accountEmail", StringComparison.Ordinal);
        var submitStart = html.IndexOf("accountEmailInput.value = accountEmail;", finalValidationStart, StringComparison.Ordinal);

        Assert.True(finalValidationStart >= 0);
        Assert.True(submitStart > finalValidationStart);

        var validationBody = html[finalValidationStart..submitStart];
        var missingEmailAlert = validationBody.IndexOf("customAlert('Please enter your email.');", StringComparison.Ordinal);
        var invalidEmailAlert = validationBody.IndexOf("customAlert('Please enter a valid email address.');", StringComparison.Ordinal);
        var invalidUrlAlert = validationBody.IndexOf("customAlert('Please enter a valid URL for your personal link.');", StringComparison.Ordinal);
        var missingEmailReturn = validationBody.IndexOf("return;", missingEmailAlert, StringComparison.Ordinal);
        var invalidEmailReturn = validationBody.IndexOf("return;", invalidEmailAlert, StringComparison.Ordinal);
        var invalidUrlReturn = validationBody.IndexOf("return;", invalidUrlAlert, StringComparison.Ordinal);

        Assert.True(missingEmailAlert >= 0);
        Assert.True(invalidEmailAlert > missingEmailAlert);
        Assert.True(invalidUrlAlert > invalidEmailAlert);
        Assert.True(missingEmailReturn > missingEmailAlert);
        Assert.True(missingEmailReturn < invalidEmailAlert);
        Assert.True(invalidEmailReturn > invalidEmailAlert);
        Assert.True(invalidEmailReturn < invalidUrlAlert);
        Assert.True(invalidUrlReturn > invalidUrlAlert);
        Assert.DoesNotContain("let isValid = true;", validationBody);
    }

    [Fact]
    public async Task ApplicationStageValidationListeners_AreRegisteredOnce()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("function addStageListenerOnce(element, type, listener, key)", html);
        Assert.Contains("if (!element || element.dataset[key] === 'true') return;", html);
        Assert.Contains("element.addEventListener(type, listener);", html);
        Assert.Contains("element.dataset[key] = 'true';", html);
        Assert.Contains("addStageListenerOnce(nameInput, 'input', checkFormValidityStage1, 'stage1ValidityListener');", html);
        Assert.Contains("addStageListenerOnce(flagInput, 'input', checkFormValidityStage1, 'stage1ValidityListener');", html);
        Assert.Contains("addStageListenerOnce(nameInput, 'blur', validateStage1Inputs, 'stage1ValidateOnBlur');", html);
        Assert.Contains("addStageListenerOnce(flagInput, 'blur', validateStage1Inputs, 'stage1ValidateOnBlur');", html);
        Assert.Contains("addStageListenerOnce(divisionSelect, 'click', validateStage1Inputs, 'stage1ValidateOnClick');", html);
        Assert.Contains("addStageListenerOnce(flagInput, 'input', validateStage1Inputs, 'stage1ValidateOnInput');", html);
        Assert.Contains("addStageListenerOnce(whyInput, 'input', checkFormValidityStage2, 'stage2ValidityListener');", html);
        Assert.Contains("addStageListenerOnce(mediaContactInput, 'input', checkFormValidityStage6, 'stage6ValidityListener');", html);
        Assert.Contains("addStageListenerOnce(personalLinkInput, 'input', checkFormValidityStage6, 'stage6ValidityListener');", html);
        Assert.Contains("addStageListenerOnce(accountEmailInput, 'input', handleAccountEmailInput, 'stage7ValidityListener');", html);
    }

    [Fact]
    public async Task ApplicationIdentityFields_AdvertiseExistingLengthRequirements()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("<input type=\"text\" id=\"name\" name=\"name\" required aria-required=\"true\" aria-describedby=\"nameError\" minlength=\"3\" maxlength=\"100\" autocomplete=\"name\" autocapitalize=\"none\" spellcheck=\"false\"", html);
        Assert.Contains("<span id=\"nameError\" class=\"error-message\" aria-live=\"polite\"></span>", html);
        Assert.Contains("<input type=\"text\" id=\"flag\" name=\"flag\" required aria-required=\"true\" aria-describedby=\"flagError\" minlength=\"3\" maxlength=\"100\" autocomplete=\"off\" autocapitalize=\"none\" spellcheck=\"false\"", html);
        Assert.Contains("<span id=\"flagError\" class=\"error-message\" aria-live=\"polite\"></span>", html);
        Assert.Contains("Name must be at least 3 characters long.", html);
        Assert.Contains("Flag must be at least 3 characters long.", html);
        Assert.Contains("const nameRegex = /^", html);
        Assert.Contains("const flagRegex = /^", html);
        Assert.Contains("{2,99}$/;", html);
    }

    [Fact]
    public async Task ApplicationMotivationField_AdvertisesExistingLengthRequirement()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("<textarea id=\"why\" name=\"why\" rows=\"5\" required aria-required=\"true\" minlength=\"30\" maxlength=\"250\"", html);
        Assert.Contains("if (charCount >= 30 && charCount <= 250)", html);
    }

    [Fact]
    public async Task ApplicationMediaContactField_AdvertisesFreeTextContactFormats()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("<input type=\"text\" id=\"mediaContact\" name=\"mediaContact\" required aria-required=\"true\" inputmode=\"email\" autocomplete=\"off\" autocapitalize=\"none\" spellcheck=\"false\" placeholder=\"media@example.com or @handle\">", html);
        Assert.DoesNotContain("id=\"mediaContact\" name=\"mediaContact\" type=\"email\"", html);
    }

    [Fact]
    public async Task ApplicationContactEmail_ReusesEmailShapedMediaContact()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var helperStart = html.IndexOf("function prefillAccountEmailFromMediaContact()", StringComparison.Ordinal);
        var helperEnd = html.IndexOf("function clearStoredBiomarkerHandoff()", helperStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(helperEnd > helperStart);

        var helperBody = html[helperStart..helperEnd];

        Assert.Contains("const accountEmailInput = document.getElementById('accountEmail');", helperBody);
        Assert.Contains("const mediaContactInput = document.getElementById('mediaContact');", helperBody);
        Assert.Contains("accountEmailInput.value.trim() && accountEmailInput.dataset.prefilledFrom !== 'mediaContact'", helperBody);
        Assert.Contains("const mediaContact = normalizeContactEmail(mediaContactInput.value);", helperBody);
        Assert.Contains("if (mediaContact)", helperBody);
        Assert.Contains("accountEmailInput.value = mediaContact;", helperBody);
        Assert.Contains("accountEmailInput.dataset.prefilledFrom = 'mediaContact';", helperBody);
        Assert.Contains("} else if (accountEmailInput.dataset.prefilledFrom === 'mediaContact') {", helperBody);
        Assert.Contains("accountEmailInput.value = '';", helperBody);
        Assert.Contains("delete accountEmailInput.dataset.prefilledFrom;", helperBody);
        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("const bracketedEmail = /<([^<>]+)>/.exec(contactEmail);", html);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", html);

        Assert.Contains("function handleAccountEmailInput()", html);
        Assert.Contains("delete accountEmailInput.dataset.prefilledFrom;", html);
        Assert.Contains("checkFormValidityStage7();", html);

        var stage7Start = html.IndexOf("case 7:", StringComparison.Ordinal);
        var stage7End = html.IndexOf("break;", stage7Start, StringComparison.Ordinal);

        Assert.True(stage7Start >= 0);
        Assert.True(stage7End > stage7Start);

        var stage7Body = html[stage7Start..stage7End];
        var prefillIndex = stage7Body.IndexOf("prefillAccountEmailFromMediaContact();", StringComparison.Ordinal);
        var listenerIndex = stage7Body.IndexOf("addStageListenerOnce(accountEmailInput, 'input', handleAccountEmailInput, 'stage7ValidityListener');", StringComparison.Ordinal);
        var validateIndex = stage7Body.IndexOf("checkFormValidityStage7();", StringComparison.Ordinal);

        Assert.True(prefillIndex >= 0);
        Assert.True(listenerIndex > prefillIndex);
        Assert.True(validateIndex > prefillIndex);
    }

    [Fact]
    public async Task ApplicationSubmission_TreatsMalformedBiomarkerStorageAsMissing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var parseStart = html.IndexOf("let biomarkerData = null;", StringComparison.Ordinal);
        var parseEnd = html.IndexOf("const paymentOffer = readAdjustedPendingPaymentOffer();", parseStart, StringComparison.Ordinal);

        Assert.True(parseStart >= 0);
        Assert.True(parseEnd > parseStart);

        var parseBody = html[parseStart..parseEnd];
        Assert.Contains("try {", parseBody);
        Assert.Contains("biomarkerData = JSON.parse(getSessionItem('biomarkerData'));", parseBody);
        Assert.Contains("} catch (_) {", parseBody);
        Assert.Contains("biomarkerData = null;", parseBody);
        Assert.Contains("function hasStoredDateOfBirth(biomarkerData)", html);
        Assert.Contains("!hasStoredDateOfBirth(biomarkerData)", parseBody);
        var dateOfBirthGuardStart = html.IndexOf("function hasStoredDateOfBirth(biomarkerData)", StringComparison.Ordinal);
        var dateOfBirthGuardEnd = html.IndexOf("function hasStoredBiomarkerDates(biomarkerData)", dateOfBirthGuardStart, StringComparison.Ordinal);
        Assert.True(dateOfBirthGuardStart >= 0);
        Assert.True(dateOfBirthGuardEnd > dateOfBirthGuardStart);
        var dateOfBirthGuardBody = html[dateOfBirthGuardStart..dateOfBirthGuardEnd];
        Assert.Contains("const parsedDate = new Date(Date.UTC(year, month - 1, day));", dateOfBirthGuardBody);
        Assert.Contains("parsedDate.getUTCFullYear() !== year", dateOfBirthGuardBody);
        Assert.Contains("todayUtc.setUTCHours(0, 0, 0, 0);", dateOfBirthGuardBody);
        Assert.Contains("return parsedDate <= todayUtc;", dateOfBirthGuardBody);
        Assert.Contains("!Array.isArray(biomarkerData.Biomarkers)", parseBody);
        Assert.Contains("!biomarkerData.Biomarkers.length", parseBody);
        Assert.Contains("function hasStoredBiomarkerDates(biomarkerData)", html);
        Assert.Contains("!hasStoredBiomarkerDates(biomarkerData)", parseBody);
        Assert.Contains("function hasStoredBiomarkerDate(value)", html);
        Assert.Contains("const match = /^(\\d{4})-(\\d{2})-(\\d{2})$/.exec(value.trim());", html);
        Assert.Contains("return parsedDate <= todayUtc;", html);
        Assert.Contains("function hasStoredBiomarkerValues(biomarkerData)", html);
        Assert.Contains("!hasStoredBiomarkerValues(biomarkerData)", parseBody);
        Assert.Contains("function hasStoredBiomarkerValue(value)", html);
        Assert.Contains("Object.keys(entry).some(key => key !== 'Date' && hasStoredBiomarkerValue(entry[key]))", html);
        Assert.Contains("if (value === null || value === undefined) return false;", html);
        Assert.Contains("if (typeof value === 'boolean') return false;", html);
        Assert.Contains("if (typeof value !== 'number' && typeof value !== 'string') return false;", html);
        Assert.Contains("return Number.isFinite(Number(value));", html);
        Assert.Contains("clearStoredBiomarkerHandoff();", parseBody);
        Assert.Contains("function clearStoredBiomarkerHandoff()", html);
        Assert.Contains("removeSessionItem('biomarkerData');", html);
        Assert.Contains("removeSessionItem('chronoPhenoDifference');", html);
        Assert.Contains("removeSessionItem('chronoBortzDifference');", html);
        Assert.Contains("customAlert('Biomarker data is missing. Please complete the biomarker step.')", parseBody);
        Assert.Contains(".then(() => window.location.href = '/onboarding/pheno-age');", parseBody);
    }

    [Fact]
    public async Task ApplicationSubmit_UsesSafeStorageForStoredMetadata()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var collectStart = html.IndexOf("function collectApplicantData()", StringComparison.Ordinal);
        var collectEnd = html.IndexOf("return applicantData;", collectStart, StringComparison.Ordinal);

        Assert.True(collectStart >= 0);
        Assert.True(collectEnd > collectStart);

        var collectBody = html[collectStart..collectEnd];

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const chronoPhenoDifference = readStoredAgeDifference('chronoPhenoDifference');", collectBody);
        Assert.Contains("const chronoBortzDifference = readStoredAgeDifference('chronoBortzDifference');", collectBody);
        Assert.Contains("function readStoredAgeDifference(key)", html);
        Assert.Contains("if (text && Number.isFinite(Number(text)))", html);
        Assert.Contains("removeSessionItem(key);", html);
        Assert.Contains("function readPendingPaymentOffer()", html);
        Assert.Contains("const rawOffer = getSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.Contains("const paymentOffer = JSON.parse(rawOffer);", html);
        Assert.Contains("if (isUsablePaymentOffer(paymentOffer))", html);
        Assert.Contains("function isUsablePaymentOffer(paymentOffer)", html);
        Assert.Contains("typeof paymentOffer.source === 'string'", html);
        Assert.Contains("typeof paymentOffer.offerType === 'string'", html);
        Assert.Contains("typeof paymentOffer.currency === 'string'", html);
        Assert.Contains("typeof paymentOffer.amountUsd === 'number'", html);
        Assert.Contains("Number.isFinite(paymentOffer.amountUsd)", html);
        Assert.Contains("paymentOffer.amountUsd >= 0", html);
        Assert.Contains("function clearPendingPaymentOffer()", html);
        Assert.Contains("clearPendingPaymentOffer();", html);
        Assert.Contains("function readAdjustedPendingPaymentOffer()", html);
        Assert.Contains("const paymentOffer = readPendingPaymentOffer();", html);
        Assert.Contains("if (!paymentOffer) return null;", html);
        Assert.Contains("const adjustedPaymentOffer = window.applyPaymentAdjustmentsToPaymentOffer", html);
        Assert.Contains("if (!adjustedPaymentOffer) return null;", html);
        Assert.Contains("if (isUsablePaymentOffer(adjustedPaymentOffer)) return adjustedPaymentOffer;", html);
        Assert.Contains("customAlert('Payment details could not be prepared. Please try again.');", html);
        Assert.Contains("return undefined;", html);
        Assert.Contains("const paymentOffer = readAdjustedPendingPaymentOffer();", collectBody);
        Assert.Contains("if (paymentOffer === undefined) return;", collectBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.DoesNotContain("window.applyPaymentAdjustmentsToPaymentOffer(paymentOffer);", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoPhenoDifference')", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoBortzDifference')", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem(PENDING_PAYMENT_OFFER_KEY)", collectBody);
        Assert.DoesNotContain("JSON.parse(getSessionItem(PENDING_PAYMENT_OFFER_KEY) || 'null')", collectBody);
    }

    [Fact]
    public async Task ApplicationDivisionSelect_UsesFallbackWhenApiFailsOrReturnsEmpty()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("const fallbackDivisions = [\"Men's\", \"Women's\", \"Open\"];", html);
        Assert.Contains("function populateDivisionSelect(divisions)", html);
        Assert.Contains("const availableDivisions = Array.isArray(divisions) && divisions.length ? divisions : fallbackDivisions;", html);
        Assert.Contains(".then(response => response.ok ? response.json() : fallbackDivisions)", html);
        Assert.Contains(".then(populateDivisionSelect)", html);
        Assert.Contains("populateDivisionSelect(fallbackDivisions);", html);
    }

    [Fact]
    public async Task ApplicationFlagAutocomplete_StillInitializesWhenFlagApiFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("athletes.flatMap(getExistingAthleteNameCandidates)", html);
        Assert.Contains("function getExistingAthleteNameCandidates(athlete)", html);
        Assert.Contains("if (!athlete || typeof athlete !== 'object' || Array.isArray(athlete))", html);
        Assert.Contains(".filter(name => typeof name === 'string')", html);
        Assert.Contains(".map(name => name.trim())", html);
        Assert.Contains(".filter(Boolean)", html);
        Assert.Contains(".then(response => response.ok ? response.json() : [])", html);
        Assert.Contains("console.error('Error fetching flags:', error);", html);
        Assert.Contains("return [];", html);
        Assert.Contains("const flagOptions = window.LwcFlags.buildFlagOptions(flags, athletes);", html);
    }

    [Fact]
    public async Task ApplicationNameFetch_RevalidatesStageOneWhenAthletesLoad()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var fetchStart = html.IndexOf("athletesPromise\n                .then(athletes =>", StringComparison.Ordinal);
        var flagStart = html.IndexOf("Promise.all([", fetchStart, StringComparison.Ordinal);
        var athletesFetchStart = html.IndexOf("const athletesPromise = fetch('/api/data/athletes')", StringComparison.Ordinal);

        Assert.True(athletesFetchStart >= 0);
        Assert.True(fetchStart >= 0);
        Assert.True(flagStart > fetchStart);

        var athletesFetchBody = html[athletesFetchStart..fetchStart];
        var fetchBody = html[fetchStart..flagStart];

        Assert.Contains(".then(response => response.ok ? response.json() : [])", athletesFetchBody);
        Assert.Contains("existingAthleteNames = Array.isArray(athletes)", fetchBody);
        Assert.Contains("checkFormValidityStage1();", fetchBody);
    }

    [Fact]
    public async Task ApplicationNameValidation_ChecksExistingDisplayNames()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var fetchStart = html.IndexOf("const athletesPromise = fetch('/api/data/athletes')", StringComparison.Ordinal);
        var validateStart = html.IndexOf("function validateName(name)", StringComparison.Ordinal);

        Assert.True(fetchStart >= 0);
        Assert.True(validateStart > fetchStart);

        var fetchBody = html[fetchStart..validateStart];

        Assert.Contains("athletes.flatMap(getExistingAthleteNameCandidates)", fetchBody);
        Assert.Contains("function getExistingAthleteNameCandidates(athlete)", html);
        Assert.Contains("return [athlete.Name, athlete.DisplayName]", html);
        Assert.Contains(".filter(name => typeof name === 'string')", html);
        Assert.Contains(".filter(Boolean)", fetchBody);
        Assert.Contains("const athleteNames = Array.isArray(existingAthleteNames) ? existingAthleteNames : [];", html);
        Assert.Contains("existingName.toLowerCase() === name.toLowerCase()", html);
        Assert.Contains("An athlete with that name is already registered.", html);
    }

    [Fact]
    public async Task ApplicationSubmit_RevalidatesIdentityBeforePosting()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var handlerStart = html.IndexOf("document.getElementById('nextButton').addEventListener('click', async function (event)", StringComparison.Ordinal);
        var collectStart = html.IndexOf("const applicantData = collectApplicantData();", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(collectStart > handlerStart);

        var handlerBeforeCollect = html[handlerStart..collectStart];

        Assert.Contains("function validateApplicationIdentity()", html);
        Assert.Contains("function showApplicationIdentityIssue(validationResult)", html);
        Assert.Contains("const nameValidationResult = validateName(document.getElementById('name').value);", html);
        Assert.Contains("const flagValidationResult = validateFlag(document.getElementById('flag').value);", html);
        Assert.Contains("error: 'Please select a division.'", html);
        Assert.Contains("const identityValidationResult = validateApplicationIdentity();", handlerBeforeCollect);
        Assert.Contains("if (!identityValidationResult.valid)", handlerBeforeCollect);
        Assert.Contains("showApplicationIdentityIssue(identityValidationResult);", handlerBeforeCollect);
        Assert.Contains("return;", handlerBeforeCollect);
    }

    [Fact]
    public async Task ApplicationSuccessHandoff_UsesSafeStorageBeforeNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var successStart = html.IndexOf("customAlert(nextStepMessage).then(() =>", StringComparison.Ordinal);
        var successEnd = html.IndexOf("window.location.href = '/review';", successStart, StringComparison.Ordinal);

        Assert.True(successStart >= 0);
        Assert.True(successEnd > successStart);

        var successBody = html[successStart..successEnd];

        Assert.Contains("function setBrowserStorageItem(storageName, key, value)", html);
        Assert.Contains("function removeBrowserStorageItem(storageName, key)", html);
        Assert.Contains("function getAthleteContactEmailStorageKey(athleteName)", html);
        Assert.Contains("return name ? 'contactEmailFor:' + name : null;", html);
        Assert.Contains("function rememberAthleteContactEmail(athleteName, email)", html);
        Assert.Contains("function getCheckoutLink(submitResult)", html);
        Assert.Contains("typeof submitResult.checkoutLink === 'string'", html);
        Assert.Contains("function getInvoiceId(submitResult)", html);
        Assert.Contains("typeof submitResult.invoiceId === 'string'", html);
        Assert.Contains("const checkoutLink = getCheckoutLink(submitResult);", html);
        Assert.Contains("const invoiceId = getInvoiceId(submitResult);", html);
        Assert.Contains("if (checkoutLink)", successBody);
        Assert.Contains("if (invoiceId)", successBody);
        Assert.Contains("invoiceId: invoiceId", successBody);
        Assert.Contains("window.location.href = checkoutLink;", successBody);
        Assert.Contains("rememberAthleteContactEmail(applicantData.name, applicantData.accountEmail);", successBody);
        Assert.Contains("setSessionItem('contactEmail', applicantData.accountEmail);", successBody);
        Assert.Contains("setLocalItem('contactEmail', applicantData.accountEmail);", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", successBody);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_INVOICE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("setLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("submissionType: 'application'", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", successBody);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", successBody);
        Assert.DoesNotContain("window.location.href = submitResult.checkoutLink;", successBody);
        Assert.DoesNotContain("if (submitResult.invoiceId)", successBody);
        Assert.DoesNotContain("invoiceId: submitResult.invoiceId", successBody);
        Assert.DoesNotContain("sessionStorage.setItem(", successBody);
        Assert.DoesNotContain("localStorage.setItem(", successBody);
        Assert.DoesNotContain("sessionStorage.removeItem(", successBody);
        Assert.DoesNotContain("localStorage.removeItem(", successBody);
    }

    [Fact]
    public async Task ApplicationSubmit_DoesNotBlockOnRememberedAthleteStorage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var submitStart = html.IndexOf("const applicantData = collectApplicantData();", StringComparison.Ordinal);
        var submitEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", submitStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(submitEnd > submitStart);

        var submitBody = html[submitStart..submitEnd];

        Assert.Contains("function setBrowserStorageItem(storageName, key, value)", html);
        Assert.Contains("setLocalItem('selectedAthleteName', applicantData.name);", submitBody);
        Assert.Contains("const accountEmail = normalizeContactEmail(accountEmailInput.value) || accountEmailInput.value.trim();", html);
        Assert.Contains("accountEmail: accountEmail,", html);
        Assert.DoesNotContain("localStorage.setItem('selectedAthleteName', applicantData.name);", submitBody);
    }

    [Fact]
    public async Task ProfilePhotoSelection_ClearsInputAfterCapturingFile()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var selectionStart = html.IndexOf("function handleProfileUploadChange(event)", StringComparison.Ordinal);
        var selectionEnd = html.IndexOf("if (profilePicInput && !profilePicInput.hasAttribute('data-listener'))", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(selectionEnd > selectionStart);

        var selectionBody = html[selectionStart..selectionEnd];
        Assert.Contains("const input = event.target;", selectionBody);
        Assert.Contains("const file = input.files[0];", selectionBody);
        Assert.Contains("input.value = '';", selectionBody);
        Assert.Contains("reader.onerror = function ()", selectionBody);
        Assert.Contains("customAlert('Profile picture upload failed.');", selectionBody);
    }

    [Fact]
    public async Task ProfilePhotoSelection_RejectsUnsupportedFormatsBeforeReading()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var selectionStart = html.IndexOf("function handleProfileUploadChange(event)", StringComparison.Ordinal);
        var readerStart = html.IndexOf("const reader = new FileReader();", selectionStart, StringComparison.Ordinal);

        Assert.True(selectionStart >= 0);
        Assert.True(readerStart > selectionStart);

        var beforeReader = html[selectionStart..readerStart];

        Assert.Contains("id=\"profilePicInput\" accept=\"image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp\"", html);
        Assert.Contains("id=\"profileCameraInput\" accept=\"image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp\"", html);
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
    public async Task ProfilePhotoSelection_ReplacesExistingCropperBeforeNewImage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var loadStart = html.IndexOf("reader.onload = function (e)", StringComparison.Ordinal);
        var loadEnd = html.IndexOf("reader.onerror = function ()", loadStart, StringComparison.Ordinal);

        Assert.True(loadStart >= 0);
        Assert.True(loadEnd > loadStart);

        var loadBody = html[loadStart..loadEnd];

        Assert.Contains("if (cropper)", loadBody);
        Assert.Contains("cropper.destroy();", loadBody);
        Assert.Contains("cropper = null;", loadBody);
        Assert.Contains("document.getElementById('cropperImage').src = e.target.result;", loadBody);
        Assert.Contains("cropper = new Cropper(document.getElementById('cropperImage'),", loadBody);
        Assert.DoesNotContain("if (!cropper)", loadBody);
    }

    [Fact]
    public async Task ProfilePhotoCrop_CanBeCanceledBackToUploadMode()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var cancelStart = html.IndexOf("cancelProfileCropButton.addEventListener('click'", StringComparison.Ordinal);
        var cancelEnd = html.IndexOf("profilePicInput.setAttribute('data-listener', 'true');", cancelStart, StringComparison.Ordinal);

        Assert.Contains("id=\"cancelProfileCropButton\"", html);
        Assert.True(cancelStart >= 0);
        Assert.True(cancelEnd > cancelStart);

        var cancelBody = html[cancelStart..cancelEnd];
        Assert.Contains("if (isProfileCropProcessing) return;", cancelBody);
        Assert.Contains("cropper.destroy();", cancelBody);
        Assert.Contains("cropper = null;", cancelBody);
        Assert.Contains("document.getElementById('uploadPart').style.display = '';", cancelBody);
        Assert.Contains("document.getElementById('croppingPart').style.display = 'none';", cancelBody);
        Assert.Contains("nextButton.disabled = !profilePic;", cancelBody);
    }

    [Fact]
    public async Task ProfilePhotoCrop_FallsBackToRawCropWhenOptimizationFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var cropStart = html.IndexOf("cropButton.addEventListener('click'", StringComparison.Ordinal);
        var cropEnd = html.IndexOf("cancelProfileCropButton.addEventListener('click'", cropStart, StringComparison.Ordinal);

        Assert.True(cropStart >= 0);
        Assert.True(cropEnd > cropStart);

        var cropBody = html[cropStart..cropEnd];
        Assert.Contains("let isProfileCropProcessing = false;", html);
        Assert.Contains("const activeCropper = cropper;", cropBody);
        Assert.Contains("if (isProfileCropProcessing || !activeCropper) return;", cropBody);
        Assert.Contains("isProfileCropProcessing = true;", cropBody);
        Assert.Contains("cropButton.disabled = true;", cropBody);
        Assert.Contains("cancelProfileCropButton.disabled = true;", cropBody);
        Assert.Contains("activeCropper.getCroppedCanvas({", cropBody);
        Assert.Contains("let croppedImageDataURL = raw;", cropBody);
        Assert.Contains("try {", cropBody);
        Assert.Contains("await window.optimizeImageClient(raw, window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS);", cropBody);
        Assert.Contains("} catch {", cropBody);
        Assert.Contains("croppedImageDataURL = raw;", cropBody);
        Assert.Contains("profilePic = croppedImageDataURL;", cropBody);
        Assert.Contains("activeCropper.destroy();", cropBody);
        Assert.Contains("if (cropper === activeCropper)", cropBody);
        Assert.Contains("isProfileCropProcessing = false;", cropBody);
        Assert.Contains("cropButton.disabled = false;", cropBody);
        Assert.Contains("cancelProfileCropButton.disabled = false;", cropBody);
    }
}
