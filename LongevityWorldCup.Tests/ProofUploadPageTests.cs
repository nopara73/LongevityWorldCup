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
}
