using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LegacyUrlBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/onboarding/join-game.html")]
    [InlineData("/Join/")]
    public async Task OldJoinLinks_OpenJoinPanelAndPreserveUrlState(string path)
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();
        const string suffix = "?ref=old+link&token=AbC%2B%2F%3D#joinTrackPanel";

        var response = await page.GotoAsync(path + suffix);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        await page.Locator("#joinTrackPanel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("/join" + suffix, new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);
        Assert.Equal("https://longevityworldcup.com/join", await page.Locator("link[rel=canonical]").GetAttributeAsync("href"));
        Assert.False(await page.Locator("#playStartPanel").IsVisibleAsync());

        await page.ReloadAsync();
        await page.Locator("#joinTrackPanel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        Assert.Equal("/join" + suffix, new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);
    }

    [Fact]
    public async Task OldDocumentLink_PreservesFragmentTarget()
    {
        await using var context = await NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync("/misc-pages/about.html?ref=archive#how-it-works");

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Equal("/about?ref=archive#how-it-works", new Uri(page.Url).PathAndQuery + new Uri(page.Url).Fragment);
        Assert.Equal("how-it-works", await page.Locator(":target").GetAttributeAsync("id"));
    }

    private async Task<IBrowserContext> NewContextAsync()
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }
}
