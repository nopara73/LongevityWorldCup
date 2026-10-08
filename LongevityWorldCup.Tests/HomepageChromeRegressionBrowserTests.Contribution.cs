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
        var newsletter = await page.Locator("#newsletter").BoundingBoxAsync();
        var contribute = await page.Locator("#contribute").BoundingBoxAsync();
        Assert.NotNull(newsletter);
        Assert.NotNull(contribute);
        Assert.InRange(Math.Abs(newsletter!.Y - contribute!.Y), 0, 1);
        // Model any late async homepage content that grows above the fragment target.
        await page.EvaluateAsync(
            """
            () => {
                const simulatedLateContent = document.createElement('div');
                simulatedLateContent.id = 'simulated-late-homepage-content';
                simulatedLateContent.style.height = '520px';
                document.querySelector('.homepage-panel-pair').before(simulatedLateContent);
            }
            """);
        await SettleLayoutAsync(page);

        // ResizeObserver queues its alignment after layout; allow that behavior to finish.
        await page.WaitForFunctionAsync(
            "() => document.getElementById('contribute').getBoundingClientRect().top <= innerHeight / 2");

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
        // Contribute shares the page's last row with the newsletter, so the document can end
        // before the section reaches the top; it must still open in the upper half of the preview.
        Assert.InRange(preview.SectionTop, 0, preview.ViewportHeight / 2);
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

    [Theory]
    [InlineData(320, 16, true)]
    [InlineData(390, 16, false)]
    [InlineData(1280, 16, true)]
    [InlineData(320, 32, false)]
    [InlineData(390, 32, true)]
    [InlineData(640, 32, false)]
    [InlineData(768, 32, true)]
    [InlineData(1280, 32, false)]
    public async Task ContributePanel_KeepsPrizeTextReadableAndTheQrCodeSquare(int width, int fontSize, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(width, 844);
        await page.EmulateMediaAsync(new() { ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light });
        await page.GotoAsync("/contribute", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync(
            """
            () => document.getElementById('bitcoinCopyButton')?.disabled === false
                && document.querySelector('.btc-status-track')?.hasAttribute('aria-valuetext')
                && document.querySelector('.qr-code-img')?.complete
                && document.querySelector('.qr-code-img')?.naturalWidth > 0
            """);
        await page.EvaluateAsync("size => document.documentElement.style.fontSize = `${size}px`", fontSize);
        await SettleLayoutAsync(page);

        var failures = await page.EvaluateAsync<string[]>(
            """
            () => {
                const failures = [];
                const section = document.getElementById('contribute').getBoundingClientRect();
                const panel = document.querySelector('.contribute-panel').getBoundingClientRect();
                const status = document.querySelector('.btc-status').getBoundingClientRect();
                if (panel.left < section.left - 1 || panel.right > section.right + 1)
                    failures.push('The contribution panel extends beyond its section.');

                for (const selector of ['.btc-status-title', '.btc-status-goal', '.btc-status-value']) {
                    const element = document.querySelector(selector);
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    while (walker.nextNode()) {
                        const node = walker.currentNode;
                        for (let index = 0; index < node.textContent.length; index++) {
                            if (/\s/.test(node.textContent[index])) continue;
                            const range = document.createRange();
                            range.setStart(node, index);
                            range.setEnd(node, index + 1);
                            const text = range.getBoundingClientRect();
                            if (text.left < status.left - 1 || text.right > status.right + 1
                                || text.top < status.top - 1 || text.bottom > status.bottom + 1) {
                                failures.push(`${selector} clips text: ${element.textContent.trim()}`);
                                break;
                            }
                        }
                    }
                }

                const qr = document.querySelector('.qr-code-img').getBoundingClientRect();
                if (Math.abs(qr.width - qr.height) > 1)
                    failures.push(`The QR code is stretched to ${qr.width} by ${qr.height}.`);
                if (qr.left < panel.left - 1 || qr.right > panel.right + 1
                    || qr.top < panel.top - 1 || qr.bottom > panel.bottom + 1)
                    failures.push('The QR code extends beyond its panel.');
                const copy = document.getElementById('bitcoinCopyButton').getBoundingClientRect();
                if (copy.width < 44 || copy.height < 44)
                    failures.push('The copy button is smaller than its touch target.');
                return failures;
            }
            """);

        Assert.True(failures.Length == 0,
            $"Contribute at {width}px, root font {fontSize}px, dark={dark}: {string.Join("; ", failures)}");
        await page.Locator("#bitcoinCopyButton").FocusAsync();
        await Assertions.Expect(page.Locator("#bitcoinCopyButton")).ToBeFocusedAsync();
    }

}
