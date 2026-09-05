using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class BiomarkerOverviewBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age", 320, false)]
    [InlineData("/bortz-age", 390, true)]
    [InlineData("/pheno-age", 1280, false)]
    public async Task Overview_ShowsEnteredUnitsAndNavigatesToMissingAndExistingFields(string path, int width, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenAsync(context, path);
        var inputs = page.Locator(".biomarker-card input[type=number][required]");
        var first = inputs.First;
        var second = inputs.Nth(1);
        await page.Locator(".bioage-next-entry").ClickAsync();
        await Assertions.Expect(first).ToBeFocusedAsync();
        await first.FillAsync("5.5");
        await page.Locator(".bioage-next-entry").ClickAsync();
        await Assertions.Expect(second).ToBeFocusedAsync();
        await page.Locator(".bioage-entry-overview summary").ClickAsync();
        Assert.Equal(await inputs.CountAsync(), await page.Locator(".bioage-entry-list li:visible").CountAsync());
        var firstRow = page.Locator($"[data-entry-field='{await first.GetAttributeAsync("id")}']");
        Assert.Contains("5.5", await firstRow.InnerTextAsync());
        await firstRow.Locator("button").ClickAsync();
        await Assertions.Expect(first).ToBeFocusedAsync();

        var albuminRow = page.Locator("[data-entry-field=albumin]");
        await albuminRow.Locator("button").ClickAsync();
        await Assertions.Expect(page.Locator("#albumin")).ToBeFocusedAsync();
        await page.Locator("#albuminUnit").SelectOptionAsync(new SelectOptionValue { Label = "g/dL" });
        await page.Locator("#albumin").FillAsync("4.4");
        await Assertions.Expect(albuminRow.Locator(".bioage-entry-value")).ToHaveTextAsync("4.4 g/dL");
        await page.Locator("#albumin").BlurAsync();
        await page.ReloadAsync();
        await page.Locator(".bioage-entry-overview summary").ClickAsync();
        await Assertions.Expect(albuminRow.Locator(".bioage-entry-value")).ToHaveTextAsync("4.4 g/dL");
        Assert.Equal("4.4", await page.Locator("#albumin").InputValueAsync());
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth>innerWidth"));
    }

    [Theory]
    [InlineData("/pheno-age?update=1")]
    [InlineData("/bortz-age?update=1")]
    public async Task PartialUpdates_ReviewOnlyNewValuesAndPreserveBelowDetectionMeaning(string path)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("""
            sessionStorage.setItem('selectedAthlete', JSON.stringify({Name:'Overview Test',DisplayName:'Overview Test',DateOfBirth:{Year:1980,Month:5,Day:20},Biomarkers:[{Date:'2026-01-01',AlbGL:45,CrpMgL:1}]}));
            """);
        var page = await OpenAsync(context, path);
        await page.Locator(".bioage-entry-overview summary").ClickAsync();
        Assert.True(await page.Locator(".bioage-entry-empty").IsVisibleAsync());
        Assert.Equal(0, await page.Locator(".bioage-entry-list li:visible").CountAsync());
        await page.Locator("#blood-draw-date").FillAsync("2026-09-01");
        await page.Locator("#albumin").FillAsync("46");
        Assert.True(await page.Locator("#calculateBioageButton").IsEnabledAsync());
        Assert.Equal(1, await page.Locator(".bioage-entry-list li:visible").CountAsync());
        await page.Locator("label[for=crp-negative]").ClickAsync();
        var crpRow = page.Locator("[data-entry-field=crp]");
        await Assertions.Expect(crpRow.Locator(".bioage-entry-value")).ToHaveTextAsync("Below detection limit");
        await crpRow.Locator("button").ClickAsync();
        await Assertions.Expect(page.Locator("#crp-negative")).ToBeFocusedAsync();
        Assert.True(await page.Locator("#crp").IsDisabledAsync());
        await page.Keyboard.PressAsync("Space");
        await page.Locator("#crp").FillAsync("");
        await Assertions.Expect(crpRow).ToBeHiddenAsync();
        Assert.True(await page.Locator("#calculateBioageButton").IsEnabledAsync());
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context, string path)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        var page = await context.NewPageAsync();
        await page.GotoAsync(path);
        if (!path.Contains("update=1"))
        {
            await page.Locator("#dob-year").SelectOptionAsync("1980");
            await page.Locator("#blood-draw-date").FillAsync("2026-09-01");
            await page.Locator("#lwcToStep2Btn").ClickAsync();
        }
        await page.Locator(".bioage-next-entry").WaitForAsync();
        return page;
    }
}
