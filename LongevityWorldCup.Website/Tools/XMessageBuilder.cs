using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class XMessageBuilder
{
    private const int MaxLength = 280;
    private const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    private static string AthleteUrl(string slug) =>
        $"https://longevityworldcup.com/athlete/{slug.Replace('_', '-')}";

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

    private static string LeagueUrl(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        var key = $"{c}|{v}";
        if (CatValToSlug.TryGetValue(key, out var slug) && LeagueBySlug.TryGetValue(slug, out var league))
            return league.Url;
        return LeaderboardUrl;
    }

    private static string BuildAthleteCtaLine(string athleteName, string url)
    {
        var options = new[]
        {
            $"👤 See profile: {url}",
            $"🔎 Details: {url}",
            $"👉 Take a look: {url}",
            $"📄 Full profile: {url}"
        };

        var key = athleteName ?? string.Empty;
        var hash = 0;
        for (var i = 0; i < key.Length; i++)
            hash = unchecked(hash * 31 + key[i]);
        if (hash < 0) hash = -hash;

        return options[hash % options.Length];
    }

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, string?>? getPodcastLinkForSlug = null,
        Func<string, double?>? getLowestPhenoAgeForSlug = null,
        Func<string, double?>? getLowestBortzAgeForSlug = null,
        Func<string, double?>? getChronoAgeForSlug = null,
        Func<string, double?>? getPhenoDiffForSlug = null,
        Func<string, double?>? getBortzDiffForSlug = null)
    {
        if (type == EventType.AthleteCountMilestone)
        {
            if (!EventHelpers.TryExtractAthleteCount(rawText, out var count) || count <= 0) return "";
            var countLabel = count.ToString("N0", CultureInfo.InvariantCulture);
            var lead = BuildAthleteCountMilestoneLine(count, countLabel);
            return Truncate($"{lead}\n\n📊 Leaderboard: {LeaderboardUrl}");
        }

        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank < 1 || rank > 3) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var rankSlug)) return "";
            var current = slugToName(rankSlug);
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            var newRankMsg = !string.IsNullOrWhiteSpace(prev)
                ? $"New #{rank} in the Ultimate League 🏆\n{current} is now ahead of {prev}.\n\n📊 Leaderboard: {LeaderboardUrl}"
                : $"New #{rank} in the Ultimate League 🏆\n{current} now holds the spot.\n\n📊 Leaderboard: {LeaderboardUrl}";
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
                $"{phenoAthlete} currently holds the lowest PhenoAge in the Longevity World Cup field{ageStr} 🧬\n\n" +
                BuildAthleteCtaLine(phenoAthlete, athleteUrl));
        }
        if (string.Equals(normLabel, "PhenoAge Best Improvement", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var diffSlug)) return "";
            var diffVal = getPhenoDiffForSlug?.Invoke(diffSlug);
            if (!diffVal.HasValue) return "";
            var years = Math.Abs(diffVal.Value);
            var yearsStr = years.ToString("0.#", CultureInfo.InvariantCulture);
            var athlete = slugToName(diffSlug);
            return Truncate($"The biggest PhenoAge improvement in the field currently belongs to {athlete}, at {yearsStr} years since their first submitted test 🧬");
        }

        if (string.Equals(normLabel, "Bortz Age - Lowest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var bortzSlug)) return "";
            var bortzAthlete = slugToName(bortzSlug);
            var bortzAge = getLowestBortzAgeForSlug?.Invoke(bortzSlug);
            var ageStr = bortzAge.HasValue ? $" at {bortzAge.Value.ToString("0.#", CultureInfo.InvariantCulture)} years" : "";
            var athleteUrl = AthleteUrl(bortzSlug);
            return Truncate(
                $"{bortzAthlete} currently holds the lowest Bortz Age in the Longevity World Cup field{ageStr} 🧬\n\n" +
                BuildAthleteCtaLine(bortzAthlete, athleteUrl));
        }

        if (string.Equals(normLabel, "Bortz Age Best Improvement", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var diffSlug)) return "";
            var diffVal = getBortzDiffForSlug?.Invoke(diffSlug);
            if (!diffVal.HasValue) return "";
            var years = Math.Abs(diffVal.Value);
            var yearsStr = years.ToString("0.#", CultureInfo.InvariantCulture);
            var athlete = slugToName(diffSlug);
            var url = AthleteUrl(diffSlug);
            return Truncate(
                $"The biggest Bortz Age improvement in the field currently belongs to {athlete}, at {yearsStr} years since their first submitted test 🧬\n\n" +
                BuildAthleteCtaLine(athlete, url));
        }

        if (string.Equals(normLabel, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} is currently the oldest athlete in the Longevity World Cup field at {ageStr} years.\n{BuildAthleteCtaLine(chronoAthlete, url)}");
        }

        if (string.Equals(normLabel, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return Truncate($"{chronoAthlete} is now the youngest in the Longevity World Cup field at {ageStr} years.\n{BuildAthleteCtaLine(chronoAthlete, url)}");
        }

        if (string.Equals(normLabel, "Age Reduction", StringComparison.OrdinalIgnoreCase)
            && EventHelpers.TryExtractPlace(rawText, out var place) && place == 1
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
                ? $"{leagueAthlete} is now #1 in the {leagueName}, overtaking {leaguePrev} 🏆\n\n📊 Leaderboard: {leagueBoardUrl}"
                : $"{leagueAthlete} is now #1 in the {leagueName} 🏆\n\n📊 Leaderboard: {leagueBoardUrl}";
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

    public static string ForFiller(
        FillerType fillerType,
        string payloadText,
        Func<string, string> slugToName,
        Func<string, IReadOnlyList<string>>? getTop3SlugsForLeague = null,
        Func<IReadOnlyList<(int Place, IReadOnlyList<string> Slugs)>>? getCrowdLowestAgePodium = null,
        Func<IReadOnlyList<string>>? getRecentNewcomersForX = null,
        Func<string, string?>? getBestDomainWinnerSlug = null)
    {
        if (fillerType == FillerType.Top3Leaderboard)
        {
            if (!EventHelpers.TryExtractLeague(payloadText ?? "", out var leagueSlug) || string.IsNullOrWhiteSpace(leagueSlug))
                return "";
            if (!LeagueBySlug.TryGetValue(leagueSlug.Trim(), out var league))
                return "";
            var slugs = getTop3SlugsForLeague?.Invoke(leagueSlug.Trim()) ?? Array.Empty<string>();
            if (slugs.Count == 0) return "";
            var lines = new List<string> { $"The race for #1 in the {league.DisplayName} is on. Current top 3 👇", "" };
            for (var i = 0; i < slugs.Count && i < 3; i++)
                lines.Add($"{i + 1}. {slugToName(slugs[i])}");
            lines.Add("");
            lines.Add($"📊 Full leaderboard: {league.Url}");
            return Truncate(string.Join("\n", lines));
        }

        if (fillerType == FillerType.CrowdGuesses)
        {
            var podium = getCrowdLowestAgePodium?.Invoke() ?? Array.Empty<(int Place, IReadOnlyList<string> Slugs)>();
            if (podium.Count == 0) return "";
            var lines = new List<string> { "Top 3 youngest-looking in the tournament according to the crowd \U0001F440", "" };
            for (var i = 0; i < podium.Count; i++)
            {
                var place = podium[i].Place;
                if (place < 1 || place > 3) continue;

                var names = podium[i].Slugs
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(slugToName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .ToList();
                if (names.Count == 0) continue;

                lines.Add($"{place}. {string.Join(", ", names)}");
            }
            if (lines.Count <= 2) return "";
            lines.Add("");
            lines.Add($"\U0001F4CA Full leaderboard: {LeaderboardUrl}");
            return Truncate(string.Join("\n", lines));
        }

        if (fillerType == FillerType.Newcomers)
        {
            var newcomers = getRecentNewcomersForX?.Invoke() ?? Array.Empty<string>();
            if (newcomers.Count == 0) return "";
            var lines = new List<string>
            {
                "Fresh faces on the Longevity World Cup leaderboard 🆕",
                "",
                "Explore the newest athletes:",
                LeaderboardUrl
            };
            return Truncate(string.Join("\n", lines));
        }

        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText ?? "", out var domainKey) || string.IsNullOrWhiteSpace(domainKey))
                return "";
            var winnerSlug = getBestDomainWinnerSlug?.Invoke(domainKey.Trim());
            if (string.IsNullOrWhiteSpace(winnerSlug)) return "";
            var name = slugToName(winnerSlug);
            var (label, emoji) = domainKey.ToLowerInvariant() switch
            {
                "liver" => ("liver", "🧬"),
                "kidney" => ("kidney", "💧"),
                "metabolic" => ("metabolic", "🔥"),
                "inflammation" => ("inflammation", ""),
                "immune" => ("immune", "🛡️"),
                _ => ("domain", "")
            };
            var line1 = string.IsNullOrEmpty(emoji)
                ? $"{name} currently has the strongest {label} profile in the Longevity World Cup field."
                : $"{name} currently has the strongest {label} profile in the Longevity World Cup field {emoji}";
            var url = AthleteUrl(winnerSlug);
            var lines = new List<string>
            {
                line1,
                "",
                BuildAthleteCtaLine(name, url)
            };
            return Truncate(string.Join("\n", lines));
        }

        return "";
    }

    public static string Truncate(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return s[..(MaxLength - 3)] + "...";
    }

    private static string BuildAthleteCountMilestoneLine(int count, string countLabel)
    {
        return count switch
        {
            10 => $"{countLabel} athletes are now on the leaderboard, our first two digit checkpoint.",
            42 => $"{countLabel} athletes are now on the leaderboard, a number with legendary status in pop culture.",
            69 => $"{countLabel} athletes are now on the leaderboard, one of the internet's most recognizable numbers.",
            100 => $"{countLabel} athletes are now on the leaderboard, marking the first triple digit milestone.",
            123 => $"{countLabel} athletes are now on the leaderboard, counting up in order.",
            256 => $"{countLabel} athletes are now on the leaderboard, a clean power of two milestone.",
            300 => $"{countLabel} athletes are now on the leaderboard, a proper Sparta-number moment.",
            404 => $"{countLabel} athletes are now on the leaderboard, and this time nothing is missing.",
            500 => $"{countLabel} athletes are now on the leaderboard, a solid half K milestone.",
            666 => $"{countLabel} athletes are now on the leaderboard, the classic devil number milestone.",
            777 => $"{countLabel} athletes are now on the leaderboard, a classic lucky number milestone.",
            1000 => $"{countLabel} athletes are now on the leaderboard, marking the first four digit milestone.",
            1337 => $"{countLabel} athletes are now on the leaderboard, a nod to early internet culture.",
            1618 => $"{countLabel} athletes are now on the leaderboard, right on the golden ratio milestone.",
            2000 => $"{countLabel} athletes are now on the leaderboard, and the board just crossed 2K.",
            3141 => $"{countLabel} athletes are now on the leaderboard, a neat Pi adjacent milestone.",
            5000 => $"{countLabel} athletes are now on the leaderboard, marking a major growth milestone.",
            6969 => $"{countLabel} athletes are now on the leaderboard, a true meme number milestone.",
            9001 => $"{countLabel} athletes are now on the leaderboard, comfortably over nine thousand.",
            10000 => $"{countLabel} athletes are now on the leaderboard, marking the first five digit milestone.",
            _ => $"{countLabel} athletes are now on the leaderboard."
        };
    }

    private static string LeagueDisplay(string? cat, string? val)
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
}






