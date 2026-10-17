using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class PlayAthleteSelectionCardBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task Selection_KeepsOnePlayerCardWithTheNameAsItsCaption(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/select-athlete");
        var input = page.Locator("#playAthleteInput");
        await Assertions.Expect(input).ToBeEditableAsync();
        await page.WaitForFunctionAsync("() => !document.body.classList.contains('play-route-hydrating')");

        const string measure = """
            () => {
                const card = document.querySelector('.play-athlete-card').getBoundingClientRect();
                const frame = document.getElementById('athleteSelectionPicture');
                const frameBox = frame.getBoundingClientRect();
                const title = document.getElementById('athleteSelectionTitle');
                const titleBox = title.getBoundingClientRect();
                const field = document.getElementById('playAthleteInput').getBoundingClientRect();
                return [card.width, card.height, frameBox.width, frameBox.height,
                    titleBox.top - frameBox.top, frameBox.bottom - titleBox.bottom,
                    parseFloat(getComputedStyle(frame).borderTopLeftRadius), Number(getComputedStyle(title).opacity),
                    field.width];
            }
            """;
        var idle = await page.EvaluateAsync<double[]>(measure);
        Assert.InRange(Math.Abs(idle[0] - idle[2]), 0, 1);
        Assert.InRange(Math.Abs(idle[2] - idle[3]), 0, 1);
        Assert.True(idle[4] > 0 && Math.Abs(idle[5]) <= 1, $"{width}px: the title should be the caption along the portrait's bottom edge.");
        Assert.True(Math.Abs(idle[0] - idle[8]) <= 1, $"{width}px: the card ({idle[0]}px) should share the field's column ({idle[8]}px).");

        await input.FillAsync("a");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator(".autocomplete-items [role=option]").First).ToBeVisibleAsync();
        var searching = await page.EvaluateAsync<double[]>(measure);
        Assert.True(searching[0] < idle[0] * 0.6, $"{width}px: the card should step aside while matches are open.");
        Assert.InRange(Math.Abs(searching[2] - searching[3]), 0, 1);
        Assert.True(searching[6] < searching[2] / 4, $"{width}px: the card keeps its rounded-square shape instead of becoming a circle.");
        Assert.Equal(0, searching[7]);

        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator(".athlete-search-open")).ToHaveCountAsync(0);
        var name = await input.InputValueAsync();
        await Assertions.Expect(page.Locator("#athleteSelectionTitle")).ToHaveTextAsync(name);
        var chosen = await page.EvaluateAsync<double[]>(measure);
        Assert.InRange(Math.Abs(chosen[0] - idle[0]), 0, 1);
        Assert.True(Math.Abs(chosen[5]) <= 1 && chosen[7] == 1, $"{width}px: the chosen athlete's name should caption their portrait.");
    }
}
