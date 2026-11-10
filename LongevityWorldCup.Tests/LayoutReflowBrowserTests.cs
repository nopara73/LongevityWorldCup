using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class LayoutReflowBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reflow_SurvivesUnrelatedResizeNotifications(bool removeItem)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 800, Height = 600 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await page.SetContentAsync("""
            <style>
              :root { --lwc-reflow-duration: 1000ms; --lwc-reflow-ease: linear; }
              #grid { display:grid; width:600px; grid-template-columns:repeat(auto-fill,minmax(180px,1fr)); gap:10px; }
              #grid > div { height:60px; background:teal; }
            </style>
            <div id="grid" data-lwc-reflow><div>One</div><div>Two</div><div>Three</div><div>Four</div></div>
            """);
        await page.AddScriptTagAsync(new() { Url = new Uri(App.BaseAddress, "/js/layout-system.js").ToString() });
        var result = await page.EvaluateAsync<bool>("""
            async removeItem => {
                const frame = () => new Promise(requestAnimationFrame);
                await frame(); await frame();
                const grid = document.getElementById('grid');
                const target = grid.children[2];
                if (removeItem) grid.firstElementChild.remove();
                else grid.style.width = '400px';
                await frame(); await frame();
                const animation = target.getAnimations().find(a => a.id === 'lwc-reflow');
                if (!animation) return false;
                grid.style.width = removeItem ? '610px' : '410px';
                await frame(); await frame();
                return target.getAnimations().includes(animation) && animation.playState === 'running';
            }
            """, removeItem);
        Assert.True(result, "A same-column or content-height resize must not cancel the grid's active movement.");
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => !document.getAnimations().some(a => a.id === 'lwc-reflow')");
    }
}
