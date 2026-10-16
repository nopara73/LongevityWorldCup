using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class HelstabChallengeBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task DarkTheme_KeepsEveryDemoFieldAsLightAsItsCard(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ColorScheme = ColorScheme.Dark,
            ViewportSize = new() { Width = width, Height = height }
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/helstab-kihivas");

        var backgrounds = await page.EvaluateAsync<string[]>(
            """
            () => ['#helstabSignupForm', '#helstabName', '#helstabEmail', '#helstabGoal']
                .map(selector => getComputedStyle(document.querySelector(selector)).backgroundColor)
            """);
        Assert.All(backgrounds.Skip(1), background => Assert.Equal(backgrounds[0], background));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    public async Task PhoneHero_ShowsThePortraitAboveTheTitle(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/helstab-kihivas");
        await page.EvaluateAsync("() => document.fonts.ready");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const rect = selector => document.querySelector(selector).getBoundingClientRect();
                const hero = rect('.helstab-hero'), kicker = rect('.helstab-kicker'), facts = rect('.helstab-hero-facts');
                return [hero.top, hero.bottom, kicker.top, facts.bottom, innerWidth];
            }
            """);
        Assert.True(layout[2] - layout[0] >= layout[4] * 0.4,
            $"{width}x{height}: the title copy starts {layout[2] - layout[0]}px into the hero, covering Martin's portrait.");
        Assert.True(layout[3] <= layout[1] + 1, $"{width}x{height}: the challenge facts should stay inside the hero.");
    }
}
