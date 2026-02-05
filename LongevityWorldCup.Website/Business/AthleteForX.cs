namespace LongevityWorldCup.Website.Business;

public record AthleteForX(
    string Slug,
    string Name,
    int? CurrentRank,
    double? LowestPhenoAge,
    double? ChronoAge,
    double? PhenoAgeDiffFromBaseline,
    string? PodcastLink,
    string? XHandle);
