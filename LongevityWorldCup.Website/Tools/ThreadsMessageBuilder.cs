using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class ThreadsMessageBuilder
{
    private const int MaxLength = 500;
    private const string LeaderboardUrl = "https://longevityworldcup.com/leaderboard";

    private static string AthleteUrl(string slug, string? leagueCtxSlug = null)
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

    private static string LeagueUrl(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        var key = $"{c}|{v}";
        if (CatValToSlug.TryGetValue(key, out var slug) && LeagueBySlug.TryGetValue(slug, out var league))
            return league.Url;
        return LeaderboardUrl;
    }

    private static string? LeagueContextSlug(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();
        var key = $"{c}|{v}";
        return CatValToSlug.TryGetValue(key, out var slug) ? slug : null;
    }

    private static string BuildAthleteCtaLine(string athleteName, string url)
    {
        return url;
    }

    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis = null,
        Func<string, int?>? getFieldSizeForLeague = null,
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
            return RejectIfTooLong($"{lead}\n\n{LeaderboardUrl}");
        }

        var eventBasis = DetermineSampleBasisForEvent(type, rawText);
        var eventLeagueScope = DetermineLeagueScopeForEvent(type, rawText);
        var eventPhase = GetPhase(sampleForBasis, eventBasis, getFieldSizeForLeague, eventLeagueScope);
        if (ShouldSuppressEvent(type, rawText, eventPhase))
            return "";

        if (type == EventType.NewRank)
        {
            if (!EventHelpers.TryExtractRank(rawText, out var rank) || rank < 1 || rank > 3) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var rankSlug)) return "";
            var current = slugToName(rankSlug);
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            var newRankMsg = BuildUltimateRankLine(eventPhase, rank, current, prev);
            return RejectIfTooLong(newRankMsg);
        }

        if (type == EventType.BecamePro)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var proSlug)) return "";
            var athlete = slugToName(proSlug);
            return RejectIfTooLong(BuildBecameProLine(athlete, AthleteUrl(proSlug)));
        }

        if (type == EventType.BiologicalAgeImproved)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var improvementSlug)) return "";
            if (!EventHelpers.TryExtractBiologicalAgeImprovement(rawText, out var clock, out var fromAge, out var toAge)) return "";
            var athlete = slugToName(improvementSlug);
            return RejectIfTooLong(BuildBiologicalAgeImprovementLine(athlete, clock, fromAge, toAge, AthleteUrl(improvementSlug)));
        }

        if (type == EventType.CrowdAgeTop10Change)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var crowdSlug)) return "";
            if (!EventHelpers.TryExtractCrowdAgeTop10Change(rawText, out var crowdPlace, out var previousPlace, out var crowdAge, out var crowdCount)) return "";
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var athlete = slugToName(crowdSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            return RejectIfTooLong(BuildCrowdAgeTop10Line(athlete, crowdPlace, previousPlace, prev, crowdAge, crowdCount, getChronoAgeForSlug?.Invoke(crowdSlug), AthleteUrl(crowdSlug, "crowd")));
        }

        if (type == EventType.AgeImprovementTop10Change)
        {
            if (!EventHelpers.TryExtractSlug(rawText, out var improvementLeaderboardSlug)) return "";
            if (!EventHelpers.TryExtractAgeImprovementTop10Change(rawText, out var improvementClock, out var improvementPlace, out var previousPlace, out var improvement, out _)) return "";
            EventHelpers.TryExtractPrev(rawText, out var prevSlug);
            var athlete = slugToName(improvementLeaderboardSlug);
            var prev = !string.IsNullOrWhiteSpace(prevSlug) ? slugToName(prevSlug) : null;
            var leagueCtxSlug = string.Equals(improvementClock, "bortz", StringComparison.OrdinalIgnoreCase) ? "bortz-improvement" : "improvement";
            return RejectIfTooLong(BuildAgeImprovementTop10Line(athlete, improvementClock, improvementPlace, previousPlace, prev, improvement, AthleteUrl(improvementLeaderboardSlug, leagueCtxSlug)));
        }

        if (type != EventType.BadgeAward) return "";

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label)) return "";
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(normLabel, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var phenoPlace) || phenoPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var phenoSlug)) return "";
            var phenoAthlete = slugToName(phenoSlug);
            var phenoAge = getLowestPhenoAgeForSlug?.Invoke(phenoSlug);
            var ageStr = phenoAge.HasValue ? $" at {phenoAge.Value.ToString("0.#", CultureInfo.InvariantCulture)} years" : "";
            var athleteUrl = AthleteUrl(phenoSlug);
            var line = BuildLowestAgeLine(
                eventPhase,
                athleteName: phenoAthlete,
                metricName: "pheno age",
                cohortLabel: "amateur",
                ageStr: ageStr);
            return RejectIfTooLong($"{line}\n\n{BuildAthleteCtaLine(phenoAthlete, athleteUrl)}");
        }
        if (string.Equals(normLabel, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var phenoImprovementPlace) || phenoImprovementPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var diffSlug)) return "";
            var diffVal = getPhenoDiffForSlug?.Invoke(diffSlug);
            if (!diffVal.HasValue) return "";
            var years = Math.Abs(diffVal.Value);
            var yearsStr = years.ToString("0.#", CultureInfo.InvariantCulture);
            var athlete = slugToName(diffSlug);
            var url = AthleteUrl(diffSlug);
            var line = BuildImprovementLine(
                eventPhase,
                athleteName: athlete,
                metricName: "pheno age",
                cohortLabel: "amateur",
                yearsStr: yearsStr);
            return RejectIfTooLong($"{line}\n\n{BuildAthleteCtaLine(athlete, url)}");
        }

        if (string.Equals(normLabel, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var bortzPlace) || bortzPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var bortzSlug)) return "";
            var bortzAthlete = slugToName(bortzSlug);
            var bortzAge = getLowestBortzAgeForSlug?.Invoke(bortzSlug);
            var ageStr = bortzAge.HasValue ? $" at {bortzAge.Value.ToString("0.#", CultureInfo.InvariantCulture)} years" : "";
            var athleteUrl = AthleteUrl(bortzSlug);
            var line = BuildLowestAgeLine(
                eventPhase,
                athleteName: bortzAthlete,
                metricName: "bortz age",
                cohortLabel: "pro",
                ageStr: ageStr);
            return RejectIfTooLong($"{line}\n\n{BuildAthleteCtaLine(bortzAthlete, athleteUrl)}");
        }

        if (string.Equals(normLabel, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var bortzImprovementPlace) || bortzImprovementPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var diffSlug)) return "";
            var diffVal = getBortzDiffForSlug?.Invoke(diffSlug);
            if (!diffVal.HasValue) return "";
            var years = Math.Abs(diffVal.Value);
            var yearsStr = years.ToString("0.#", CultureInfo.InvariantCulture);
            var athlete = slugToName(diffSlug);
            var url = AthleteUrl(diffSlug);
            var line = BuildImprovementLine(
                eventPhase,
                athleteName: athlete,
                metricName: "bortz age",
                cohortLabel: "pro",
                yearsStr: yearsStr);
            return RejectIfTooLong($"{line}\n\n{BuildAthleteCtaLine(athlete, url)}");
        }

        if (string.Equals(normLabel, "Chronological age – oldest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var chronoOldestPlace) || chronoOldestPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return RejectIfTooLong($"{chronoAthlete} is the oldest athlete in the current Longevity World Cup field at {ageStr}.\n\n{BuildAthleteCtaLine(chronoAthlete, url)}");
        }

        if (string.Equals(normLabel, "Chronological age – youngest", StringComparison.OrdinalIgnoreCase))
        {
            if (!EventHelpers.TryExtractPlace(rawText, out var chronoYoungestPlace) || chronoYoungestPlace != 1) return "";
            if (!EventHelpers.TryExtractSlug(rawText, out var chronoSlug)) return "";
            var chronoAge = getChronoAgeForSlug?.Invoke(chronoSlug);
            if (!chronoAge.HasValue) return "";
            var chronoAthlete = slugToName(chronoSlug);
            var ageStr = chronoAge.Value.ToString("0", CultureInfo.InvariantCulture);
            var url = AthleteUrl(chronoSlug);
            return RejectIfTooLong($"{chronoAthlete} is the youngest athlete in the current Longevity World Cup field at {ageStr}.\n\n{BuildAthleteCtaLine(chronoAthlete, url)}");
        }

        if (string.Equals(normLabel, "Age reduction", StringComparison.OrdinalIgnoreCase)
            && EventHelpers.TryExtractPlace(rawText, out var place) && place == 1
            && EventHelpers.TryExtractCategory(rawText, out var leagueCat) && !string.Equals(leagueCat, "Global", StringComparison.OrdinalIgnoreCase)
            && EventHelpers.TryExtractSlug(rawText, out var leagueSlug))
        {
            EventHelpers.TryExtractValue(rawText, out var leagueVal);
            var leagueName = LeagueDisplay(leagueCat, leagueVal);
            if (string.IsNullOrWhiteSpace(leagueName)) return "";
            var leagueAthlete = slugToName(leagueSlug);
            var leagueCtxSlug = LeagueContextSlug(leagueCat, leagueVal);
            var athleteUrl = AthleteUrl(leagueSlug, leagueCtxSlug);
            EventHelpers.TryExtractPrev(rawText, out var leaguePrevSlug);
            var leaguePrev = !string.IsNullOrWhiteSpace(leaguePrevSlug) ? slugToName(leaguePrevSlug) : null;
            var msg = BuildLeagueLeaderLine(eventPhase, leagueAthlete, leagueName, leaguePrev, athleteUrl);
            return RejectIfTooLong(msg);
        }

        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase)) return "";

        if (!EventHelpers.TryExtractSlug(rawText, out var guestSlug)) return "";

        var guest = slugToName(guestSlug);
        var podcastUrl = getPodcastLinkForSlug?.Invoke(guestSlug);
        if (string.IsNullOrWhiteSpace(podcastUrl)) return "";

        const string host = "@nopara73";

        return RejectIfTooLong(
            $"New Longevity World Cup podcast.\n" +
            $"{host} talks with {guest} in the latest episode. \U0001F3A7\n\n" +
            $"{podcastUrl}");
    }

    public static string ForFiller(
        FillerType fillerType,
        string payloadText,
        Func<string, string> slugToName,
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis = null,
        Func<string, int?>? getFieldSizeForLeague = null,
        Func<string, int?>? getBortzFieldSizeForLeague = null,
        Func<string, IReadOnlyList<string>>? getTop3SlugsForLeague = null,
        Func<IReadOnlyList<(int Place, IReadOnlyList<string> Slugs)>>? getCrowdLowestAgePodium = null,
        Func<IReadOnlyList<string>>? getRecentNewcomersForX = null,
        Func<string, string?>? getBestDomainWinnerSlug = null)
    {
        var fillerBasis = DetermineSampleBasisForFiller(fillerType, payloadText ?? "");
        var fillerLeagueScope = DetermineLeagueScopeForFiller(fillerType, payloadText ?? "");
        var fillerPhase = fillerType == FillerType.Top3Leaderboard
            ? GetTop3LeaderboardPhase(payloadText ?? "", sampleForBasis, getFieldSizeForLeague, getBortzFieldSizeForLeague)
            : GetPhase(sampleForBasis, fillerBasis, getFieldSizeForLeague, fillerLeagueScope);
        if (ShouldSuppressFiller(fillerType, fillerPhase))
            return "";

        if (fillerType == FillerType.HistoryDocument)
            return RejectIfTooLong(HistoryDocumentReminderPost.BuildText());

        if (fillerType == FillerType.Ruleset)
            return RejectIfTooLong(RulesetReminderPost.BuildText());

        if (fillerType == FillerType.GitHubRepository)
            return RejectIfTooLong(GitHubRepositoryReminderPost.BuildText());

        if (fillerType == FillerType.Donation)
            return RejectIfTooLong(DonationReminderPost.BuildText());

        if (fillerType == FillerType.Top3Leaderboard)
        {
            if (!EventHelpers.TryExtractLeague(payloadText ?? "", out var leagueSlug) || string.IsNullOrWhiteSpace(leagueSlug))
                return "";
            if (!LeagueBySlug.TryGetValue(leagueSlug.Trim(), out var league))
                return "";
            var slugs = getTop3SlugsForLeague?.Invoke(leagueSlug.Trim()) ?? Array.Empty<string>();
            if (slugs.Count == 0) return "";
            var lines = new List<string> { BuildTop3LeaderboardIntro(fillerPhase, league.DisplayName), "" };
            for (var i = 0; i < slugs.Count && i < 3; i++)
                lines.Add($"{i + 1}. {slugToName(slugs[i])}");
            lines.Add("");
            lines.Add($"{league.Url}");
            return RejectIfTooLong(string.Join("\n", lines));
        }

        if (fillerType == FillerType.CrowdGuesses)
        {
            var podium = getCrowdLowestAgePodium?.Invoke() ?? Array.Empty<(int Place, IReadOnlyList<string> Slugs)>();
            if (podium.Count == 0) return "";
            var lines = new List<string> { "The crowd's current top 3 for youngest-looking in the tournament \U0001F440", "" };
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
            lines.Add($"{LeaderboardUrl}");
            return RejectIfTooLong(string.Join("\n", lines));
        }

        if (fillerType == FillerType.Newcomers)
        {
            var newcomers = getRecentNewcomersForX?.Invoke() ?? Array.Empty<string>();
            if (newcomers.Count == 0) return "";
            var lines = new List<string>
            {
                "A few new names just landed on the Longevity World Cup leaderboard \U0001F195",
                "",
                LeaderboardUrl
            };
            return RejectIfTooLong(string.Join("\n", lines));
        }

        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText ?? "", out var domainKey) || string.IsNullOrWhiteSpace(domainKey))
                return "";
            var isEarly = fillerPhase is XPostPhase.Tiny or XPostPhase.Early;
            var winnerSlug = getBestDomainWinnerSlug?.Invoke(domainKey.Trim());
            if (string.IsNullOrWhiteSpace(winnerSlug)) return "";
            var name = slugToName(winnerSlug);
            var (label, emoji, clockLabel) = domainKey.ToLowerInvariant() switch
            {
                "liver" => ("liver", "\U0001F9EC", "Bortz"),
                "kidney" => ("kidney", "\U0001F4A7", "Bortz"),
                "metabolic" => ("metabolic", "\U0001F525", "Bortz"),
                "inflammation" => ("inflammation", "", "CRP"),
                "immune" => ("immune", "\U0001F6E1\uFE0F", "Bortz"),
                "vitamin_d" => ("vitamin D", "\u2600\uFE0F", "Bortz"),
                _ => ("domain", "", "Bortz")
            };
            var line1 = isEarly
                ? BuildEarlyDomainLine(name, label, emoji, clockLabel, fillerBasis)
                : string.IsNullOrEmpty(emoji)
                    ? BuildMatureDomainLine(name, label, clockLabel)
                    : $"{BuildMatureDomainLine(name, label, clockLabel)} {emoji}";
            var url = AthleteUrl(winnerSlug);
            var lines = new List<string>
            {
                line1,
                "",
                BuildAthleteCtaLine(name, url)
            };
            return RejectIfTooLong(string.Join("\n", lines));
        }

        return "";
    }

    public static string RejectIfTooLong(string s)
    {
        if (string.IsNullOrEmpty(s) || s.Length <= MaxLength) return s;
        return "";
    }

    private static string BuildAthleteCountMilestoneLine(int count, string countLabel)
    {
        return count switch
        {
            10 => $"{countLabel} athletes are now on the leaderboard. First double digits.",
            42 => $"{countLabel} athletes are now on the leaderboard. A number with some legendary baggage.",
            69 => $"{countLabel} athletes are now on the leaderboard. The internet will notice.",
            100 => $"{countLabel} athletes are now on the leaderboard. First triple-digit mark.",
            123 => $"{countLabel} athletes are now on the leaderboard. Nice clean count.",
            200 => $"{countLabel} athletes are now on the leaderboard.",
            222 => $"{countLabel} athletes are now on the leaderboard. Perfectly doubled.",
            250 => $"{countLabel} athletes are now on the leaderboard.",
            256 => $"{countLabel} athletes are now on the leaderboard. A tidy power-of-two milestone.",
            300 => $"{countLabel} athletes are now on the leaderboard. Sparta would approve.",
            404 => $"{countLabel} athletes are now on the leaderboard. This time nothing is missing.",
            500 => $"{countLabel} athletes are now on the leaderboard.",
            666 => $"{countLabel} athletes are now on the leaderboard. Slightly cursed milestone.",
            777 => $"{countLabel} athletes are now on the leaderboard. Lucky sevens.",
            888 => $"{countLabel} athletes are now on the leaderboard. Triple eights.",
            999 => $"{countLabel} athletes are now on the leaderboard. One away from the comma club.",
            1000 => $"{countLabel} athletes are now on the leaderboard.",
            1024 => $"{countLabel} athletes are now on the leaderboard. Proper power-of-two territory.",
            1234 => $"{countLabel} athletes are now on the leaderboard. Counting up nicely.",
            1337 => $"{countLabel} athletes are now on the leaderboard. Old internet heads will appreciate that one.",
            1500 => $"{countLabel} athletes are now on the leaderboard.",
            1618 => $"{countLabel} athletes are now on the leaderboard. Golden-ratio territory.",
            2000 => $"{countLabel} athletes are now on the leaderboard.",
            2048 => $"{countLabel} athletes are now on the leaderboard. Power-of-two territory.",
            2222 => $"{countLabel} athletes are now on the leaderboard. Twos all the way down.",
            2500 => $"{countLabel} athletes are now on the leaderboard.",
            3000 => $"{countLabel} athletes are now on the leaderboard.",
            3141 => $"{countLabel} athletes are now on the leaderboard. Very close to pi.",
            3333 => $"{countLabel} athletes are now on the leaderboard. Repeating digits, serious field.",
            4000 => $"{countLabel} athletes are now on the leaderboard.",
            4444 => $"{countLabel} athletes are now on the leaderboard. Four-four-four-four and still found.",
            5000 => $"{countLabel} athletes are now on the leaderboard. That is a real field now.",
            5555 => $"{countLabel} athletes are now on the leaderboard. Repeating fives.",
            6969 => $"{countLabel} athletes are now on the leaderboard. Yes, that number.",
            7500 => $"{countLabel} athletes are now on the leaderboard.",
            8008 => $"{countLabel} athletes are now on the leaderboard. Calculator humor survived.",
            8888 => $"{countLabel} athletes are now on the leaderboard. Jackpot-adjacent.",
            9001 => $"{countLabel} athletes are now on the leaderboard. Over nine thousand.",
            9999 => $"{countLabel} athletes are now on the leaderboard. One short of five digits.",
            10000 => $"{countLabel} athletes are now on the leaderboard.",
            11111 => $"{countLabel} athletes are now on the leaderboard. The one key is doing overtime.",
            12345 => $"{countLabel} athletes are now on the leaderboard. Counting is officially a feature.",
            22222 => $"{countLabel} athletes are now on the leaderboard. Twos all the way down again.",
            54321 => $"{countLabel} athletes are now on the leaderboard. Countdown complete and somehow upward.",
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

    private static XPostSampleBasis? DetermineSampleBasisForEvent(EventType type, string rawText)
    {
        if (type == EventType.NewRank)
            return XPostSampleBasis.Combined;

        if (type == EventType.BecamePro)
            return XPostSampleBasis.Bortz;

        if (type == EventType.BiologicalAgeImproved)
        {
            if (!EventHelpers.TryExtractClock(rawText, out var clock))
                return null;

            return string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
                ? XPostSampleBasis.Bortz
                : XPostSampleBasis.PhenoAge;
        }

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase))
            return XPostSampleBasis.PhenoAge;

        if (string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase))
            return XPostSampleBasis.Bortz;

        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase) &&
            EventHelpers.TryExtractPlace(rawText, out var place) && place == 1 &&
            EventHelpers.TryExtractCategory(rawText, out var category) &&
            !string.Equals(category, "Global", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(category, "Amateur", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.PhenoAge;

            return XPostSampleBasis.Combined;
        }

        return null;
    }

    private static XPostSampleBasis? DetermineSampleBasisForFiller(FillerType fillerType, string payloadText)
    {
        if (fillerType == FillerType.DomainTop)
        {
            if (!EventHelpers.TryExtractDomain(payloadText, out var domain))
                return null;

            if (string.Equals(domain, "inflammation", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.Combined;

            return XPostSampleBasis.Bortz;
        }

        if (fillerType == FillerType.Top3Leaderboard)
        {
            if (!EventHelpers.TryExtractLeague(payloadText, out var league))
                return null;

            if (string.Equals(league, "amateur", StringComparison.OrdinalIgnoreCase))
                return XPostSampleBasis.PhenoAge;

            return XPostSampleBasis.Combined;
        }

        return null;
    }

    private static string? DetermineLeagueScopeForEvent(EventType type, string rawText)
    {
        if (type == EventType.NewRank)
            return "ultimate";

        if (type != EventType.BadgeAward)
            return null;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return null;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase) &&
            EventHelpers.TryExtractPlace(rawText, out var place) && place == 1 &&
            EventHelpers.TryExtractCategory(rawText, out var category))
        {
            EventHelpers.TryExtractValue(rawText, out var value);
            return LeagueContextSlug(category, value);
        }

        return null;
    }

    private static string? DetermineLeagueScopeForFiller(FillerType fillerType, string payloadText)
    {
        if (fillerType == FillerType.Top3Leaderboard &&
            EventHelpers.TryExtractLeague(payloadText, out var league) &&
            !string.IsNullOrWhiteSpace(league))
            return league.Trim();

        return null;
    }

    private static XPostPhase? GetPhase(
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis,
        XPostSampleBasis? basis,
        Func<string, int?>? getFieldSizeForLeague,
        string? leagueScope)
    {
        XPostPhase? basisPhase = null;
        if (basis.HasValue && sampleForBasis is not null)
        {
            var sample = sampleForBasis(basis.Value);
            basisPhase = XPostPhaseDecider.Determine(sample);
        }

        XPostPhase? scopePhase = null;
        if (!string.IsNullOrWhiteSpace(leagueScope) && getFieldSizeForLeague is not null)
        {
            var fieldSize = getFieldSizeForLeague(leagueScope);
            if (fieldSize.HasValue)
            {
                var scopedSample = new XPostSampleSize(
                    Basis: basis ?? XPostSampleBasis.Combined,
                    N: fieldSize.Value,
                    PhenoCount: 0,
                    BortzCount: 0,
                    CombinedCount: fieldSize.Value);
                scopePhase = XPostPhaseDecider.Determine(scopedSample);
            }
        }

        if (basisPhase.HasValue && scopePhase.HasValue)
            return XPostPhaseDecider.Min(basisPhase.Value, scopePhase.Value);

        return basisPhase ?? scopePhase;
    }

    private static XPostPhase? GetTop3LeaderboardPhase(
        string payloadText,
        Func<XPostSampleBasis, XPostSampleSize>? sampleForBasis,
        Func<string, int?>? getFieldSizeForLeague,
        Func<string, int?>? getBortzFieldSizeForLeague)
    {
        if (!EventHelpers.TryExtractLeague(payloadText, out var leagueSlug) || string.IsNullOrWhiteSpace(leagueSlug))
            return null;

        var normalizedLeague = leagueSlug.Trim();
        if (string.Equals(normalizedLeague, "amateur", StringComparison.OrdinalIgnoreCase))
            return GetPhase(sampleForBasis, XPostSampleBasis.PhenoAge, getFieldSizeForLeague, normalizedLeague);

        var totalPhase = GetPhase(null, null, getFieldSizeForLeague, normalizedLeague);
        var bortzPhase = GetPhase(null, null, getBortzFieldSizeForLeague, normalizedLeague);

        if (totalPhase.HasValue && bortzPhase.HasValue)
            return XPostPhaseDecider.Min(totalPhase.Value, bortzPhase.Value);

        return bortzPhase ?? totalPhase;
    }

    private static bool ShouldSuppressEvent(EventType type, string rawText, XPostPhase? phase)
    {
        if (!phase.HasValue || phase == XPostPhase.Mature)
            return false;

        if (type == EventType.NewRank)
        {
            return !EventHelpers.TryExtractRank(rawText, out var rank) || rank != 1;
        }

        if (type != EventType.BadgeAward)
            return false;

        if (!EventHelpers.TryExtractBadgeLabel(rawText, out var label))
            return false;

        var norm = EventHelpers.NormalizeBadgeLabel(label);
        if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(norm, "Pheno Age – lowest", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(norm, "Bortz Age – lowest", StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(norm, "Age reduction", StringComparison.OrdinalIgnoreCase))
        {
            return !(
                EventHelpers.TryExtractPlace(rawText, out var place) &&
                place == 1 &&
                EventHelpers.TryExtractCategory(rawText, out var category) &&
                !string.Equals(category, "Global", StringComparison.OrdinalIgnoreCase));
        }

        if (phase == XPostPhase.Early &&
            (string.Equals(norm, "Pheno Age best improvement", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(norm, "Bortz Age best improvement", StringComparison.OrdinalIgnoreCase)))
            return false;

        return true;
    }

    private static bool ShouldSuppressFiller(FillerType fillerType, XPostPhase? phase)
    {
        if (!phase.HasValue)
            return false;

        if (phase == XPostPhase.Mature)
            return false;

        return fillerType switch
        {
            FillerType.Top3Leaderboard => phase == XPostPhase.Tiny,
            FillerType.DomainTop => true,
            FillerType.CrowdGuesses => true,
            FillerType.Newcomers => false,
            _ => false
        };
    }

    private static string BuildLowestAgeLine(
        XPostPhase? phase,
        string athleteName,
        string metricName,
        string cohortLabel,
        string ageStr)
    {
        return phase switch
        {
            XPostPhase.Tiny => $"Early days, but {athleteName} has the lowest {metricName} among the first {cohortLabel} athletes so far{ageStr} \U0001F9EC",
            XPostPhase.Early => $"{athleteName} has the lowest {metricName} among {cohortLabel} athletes so far{ageStr} \U0001F9EC",
            _ => $"{athleteName} currently has the lowest {metricName} in Longevity World Cup{ageStr} \U0001F9EC"
        };
    }

    private static string BuildImprovementLine(
        XPostPhase? phase,
        string athleteName,
        string metricName,
        string cohortLabel,
        string yearsStr)
    {
        return phase switch
        {
            XPostPhase.Tiny => $"{athleteName} has the largest {metricName} improvement so far among the first {cohortLabel} athletes: {yearsStr} years versus the first submitted test. \U0001F9EC",
            XPostPhase.Early => $"{athleteName} is leading the current {cohortLabel} field in {metricName} improvement: {yearsStr} years better than the first submitted test. \U0001F9EC",
            _ => $"{athleteName} currently has the largest {metricName} improvement in the field: {yearsStr} years better than the first submitted test. \U0001F9EC"
        };
    }

    private static string BuildLeagueLeaderLine(
        XPostPhase? phase,
        string athleteName,
        string leagueName,
        string? previousAthlete,
        string athleteUrl)
    {
        var lead = phase switch
        {
            XPostPhase.Tiny => $"Early lead in the {leagueName}: {athleteName} \U0001F3C6",
            XPostPhase.Early => $"{athleteName} is currently #1 in the {leagueName} \U0001F3C6",
            _ => !string.IsNullOrWhiteSpace(previousAthlete)
                ? $"{athleteName} just moved into #1 in the {leagueName}, ahead of {previousAthlete} \U0001F3C6"
                : $"{athleteName} just moved into #1 in the {leagueName} \U0001F3C6"
        };

        return $"{lead}\n\n{athleteUrl}";
    }

    private static string BuildUltimateRankLine(
        XPostPhase? phase,
        int rank,
        string athleteName,
        string? previousAthlete)
    {
        var lead = phase switch
        {
            XPostPhase.Tiny => $"Early lead in the Ultimate League: {athleteName} \U0001F3C6",
            XPostPhase.Early => $"{athleteName} is currently #{rank} in the Ultimate League \U0001F3C6",
            _ => !string.IsNullOrWhiteSpace(previousAthlete)
                ? $"{athleteName} just climbed to #{rank} in the Ultimate League \U0001F3C6\nNow ahead of {previousAthlete}."
                : $"{athleteName} just climbed to #{rank} in the Ultimate League \U0001F3C6"
        };

        return phase switch
        {
            XPostPhase.Tiny => $"{lead}\n\n{LeaderboardUrl}",
            XPostPhase.Early => $"{lead}\n\n{LeaderboardUrl}",
            _ => $"{lead}\n\n{LeaderboardUrl}"
        };
    }

    private static string BuildBecameProLine(string athleteName, string athleteUrl)
    {
        return $"{athleteName} went Pro.\n\nBortz Age results now place them in the Pro track.\n\n{athleteUrl}";
    }

    private static string BuildBiologicalAgeImprovementLine(
        string athleteName,
        string clock,
        double fromAge,
        double toAge,
        string athleteUrl)
    {
        var clockLabel = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "Bortz Age"
            : "pheno age";
        var fromText = fromAge.ToString("0.##", CultureInfo.InvariantCulture);
        var toText = toAge.ToString("0.##", CultureInfo.InvariantCulture);
        return $"{athleteName} improved their {clockLabel} from {fromText} to {toText} years.\n\n{athleteUrl}";
    }

    private static string FormatSignedYears(double years)
    {
        var text = years.ToString("0.#", CultureInfo.InvariantCulture);
        return years > 0 ? $"+{text}" : text;
    }

    private static string BuildCrowdAgeTop10Line(
        string athleteName,
        int place,
        int? previousPlace,
        string? previousAthlete,
        double crowdAge,
        int crowdCount,
        double? chronologicalAge,
        string athleteUrl)
    {
        var crowdAgeText = crowdAge.ToString("0.#", CultureInfo.InvariantCulture);
        var countText = crowdCount.ToString("N0", CultureInfo.InvariantCulture);
        var movement = BuildCrowdAgeMovement(place, previousPlace);
        var signal = BuildCrowdAgeSignal(crowdAge, chronologicalAge);
        var metricLine = !string.IsNullOrWhiteSpace(signal)
            ? $"{athleteName}'s Crowd Age is {crowdAgeText}, {signal}."
            : $"{athleteName}'s Crowd Age is {crowdAgeText}.";

        return $"{athleteName} {movement} in Crowd Age with {countText} guesses.\n{metricLine}\n\n{athleteUrl}";
    }

    private static string BuildCrowdAgeMovement(int place, int? previousPlace)
    {
        var placeText = CrowdOrdinal(place);
        return previousPlace.HasValue
            ? previousPlace.Value > place
                ? $"climbed from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
                : $"moved from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
            : $"just entered the top 10 at {placeText}";
    }

    private static string? BuildCrowdAgeSignal(double crowdAge, double? chronologicalAge)
    {
        if (!chronologicalAge.HasValue || !double.IsFinite(chronologicalAge.Value))
            return null;

        var difference = crowdAge - chronologicalAge.Value;
        if (!double.IsFinite(difference))
            return null;

        if (Math.Abs(difference) < 0.05)
            return "about the same age as their chronological age";

        var years = Math.Abs(difference).ToString("0.#", CultureInfo.InvariantCulture);
        return difference < 0
            ? $"{years} years below chronological age"
            : $"{years} years above chronological age";
    }

    private static string BuildAgeImprovementTop10Line(
        string athleteName,
        string clock,
        int place,
        int? previousPlace,
        string? previousAthlete,
        double improvement,
        string athleteUrl)
    {
        var placeText = CrowdOrdinal(place);
        var movement = previousPlace.HasValue
            ? previousPlace.Value > place ? $"climbed from {CrowdOrdinal(previousPlace.Value)} to {placeText}" : $"moved from {CrowdOrdinal(previousPlace.Value)} to {placeText}"
            : place == 1 ? $"took {placeText} place" : $"entered the top 10 at {placeText}";
        var leaderboardName = string.Equals(clock, "bortz", StringComparison.OrdinalIgnoreCase)
            ? "Bortz Improvement"
            : "Pheno Improvement";
        var improvementText = FormatSignedYears(improvement);

        var lead = !string.IsNullOrWhiteSpace(previousAthlete)
            ? $"{athleteName} {movement} in the {leaderboardName} leaderboard, ahead of {previousAthlete}."
            : $"{athleteName} {movement} in the {leaderboardName} leaderboard.";

        return $"{lead}\n\nImprovement: {improvementText} years from worst to latest eligible result.\n\n{athleteUrl}";
    }

    private static string CrowdOrdinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13
            ? "th"
            : (n % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        return $"{n}{suffix}";
    }

    private static string BuildTop3LeaderboardIntro(XPostPhase? phase, string leagueDisplayName)
    {
        return phase switch
        {
            XPostPhase.Early => $"Current top 3 in the {leagueDisplayName}:",
            _ => $"{leagueDisplayName}, current top 3:"
        };
    }

    private static string BuildMatureDomainLine(string name, string label, string clockLabel)
    {
        if (string.Equals(label, "inflammation", StringComparison.OrdinalIgnoreCase))
            return $"{name} currently has the strongest inflammation profile in the Longevity World Cup field.";

        return $"{name} currently has the strongest {clockLabel} {label} profile.";
    }

    private static string BuildEarlyDomainLine(string name, string label, string emoji, string clockLabel, XPostSampleBasis? basis)
    {
        if (string.Equals(label, "inflammation", StringComparison.OrdinalIgnoreCase))
        {
            var line = $"{name} has the best inflammation profile in this early-stage field";
            return string.IsNullOrEmpty(emoji) ? line : $"{line} {emoji}";
        }

        var cohort = basis == XPostSampleBasis.PhenoAge ? "amateur" : "pro";
        var lineBase = $"{name} has the strongest {clockLabel} {label} profile among the first {cohort} athletes so far";
        return string.IsNullOrEmpty(emoji) ? lineBase : $"{lineBase} {emoji}";
    }
}








