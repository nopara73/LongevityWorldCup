using Microsoft.Playwright;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofReviewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovalAndUndo_PreserveTheSubmittedPagesAndTheirOrder(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 320, Height = 844 } });
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(3));
        var original = await ReadSourcesAsync(page);
        var firstCard = (await page.Locator(".proof-page-card").First.BoundingBoxAsync())!;
        var secondCard = (await page.Locator(".proof-page-card").Nth(1).BoundingBoxAsync())!;
        Assert.InRange(Math.Abs(firstCard.Y - secondCard.Y), 0, 1);
        Assert.True(secondCard.X >= firstCard.X + firstCard.Width);
        await page.Locator(".proof-page-remove").Nth(1).ClickAsync();
        await page.Locator(".proof-page-remove").First.ClickAsync();
        Assert.Equal(original.Skip(2), await ReadSourcesAsync(page));
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo removal", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo removal", Exact = true }).ClickAsync();
        Assert.Equal(original, await ReadSourcesAsync(page));
        Assert.Equal(original, await page.EvaluateAsync<string[]>("proofPics"));

        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsDisabledAsync());
        Assert.True(await page.Locator(".proof-undo").IsVisibleAsync());
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Single(await ReadSourcesAsync(page));
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsEnabledAsync());
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(390, true)]
    [InlineData(1280, false)]
    public async Task Review_PagesZoomAndKeyboardFocusStayInsideTheViewer(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, await CreatePagesAsync(3));
        var sources = await ReadSourcesAsync(page);
        await page.Locator(".proof-page-preview").First.ClickAsync();
        var dialog = page.GetByRole(AriaRole.Dialog, new() { Name = "Review attached proofs" });
        await dialog.WaitForAsync();
        Assert.True(await dialog.GetByRole(AriaRole.Button, new() { Name = "← Previous", Exact = true }).IsDisabledAsync());
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(sources[1], await dialog.Locator("img").GetAttributeAsync("src"));
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(sources[2], await dialog.Locator("img").GetAttributeAsync("src"));
        Assert.True(await dialog.GetByRole(AriaRole.Button, new() { Name = "Next →", Exact = true }).IsDisabledAsync());
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Zoom in", Exact = true }).ClickAsync();
        Assert.True(await dialog.Locator(".proof-review-stage").EvaluateAsync<bool>("e => e.scrollWidth > e.clientWidth"));
        await page.Keyboard.PressAsync("ArrowRight");
        await page.Keyboard.PressAsync("ArrowDown");
        Assert.True(await dialog.Locator(".proof-review-stage").EvaluateAsync<bool>("e => e.scrollLeft > 0 && e.scrollTop > 0"));
        Assert.Equal(sources[2], await dialog.Locator("img").GetAttributeAsync("src"));
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Fit page", Exact = true }).ClickAsync();
        var bounds = await dialog.BoundingBoxAsync();
        Assert.NotNull(bounds);
        Assert.InRange(bounds.X, 0, width - bounds.Width);
        Assert.InRange(bounds.Y, 0, 844 - bounds.Height);
        for (var index = 0; index < 8; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            Assert.True(await dialog.EvaluateAsync<bool>("e => e.contains(document.activeElement)"));
        }
        await page.Keyboard.PressAsync("Escape");
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('proof-review-open')");
        Assert.False(await dialog.IsVisibleAsync());
        Assert.True(await page.Locator(".proof-page-preview").First.EvaluateAsync<bool>("e => e === document.activeElement"));
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('proof-review-open')"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Fact]
    public async Task ReuploadAfterRemoval_ThenUndoNeverAddsTheSamePageTwice()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        var files = await CreatePagesAsync(2);
        await UploadAsync(page, files);
        await page.Locator(".proof-page-remove").First.ClickAsync();
        await UploadAsync(page, [files[0]]);
        await page.Locator(".proof-undo").ClickAsync();
        var sources = await ReadSourcesAsync(page);
        Assert.Equal(2, sources.Length);
        Assert.Equal(2, sources.Distinct().Count());
        await UploadAsync(page, files);
        Assert.Equal(2, (await ReadSourcesAsync(page)).Length);
        Assert.Contains("Duplicate proof images were skipped.", await page.Locator(".proof-upload-notice").InnerTextAsync());
    }

    [Fact]
    public async Task PreparingAFile_KeepsSubmissionDisabledEvenWhenTheChecklistChanges()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        var files = await CreatePagesAsync(2);
        await UploadAsync(page, [files[0]]);
        await page.EvaluateAsync("""
            () => {
                const optimize = window.optimizeImageClient;
                window.optimizeImageClient = async (...args) => {
                    await new Promise(resolve => window.__releaseProofPreparation = resolve);
                    return optimize(...args);
                };
            }
            """);
        await page.Locator("#proofPicInput").SetInputFilesAsync(files[1]);
        await page.WaitForFunctionAsync("() => typeof window.__releaseProofPreparation === 'function'");
        Assert.True(await page.Locator(".proof-preparation progress").IsVisibleAsync());
        Assert.Contains(files[1].Name, await page.Locator(".proof-preparation").InnerTextAsync());
        await page.Locator(".biomarker-checkbox").First.CheckAsync();
        Assert.True(await page.Locator("#submitButton").IsDisabledAsync());
        Assert.True(await page.Locator(".proof-page-remove").First.IsDisabledAsync());
        await page.EvaluateAsync("window.__releaseProofPreparation()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        Assert.Equal(2, (await ReadSourcesAsync(page)).Length);
        Assert.True(await page.Locator("#submitButton").IsEnabledAsync());
        Assert.Equal(0, await page.Locator(".proof-preparation").CountAsync());
    }

    [Fact]
    public async Task LeavingTheProofStepDuringPreparation_PreservesTheOtherStepsValidation()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, true);
        await page.EvaluateAsync("""
            () => {
                const optimize = window.optimizeImageClient;
                window.optimizeImageClient = async (...args) => {
                    await new Promise(resolve => window.__releaseProofPreparation = resolve);
                    return optimize(...args);
                };
            }
            """);
        await page.Locator("#proofPicInput").SetInputFilesAsync((await CreatePagesAsync(1))[0]);
        await page.WaitForFunctionAsync("() => typeof window.__releaseProofPreparation === 'function'");
        foreach (var heading in new[] { "4/a. Almost there", "3. The price of glory", "2. Finding your why" })
        {
            await page.Locator("#backButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
        }
        await page.Locator("#why").FillAsync("");
        Assert.True(await page.Locator("#nextButton").IsDisabledAsync());
        await page.EvaluateAsync("window.__releaseProofPreparation()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        Assert.True(await page.Locator("#nextButton").IsDisabledAsync());
        await page.Locator("#why").FillAsync("To live a longer and healthier life with the people I love.");
        foreach (var heading in new[] { "3. The price of glory", "4/a. Almost there", "4/b. Don't trust, verify" })
        {
            await page.Locator("#nextButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
        }
        Assert.Equal(2, await page.Locator(".proof-page-preview").CountAsync());
        Assert.True(await page.Locator("#nextButton").IsEnabledAsync());
    }

    [Fact]
    public async Task UnsupportedFiles_AreRejectedWithoutStartingPreparationOrDiscardingExistingProofs()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        await UploadAsync(page, await CreatePagesAsync(1));
        var original = await ReadSourcesAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.__unexpectedProofPreparation = false;
                window.optimizeImageClient = async () => { window.__unexpectedProofPreparation = true; throw new Error('Unexpected optimization'); };
            }
            """);
        await UploadAsync(page, [new() { Name = "notes.txt", MimeType = "text/plain", Buffer = [1, 2, 3] }]);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Proof files must be images or PDFs.");
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator("#uploadProofButton")).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("window.__unexpectedProofPreparation"));
        Assert.Equal(original, await ReadSourcesAsync(page));
        Assert.Equal(0, await page.Locator(".proof-preparation").CountAsync());
        Assert.True(await page.Locator("#submitButton").IsEnabledAsync());
    }

    [Fact]
    public async Task FailedPdf_KeepsExistingPagesAndContinuesWithTheNextImage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        var files = await CreatePagesAsync(2);
        await UploadAsync(page, [files[0]]);
        var original = await ReadSourcesAsync(page);
        await page.EvaluateAsync("""
            window.pdfjsLib = { getDocument: () => ({ promise: Promise.reject(new Error('Unreadable PDF')) }) };
            """);
        await UploadAsync(page, [new() { Name = "broken.pdf", MimeType = "application/pdf", Buffer = [1, 2, 3] }, files[1]]);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Some proof files could not be processed. Please try them again as images or PDFs.");
        await page.Locator("#custom-alert-close").ClickAsync();
        var sources = await ReadSourcesAsync(page);
        Assert.Equal(2, sources.Length);
        Assert.Equal(original[0], sources[0]);
        Assert.Equal(0, await page.Locator(".proof-preparation").CountAsync());
        Assert.True(await page.Locator("#proofPicInput").IsEnabledAsync());
        Assert.True(await page.Locator("#submitButton").IsEnabledAsync());
        await page.Locator(".proof-page-preview").Last.ClickAsync();
        Assert.Equal(sources[1], await page.Locator(".proof-review-dialog img").GetAttributeAsync("src"));
    }

    [Fact]
    public async Task PageLimit_LeavesTheReviewUsableAndPreventsUndoFromExceedingTheCap()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, false);
        var files = await CreatePagesAsync(38);
        await UploadAsync(page, files);
        Assert.Equal(37, (await ReadSourcesAsync(page)).Length);
        Assert.Contains("Only the first 37", await page.Locator(".proof-upload-notice").InnerTextAsync());
        await page.Locator(".proof-page-remove").First.ClickAsync();
        await UploadAsync(page, [files[37]]);
        Assert.Equal(37, (await ReadSourcesAsync(page)).Length);
        Assert.True(await page.Locator(".proof-undo").IsDisabledAsync());
        await page.Locator(".proof-page-remove").Last.ClickAsync();
        await page.Locator(".proof-undo").ClickAsync();
        var sources = await ReadSourcesAsync(page);
        Assert.Equal(37, sources.Length);
        Assert.Equal(sources, await page.EvaluateAsync<string[]>("proofPics"));
        Assert.True(await page.Locator("#submitButton").IsEnabledAsync());
    }

    [Fact]
    public async Task ReturningToTheProofStep_PreservesReviewStateAndDoesNotBindUploadTwice()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, true);
        var files = await CreatePagesAsync(2);
        await UploadAsync(page, files);
        var original = await ReadSourcesAsync(page);
        await page.Locator(".biomarker-checkbox").First.CheckAsync();
        await page.Locator(".proof-page-remove").Last.ClickAsync();
        await page.Locator("#nextButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "5. Final details", Exact = true }).WaitForAsync();
        await page.Locator("#backButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "4/b. Don't trust, verify", Exact = true }).WaitForAsync();
        Assert.True(await page.Locator(".biomarker-checkbox").First.IsCheckedAsync());
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Equal(original, await ReadSourcesAsync(page));
        await UploadAsync(page, files);
        Assert.Equal(original, await ReadSourcesAsync(page));
    }

    internal static async Task<IPage> PrepareAsync(IBrowserContext context, bool onboarding)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Proof Review Test',DisplayName:'Proof Review Test',Biomarkers:[]}));
            sessionStorage.setItem('biomarkerData', JSON.stringify({DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[{Date:'2026-09-01',AlbGL:45,GluMmolL:5.1}]}));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(onboarding ? "/apply?fake=1" : "/play/proof-upload.html", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (onboarding)
        {
            foreach (var heading in new[] { "2. Finding your why", "3. The price of glory", "4/a. Almost there", "4/b. Don't trust, verify" })
            {
                await page.Locator("#nextButton").ClickAsync();
                await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
            }
        }
        await page.WaitForFunctionAsync("() => document.querySelector('#proofPicInput')?.hasAttribute('data-listener')");
        return page;
    }

    private static async Task UploadAsync(IPage page, FilePayload[] files)
    {
        await page.Locator("#proofPicInput").SetInputFilesAsync(files);
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
    }

    private static Task<string[]> ReadSourcesAsync(IPage page) => page.Locator("#proofImageContainer img").EvaluateAllAsync<string[]>("images => images.map(image => image.src)");

    private static async Task<FilePayload[]> CreatePagesAsync(int count)
    {
        var files = new List<FilePayload>();
        for (var index = 0; index < count; index++)
        {
            using var image = new Image<Rgba32>(300, 420, new Rgba32((byte)(30 + index * 50), 110, 160));
            await using var stream = new MemoryStream();
            await image.SaveAsPngAsync(stream);
            files.Add(new() { Name = $"Lab report page {index + 1}.png", MimeType = "image/png", Buffer = stream.ToArray() });
        }
        return files.ToArray();
    }
}
