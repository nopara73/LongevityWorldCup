using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RulesetImageAccessibilityTests
{
    [Fact]
    public async Task RulesetDiagrams_HaveDescriptiveAlternativeText()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/ruleset");

        Assert.DoesNotContain("alt=\"image\"", html);
        Assert.Contains("alt=\"Season duration defines the competition cycle, while test validity defines test acceptance\"", html);
        Assert.Contains("alt=\"Multiple submissions may improve ranking at higher cost, while one strategic submission is cheaper but may miss the optimum\"", html);
        Assert.Contains("alt=\"Prize money timeline from Bitcoin donations through allocation, funding, costs, wallet setup, and January payouts\"", html);
        Assert.Contains("alt=\"Registration process: visit the website, then follow the instructions\"", html);
        Assert.Contains("alt=\"Biomarkers used in pheno age calculation and their common laboratory names\"", html);
        Assert.Contains("alt=\"Tie-break order: pheno age, chronological age, then alphabetical order\"", html);
        Assert.Contains("alt=\"Profile picture compliance balances editing freedom with use of a personal image\"", html);
    }
}
