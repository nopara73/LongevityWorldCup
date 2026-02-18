namespace LongevityWorldCup.Website.Business;

public record AthleteForX(
    string Slug,
    string Name,
    int? CurrentRank,
    double? LowestPhenoAge,
    double? LowestBortzAge,
    double? ChronoAge,
    double? PhenoAgeDiffFromBaseline,
    double? BortzAgeDiffFromBaseline,
    string? PodcastLink,
    string? XHandle);
