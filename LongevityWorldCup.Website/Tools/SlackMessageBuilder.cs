using System.Globalization;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public static class SlackMessageBuilder
{
    public static string ForEventText(
        EventType type,
        string rawText,
        Func<string, string> slugToName,
        Func<string, int?>? getRankForSlug = null)
    {
        var (slug, rank, prev) = ParseTokens(rawText);
        return type switch
        {
            EventType.NewRank => BuildNewRank(slug, rank, prev, slugToName),
            EventType.Joined => BuildJoined(slug, slugToName),
            EventType.DonationReceived => BuildDonation(rawText),
            EventType.AthleteCountMilestone => BuildAthleteCountMilestone(rawText),
            EventType.BadgeAward => BuildBadgeAward(rawText, slugToName),
            _ => Escape(rawText)
        };
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
            $"Say hi to {nameLink} — new on the leaderboard",
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
            $"We just received {amountMd} — thank you{Gap}:yellow_heart:",
            $"Support came in: {amountMd}{Gap}:rocket:",
            $"{amountMd} donated — much appreciated{Gap}:sparkles:",
            $"New donation: {amountMd}{Gap}:dizzy:",
            $"Thanks for the {amountMd} gift{Gap}:pray:",
            $"A kind supporter sent {amountMd}{Gap}:gift:",
            $"Donation confirmed: {amountMd}{Gap}:white_check_mark:",
            $"Appreciate your support — {amountMd}{Gap}:star2:",
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
            case 42:   return $"{C} athletes — the answer to life, the universe & everything ✨";
            case 69:   return $"{C} athletes — nice 😏";
            case 100:  return $"Hit {C} on the leaderboard, triple digits 🏁";
            case 123:  return $"Counted up to {C} contenders in the tournament 🔢";
            case 256:  return $"Power of two — {C} competitors in the bracket 💻";
            case 300:  return $"{C} in the tournament — This is Sparta! 🛡️";
            case 404:  return $"Logged {C} in the competition — athlete not found? found 🔎";
            case 500:  return $"Crossed {C}, half-K competing 🚀";
            case 666:  return $"Hit {C} athletes — beast mode 😈";
            case 777:  return $"Lucky sevens, {C} athletes on the leaderboard 🍀";
            case 1000: return $"Reached {C}, the big 1K competing 🏆";
            case 1337: return $"Leet level — {C} contenders in play 🕹️";
            case 1500: return $"Passed {C}, a solid field in the tournament 🧱";
            case 1618: return $"Golden-ratio vibes at {C} in the competition 🌀";
            case 2000: return $"Cleared {C} — 2K participants in contention 🎯";
            case 3141: return $"Slice of π, {C} now on the board 🥧";
            case 5000: return $"Press-worthy surge — {C} athletes in the tournament 📰";
            case 6969: return $"Meme tier unlocked, {C} competitors 🔓";
            case 10000:return $"Five digits strong — {C} in the competition 💪";
        }

        if (n > 9000 && n < 10000)
            return $"Over nine thousand, {C} in the tournament 🔥";

        return $"The compatation reach {C} athletes";
    }

    private static string LeaderboardUrl() =>
        "https://longevityworldcup.com/leaderboard";

    private static string BuildBadgeAward(string rawText, Func<string, string> slugToName)
    {
        if (!EventHelpers.TryExtractSlug(rawText, out var slug)) return Escape(rawText);

        EventHelpers.TryExtractBadgeLabel(rawText, out var label);
        EventHelpers.TryExtractCategory(rawText, out var cat);
        EventHelpers.TryExtractValue(rawText, out var val);
        EventHelpers.TryExtractPlace(rawText, out var place);
        EventHelpers.TryExtractPrev(rawText, out var prevSlug);

        var name = slugToName(slug);
        var nameLink = Link(AthleteUrl(slug), name);

        var normLabel = EventHelpers.NormalizeBadgeLabel(label);

        if (!string.IsNullOrWhiteSpace(normLabel) && normLabel.StartsWith("Best Domain", StringComparison.Ordinal))
        {
            var domain = EventHelpers.ExtractDomainFromLabel(normLabel);
            var ord = place > 0 ? Ordinal(place) : "ranked";
            var medal = place > 0 ? MedalOrTrend(place) : "";
            var rw = $"{ord}{medal}";

            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for {domain} biomarkers from {prevLink}",
                    $"{nameLink} grabbed {rw} for {domain} biomarkers from {prevLink}",
                    $"{nameLink} claimed {rw} for {domain} biomarkers from {prevLink}",
                    $"{nameLink} overtook {prevLink} for {rw} in {domain} biomarkers"
                );
            }

            return Pick(
                $"{nameLink} is {rw} for {domain} biomarkers",
                $"{nameLink} takes {rw} for {domain} biomarkers",
                $"{nameLink} now {rw} for {domain} biomarkers",
                $"{nameLink} secures {rw} for {domain} biomarkers"
            );
        }

        if (string.Equals(normLabel, "Chronological Age - Oldest", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for oldest chronological age from {prevLink}",
                    $"{nameLink} grabbed {rw} for oldest chronological age from {prevLink}",
                    $"{nameLink} claimed {rw} for oldest chronological age from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for oldest chronological age",
                $"{nameLink} takes {rw} for oldest chronological age",
                $"{nameLink} now {rw} for oldest chronological age"
            );
        }

        if (string.Equals(normLabel, "Chronological Age - Youngest", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for youngest chronological age from {prevLink}",
                    $"{nameLink} grabbed {rw} for youngest chronological age from {prevLink}",
                    $"{nameLink} claimed {rw} for youngest chronological age from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for youngest chronological age",
                $"{nameLink} takes {rw} for youngest chronological age",
                $"{nameLink} now {rw} for youngest chronological age"
            );
        }

        if (string.Equals(normLabel, "PhenoAge - Lowest", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for lowest PhenoAge from {prevLink}",
                    $"{nameLink} grabbed {rw} for lowest PhenoAge from {prevLink}",
                    $"{nameLink} claimed {rw} for lowest PhenoAge from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for lowest PhenoAge",
                $"{nameLink} takes {rw} for lowest PhenoAge",
                $"{nameLink} now {rw} for lowest PhenoAge"
            );
        }

        if (string.Equals(normLabel, "PhenoAge Best Improvement", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for best PhenoAge improvement from {prevLink}",
                    $"{nameLink} grabbed {rw} for best PhenoAge improvement from {prevLink}",
                    $"{nameLink} claimed {rw} for best PhenoAge improvement from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for best PhenoAge improvement",
                $"{nameLink} takes {rw} for best PhenoAge improvement",
                $"{nameLink} now {rw} for best PhenoAge improvement"
            );
        }

        if (string.Equals(normLabel, "Crowd - Most Guessed", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for most crowd guesses from {prevLink}",
                    $"{nameLink} grabbed {rw} for most crowd guesses from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for most crowd guesses",
                $"{nameLink} takes {rw} for most crowd guesses"
            );
        }

        if (string.Equals(normLabel, "Crowd - Age Gap (Chrono-Crowd)", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for smallest Chrono-Crowd age gap from {prevLink}",
                    $"{nameLink} grabbed {rw} for smallest Chrono-Crowd age gap from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for smallest Chrono-Crowd age gap",
                $"{nameLink} takes {rw} for smallest Chrono-Crowd age gap"
            );
        }

        if (string.Equals(normLabel, "Crowd - Lowest Crowd Age", StringComparison.Ordinal))
        {
            var rw = RankWithMedal(place);
            if (!string.IsNullOrWhiteSpace(prevSlug))
            {
                var prevName = slugToName(prevSlug);
                var prevLink = Link(AthleteUrl(prevSlug), prevName);
                return Pick(
                    $"{nameLink} took {rw} for lowest crowd age from {prevLink}",
                    $"{nameLink} grabbed {rw} for lowest crowd age from {prevLink}"
                );
            }
            return Pick(
                $"{nameLink} is {rw} for lowest crowd age",
                $"{nameLink} takes {rw} for lowest crowd age"
            );
        }

        var league = LeagueDisplay(cat, val);
        var o = place > 0 ? Ordinal(place) : "ranked";
        var m = place > 0 ? MedalOrTrend(place) : "";
        var rwDefault = $"{o}{m}";

        if (!string.IsNullOrWhiteSpace(prevSlug))
        {
            var prevName = slugToName(prevSlug);
            var prevLink = Link(AthleteUrl(prevSlug), prevName);
            return Pick(
                $"{nameLink} took {rwDefault} in {league} from {prevLink}",
                $"{nameLink} grabbed {rwDefault} in {league} from {prevLink}",
                $"{nameLink} claimed {rwDefault} in {league} from {prevLink}",
                $"{nameLink} overtook {prevLink} for {rwDefault} in {league}"
            );
        }

        return Pick(
            $"{nameLink} is {rwDefault} in {league}",
            $"{nameLink} takes {rwDefault} in {league}",
            $"{nameLink} now {rwDefault} in {league}",
            $"{nameLink} secures {rwDefault} in {league}"
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
}
