namespace LongevityWorldCup.Website.Tools;

internal static class SocialPostLinks
{
    public const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    public static string AthleteUrl(string slug, string? leagueCtxSlug = null)
    {
        var baseUrl = $"https://longevityworldcup.com/athlete/{slug.Replace('_', '-')}";
        if (string.IsNullOrWhiteSpace(leagueCtxSlug) ||
            string.Equals(leagueCtxSlug, "ultimate", StringComparison.OrdinalIgnoreCase))
            return baseUrl;

        return $"{baseUrl}?ctx={Uri.EscapeDataString(leagueCtxSlug)}";
    }

    private static readonly Dictionary<string, (string DisplayName, string Url)> LeagueBySlug = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ultimate"] = ("Ultimate League", LeaderboardUrl),
        ["amateur"] = ("Amateur League", "https://longevityworldcup.com/league/amateur"),
        ["mens"] = ("Men's Division", "https://longevityworldcup.com/league/mens"),
        ["womens"] = ("Women's Division", "https://longevityworldcup.com/league/womens"),
        ["open"] = ("Open Division", "https://longevityworldcup.com/league/open"),
        ["silent-generation"] = ("Silent Generation", "https://longevityworldcup.com/league/silent-generation"),
        ["baby-boomers"] = ("Baby Boomers Generation", "https://longevityworldcup.com/league/baby-boomers"),
        ["gen-x"] = ("Gen X Generation", "https://longevityworldcup.com/league/gen-x"),
        ["millennials"] = ("Millennials Generation", "https://longevityworldcup.com/league/millennials"),
        ["gen-z"] = ("Gen Z Generation", "https://longevityworldcup.com/league/gen-z"),
        ["gen-alpha"] = ("Gen Alpha Generation", "https://longevityworldcup.com/league/gen-alpha"),
        ["prosperan"] = ("Prosperan Exclusive League", "https://longevityworldcup.com/league/prosperan")
    };

    private static readonly Dictionary<string, string> CatValToSlug = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Division|Men's"] = "mens",
        ["Division|Women's"] = "womens",
        ["Division|Open"] = "open",
        ["Generation|Silent Generation"] = "silent-generation",
        ["Generation|Baby Boomers"] = "baby-boomers",
        ["Generation|Gen X"] = "gen-x",
        ["Generation|Millennials"] = "millennials",
        ["Generation|Gen Z"] = "gen-z",
        ["Generation|Gen Alpha"] = "gen-alpha",
        ["Exclusive|Prosperan"] = "prosperan",
        ["Amateur|Amateur"] = "amateur",
        ["Global|"] = "ultimate"
    };

    public static string? LeagueContextSlug(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        var key = $"{c}|{v}";
        return CatValToSlug.TryGetValue(key, out var slug) ? slug : null;
    }

    public static string LeagueDisplay(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        var key = $"{c}|{v}";
        if (CatValToSlug.TryGetValue(key, out var slug) && LeagueBySlug.TryGetValue(slug, out var league))
            return league.DisplayName;
        if (string.IsNullOrWhiteSpace(c) && string.IsNullOrWhiteSpace(v)) return "";
        if (string.IsNullOrWhiteSpace(c)) return v;
        if (string.IsNullOrWhiteSpace(v)) return c;
        return $"{v} {c}";
    }

    public static bool TryGetLeague(string slug, out (string DisplayName, string Url) league)
    {
        return LeagueBySlug.TryGetValue(slug, out league);
    }
}
