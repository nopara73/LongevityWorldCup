using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofUploadPageTests
{
    [Fact]
    public async Task ResultUpload_BackButtonReturnsToDashboardWithoutHistoryFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("type=\"button\" class=\"option-button back-button flow-action flow-action--secondary flow-action--icon-left\" onclick=\"window.navigateToFlowDestination('/dashboard')\"", html);
        Assert.DoesNotContain("onclick=\"window.goBackOrHome()\"", html);
    }

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
    public async Task ProofUploadPages_AcceptImagesAndPdfWithoutNarrowingPhonePhotoFormats(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync(path);

        Assert.Contains("id=\"proofPicInput\" accept=\"image/*,application/pdf,.heic,.heif,.pdf\"", html);
        Assert.Contains("id=\"proofCameraInput\" accept=\"image/*,.heic,.heif\"", html);
        Assert.DoesNotContain("id=\"proofPicInput\" accept=\"image/jpeg,image/png,image/webp,application/pdf", html);
        Assert.DoesNotContain("id=\"proofCameraInput\" accept=\"image/jpeg,image/png,image/webp", html);
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
        Assert.Contains("const maxProofImages = 30;", javascript);
        Assert.Contains("if (proofPics.length >= maxProofImages)", javascript);
        Assert.DoesNotContain("const loadingTask = pdfjsLib.getDocument({ data: arrayBuffer });", javascript);
    }

    [Fact]
    public async Task ProofHelper_ProcessesAllowedFilesUntilImageCap()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (files, input, retryButton)", StringComparison.Ordinal);
        var readerStart = javascript.IndexOf("const readDataURL = file =>", handlerStart, StringComparison.Ordinal);
        var imageCapStart = javascript.IndexOf("if (proofPics.length >= maxProofImages)", readerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(readerStart > handlerStart);
        Assert.True(imageCapStart > readerStart);

        var beforeReader = javascript[handlerStart..readerStart];

        Assert.DoesNotContain("proofPics.length + selectedFiles.length > maxProofImages", beforeReader);
        Assert.Contains("const maxProofImages = 30;", javascript);
        Assert.Contains("if (proofPics.length >= maxProofImages)", javascript);
        Assert.Contains("let hitImageLimit = false;", javascript);
        Assert.Contains("hitImageLimit = true;", javascript);
        Assert.Contains("const showProofUploadNotice = message =>", javascript);
        Assert.Contains("showProofUploadNotice('Only the first ' + maxProofImages + ' proof images were kept. Remove one to add another.');", javascript);
        Assert.DoesNotContain("customAlert('You can upload a maximum of 30 images.')", javascript);
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

        var catchBody = javascript[catchIndex..finallyIndex];
        Assert.Contains("customAlert('Proof upload failed. Please try again with an image or PDF file.')\n                .then(() => focusProofRetryButton(retryButton));", catchBody);
        Assert.DoesNotContain("error.message", catchBody);
    }

    [Fact]
    public async Task ProofHelper_DisablesProofControlsWhileProcessingSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (files, input, retryButton)", StringComparison.Ordinal);
        var finallyIndex = javascript.IndexOf("} finally {", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(finallyIndex > handlerStart);

        var handlerBody = javascript[handlerStart..finallyIndex];
        var finallyBody = javascript[finallyIndex..javascript.IndexOf("};", finallyIndex, StringComparison.Ordinal)];

        Assert.Contains("let isProofUploadProcessing = false;", javascript);
        Assert.Contains("if (isProofUploadProcessing) return;", javascript);
        Assert.Contains("if (isProofUploadProcessing)", handlerBody);
        Assert.Contains("if (input) input.value = \"\";", handlerBody);
        Assert.Contains("const focusProofRetryButton = retryButton =>", javascript);
        Assert.Contains("await handleProofFiles(event.target.files, proofPicInput, uploadProofButton);", javascript);
        Assert.Contains("await handleProofFiles(event.target.files, cameraInput, cameraButton || uploadProofButton);", javascript);
        Assert.Contains("isProofUploadProcessing = true;", handlerBody);
        Assert.Contains("uploadProofButton.disabled = true;", handlerBody);
        Assert.Contains("proofPicInput.disabled = true;", handlerBody);
        Assert.Contains("if (cameraButton) cameraButton.disabled = true;", handlerBody);
        Assert.Contains("if (cameraInput) cameraInput.disabled = true;", handlerBody);
        Assert.Contains("nextButton.disabled = true;", handlerBody);
        Assert.Contains("isProofUploadProcessing = false;", finallyBody);
        Assert.Contains("uploadProofButton.disabled = false;", finallyBody);
        Assert.Contains("proofPicInput.disabled = false;", finallyBody);
        Assert.Contains("if (cameraButton) cameraButton.disabled = false;", finallyBody);
        Assert.Contains("if (cameraInput) cameraInput.disabled = false;", finallyBody);
        Assert.Contains("checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", finallyBody);
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
    public async Task ProofHelper_RejectsAllUnsupportedFilesBeforeProcessing()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (files, input, retryButton)", StringComparison.Ordinal);
        var loadingStart = javascript.IndexOf("showLoading();", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(loadingStart > handlerStart);

        var beforeLoading = javascript[handlerStart..loadingStart];

        Assert.Contains("function isSupportedProofFile(file)", javascript);
        Assert.Contains("type === 'application/pdf'", javascript);
        Assert.Contains("type.startsWith('image/')", javascript);
        Assert.Contains("extension === 'jpg'", javascript);
        Assert.Contains("extension === 'jpeg'", javascript);
        Assert.Contains("extension === 'png'", javascript);
        Assert.Contains("extension === 'webp'", javascript);
        Assert.Contains("extension === 'heic'", javascript);
        Assert.Contains("extension === 'heif'", javascript);
        Assert.Contains("r.onabort = rej;", javascript);
        Assert.Contains("const selectedFiles = Array.from(files || []);", beforeLoading);
        Assert.Contains("const unsupportedFiles = selectedFiles.filter(file => !isSupportedProofFile(file));", beforeLoading);
        Assert.Contains("const supportedFiles = selectedFiles.filter(file => isSupportedProofFile(file));", beforeLoading);
        Assert.Contains("if (supportedFiles.length === 0)", beforeLoading);
        Assert.Contains("if (input) input.value = \"\";", beforeLoading);
        Assert.Contains("customAlert('Proof files must be images or PDFs.')\n                .then(() => focusProofRetryButton(retryButton));", beforeLoading);
        Assert.Contains("return;", beforeLoading);
        Assert.Contains("if (isProofPdfFile(file))", javascript);
        Assert.DoesNotContain("if (file.type === 'application/pdf')", javascript);
    }

    [Fact]
    public async Task ProofHelper_ProcessesSupportedFilesFromMixedSelection()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (files, input, retryButton)", StringComparison.Ordinal);
        var loopStart = javascript.IndexOf("for (const file of supportedFiles)", handlerStart, StringComparison.Ordinal);
        var catchStart = javascript.IndexOf("} catch (error)", loopStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(loopStart > handlerStart);
        Assert.True(catchStart > loopStart);

        var processingBody = javascript[loopStart..catchStart];

        Assert.Contains("const supportedFiles = selectedFiles.filter(file => isSupportedProofFile(file));", javascript);
        Assert.Contains("for (const file of supportedFiles)", processingBody);
        Assert.DoesNotContain("for (const file of selectedFiles)", processingBody);
        Assert.Contains("if (unsupportedFiles.length > 0)", processingBody);
        Assert.Contains("customAlert('Some proof files were skipped because proof files must be images or PDFs.')\n                    .then(() => focusProofRetryButton(retryButton));", processingBody);
    }

    [Fact]
    public async Task ProofHelper_ContinuesAfterIndividualProofFileProcessingFailure()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var loopStart = javascript.IndexOf("for (const file of supportedFiles)", StringComparison.Ordinal);
        var unsupportedAlertStart = javascript.IndexOf("if (unsupportedFiles.length > 0)", loopStart, StringComparison.Ordinal);

        Assert.True(loopStart >= 0);
        Assert.True(unsupportedAlertStart > loopStart);

        var processingBody = javascript[loopStart..unsupportedAlertStart];

        Assert.Contains("let failedFiles = 0;", javascript);
        Assert.Contains("const proofCountBeforeFile = proofPics.length;", processingBody);
        Assert.Contains("try {", processingBody);
        Assert.Contains("if (!context) throw new Error('Canvas context unavailable.');", processingBody);
        Assert.Contains("failedFiles++;", processingBody);
        Assert.Contains("if (proofPics.length > proofCountBeforeFile)", processingBody);
        Assert.Contains("updateProofImageContainer(proofImageContainer, nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", processingBody);
        Assert.Contains("checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", processingBody);
        Assert.Contains("nextButton.disabled = true;", processingBody);
        Assert.Contains("if (failedFiles > 0)", javascript);
        Assert.Contains("customAlert('Some proof files could not be processed. Please try them again as images or PDFs.')\n                    .then(() => focusProofRetryButton(retryButton));", javascript);
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
    public async Task ProofHelper_KeepsChecklistAdvisoryWhenBiomarkersAreListed()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("const hasProofs = proofPics.length > 0;", javascript);
        Assert.Contains("nextButton.disabled = !hasProofs;", javascript);
        Assert.Contains("var PROOF_CONTEXT_CHECKLIST_LABELS = ['Collection date', 'Lab/report source'];", javascript);
        Assert.Contains("return labels.length > 0 ? PROOF_CONTEXT_CHECKLIST_LABELS.concat(labels) : labels;", javascript);
        Assert.Contains("instructions.textContent = \"Check each item only when an uploaded proof shows its marker name and submitted value:\";", javascript);
        Assert.Contains("input.addEventListener('change', function ()", javascript);
        Assert.Contains("checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", javascript);
        Assert.DoesNotContain("areRequiredProofChecklistItemsChecked", javascript);
        Assert.DoesNotContain("nextButton.disabled = !(hasProofs && checklistComplete);", javascript);
    }

    [Fact]
    public async Task ApplicationProofUploadButtons_TolerateMissingProofHelper()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/onboarding/convergence.html");
        var helperCall = html.IndexOf("window.updateProofUploadButtons(nextButton, uploadProofButton, takeProofPhotoButton);", StringComparison.Ordinal);

        Assert.True(helperCall >= 0);
        var guardStart = html.LastIndexOf("if (typeof window.updateProofUploadButtons === 'function')", helperCall, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(guardStart < helperCall);
    }

    [Fact]
    public async Task ProofHelper_UsesSafeStorageForChecklistBiomarkerHandoff()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");
        var helperStart = javascript.IndexOf("function getProofSessionItem(key)", StringComparison.Ordinal);
        var helperEnd = javascript.IndexOf("window.getProofChecklistLabelsFromSession = function ()", helperStart, StringComparison.Ordinal);
        var checklistStart = helperEnd;
        var checklistEnd = javascript.IndexOf("function getProofFileExtension(file)", checklistStart, StringComparison.Ordinal);

        Assert.True(helperStart >= 0);
        Assert.True(helperEnd > helperStart);
        Assert.True(checklistEnd > checklistStart);

        var helperBody = javascript[helperStart..helperEnd];
        var checklistBody = javascript[checklistStart..checklistEnd];

        Assert.Contains("try {", helperBody);
        Assert.Contains("return window.sessionStorage.getItem(key);", helperBody);
        Assert.Contains("} catch (_) {", helperBody);
        Assert.Contains("return null;", helperBody);
        Assert.Contains("var raw = getProofSessionItem('biomarkerData');", checklistBody);
        Assert.DoesNotContain("sessionStorage.getItem('biomarkerData')", javascript);
    }

    [Fact]
    public async Task ProofHelper_IgnoresBlankStoredBiomarkerValuesInChecklist()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var javascript = await client.GetStringAsync("/js/proof-helpers.js");

        Assert.Contains("function hasFiniteBiomarkerValue(value)", javascript);
        Assert.Contains("if (value === null || value === undefined || typeof value === 'boolean') return false;", javascript);
        Assert.Contains("if (typeof value === 'number') return Number.isFinite(value);", javascript);
        Assert.Contains("var trimmed = value.trim();", javascript);
        Assert.Contains("return trimmed !== '' && Number.isFinite(Number(trimmed));", javascript);
        Assert.Contains("if (hasFiniteBiomarkerValue(val)) return true;", javascript);
        Assert.Contains("if (hasFiniteBiomarkerValue(val))", javascript);
        Assert.DoesNotContain("val !== undefined && val !== null && !isNaN(val)", javascript);
    }

    [Fact]
    public async Task ResultUploadFailures_UseReadableErrorExtractor()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("const fallbackError = Number.isFinite(response.status) ? `HTTP ${response.status}` : 'Request failed';", html);
        Assert.Contains("window.readApplicationErrorMessage(response).catch(() => fallbackError).then(badResponse =>", html);
        Assert.Contains("function isSubmissionAcceptedPaymentFailure(message)", html);
        Assert.Contains("/^Application sent, but failed to create BTCPay invoice:/i.test(message.trim())", html);
        Assert.Contains("if (isSubmissionAcceptedPaymentFailure(badResponse))", html);
        Assert.Contains("'payment-unavailable'", html);
        Assert.Contains("Your results were received, but the payment page could not be created. We will follow up by email.", html);
        Assert.DoesNotContain("response.text().then(badResponse =>", html);
    }

    [Fact]
    public async Task ResultUploadNetworkFailures_ShowNormalizedErrorMessage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("const displayError = error && error.message ? error.message : String(error);", html);
        Assert.Contains("const alertMessage = 'Results could not be submitted. Please check your connection and try again.';", html);
        Assert.Contains("message: displayError", html);
        Assert.Contains("customAlert(alertMessage)", html);
        Assert.DoesNotContain("customAlert(`An error occurred:\\n\\n${displayError}`)", html);
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
        Assert.Contains("customAlert(alertMessage).then(() => {\n                            isResultUploadSubmitting = false;", html);
        Assert.Contains("setSubmitButtonLabel();\n                                    submitButton.focus();", html);
        Assert.Contains("setSubmitButtonLabel();\n                            submitButton.focus();", html);
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
        Assert.Contains("function hasCompleteSubmittedBiomarkerValues(biomarkerData, chronoPhenoDifference, chronoBortzDifference)", html);
        Assert.Contains("!hasCompleteSubmittedBiomarkerValues(biomarkerData, chronoPhenoDifference, chronoBortzDifference)", parseBody);
        Assert.Contains("function hasStoredBiomarkerValue(value)", html);
        Assert.Contains("Object.keys(entry).some(key => key !== 'Date' && hasStoredBiomarkerValue(entry[key]))", html);
        Assert.Contains("const PHENO_RESULT_BIOMARKER_KEYS = ['AlbGL', 'CreatUmolL', 'GluMmolL', 'CrpMgL', 'Wbc1000cellsuL', 'LymPc', 'McvFL', 'RdwPc', 'AlpUL'];", html);
        Assert.Contains("'MonocytePc', 'NeutrophilPc'", html);
        Assert.Contains("requiredKeys.every(key => hasStoredBiomarkerValue(entry[key]))", html);
        Assert.Contains("if (value === null || value === undefined) return false;", html);
        Assert.Contains("if (typeof value === 'boolean') return false;", html);
        Assert.Contains("if (typeof value !== 'number' && typeof value !== 'string') return false;", html);
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
        var submitStart = html.IndexOf("const paymentOffer = readAdjustedPendingPaymentOffer();", StringComparison.Ordinal);
        var submitEnd = html.IndexOf("fetchWithTimeout('/api/application/application'", submitStart, StringComparison.Ordinal);

        Assert.True(submitStart >= 0);
        Assert.True(submitEnd > submitStart);

        var submitBody = html[submitStart..submitEnd];

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("return null;", html);
        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("if (typeof value !== 'string') return null;", html);
        Assert.Contains("const bracketedEmail = /<([^<>]+)>/.exec(contactEmail);", html);
        Assert.Contains("function readResultUploadContactEmail()", html);
        Assert.Contains("return normalizeContactEmail(athlete && athlete.AccountEmail)", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("|| readStoredContactEmail();", html);
        Assert.Contains("accountEmail: readResultUploadContactEmail()", submitBody);
        Assert.Contains("const chronoPhenoDifference = readStoredAgeDifference('chronoPhenoDifference');", html);
        Assert.Contains("const chronoBortzDifference = readStoredAgeDifference('chronoBortzDifference');", html);
        Assert.Contains("chronoPhenoDifference: chronoPhenoDifference", submitBody);
        Assert.Contains("chronoBortzDifference: chronoBortzDifference", submitBody);
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
        Assert.Contains("const retryButton = document.getElementById('submitButton') || document.getElementById('nextButton');", html);
        Assert.Contains("customAlert('Payment details could not be prepared. Please try again.')\n                .then(() => retryButton?.focus());", html);
        Assert.Contains("return undefined;", html);
        Assert.Contains("const paymentOffer = readAdjustedPendingPaymentOffer();", submitBody);
        Assert.Contains("if (paymentOffer === undefined) return;", submitBody);
        Assert.Contains("paymentOffer: paymentOffer", submitBody);
        Assert.DoesNotContain("window.applyPaymentAdjustmentsToPaymentOffer(paymentOffer)", submitBody);
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
        Assert.Contains("if (typeof value !== 'string') return null;", html);
        Assert.Contains("const bracketedEmail = /<([^<>]+)>/.exec(contactEmail);", html);
        Assert.Contains("contactEmail.replace(/^mailto:/i, '').split('?')[0].trim();", html);
        Assert.Contains("function readStoredContactEmail()", html);
        Assert.Contains("const sessionContactEmail = normalizeContactEmail(getSessionItem('contactEmail'));", html);
        Assert.Contains("if (sessionContactEmail) return sessionContactEmail;", html);
        Assert.Contains("const localContactEmail = normalizeContactEmail(getLocalItem('contactEmail'));", html);
        Assert.Contains("if (localContactEmail)", html);
        Assert.Contains("return localContactEmail;", html);
        Assert.Contains("function readStoredAthleteContactEmail(athleteName)", html);
        Assert.Contains("return key ? normalizeContactEmail(getLocalItem(key)) : null;", html);
        Assert.Contains("emailInput.type = 'email';", html);
        Assert.Contains("return emailInput.checkValidity() ? contactEmail : null;", html);
        Assert.Contains("|| normalizeContactEmail(athlete && athlete.MediaContact)", html);
        Assert.Contains("|| readStoredAthleteContactEmail(athlete && athlete.Name)", html);
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
        var guardStart = html.IndexOf("if (!isValidSelectedAthlete(athlete))", StringComparison.Ordinal);
        var guardEnd = html.IndexOf("let biomarkerData = null;", guardStart, StringComparison.Ordinal);

        Assert.True(guardStart >= 0);
        Assert.True(guardEnd > guardStart);

        var guardBody = html[guardStart..guardEnd];

        Assert.Contains("function isValidSelectedAthlete(value)", html);
        Assert.Contains("customAlert('No athlete selected. Please return and choose your athlete.')", guardBody);
        Assert.Contains(".then(() => window.location.href = '/select-athlete');", guardBody);
    }

    [Fact]
    public async Task ResultUploadDisplayName_FallsBackWhenStoredDisplayNameIsNotText()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("typeof athlete?.DisplayName === 'string'", html);
        Assert.Contains("return typeof athlete?.Name === 'string' ? athlete.Name : '';", html);
        Assert.DoesNotContain("athlete?.DisplayName && athlete.DisplayName.trim()", html);
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
        Assert.Contains("function normalizeCheckoutLink(value)", html);
        Assert.Contains("const checkoutUrl = new URL(trimmed, window.location.origin);", html);
        Assert.Contains("return checkoutUrl.protocol === 'http:' || checkoutUrl.protocol === 'https:'", html);
        Assert.Contains("? normalizeCheckoutLink(submitResult.checkoutLink)", html);
        Assert.Contains("function isPaymentRequired(submitResult)", html);
        Assert.Contains("return !!(submitResult && submitResult.paymentRequired);", html);
        Assert.Contains("function isPaymentUnavailable(submitResult)", html);
        Assert.Contains("return !!(submitResult && submitResult.paymentUnavailable);", html);
        Assert.Contains("function getCheckoutLink(submitResult)", html);
        Assert.Contains("function getInvoiceId(submitResult)", html);
        Assert.Contains("typeof submitResult.invoiceId === 'string'", html);
        Assert.Contains("const checkoutLink = getCheckoutLink(submitResult);", html);
        Assert.Contains("const invoiceId = getInvoiceId(submitResult);", html);
        Assert.Contains("const paymentRequired = isPaymentRequired(submitResult);", html);
        Assert.Contains("const paymentUnavailable = isPaymentUnavailable(submitResult);", html);
        Assert.Contains("? 'Your results were received, but the payment page could not be created. We will follow up by email.'", html);
        Assert.Contains("? 'Your results were received, but the payment page could not be opened. Check your confirmation email.'", html);
        Assert.Contains("if (checkoutLink)", successBody);
        Assert.Contains("if (invoiceId)", successBody);
        Assert.Contains("invoiceId: invoiceId", successBody);
        Assert.Contains("window.location.href = checkoutLink;", successBody);
        Assert.Contains("const reviewContactEmail = normalizeContactEmail(applicantData.accountEmail);", successBody);
        Assert.Contains("rememberAthleteContactEmail(athlete && athlete.Name, reviewContactEmail);", successBody);
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
        Assert.DoesNotContain("window.location.href = submitResult.checkoutLink;", successBody);
        Assert.DoesNotContain("if (submitResult.invoiceId)", successBody);
        Assert.DoesNotContain("invoiceId: submitResult.invoiceId", successBody);
        Assert.DoesNotContain("sessionStorage.setItem(", successBody);
        Assert.DoesNotContain("localStorage.setItem(", successBody);
        Assert.DoesNotContain("sessionStorage.removeItem(", successBody);
        Assert.DoesNotContain("localStorage.removeItem(", successBody);
    }
}
