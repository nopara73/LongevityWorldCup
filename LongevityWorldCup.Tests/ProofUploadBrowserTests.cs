using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.Integration)]
public sealed class ProofUploadBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task ResultUpload_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var proofHelperRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProofHelper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RoutePageDependenciesAsync(
            context,
            delayProofHelper: true,
            proofHelperRequestStarted,
            releaseProofHelper.Task);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Browser Test Athlete',
                DisplayName: 'Browser Test Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        var navigation = page.GotoAsync(
            "/play/proof-upload.html",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await proofHelperRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseProofHelper.SetResult();
        await navigation;
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        Assert.Contains("Browser Test Athlete", await page.Locator("#character-title").InnerTextAsync());
        Assert.Contains("Upload", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("proofs", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.True(await page.Locator("#submitButton").IsDisabledAsync());
        Assert.Empty(errors);
    }

    [Fact]
    public async Task OnboardingProofStage_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var proofHelperRequestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProofHelper = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await RoutePageDependenciesAsync(
            context,
            delayProofHelper: true,
            proofHelperRequestStarted,
            releaseProofHelper.Task);

        await context.AddInitScriptAsync(
            """
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                DateOfBirth: { Year: 1980, Month: 5, Day: 20 },
                Biomarkers: [
                    { Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        var navigation = page.GotoAsync(
            "/onboarding/convergence.html?fake=1",
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await proofHelperRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        releaseProofHelper.SetResult();
        await navigation;

        await AdvanceOnboardingStageAsync(page, "2. Finding your why");
        await AdvanceOnboardingStageAsync(page, "3. The price of glory");
        await AdvanceOnboardingStageAsync(page, "4/a. Almost there");
        await AdvanceOnboardingStageAsync(page, "4/b. Don't trust, verify");

        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        Assert.Contains("Upload", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("proofs", await page.Locator("#mainProofInstructions").InnerHTMLAsync());
        Assert.Contains("Albumin", await page.Locator("#biomarker-checklist").InnerTextAsync());
        Assert.Contains("Glucose", await page.Locator("#biomarker-checklist").InnerTextAsync());
        Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
        Assert.True(await page.Locator("#onboardingProofSymbol").IsVisibleAsync());
        Assert.False(await page.Locator("#illustrationPicture").IsVisibleAsync());

        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "5. Final details" }).WaitForAsync();
        await page.Locator("#backButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "4/b. Don't trust, verify" }).WaitForAsync();
        Assert.Equal("stage active", await page.Locator("#subStage4").GetAttributeAsync("class"));
        Assert.Equal("stage", await page.Locator("#subStage5").GetAttributeAsync("class"));
        Assert.True(await page.Locator("#onboardingProofSymbol").IsVisibleAsync());
        Assert.False(await page.Locator("#illustrationPicture").IsVisibleAsync());

        var uploadButton = page.Locator("#uploadProofButton");
        var cameraButton = page.Locator("#takeProofPhotoButton");
        await AssertSecondaryProofActionAsync(uploadButton);
        await AssertSecondaryProofActionAsync(cameraButton);

        await page.EvaluateAsync(
            """
            () => {
                const nextButton = document.getElementById('nextButton');
                const uploadButton = document.getElementById('uploadProofButton');
                const cameraButton = document.getElementById('takeProofPhotoButton');
                nextButton.disabled = true;
                window.updateProofUploadButtons(nextButton, uploadButton, cameraButton);
            }
            """);

        await AssertPrimaryProofActionAsync(uploadButton);
        await AssertSecondaryProofActionAsync(cameraButton);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ResultUpload_SubmitsPdfProofFromReportedFlow()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var payloadTask = await RoutePageDependenciesAndCaptureApplicationPostAsync(context);

        await context.AddInitScriptAsync(
            """
            const originalCanvasToBlob = HTMLCanvasElement.prototype.toBlob;
            window.__proofCanvasEncodeRequests = { webp: 0, jpeg: 0 };
            HTMLCanvasElement.prototype.toBlob = function(callback, type, quality) {
                if (type === 'image/webp') window.__proofCanvasEncodeRequests.webp++;
                if (type === 'image/jpeg') window.__proofCanvasEncodeRequests.jpeg++;
                const encodedType = type === 'image/webp' ? 'image/png' : type;
                return originalCanvasToBlob.call(this, callback, encodedType, quality);
            };
            window.pdfjsLib = {
                GlobalWorkerOptions: {},
                getDocument() {
                    return {
                        promise: Promise.resolve({
                            numPages: 3,
                            getPage: async pageNumber => ({
                                getViewport: () => ({ width: 12, height: 12 }),
                                render: ({ canvasContext }) => {
                                    canvasContext.fillStyle = ['#ffffff', '#dddddd', '#bbbbbb'][pageNumber - 1];
                                    canvasContext.fillRect(0, 0, 12, 12);
                                    return { promise: Promise.resolve() };
                                }
                            })
                        })
                    };
                }
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Majoros Gabor',
                DisplayName: 'Majoros Gabor',
                AccountEmail: 'gabor@example.test',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [
                    {
                        Date: '2026-06-19',
                        Wbc1000cellsuL: 6.1,
                        LymPc: 31,
                        McvFL: 89,
                        RdwPc: 12.5,
                        AlbGL: 45,
                        AlpUL: 72,
                        CreatUmolL: 82,
                        GluMmolL: 5.1,
                        CrpMgL: 1.2
                    }
                ]
            }));
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);

        await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        var pdfProof = new FilePayload
        {
            Name = "lab-results.pdf",
            MimeType = "application/pdf",
            Buffer = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF")
        };
        await page.Locator("#proofPicInput").SetInputFilesAsync(pdfProof);
        await page.WaitForFunctionAsync(
            "() => !document.getElementById('submitButton')?.disabled && document.querySelectorAll('#proofImageContainer img').length === 3");
        await page.Locator("#proofPicInput").SetInputFilesAsync(pdfProof);
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#proofImageContainer .proof-upload-notice')?.textContent?.includes('Duplicate proof images were skipped.')");
        Assert.Equal(3, await page.Locator("#proofImageContainer img").CountAsync());
        var encodeRequests = await page.EvaluateAsync<JsonElement>("() => window.__proofCanvasEncodeRequests");
        Assert.Equal(1, encodeRequests.GetProperty("webp").GetInt32());
        Assert.Equal(3, encodeRequests.GetProperty("jpeg").GetInt32());
        await page.EvaluateAsync(
            """
            () => document.querySelectorAll('.biomarker-checkbox')
                .forEach(box => {
                    box.checked = true;
                    box.dispatchEvent(new Event('change', { bubbles: true }));
                })
            """);

        await page.Locator("#submitButton").ClickAsync();

        JsonElement payload;
        try
        {
            payload = await payloadTask.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException exception)
        {
            var diagnostics = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify({
                    href: window.location.href,
                    submitDisabled: document.getElementById('submitButton')?.disabled ?? null,
                    submitText: document.getElementById('submitButton')?.textContent ?? null,
                    proofImages: document.querySelectorAll('#proofImageContainer img').length,
                    alertHidden: document.getElementById('custom-alert')?.hidden ?? null,
                    alertMessage: document.getElementById('custom-alert-message')?.textContent ?? null,
                    loadingHidden: document.getElementById('loading-dialog')?.hidden ?? null,
                    hasFetchWithTimeout: typeof window.fetchWithTimeout,
                    hasCreateApplicationSubmissionId: typeof window.createApplicationSubmissionId,
                    hasTrySendApplicationSubmissionReport: typeof window.trySendApplicationSubmissionReport,
                    hasReadApplicationErrorMessage: typeof window.readApplicationErrorMessage,
                    biomarkerData: window.sessionStorage.getItem('biomarkerData')
                })
                """);
            throw new TimeoutException($"Application POST was not sent. Diagnostics: {diagnostics}. Console/page errors: {string.Join(" | ", errors)}", exception);
        }

        Assert.Equal("Majoros Gabor", payload.GetProperty("name").GetString());
        Assert.Equal("gabor@example.test", payload.GetProperty("accountEmail").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("paymentOffer").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(payload.GetProperty("submissionId").GetString()));
        var proofs = payload.GetProperty("proofPics").EnumerateArray().ToList();
        Assert.Equal(3, proofs.Count);
        Assert.All(proofs, proof =>
        {
            Assert.StartsWith("data:image/jpeg;base64,", proof.GetString());
            Assert.Contains(";base64,", proof.GetString());
        });
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ResultUpload_BoundsLargeNoisyPdfCanvasBeforeKeepingIt()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: false);

        await context.AddInitScriptAsync(
            """
            const nativeCanvasToBlob = HTMLCanvasElement.prototype.toBlob;
            window.__proofCanvasEncodes = [];
            HTMLCanvasElement.prototype.toBlob = function(callback, contentType, quality) {
                const width = this.width;
                const height = this.height;
                return nativeCanvasToBlob.call(this, blob => {
                    if (width > 1 && height > 1) {
                        window.__proofCanvasEncodes.push({
                            width,
                            height,
                            contentType,
                            quality,
                            size: blob?.size || 0
                        });
                    }
                    callback(blob);
                }, contentType, quality);
            };
            window.pdfjsLib = {
                GlobalWorkerOptions: {},
                getDocument() {
                    return {
                        promise: Promise.resolve({
                            numPages: 1,
                            getPage: async () => ({
                                getViewport: () => ({ width: 2600, height: 1600 }),
                                render: ({ canvasContext }) => {
                                    const width = canvasContext.canvas.width;
                                    const height = canvasContext.canvas.height;
                                    const noiseCanvas = document.createElement('canvas');
                                    noiseCanvas.width = Math.ceil(width / 4);
                                    noiseCanvas.height = Math.ceil(height / 4);
                                    const noiseContext = noiseCanvas.getContext('2d');
                                    const pixels = noiseContext.createImageData(noiseCanvas.width, noiseCanvas.height);
                                    // Deterministic block noise keeps this a real, difficult encode
                                    // without making the regression depend on random compressed size.
                                    let state = 0x12345678;
                                    for (let index = 0; index < pixels.data.length; index += 4) {
                                        state ^= state << 13;
                                        state ^= state >>> 17;
                                        state ^= state << 5;
                                        state >>>= 0;
                                        pixels.data[index] = state & 255;
                                        pixels.data[index + 1] = (state >>> 8) & 255;
                                        pixels.data[index + 2] = (state >>> 16) & 255;
                                        pixels.data[index + 3] = 255;
                                    }
                                    noiseContext.putImageData(pixels, 0, 0);
                                    canvasContext.imageSmoothingEnabled = false;
                                    canvasContext.drawImage(noiseCanvas, 0, 0, width, height);
                                    return { promise: Promise.resolve() };
                                }
                            })
                        })
                    };
                }
            };
            window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
                Name: 'Canvas Bounds Athlete',
                DisplayName: 'Canvas Bounds Athlete',
                Biomarkers: []
            }));
            window.sessionStorage.setItem('biomarkerData', JSON.stringify({
                Biomarkers: [{ Date: '2026-06-19', AlbGL: 45 }]
            }));
            """);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/proofs", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true'");

        await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload
        {
            Name = "large-lab-results.pdf",
            MimeType = "application/pdf",
            Buffer = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF")
        });
        await page.WaitForFunctionAsync(
            "() => document.querySelectorAll('#proofImageContainer img').length === 1",
            null,
            new PageWaitForFunctionOptions { Timeout = 60_000 });

        var dataUrl = await page.Locator("#proofImageContainer img").GetAttributeAsync("src");
        Assert.NotNull(dataUrl);
        var separator = dataUrl!.IndexOf(',');
        Assert.True(separator > 0);
        var bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]);
        Assert.True(bytes.Length <= (int)(1.5 * 1024 * 1024), $"Encoded proof was {bytes.Length} bytes.");

        var encodeDiagnostics = await page.EvaluateAsync<JsonElement>("() => window.__proofCanvasEncodes");
        var encodes = encodeDiagnostics.EnumerateArray().ToArray();
        Assert.NotEmpty(encodes);
        Assert.Equal(2560, encodes[0].GetProperty("width").GetInt32());
        Assert.True(
            encodes[0].GetProperty("size").GetInt32() > (int)(1.5 * 1024 * 1024),
            "The noisy PDF witness must exceed the byte cap before the optimizer accepts it.");
        Assert.Equal(bytes.Length, encodes[^1].GetProperty("size").GetInt32());

        using var stream = new MemoryStream(bytes);
        var imageInfo = SixLabors.ImageSharp.Image.Identify(stream);
        Assert.NotNull(imageInfo);
        Assert.True(Math.Max(imageInfo!.Width, imageInfo.Height) <= 2560);
    }

    [Fact]
    public async Task ApplicationSubmissionId_ReusesExactPayloadAndRotatesAfterAnEdit()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: false);

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.createApplicationSubmissionId === 'function'");

        var result = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const largeProof = 'data:image/jpeg;base64,' + 'A'.repeat(2 * 1024 * 1024);
                const applicantData = { proofPics: [largeProof] };
                const firstPayload = window.createApplicationSubmissionPayloadKey(applicantData);
                const editedPayload = window.createApplicationSubmissionPayloadKey({ proofPics: [largeProof, 'proof-b'] });
                const firstId = window.createApplicationSubmissionId(firstPayload);
                window.rememberPendingApplicationSubmission({
                    submissionId: firstId,
                    payloadFingerprint: firstPayload,
                    submissionKind: 'result-upload',
                    applicantName: 'Reload Test',
                    accountEmail: 'reload@example.test'
                });
                const report = window.buildApplicationSubmissionReport(
                    applicantData,
                    'submission-test',
                    'started',
                    'result-upload',
                    null);
                return {
                    ids: [
                        firstId,
                        window.createApplicationSubmissionId(firstPayload),
                        window.createApplicationSubmissionId(editedPayload)
                    ],
                    firstKeyLength: firstPayload.length,
                    retainedKeyLength: window.__pendingApplicationSubmissionFingerprint.length,
                    reportedBodyLength: report.jsonBodyLength,
                    actualBodyLength: JSON.stringify(applicantData).length
                };
            }
            """);

        var ids = result.GetProperty("ids").EnumerateArray().Select(value => value.GetString()).ToList();
        Assert.Equal(ids[0], ids[1]);
        Assert.NotEqual(ids[0], ids[2]);
        Assert.True(result.GetProperty("firstKeyLength").GetInt32() < 256);
        Assert.True(result.GetProperty("retainedKeyLength").GetInt32() < 256);
        Assert.Equal(
            result.GetProperty("actualBodyLength").GetInt32(),
            result.GetProperty("reportedBodyLength").GetInt32());

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.createApplicationSubmissionId === 'function'");
        var reloadedId = await page.EvaluateAsync<string>(
            """
            () => {
                const largeProof = 'data:image/jpeg;base64,' + 'A'.repeat(2 * 1024 * 1024);
                const firstPayload = window.createApplicationSubmissionPayloadKey({ proofPics: [largeProof] });
                return window.createApplicationSubmissionId(firstPayload);
            }
            """);

        Assert.Equal(ids[0], reloadedId);
    }

    [Fact]
    public async Task ApplicationSubmission_RecoversCheckoutAfterThePostConnectionDrops()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: false);
        var applicationAttempts = 0;
        var recoveryAttempts = 0;

        await context.RouteAsync("**/api/application/application", async route =>
        {
            Interlocked.Increment(ref applicationAttempts);
            await route.AbortAsync("connectionreset");
        });
        await context.RouteAsync("**/api/application/submission-status", async route =>
        {
            Interlocked.Increment(ref recoveryAttempts);
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"success":true,"paymentRequired":true,"checkoutLink":"https://pay.example.test/invoice-1","invoiceId":"invoice-1"}"""
            });
        });

        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof window.submitApplicationWithRecovery === 'function'");

        var result = await page.EvaluateAsync<JsonElement>(
            """
            async () => {
                const attempt = await window.submitApplicationWithRecovery({
                    submissionId: 'submission-recovery-test',
                    name: 'Recovery Test'
                }, 2000);
                return {
                    ok: attempt.ok,
                    recovered: attempt.recovered,
                    hasResponse: attempt.response !== null,
                    checkoutLink: attempt.submitResult?.checkoutLink,
                    invoiceId: attempt.submitResult?.invoiceId
                };
            }
            """);

        Assert.True(result.GetProperty("ok").GetBoolean());
        Assert.True(result.GetProperty("recovered").GetBoolean());
        Assert.False(result.GetProperty("hasResponse").GetBoolean());
        Assert.Equal("https://pay.example.test/invoice-1", result.GetProperty("checkoutLink").GetString());
        Assert.Equal("invoice-1", result.GetProperty("invoiceId").GetString());
        Assert.Equal(1, applicationAttempts);
        Assert.Equal(1, recoveryAttempts);
    }

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser, BrowserTestApp app)
    {
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
    }

    private static async Task RoutePageDependenciesAsync(
        IBrowserContext context,
        bool delayProofHelper,
        TaskCompletionSource? proofHelperRequestStarted = null,
        Task? releaseProofHelper = null)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context, async uri =>
        {
            if (delayProofHelper && uri.AbsolutePath.Equals("/js/proof-helpers.js", StringComparison.OrdinalIgnoreCase))
            {
                proofHelperRequestStarted?.TrySetResult();
                if (releaseProofHelper is not null)
                    await releaseProofHelper;
            }
        });
    }

    private static async Task AdvanceOnboardingStageAsync(IPage page, string expectedHeading)
    {
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton')?.disabled");
        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = expectedHeading }).WaitForAsync();
    }

    private static async Task AssertPrimaryProofActionAsync(ILocator button)
    {
        Assert.True(await button.EvaluateAsync<bool>("element => element.classList.contains('green')"));
        Assert.False(await button.EvaluateAsync<bool>("element => element.classList.contains('grey')"));
        Assert.False(await button.EvaluateAsync<bool>("element => element.classList.contains('flow-action--secondary')"));
    }

    private static async Task AssertSecondaryProofActionAsync(ILocator button)
    {
        Assert.False(await button.EvaluateAsync<bool>("element => element.classList.contains('green')"));
        Assert.True(await button.EvaluateAsync<bool>("element => element.classList.contains('grey')"));
        Assert.True(await button.EvaluateAsync<bool>("element => element.classList.contains('flow-action--secondary')"));
    }

    private static async Task<Task<JsonElement>> RoutePageDependenciesAndCaptureApplicationPostAsync(IBrowserContext context)
    {
        var payloadSource = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

        await context.RouteAsync("**/*", async route =>
        {
            if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var uri))
            {
                await route.ContinueAsync();
                return;
            }

            if ((uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) && uri.IsLoopback)
            {
                if (uri.AbsolutePath.Equals("/api/application/submission-report", StringComparison.OrdinalIgnoreCase))
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 204,
                        Body = ""
                    });
                    return;
                }

                if (uri.AbsolutePath.Equals("/api/application/application", StringComparison.OrdinalIgnoreCase)
                    && route.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(route.Request.PostData ?? "{}");
                        payloadSource.TrySetResult(document.RootElement.Clone());
                    }
                    catch (Exception exception)
                    {
                        payloadSource.TrySetException(exception);
                    }

                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = 200,
                        ContentType = "application/json",
                        Body = """{"paymentRequired":false}"""
                    });
                    return;
                }

                await route.ContinueAsync();
                return;
            }

            if (uri.Host.Equals("ipapi.co", StringComparison.OrdinalIgnoreCase))
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = """{"country_code":"HU","region_code":""}"""
                });
                return;
            }

            if (route.Request.ResourceType == "script")
            {
                await route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/javascript",
                    Body = uri.AbsolutePath.Contains("/aos/", StringComparison.OrdinalIgnoreCase)
                        ? "window.AOS={init(){},refresh(){}};"
                        : ""
                });
                return;
            }

            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = route.Request.ResourceType == "stylesheet" ? "text/css" : "text/plain",
                Body = ""
            });
        });

        return payloadSource.Task;
    }
}
