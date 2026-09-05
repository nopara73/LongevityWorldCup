using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class ProofUploadPageTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task ProofHelper_ProcessesAllowedFilesUntilImageCap()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await GetProofHelpersTypeScriptAsync(client);
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (", StringComparison.Ordinal);
        var readerStart = javascript.IndexOf("const readDataURL: (file: Blob) => Promise<string>", handlerStart, StringComparison.Ordinal);
        var imageCapStart = javascript.IndexOf("if (proofPics.length >= maxProofImages)", readerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(readerStart > handlerStart);
        Assert.True(imageCapStart > readerStart);

        var beforeReader = javascript[handlerStart..readerStart];

        Assert.DoesNotContain("proofPics.length + selectedFiles.length > maxProofImages", beforeReader);
        Assert.Contains("const maxProofImages = 37;", javascript);
        Assert.Contains("if (proofPics.length >= maxProofImages)", javascript);
        Assert.Contains("let hitImageLimit = false;", javascript);
        Assert.Contains("hitImageLimit = true;", javascript);
        Assert.Contains("const showProofUploadNotice: (message: string) => void = message =>", javascript);
        Assert.Contains("uploadNotices.push('Only the first ' + maxProofImages + ' proof images were kept. Remove one to add another.');", javascript);
        Assert.Contains("showProofUploadNotice(uploadNotices.join(' '));", javascript);
        Assert.DoesNotContain("customAlert('You can upload a maximum of 37 images.')", javascript);
    }

    [Fact]
    public async Task ProofHelper_SkipsDuplicateEncodedProofs()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await GetProofHelpersTypeScriptAsync(client);

        Assert.Contains("const knownProofImages = new Set(proofPics);", javascript);
        Assert.Contains("const proofSourceImages = new Map<string, string>();", javascript);
        Assert.Contains("const fingerprintProofSource = async (bytes: ArrayBuffer): Promise<string> =>", javascript);
        Assert.Contains("const sourceKey = `${sourceFingerprint}:page:${pageNum}`;", javascript);
        Assert.Contains("if (isKnownLiveProofSource(sourceKey))", javascript);
        Assert.Contains("const addProofImage = (dataUrl: string): boolean =>", javascript);
        Assert.Contains("if (knownProofImages.has(dataUrl))", javascript);
        Assert.Contains("duplicateProofs++;", javascript);
        Assert.Contains("addProofImage(optimizedPage);", javascript);
        Assert.Contains("if (addProofImage(dataUrl))", javascript);
        Assert.Contains("uploadNotices.push('Duplicate proof images were skipped.');", javascript);
    }

    [Fact]
    public async Task ProofHelper_RejectsAllUnsupportedFilesBeforeProcessing()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await GetProofHelpersTypeScriptAsync(client);
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (", StringComparison.Ordinal);
        var loadingStart = javascript.IndexOf("review.setProgress(", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(loadingStart > handlerStart);

        var beforeLoading = javascript[handlerStart..loadingStart];

        Assert.Contains("function isSupportedProofFile(file: File | null | undefined): file is File", javascript);
        Assert.Contains("type === 'application/pdf'", javascript);
        Assert.Contains("type.startsWith('image/')", javascript);
        Assert.Contains("extension === 'jpg'", javascript);
        Assert.Contains("extension === 'jpeg'", javascript);
        Assert.Contains("extension === 'png'", javascript);
        Assert.Contains("extension === 'webp'", javascript);
        Assert.Contains("extension === 'heic'", javascript);
        Assert.Contains("extension === 'heif'", javascript);
        Assert.Contains("r.onabort = () => rej(new Error('Proof file read was aborted.'));", javascript);
        Assert.Contains("const selectedFiles = Array.from(files || []);", beforeLoading);
        Assert.Contains("const unsupportedFiles = selectedFiles.filter(file => !isSupportedProofFile(file));", beforeLoading);
        Assert.Contains("const supportedFiles = selectedFiles.filter(file => isSupportedProofFile(file));", beforeLoading);
        Assert.Contains("if (supportedFiles.length === 0)", beforeLoading);
        Assert.Contains("if (input) input.value = \"\";", beforeLoading);
        Assert.Contains("window.customAlert('Proof files must be images or PDFs.')", beforeLoading);
        Assert.Contains(".then(() => focusProofRetryButton(retryButton));", beforeLoading);
        Assert.Contains("return;", beforeLoading);
        Assert.Contains("if (isProofPdfFile(file))", javascript);
        Assert.DoesNotContain("if (file.type === 'application/pdf')", javascript);
    }

    [Fact]
    public async Task ProofHelper_ProcessesSupportedFilesFromMixedSelection()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await GetProofHelpersTypeScriptAsync(client);
        var handlerStart = javascript.IndexOf("const handleProofFiles = async function (", StringComparison.Ordinal);
        var loopStart = javascript.IndexOf("for (const file of supportedFiles)", handlerStart, StringComparison.Ordinal);
        var catchStart = javascript.IndexOf("} catch (error) {", loopStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(loopStart > handlerStart);
        Assert.True(catchStart > loopStart);

        var processingBody = javascript[loopStart..catchStart];

        Assert.Contains("const supportedFiles = selectedFiles.filter(file => isSupportedProofFile(file));", javascript);
        Assert.Contains("for (const file of supportedFiles)", processingBody);
        Assert.DoesNotContain("for (const file of selectedFiles)", processingBody);
        Assert.Contains("if (unsupportedFiles.length > 0)", processingBody);
        Assert.Contains("window.customAlert('Some proof files were skipped because proof files must be images or PDFs.')", processingBody);
        Assert.Contains(".then(() => focusProofRetryButton(retryButton));", processingBody);
    }

    [Fact]
    public async Task ProofHelper_ContinuesAfterIndividualProofFileProcessingFailure()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var javascript = await GetProofHelpersTypeScriptAsync(client);
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
        Assert.Contains("review.render();", processingBody);
        Assert.Contains("checkProofImages(nextButton, proofPics, uploadProofButton, cameraButton, biomarkerChecklistContainer);", processingBody);
        Assert.Contains("proofProcessingButtons.has(nextButton)", javascript);
        Assert.Contains("if (failedFiles > 0)", javascript);
        Assert.Contains("window.customAlert('Some proof files could not be processed. Please try them again as images or PDFs.')", javascript);
        Assert.Contains(".then(() => focusProofRetryButton(retryButton));", javascript);
    }

    private static Task<string> GetProofHelpersTypeScriptAsync(HttpClient client) =>
        FrontendSourceTestHelper.GetFrontendTypeScriptAsync(client, "proof-helpers.ts");
}
