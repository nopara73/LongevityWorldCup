using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class TypeSystemBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/about")]
    [InlineData("/media")]
    [InlineData("/error/404.html")]
    public async Task SharedType_LoadsOnceBeforeTheAestheticLayer(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, path);
        var sheets = await page.Locator("head link[rel=stylesheet]").EvaluateAllAsync<string[]>(
            "links => links.map(link => new URL(link.href).pathname)");
        Assert.Single(sheets, path => path == "/css/type-system.css");
        Assert.True(Array.IndexOf(sheets, "/css/type-system.css") < Array.IndexOf(sheets, "/css/aesthetic-system.css"));
        Assert.Equal(0, await page.Locator("script[src*='/js/type-system.js']").CountAsync());
    }

    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(600)]
    [InlineData(601)]
    [InlineData(1024)]
    public async Task DefaultRoles_RespectTextEnlargementWithoutASecondEntrance(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.NoPreference
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/media");
        // Isolate the default role from the page's own compact layout rules.
        // This is a synthetic role fixture, not production screenshot content.
        await page.EvaluateAsync("""
            () => {
                document.body.innerHTML = '<main><h1>One shared title</h1><h2>Section heading</h2></main>';
                scrollTo(0, 0);
            }
            """);
        var normal = await page.Locator("h1").EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        var enlarged = await page.Locator("h1").EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        Assert.InRange(Math.Abs(enlarged - 2 * normal), 0, 0.1);
        Assert.Equal("none", await page.Locator("h1").EvaluateAsync<string>("e => getComputedStyle(e).animationName"));
        Assert.Equal("1", await page.Locator("h1").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
        await page.SetViewportSizeAndWaitForLayoutAsync(width + 1, 844);
        var resized = await page.Locator("h1").EvaluateAsync<double>("e => parseFloat(getComputedStyle(e).fontSize)");
        Assert.InRange(resized - enlarged, -0.01, 0.1);
    }
}
