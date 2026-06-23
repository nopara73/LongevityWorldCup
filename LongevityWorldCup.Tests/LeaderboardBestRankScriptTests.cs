using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public class LeaderboardBestRankScriptTests
{
    [Fact]
    public void BestRankCandidates_CoverLeaderboardViewsAndSidebarLeagues()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("assignBestRankCandidates(athleteResults);", html);
        Assert.Contains("leagueType: 'ultimate'", html);
        Assert.Contains("leagueType: 'bortz'", html);
        Assert.Contains("leagueType: 'pheno'", html);
        Assert.Contains("leagueType: 'amateur'", html);
        Assert.Contains("leagueType: 'pheno-improvement'", html);
        Assert.Contains("leagueType: 'bortz-improvement'", html);
        Assert.Contains("leagueType: 'crowd'", html);
        Assert.Contains("leagueType: 'pheno-pace'", html);
        Assert.Contains("leagueType: 'bortz-pace'", html);
        Assert.Contains("leagueType: 'division'", html);
        Assert.Contains("leagueType: 'generation'", html);
        Assert.Contains("leagueType: 'combination'", html);
        Assert.Contains("leagueType: 'exclusive'", html);

        Assert.Contains("CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT", html);
        Assert.Contains("buildFiltersHref([generation, division])", html);
    }

    [Fact]
    public void BestRankModal_RendersFromCandidateMetadata()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("renderBestRankLink(rankSummary && rankSummary.bestCandidate)", html);
        Assert.Contains("href=\"${escapeHtml(candidate.href)}\"", html);
        Assert.Contains("candidates.length === 0 && Number.isFinite(ultimateRank)", html);
        Assert.DoesNotContain("bestLeagueType ===", html);
    }

    [Fact]
    public void FlagRoutes_AreRecognizedAndUsedForSingleFlagFilters()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("function getFlagRouteState(pathname)", html);
        Assert.Contains("const flagPrefix = '/flag/';", html);
        Assert.Contains("url.pathname = `/flag/${getFlagRouteSlug(selectedFlagNames[0])}`;", html);
        Assert.Contains("replace(/\\/(?:league|flag)\\/[^/]+\\/?$/, '')", html);
    }

    [Fact]
    public void AthleteDetailModal_RendersFlagRanksAndMutedSecondaryValues()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("function getFlagRankKey(flag)", html);
        Assert.Contains("const flagRankKeys = [...new Set(athleteResults.map(a => a.flagFilterKey).filter(Boolean))];", html);
        Assert.Contains("athlete.ranks[key] = index + 1;", html);
        Assert.Contains("function renderAthleteFlagDetail(flag, athleteData)", html);
        Assert.Contains("href=\"${escapeHtml(flagHref)}\"", html);
        Assert.Contains("getModalDetailRankMarkup(flagRank)", html);

        Assert.Contains("<span id=\"crowdAge\">0</span> <span class=\"unit\">years</span><span class=\"detail-muted\">, <span id=\"crowdCount\">0</span> guesses</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"lowestPhenoAgeRank\" class=\"detail-value\"></span>)</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"bortzPaceOfAgingRank\" class=\"detail-value\"></span>)</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"paceOfAgingRank\" class=\"detail-value\"></span>)</span>", html);
    }

    private static string ReadLeaderboardPartial()
    {
        var repoRoot = FindRepoRoot();
        return File.ReadAllText(Path.Combine(
            repoRoot,
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "leaderboard-content.html"));
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
