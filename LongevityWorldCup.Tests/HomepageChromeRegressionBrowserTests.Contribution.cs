using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.HomepageChromeRegressionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepageContributionBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/#contribute")]
    [InlineData("/contribute")]
    [InlineData("/contribute?utm_content=donation-example")]
    public async Task ContributeDeepLink_KeepsTheQrCodeAndAddressInThePreviewViewport(string path)
    {
        const string donationAddress = "bc1qphwpd3mc9rts7vt4lrxxlxzs5jm3wh33w7hxz7";
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1194, 862);

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            expectedAddress => document.getElementById('leaderboardStatus')?.textContent === 'Leaderboard loaded.'
                && document.getElementById('eventsStatus')?.textContent === 'Events loaded.'
                && document.getElementById('btcAddressLink')?.href.includes(expectedAddress)
                && document.querySelector('.qr-code-img')?.complete
                && document.querySelector('.qr-code-img')?.naturalWidth > 0
            """,
            donationAddress);
        await SettleLayoutAsync(page);
        // Model any late async homepage content that grows above the fragment target.
        await page.EvaluateAsync(
            """
            () => {
                const simulatedLateContent = document.createElement('div');
                simulatedLateContent.id = 'simulated-late-homepage-content';
                simulatedLateContent.style.height = '520px';
                document.getElementById('contribute').before(simulatedLateContent);
            }
            """);
        await SettleLayoutAsync(page);

        var preview = await page.EvaluateAsync<ContributePreviewDiagnostics>(
            """
            () => {
                const section = document.getElementById('contribute').getBoundingClientRect();
                const qrCode = document.querySelector('.qr-code-img').getBoundingClientRect();
                const address = document.querySelector('.btc-address').getBoundingClientRect();
                return {
                    Hash: location.hash,
                    SectionTop: section.top,
                    QrTop: qrCode.top,
                    QrBottom: qrCode.bottom,
                    AddressTop: address.top,
                    AddressBottom: address.bottom,
                    ViewportHeight: innerHeight
                };
            }
            """);

        Assert.Equal("#contribute", preview.Hash);
        Assert.InRange(preview.SectionTop, 0, 80);
        Assert.InRange(preview.QrTop, 0, preview.ViewportHeight);
        Assert.InRange(preview.QrBottom, 0, preview.ViewportHeight);
        Assert.InRange(preview.AddressTop, 0, preview.ViewportHeight);
        Assert.InRange(preview.AddressBottom, 0, preview.ViewportHeight);
    }

    [Theory]
    [InlineData(900, 862)]
    [InlineData(390, 844)]
    [InlineData(844, 390)]
    public async Task ContributeShareLink_PreservesTheSectionOnSmallerScreens(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/contribute", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.getElementById('btcAddressLink')?.textContent.includes('bc1q')
                && document.querySelector('.qr-code-img')?.complete
                && document.querySelector('.qr-code-img')?.naturalWidth > 0
            """);
        await SettleLayoutAsync(page);

        Assert.EndsWith("/contribute#contribute", page.Url);
        var section = await page.Locator("#contribute").BoundingBoxAsync();
        Assert.NotNull(section);
        Assert.InRange(section!.X, 0, width);
        Assert.InRange(section.X + section.Width, 0, width + 1);
        Assert.InRange(section.Y, 0, 80);
        await Assertions.Expect(page.Locator(".qr-code-img")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#btcAddressLink")).ToHaveAttributeAsync("href", "https://mempool.space/address/bc1qphwpd3mc9rts7vt4lrxxlxzs5jm3wh33w7hxz7");
        await Assertions.Expect(page.Locator("#bitcoinCopyButton")).ToBeEnabledAsync();
    }

}
