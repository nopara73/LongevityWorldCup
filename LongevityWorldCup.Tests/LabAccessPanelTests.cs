using Microsoft.Playwright;
using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class LabAccessPanelTests
{
    [Fact]
    public void BortzPageShowsNewZealandPanelForEligibleVisitors()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("https://merch.longevityworldcup.com/product/ultimate-league-test-package-new-zealand/", html);
        Assert.Contains("This New Zealand panel covers the biomarkers for bortz age.", html);
        Assert.Contains("It also includes 40% off the entry fee.", html);
        Assert.Contains("Open New Zealand panel", html);
        Assert.Contains("/athlete/klaus-townsend", html);
        Assert.Contains("Available in most US states; not available in Hawaii, New York, New Jersey, or Rhode Island.", html);
        Assert.Contains("country === 'NZ'", html);
        Assert.Contains("labGeo", html);
        Assert.Contains("REQUEST_COUNTRY_CODE", html);
        Assert.Contains("getBrowserLocaleGeo", html);
        Assert.Contains("Pacific/Auckland", html);
        Assert.Contains("match[1] === 'NZ' || match[1] === 'AU' || match[1] === 'US'", html);
    }

    [Fact]
    public void BortzPageShowsAustralianPanelForEligibleVisitors()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html"));

        Assert.Contains("https://merch.longevityworldcup.com/product/ultimate-league-test-package-australia/", html);
        Assert.Contains("This Australian panel covers the biomarkers for bortz age.", html);
        Assert.Contains("Open Australian panel", html);
        Assert.Contains("country === 'AU'", html);
        Assert.Contains("timeZone.startsWith('Australia/')", html);
        Assert.Contains("match[1] === 'AU'", html);
    }

    [Fact]
    public void BortzPageDoesNotLetBrowserLanguageOverrideKnownTimezone()
    {
        var html = File.ReadAllText(GetPagePath("bortz-age.html")).Replace("\r\n", "\n");

        var timezoneCheck = html.IndexOf("const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;", StringComparison.Ordinal);
        var knownTimezoneFallback = html.IndexOf("if (timeZone) {\n                        // Timezone is stronger location evidence than a language preference such as en-US.\n                        return null;\n                    }", timezoneCheck, StringComparison.Ordinal);
        var languageFallback = html.IndexOf("const languages = [];", timezoneCheck, StringComparison.Ordinal);

        Assert.True(timezoneCheck >= 0);
        Assert.True(knownTimezoneFallback > timezoneCheck);
        Assert.True(languageFallback > knownTimezoneFallback);
    }

    [Fact]
    public void PhenoPageShowsNewZealandPanelForEligibleVisitors()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("https://merch.longevityworldcup.com/product/amateur-league-test-package-new-zealand/", html);
        Assert.Contains("This New Zealand panel covers the biomarkers for pheno age.", html);
        Assert.Contains("Open New Zealand panel", html);
        Assert.Contains("/athlete/klaus-townsend", html);
        Assert.Contains("country === 'NZ'", html);
        Assert.Contains("labGeo", html);
        Assert.Contains("REQUEST_COUNTRY_CODE", html);
        Assert.Contains("getBrowserLocaleGeo", html);
        Assert.Contains("Pacific/Auckland", html);
        Assert.Contains("match[1] === 'NZ'", html);
    }

    [Fact]
    public void PhenoPageShowsAustralianPanelForEligibleVisitors()
    {
        var html = File.ReadAllText(GetPagePath("pheno-age.html"));

        Assert.Contains("https://merch.longevityworldcup.com/product/phenoage-league-test-package-australia/", html);
        Assert.Contains("This Australian panel covers the biomarkers for pheno age.", html);
        Assert.Contains("Open Australian panel", html);
        Assert.Contains("country === 'AU'", html);
        Assert.Contains("timeZone.startsWith('Australia/')", html);
        Assert.Contains("match[1] === 'AU'", html);
    }

    [Theory]
    [InlineData("/pheno-age")]
    [InlineData("/bortz-age")]
    public async Task RenderedBioagePagesUseCloudflareCountryHeaderForLabAccess(string path)
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
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
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
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
        await using var app = await BrowserTestApp.StartAsync();
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
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

        await page.GotoAsync(path, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        var panel = page.Locator(".lab-access-panel");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        Assert.Contains(expectedDescription, await panel.InnerTextAsync());
        Assert.Equal(expectedHref, await panel.Locator(".lab-access-link").GetAttributeAsync("href"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task RenderedBortzPageReceivesCloudflareUsCountryHeaderForFallback()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
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
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("CF-IPCountry", "NZ<script>");

        var html = await client.GetStringAsync("/pheno-age");

        Assert.Contains("const REQUEST_COUNTRY_CODE = '';", html);
        Assert.DoesNotContain("NZ<script>", html);
        Assert.DoesNotContain("{{REQUEST_COUNTRY_CODE}}", html);
    }

    [Theory]
    [InlineData("pheno-age.html")]
    [InlineData("bortz-age.html")]
    public void LabAccessGeoLookup_IsTimeBounded(string fileName)
    {
        var html = File.ReadAllText(GetPagePath(fileName));

        Assert.Contains("function fetchLabGeoWithTimeout()", html);
        Assert.Contains("const timeoutMs = 3500;", html);
        Assert.Contains("const controller = typeof AbortController !== 'undefined' ? new AbortController() : null;", html);
        Assert.Contains("window.setTimeout(() => controller.abort(), timeoutMs)", html);
        Assert.Contains("...(controller ? { signal: controller.signal } : {})", html);
        Assert.Contains("fetchLabGeoWithTimeout()", html);
    }

    private static string GetPagePath(string fileName)
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "onboarding", fileName);
    }

    private static string FindRepoRoot([CallerFilePath] string sourceFilePath = "")
    {
        var startDirectory = Path.GetDirectoryName(sourceFilePath) ?? AppContext.BaseDirectory;
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "LongevityWorldCup.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException($"Could not find repository root from {startDirectory}.");
    }
}
