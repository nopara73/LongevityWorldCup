using LongevityWorldCup.Website.Business;
using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DiscountCodeTests
{
    [Fact]
    public void MightyKlausDiscountIsSeventyPercent()
    {
        Assert.Equal(70m, DiscountCodes.MightyKlausPercent);
    }

    [Fact]
    public void HeadDefinesMatchingMightyKlausDiscount()
    {
        var html = File.ReadAllText(GetHeadPath());

        Assert.Contains("mightyklaus: {", html);
        Assert.Contains("code: 'mightyklaus'", html);
        Assert.Contains("percent: 70", html);
        Assert.DoesNotContain("percent: 40", html);
    }

    private static string GetHeadPath()
    {
        var repoRoot = FindRepoRoot();
        return Path.Combine(repoRoot, "LongevityWorldCup.Website", "wwwroot", "partials", "head.html");
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
