using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class HomepageChromeRegressionBrowserTests
{
    [Fact]
    public async Task NewsletterSubscribeAction_HasOpaqueHighContrastFillAcrossResponsiveLayouts()
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
                     new ViewportSize { Width = 1026, Height = 505 },
                     new ViewportSize { Width = 1280, Height = 720 }
                 })
        {
            await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
            await SettleLayoutAsync(page);
            var diagnostics = await page.Locator(".enhanced-subscribe-btn").EvaluateAsync<FilledActionDiagnostics>(
                MeasureFilledActionScript);

            Assert.True(diagnostics.Visible,
                $"Subscribe action was hidden at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.Enabled,
                $"Subscribe action was unexpectedly disabled at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.BackgroundAlpha >= 0.99,
                $"Subscribe action background was {diagnostics.Background} at {viewport.Width}x{viewport.Height}.");
            Assert.True(diagnostics.TextContrast >= 4.5,
                $"Subscribe text contrast was only {diagnostics.TextContrast:F2}:1 at " +
                $"{viewport.Width}x{viewport.Height}; foreground={diagnostics.Foreground}, " +
                $"background={diagnostics.Background}.");
            Assert.True(diagnostics.Width >= 44 && diagnostics.Height >= 44,
                $"Subscribe action collapsed to {diagnostics.Width:F1}x{diagnostics.Height:F1}px at " +
                $"{viewport.Width}x{viewport.Height}.");
        }
    }

    [Fact]
    public async Task CompactPageHeaders_PreserveTheLogoAndFullPrimaryActionWhenSpaceAllows()
    {
        var app = App;
        var browser = Browser;
        await using var context = await NewContextAsync(browser, app);
        var page = await context.NewPageAsync();

        foreach (var path in new[] { "/leaderboard", "/ruleset", "/history" })
        {
            await page.SetViewportSizeAsync(464, 800);
            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            foreach (var viewport in new[]
                     {
                         new ViewportSize { Width = 464, Height = 800 },
                         new ViewportSize { Width = 464, Height = 300 }
                     })
            {
                await page.SetViewportSizeAsync(viewport.Width, viewport.Height);
                await SettleLayoutAsync(page);

                var diagnostics = await page.EvaluateAsync<CompactHeaderDiagnostics>(
                    """
                    () => {
                        const header = document.querySelector('header[role="banner"]');
                        const brand = header.querySelector('.header-link');
                        const logo = brand.querySelector('.main-logo-image');
                        const action = header.querySelector('.join-game:not(.scrolled-button)');
                        const brandRect = brand.getBoundingClientRect();
                        const logoRect = logo.getBoundingClientRect();
                        const actionRect = action.getBoundingClientRect();
                        return {
                            ActionText: action.innerText.trim().replace(/\s+/g, ' '),
                            ActionLeft: actionRect.left,
                            ActionRight: actionRect.right,
                            BrandLeft: brandRect.left,
                            BrandRight: brandRect.right,
                            LogoWidth: logoRect.width,
                            LogoRenderedAspectRatio: logoRect.width / logoRect.height,
                            LogoNaturalAspectRatio: logo.naturalWidth / logo.naturalHeight,
                            HasHorizontalOverflow: document.documentElement.scrollWidth
                                > document.documentElement.clientWidth
                        };
                    }
                    """);

                Assert.Equal("PLAY THE GAME", diagnostics.ActionText);
                Assert.True(diagnostics.LogoWidth >= 44,
                    $"{path} logo collapsed to {diagnostics.LogoWidth:F1}px at " +
                    $"{viewport.Width}x{viewport.Height}.");
                Assert.InRange(
                    Math.Abs(diagnostics.LogoRenderedAspectRatio - diagnostics.LogoNaturalAspectRatio),
                    0,
                    0.01);
                Assert.True(diagnostics.ActionLeft >= diagnostics.BrandRight,
                    $"{path} brand and Play action overlapped at {viewport.Width}x{viewport.Height}.");
                Assert.True(diagnostics.BrandLeft >= -0.5 && diagnostics.ActionRight <= viewport.Width + 0.5,
                    $"{path} compact header left the viewport at {viewport.Width}x{viewport.Height}.");
                Assert.False(diagnostics.HasHorizontalOverflow,
                    $"{path} overflowed horizontally at {viewport.Width}x{viewport.Height}.");
            }
        }
    }

}
