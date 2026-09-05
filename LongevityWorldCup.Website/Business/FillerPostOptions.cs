namespace LongevityWorldCup.Website.Business;

internal static class FillerPostOptions
{
    private static readonly string[] Top3LeagueSlugs = ["ultimate", "amateur", "mens", "womens", "open", "silent-generation", "baby-boomers", "gen-x", "millennials", "gen-z", "gen-alpha", "prosperan"];
    private static readonly string[] DomainKeys = ["liver", "kidney", "metabolic", "inflammation", "immune", "vitamin_d"];

    public static List<(FillerType Type, string Text)> Create()
    {
        var options = new List<(FillerType Type, string Text)>();
        foreach (var slug in Top3LeagueSlugs)
            options.Add((FillerType.Top3Leaderboard, $"league[{slug}]"));
        foreach (var dk in DomainKeys)
            options.Add((FillerType.DomainTop, $"domain[{dk}]"));
        options.Add((FillerType.HistoryDocument, ""));
        options.Add((FillerType.Ruleset, ""));
        options.Add((FillerType.GitHubRepository, ""));
        options.Add((FillerType.Donation, ""));
        return options;
    }
}
