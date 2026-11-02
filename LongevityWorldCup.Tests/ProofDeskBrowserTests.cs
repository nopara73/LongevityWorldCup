using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.ProofUploadLayoutBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofDeskBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320, 200)]
    [InlineData(187, 100)]
    public async Task NarrowText_KeepsUploadAndBiomarkerWordsIntact(int width, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync(AthleteState);
        var page = await OpenAsync(context);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 1));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled");
        var brokenWords = await page.EvaluateAsync<string[]>("""
            () => {
                const broken = [];
                for (const element of document.querySelectorAll('#uploadProofButton .flow-action__label, .biomarker-item > span')) {
                    const bounds = element.getBoundingClientRect();
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    let text;
                    // Breaks at punctuation (Lab/report, hormone-binding) are valid;
                    // ordinary words must each fit on one line at the chosen size.
                    while (text = walker.nextNode()) for (const word of text.textContent.matchAll(/[\p{L}\p{N}]+/gu)) {
                        const range = document.createRange();
                        range.setStart(text, word.index); range.setEnd(text, word.index + word[0].length);
                        const rects = [...range.getClientRects()];
                        if (new Set(rects.map(r => Math.round(r.top))).size > 1
                            || rects.some(r => r.left < bounds.left - 1 || r.right > bounds.right + 1)) broken.push(word[0]);
                    }
                }
                return broken;
            }
            """);
        Assert.Empty(brokenWords);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData("drop", false)]
    [InlineData("camera", false)]
    [InlineData("drop", true)]
    public async Task FirstProof_KeepsActionsStableAndUsesTheSharedFileValidation(string source, bool reducedMotion)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync(AthleteState);
        var page = await OpenAsync(context);
        var files = await CreateProofImagesAsync(context, 1);
        if (source == "drop") await DropAsync(page, files[0]);
        else await page.Locator("#proofCameraInput").SetInputFilesAsync(files[0]);
        await page.WaitForFunctionAsync("() => document.querySelectorAll('.proof-page-card').length === 1 && !document.getElementById('proofPicInput').disabled");
        // Sample every frame while the new proof's arrival animation is active.
        var positions = await page.EvaluateAsync<double[][]>("""
            () => new Promise(resolve => {
                const samples = [];
                const sample = () => {
                    samples.push(['.proof-upload-start', '#uploadProofButton', '#proofImageContainer'].flatMap(selector => {
                        const r = document.querySelector(selector).getBoundingClientRect();
                        return [r.x, r.y, r.width, r.height];
                    }));
                    if (samples.length < 24) requestAnimationFrame(sample); else resolve(samples);
                };
                sample();
            })
            """);
        for (var dimension = 0; dimension < positions[0].Length; dimension++)
            Assert.InRange(positions.Max(sample => sample[dimension]) - positions.Min(sample => sample[dimension]), 0, 1);
        if (reducedMotion)
            Assert.Empty(await page.EvaluateAsync<string[]>("() => document.getAnimations().filter(a => a.effect?.target?.closest('.proof-upload-main')).map(a => a.playState)"));

        var original = await page.EvaluateAsync<string[]>("proofPics");
        await DropAsync(page, new() { Name = "not-a-proof.txt", MimeType = "text/plain", Buffer = "not an image"u8.ToArray() });
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Proof files must be images or PDFs.");
        Assert.Equal(original, await page.EvaluateAsync<string[]>("proofPics"));
        await page.Locator("#custom-alert-close").ClickAsync();
        await Assertions.Expect(page.Locator("#uploadProofButton")).ToBeFocusedAsync();
        await DropAsync(page, files[0]);
        await Assertions.Expect(page.Locator(".proof-upload-notice")).ToContainTextAsync("Duplicate proof images were skipped.");
        Assert.Equal(original, await page.EvaluateAsync<string[]>("proofPics"));
    }

    [Fact]
    public async Task Drop_RespectsLoadingBusyAndModalEditingAndClearsDragFeedback()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.AddInitScriptAsync(AthleteState);
        var page = await OpenAsync(context);
        var files = await CreateProofImagesAsync(context, 2);
        await page.Locator("#proofPicInput").SetInputFilesAsync(files[0]);
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled");
        await page.Locator(".proof-page-preview").ClickAsync();
        await DropAsync(page, files[1]);
        Assert.Single(await page.EvaluateAsync<string[]>("proofPics"));
        await Assertions.Expect(page.Locator(".proof-review-dialog")).ToHaveAttributeAsync("open", "");
        await page.Keyboard.PressAsync("Escape");

        foreach (var state in new[] { "loading", "busy", "input" })
        {
            await page.EvaluateAsync("""
                state => {
                    if (state === 'loading') document.documentElement.setAttribute('data-initial-view', 'loading');
                    else document.getElementById(state === 'busy' ? 'uploadProofButton' : 'proofPicInput').disabled = true;
                }
                """, state);
            await DropAsync(page, files[1]);
            Assert.Single(await page.EvaluateAsync<string[]>("proofPics"));
            await page.EvaluateAsync("""
                () => {
                    document.documentElement.removeAttribute('data-initial-view');
                    document.getElementById('uploadProofButton').disabled = false;
                    document.getElementById('proofPicInput').disabled = false;
                }
                """);
        }
        foreach (var ending in new[] { "dragleave", "blur", "drop" })
        {
            await page.EvaluateAsync("""
                () => {
                    window.__deskTransfer = new DataTransfer();
                    __deskTransfer.items.add(new File(['x'], 'test.txt', {type:'text/plain'}));
                    window.dispatchEvent(new DragEvent('dragenter', {dataTransfer:__deskTransfer, cancelable:true}));
                }
                """);
            await Assertions.Expect(page.Locator("body")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("proof-desk-dragging"));
            await page.EvaluateAsync("""
                ending => {
                    if (ending === 'blur') window.dispatchEvent(new Event('blur'));
                    else {
                        if (ending === 'drop') document.getElementById('uploadProofButton').disabled = true;
                        window.dispatchEvent(new DragEvent(ending, {dataTransfer:__deskTransfer, cancelable:true}));
                        document.getElementById('uploadProofButton').disabled = false;
                    }
                }
                """, ending);
            Assert.False(await page.Locator("body").EvaluateAsync<bool>("e => e.classList.contains('proof-desk-dragging')"));
        }
        await DropAsync(page, files[1]);
        await Assertions.Expect(page.Locator(".proof-page-card")).ToHaveCountAsync(2);
    }

    private static async Task DropAsync(IPage page, FilePayload file)
    {
        var result = await page.EvaluateAsync<bool[]>("""
            file => {
                const transfer = new DataTransfer();
                transfer.items.add(new File([Uint8Array.from(atob(file.data), c => c.charCodeAt(0))], file.name, {type:file.type}));
                const over = new DragEvent('dragover', {dataTransfer:transfer, bubbles:true, cancelable:true});
                const drop = new DragEvent('drop', {dataTransfer:transfer, bubbles:true, cancelable:true});
                window.dispatchEvent(over);window.dispatchEvent(drop);
                return [over.defaultPrevented, drop.defaultPrevented];
            }
            """, new { name = file.Name, type = file.MimeType, data = Convert.ToBase64String(file.Buffer) });
        Assert.All(result, Assert.True);
    }
}
