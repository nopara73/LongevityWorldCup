using System.Globalization;
using System.Text.RegularExpressions;
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
            _ => Escape(rawText)
        };
    }

    // ------------------ Joined ------------------

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

    // ------------------ NewRank ------------------

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
                $"{currNameLink} is now {rankWithMedal}",
                $"{currNameLink} takes {rankWithMedal}",
                $"{currNameLink} secures {rankWithMedal}",
                $"{currNameLink} locks in {rankWithMedal}",
                $"{currNameLink} claims {rankWithMedal}",
                $"{currNameLink} now at {rankWithMedal}"
            );
        }

        var prevName = slugToName(prev);
        var prevNameLink = Link(AthleteUrl(prev), prevName);

        return Pick(
            $"{currNameLink} took {rankWithMedal} from {prevNameLink}",
            $"{currNameLink} grabbed {rankWithMedal} from {prevNameLink}",
            $"{currNameLink} overtook {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} edged past {prevNameLink} into {rankWithMedal}",
            $"{currNameLink} passed {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} displaced {prevNameLink} at {rankWithMedal}",
            $"{currNameLink} leapt ahead of {prevNameLink} to {rankWithMedal}",
            $"{currNameLink} snatched {rankWithMedal} from {prevNameLink}",
            $"{currNameLink} nudged ahead of {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} outpaced {prevNameLink} for {rankWithMedal}"
        );
    }

    // ------------------ Donation ------------------

    private static string BuildDonation(string rawText)
    {
        var (tx, sats) = ParseDonationTokens(rawText);
        if (sats is null || sats <= 0) return Escape(rawText);

        var btc = SatsToBtc(sats.Value);
        var btcFormatted = btc.ToString("0.########", CultureInfo.InvariantCulture);

        // Slack link: <url|label>
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

        var mTx = Regex.Match(text, @"\btx\[(?<v>[^\]]+)\]");
        if (mTx.Success) tx = mTx.Groups["v"].Value;

        var mSats = Regex.Match(text, @"\bsats\[(?<v>\d+)\]");
        if (mSats.Success && long.TryParse(mSats.Groups["v"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var s))
            sats = s;

        return (tx, sats);
    }

    private static decimal SatsToBtc(long sats) => sats / 100_000_000m;

    // ------------------ Athlete Count Milestone (Slack) ------------------
    // One distinct line per special number. Avoid sentence-ending periods.
    // Count is a clickable link to the leaderboard.

    private static string BuildAthleteCountMilestone(string rawText)
    {
        var count = ParseAthleteCount(rawText);
        if (count is null || count <= 0)
            return Escape(rawText);

        var countLabel = count.Value.ToString("N0", CultureInfo.InvariantCulture); // e.g., "1,337"
        var countLink = Link(LeaderboardUrl(), countLabel);

        return MilestoneMessage(count.Value, countLink);
    }

    private static int? ParseAthleteCount(string text)
    {
        var m = Regex.Match(text, @"\bathletes\[(?<v>\d+)\]");
        if (!m.Success) return null;
        if (int.TryParse(m.Groups["v"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            return n;
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

        // exact fallback phrasing as requested
        return $"The compatation reach {C} athletes";
    }

    private static string LeaderboardUrl() =>
        "https://longevityworldcup.com/leaderboard";

    // ------------------ helpers ------------------

    private static (string? slug, int? rank, string? prev) ParseTokens(string text)
    {
        string? slug = null;
        int? rank = null;
        string? prev = null;

        var mSlug = Regex.Match(text, @"slug\[(?<v>[^\]]+)\]");
        if (mSlug.Success) slug = mSlug.Groups["v"].Value;

        var mRank = Regex.Match(text, @"rank\[(?<v>\d+)\]");
        if (mRank.Success && int.TryParse(mRank.Groups["v"].Value, out var r)) rank = r;

        var mPrev = Regex.Match(text, @"prev\[(?<v>[^\]]+)\]");
        if (mPrev.Success) prev = mPrev.Groups["v"].Value;

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

    private static string Pick(params string[] options) =>
        options.Length == 0 ? "" : options[Random.Shared.Next(options.Length)];
}
