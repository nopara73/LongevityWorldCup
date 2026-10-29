using Microsoft.Playwright;
using System.Text.RegularExpressions;
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
    [InlineData(1440, "outlook")]
    [InlineData(390, "inline-only")]
    [InlineData(320, "inline-only")]
    [InlineData(320, "enlarged-text")]
    [InlineData(195, "zoom-reflow")]
    [InlineData(160, "zoom-reflow")]
    [InlineData(320, "no-css")]
    [InlineData(390, "no-css-dark")]
    public async Task EmailFallbacks_KeepContentAndActionsReadable(int width, string mode)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = mode.EndsWith("-dark", StringComparison.Ordinal) ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("https://**/*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        foreach (var (name, original) in TransactionalEmailFamilyTests.AthleteEmails())
        {
            var html = mode is "enlarged-text" or "zoom-reflow" ? original : Regex.Replace(original, "<style>.*?</style>", "", RegexOptions.Singleline);
            var noCss = mode.StartsWith("no-css", StringComparison.Ordinal);
            if (noCss)
                html = Regex.Replace(html, " style=\"[^\"]*\"", "");
            if (mode == "outlook")
            {
                // Materialize MSO-only markup and remove unsupported table width CSS.
                // This checks the fallback contract; Chromium is not the Word rendering engine.
                Assert.Contains("<!--[if mso]><table role=\"presentation\" width=\"560\"", html);
                if (html.Contains("class=\"lwc-email-action-link\"", StringComparison.Ordinal))
                    Assert.Contains("mso-padding-alt:15px 20px", html);
                html = Regex.Replace(html, @"<!--\[if mso\]>(.*?)<!\[endif\]-->", "$1", RegexOptions.Singleline);
                html = html.Replace("max-width:560px;", "").Replace("table-layout:fixed;", "");
            }
            await page.SetContentAsync(html);
            if (mode == "enlarged-text")
                await page.EvaluateAsync("() => { for (const e of document.querySelectorAll('h1,p,a,span,td')) { const size=parseFloat(getComputedStyle(e).fontSize); e.dataset.textSize=size; } for (const e of document.querySelectorAll('[data-text-size]')) e.style.setProperty('font-size', (+e.dataset.textSize*2)+'px','important'); }");
            await ExpectNoHorizontalOverflowAsync(page);
            if (mode == "outlook")
                Assert.InRange(await page.Locator(".lwc-email-shell").EvaluateAsync<double>("e => e.getBoundingClientRect().width"), 559, 561);

            var clipped = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const bad=[];
                    const walker=document.createTreeWalker(document.body,NodeFilter.SHOW_TEXT);
                    for(let n; n=walker.nextNode();) {
                        if(!n.textContent.trim() || ['STYLE','SCRIPT'].includes(n.parentElement.tagName)) continue;
                        const range=document.createRange(); range.selectNodeContents(n);
                        for(const r of range.getClientRects()) if(r.left < -1 || r.right > innerWidth+1) bad.push(n.textContent.trim());
                    }
                    return bad;
                }
                """);
            Assert.True(clipped.Length == 0, $"{name}, {mode}: clipped glyphs: {string.Join(" | ", clipped)}");
            if (noCss)
            {
                Assert.Equal("rgb(23, 33, 43)", await page.Locator(".lwc-email-greeting").EvaluateAsync<string>("e => getComputedStyle(e).color"));
                foreach (var link in await page.Locator("a[href]").AllAsync())
                {
                    var action = await link.EvaluateAsync<bool>("e => e.classList.contains('lwc-email-action-link')");
                    Assert.Equal(action ? "rgb(255, 255, 255)" : "rgb(8, 118, 133)",
                        await link.EvaluateAsync<string>("e => getComputedStyle(e.querySelector('font') || e).color"));
                }
            }
            if (name.StartsWith("lmx-") && name is not "lmx-confirmation" and not "lmx-access")
            {
                var stop = page.GetByRole(AriaRole.Link, new() { Name = "Stop Challenge reminder emails" });
                Assert.Equal(TransactionalEmailFamilyTests.StopUrl, await stop.GetAttributeAsync("href"));
                if (!noCss)
                    Assert.True((await stop.BoundingBoxAsync())!.Height >= 44);
            }
        }
    }

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
