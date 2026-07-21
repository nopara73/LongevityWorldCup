using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class BioageRankPreviewBrowserTests
{
    [Fact]
    public async Task RankPreview_RetryRecoversWithoutNestedLiveRegionsOrStaleState()
    {
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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
        await page.GotoAsync("/pheno-age", new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => Boolean(window.LwcBioAgeRankPreview)");
        await page.EvaluateAsync(
            """
            () => {
                document.body.classList.add('bioage-result-ready');
                document.getElementById('phenoAgeResult')?.classList.add('show');
                return window.LwcBioAgeRankPreview.render('phenoAgeRankPreview', {
                    clock: 'pheno',
                    ageReduction: -5,
                    dateOfBirth: new Date(1990, 0, 1)
                });
            }
            """);

        var preview = page.Locator("#phenoAgeRankPreview");
        var error = preview.Locator(".bioage-rank-error");
        await error.WaitForAsync();

        Assert.Null(await preview.GetAttributeAsync("role"));
        Assert.Null(await preview.GetAttributeAsync("aria-atomic"));
        Assert.Null(await preview.GetAttributeAsync("aria-live"));
        Assert.Equal("false", await preview.GetAttributeAsync("aria-busy"));
        Assert.Equal("alert", await error.GetAttributeAsync("role"));
        Assert.True(await error.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retry" }).IsVisibleAsync());

        await error.GetByRole(AriaRole.Button, new LocatorGetByRoleOptions { Name = "Retry" }).ClickAsync();
        await preview.Locator(".bioage-rank-summary").WaitForAsync();

        Assert.Equal(2, await page.EvaluateAsync<int>("() => window.__rankPreviewCalls"));
        Assert.Equal("status", await preview.GetAttributeAsync("role"));
        Assert.Equal("true", await preview.GetAttributeAsync("aria-atomic"));
        Assert.Equal("polite", await preview.GetAttributeAsync("aria-live"));
        Assert.Equal("false", await preview.GetAttributeAsync("aria-busy"));
        Assert.Contains("#1", await preview.InnerTextAsync());
        Assert.Equal(0, await error.CountAsync());

        await page.EvaluateAsync("() => window.LwcBioAgeRankPreview.clear('phenoAgeRankPreview')");
        Assert.True(await preview.IsHiddenAsync());
        Assert.Null(await preview.GetAttributeAsync("role"));
        Assert.Null(await preview.GetAttributeAsync("aria-atomic"));
        Assert.Equal("polite", await preview.GetAttributeAsync("aria-live"));
        Assert.Null(await preview.GetAttributeAsync("aria-busy"));
        Assert.Empty(errors);
    }
}
