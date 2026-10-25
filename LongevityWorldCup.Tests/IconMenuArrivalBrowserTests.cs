using Microsoft.Playwright;
using System.Text.Json;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class IconMenuArrivalBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task MenuContent_RemainsReadableWithItsMovingPanelFromTheFirstPresentedFrames(int width, bool dark)
    {
        await using var context = await Browser.NewContextAsync(new() {
            BaseURL=App.BaseAddress.ToString(), ViewportSize=new(){Width=width,Height=900},
            ColorScheme=dark?ColorScheme.Dark:ColorScheme.Light, ReducedMotion=ReducedMotion.NoPreference,
            IsMobile=width<600,HasTouch=width<600
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page=await context.NewPageAsync();
        await page.GotoAsync("/leaderboard");
        await page.EvaluateAsync("()=>document.fonts.ready");
        await page.EvaluateAsync("""
            () => {
                window.menuContentFrames=[];window.recordMenuContent=true;
                const sample=()=>{
                    if(!window.recordMenuContent)return;
                    setTimeout(()=>{
                        const menu=document.getElementById('site-menu'),panel=menu?.querySelector('.site-menu-panel');
                        if(!menu?.open||!menu.classList.contains('is-open')||!panel)return;
                        const box=panel.getBoundingClientRect();
                        if(box.bottom<=0||box.top>=innerHeight||Number(getComputedStyle(panel).opacity)===0)return;
                        const links=[...panel.querySelectorAll('.footer-link')].filter(e=>e.getBoundingClientRect().width);
                        const hidden=links.filter(link=>{
                            for(let e=link;e&&e!==panel;e=e.parentElement){
                                const style=getComputedStyle(e);
                                if(style.visibility!=='visible'||Number(style.opacity)<.99)return true;
                            }
                            return false;
                        }).map(link=>link.ariaLabel||link.textContent.trim());
                        window.menuContentFrames.push({at:performance.now(),count:links.length,hidden});
                    },0);
                    requestAnimationFrame(sample);
                };requestAnimationFrame(sample);
            }
            """);
        if(width<600)await page.Locator("header .site-menu-toggle").TapAsync();
        else await page.Locator("header .site-menu-toggle").ClickAsync();
        await page.WaitForFunctionAsync("()=>window.menuContentFrames.length>=20");
        var frames=await page.EvaluateAsync<JsonElement>("()=>{window.recordMenuContent=false;return window.menuContentFrames}");
        Assert.True(frames.GetArrayLength()>=20);
        foreach(var frame in frames.EnumerateArray())
        {
            Assert.True(frame.GetProperty("count").GetInt32()>=15,frame.ToString());
            Assert.Equal(0,frame.GetProperty("hidden").GetArrayLength());
        }
        await page.Keyboard.PressAsync("Escape");
    }
}
