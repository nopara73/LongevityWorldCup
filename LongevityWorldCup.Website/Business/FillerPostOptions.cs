using LongevityWorldCup.Website.Tools;

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

    internal static bool MatchesMetaToken(FillerType type, string payloadText, string token)
    {
        var payload = payloadText ?? "";
        var t = token ?? "";

        return type switch
        {
            FillerType.Top3Leaderboard => EventHelpers.TryExtractLeague(payload, out var leagueSlug)
                && !string.IsNullOrWhiteSpace(leagueSlug)
                && t.Contains($"league[{leagueSlug.Trim().ToLowerInvariant()}]", StringComparison.Ordinal),
            FillerType.DomainTop => EventHelpers.TryExtractDomain(payload, out var domainKey)
                && !string.IsNullOrWhiteSpace(domainKey)
                && t.Contains($"domain[{domainKey.Trim().ToLowerInvariant()}]", StringComparison.Ordinal),
            FillerType.CrowdGuesses => t.StartsWith("podium[", StringComparison.Ordinal),
            FillerType.Newcomers => t.StartsWith("slugs[", StringComparison.Ordinal),
            FillerType.HistoryDocument => t.StartsWith("history-document[", StringComparison.Ordinal),
            FillerType.Ruleset => t.StartsWith("ruleset[", StringComparison.Ordinal),
            FillerType.GitHubRepository => t.StartsWith("github-repository[", StringComparison.Ordinal),
            FillerType.Donation => t.StartsWith("donation-reminder[", StringComparison.Ordinal),
            _ => false
        };
    }
}
