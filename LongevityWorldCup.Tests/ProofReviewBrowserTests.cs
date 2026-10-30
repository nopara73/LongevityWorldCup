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
    [InlineData(1440, 900, 100, false)]
    [InlineData(390, 844, 100, true)]
    [InlineData(320, 640, 200, false)]
    [InlineData(195, 422, 100, true)]
    public async Task Review_LongFileNamesRemainReadableWithoutClippingTheTitle(int width, int height, int textPercent, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, false);
        var files = await CreatePagesAsync(1);
        files[0].Name = "Comprehensive metabolic panel and complete blood count — laboratory results September 2026.png";
        await UploadAsync(page, files);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await ApplyDraftForReviewAsync(page);
        var title = page.Locator(".proof-review-toolbar h2");
        await Assertions.Expect(title).ToHaveTextAsync(files[0].Name);
        Assert.True(await title.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"),
            "The full proof filename must remain readable instead of being line-clamped.");
        await page.Locator(".proof-review-close").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Removal_UpdatesDataAndValidationBeforeAnimationOrImmediateSubmit(bool onboarding, bool reduced)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(2));
        // A single browser task deliberately gives the exit animation no time to
        // finish. Exercise the real submission entry point with its write mocked.
        Assert.True(await page.EvaluateAsync<bool>("""
            onboarding => {
                const expected = proofPics.slice(1);
                let submitted;
                window.submitApplicationWithRecovery = data => {
                    submitted = JSON.parse(JSON.stringify(data));
                    return new Promise(() => {});
                };
                const button = document.querySelector(onboarding ? '#nextButton' : '#submitButton');
                document.querySelector('.proof-page-remove').click();
                if (JSON.stringify(proofPics) !== JSON.stringify(expected) || button.disabled) return false;
                if (!onboarding) {
                    button.click();
                    if (!submitted || JSON.stringify(submitted.proofPics) !== JSON.stringify(expected)) return false;
                }
                // Undo during the visual exit, then remove all pages in the same
                // task. No delayed callback may remove the restored page again.
                document.querySelector('.proof-undo').click();
                window.__restoredProofs = [...proofPics];
                for (const animation of document.getAnimations()) animation.cancel();
                return JSON.stringify(proofPics) === JSON.stringify(window.__restoredProofs);
            }
            """, onboarding));
        await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        Assert.True(await page.EvaluateAsync<bool>("""
            onboarding => {
                if (JSON.stringify(proofPics) !== JSON.stringify(window.__restoredProofs)) return false;
                while (document.querySelector('.proof-page-remove')) document.querySelector('.proof-page-remove').click();
                const action = document.querySelector(onboarding ? '#nextButton' : '#submitButton');
                return proofPics.length === 0 && action.disabled
                    && window.dispatchEvent(new Event('beforeunload', { cancelable: true }));
            }
            """, onboarding));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReloadWithProofs_CanBeCancelledWithoutLosingThePages(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(2));
        await page.Locator(".biomarker-checkbox").First.CheckAsync();
        var original = await ReadSourcesAsync(page);
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await page.Locator(".proof-review-close").ClickAsync();
        var warned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.Dialog += async (_, dialog) =>
        {
            if (dialog.Type == "beforeunload")
            {
                await dialog.DismissAsync();
                warned.TrySetResult();
            }
        };
        await page.EvaluateAsync("setTimeout(() => location.reload(), 0)");
        await warned.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(original, await ReadSourcesAsync(page));
        Assert.True(await page.Locator(".biomarker-checkbox").First.IsCheckedAsync());
        Assert.True(await page.Locator(onboarding ? "#nextButton" : "#submitButton").IsEnabledAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovingAllProofs_ClearsTheExitWarningAndUndoRestoresIt(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(1));
        Assert.True(await WouldWarnOnExitAsync(page));
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        Assert.False(await WouldWarnOnExitAsync(page));
        await page.Locator(".proof-undo").ClickAsync();
        Assert.True(await WouldWarnOnExitAsync(page));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedProofSubmission_CanContinueWithoutAnExitWarning(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(1));
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) => { dialogs.Add(dialog.Type); await dialog.AcceptAsync(); };
        await page.EvaluateAsync("""
            onboarding => {
                const finish = onboarding ? finishFullApplicationSubmission : finishResultSubmission;
                finish({paymentRequired:false}, {applicantName:'Proof Review Test',accountEmail:'proof@example.test',submissionId:'proof-exit-test'});
            }
            """, onboarding);
        Assert.False(await WouldWarnOnExitAsync(page));
        await page.Locator("#custom-alert-close").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/review");
        Assert.Empty(dialogs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AcceptedSubmissionWithPaymentFailure_ClearsTheExitWarningBeforeConfirmation(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(1));
        await page.EvaluateAsync("""
            () => {
                window.trySendApplicationSubmissionReport = async () => {};
                window.submitApplicationWithRecovery = async () => ({
                    ok: false,
                    response: new Response('Application sent, but failed to create BTCPay invoice: unavailable', {status: 502})
                });
            }
            """);
        if (onboarding)
        {
            foreach (var heading in new[] { "Final details", "Application" })
            {
                await page.Locator("#nextButton").ClickAsync();
                await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
            }
            await page.Locator("#accountEmail").FillAsync("proof@example.test");
        }
        var dialogs = new List<string>();
        page.Dialog += async (_, dialog) => { dialogs.Add(dialog.Type); await dialog.AcceptAsync(); };
        await page.Locator(onboarding ? "#nextButton" : "#submitButton").ClickAsync();
        var received = onboarding ? "Your application was received" : "Your results were received";
        await Assertions.Expect(page.Locator("#custom-alert-message"))
            .ToHaveTextAsync($"{received}, but the payment page could not be created. We will follow up by email.");
        Assert.False(await WouldWarnOnExitAsync(page));
        await page.Locator("#custom-alert-close").ClickAsync();
        await page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/review");
        Assert.Empty(dialogs);
    }

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
        await Assertions.Expect(page.Locator(".proof-page-remove").Nth(1)).ToBeFocusedAsync();
        Assert.Equal(0, await page.Locator(".proof-review-dialog[open]").CountAsync());
        await page.Locator(".proof-page-remove").First.ClickAsync();
        await Assertions.Expect(page.Locator(".proof-page-remove").First).ToBeFocusedAsync();
        Assert.Equal(original.Skip(2), await ReadSourcesAsync(page));
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo removal", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Undo removal", Exact = true }).ClickAsync();
        await Assertions.Expect(page.Locator(".proof-page-preview").Nth(1)).ToBeFocusedAsync();
        Assert.Equal(original, await ReadSourcesAsync(page));
        Assert.Equal(original, await page.EvaluateAsync<string[]>("proofPics"));
        Assert.Equal(0, await page.Locator(".proof-review-feedback").CountAsync());

        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        await Assertions.Expect(page.Locator(".proof-undo")).ToBeFocusedAsync();
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
        await ApplyDraftForReviewAsync(page);
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
        // Apply replaces the gallery cards; return to the page last inspected.
        await Assertions.Expect(page.Locator(".proof-page-preview").Nth(2)).ToBeFocusedAsync();
        Assert.False(await page.EvaluateAsync<bool>("document.body.classList.contains('proof-review-open')"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardActivation_ReviewsRemovesAndRestoresWithoutAdvancingTheForm(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        await UploadAsync(page, await CreatePagesAsync(2));
        var original = await ReadSourcesAsync(page);
        await page.Locator(".proof-page-preview").First.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator(".proof-page-remove").First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(1);
        Assert.Equal(0, await page.Locator(".proof-review-dialog[open]").CountAsync());
        await page.Locator(".proof-undo").FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal(original, await ReadSourcesAsync(page));
        await Assertions.Expect(page.Locator(".proof-page-preview").First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeVisibleAsync();
        if (onboarding)
            Assert.Equal("5", await page.Locator("body").GetAttributeAsync("data-convergence-stage"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task QueuedCloseEvent_PreservesNewFocusAndAReopenedReview(bool onboarding, bool reopen)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(1));
        await page.Locator(".proof-page-preview").First.ClickAsync();
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeVisibleAsync();
        await page.EvaluateAsync("""
            reopen => {
                const dialog = document.querySelector('.proof-review-dialog');
                window.__proofCloseObserved = false;
                dialog.addEventListener('close', () => window.__proofCloseObserved = true, { once: true });
                dialog.close();
                if (reopen) document.querySelector('.proof-page-preview').click();
                else document.querySelector('.proof-page-remove').focus();
            }
            """, reopen);
        await page.WaitForFunctionAsync("() => window.__proofCloseObserved === true");
        if (reopen)
        {
            await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("() => document.body.classList.contains('proof-review-open') && document.querySelector('.proof-review-dialog').contains(document.activeElement)"));
        }
        else await Assertions.Expect(page.Locator(".proof-page-remove").First).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemoveTarget_TransparentCornersCatchTouchWithoutOpeningReview(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await PrepareAsync(context, onboarding);
        await UploadAsync(page, await CreatePagesAsync(2));
        var original = await ReadSourcesAsync(page);
        var remove = page.Locator(".proof-page-remove").Nth(1);
        await remove.ScrollIntoViewIfNeededAsync();
        Assert.True(await remove.EvaluateAsync<bool>(
            """
            button => {
                const rect = button.getBoundingClientRect();
                return [[2, 2], [rect.width - 2, 2], [2, rect.height - 2], [rect.width - 2, rect.height - 2]]
                    .every(([x, y]) => document.elementFromPoint(rect.left + x, rect.top + y)?.closest('button') === button);
            }
            """), "The complete 44px remove target must catch taps, including corners outside its round visible mark.");
        var target = (await remove.BoundingBoxAsync())!;
        await page.Touchscreen.TapAsync(target.X + 2, target.Y + 2);
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(original.Length - 1);
        Assert.Equal(0, await page.Locator(".proof-review-dialog[open]").CountAsync());
        await page.Locator(".proof-undo").TapAsync();
        Assert.Equal(original, await ReadSourcesAsync(page));
        await page.Locator(".proof-page-preview").Nth(1).TapAsync();
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".proof-edit-sheet img")).ToHaveAttributeAsync("src", original[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReuploadAfterRemoval_ThenUndoNeverAddsTheSamePageTwice(bool onboarding)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await PrepareAsync(context, onboarding);
        var files = await CreatePagesAsync(2);
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        await UploadAsync(page, files);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            var before = await ReadSourcesAsync(page);
            await page.Locator(".proof-page-remove").First.ClickAsync();
            await UploadAsync(page, [files[cycle % 2]]);
            await page.Locator(".proof-undo").ClickAsync();
            var after = await ReadSourcesAsync(page);
            Assert.Equal(2, after.Length);
            Assert.Equal(before.Reverse(), after);
            Assert.Equal(after, await page.EvaluateAsync<string[]>("proofPics"));
            Assert.Equal(0, await page.Locator(".proof-review-dialog[open]").CountAsync());
        }
        var sources = await ReadSourcesAsync(page);
        Assert.Equal(2, sources.Length);
        Assert.Equal(2, sources.Distinct().Count());
        await UploadAsync(page, files);
        Assert.Equal(2, (await ReadSourcesAsync(page)).Length);
        Assert.Contains("Duplicate proof images were skipped.", await page.Locator(".proof-upload-notice").InnerTextAsync());
        await UploadAsync(page, [(await CreatePagesAsync(3))[2]]);
        Assert.Equal(3, (await ReadSourcesAsync(page)).Length);
        Assert.Equal(onboarding ? 1 : 0, await page.Locator(".proof-review-feedback").CountAsync());
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
        foreach (var heading in new[] { "Almost there", "The price of glory", "Finding your why" })
        {
            await page.Locator("#backButton").ClickAsync();
            await page.GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true }).WaitForAsync();
        }
        await page.Locator("#why").FillAsync("");
        Assert.True(await page.Locator("#nextButton").IsDisabledAsync());
        Assert.True(await WouldWarnOnExitAsync(page));
        await page.EvaluateAsync("window.__releaseProofPreparation()");
        await page.WaitForFunctionAsync("() => !document.querySelector('#proofPicInput').disabled");
        Assert.True(await page.Locator("#nextButton").IsDisabledAsync());
        await page.Locator("#why").FillAsync("To live a longer and healthier life with the people I love.");
        foreach (var heading in new[] { "The price of glory", "Almost there", "Don't trust, verify" })
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
        await Assertions.Expect(page.Locator(".proof-edit-sheet img")).ToHaveAttributeAsync("src", sources[1]);
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
        await UploadAsync(page, [files[0]]);
        await Assertions.Expect(page.Locator(".proof-upload-notice")).ToHaveTextAsync("Duplicate proof images were skipped.");
        Assert.Equal(37, (await ReadSourcesAsync(page)).Length);
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
        await page.GetByRole(AriaRole.Heading, new() { Name = "Final details", Exact = true }).WaitForAsync();
        await page.Locator("#backButton").ClickAsync();
        await page.GetByRole(AriaRole.Heading, new() { Name = "Don't trust, verify", Exact = true }).WaitForAsync();
        Assert.True(await page.Locator(".biomarker-checkbox").First.IsCheckedAsync());
        await page.Locator(".proof-undo").ClickAsync();
        Assert.Equal(original, await ReadSourcesAsync(page));
        await UploadAsync(page, files);
        Assert.Equal(original, await ReadSourcesAsync(page));
    }

    internal static async Task ApplyDraftForReviewAsync(IPage page)
    {
        // Uploaded pages open ready to edit; inspection follows a real Apply.
        var stage = page.Locator(".proof-edit-stage");
        await Assertions.Expect(stage).ToBeFocusedAsync();
        await page.Locator(".proof-edit-sheet:not([hidden])").WaitForAsync();
        await stage.PressAsync("Enter");
        await page.Locator(".proof-edit-apply").ClickAsync();
        await Assertions.Expect(stage).ToHaveCountAsync(0);
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
            foreach (var heading in new[] { "Finding your why", "The price of glory", "Almost there", "Don't trust, verify" })
            {
                await WaitForFiniteAnimationsAsync(page);
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

    private static Task<bool> WouldWarnOnExitAsync(IPage page) =>
        page.EvaluateAsync<bool>("!window.dispatchEvent(new Event('beforeunload', {cancelable:true}))");

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
