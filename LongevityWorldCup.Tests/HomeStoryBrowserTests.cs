using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomeStoryBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(1024, 768, ColorScheme.Dark)]
    [InlineData(390, 844, ColorScheme.Light)]
    [InlineData(390, 844, ColorScheme.Dark)]
    public async Task HallOfFameAndFaq_AreOneReadingBlockOfQuestionTiles(int width, int height, ColorScheme scheme)
    {
        var (context, page) = await OpenHomepageAsync(width, height, scheme);
        await using var _ = context;

        var block = await page.EvaluateAsync<BlockMeasurement>(
            """
            () => {
                const hall = document.querySelector('#hall-of-fame');
                const faq = document.querySelector('#faq');
                const h = hall.getBoundingClientRect();
                const f = faq.getBoundingClientRect();
                const items = [...faq.querySelectorAll('.homepage-faq-item')];
                const styles = items.map(item => getComputedStyle(item));
                const framed = style => ['Top', 'Right', 'Bottom', 'Left'].every(side => parseFloat(style['border' + side + 'Width']) >= 1);
                return {
                    Seam: f.top - h.bottom,
                    EdgeDrift: Math.max(Math.abs(f.left - h.left), Math.abs(f.right - h.right)),
                    SeamRadius: parseFloat(getComputedStyle(hall).borderBottomLeftRadius) + parseFloat(getComputedStyle(faq).borderTopLeftRadius),
                    Columns: new Set(items.map(item => Math.round(item.getBoundingClientRect().left))).size,
                    FramedTiles: styles.filter(style => framed(style) && parseFloat(style.borderTopLeftRadius) >= 4).length,
                    AccentEdges: new Set(styles.map(style => style.borderLeftColor + ' ' + style.borderLeftWidth)).size,
                    Tiles: items.length
                };
            }
            """);

        Assert.InRange(block.Seam, -1, 1);
        Assert.InRange(block.EdgeDrift, 0, 1);
        Assert.Equal(0, block.SeamRadius);
        Assert.Equal(width > 600 ? 2 : 1, block.Columns);
        Assert.Equal(block.Tiles, block.FramedTiles);
        Assert.Equal(1, block.AccentEdges);
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(1440, 900, ColorScheme.Dark)]
    [InlineData(768, 1024, ColorScheme.Light)]
    [InlineData(390, 844, ColorScheme.Dark)]
    public async Task Finale_JoinsContributeAndNewsletterOnABandThatMeetsTheFooter(int width, int height, ColorScheme scheme)
    {
        var (context, page) = await OpenHomepageAsync(width, height, scheme);
        await using var _ = context;

        var finale = await page.EvaluateAsync<FinaleMeasurement>(
            """
            () => {
                const pair = document.querySelector('.homepage-panel-pair');
                const contribute = document.querySelector('#contribute').getBoundingClientRect();
                const newsletter = document.querySelector('#newsletter').getBoundingClientRect();
                const footer = document.querySelector('.footer').getBoundingClientRect();
                const probe = document.createElement('div');
                probe.style.background = 'var(--lwc-surface-accent)';
                document.body.append(probe);
                const accent = getComputedStyle(probe).backgroundColor;
                probe.remove();
                const style = getComputedStyle(pair);
                const sideBySide = Math.abs(contribute.top - newsletter.top) < 1;
                return {
                    FooterGap: footer.top - pair.getBoundingClientRect().bottom,
                    BandIsAccent: style.backgroundColor === accent,
                    BandBleeds: style.boxShadow.startsWith(accent) && style.clipPath.startsWith('inset'),
                    Join: sideBySide ? newsletter.left - contribute.right : newsletter.top - contribute.bottom,
                    PanelsShareEdges: sideBySide
                        ? Math.abs(contribute.bottom - newsletter.bottom)
                        : Math.abs(contribute.left - newsletter.left) + Math.abs(contribute.right - newsletter.right),
                    NewsletterTopAligned: getComputedStyle(document.querySelector('#newsletter .newsletter-panel')).alignContent === 'start'
                };
            }
            """);

        Assert.InRange(finale.FooterGap, -1, 1);
        Assert.True(finale.BandIsAccent, "The finale must sit on the accent band.");
        Assert.True(finale.BandBleeds, "The finale band must run the full window width.");
        Assert.InRange(finale.Join, -1, 1);
        Assert.InRange(finale.PanelsShareEdges, 0, 1);
        Assert.True(finale.NewsletterTopAligned, "The newsletter must read from the top like Contribute.");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task CardFeet_ShareTheBoardsMutedStrip(int width, int height)
    {
        var (context, page) = await OpenHomepageAsync(width, height, ColorScheme.Light);
        await using var _ = context;

        var backgrounds = await page.EvaluateAsync<string[]>(
            """
            () => ['.full-rankings-action', '#hall-of-fame .homepage-section-footer', '#faq .homepage-section-footer']
                .map(selector => getComputedStyle(document.querySelector(selector)).backgroundColor)
            """);

        Assert.All(backgrounds, background => Assert.Equal(backgrounds[0], background));
    }

    private async Task<(IBrowserContext, IPage)> OpenHomepageAsync(int width, int height, ColorScheme scheme)
    {
        var context = await HomepageChromeRegressionBrowserTests.NewContextAsync(Browser, App, ReducedMotion.Reduce);
        var page = await context.NewPageAsync();
        await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = scheme });
        await page.SetViewportSizeAsync(width, height);
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        return (context, page);
    }

    private sealed class BlockMeasurement
    {
        public double Seam { get; set; }
        public double EdgeDrift { get; set; }
        public double SeamRadius { get; set; }
        public int Columns { get; set; }
        public int FramedTiles { get; set; }
        public int AccentEdges { get; set; }
        public int Tiles { get; set; }
    }

    private sealed class FinaleMeasurement
    {
        public double FooterGap { get; set; }
        public bool BandIsAccent { get; set; }
        public bool BandBleeds { get; set; }
        public double Join { get; set; }
        public double PanelsShareEdges { get; set; }
        public bool NewsletterTopAligned { get; set; }
    }
}
