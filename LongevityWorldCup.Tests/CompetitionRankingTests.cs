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
            CrowdCandidate("larger_reduction", "Larger Reduction", crowdAge: 45, crowdAgeReduction: -25, crowdCount: 100, year: 1950),
            CrowdCandidate("smaller_reduction", "Smaller Reduction", crowdAge: 30, crowdAgeReduction: -10, crowdCount: 200, year: 1950),
            CrowdCandidate("same_reduction_more_guesses", "Same More", crowdAge: 40, crowdAgeReduction: -20, crowdCount: 200, year: 1960),
            CrowdCandidate("same_reduction_fewer_guesses", "Same Fewer", crowdAge: 40, crowdAgeReduction: -20, crowdCount: 100, year: 1950),
            CrowdCandidate("older_tie", "Older Tie", crowdAge: 40, crowdAgeReduction: -15, crowdCount: 100, year: 1940),
            CrowdCandidate("younger_tie", "Younger Tie", crowdAge: 40, crowdAgeReduction: -15, crowdCount: 100, year: 1980),
            CrowdCandidate("alphabetical", "Alphabetical", crowdAge: 40, crowdAgeReduction: -15, crowdCount: 100, year: 1980)
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

    [Fact]
    public void SortByPhenoAgeImprovementRules_UsesImprovementReductionDobAndNameTieBreakers()
    {
        var rows = new[]
        {
            PhenoImprovementCandidate("largest_improvement", "Largest Improvement", phenoAgeImprovement: -8, phenoAgeReduction: -3, year: 1980),
            PhenoImprovementCandidate("smaller_improvement", "Smaller Improvement", phenoAgeImprovement: -2, phenoAgeReduction: -30, year: 1950),
            PhenoImprovementCandidate("same_improvement_better_reduction", "Same Better", phenoAgeImprovement: -5, phenoAgeReduction: -20, year: 1990),
            PhenoImprovementCandidate("same_improvement_worse_reduction", "Same Worse", phenoAgeImprovement: -5, phenoAgeReduction: -10, year: 1940),
            PhenoImprovementCandidate("older_tie", "Older Tie", phenoAgeImprovement: -4, phenoAgeReduction: -7, year: 1940),
            PhenoImprovementCandidate("younger_tie", "Younger Tie", phenoAgeImprovement: -4, phenoAgeReduction: -7, year: 1980),
            PhenoImprovementCandidate("alphabetical", "Alphabetical", phenoAgeImprovement: -4, phenoAgeReduction: -7, year: 1980)
        };

        var sorted = CompetitionRanking.SortByPhenoAgeImprovementRules(rows).ToList();

        Assert.Equal("largest_improvement", sorted[0].Slug);
        Assert.Equal("same_improvement_better_reduction", sorted[1].Slug);
        Assert.Equal("same_improvement_worse_reduction", sorted[2].Slug);
        Assert.Equal("older_tie", sorted[3].Slug);
        Assert.Equal("alphabetical", sorted[4].Slug);
        Assert.Equal("younger_tie", sorted[5].Slug);
        Assert.Equal("smaller_improvement", sorted[6].Slug);
    }

    [Fact]
    public void SortByBortzAgeImprovementRules_UsesImprovementReductionDobAndNameTieBreakers()
    {
        var rows = new[]
        {
            BortzImprovementCandidate("largest_improvement", "Largest Improvement", bortzAgeImprovement: -8, bortzAgeReduction: -3, year: 1980),
            BortzImprovementCandidate("smaller_improvement", "Smaller Improvement", bortzAgeImprovement: -2, bortzAgeReduction: -30, year: 1950),
            BortzImprovementCandidate("same_improvement_better_reduction", "Same Better", bortzAgeImprovement: -5, bortzAgeReduction: -20, year: 1990),
            BortzImprovementCandidate("same_improvement_worse_reduction", "Same Worse", bortzAgeImprovement: -5, bortzAgeReduction: -10, year: 1940),
            BortzImprovementCandidate("older_tie", "Older Tie", bortzAgeImprovement: -4, bortzAgeReduction: -7, year: 1940),
            BortzImprovementCandidate("younger_tie", "Younger Tie", bortzAgeImprovement: -4, bortzAgeReduction: -7, year: 1980),
            BortzImprovementCandidate("alphabetical", "Alphabetical", bortzAgeImprovement: -4, bortzAgeReduction: -7, year: 1980)
        };

        var sorted = CompetitionRanking.SortByBortzAgeImprovementRules(rows).ToList();

        Assert.Equal("largest_improvement", sorted[0].Slug);
        Assert.Equal("same_improvement_better_reduction", sorted[1].Slug);
        Assert.Equal("same_improvement_worse_reduction", sorted[2].Slug);
        Assert.Equal("older_tie", sorted[3].Slug);
        Assert.Equal("alphabetical", sorted[4].Slug);
        Assert.Equal("younger_tie", sorted[5].Slug);
        Assert.Equal("smaller_improvement", sorted[6].Slug);
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

    private static PhenoAgeImprovementRankCandidate PhenoImprovementCandidate(
        string slug,
        string name,
        double phenoAgeImprovement,
        double phenoAgeReduction,
        int year)
    {
        return new PhenoAgeImprovementRankCandidate(
            slug,
            name,
            phenoAgeImprovement,
            phenoAgeReduction,
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    private static BortzAgeImprovementRankCandidate BortzImprovementCandidate(
        string slug,
        string name,
        double bortzAgeImprovement,
        double bortzAgeReduction,
        int year)
    {
        return new BortzAgeImprovementRankCandidate(
            slug,
            name,
            bortzAgeImprovement,
            bortzAgeReduction,
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
