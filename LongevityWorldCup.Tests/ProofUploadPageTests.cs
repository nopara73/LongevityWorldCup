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
}
