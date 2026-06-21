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
        Assert.Contains("window.readApplicationErrorMessage(response).then(badResponse =>", html);
        Assert.DoesNotContain("response.text().then(badResponse =>", html);
    }

    [Fact]
    public async Task ApplicationNetworkFailures_ShowNormalizedErrorMessage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("const displayError = error && error.message ? error.message : String(error);", html);
        Assert.Contains("message: displayError", html);
        Assert.Contains("customAlert(`An error occurred while submitting your application:\\n\\n${displayError}`)", html);
        Assert.DoesNotContain("customAlert(`An error occurred while submitting your application:\\n\\n${error}`)", html);
    }

    [Fact]
    public async Task ApplicationValidation_FallsBackWhenValidatorScriptIsUnavailable()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("function isEmailAddress(value)", html);
        Assert.Contains("function isOptionalUrl(value)", html);
        Assert.Contains("window.validator && typeof window.validator.isEmail === 'function'", html);
        Assert.Contains("window.validator && typeof window.validator.isURL === 'function'", html);
        Assert.Contains("input.type = 'email';", html);
        Assert.Contains("input.type = 'url';", html);
        Assert.Contains("} else if (!isEmailAddress(accountEmail)) {", html);
        Assert.Contains("if (!isOptionalUrl(personalLink)) {", html);
        Assert.Contains("if (!isOptionalUrl(personalLinkValue)) {", html);
        Assert.Contains("if (isEmailAddress(accountEmailInput.value)) {", html);
        Assert.DoesNotContain("!validator.isEmail(accountEmail)", html);
        Assert.DoesNotContain("!validator.isURL(personalLink)", html);
        Assert.DoesNotContain("!validator.isURL(personalLinkValue)", html);
    }

    [Fact]
    public async Task ApplicationSubmission_TreatsMalformedBiomarkerStorageAsMissing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var parseStart = html.IndexOf("let biomarkerData = null;", StringComparison.Ordinal);
        var parseEnd = html.IndexOf("let paymentOffer = null;", parseStart, StringComparison.Ordinal);

        Assert.True(parseStart >= 0);
        Assert.True(parseEnd > parseStart);

        var parseBody = html[parseStart..parseEnd];
        Assert.Contains("try {", parseBody);
        Assert.Contains("biomarkerData = JSON.parse(getSessionItem('biomarkerData'));", parseBody);
        Assert.Contains("} catch (_) {", parseBody);
        Assert.Contains("biomarkerData = null;", parseBody);
        Assert.Contains("!Array.isArray(biomarkerData.Biomarkers)", parseBody);
        Assert.Contains("!biomarkerData.Biomarkers.length", parseBody);
        Assert.Contains("clearStoredBiomarkerHandoff();", parseBody);
        Assert.Contains("function clearStoredBiomarkerHandoff()", html);
        Assert.Contains("removeSessionItem('biomarkerData');", html);
        Assert.Contains("removeSessionItem('chronoPhenoDifference');", html);
        Assert.Contains("removeSessionItem('chronoBortzDifference');", html);
        Assert.Contains("customAlert('Biomarker data is missing. Please complete the biomarker form.');", parseBody);
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
        Assert.Contains("const chronoPhenoDifference = getSessionItem('chronoPhenoDifference');", collectBody);
        Assert.Contains("const chronoBortzDifference = getSessionItem('chronoBortzDifference');", collectBody);
        Assert.Contains("JSON.parse(getSessionItem(PENDING_PAYMENT_OFFER_KEY) || 'null')", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoPhenoDifference')", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoBortzDifference')", collectBody);
        Assert.DoesNotContain("sessionStorage.getItem(PENDING_PAYMENT_OFFER_KEY)", collectBody);
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
        Assert.Contains(".then(populateDivisionSelect)", html);
        Assert.Contains("populateDivisionSelect(fallbackDivisions);", html);
    }

    [Fact]
    public async Task ApplicationFlagAutocomplete_StillInitializesWhenFlagApiFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");

        Assert.Contains("existingAthleteNames = Array.isArray(athletes) ? athletes.map(athlete => athlete.Name) : [];", html);
        Assert.Contains(".then(response => response.ok ? response.json() : [])", html);
        Assert.Contains("console.error('Error fetching flags:', error);", html);
        Assert.Contains("return [];", html);
        Assert.Contains("const flagOptions = window.LwcFlags.buildFlagOptions(flags, athletes);", html);
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
        Assert.Contains("setSessionItem('contactEmail', applicantData.accountEmail);", successBody);
        Assert.Contains("setLocalItem('contactEmail', applicantData.accountEmail);", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", successBody);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_INVOICE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("setLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("submissionType: 'application'", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", successBody);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", successBody);
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
    public async Task ProfilePhotoCrop_CanBeCanceledBackToUploadMode()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var cancelStart = html.IndexOf("document.getElementById('cancelProfileCropButton').addEventListener('click'", StringComparison.Ordinal);
        var cancelEnd = html.IndexOf("profilePicInput.setAttribute('data-listener', 'true');", cancelStart, StringComparison.Ordinal);

        Assert.Contains("id=\"cancelProfileCropButton\"", html);
        Assert.True(cancelStart >= 0);
        Assert.True(cancelEnd > cancelStart);

        var cancelBody = html[cancelStart..cancelEnd];
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
        var cropStart = html.IndexOf("document.getElementById('cropButton').addEventListener('click'", StringComparison.Ordinal);
        var cropEnd = html.IndexOf("document.getElementById('cancelProfileCropButton').addEventListener('click'", cropStart, StringComparison.Ordinal);

        Assert.True(cropStart >= 0);
        Assert.True(cropEnd > cropStart);

        var cropBody = html[cropStart..cropEnd];
        Assert.Contains("let croppedImageDataURL = raw;", cropBody);
        Assert.Contains("try {", cropBody);
        Assert.Contains("await window.optimizeImageClient(raw, window.PROFILE_IMAGE_OPTIMIZATION_OPTIONS);", cropBody);
        Assert.Contains("} catch {", cropBody);
        Assert.Contains("croppedImageDataURL = raw;", cropBody);
        Assert.Contains("profilePic = croppedImageDataURL;", cropBody);
    }
}
