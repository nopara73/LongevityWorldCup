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
        var resetIndex = javascript.IndexOf("if (input) input.value = \"\";", StringComparison.Ordinal);
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
        Assert.Contains("customAlert('Biomarker data is missing. Please fill out the biomarker form first.');", parseBody);
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
        Assert.Contains("return normalizeContactEmail(athlete && athlete.AccountEmail) || readStoredContactEmail();", html);
        Assert.Contains("accountEmail: readResultUploadContactEmail()", submitBody);
        Assert.Contains("chronoPhenoDifference: getSessionItem('chronoPhenoDifference') || null", submitBody);
        Assert.Contains("chronoBortzDifference: getSessionItem('chronoBortzDifference') || null", submitBody);
        Assert.Contains("JSON.parse(getSessionItem(PENDING_PAYMENT_OFFER_KEY) || 'null')", submitBody);
        Assert.DoesNotContain("accountEmail: readStoredContactEmail()", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("localStorage.getItem('contactEmail')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoPhenoDifference')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem('chronoBortzDifference')", submitBody);
        Assert.DoesNotContain("sessionStorage.getItem(PENDING_PAYMENT_OFFER_KEY)", submitBody);
    }

    [Fact]
    public async Task ResultUploadSubmit_IgnoresMalformedStoredContactEmail()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/proof-upload.html");

        Assert.Contains("function normalizeContactEmail(value)", html);
        Assert.Contains("function readStoredContactEmail()", html);
        Assert.Contains("const contactEmail = normalizeContactEmail(getSessionItem('contactEmail') || getLocalItem('contactEmail'));", html);
        Assert.Contains("emailInput.type = 'email';", html);
        Assert.Contains("return emailInput.checkValidity() ? contactEmail : null;", html);
        Assert.Contains("removeSessionItem('contactEmail');", html);
        Assert.Contains("removeLocalItem('contactEmail');", html);
        Assert.Contains("accountEmail: readResultUploadContactEmail()", html);
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
        Assert.Contains("removeSessionItem(PENDING_PAYMENT_OFFER_KEY);", successBody);
        Assert.Contains("setSessionItem(PENDING_PAYMENT_INVOICE_KEY, pendingInvoiceInfo);", successBody);
        Assert.Contains("setLocalItem(PENDING_PAYMENT_INVOICE_STORAGE_KEY, pendingInvoiceInfo);", successBody);
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
