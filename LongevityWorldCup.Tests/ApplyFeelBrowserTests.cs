using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyFeelBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && !document.getElementById('nextButton').disabled");
        return page;
    }

    [Theory]
    [InlineData(160)]
    [InlineData(195)]
    public async Task NarrowZoom_KeepsTheThreeJourneyLabelsWhole(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 422 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context);
        Assert.True(await page.Locator("#mainProgressBar .stage-label").EvaluateAllAsync<bool>("labels => labels.every(e => e.scrollWidth <= e.clientWidth + 1 && e.clientHeight <= parseFloat(getComputedStyle(e).lineHeight) + 1)"),
            "Test, Calculate and Submit must fit on one line without reducing their font size.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FileDrops_UseExistingValidationAndNeverReplaceBusyPhotoOrProofSelections(bool reduced)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.dropFixture = (inside = true) => {
                    const transfer = new DataTransfer();
                    transfer.items.add(new File(['invalid'], 'notes.txt', { type: 'text/plain' }));
                    const target = inside ? document.querySelector('.convergence-stage-layout') : document.body;
                    for (const type of ['dragenter', 'dragover']) target.dispatchEvent(new DragEvent(type, {bubbles:true, cancelable:true, dataTransfer:transfer}));
                    return !target.dispatchEvent(new DragEvent('drop', {bubbles:true, cancelable:true, dataTransfer:transfer}));
                };
                currentStage = 5; goToStage(5);
                window.proofDropChanges = 0;
                document.querySelector('#proofPicInput').addEventListener('change', () => window.proofDropChanges++);
                window.dropFixture();
            }
            """);
        await Assertions.Expect(page.Locator("#custom-alert-message")).ToHaveTextAsync("Proof files must be images or PDFs.");
        await page.Locator("#custom-alert-close").ClickAsync();
        Assert.True(await page.EvaluateAsync<bool>("""
            () => {
                document.querySelector('#uploadProofButton').disabled = true;
                const blocked = window.dropFixture();
                const outside = window.dropFixture(false);
                currentStage = 4; goToStage(4);
                window.photoDropChanges = 0;
                document.querySelector('#profilePicInput').addEventListener('change', () => window.photoDropChanges++);
                applicationProfilePending = true;
                const reading = window.dropFixture();
                applicationProfilePending = false;
                document.querySelector('#uploadPart').style.display = 'none';
                const cropping = window.dropFixture();
                return blocked && outside && reading && cropping && window.proofDropChanges === 1
                    && window.photoDropChanges === 0 && !document.querySelector('.apply-drop-armed, .apply-drop-over');
            }
            """));
    }

    [Fact]
    public async Task RapidErrors_StayReadableAndDoNotLeaveSpacersWhenTheMessageReturns()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await OpenAsync(context);
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const error = document.querySelector('#nameError');
                const frame = () => new Promise(requestAnimationFrame);
                error.textContent = 'Name must contain at least three readable characters.';
                await frame();
                if (error.clientHeight <= 0 || getComputedStyle(error).opacity !== '1') return false;
                error.textContent = '';
                await frame();
                error.textContent = 'Name must contain at least three readable characters.';
                await frame();
                return error.clientHeight > 0 && !document.querySelector('.apply-error-spacer');
            }
            """));
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task InterruptedTravel_UsesNaturalHeightAndReducedMotionCancelsActiveEffects(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await OpenAsync(context);
        await page.EvaluateAsync("() => { for (const stage of [5, 2, 7, 1, 6]) { currentStage = stage; goToStage(stage, {focus:true}); } }");
        Assert.True(await page.Locator(".convergence-stage-layout").EvaluateAsync<bool>("card => !['hidden', 'clip'].includes(getComputedStyle(card).overflow)"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.getAnimations().some(a => a.playState === 'running')");
        await Assertions.Expect(page.Locator("#personalLink")).ToBeFocusedAsync();
        Assert.True(await page.Locator(".convergence-stage-layout").EvaluateAsync<bool>("card => card.clientHeight >= card.scrollHeight - 1"));
    }
}
