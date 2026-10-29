using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class LabAccessPanelTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task RenderedBioagePagesUseCloudflareCountryHeaderForLabAccess(string path)
    {
        using var client = App.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", "NZ");

        var html = await client.GetStringAsync(path);

        Assert.Contains("const REQUEST_COUNTRY_CODE = 'NZ';", html);
        Assert.DoesNotContain("{{REQUEST_COUNTRY_CODE}}", html);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task RenderedBioagePagesUseAustralianCloudflareCountryHeaderForLabAccess(string path)
    {
        using var client = App.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", "AU");

        var html = await client.GetStringAsync(path);

        Assert.Contains("const REQUEST_COUNTRY_CODE = 'AU';", html);
        Assert.DoesNotContain("{{REQUEST_COUNTRY_CODE}}", html);
    }

    [Theory]
    [InlineData(
        "/pheno-age?labGeo=AU",
        "https://merch.longevityworldcup.com/product/phenoage-league-test-package-australia/",
        "This Australian panel covers the biomarkers for pheno age.")]
    [InlineData(
        "/bortz-age?labGeo=AU",
        "https://merch.longevityworldcup.com/product/ultimate-league-test-package-australia/",
        "This Australian panel covers the biomarkers for bortz age.")]
    public async Task AustralianLabPreviewShowsTheMatchingPanel(
        string path,
        string expectedHref,
        string expectedDescription)
    {
        var app = App;
        var browser = Browser;
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = app.BaseAddress.ToString(),
            Locale = "en-HU"
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);

        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
                errors.Add(message.Text);
        };
        page.PageError += (_, error) => errors.Add(error);
        try
        {
            await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

            var panel = page.Locator(".lab-access-panel");
            await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

            Assert.Contains(expectedDescription, await panel.InnerTextAsync());
            Assert.Equal(expectedHref, await panel.Locator(".lab-access-link").GetAttributeAsync("href"));
            Assert.True(errors.Count == 0, $"{path}: {string.Join(Environment.NewLine, errors)}");
        }
        finally
        {
            await page.CloseAsync();
        }
    }

    [Fact]
    public async Task RenderedBortzPageReceivesCloudflareUsCountryHeaderForFallback()
    {
        using var client = App.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", "US");

        var html = await client.GetStringAsync("/bortz-age");

        Assert.Contains("const REQUEST_COUNTRY_CODE = 'US';", html);
        Assert.Contains("allowUnknownUsRegion", html);
        Assert.Contains("hasKnownBlockedUsRegion", html);
        Assert.DoesNotContain("{{REQUEST_COUNTRY_CODE}}", html);
    }

    [Fact]
    public async Task RenderedBioagePagesIgnoreInvalidCloudflareCountryHeader()
    {
        using var client = App.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", "NZ<script>");

        var html = await client.GetStringAsync("/pheno-age");

        Assert.Contains("const REQUEST_COUNTRY_CODE = '';", html);
        Assert.DoesNotContain("NZ<script>", html);
        Assert.DoesNotContain("{{REQUEST_COUNTRY_CODE}}", html);
    }


}
