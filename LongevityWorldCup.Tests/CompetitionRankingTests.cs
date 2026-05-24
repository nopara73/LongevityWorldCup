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
}
