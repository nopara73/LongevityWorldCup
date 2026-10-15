using System.Text.Json.Nodes;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class FlagLeaderboardTests
{
    [Fact]
    public void Ultimate_UsesCompleteBortzThenCompletePhenoThenIncomplete_WithoutMixingClocks()
    {
        var athletes = new[]
        {
            Athlete("b1", "Hungary", -1, -9), Athlete("b2", "Hungary", -2, -8), Athlete("b3", "Hungary", -3, -7),
            Athlete("b4", "Hungary", -100, -6),
            Athlete("p1", "USA", -20, -100), Athlete("p2", "United States", -21), Athlete("p3", "United States", -22),
            Athlete("i1", "Germany", -90, -99), Athlete("i2", "Germany", -90, -99)
        };
        var teams = FlagLeaderboard.Build(athletes, "ultimate");
        Assert.Equal(["Hungary", "United States", "Germany"], teams.Select(t => t.Flag.Name));
        Assert.Equal(["bortz", "pheno", "bortz"], teams.Select(t => t.View));
        Assert.Equal([-24d, -63d, -198d], teams.Select(t => t.Score));
        Assert.Equal([1, 2, (int?)null], teams.Select(t => t.Rank));
        Assert.Equal(["b1", "b2", "b3"], teams[0].Athletes.Select(a => a.Slug));
        Assert.Equal(4, teams[0].TotalAthletes);
    }

    [Fact]
    public void Pheno_UsesProAthletesPhenoResults_AndExcludesMissingPanels()
    {
        var teams = FlagLeaderboard.Build([
            Athlete("pro", "Hungary", -5, -50), Athlete("amateur", "Hungary", -10),
            Athlete("missing", "Hungary", null, -100), Athlete("third", "Hungary", -15)
        ], "pheno");
        var team = Assert.Single(teams);
        Assert.Equal(-30, team.Score);
        Assert.Equal(["third", "amateur", "pro"], team.Athletes.Select(a => a.Slug));
        Assert.Equal([-15d, -10d, -5d], team.Athletes.Select(a => a.EffectiveAgeReductionYears));
    }

    [Fact]
    public void Crowd_Requires100Guesses_AndUsesCrowdTieBreakers()
    {
        var teams = FlagLeaderboard.Build([
            Athlete("low", "Hungary", -10, crowd: -100, count: 99),
            Athlete("first", "Hungary", -10, crowd: -12, count: 100),
            Athlete("tied_less", "Hungary", -10, crowd: -10, count: 100),
            Athlete("tied_more", "Hungary", -10, crowd: -10, count: 120)
        ], "crowd");
        var team = Assert.Single(teams);
        Assert.Equal(["first", "tied_more", "tied_less"], team.Athletes.Select(a => a.Slug));
        Assert.Equal(-32d, team.Score);
    }

    [Fact]
    public void TiesShareCompetitionRanks_UsingUnroundedTotals()
    {
        var teams = FlagLeaderboard.Build(new[] { "Hungary", "Germany", "France" }
            .SelectMany((flag, index) => Enumerable.Range(1, 3).Select(n => Athlete(flag + n, flag, index == 2 ? -9.999 : -10))), "pheno");
        Assert.Equal([1, 1, 3], teams.Select(t => t.Rank));
        Assert.Equal("France", teams[2].Flag.Name);
    }

    [Fact]
    public void IncompleteUltimate_UsesLargestSingleClockTeam_AndMissingFlagsAreExcluded()
    {
        var teams = FlagLeaderboard.Build([
            Athlete("one", "Hungary", -10, -99), Athlete("two", "Hungary", -12),
            Athlete("missing", "", -50)
        ], "ultimate");
        var team = Assert.Single(teams);
        Assert.Equal("pheno", team.View);
        Assert.Null(team.Rank);
        Assert.Equal(2, team.Athletes.Count);
    }

    [Fact]
    public void Renderer_EncodesPublicNames_AndShowsUnrankedQualification()
    {
        var team = Assert.Single(FlagLeaderboard.Build([Athlete("<script>name</script>", "<b>flag</b>", -10)], "pheno"));
        var html = FlagLeaderboardHtmlRenderer.Render([team]);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("&lt;b&gt;flag&lt;/b&gt;", html);
        Assert.Contains("1/3", html);
        Assert.Contains("Unranked", html);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("src=\"\"", html);
    }

    [Fact]
    public void UltimateRenderer_SeparatesAmateurTrack_AndKeepsFlagNamesPlain()
    {
        var teams = FlagLeaderboard.Build([
            Athlete("b1", "Hungary", -10, -9), Athlete("b2", "Hungary", -10, -8), Athlete("b3", "Hungary", -10, -7),
            Athlete("p1", "France", -10), Athlete("p2", "France", -11), Athlete("p3", "France", -12),
            Athlete("i1", "Germany", -10)
        ], "ultimate");
        var html = FlagLeaderboardHtmlRenderer.Render(teams, separateClocks: true);
        var headings = System.Text.RegularExpressions.Regex.Matches(html, "<h3 class=\"flag-section-heading[^\"]*\"[^>]*>(.*?)</h3>")
            .Select(match => System.Text.RegularExpressions.Regex.Replace(match.Groups[1].Value, "<[^>]+>", ""));
        Assert.Equal(["Amateur track |Pheno Age", "Incomplete teams"], headings);
        Assert.Contains("<span>Hungary</span>", html);
        Assert.DoesNotContain("<span>Hungary<small>", html);
        Assert.Contains("years total", html);
        Assert.DoesNotContain("avg.", html);
    }

    private static PublicAthlete Athlete(string name, string flag, double? pheno, double? bortz = null, double crowd = -10, int count = 100)
    {
        var row = new LeaderboardSnapshotRow(1, name, name, name, "/athlete/" + Uri.EscapeDataString(name), "", bortz.HasValue ? "Pro" : "Amateur",
            bortz.HasValue ? "pro" : "amateur", bortz ?? pheno, null, null, 50, "Open", "Gen X", flag, "", "", null);
        return new PublicAthlete(row, new PhenoStatsCalculator.Result
        {
            Slug = name, Name = name, DobUtc = new DateTime(1976, 1, 1), ChronoAge = 50,
            AgeReduction = pheno, BortzAgeReduction = bortz,
            LowestPhenoAgeDateUtc = pheno.HasValue ? new DateTime(2026, 1, 1) : null,
            CrowdAge = 50 + crowd, CrowdCount = count
        }, new JsonObject());
    }
}
