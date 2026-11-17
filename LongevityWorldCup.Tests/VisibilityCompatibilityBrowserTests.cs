using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class VisibilityCompatibilityBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(false, 390, ColorScheme.Light, ReducedMotion.NoPreference)]
    [InlineData(true, 390, ColorScheme.Dark, ReducedMotion.Reduce)]
    [InlineData(true, 1280, ColorScheme.Light, ReducedMotion.NoPreference)]
    public async Task FlagProfiles_LoadAndNavigateOnlyTheExpandedTeam(
        bool missingVisibility, int width, ColorScheme theme, ReducedMotion motion)
    {
        await using var context = await NewContextAsync(missingVisibility, width, theme, motion);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/league/pheno?group=flags");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        var team = page.Locator(".flag-team").First;
        await team.Locator("summary").ClickAsync();
        var links = team.Locator(".flag-athlete-link");
        var slugs = await links.EvaluateAllAsync<string[]>("links => links.map(link => new URL(link.href).pathname.split('/').pop())");
        Assert.Equal(3, slugs.Length);
        await links.First.ClickAsync();
        var modal = page.Locator("#detailsModal .modal-content");
        for (var index = 0; index < slugs.Length; index++)
        {
            await Assertions.Expect(modal).ToHaveAttributeAsync("data-athlete-slug", slugs[index]);
            await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
            Assert.False(await modal.EvaluateAsync<bool>("e => e.classList.contains('has-load-error')"));
            await Assertions.Expect(page.Locator("#athleteLoadError")).ToBeHiddenAsync();
            var next = page.Locator(".athlete-step[data-step=next]:visible").First;
            if (index + 1 < slugs.Length)
            {
                await Assertions.Expect(next).ToBeEnabledAsync();
                await next.ClickAsync();
            }
            else await Assertions.Expect(next).ToBeDisabledAsync();
        }
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/ruleset", "img[data-lwc-viewable]:visible", 390, ColorScheme.Dark)]
    [InlineData("/athlete/martin-helstab", "#proofsGallery img", 1280, ColorScheme.Light)]
    public async Task ImageViewer_OpensAndWrapsKeyboardFocusWithoutNativeVisibility(
        string path, string imageSelector, int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(true, width, theme, ReducedMotion.Reduce);
        await context.RouteAsync("https://github.com/user-attachments/assets/**", route => route.FulfillAsync(new()
        {
            ContentType = "image/svg+xml",
            Body = "<svg xmlns='http://www.w3.org/2000/svg' width='1200' height='800'><rect width='1200' height='800' fill='#e8efe9'/></svg>"
        }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(path);
        if (path.StartsWith("/athlete/"))
            await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
        var opener = page.Locator(imageSelector).First;
        await opener.ClickAsync();
        var viewer = page.Locator("dialog.lwc-viewer");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-state", "open");
        await Assertions.Expect(viewer).ToHaveAttributeAsync("data-image-state", "ready");
        // Use the browser's rendered controls, independently of the helper under test.
        var controls = viewer.Locator("button:enabled:visible, [tabindex='0']:visible");
        await controls.Last.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(controls.First).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(controls.Last).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(viewer).ToBeHiddenAsync();
        await Assertions.Expect(opener).ToBeFocusedAsync();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task OptionalMotion_KeepsNumbersAndPortraitsWithoutSchedulingUnsupportedEffects()
    {
        await using var context = await NewContextAsync(true, motion: ReducedMotion.NoPreference);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/");
        await Assertions.Expect(page.Locator("#leaderboardStatus")).ToHaveTextAsync("Leaderboard loaded.");
        await page.WaitForFunctionAsync("() => window.LwcCharts && window.LwcCelebrate");
        Assert.True(await page.EvaluateAsync<bool>("""
            async () => {
                const figure = document.createElement('span');
                figure.textContent = '1,234';
                document.body.append(figure);
                window.LwcCharts.count(figure);
                const started = window.LwcCelebrate.play(figure, { once: 'visibility-regression' });
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                const unchanged = figure.textContent === '1,234' && !figure.hasAttribute('data-lwc-count')
                    && !started && !window.LwcCelebrate.seen('visibility-regression');
                figure.remove();
                return unchanged;
            }
            """));
        var portrait = page.Locator(".podium img.podium-portrait").First;
        await Assertions.Expect(portrait).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    private async Task<IBrowserContext> NewContextAsync(bool missingVisibility, int width = 1280,
        ColorScheme theme = ColorScheme.Light, ReducedMotion motion = ReducedMotion.Reduce)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ColorScheme = theme, ReducedMotion = motion,
            ViewportSize = new() { Width = width, Height = 900 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true')");
        if (missingVisibility)
            await context.AddInitScriptAsync("Object.defineProperty(Element.prototype, 'checkVisibility', { configurable: true, writable: true, value: undefined });");
        return context;
    }
}
