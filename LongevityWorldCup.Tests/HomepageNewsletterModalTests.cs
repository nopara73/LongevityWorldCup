using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageNewsletterModalTests
{
    [Fact]
    public void NewsletterSuccessDialog_AllowsLongEmailToWrap()
    {
        var indexHtml = File.ReadAllText(Path.Combine(
            FindRepoRoot(),
            "LongevityWorldCup.Website",
            "wwwroot",
            "index.html"));

        Assert.Contains("id=\"newsletterSuccessMessage\"", indexHtml);

        var ruleStart = indexHtml.IndexOf("#newsletterSuccessMessage,", StringComparison.Ordinal);
        Assert.True(ruleStart >= 0, "Could not find newsletter success message wrapping rule.");

        var ruleEnd = indexHtml.IndexOf('}', ruleStart);
        Assert.True(ruleEnd > ruleStart, "Could not find end of newsletter success message wrapping rule.");

        var rule = indexHtml[ruleStart..ruleEnd];
        Assert.Contains("#newsletterSuccessMessage strong", rule);
        Assert.Contains("overflow-wrap: anywhere;", rule);
        Assert.Contains("word-break: break-word;", rule);
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
