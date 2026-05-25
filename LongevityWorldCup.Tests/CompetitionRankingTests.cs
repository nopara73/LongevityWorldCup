using LongevityWorldCup.Website.Business;
using Xunit;

namespace LongevityWorldCup.Tests;

public class CompetitionRankingTests
{
    [Fact]
    public void SortByCompetitionRules_AlwaysRanksProBeforeAmateur()
    {
        var rows = new[]
        {
            Candidate("phenoage", "PhenoAge", hasBortz: false, effectiveReduction: -100, year: 1950),
            Candidate("bortz", "Bortz", hasBortz: true, effectiveReduction: 100, year: 1990)
        };

        var sorted = CompetitionRanking.SortByCompetitionRules(rows).ToList();

        Assert.Equal("bortz", sorted[0].Slug);
        Assert.Equal("phenoage", sorted[1].Slug);
    }

    [Fact]
    public void CalculateHypothetical_UsesReductionDobAndNameTieBreakers()
    {
        var rows = new[]
        {
            Candidate("leader", "Leader", hasBortz: true, effectiveReduction: -10, year: 1970),
            Candidate("younger_tie", "Younger Tie", hasBortz: true, effectiveReduction: -5, year: 1960),
            Candidate("amateur", "Amateur", hasBortz: false, effectiveReduction: -100, year: 1940)
        };

        var result = CompetitionRanking.CalculateHypothetical(
            rows,
            chronologicalAge: 60,
            biologicalAge: 55,
            dobUtc: new DateTime(1955, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            hasBortz: true);

        Assert.Equal(2, result.Rank);
        Assert.Equal(4, result.FieldSize);
        Assert.Equal("Pro", result.Category);
        Assert.Contains(result.Nearby, n => n.IsHypothetical && n.Rank == 2);
    }

    [Fact]
    public void CalculateHypothetical_PhenoOnlyStaysBehindProField()
    {
        var rows = new[]
        {
            Candidate("pro", "Pro", hasBortz: true, effectiveReduction: 20, year: 1980),
            Candidate("amateur", "Amateur", hasBortz: false, effectiveReduction: -10, year: 1970)
        };

        var result = CompetitionRanking.CalculateHypothetical(
            rows,
            chronologicalAge: 60,
            biologicalAge: 20,
            dobUtc: new DateTime(1960, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            hasBortz: false);

        Assert.Equal(2, result.Rank);
        Assert.Equal("Amateur", result.Category);
    }

    [Fact]
    public void SortByCrowdAgeRules_UsesReductionCountDobAndNameTieBreakers()
    {
        var rows = new[]
        {
            CrowdCandidate("larger_reduction", "Larger Reduction", crowdAge: 45, crowdAgeReduction: 25, crowdCount: 100, year: 1950),
            CrowdCandidate("smaller_reduction", "Smaller Reduction", crowdAge: 30, crowdAgeReduction: 10, crowdCount: 200, year: 1950),
            CrowdCandidate("same_reduction_more_guesses", "Same More", crowdAge: 40, crowdAgeReduction: 20, crowdCount: 200, year: 1960),
            CrowdCandidate("same_reduction_fewer_guesses", "Same Fewer", crowdAge: 40, crowdAgeReduction: 20, crowdCount: 100, year: 1950),
            CrowdCandidate("older_tie", "Older Tie", crowdAge: 40, crowdAgeReduction: 15, crowdCount: 100, year: 1940),
            CrowdCandidate("younger_tie", "Younger Tie", crowdAge: 40, crowdAgeReduction: 15, crowdCount: 100, year: 1980),
            CrowdCandidate("alphabetical", "Alphabetical", crowdAge: 40, crowdAgeReduction: 15, crowdCount: 100, year: 1980)
        };

        var sorted = CompetitionRanking.SortByCrowdAgeRules(rows).ToList();

        Assert.Equal("larger_reduction", sorted[0].Slug);
        Assert.Equal("same_reduction_more_guesses", sorted[1].Slug);
        Assert.Equal("same_reduction_fewer_guesses", sorted[2].Slug);
        Assert.Equal("older_tie", sorted[3].Slug);
        Assert.Equal("alphabetical", sorted[4].Slug);
        Assert.Equal("younger_tie", sorted[5].Slug);
        Assert.Equal("smaller_reduction", sorted[6].Slug);
    }

    private static CompetitionRankCandidate Candidate(
        string slug,
        string name,
        bool hasBortz,
        double effectiveReduction,
        int year)
    {
        return new CompetitionRankCandidate(
            slug,
            name,
            hasBortz,
            effectiveReduction,
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static CrowdAgeRankCandidate CrowdCandidate(
        string slug,
        string name,
        double crowdAge,
        double crowdAgeReduction,
        int crowdCount,
        int year)
    {
        return new CrowdAgeRankCandidate(
            slug,
            name,
            crowdAge,
            crowdAgeReduction,
            crowdCount,
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
