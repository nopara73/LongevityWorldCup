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
        Assert.Contains("leagueType: 'flag'", html);

        Assert.Contains("CROWD_AGE_LEADERBOARD_MINIMUM_GUESS_COUNT", html);
        Assert.Contains("buildFiltersHref([generation, division])", html);
        Assert.Contains("href: buildFlagHref(flagOption.name)", html);
        Assert.Contains("leagueLabel: flagOption.name", html);
        Assert.Contains("leagueLabelHtml: renderFlagLabel(flagOption.name)", html);
        Assert.DoesNotContain("leagueLabelHtml: `${renderFlagLabel(flagOption.name)} Flag`", html);
    }

    [Fact]
    public void BestRankModal_RendersFromCandidateMetadata()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("renderBestRankLink(rankSummary && rankSummary.bestCandidate)", html);
        Assert.Contains("const preposition = candidate.leagueType === 'flag' ? 'in' : 'in the';", html);
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

        Assert.Contains("<span id=\"crowdAge\">0</span> <span class=\"unit\">years</span>, <span id=\"crowdCount\">0</span> <span class=\"detail-muted\">guesses</span>", html);
        Assert.DoesNotContain("<span class=\"detail-muted\">, <span id=\"crowdCount\">0</span> guesses</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"lowestPhenoAgeRank\" class=\"detail-value\"></span>)</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"bortzPaceOfAgingRank\" class=\"detail-value\"></span>)</span>", html);
        Assert.Contains("<span class=\"detail-muted\">(rank: <span id=\"paceOfAgingRank\" class=\"detail-value\"></span>)</span>", html);
    }

    [Fact]
    public void AthleteShareButton_UsesDesktopShareMenuInsteadOfDesktopNativeShare()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("id=\"athleteShareMenu\"", html);
        Assert.Contains("id=\"copyAthleteProfileLink\"", html);
        Assert.Contains("id=\"shareAthleteProfileX\"", html);
        Assert.Contains("id=\"shareAthleteProfileFacebook\"", html);
        Assert.Contains("id=\"shareAthleteProfileLinkedIn\"", html);
        Assert.Contains("id=\"shareAthleteProfileEmail\"", html);
        Assert.Contains("function shouldUseNativeAthleteShare(sharePayload)", html);
        Assert.Contains("function getAthleteShareOrigin()", html);
        Assert.Contains("hostname === 'localhost' || hostname === '127.0.0.1'", html);
        Assert.Contains("'https://longevityworldcup.com'", html);
        Assert.Contains("window.matchMedia('(pointer: coarse)').matches", html);
        Assert.Contains("https://twitter.com/intent/tweet?text=", html);
        Assert.Contains("https://www.facebook.com/sharer/sharer.php?u=", html);
        Assert.Contains("https://www.linkedin.com/sharing/share-offsite/?url=", html);
        Assert.Contains("mailto:?subject=", html);
        Assert.Contains("openAthleteShareMenu(button, sharePayload)", html);
        Assert.Contains("#detailsModal .athlete-share-menu.is-above", html);
        Assert.Contains("function positionAthleteShareMenu(menu)", html);
        Assert.Contains("const viewportBottom = Math.min(window.innerHeight, modalRect ? modalRect.bottom : window.innerHeight);", html);
        Assert.Contains("menu.classList.add('is-above');", html);
        Assert.Contains("positionAthleteShareMenu(menu);", html);
        Assert.Contains("function keepAthleteShareMenuInView(menu)", html);
        Assert.Contains("menu.scrollIntoView({ block: 'nearest', inline: 'nearest' });", html);
        Assert.Contains("keepAthleteShareMenuInView(menu);", html);
        Assert.Contains("requestAnimationFrame(() => keepAthleteShareMenuInView(menu));", html);
        Assert.Contains("menu.classList.remove('is-above');", html);
        Assert.Contains("copyTextToClipboard(sharePayload.url)", html);
        Assert.Contains("if (shouldUseNativeAthleteShare(sharePayload))", html);
        Assert.DoesNotContain("if (navigator.share &&", html);
        Assert.DoesNotContain("await navigator.share(sharePayload);", html);
    }

    [Fact]
    public void LeaderboardSidebar_MatchesRenderedTableHeightAndKeepsStickyRail()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("--leaderboard-sidebar-sticky-top: 4rem;", html);
        Assert.Contains("min-height:var(--leaderboard-table-height);", html);
        Assert.Contains("overflow:clip; flex-grow:1;", html);
        Assert.Contains("position:relative; width:50px; min-height:var(--leaderboard-table-height);", html);
        Assert.Contains("overflow:visible; flex-shrink:0;", html);
        Assert.DoesNotContain(".sidebar::before", html);
        Assert.DoesNotContain("border-bottom:3px solid rgba(0,188,212,.7);", html);
        Assert.DoesNotContain("background:linear-gradient(to bottom, rgba(0,188,212,0), rgba(0,188,212,.12));", html);
        Assert.DoesNotContain(".filter-section.has-active-filter h3::after", html);
        Assert.Contains("#flag-filter-section ul::after", html);
        Assert.Contains("background:linear-gradient(90deg, rgba(0,188,212,.68), rgba(0,188,212,.18));", html);
        Assert.Contains("display:block; position:sticky; top:calc(var(--leaderboard-sidebar-sticky-top) + .75rem);", html);
        Assert.Contains("width:100%; box-sizing:border-box;", html);
        Assert.DoesNotContain("position:sticky; top:var(--leaderboard-sidebar-sticky-top); z-index:2;", html);
        Assert.Contains("display:flex; align-items:center; justify-content:center; border-bottom:2px solid var(--primary-color);", html);
        Assert.Contains("display:inline-flex; align-items:center; justify-content:center; flex:0 0 2.25rem; width:2.25rem; height:2.25rem;", html);
        Assert.Contains(".sidebar.expanded .sidebar-title{ position:static; justify-content:flex-start; }", html);
        Assert.Contains("position:sticky; top:var(--leaderboard-sidebar-sticky-top); width:auto; min-height:0; max-height:min(calc(100vh - 2rem), var(--leaderboard-table-height));", html);
        Assert.Contains("height:100dvh; min-height:0; max-height:100dvh;", html);
        Assert.Contains("leaderboard.style.setProperty('--leaderboard-table-height', `${Math.ceil(tableHeight)}px`);", html);
        Assert.DoesNotContain("LEADERBOARD_SIDEBAR_MIN_ROWS", html);
        Assert.DoesNotContain("tenRowTableHeight", html);
        Assert.DoesNotContain("sidebarLayoutHeight", html);
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
