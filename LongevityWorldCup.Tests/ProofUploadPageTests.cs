using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofUploadPageTests
{
    [Theory]
    [InlineData("/onboarding/convergence.html")]
    [InlineData("/play/proof-upload.html")]
    public async Task ProofUploadPages_LoadVersionedProofHelper(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("/js/proof-helpers.js?v=", html);
        Assert.DoesNotContain("{{ASSET_PROOF_HELPERS_JS}}", html);
    }

    [Theory]
    [InlineData("/onboarding/convergence.html")]
    [InlineData("/play/proof-upload.html")]
    public async Task ProofUploadPages_AdvertiseSupportedProofFileFormats(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("id=\"proofPicInput\" accept=\"image/jpeg,image/png,image/webp,application/pdf,.jpg,.jpeg,.png,.webp,.pdf\"", html);
        Assert.Contains("id=\"proofCameraInput\" accept=\"image/jpeg,image/png,image/webp,.jpg,.jpeg,.png,.webp\"", html);
        Assert.DoesNotContain("id=\"proofPicInput\" accept=\"image/*,application/pdf\"", html);
        Assert.DoesNotContain("id=\"proofCameraInput\" accept=\"image/*\"", html);
    }

    [Fact]
    public async Task ProofHelper_WaitsForPdfRendererBeforeProcessingPdfUploads()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("function ensurePdfJsReady()", javascript);
        Assert.Contains("const pdfLib = await ensurePdfJsReady();", javascript);
        Assert.Contains("const loadingTask = pdfLib.getDocument({ data: arrayBuffer });", javascript);
        Assert.Contains("if (proofPics.length >= 9)", javascript);
        Assert.DoesNotContain("const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });", javascript);
    }

    [Fact]
    public async Task ProofHelper_ClearsFileInputAfterFailedProofProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        var catchIndex = javascript.IndexOf("} catch (error) {", StringComparison.Ordinal);
        var finallyIndex = javascript.IndexOf("} finally {", StringComparison.Ordinal);
        var resetIndex = javascript.IndexOf("if (input) input.value = \"\";", finallyIndex, StringComparison.Ordinal);
        var hideLoadingIndex = javascript.IndexOf("hideLoading();", resetIndex, StringComparison.Ordinal);

        Assert.True(catchIndex >= 0);
        Assert.True(finallyIndex > catchIndex);
        Assert.True(resetIndex > finallyIndex);
        Assert.True(hideLoadingIndex > resetIndex);
    }

    [Fact]
    public async Task ProofHelper_FallsBackToRawImageWhenClientOptimizationFails()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("const optimizeProofImageOrFallback = async raw =>", javascript);
        Assert.Contains("return dataUrl || raw;", javascript);
        Assert.Contains("} catch (_) {", javascript);
        Assert.Contains("return raw;", javascript);
        Assert.Contains("const optimizedPage = await optimizeProofImageOrFallback(rawPage);", javascript);
        Assert.Contains("const dataUrl = await optimizeProofImageOrFallback(raw);", javascript);
        Assert.DoesNotContain("await window.optimizeImageClient(rawPage, proofOptimizationOptions);", javascript);
    }

    [Fact]
    public async Task ProofHelper_RejectsUnsupportedFilesBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (files, input)", StringComparison.Ordinal);
        var loadingStart = javascript.IndexOf("showLoading();", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(loadingStart > handlerStart);

        var beforeLoading = javascript[handlerStart..loadingStart];

        Assert.Contains("function isSupportedProofFile(file)", javascript);
        Assert.Contains("type === 'application/pdf'", javascript);
        Assert.Contains("type === 'image/jpeg'", javascript);
        Assert.Contains("type === 'image/png'", javascript);
        Assert.Contains("type === 'image/webp'", javascript);
        Assert.Contains("extension === 'jpg'", javascript);
        Assert.Contains("extension === 'jpeg'", javascript);
        Assert.Contains("extension === 'png'", javascript);
        Assert.Contains("extension === 'webp'", javascript);
        Assert.Contains("const selectedFiles = Array.from(files || []);", beforeLoading);
        Assert.Contains("const unsupportedFiles = selectedFiles.filter(file => !isSupportedProofFile(file));", beforeLoading);
        Assert.Contains("if (input) input.value = \"\";", beforeLoading);
        Assert.Contains("customAlert('Proof files must be JPG, PNG, WebP, or PDF.');", beforeLoading);
        Assert.Contains("return;", beforeLoading);
        Assert.Contains("if (isProofPdfFile(file))", javascript);
        Assert.DoesNotContain("if (file.type === 'application/pdf')", javascript);
    }

    [Fact]
    public async Task ProofHelper_InstructionsAskForDateAndSource()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("showing each submitted biomarker, the collection date, and the lab or report source", javascript);
        Assert.Contains("These images will be <strong>public</strong>", javascript);
    }

    [Fact]
    public async Task ProofHelper_RequiresChecklistWhenBiomarkersAreListed()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("function areRequiredProofChecklistItemsChecked(biomarkerChecklistContainer)", javascript);
        Assert.Contains("const checkboxes = Array.from(biomarkerChecklistContainer.querySelectorAll('.biomarker-checkbox'));", javascript);
        Assert.Contains("return checkboxes.length === 0 || checkboxes.every(input => input.checked);", javascript);
        Assert.Contains("const checklistComplete = areRequiredProofChecklistItemsChecked(biomarkerChecklistContainer);", javascript);
        Assert.Contains("nextButton.disabled = !(hasProofs && checklistComplete);", javascript);
        Assert.Contains("input.addEventListener('change', function ()", javascript);
        Assert.Contains("checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", javascript);
    }

    [Fact]
    public async Task ResultUploadFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("window.readApplicationErrorMessage(response).then(badResponse =>", html);
        Assert.DoesNotContain("response.text().then(badResponse =>", html);
    }

    [Fact]
    public async Task ResultUploadNetworkFailures_ShowNormalizedErrorMessage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("const displayError = error && error.message ? error.message : String(error);", html);
        Assert.Contains("message: displayError", html);
        Assert.Contains("customAlert(`An error occurred:\\n\\n${displayError}`)", html);
        Assert.DoesNotContain("customAlert(`An error occurred:\\n\\n${error}`)", html);
    }

    [Fact]
    public async Task ResultUploadSubmit_GuardsAgainstDuplicateClicks()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");
        var handlerStart = html.IndexOf("submitButton.addEventListener('click', async function ()", StringComparison.Ordinal);
        var handlerEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);

        var handlerBeforeFetch = html[handlerStart..handlerEnd];

        Assert.Contains("let isResultUploadSubmitting = false;", html);
        Assert.Contains("if (isResultUploadSubmitting || submitButton.disabled) return;", handlerBeforeFetch);
        Assert.Contains("isResultUploadSubmitting = true;", handlerBeforeFetch);
        Assert.Contains("submitButton.disabled = true;", handlerBeforeFetch);
        Assert.Contains("customAlert(`Failed to submit results. Please try again later.\\n\\n${badResponse}`).then(() => {\n                                    isResultUploadSubmitting = false;", html);
        Assert.Contains("customAlert(`An error occurred:\\n\\n${displayError}`).then(() => {\n                            isResultUploadSubmitting = false;", html);
    }

    [Fact]
    public async Task ResultUpload_TreatsMalformedBiomarkerStorageAsMissing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");
        var parseStart = html.IndexOf("let biomarkerData = null;", StringComparison.Ordinal);
        var parseEnd = html.IndexOf("const submissionId = window.createApplicationSubmissionId();", parseStart, StringComparison.Ordinal);

        Assert.True(parseStart >= 0);
        Assert.True(parseEnd > parseStart);

        var parseBody = html[parseStart..parseEnd];
        Assert.Contains("try {", parseBody);
        Assert.Contains("biomarkerData = JSON.parse(getSessionItem('biomarkerData'));", parseBody);
        Assert.Contains("} catch (_) {", parseBody);
        Assert.Contains("biomarkerData = null;", parseBody);
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
        Assert.Contains("return Number.isFinite(Number(value));", html);
        Assert.Contains("clearStoredBiomarkerHandoff();", parseBody);
        Assert.Contains("function clearStoredBiomarkerHandoff()", html);
        Assert.Contains("removeSessionItem('biomarkerData');", html);
        Assert.Contains("removeSessionItem('chronoPhenoDifference');", html);
        Assert.Contains("removeSessionItem('chronoBortzDifference');", html);
        Assert.Contains("customAlert('Biomarker data is missing. Please fill out the biomarker form first.')", parseBody);
        Assert.Contains(".then(() => window.location.href = '/dashboard');", parseBody);
    }

    [Fact]
    public async Task ResultUploadSubmit_UsesSafeStorageForStoredMetadata()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");
        var submitStart = html.IndexOf("const applicantData = {", StringComparison.Ordinal);
        var submitEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", submitStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(submitEnd > submitStart);

        var submitBody = html[submitStart..submitEnd];

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("function readResultUploadContactEmail()", html);
        Assert.Contains("return normalizeContactEmail(athlete && athlete.AccountEmail)", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("|| readStoredContactEmail();", html);
        Assert.Contains("accountEmail: readResultUploadContactEmail()", submitBody);
        Assert.Contains("chronoPhenoDifference: getSessionItem('chronoPhenoDifference') || null", submitBody);
        Assert.Contains("chronoBortzDifference: getSessionItem('chronoBortzDifference') || null", submitBody);
        Assert.Contains("function readPendingPaymentOffer()", html);
        Assert.Contains("const rawOffer = getSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.Contains("const paymentOffer = JSON.parse(rawOffer);", html);
        Assert.Contains("if (paymentOffer && typeof paymentOffer === 'object' && !Array.isArray(paymentOffer))", html);
        Assert.Contains("function clearPendingPaymentOffer()", html);
        Assert.Contains("clearPendingPaymentOffer();", html);
        Assert.Contains("const paymentOffer = readPendingPaymentOffer();", submitBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", html);
        Assert.DoesNotContain("accountEmail: readStoredContactEmail()", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("localStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoPhenoDifference')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoBortzDifference')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem(PENDING_PAYMENT_OFFER_KEY)", submitBody);
        Assert.DoesNotContain("JSON.parse(getSessionItem(PENDING_PAYMENT_OFFER_KEY) || 'null')", submitBody);
    }

    [Fact]
    public async Task ResultUploadSubmit_IgnoresMalformedStoredContactEmail()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", html);
        Assert.Contains("function readStoredContactEmail()", html);
        Assert.Contains("const contactEmail = normalizeContactEmail(getSessionItem('contactEmail') || getLocalItem('contactEmail'));", html);
        Assert.Contains("emailInput.type = 'email';", html);
        Assert.Contains("return emailInput.checkValidity() ? contactEmail : null;", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("accountEmail: readResultUploadContactEmail()", html);
    }

    [Fact]
    public async Task ResultUploadNoAthleteGuard_ReturnsToAthleteSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");
        var guardStart = html.IndexOf("if (!athlete || !athlete.Name)", StringComparison.Ordinal);
        var guardEnd = html.IndexOf("let biomarkerData = null;", guardStart, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(guardEnd > guardStart);

        var guardBody = html[guardStart..guardEnd];

        Assert.Contains("customAlert('No athlete selected. Please return and choose your athlete.')", guardBody);
        Assert.Contains(".then(() => window.location.href = '/select-athlete');", guardBody);
    }

    [Fact]
    public async Task ResultUploadSuccessHandoff_UsesSafeStorageBeforeNavigation()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");
        var successStart = html.IndexOf("customAlert(nextStepMessage).then(() =>", StringComparison.Ordinal);
        var successEnd = html.IndexOf("});", html.IndexOf("window.location.href = '/review?from=proof-upload';", successStart, StringComparison.Ordinal), StringComparison.Ordinal);

        Assert.True(successStart >= 0);
        Assert.True(successEnd > successStart);

        var successBody = html[successStart..successEnd];

        Assert.Contains("function setBrowserStorageItem(storageName, key, value)", html);
        Assert.Contains("function removeBrowserStorageItem(storageName, key)", html);
        Assert.Contains("const reviewContactEmail = normalizeContactEmail(applicantData.accountEmail);", successBody);
        Assert.Contains("setSessionItem('contactEmail', reviewContactEmail);", successBody);
        Assert.Contains("setLocalItem('contactEmail', reviewContactEmail);", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", successBody);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_INVOICE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("setLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("accountEmail: reviewContactEmail || null", successBody);
        Assert.Contains("submissionType: 'result'", successBody);
        Assert.Contains("reviewSource: 'proof-upload'", successBody);
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_INVOICE_KEY);", successBody);
        Assert.Contains("removeLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY);", successBody);
        Assert.Contains("setSessionItem(\"came-from\", \"proof-upload\");", successBody);
        Assert.Contains("window.location.href = '/review?from=proof-upload';", successBody);
        Assert.DoesNotContain("sessionStorage.setItem(", successBody);
        Assert.DoesNotContain("localStorage.setItem(", successBody);
        Assert.DoesNotContain("sessionStorage.removeItem(", successBody);
        Assert.DoesNotContain("localStorage.removeItem(", successBody);
    }
}
