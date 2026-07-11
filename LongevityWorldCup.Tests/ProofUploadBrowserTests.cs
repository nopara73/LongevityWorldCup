using Microsoft.Playwright;
using System.Text;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class ProofUploadBrowserTests
{
    [Fact]
    public async Task ResultUpload_WaitsForDelayedProofHelperBeforeBindingUploadControls()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: true);

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

        await page.GotoAsync("/play/proof-upload.html", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(browser, app);
        await RoutePageDependenciesAsync(context, delayProofHelper: true);

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

        await page.GotoAsync("/onboarding/convergence.html?fake=1", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

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
        await AssertPrimaryProofActionAsync(cameraButton);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task ResultUpload_SubmitsPdfProofFromReportedFlow()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await LaunchBrowserAsync(playwright);
        await using var context = await NewContextAsync(browser, app);
        var payloadTask = await RoutePageDependenciesAndCaptureApplicationPostAsync(context);

        await context.AddInitScriptAsync(
            """
            window.pdfjsLib = {
                GlobalWorkerOptions: {},
                getDocument() {
                    return {
                        promise: Promise.resolve({
                            numPages: 1,
                            getPage: async () => ({
                                getViewport: () => ({ width: 12, height: 12 }),
                                render: ({ canvasContext }) => {
                                    canvasContext.fillStyle = '#ffffff';
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

        await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload
        {
            Name = "lab-results.pdf",
            MimeType = "application/pdf",
            Buffer = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF")
        });
        await page.WaitForFunctionAsync(
            "() => !document.getElementById('submitButton')?.disabled && document.querySelectorAll('#proofImageContainer img').length === 1");
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
        var proof = Assert.Single(payload.GetProperty("proofPics").EnumerateArray()).GetString();
        Assert.StartsWith("data:image/", proof);
        Assert.Contains(";base64,", proof);
        Assert.Empty(errors);
    }

    private static async Task<IBrowser> LaunchBrowserAsync(IPlaywright playwright)
    {
        return await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    private static async Task<IBrowserContext> NewContextAsync(IBrowser browser, BrowserTestApp app)
    {
        return await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US"
        });
    }

    private static async Task RoutePageDependenciesAsync(IBrowserContext context, bool delayProofHelper)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context, async uri =>
        {
            if (delayProofHelper && uri.AbsolutePath.Equals("/js/proof-helpers.js", StringComparison.OrdinalIgnoreCase))
                await Task.Delay(1200);
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
