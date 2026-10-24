using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadA)]
public sealed class BioageRankPreviewBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", "phenoAgeRankPreview", "pheno")]
    [InlineData("/bortz-age", "bortzAgeRankPreview", "bortz")]
    public async Task RankPreview_RetryRecoversWithoutNestedLiveRegionsOrStaleState(string path, string target, string clock)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-US",
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            window.__rankPreviewCalls = 0;
            window.getSharedAthletes = () => {
                window.__rankPreviewCalls += 1;
                return window.__rankPreviewCalls === 1
                    ? Promise.reject(new Error('simulated rank preview failure'))
                    : Promise.resolve([]);
            };
            """);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, path);
        await page.WaitForFunctionAsync("() => Boolean(window.LwcBioAgeRankPreview)");
        await page.EvaluateAsync(
            """
            ({target, clock}) => {
                document.documentElement.style.fontSize = '200%';
                document.body.classList.add('bioage-result-ready');
                document.querySelector('.bioageform[id$="AgeResult"]')?.classList.add('show');
                return window.LwcBioAgeRankPreview.render(target, {
                    clock,
                    ageReduction: -5,
                    dateOfBirth: new Date(1990, 0, 1)
                });
            }
            """, new { target, clock });

        var preview = page.Locator($"#{target}");
        var error = preview.Locator(".bioage-rank-error");
        await error.WaitForAsync();

        Assert.Null(await preview.GetAttributeAsync("role"));
        Assert.Null(await preview.GetAttributeAsync("aria-atomic"));
        Assert.Null(await preview.GetAttributeAsync("aria-live"));
        Assert.Equal("false", await preview.GetAttributeAsync("aria-busy"));
        Assert.Equal("alert", await error.GetAttributeAsync("role"));
        var retry = error.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retry" });
        await Assertions.Expect(retry).ToBeVisibleAsync();
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await preview.Locator(".bioage-rank-summary").WaitForAsync();

        Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__rankPreviewCalls"));
        Assert.Equal("status", await preview.GetAttributeAsync("role"));
        Assert.Equal("true", await preview.GetAttributeAsync("aria-atomic"));
        Assert.Equal("polite", await preview.GetAttributeAsync("aria-live"));
        Assert.Equal("false", await preview.GetAttributeAsync("aria-busy"));
        Assert.Contains("#1", await preview.InnerTextAsync());
        Assert.Equal(0, await error.CountAsync());
        var placeholder = await page.Locator("meta[name='lwc-athlete-placeholder']").GetAttributeAsync("content");
        Assert.StartsWith("/assets/content-images/play-athlete-placeholder.webp?v=", placeholder);
        await Assertions.Expect(preview.Locator(".bioage-rank-row.current img")).ToHaveAttributeAsync("src", placeholder!);

        await page.EvaluateAsync("target => window.LwcBioAgeRankPreview.clear(target)", target);
        Assert.True(await preview.IsHiddenAsync());
        Assert.Null(await preview.GetAttributeAsync("role"));
        Assert.Null(await preview.GetAttributeAsync("aria-atomic"));
        Assert.Equal("polite", await preview.GetAttributeAsync("aria-live"));
        Assert.Null(await preview.GetAttributeAsync("aria-busy"));
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData("/pheno-age", "phenoAgeRankPreview", "pheno")]
    [InlineData("/bortz-age", "bortzAgeRankPreview", "bortz")]
    public async Task RankPreview_LateResponsesCannotReplaceRecalculationOrReopenClearedRank(string path, string target, string clock)
    {
        await using var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(),
            ViewportSize = new() { Width = 320, Height = 844 }
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.AddInitScriptAsync(
            """
            window.__rankResolvers = [];
            window.getSharedAthletes = () => new Promise(resolve => window.__rankResolvers.push(resolve));
            """);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        await page.WaitForFunctionAsync("() => Boolean(window.LwcBioAgeRankPreview)");
        await page.EvaluateAsync(
            """
            ({target, clock}) => {
                document.querySelector('.bioageform[id$="AgeResult"]').classList.add('show');
                window.__rankOptions = {clock, ageReduction: -5, dateOfBirth: new Date(1980, 0, 1)};
                void window.LwcBioAgeRankPreview.render(target, window.__rankOptions);
            }
            """, new { target, clock });
        var preview = page.Locator($"#{target}");
        await Assertions.Expect(preview).ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(preview.Locator(".bioage-rank-loading")).ToHaveTextAsync("Calculating rank...");
        await page.EvaluateAsync(
            """
            target => {
                void window.LwcBioAgeRankPreview.render(target, {...window.__rankOptions, ageReduction: 4});
                window.__rankResolvers[1]([]);
            }
            """, target);
        await Assertions.Expect(preview.Locator(".current .bioage-rank-row-score")).ToHaveTextAsync("+4.0y");
        await page.EvaluateAsync("window.__rankResolvers[0]([])");
        await Assertions.Expect(preview.Locator(".current .bioage-rank-row-score")).ToHaveTextAsync("+4.0y");
        await page.EvaluateAsync(
            """
            target => {
                void window.LwcBioAgeRankPreview.render(target, window.__rankOptions);
                window.LwcBioAgeRankPreview.clear(target);
                window.__rankResolvers[2]([]);
            }
            """, target);
        await Assertions.Expect(preview).ToBeHiddenAsync();
        await Assertions.Expect(preview).ToBeEmptyAsync();
        Assert.Null(await preview.GetAttributeAsync("aria-busy"));
    }
}
