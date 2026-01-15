using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class SlackMessageBuilder
{
    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, int?>? getRankForSlug = null,
        Func<string, string?>? getPodcastLinkForSlug = null)
    {
        var (slug, rank, prev) = ParseTokens(rawText);
        return type switch
        {
            EventType.NewRank => BuildNewRank(slug, rank, prev, slugToName),
            EventType.Joined => BuildJoined(slug, slugToName),
            EventType.DonationReceived => BuildDonation(rawText),
            EventType.AthleteCountMilestone => BuildAthleteCountMilestone(rawText),
            EventType.BadgeAward => BuildBadgeAward(rawText, slugToName, getPodcastLinkForSlug),
            EventType.CustomEvent => BuildCustomEvent(rawText),
            EventType.General => BuildCustomEvent(rawText),
            _ => Escape(rawText)
        };
    }

    public static string ForMergedGroup(
        IEnumerable<(EventType Type, string Raw)> items,
        Func<string, string> slugToName,
        Func<string, double?>? getChronoAgeForSlug = null,
        Func<string, double?>? getLowestPhenoAgeForSlug = null,
        Func<string, string?>? getPodcastLinkForSlug = null)
    {
        var list = items as List<(EventType Type, string Raw)> ?? items.ToList();

        string slug = "";
        foreach (var it in list)
        {
            if (EventHelpers.TryExtractSlug(it.Raw, out var s) && !string.IsNullOrWhiteSpace(s))
            {
                slug = s;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(slug)) return "";

        var name = slugToName(slug);
        var nameLink = Link(AthleteUrl(slug), name);

        int? newRank = null;
        string? prevSlug = null;

        bool hasPodcast = false;

        string? divisionVal = null;
        string? generationVal = null;
        string? exclusiveVal = null;

        bool hasPhenoLowest = false;
        bool hasChronoYoungest = false;
        bool hasChronoOldest = false;

        foreach (var it in list)
        {
            if (it.Type == EventType.NewRank)
            {
                if (EventHelpers.TryExtractRank(it.Raw, out var r)) newRank = r;
                if (EventHelpers.TryExtractPrev(it.Raw, out var p) && !string.IsNullOrWhiteSpace(p)) prevSlug = p;
                continue;
            }

            if (it.Type != EventType.BadgeAward) continue;

            EventHelpers.TryExtractBadgeLabel(it.Raw, out var label);
            EventHelpers.TryExtractCategory(it.Raw, out var cat);
            EventHelpers.TryExtractValue(it.Raw, out var val);
            EventHelpers.TryExtractPlace(it.Raw, out var place);

            var norm = EventHelpers.NormalizeBadgeLabel(label);
            if (string.IsNullOrWhiteSpace(norm)) continue;

            if (string.Equals(norm, "Podcast", StringComparison.OrdinalIgnoreCase))
            {
                hasPodcast = true;
                continue;
            }

            if (string.Equals(norm, "Age Reduction", StringComparison.OrdinalIgnoreCase))
            {
                if (place != 1) continue;

                if (string.Equals(cat, "Division", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(val)) divisionVal = val.Trim();
                    continue;
                }

                if (string.Equals(cat, "Generation", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(val)) generationVal = val.Trim();
                    continue;
                }

                if (string.Equals(cat, "Exclusive", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(val)) exclusiveVal = val.Trim();
                    continue;
                }

                continue;
            }

            if (string.Equals(norm, "PhenoAge - Lowest", StringComparison.OrdinalIgnoreCase))
            {
                if (place == 1) hasPhenoLowest = true;
                continue;
            }

            if (string.Equals(norm, "Chronological Age - Youngest", StringComparison.OrdinalIgnoreCase))
            {
                if (place == 1) hasChronoYoungest = true;
                continue;
            }

            if (string.Equals(norm, "Chronological Age - Oldest", StringComparison.OrdinalIgnoreCase))
            {
                if (place == 1) hasChronoOldest = true;
                continue;
            }
        }

        if (!newRank.HasValue || newRank.Value <= 0) return "";

        static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);

        string DivisionName(string v)
        {
            var t = (v ?? "").Trim();
            return t switch
            {
                "Men" => "Men's",
                "Women" => "Women's",
                "Open" => "Open",
                _ => t
            };
        }

        string GenerationName(string v) => (v ?? "").Trim();

        string ExclusiveName(string v) => (v ?? "").Trim();

        var leagueParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(divisionVal)) leagueParts.Add($"the {DivisionName(divisionVal)} division");
        if (!string.IsNullOrWhiteSpace(generationVal)) leagueParts.Add($"the {GenerationName(generationVal)} league");
        if (!string.IsNullOrWhiteSpace(exclusiveVal)) leagueParts.Add($"the {ExclusiveName(exclusiveVal)} league");

        string? awardText = null;

        if (leagueParts.Count == 0)
        {
            if (hasPhenoLowest)
            {
                var ph = getLowestPhenoAgeForSlug?.Invoke(slug);
                awardText = ph.HasValue
                    ? $"lowest biological age in the field at {F2(ph.Value)} years"
                    : "lowest biological age in the field";
            }
            else if (hasChronoYoungest || hasChronoOldest)
            {
                var ch = getChronoAgeForSlug?.Invoke(slug);
                if (hasChronoYoungest)
                {
                    awardText = ch.HasValue
                        ? $"youngest athlete in the field at {F2(ch.Value)} years"
                        : "youngest athlete in the field";
                }
                else
                {
                    awardText = ch.HasValue
                        ? $"oldest athlete in the field at {F2(ch.Value)} years"
                        : "oldest athlete in the field";
                }
            }
        }

        var sb = new StringBuilder();
        sb.Append("In the Ultimate League, ");
        sb.Append(nameLink);
        sb.Append(" climbed to #");
        sb.Append(newRank.Value);

        if (!string.IsNullOrWhiteSpace(prevSlug))
        {
            var prevName = slugToName(prevSlug);
            var prevLink = Link(AthleteUrl(prevSlug), prevName);
            sb.Append(", overtaking ");
            sb.Append(prevLink);
        }

        if (leagueParts.Count > 0)
        {
            sb.Append(", and is also currently #1 in ");
            sb.Append(JoinList(leagueParts));
        }
        else if (!string.IsNullOrWhiteSpace(awardText))
        {
            sb.Append(", and is also currently the ");
            sb.Append(awardText);
        }

        sb.Append('.');

        if (hasPodcast)
        {
            var podcastUrl = getPodcastLinkForSlug?.Invoke(slug);
            var episodeLink = !string.IsNullOrWhiteSpace(podcastUrl) ? Link(podcastUrl, "episode") : "episode";

            sb.Append('\n');
            sb.Append("If you're curious who ");
            sb.Append(Escape(name));
            sb.Append(" is beyond the stats, check out the new podcast ");
            sb.Append(episodeLink);
        }

        return sb.ToString();
    }

    static string JoinList(List<string> parts)
    {
        if (parts.Count == 0) return "";
        if (parts.Count == 1) return parts[0];
        if (parts.Count == 2) return $"{parts[0]} and {parts[1]}";
        var allButLast = string.Join(", ", parts.Take(parts.Count - 1));
        return $"{allButLast} and {parts.Last()}";
    }

    private static string BuildJoined(string? slug, Func<string, string> slugToName)
    {
        if (slug is null) return "A new athlete joined the leaderboard";
        var name = slugToName(slug);
        var nameLink = Link(AthleteUrl(slug), name);
        return Pick(
            $"{nameLink} joined the leaderboard",
            $"Welcome {nameLink} to the leaderboard",
            $"New contender: {nameLink} just joined the leaderboard",
            $"{nameLink} has entered the leaderboard",
            $"Say hi to {nameLink} - new on the leaderboard",
            $"{nameLink} steps onto the leaderboard",
            $"{nameLink} appears on the leaderboard",
            $"{nameLink} is now on the leaderboard",
            $"{nameLink} just made the leaderboard",
            $"A warm welcome to {nameLink} on the leaderboard"
        );
    }

    private static string BuildNewRank(
        string? slug,
        int? rank,
        string? prev,
        Func<string, string> slugToName)
    {
        if (slug is null || !rank.HasValue) return Escape($"rank update: {slug} -> {rank}");

        var currName = slugToName(slug);
        var currNameLink = Link(AthleteUrl(slug), currName);
        var ord = Ordinal(rank.Value);
        var medal = MedalOrTrend(rank.Value);

        var rankWithMedal = $"{ord}{medal}";

        if (prev is null)
        {
            return Pick(
                $"{currNameLink} is now {rankWithMedal} in Ultimate League",
                $"{currNameLink} takes {rankWithMedal} in Ultimate League",
                $"{currNameLink} secures {rankWithMedal} in Ultimate League",
                $"{currNameLink} locks in {rankWithMedal} in Ultimate League",
                $"{currNameLink} claims {rankWithMedal} in Ultimate League",
                $"{currNameLink} now at {rankWithMedal} in Ultimate League"
            );
        }

        var prevName = slugToName(prev);
        var prevNameLink = Link(AthleteUrl(prev), prevName);

        return Pick(
            $"{currNameLink} took {rankWithMedal} in Ultimate League from {prevNameLink}",
            $"{currNameLink} grabbed {rankWithMedal} in Ultimate League from {prevNameLink}",
            $"{currNameLink} overtook {prevNameLink} for {rankWithMedal} in Ultimate League",
            $"{currNameLink} edged past {prevNameLink} into {rankWithMedal} in Ultimate League",
            $"{currNameLink} passed {prevNameLink} for {rankWithMedal} in Ultimate League",
            $"{currNameLink} displaced {prevNameLink} at {rankWithMedal} in Ultimate League",
            $"{currNameLink} leapt ahead of {prevNameLink} to {rankWithMedal} in Ultimate League",
            $"{currNameLink} snatched {rankWithMedal} from {prevNameLink} in Ultimate League",
            $"{currNameLink} nudged ahead of {prevNameLink} for {rankWithMedal} in Ultimate League",
            $"{currNameLink} outpaced {prevNameLink} for {rankWithMedal} in Ultimate League"
        );
    }

    private static string BuildDonation(string rawText)
    {
        var (tx, sats) = ParseDonationTokens(rawText);
        if (sats is null || sats <= 0) return Escape(rawText);

        var btc = SatsToBtc(sats.Value);
        var btcFormatted = btc.ToString("0.########", CultureInfo.InvariantCulture);

        string donationUrl = "https://longevityworldcup.com/#donation-section";
        string amountMd = $"<{donationUrl}|{btcFormatted} BTC>";

        const string Gap = "  ";
        return Pick(
            $"Someone has donated {amountMd}{Gap}:tada:",
            $"Donation of {amountMd} received{Gap}:tada:",
            $"A generous donor contributed {amountMd}{Gap}:raised_hands:",
            $"We just received {amountMd} - thank you{Gap}:yellow_heart:",
            $"Support came in: {amountMd}{Gap}:rocket:",
            $"{amountMd} donated - much appreciated{Gap}:sparkles:",
            $"New donation: {amountMd}{Gap}:dizzy:",
            $"Thanks for the {amountMd} gift{Gap}:pray:",
            $"A kind supporter sent {amountMd}{Gap}:gift:",
            $"Donation confirmed: {amountMd}{Gap}:white_check_mark:",
            $"Appreciate your support - {amountMd}{Gap}:star2:",
            $"Your generosity fueled us: {amountMd}{Gap}:fire:"
        );
    }

    private static (string? tx, long? sats) ParseDonationTokens(string text)
    {
        string? tx = null;
        long? sats = null;

        if (EventHelpers.TryExtractTx(text, out var txOut)) tx = txOut;
        if (EventHelpers.TryExtractSats(text, out var satsOut)) sats = satsOut;

        return (tx, sats);
    }

    private static decimal SatsToBtc(long sats) => sats / 100_000_000m;

    private static string BuildAthleteCountMilestone(string rawText)
    {
        var count = ParseAthleteCount(rawText);
        if (count is null || count <= 0)
            return Escape(rawText);

        var countLabel = count.Value.ToString("N0", CultureInfo.InvariantCulture);
        var countLink = Link(LeaderboardUrl(), countLabel);

        return MilestoneMessage(count.Value, countLink);
    }

    private static int? ParseAthleteCount(string text)
    {
        if (EventHelpers.TryExtractAthleteCount(text, out var n)) return n;
        return null;
    }

    private static string MilestoneMessage(int n, string C)
    {
        switch (n)
        {
            case 42: return $"{C} athletes - the answer to life, the universe & everything ✨";
            case 69: return $"{C} athletes - nice 😏";
            case 100: return $"Hit {C} on the leaderboard, triple digits 🏁";
            case 123: return $"Counted up to {C} contenders in the tournament 🔢";
            case 256: return $"Power of two - {C} competitors in the bracket 💻";
            case 300: return $"{C} in the tournament - This is Sparta! 🛡️";
            case 404: return $"Logged {C} in the competition - athlete not found? found 🔎";
            case 500: return $"Crossed {C}, half-K competing 🚀";
            case 666: return $"Hit {C} athletes - beast mode 😈";
            case 777: return $"Lucky sevens, {C} athletes on the leaderboard 🍀";
            case 1000: return $"Reached {C}, the big 1K competing 🏆";
            case 1337: return $"Leet level - {C} contenders in play 🕹️";
            case 1500: return $"Passed {C}, a solid field in the tournament 🧱";
            case 1618: return $"Golden-ratio vibes at {C} in the competition 🌀";
            case 2000: return $"Cleared {C} - 2K participants in contention 🎯";
            case 3141: return $"Slice of π, {C} now on the board 🥧";
            case 5000: return $"Press-worthy surge - {C} athletes in the tournament 📰";
            case 6969: return $"Meme tier unlocked, {C} competitors 🔓";
            case 10000: return $"Five digits strong - {C} in the competition 💪";
        }

        if (n > 9000 && n < 10000)
            return $"Over nine thousand, {C} in the tournament 🔥";

        return $"The compatation reach {C} athletes";
    }

    private static string LeaderboardUrl() =>
        "https://longevityworldcup.com/leaderboard";

    private static string BuildBadgeAward(string rawText, Func<string, string> slugToName, Func<string, string?>? getPodcastLinkForSlug = null)
    {
        if (!EventHelpers.TryExtractSlug(rawText, out var slug)) return Escape(rawText);

        EventHelpers.TryExtractBadgeLabel(rawText, out var label);
        var normLabel = EventHelpers.NormalizeBadgeLabel(label);

        if (!string.Equals(normLabel, "Podcast", StringComparison.OrdinalIgnoreCase))
            return "";

        _ = slugToName;

        var displaySlug = slug.Replace('_', '-');
        var slugLink = Link(AthleteUrl(slug), displaySlug);

        var podcastUrl = getPodcastLinkForSlug?.Invoke(slug);
        var episodeLink = !string.IsNullOrWhiteSpace(podcastUrl) ? Link(podcastUrl, "episode") : "episode";

        return Pick(
            $"{slugLink} just dropped in on a brand new podcast {episodeLink}",
            $"Fresh {episodeLink} out now featuring {slugLink}",
            $"New podcast {episodeLink} with {slugLink} on the mic",
            $"{slugLink} takes the spotlight in our latest podcast {episodeLink}",
            $"Hear {slugLink} in a newly released podcast {episodeLink}"
        );
    }

    private static string LeagueDisplay(string? cat, string? val)
    {
        var c = (cat ?? "").Trim();
        var v = (val ?? "").Trim();

        if (string.Equals(c, "Global", StringComparison.OrdinalIgnoreCase))
            return "Ultimate League";

        if (c == "Division")
        {
            return v switch
            {
                "Men" => "Men's Division",
                "Women" => "Women's Division",
                "Open" => "Open Division",
                _ => $"{v} Division"
            };
        }

        if (c == "Generation")
        {
            return v switch
            {
                "Silent Generation" => "Silent Generation",
                "Baby Boomers" => "Baby Boomers Generation",
                "Gen X" => "Gen X Generation",
                "Millennials" => "Millennials Generation",
                "Gen Z" => "Gen Z Generation",
                "Gen Alpha" => "Gen Alpha Generation",
                _ => $"{v} Generation"
            };
        }

        if (c == "Exclusive")
        {
            return "Prosperan Exclusive League";
        }

        if (string.IsNullOrWhiteSpace(c) && string.IsNullOrWhiteSpace(v)) return "league";
        if (string.IsNullOrWhiteSpace(c)) return v;
        if (string.IsNullOrWhiteSpace(v)) return c;
        return $"{v} {c}";
    }

    private static (string? slug, int? rank, string? prev) ParseTokens(string text)
    {
        string? slug = null;
        int? rank = null;
        string? prev = null;

        if (EventHelpers.TryExtractSlug(text, out var s)) slug = s;
        if (EventHelpers.TryExtractRank(text, out var r)) rank = r;
        if (EventHelpers.TryExtractPrev(text, out var p)) prev = p;

        return (slug, rank, prev);
    }

    private static string AthleteUrl(string slug) =>
        $"https://longevityworldcup.com/athlete/{slug.Replace('_', '-')}";

    private static string Link(string url, string text) => $"<{url}|{Escape(text)}>";

    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Ordinal(int n)
    {
        if (n % 100 is 11 or 12 or 13) return $"{n}th";
        return (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
    }

    private static string MedalOrTrend(int n) =>
        n switch { 1 => " 🥇", 2 => " 🥈", 3 => " 🥉", _ => "" };

    private static string RankWithMedal(int? place)
    {
        var o = place > 0 ? Ordinal(place!.Value) : "ranked";
        var m = place > 0 ? MedalOrTrend(place!.Value) : "";
        return $"{o}{m}";
    }

    private static string Pick(params string[] options) =>
        options.Length == 0 ? "" : options[Random.Shared.Next(options.Length)];

    private static string BuildCustomEvent(string rawText)
    {
        if (string.IsNullOrEmpty(rawText)) return "";

        var s = rawText.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();

        var idx = s.IndexOf("\n\n", StringComparison.Ordinal);
        var title = (idx >= 0 ? s.Substring(0, idx) : s).Trim();
        var content = (idx >= 0 ? s.Substring(idx + 2) : "").TrimEnd();

        var titleEsc = ApplyCustomMarkupToSlack(Escape(title));

        if (string.IsNullOrWhiteSpace(content))
            return titleEsc;

        var contentEsc = ApplyCustomMarkupToSlack(Escape(content));
        return titleEsc + "\n\n" + contentEsc;
    }

    private static string ApplyCustomMarkupToSlack(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        s = Regex.Replace(
            s,
            @"\[strong\]\(([^()]*)\)",
            m => $"*_{m.Groups[1].Value}_*",
            RegexOptions.CultureInvariant);

        s = Regex.Replace(
            s,
            @"\[bold\]\(([^()]*)\)",
            m => $"*{m.Groups[1].Value}*",
            RegexOptions.CultureInvariant);

        s = Regex.Replace(
            s,
            @"\[([^\[\]]+)\]\((https?:[^)\s]+)\)",
            m => $"<{m.Groups[2].Value}|{m.Groups[1].Value}>",
            RegexOptions.CultureInvariant);

        return s;
    }
}