using Xunit;

namespace LongevityWorldCup.Tests;


public sealed class RulesetImageAccessibilityTests(TestWebApplicationFactory sharedFactory) : IClassFixture<TestWebApplicationFactory>
{
    [Fact]
    public async Task RulesetDiagrams_HaveDescriptiveAlternativeText()
    {
        using var client = sharedFactory.CreateClient();

        var html = await client.GetStringAsync("/ruleset");

        Assert.DoesNotContain("alt=\"image\"", html);
        Assert.Contains("alt=\"Season duration defines the competition cycle, while test validity defines test acceptance\"", html);
        Assert.Contains("alt=\"Multiple submissions may improve ranking at higher cost, while one strategic submission is cheaper but may miss the optimum\"", html);
        Assert.Contains("alt=\"Prize money timeline from Bitcoin donations through allocation, funding, costs, wallet setup, and January payouts\"", html);
        Assert.Contains("alt=\"Registration process: visit the website, then follow the instructions\"", html);
        Assert.Contains("alt=\"Biomarkers used in pheno age calculation and their common laboratory names\"", html);
        Assert.Contains("alt=\"Ranking flow: compare pheno age, then break ties by older chronological age and, if still tied, username alphabetically\"", html);
        Assert.Contains("alt=\"Profile picture compliance balances editing freedom with use of a personal image\"", html);
    }

    [Fact]
    public async Task RulesetDiagrams_AppearInTheirMatchingSections()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/ruleset");
        var expectedSections = new[]
        {
            (
                Start: "id=\"seasons--schedule\"",
                Image: "alt=\"Season duration defines the competition cycle, while test validity defines test acceptance\"",
                End: "id=\"tracks\""),
            (
                Start: "id=\"point-system-ranking\"",
                Image: "alt=\"Multiple submissions may improve ranking at higher cost, while one strategic submission is cheaper but may miss the optimum\"",
                End: "id=\"prizes-and-payouts\""),
            (
                Start: "id=\"prizes-and-payouts\"",
                Image: "alt=\"Prize money timeline from Bitcoin donations through allocation, funding, costs, wallet setup, and January payouts\"",
                End: "id=\"faq\""),
            (
                Start: "id=\"how-do-i-register-for-the-competition\"",
                Image: "alt=\"Registration process: visit the website, then follow the instructions\"",
                End: "id=\"can-i-withdraw-from-the-competition\""),
            (
                Start: "id=\"from-which-biomarkers-can-i-calculate-my-pheno-age\"",
                Image: "alt=\"Biomarkers used in pheno age calculation and their common laboratory names\"",
                End: "id=\"from-which-biomarkers-can-i-calculate-my-bortz-age\""),
            (
                Start: "id=\"what-if-theres-a-tie\"",
                Image: "alt=\"Ranking flow: compare pheno age, then break ties by older chronological age and, if still tied, username alphabetically\"",
                End: "id=\"how-is-my-score-calculated-if-i-submit-multiple-results\""),
            (
                Start: "id=\"how-much-can-i-edit-my-profile-picture\"",
                Image: "alt=\"Profile picture compliance balances editing freedom with use of a personal image\"",
                End: "id=\"im-already-an-athlete-how-can-i-make-changes\"")
        };

        foreach (var expected in expectedSections)
        {
            var sectionStart = html.IndexOf(expected.Start, StringComparison.Ordinal);
            var image = html.IndexOf(expected.Image, StringComparison.Ordinal);
            var sectionEnd = html.IndexOf(expected.End, StringComparison.Ordinal);

            Assert.True(sectionStart >= 0, $"Missing section marker: {expected.Start}");
            Assert.True(image >= 0, $"Missing ruleset diagram: {expected.Image}");
            Assert.True(sectionEnd >= 0, $"Missing next section marker: {expected.End}");
            Assert.True(
                sectionStart < image && image < sectionEnd,
                $"Ruleset diagram {expected.Image} must appear between {expected.Start} and {expected.End}.");
        }
    }
}
