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
    [InlineData(390, 844, 200)]
    [InlineData(820, 1180, 200)]
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
        Assert.Equal(count, await page.Locator(".ruleset-faq-answer[hidden='until-found']").CountAsync());
        Assert.True(await page.Locator(".ruleset-faq-answer").EvaluateAllAsync<bool>(
            "answers => answers.every(answer => getComputedStyle(answer).display !== 'none' && getComputedStyle(answer).contentVisibility === 'hidden')"));
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
        await page.Locator("#from-which-biomarkers-can-i-calculate-my-bortz-age ~ .ruleset-faq-toggle").ClickAsync();
        Assert.True(await page.Locator(".ruleset-faq-item").EvaluateAllAsync<bool>(
            "items => items.every(item => item.scrollWidth <= item.clientWidth + 1)"), $"{scenario}: a question or answer is clipped.");
        Assert.True(await page.EvaluateAsync<bool>("Math.max(document.documentElement.scrollWidth, document.body.scrollWidth) <= innerWidth + 1"),
            $"{scenario}: content overflows even when the root clips it.");
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

    [Fact]
    public async Task Faq_NativeFragmentRevealSynchronizesToggleAndCanBeClosedAgain()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.EvaluateAsync("""
            () => {
                window.faqBeforematches = 0;
                document.addEventListener('beforematch', () => window.faqBeforematches++);
                location.hash = 'how-can-i-cheat-answer';
            }
            """);
        await page.WaitForFunctionAsync("() => window.faqBeforematches > 0");
        var toggle = page.Locator("#how-can-i-cheat ~ .ruleset-faq-toggle");
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
        Assert.Null(await page.Locator("#how-can-i-cheat-answer").GetAttributeAsync("hidden"));
        await toggle.ClickAsync();
        await page.WaitForFunctionAsync("() => document.getElementById('how-can-i-cheat-answer').getAttribute('hidden') === 'until-found'");
        Assert.Equal("until-found", await page.Locator("#how-can-i-cheat-answer").GetAttributeAsync("hidden"));

        await page.EvaluateAsync("location.hash = 'what-will-sponsorships-include'");
        await page.WaitForFunctionAsync("() => document.querySelector('#what-will-sponsorships-include-answer:not([hidden])')");
        await page.GoBackAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#how-can-i-cheat-answer:not([hidden])')");
        Assert.Equal("true", await toggle.GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task Faq_InitializationIsIdempotentAndCollapsedAnswersLeaveKeyboardOrder()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await page.EvaluateAsync("document.dispatchEvent(new Event('DOMContentLoaded'))");
        Assert.Equal(await page.Locator(".ruleset-faq-item").CountAsync(), await page.Locator(".ruleset-faq-toggle").CountAsync());
        var registrationToggle = page.Locator("#how-do-i-register-for-the-competition ~ .ruleset-faq-toggle");
        await registrationToggle.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        Assert.Equal("can-i-withdraw-from-the-competition-answer", await page.Locator(":focus").GetAttributeAsync("aria-controls"));
        await registrationToggle.ClickAsync();
        await page.Locator("#how-do-i-register-for-the-competition-answer a").First.FocusAsync();
        await page.Locator("#how-do-i-register-for-the-competition").ClickAsync();
        Assert.True(await registrationToggle.EvaluateAsync<bool>("toggle => toggle === document.activeElement"));
        Assert.Equal("false", await registrationToggle.GetAttributeAsync("aria-expanded"));
    }

    [Fact]
    public async Task Faq_QuestionLinksStillNavigateAndBiomarkerListsKeepEveryMarker()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        var question = page.Locator("#what-is-bortz-age");
        await context.RouteAsync("https://www.nature.com/**", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<p>Study</p>"
        }));
        var popup = await page.RunAndWaitForPopupAsync(() => question.Locator("a").ClickAsync());
        Assert.Contains("s42003-023-05456-z", popup.Url);
        Assert.Equal("false", await page.Locator("#what-is-bortz-age ~ .ruleset-faq-toggle").GetAttributeAsync("aria-expanded"));
        await popup.CloseAsync();
        Assert.Equal(9, await page.Locator("#from-which-biomarkers-can-i-calculate-my-pheno-age-answer .ruleset-term-list li").CountAsync());
        Assert.Equal(21, await page.Locator("#from-which-biomarkers-can-i-calculate-my-bortz-age-answer .ruleset-term-list li").CountAsync());
        Assert.Equal(new[] { "/about", "/history", "/leaderboard" },
            await page.Locator(".ruleset-next a").EvaluateAllAsync<string[]>("links => links.map(link => link.getAttribute('href'))"));
    }

    [Fact]
    public async Task Faq_WithoutScriptsKeepsAllAnswersAndNavigationReadable()
    {
        await using var context = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/ruleset");
        var answers = page.Locator(".ruleset-faq-answer");
        Assert.True(await answers.CountAsync() > 10);
        Assert.Equal(0, await page.Locator(".ruleset-faq-answer[hidden], .ruleset-faq-toggle").CountAsync());
        for (var i = 0; i < await answers.CountAsync(); i++)
            Assert.True(await answers.Nth(i).IsVisibleAsync());
    }

    [Fact]
    public async Task Faq_WithoutNativeRevealSupportKeepsAnswersSearchable()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await context.AddInitScriptAsync("delete HTMLElement.prototype.onbeforematch");
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        Assert.False(await page.EvaluateAsync<bool>("'onbeforematch' in document.body"));
        Assert.Equal(0, await page.Locator(".ruleset-faq-answer[hidden], .ruleset-faq-toggle").CountAsync());
        Assert.Equal(20, await page.Locator(".ruleset-faq-answer:visible").CountAsync());
    }
}
