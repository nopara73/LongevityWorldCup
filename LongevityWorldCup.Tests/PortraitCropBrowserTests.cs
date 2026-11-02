using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PortraitCropBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KeyboardCrop_IsScopedToTheCanvas_AndCancelPreservesThePortrait(bool apply)
    {
        await using var context = await ContextAsync();
        var page = await PrepareAsync(context, apply);
        var image = apply ? "cropperImage" : "changeProfileCropperImage";
        var stage = page.Locator("[data-lwc-crop-stage] cropper-canvas");
        await stage.FocusAsync();
        await Assertions.Expect(stage).ToBeFocusedAsync();
        var before = await DataAsync(page, image);
        await page.Keyboard.PressAsync("ArrowRight");
        var moved = await DataAsync(page, image);
        Assert.True(moved[0] > before[0]);
        Assert.Equal(before[2], moved[2], 4);
        await page.Keyboard.PressAsync("+");
        Assert.True((await DataAsync(page, image))[2] < moved[2]);
        await page.Keyboard.PressAsync("Delete");
        await Assertions.Expect(page.Locator("cropper-selection")).ToHaveCountAsync(1);
        var adjusted = await DataAsync(page, image);
        var cancel = page.Locator(apply ? "#cancelProfileCropButton" : "#changeProfileCancelButton");
        await cancel.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.Equal(adjusted, await DataAsync(page, image));
        await cancel.ClickAsync();
        await Assertions.Expect(page.Locator(apply ? "#uploadButton" : "#changeProfilePicButton")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("[data-lwc-crop-stage], .lwc-crop-ambient")).ToHaveCountAsync(0);
        if (!apply) await Assertions.Expect(page.Locator("#submitButton")).ToBeDisabledAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Landing_InterruptedOrReplacedPortraitNeverStaysHidden(bool replaceSource)
    {
        await using var context = await ContextAsync();
        var page = await PrepareAsync(context, false);
        await page.Locator("#changeProfileCancelButton").ClickAsync();
        await page.EvaluateAsync("""
            async replaceSource => {
                const target = document.querySelector('.edit-profile-visual .illustration');
                target.scrollIntoView({block:'center'});
                const from = target.getBoundingClientRect();
                LwcPortraitCropper.land(from, target.src, target);
                await new Promise(r => setTimeout(r, 70));
                if (replaceSource) target.src = '/assets/logo.png';
                else LwcPortraitCropper.land(from, target.src, target);
            }
            """, replaceSource);
        await page.WaitForFunctionAsync("() => !document.querySelector('.lwc-crop-flight')");
        Assert.Equal("", await page.Locator(".edit-profile-visual .illustration").EvaluateAsync<string>("e => e.style.opacity"));
    }

    [Theory]
    [InlineData(390, 844, true, false)]
    [InlineData(390, 844, true, true)]
    [InlineData(252, 417, false, false)]
    [InlineData(195, 422, false, true)]
    public async Task CropDialog_ShortContentFitsWithLargeTextAndZoomSizedViewports(int width, int height, bool largeText, bool dark)
    {
        await using var context = await ContextAsync();
        var page = await PrepareAsync(context, false);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.SetViewportSizeAsync(width, height);
        if (largeText) await page.AddStyleTagAsync(new() { Content = "html {font-size:200% !important;}" });
        await page.EvaluateAsync("() => document.fonts.ready");
        await WaitForFiniteAnimationsAsync(page);
        var dialog = page.Locator("#changeProfileCropperModal");
        Assert.True(await dialog.EvaluateAsync<bool>("e => e.scrollHeight <= e.clientHeight + 1 && e.scrollWidth <= e.clientWidth + 1"));
        foreach (var selector in new[] { "#changeProfileCropButton", "#changeProfileCancelButton" })
        {
            var button = page.Locator(selector);
            await button.FocusAsync();
            Assert.True(await button.EvaluateAsync<bool>("""
                e => { const r=e.getBoundingClientRect(), d=e.closest('[role=dialog]').getBoundingClientRect();
                    const range=document.createRange();range.selectNodeContents(e.querySelector('.flow-action__label'));
                    return r.top>=d.top && r.bottom<=d.bottom && [...range.getClientRects()].every(t=>t.left>=r.left && t.right<=r.right && t.top>=r.top && t.bottom<=r.bottom); }
                """));
        }
    }

    [Fact]
    public async Task ApplicationLandscape_KeepsCropCommandsAboveDockAndPreservesSelectionOnHeightChanges()
    {
        await using var context = await ContextAsync();
        var page = await PrepareAsync(context, true);
        await page.SetViewportSizeAsync(844, 390);
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper.getContainerData().height < 200");
        var before = await DataAsync(page, "cropperImage");
        await page.EvaluateAsync("() => document.querySelector('.convergence-crop-preview').scrollIntoView({block:'start'})");
        await WaitForFiniteAnimationsAsync(page);
        Assert.True(await page.EvaluateAsync<bool>("""
            () => { const dock=document.querySelector('.flow-action-stack--docked').getBoundingClientRect();
                return ['cropButton','cancelProfileCropButton'].every(id=>{const r=document.getElementById(id).getBoundingClientRect();return r.top>=0 && r.bottom<=dock.top && r.height>=44;}); }
            """));
        await page.SetViewportSizeAsync(844, 900);
        await page.WaitForFunctionAsync("() => document.getElementById('cropperImage').cropper.getContainerData().height === 320");
        var after = await DataAsync(page, "cropperImage");
        for (var i=0;i<before.Length;i++) Assert.Equal(before[i],after[i],4);
    }

    private async Task<IBrowserContext> ContextAsync()
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, HasTouch = true,
            ReducedMotion = ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Alex Morgan', DisplayName:'Alex Morgan',
                ProfilePic:'/assets/content-images/play-athlete-placeholder.jpg', Division:"Men's", Flag:'United Kingdom',
                PersonalLink:'https://example.com', MediaContact:'press@example.com', Why:'Training for a longer, healthier life.', Biomarkers:[]}));
            localStorage.setItem('selectedAthleteName','Alex Morgan');localStorage.setItem('hasApplication','true');
            """);
        return context;
    }

    private async Task<IPage> PrepareAsync(IBrowserContext context, bool apply)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(apply ? "/apply?fake=1" : "/edit-profile");
        await page.WaitForFunctionAsync("() => !document.documentElement.hasAttribute('data-initial-view') && window.modulesReady");
        if (apply) await page.EvaluateAsync("() => { currentStage=4; goToStage(4); }");
        var bytes = await (await context.APIRequest.GetAsync("/assets/content-images/play-athlete-placeholder.jpg")).BodyAsync();
        await page.Locator(apply ? "#profilePicInput" : "#profilePicInputModal").SetInputFilesAsync(new FilePayload
        { Name = "portrait.jpg", MimeType = "image/jpeg", Buffer = bytes });
        await page.WaitForFunctionAsync("id => document.getElementById(id).cropper?.ready", apply ? "cropperImage" : "changeProfileCropperImage");
        return page;
    }

    private static Task<double[]> DataAsync(IPage page, string id) => page.EvaluateAsync<double[]>(
        "id => { const d=document.getElementById(id).cropper.getData(); return [d.x,d.y,d.width,d.height]; }", id);
}
