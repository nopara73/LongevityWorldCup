using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class XMessageBuilder
{
    private const int MaxLength = 280;
    private const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    private static string AthleteUrl(string slug) =>
        $"https://longevityworldcup.com/athlete/{slug.Replace('_', '-')}";

    private static readonly Dictionary<string, string> LeagueUrlByCatVal = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Division|Men's"] = "https://longevityworldcup.com/league/mens",
        ["Division|Women's"] = "https://longevityworldcup.com/league/womens",
        ["Division|Open"] = "https://longevityworldcup.com/league/open",
        ["Generation|Silent Generation"] = "https://longevityworldcup.com/league/silent-generation",
        ["Generation|Baby Boomers"] = "https://longevityworldcup.com/league/baby-boomers",
        ["Generation|Gen X"] = "https://longevityworldcup.com/league/gen-x",
        ["Generation|Millennials"] = "https://longevityworldcup.com/league/millennials",
        ["Generation|Gen Z"] = "https://longevityworldcup.com/league/gen-z",
        ["Generation|Gen Alpha"] = "https://longevityworldcup.com/league/gen-alpha",
        ["Exclusive|Prosperan"] = "https://longevityworldcup.com/league/prosperan"
    };

    private static string LeagueUrl(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        if (string.IsNullOrWhiteSpace(v)) return LeaderboardUrl;
        var key = $"{c}|{v}";
        return LeagueUrlByCatVal.TryGetValue(key, out var url) ? url : LeaderboardUrl;
    }

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, string?>? getPodcastLinkForSlug = null,
        Func<string, double?>? getLowestPhenoAgeForSlug = null,
        Func<string, double?>? getChronoAgeForSlug = null,
        Func<string, double?>? getPhenoDiffForSlug = null)
    {
        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0) return "";
            var countLabel = count.ToString("N0", CultureInfo.InvariantCulture);
            return Truncate(
                $"Longevity World Cup just hit {countLabel} athletes on the leaderboard 🏁\n" +
                $"📊 View the board: {LeaderboardUrl}");
        }

        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank < 1 || rank > 3) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var rankSlug)) return "";
            var current = slugToName(rankSlug);
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            var newRankMsg = !string.IsNullOrWhiteSpace(prev)
                ? $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} overtakes {prev} for the spot.\n📊 Leaderboard: {LeaderboardUrl}"
                : $"New #{rank} in the Longevity World Cup Ultimate League 🏆\n{current} takes #{rank}.\n📊 Leaderboard: {LeaderboardUrl}";
            return Truncate(newRankMsg);
        }

        if (type != EventType.BadgeAward) return "";

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return "";
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);

        if (string.Equals(normLabel, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var phenoSlug)) return "";
            var phenoAthlete = slugToName(phenoSlug);
            var phenoAge = getLowestPhenoAgeForSlug?.Invoke(phenoSlug);
            var ageStr = phenoAge.HasValue ? $" at {phenoAge.Value.ToString("0.#", CultureInfo.InvariantCulture)} years" : "";
            var athleteUrl = AthleteUrl(phenoSlug);
            return Truncate(
                $"Lowest biological age in the Longevity World Cup field 🧬\n" +
                $"{phenoAthlete} holds it{ageStr}.\n" +
                $"📊 Profile: {athleteUrl}");
        }

        if (string.Equals(normLabel, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var diffSlug)) return "";
            var diffVal = getPhenoDiffForSlug?.Invoke(diffSlug);
            if (!diffVal.HasValue) return "";
            var years = Math.Abs(diffVal.Value);
            var yearsStr = years.ToString("0.#", CultureInfo.InvariantCulture);
            var athlete = slugToName(diffSlug);
            var url = AthleteUrl(diffSlug);
            return Truncate(
                $"Best PhenoAge improvement in the Longevity World Cup field 🧬\n" +
                $"{athlete} — biological age improved by {yearsStr} years vs baseline.\n" +
                $"📊 Profile: {url}");
        }

        if (string.Equals(normLabel, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} is now the oldest in the Longevity World Cup field at {ageStr} 🏃‍♂️\n📊 Profile: {url}");
        }

        if (string.Equals(normLabel, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} is now the youngest in the Longevity World Cup field at {ageStr} 🏃‍♂️\n📊 Profile: {url}");
        }

        if (EventHelpers.TryExtractPlace(rawText, out var place) && place == 1
            && EventHelpers.TryExtractCategory(rawText, out var leagueCat) && !string.Equals(leagueCat, "Global", StringComparison.OrdinalIgnoreCase)
            && EventHelpers.TryExtractSlug(rawText, out var leagueSlug))
        {
            EventHelpers.TryExtractValue(rawText, out var leagueVal);
            var leagueName = LeagueDisplay(leagueCat, leagueVal);
            if (string.IsNullOrWhiteSpace(leagueName)) return "";
            var leagueAthlete = slugToName(leagueSlug);
            EventHelpers.TryExtractPrev(rawText, out var leaguePrevSlug);
            var leaguePrev = !string.IsNullOrWhiteSpace(leaguePrevSlug) ? slugToName(leaguePrevSlug) : null;
            var leagueBoardUrl = LeagueUrl(leagueCat, leagueVal);
            var msg = !string.IsNullOrWhiteSpace(leaguePrev)
                ? $"{leagueAthlete} is now #1 in the {leagueName}, overtaking {leaguePrev} 🏆\n📊 Leaderboard: {leagueBoardUrl}"
                : $"{leagueAthlete} is now #1 in the {leagueName} 🏆\n📊 Leaderboard: {leagueBoardUrl}";
            return Truncate(msg);
        }

        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase)) return "";

        if (!EventHelpers.TryExtractSlug(rawText, out var guestSlug)) return "";

        var guest = slugToName(guestSlug);
        var podcastUrl = getPodcastLinkForSlug?.Invoke(guestSlug);
        if (string.IsNullOrWhiteSpace(podcastUrl)) return "";

        const string host = "@nopara73";

        return Truncate(
            $"New Longevity World Cup podcast 🎧\n" +
            $"{host} sits down with {guest} for a full conversation on the show.\n" +
            $"📹 Full episode: {podcastUrl}");
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }

    private static string LeagueDisplay(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        if (string.Equals(c, "Global", StringComparison.OrdinalIgnoreCase)) return "Ultimate League";
        if (string.Equals(c, "Division", StringComparison.OrdinalIgnoreCase))
            return v switch { "Men" => "Men's Division", "Women" => "Women's Division", "Open" => "Open Division", _ => $"{v} Division" };
        if (string.Equals(c, "Generation", StringComparison.OrdinalIgnoreCase))
            return v switch { "Silent Generation" => "Silent Generation", "Baby Boomers" => "Baby Boomers Generation", "Gen X" => "Gen X Generation", "Millennials" => "Millennials Generation", "Gen Z" => "Gen Z Generation", "Gen Alpha" => "Gen Alpha Generation", _ => $"{v} Generation" };
        if (string.Equals(c, "Exclusive", StringComparison.OrdinalIgnoreCase)) return "Prosperan Exclusive League";
        if (string.IsNullOrWhiteSpace(c) && string.IsNullOrWhiteSpace(v)) return "";
        if (string.IsNullOrWhiteSpace(c)) return v;
        if (string.IsNullOrWhiteSpace(v)) return c;
        return $"{v} {c}";
    }
}
