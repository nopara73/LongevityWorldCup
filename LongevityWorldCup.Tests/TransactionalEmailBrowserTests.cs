using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class TransactionalEmailBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1280, false)]
    [InlineData(390, false)]
    [InlineData(390, true)]
    [InlineData(320, true)]
    public async Task AthleteEmails_FitOneCenteredCardWithAFullWidthAction(int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.RouteAsync("https://longevityworldcup.com/**", route => route.AbortAsync());
        var page = await context.NewPageAsync();

        foreach (var (name, html) in TransactionalEmailFamilyTests.AthleteEmails())
        {
            await page.SetContentAsync(html);
            await ExpectNoHorizontalOverflowAsync(page);

            var layout = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const card = document.querySelector('.lwc-email-card').getBoundingClientRect();
                    const head = document.querySelector('.lwc-email-masthead').getBoundingClientRect();
                    const action = document.querySelector('.lwc-email-action-link, .lwc-email-notice-cell')?.getBoundingClientRect();
                    const style = getComputedStyle(document.querySelector('.lwc-email-card'));
                    const pad = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
                    const greeting = parseFloat(getComputedStyle(document.querySelector('.lwc-email-greeting')).fontSize);
                    return [card.width, card.left, window.innerWidth - card.right, head.width,
                        action ? action.width : -1, action ? action.height : -1, card.width - pad, greeting];
                }
                """);
            var (cardWidth, left, right, headWidth, actionWidth, actionHeight, contentWidth, greeting) =
                (layout[0], layout[1], layout[2], layout[3], layout[4], layout[5], layout[6], layout[7]);

            Assert.True(cardWidth <= 561, $"{name}: the card should stay at most 560px wide, was {cardWidth}.");
            Assert.True(Math.Abs(left - right) <= 1, $"{name}: the card should be centred ({left} vs {right}).");
            Assert.True(Math.Abs(headWidth - cardWidth) <= 1, $"{name}: the masthead should span the card.");
            Assert.True(greeting >= 20, $"{name}: the greeting should lead the message, was {greeting}px.");
            if (name != "change-request")
            {
                Assert.True(actionWidth >= contentWidth - 2, $"{name}: the main step should span the card ({actionWidth} of {contentWidth}).");
                Assert.True(actionHeight >= 44, $"{name}: the main step should be a comfortable tap target, was {actionHeight}px.");
            }
            if (width < 600)
                Assert.True(cardWidth >= width - 20, $"{name}: phones should give the card nearly the full width, was {cardWidth}.");
        }
    }

    [Theory]
    [InlineData(false, "rgb(255, 255, 255)", "rgb(23, 33, 43)", "rgb(243, 245, 247)")]
    [InlineData(true, "rgb(23, 33, 43)", "rgb(230, 237, 243)", "rgb(13, 20, 27)")]
    public async Task AthleteEmails_FollowTheReadersColorScheme(bool dark, string card, string ink, string canvas)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 600, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await context.RouteAsync("https://longevityworldcup.com/**", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.SetContentAsync(TransactionalEmailFamilyTests.SubmissionHtml(checkoutLink: TransactionalEmailFamilyTests.CheckoutUrl, isPro: true));

        var colors = await page.EvaluateAsync<string[]>(
            """
            () => [
                getComputedStyle(document.querySelector('.lwc-email-card')).backgroundColor,
                getComputedStyle(document.querySelector('.lwc-email-greeting')).color,
                getComputedStyle(document.querySelector('.lwc-email-canvas')).backgroundColor,
                getComputedStyle(document.querySelector('.lwc-email-masthead')).backgroundColor,
                getComputedStyle(document.querySelector('.lwc-email-action-cell')).backgroundColor,
                getComputedStyle(document.querySelector('.lwc-email-action-link')).color
            ]
            """);

        Assert.Equal(card, colors[0]);
        Assert.Equal(ink, colors[1]);
        Assert.Equal(canvas, colors[2]);
        Assert.Equal("rgb(16, 24, 32)", colors[3]);
        Assert.Equal("rgb(8, 118, 133)", colors[4]);
        Assert.Equal("rgb(255, 255, 255)", colors[5]);
    }
}
