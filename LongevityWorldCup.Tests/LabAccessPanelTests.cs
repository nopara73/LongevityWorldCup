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
        Assert.Contains("It also includes 70% off the entry fee.", html);
        Assert.Contains("Open New Zealand panel", html);
        Assert.Contains("/athlete/klaus-townsend", html);
        Assert.Contains("Available in most US states; not available in Hawaii, New York, New Jersey, or Rhode Island.", html);
        Assert.Contains("country === 'NZ'", html);
        Assert.Contains("labGeo", html);
        Assert.Contains("REQUEST_COUNTRY_CODE", html);
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
