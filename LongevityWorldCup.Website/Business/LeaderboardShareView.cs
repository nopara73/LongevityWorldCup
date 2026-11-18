using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

/// <summary>A bounded, canonical share identity for the same selection used by the public HTML.</summary>
internal sealed record LeaderboardShareView(PublicLeaderboardSelection Selection, string Path, string ImageQuery,
    string Title, string Subtitle, bool Neutral, string? LegacySlug)
{
    internal static bool TryCreate(PublicLeaderboardSnapshot snapshot, string path, IQueryCollection query,
        out LeaderboardShareView share)
    {
        share = null!;
        if (path is not "/" and not "/leaderboard"
            && !(path.StartsWith("/league/", StringComparison.Ordinal) &&
                 (PublicLeaderboardSnapshot.Views.Contains(path[8..]) || PublicLeaderboardSnapshot.LeagueFilters.ContainsKey(path[8..])))
            && !(path.StartsWith("/flag/", StringComparison.Ordinal) &&
                 FlagRouteCatalog.TryResolve(path[6..], snapshot.Athletes.Select(a => a.Row.Flag), out _))) return false;

        var selection = snapshot.Select(path, query);
        var filters = selection.Filters;
        var knownFilters = PublicLeaderboardSnapshot.LeagueFilters.Values.Concat(snapshot.Athletes.SelectMany(a =>
            new[] { a.Row.Division, a.Row.Generation, a.Row.ExclusiveLeague })).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rawFilters = (query["filters"].FirstOrDefault() ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(PublicLeaderboardSnapshot.DecodeFilter).ToArray();
        var unknownFilter = rawFilters.Any(filter => !knownFilters.Contains(filter)
            && !FlagRouteCatalog.TryResolve(filter, snapshot.Athletes.Select(a => a.Row.Flag), out _));
        // Search uses the browser's badge index; custom sorts aren't an official podium.
        // Never put arbitrary search text, unknown filters, or redacted identities in a card.
        var neutral = unknownFilter || !string.IsNullOrWhiteSpace(query["search"])
            || selection.DisplayOrdering.IsCustom || query["preview"] == "neutral"
            || (!string.IsNullOrEmpty(query["view"]) && !PublicLeaderboardSnapshot.Views.Contains(query["view"].ToString().ToLowerInvariant())
                && !query["view"].ToString().Equals("pheno-improvement", StringComparison.OrdinalIgnoreCase))
            || query.ContainsKey("redact") || query.ContainsKey("redacted") || query["guessmyage"] == "1";

        // All combinations of the small named division/generation/track sets are finite.
        // Multi-flag searches can be arbitrarily large: keep their image generic.
        var flagCount = filters.Count(filter => FlagRouteCatalog.TryResolve(filter, snapshot.Athletes.Select(a => a.Row.Flag), out _));
        if (flagCount > 1) return false;

        var parameters = new Dictionary<string, string?>();
        if (selection.View != "ultimate") parameters["view"] = selection.View;
        if (filters.Count > 0) parameters["filters"] = string.Join(',', filters);
        if (selection.ByFlag) parameters["group"] = "flags";
        var sharePath = "/leaderboard" + QueryString.Create(parameters);
        var legacySlug = !selection.ByFlag && filters.Count == 0 ? selection.View : null;
        if (!selection.ByFlag && selection.View == "ultimate" && filters.Count == 1)
        {
            legacySlug = PublicLeaderboardSnapshot.LeagueFilters.FirstOrDefault(pair =>
                pair.Value.Equals(filters[0], StringComparison.OrdinalIgnoreCase) && pair.Key != "professional").Key;
            if (selection.Flag is { } flag) sharePath = flag.Path;
        }
        if (legacySlug is not null) sharePath = legacySlug == "ultimate" ? "/leaderboard" : "/league/" + legacySlug;
        if (neutral)
        {
            sharePath = path + QueryString.Create(query.Where(pair => new[] { "view", "filters", "group", "search", "sort", "dir", "then", "thenDir", "redact", "redacted", "guessmyage" }.Contains(pair.Key)));
            parameters["preview"] = "neutral";
            legacySlug = null;
        }

        var title = selection.ByFlag ? selection.RailTitle
            : selection.LeagueTitle.Length > 0 ? selection.LeagueTitle
            : selection.View == "ultimate" ? selection.RailTitle : FlagLeaderboard.Label(selection.View) + " Leaderboard";
        var subtitle = selection.ByFlag ? "Top-three team totals"
            : selection.LeagueTitle.Length > 0 ? FlagLeaderboard.Label(selection.View) : "Current standings";
        if (neutral) subtitle = FlagLeaderboard.Label(selection.View) + " · Selected leaderboard";
        share = new(selection, sharePath, QueryString.Create(parameters).ToString(), title, subtitle, neutral, legacySlug);
        return true;
    }
}
