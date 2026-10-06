using Microsoft.Playwright;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class ScoreXrayBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Fact]
    public async Task Walkthrough_UsesKeyboardDeepLinksAndDistinctClockPathsOnMobileWithReducedMotion()
    {
        await using var context = await ContextAsync(mobile: true);
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        using var client = App.CreateClient();
        var field = JsonSerializer.Deserialize<XrayDirectory>(await client.GetStringAsync("/api/score-xray"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var pro = field.Athletes.First(a => a.IsPro);
        await page.GotoAsync($"/score-xray?athlete={pro.Slug}&clock=pheno&step=4");
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("Inside the clock.");
        await Assertions.Expect(page.Locator("#phenoClock")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.Locator("#stageBody")).ToContainTextAsync("Weighted model terms · not years");
        var node = page.Locator("[data-step='3']");
        await node.FocusAsync();
        await node.PressAsync("ArrowRight");
        await Assertions.Expect(page.Locator("[data-step='4']")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("One subtraction. The score.");
        Assert.Contains("step=5", page.Url);
        await page.Locator("[data-step='4']").PressAsync("End");
        await Assertions.Expect(page.Locator("#stageBody")).ToContainTextAsync("Pheno Age League");
        await page.Locator("#bortzClock").ClickAsync();
        await Assertions.Expect(page.Locator("#bortzClock")).ToHaveAttributeAsync("aria-pressed", "true");
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("Start with the actual test.");
        Assert.Equal(22, await page.Locator(".lab-value").CountAsync());
        await page.Locator("[data-step='3']").ClickAsync();
        await Assertions.Expect(page.Locator("#stageBody")).ToContainTextAsync("Contributions to age acceleration · years");
        await page.Locator(".term").First.Locator("summary").ClickAsync();
        await Assertions.Expect(page.Locator(".term").First).ToHaveAttributeAsync("open", "");
        Assert.Equal("none", await page.Locator(".pipe-node[aria-current='step']").EvaluateAsync<string>("el => getComputedStyle(el,'::before').animationName"));
        Assert.True(await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await page.GoBackAsync();
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("Start with the actual test.");
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Playback_StopsAtTheLastStepAndManualClockChangesCancelIt()
    {
        await using var context = await ContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync("/score-xray?athlete=michael_lustgarten&clock=pheno&step=5");
        await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        await page.Clock.InstallAsync();
        await page.Locator("#playWalkthrough").ClickAsync();
        await Assertions.Expect(page.Locator("#playWalkthrough")).ToHaveAttributeAsync("aria-pressed", "true");
        await page.Clock.FastForwardAsync(6501);
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("Now it has a place.");
        await Assertions.Expect(page.Locator("#playWalkthrough")).ToHaveAttributeAsync("aria-pressed", "false");
        await page.Clock.FastForwardAsync(65000);
        await Assertions.Expect(page.Locator("#stepPosition")).ToHaveTextAsync("6 of 6");
        await page.Locator("#playWalkthrough").ClickAsync();
        await Assertions.Expect(page.Locator("#stepPosition")).ToHaveTextAsync("1 of 6");
        await page.Locator("#phenoClock").ClickAsync();
        await page.Clock.FastForwardAsync(65000);
        await Assertions.Expect(page.Locator("#stepPosition")).ToHaveTextAsync("1 of 6");
        await Assertions.Expect(page.Locator("#playWalkthrough")).ToHaveAttributeAsync("aria-pressed", "false");
    }

    [Fact]
    public async Task SearchAndCopyFailure_StayUsableAndNeverClaimAnUncopiedLink()
    {
        await using var context = await ContextAsync();
        await context.AddInitScriptAsync("Object.defineProperty(navigator,'clipboard',{value:{writeText:()=>Promise.reject(new Error('denied'))},configurable:true});");
        var page = await context.NewPageAsync();
        await page.GotoAsync("/score-xray?athlete=michael_lustgarten&clock=pheno&step=1");
        await Assertions.Expect(page.Locator("#workspace")).ToBeVisibleAsync();
        var search = page.Locator("#athleteSearch");
        await search.FillAsync("Egorov");
        await search.PressAsync("ArrowDown");
        await Assertions.Expect(search).ToHaveAttributeAsync("aria-activedescendant", "athlete-option-0");
        await search.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteName")).ToHaveTextAsync("Michael Egorov");
        await Assertions.Expect(page.Locator("#searchResults")).ToBeHiddenAsync();
        await page.Locator("[data-step='4']").ClickAsync();
        await page.Locator("#copyLink").ClickAsync();
        await Assertions.Expect(page.Locator("#shareStatus")).ToContainTextAsync("Copy access wasn’t available");
        await Assertions.Expect(page.Locator("#copyFallback")).ToHaveValueAsync(page.Url);
        await Assertions.Expect(page.Locator("#copyFallback")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#copyFallback").EvaluateAsync<bool>("el => el.selectionStart === 0 && el.selectionEnd === el.value.length"));
    }

    [Fact]
    public async Task FailedDataAndEvidenceRequests_CanRecoverWithoutLosingTheRequestedView()
    {
        await using var context = await ContextAsync();
        var failApi = true;
        await context.RouteAsync("**/api/score-xray/michael_lustgarten", async route =>
        {
            if (failApi) { failApi = false; await route.FulfillAsync(new RouteFulfillOptions { Status = 503 }); }
            else await route.ContinueAsync();
        });
        var failProof = true;
        await context.RouteAsync("**/athletes/**", async route =>
        {
            if (route.Request.ResourceType == "image" && route.Request.Url.Contains("/proof_", StringComparison.OrdinalIgnoreCase))
            {
                if (failProof) { failProof = false; await route.AbortAsync(); }
                else await route.FulfillAsync(new RouteFulfillOptions { ContentType = "image/png", BodyBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jT1sAAAAASUVORK5CYII=") });
            }
            else await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/score-xray?athlete=michael_lustgarten&clock=pheno&step=3");
        await Assertions.Expect(page.Locator("#loadError")).ToBeVisibleAsync();
        await page.Locator("#retryLoad").ClickAsync();
        await Assertions.Expect(page.Locator("#stageTitle")).ToHaveTextAsync("Where the scoring limits kick in.");
        await page.Locator("#openProofs").ClickAsync();
        await Assertions.Expect(page.Locator("#proofDialog")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#retryProof")).ToBeVisibleAsync();
        await page.Locator("#retryProof").ClickAsync();
        await Assertions.Expect(page.Locator("#proofImage")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#proofStatus")).ToHaveTextAsync("");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#proofDialog")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#openProofs")).ToBeFocusedAsync();
        await page.GotoAsync("/score-xray?athlete=unknown&clock=pheno&step=6");
        await Assertions.Expect(page.Locator("#loadErrorText")).ToContainTextAsync("not in the current published field");
        await page.Locator("#athleteSearch").FillAsync("Lustgarten");
        await page.Locator("#athleteSearch").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#athleteName")).ToContainTextAsync("Michael Lustgarten");
    }

    private async Task<IBrowserContext> ContextAsync(bool mobile = false)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString(), Locale = "en-US", ReducedMotion = ReducedMotion.Reduce,
            ViewportSize = new ViewportSize { Width = mobile ? 390 : 1440, Height = mobile ? 844 : 1000 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        return context;
    }
}
