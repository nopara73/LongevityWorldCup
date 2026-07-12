using System.Runtime.CompilerServices;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class OpenDataProfileUiTests
{
    [Fact]
    public void LeaderboardProfiles_ArePartitionedBeforeOfficialRankingWork()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("fetch('/api/data/leaderboard-profiles')", html);
        Assert.Contains("const officialAthleteProfiles = (Array.isArray(profiles) ? profiles : []).filter(isOfficialAthleteProfile);", html);
        Assert.Contains("openDataProfiles = (Array.isArray(profiles) ? profiles : [])", html);
        Assert.Contains("athleteResults = officialAthleteProfiles.map(athlete => {", html);
        Assert.Contains("profile && profile.ProfileType === 'Athlete'", html);
        Assert.Contains("profile && profile.ProfileType === 'OpenData'", html);
        Assert.DoesNotContain("athleteResults = profiles.map", html);

        var partition = html.IndexOf("const officialAthleteProfiles", StringComparison.Ordinal);
        var ranking = html.IndexOf("athleteResults.sort(window.compareAthleteRank);", StringComparison.Ordinal);
        Assert.True(partition >= 0 && ranking > partition, "Profiles must be partitioned before official ranking starts.");
    }

    [Fact]
    public void FullLeaderboard_UsesASeparateUnrankedPublicDataSection()
    {
        var html = ReadLeaderboardPartial();
        const string disclosure = "These reference profiles use bloodwork the subjects made public. The subjects did not apply to the Longevity World Cup, are not Longevity athletes, and never affect rankings, awards, or prizes; inclusion does not imply endorsement.";

        Assert.Contains("id=\"openDataProfilesSection\"", html);
        Assert.Contains("aria-labelledby=\"openDataProfilesTitle\"", html);
        Assert.Contains("PUBLIC DATA · UNRANKED", html);
        Assert.Contains(disclosure, html);
        Assert.Contains("Request a correction or removal", html);
        Assert.Contains("normalizedPath.toLowerCase() === '/leaderboard'", html);
        Assert.Contains("sort((a, b) => a.displayName.localeCompare", html);
        Assert.Contains("metricLabel.textContent = 'Reference Pheno difference';", html);
        Assert.Contains("formulaNote.textContent = 'Pheno Age − age at published panel';", html);
        Assert.Contains("summary.className = 'open-data-card-summary';", html);
        Assert.Contains("summary.textContent = profile.notabilitySummary;", html);
        Assert.Contains("id=\"openDataNotabilitySummary\"", html);
        Assert.Contains("fa-solid fa-database", html);
        Assert.Contains("renderOpenDataProfiles();", html);
        Assert.DoesNotContain("remainingAthletes = athleteResults.concat(openDataProfiles)", html);
    }

    [Fact]
    public void PublicDataCards_UseNativeActionsAndSearchWithoutLeagueFiltering()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("const openButton = document.createElement('button');", html);
        Assert.Contains("openButton.type = 'button';", html);
        Assert.Contains("View unranked public-data profile for", html);
        Assert.Contains("sourceLink.rel = 'noopener noreferrer external';", html);
        Assert.Contains("sourceLink.setAttribute('aria-label'", html);
        Assert.Contains("source.kind === 'Bloodwork' && source.subjectAuthorization", html);
        Assert.Contains("filterOpenDataProfiles(uniqueSearchTerms);", html);
        Assert.Contains("...profile.aliases", html);
        Assert.Contains("const matches = terms.length === 0 || terms.every(term => searchText.includes(term));", html);
        Assert.DoesNotContain("filterOpenDataProfiles(selectedDivisions", html);
        Assert.Contains("min-height:44px", html);
        Assert.Contains(".open-data-card-action:focus-visible", html);
    }

    [Fact]
    public void PublicDataModal_RepeatsDisclosureAndSuppressesCompetitionOnlyUi()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("id=\"openDataProfileDisclosure\"", html);
        Assert.Contains("role=\"note\"", html);
        Assert.Contains("id=\"openDataStickyToken\"", html);
        Assert.Contains("modalContent.classList.toggle('open-data-profile', isOpenData);", html);
        Assert.Contains("detailsModal.setAttribute('aria-describedby', 'openDataProfileDisclosure');", html);
        Assert.Contains("'Close public-data profile'", html);
        Assert.Contains("resetModalForLoading(athleteSlug, openDataProfile)", html);
        Assert.Contains("populateOpenDataModal(fullAthleteData, athleteData);", html);
        Assert.Contains("gmaCard.hidden = openDataProfile;", html);
        Assert.Contains("chronologicalAgeLabel: 'Age at published panel:'", html);
        Assert.Contains("lowestPhenoAgeLabel: 'Reference Pheno Age:'", html);
        Assert.Contains("if (phenoPaceContainer) phenoPaceContainer.style.display = 'none';", html);
        Assert.Contains("if (frame && !openDataProfile)", html);
        Assert.Contains("if (!openDataProfile) {\n                            generateAgeVisualization", html);
        Assert.Contains("if (event.key === 'Tab')", html);
        Assert.Contains("requestAnimationFrame(() => closeBtn?.focus());", html);
        Assert.Contains("returnFocusTo.isConnected", html);
        Assert.DoesNotContain(".open-data-sticky-token{ display:none !important; }", html);

        foreach (var selector in new[]
                 {
                     ".sticky-pro-badge",
                     "#modalBadgeStrip",
                     "#guessAgeContainer",
                     "#ageVisualization",
                     ".official-profile-only",
                     ".rank-annotation",
                     ".events-embed",
                     ".proofs-section",
                     "#personalLink",
                     "#mediaContact"
                 })
        {
            Assert.Contains($"#detailsModal .modal-content.open-data-profile {selector}", html);
        }
    }

    [Fact]
    public void PublicDataModal_UsesDedicatedRoutesAndAccessibleSourceProvenance()
    {
        var html = ReadLeaderboardPartial();

        Assert.Contains("id=\"publicDataSources\"", html);
        Assert.Contains("aria-labelledby=\"publicDataSourcesTitle\"", html);
        Assert.Contains("link.target = '_blank';", html);
        Assert.Contains("link.rel = 'noopener noreferrer external';", html);
        Assert.Contains("Accessed ${accessedOn}", html);
        Assert.Contains("Publication explicitly authorized by the subject", html);
        Assert.Contains("View authorization evidence", html);
        Assert.Contains("getOpenDataSubjectAuthorization(source)", html);
        Assert.Contains("Supports notability context", html);
        Assert.Contains("const transcriptionNotes = getOpenDataTranscriptionNotes(provenance.TranscriptionNotes);", html);
        Assert.Contains("Transcription notes: ${transcriptionNotes.join(' ')}", html);
        Assert.Contains("record.SourceIds", html);
        Assert.Contains("const route = isOpenData ? 'public-data' : 'athlete';", html);
        Assert.Contains("const profilePath = openDataProfile ? `/public-data/${athleteSlug}` : `/athlete/${athleteSlug}`;", html);
        Assert.Contains("history.pushState({ modal: 'details', athlete: athleteSlug, profileType: athleteData.profileType }, \"\", profilePath);", html);
        Assert.Contains("/^\\/(athlete|public-data)\\/([^/]+)\\/?$/i", html);
        Assert.Contains("function getInitialProfileRoute()", html);
        Assert.Contains("return { slug: publicDataParam, profileType: 'OpenData' };", html);
        Assert.Contains("return { slug: athleteParam, profileType: 'Athlete' };", html);
        Assert.Contains("An explicit /athlete/ or /public-data/ route is authoritative.", html);
        Assert.Contains("getAthleteData(normalizedAthlete, initialProfileRoute.profileType)", html);
        Assert.Contains("if (profileType === 'Athlete') return athleteResults.find(matches);", html);
        Assert.Contains("if (profileType === 'OpenData') return openDataProfiles.find(matches);", html);
        Assert.Contains("function getCanonicalProfileRouteSlug(value)", html);
        Assert.Contains("String(value || '').replace(/_/g, '-')", html);
        Assert.Contains("const athleteSlug = getCanonicalProfileRouteSlug(athleteData.athleteSlug || athleteNameText);", html);
        Assert.Contains("url.searchParams.delete('publicData');", html);
        Assert.Contains("public-data reference - Longevity World Cup", html);
        Assert.Contains("The subject did not apply to the Longevity World Cup; inclusion does not imply endorsement.", html);
    }

    private static string ReadLeaderboardPartial([CallerFilePath] string sourceFilePath = "")
    {
        var testsDirectory = Path.GetDirectoryName(sourceFilePath)
            ?? throw new InvalidOperationException("Could not locate the tests directory.");
        return File.ReadAllText(Path.GetFullPath(Path.Combine(
            testsDirectory,
            "..",
            "LongevityWorldCup.Website",
            "wwwroot",
            "partials",
            "leaderboard-content.html")));
    }
}
