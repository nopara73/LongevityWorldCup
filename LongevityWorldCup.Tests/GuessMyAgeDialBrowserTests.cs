using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class GuessMyAgeDialBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(768, false)]
    [InlineData(1440, true)]
    public async Task DialFollowsTheRangeWithoutMeasuringDuringInputAndResetsOnReentry(int width, bool reduced)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/athlete/patrick-ruff?guessmyage=1", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        var slider = page.Locator("#gmaRange");
        await Assertions.Expect(slider).ToBeFocusedAsync();
        await AssertDialAsync(page, 33);
        Assert.Equal(0, await page.Locator(".gma-dial").EvaluateAsync<int>(
            "dial => dial.getAnimations({subtree:true}).filter(a => a.playState === 'running').length"));
        await slider.PressAsync("ArrowRight");
        await AssertDialAsync(page, 34);

        var reads = await slider.EvaluateAsync<int>(
            """
            range => {
                const elements = [range, range.parentElement, document.getElementById('gmaBubble'),
                    document.querySelector('.gma-dial-hand')];
                let reads = 0;
                const width = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetWidth').get;
                for (const el of elements) Object.defineProperty(el, 'offsetWidth', {
                    configurable: true, get() { reads++; return width.call(this); }
                });
                try {
                    for (const age of [1, 39, 40, 61, 74, 120, 121, 130, 63]) {
                        range.value = String(age);
                        range.dispatchEvent(new Event('input', {bubbles: true}));
                    }
                } finally {
                    for (const el of elements) delete el.offsetWidth;
                }
                return reads;
            }
            """);
        Assert.Equal(0, reads);
        await AssertDialAsync(page, 63);
        if (reduced)
            Assert.Equal(0, await page.Locator("#gmaBubble").EvaluateAsync<int>("bubble => bubble.getAnimations().length"));

        // Cached hydration can leave and restore guess mode in the same task, even for the same athlete.
        await page.Locator("#detailsModal .modal-content").EvaluateAsync(
            "modal => { modal.classList.remove('guess-mode'); modal.classList.add('guess-mode'); }");
        await Assertions.Expect(slider).ToHaveValueAsync("33");
        await AssertDialAsync(page, 33);

        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        await slider.PressAsync("End");
        Assert.True(await page.Locator("#guessAgeContainer").EvaluateAsync<bool>(
            "card => card.scrollWidth <= card.clientWidth + 1"));
    }

    private static async Task AssertDialAsync(IPage page, int age)
    {
        var state = await page.EvaluateAsync<JsonElement>(
            """
            () => {
                const ruler = getComputedStyle(document.querySelector('.gma-slider-wrap'), '::before');
                return {
                    number: Number(document.querySelector('#gmaBubble').textContent),
                    years: Number(getComputedStyle(document.querySelector('.gma-dial')).getPropertyValue('--gma-years')),
                    angle: parseFloat(getComputedStyle(document.querySelector('.gma-dial-hand')).rotate),
                    rulerVisible: ruler.content === '""' && ruler.clipPath.startsWith('inset('),
                    fraction: Number(ruler.getPropertyValue('--gma-frac'))
                };
            }
            """);
        Assert.Equal(age, state.GetProperty("number").GetInt32());
        Assert.Equal(age, state.GetProperty("years").GetInt32());
        Assert.Equal(age * 6, state.GetProperty("angle").GetInt32());
        Assert.True(state.GetProperty("rulerVisible").GetBoolean());
        Assert.Equal((age - 1) / 129d, state.GetProperty("fraction").GetDouble(), precision: 4);
    }
}
