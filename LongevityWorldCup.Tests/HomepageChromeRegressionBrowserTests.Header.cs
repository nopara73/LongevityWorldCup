using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.HomepageChromeRegressionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HomepageHeaderBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task SharedHeaderBrand_HoverKeepsItsTextColorAndUsesPointerCursor()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        await AssertHoverKeepsColorAndUsesPointerAsync(
            page.Locator("header[role=\"banner\"] .header-link"));

        await page.GotoAsync("/history", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.EvaluateAsync("window.scrollTo(0, Math.min(700, document.documentElement.scrollHeight - innerHeight))");
        await SettleLayoutAsync(page);

        await page.Locator("#site-sticky-header")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await AssertHoverKeepsColorAndUsesPointerAsync(page.Locator(".site-sticky-header-link"));

        static async Task AssertHoverKeepsColorAndUsesPointerAsync(ILocator brand)
        {
            var colorBeforeHover = await brand.EvaluateAsync<string>("element => getComputedStyle(element).color");

            await brand.HoverAsync();

            var colorWhileHovered = await brand.EvaluateAsync<string>("element => getComputedStyle(element).color");
            var cursorWhileHovered = await brand.EvaluateAsync<string>("element => getComputedStyle(element).cursor");
            Assert.Equal(colorBeforeHover, colorWhileHovered);
            Assert.Equal("pointer", cursorWhileHovered);
        }
    }

    [Fact]
    public async Task HomepagePrimaryAction_RemainsProminentVisibleAndSeparateFromTheBrand()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        foreach (var viewport in new[]
                 {
                     new ViewportSize { Width = 320, Height = 720 },
                     new ViewportSize { Width = 390, Height = 844 },
                     new ViewportSize { Width = 640, Height = 390 },
                     new ViewportSize { Width = 844, Height = 390 },
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);

            var diagnostics = await MeasureHomepageHeaderAsync(page);
            Assert.True(diagnostics.ActionVisible,
                $"Homepage Play action was hidden at {viewport.Width}x{viewport.Height}.");
            Assert.Equal("PLAY THE GAME", diagnostics.ActionText);
            Assert.True(diagnostics.ActionWidth >= 44 && diagnostics.ActionHeight >= 44,
                $"Homepage Play action collapsed to {diagnostics.ActionWidth:F1}x{diagnostics.ActionHeight:F1}px at " +
                $"{viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.ActionLeft >= -0.5 && diagnostics.ActionRight <= viewport.Width + 0.5,
                $"Homepage Play action left the viewport at {viewport.Width}x{viewport.Height}.");
            Assert.False(diagnostics.ActionOverlapsBrand,
                $"Homepage Play action overlapped the brand at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.ActionBackgroundAlpha >= 0.99,
                $"Homepage Play action lost its opaque fill at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.LogoNaturalWidth > 0 && diagnostics.LogoNaturalHeight > 0,
                "Homepage logo did not decode.");
            Assert.InRange(
                Math.Abs(diagnostics.LogoRenderedAspectRatio - diagnostics.LogoNaturalAspectRatio),
                0,
                0.01);
        }
    }

    [Fact]
    public async Task HomepagePrimaryAction_RetainsInvitationCuesAndHonorsReducedMotion()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app, ReducedMotion.NoPreference);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(page);

        var action = page.Locator("body.home-page header[role=\"banner\"] .join-game:not(.scrolled-button)");
        var cues = await action.EvaluateAsync<InvitationCueDiagnostics>(
            """
            element => {
                const style = getComputedStyle(element);
                const halo = getComputedStyle(element, '::before');
                return {
                    BackgroundImage: style.backgroundImage,
                    Foreground: style.color,
                    BoxShadow: style.boxShadow,
                    AnimationName: style.animationName,
                    AnimationDuration: style.animationDuration,
                    AnimationIterationCount: style.animationIterationCount,
                    HaloDisplay: halo.display,
                    HaloPointerEvents: halo.pointerEvents,
                    PlayWeight: getComputedStyle(element.querySelector('strong')).fontWeight,
                    MiddleWeight: getComputedStyle(element.querySelector('.join-game-middle')).fontWeight,
                    GameWeight: getComputedStyle(element.querySelector('.join-game-end')).fontWeight,
                    Transform: style.transform
                };
            }
            """);

        Assert.Contains("linear-gradient", cues.BackgroundImage);
        Assert.Contains("rgb(31, 111, 53)", cues.BackgroundImage);
        Assert.Contains("rgb(46, 125, 50)", cues.BackgroundImage);
        Assert.Equal("rgb(255, 255, 255)", cues.Foreground);
        Assert.Equal("700", cues.PlayWeight);
        Assert.Equal("400", cues.MiddleWeight);
        Assert.Equal("700", cues.GameWeight);
        Assert.NotEqual("none", cues.BoxShadow);
        Assert.Equal("play-invitation", cues.AnimationName);
        Assert.Equal("0.88s", cues.AnimationDuration);
        Assert.Equal("3", cues.AnimationIterationCount);
        Assert.Equal("block", cues.HaloDisplay);
        Assert.Equal("none", cues.HaloPointerEvents);

        await action.HoverAsync();
        await page.WaitForFunctionAsync(
            """
            initialBackground => {
                const style = getComputedStyle(document.querySelector(
                    'body.home-page header[role="banner"] .join-game:not(.scrolled-button)'));
                return style.transform !== 'none' && style.backgroundImage !== initialBackground;
            }
            """,
            cues.BackgroundImage);
        var hovered = await action.EvaluateAsync<InvitationCueDiagnostics>(
            """
            element => {
                const style = getComputedStyle(element);
                return {
                    BackgroundImage: style.backgroundImage,
                    BoxShadow: style.boxShadow,
                    AnimationName: style.animationName,
                    AnimationDuration: style.animationDuration,
                    AnimationIterationCount: style.animationIterationCount,
                    Transform: style.transform
                };
            }
            """);

        Assert.NotEqual("none", hovered.Transform);
        Assert.NotEqual(cues.BackgroundImage, hovered.BackgroundImage);

        await using var reducedContext = await NewContextAsync(browser, app, ReducedMotion.Reduce);
        var reducedPage = await reducedContext.NewPageAsync();
        await reducedPage.GotoAsync("/", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await SettleLayoutAsync(reducedPage);
        var reduced = await reducedPage
            .Locator("body.home-page header[role=\"banner\"] .join-game:not(.scrolled-button)")
            .EvaluateAsync<InvitationCueDiagnostics>(
                """
                element => {
                    const style = getComputedStyle(element);
                    return {
                        BackgroundImage: style.backgroundImage,
                        BoxShadow: style.boxShadow,
                        AnimationName: style.animationName
                    };
                }
                """);

        Assert.Contains("linear-gradient", reduced.BackgroundImage);
        Assert.NotEqual("none", reduced.BoxShadow);
        Assert.Equal("none", reduced.AnimationName);
    }

}
