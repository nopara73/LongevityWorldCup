using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class RulesetReferenceBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(320, 640, 200)]
    public async Task Faq_ListsQuestionsThatOpenInPlace(int width, int height, int textPercent)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.EvaluateAsync($"document.documentElement.style.fontSize = '{textPercent}%'");
        var scenario = $"{width}x{height} at {textPercent}%";

        var items = page.Locator(".ruleset-faq-item");
        var count = await items.CountAsync();
        Assert.True(count > 10, $"{scenario}: the FAQ questions should be listed.");
        Assert.Equal(0, await page.Locator(".ruleset-faq-answer:visible").CountAsync());
        Assert.Equal(count, await page.Locator(".ruleset-faq-toggle[aria-expanded='false']").CountAsync());

        // Every rule heading carries its number, and the contents list repeats it.
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const rules = [...document.querySelectorAll('.ruleset-rule > h2')];
                const links = [...document.querySelectorAll('.documentation-nav-rule')];
                const numbered = element => /^counter\(/.test(getComputedStyle(element, '::before').content);
                return rules.length === 4 && links.length === 4 &&
                    rules.every(heading => numbered(heading) && parseFloat(getComputedStyle(heading, '::before').width) >= 30) &&
                    links.every(numbered);
            }
            """), $"{scenario}: rules should be numbered.");

        var toggle = page.Locator("#how-can-i-cheat ~ .ruleset-faq-toggle");
        var toggleBox = await toggle.BoundingBoxAsync();
        Assert.NotNull(toggleBox);
        Assert.True(toggleBox.Width >= 44 && toggleBox.Height >= 44, $"{scenario}: the toggle measures {toggleBox.Width}x{toggleBox.Height}.");
        await toggle.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.True(await page.Locator("#how-can-i-cheat-answer").IsVisibleAsync(), $"{scenario}: Enter should open the answer.");
        await page.Keyboard.PressAsync("Space");
        Assert.Equal("false", await toggle.GetAttributeAsync("aria-expanded"));

        await page.Locator("#what-if-theres-a-tie").ClickAsync();
        Assert.True(await page.Locator("#what-if-theres-a-tie-answer").IsVisibleAsync(), $"{scenario}: clicking a question should open its answer.");
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth + 1"), $"{scenario}: the page scrolls sideways.");
    }

    [Fact]
    public async Task Faq_LinkToAQuestionOpensAndShowsItsAnswer()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset#how-can-i-cheat");
        await page.WaitForFunctionAsync("() => document.getElementById('how-can-i-cheat-answer')?.checkVisibility()");
        var answer = await page.Locator("#how-can-i-cheat-answer").BoundingBoxAsync();
        Assert.NotNull(answer);
        Assert.True(answer.Y > 52 && answer.Y + answer.Height < 844, $"The linked answer is off screen at y={answer.Y}.");

        await page.EvaluateAsync("location.hash = '#what-will-sponsorships-include'");
        await page.WaitForFunctionAsync("() => document.getElementById('what-will-sponsorships-include-answer')?.checkVisibility()");
    }
}
