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
            EventType.NewRank          => BuildNewRank(slug, rank, prev, slugToName),
            EventType.Joined           => BuildJoined(slug, slugToName),
            EventType.DonationReceived => BuildDonation(rawText),
            _                          => Escape(rawText)
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
            $"{currNameLink} grabs {rankWithMedal} from {prevNameLink}",
            $"{currNameLink} overtakes {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} edges past {prevNameLink} into {rankWithMedal}",
            $"{currNameLink} passes {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} displaces {prevNameLink} at {rankWithMedal}",
            $"{currNameLink} leaps ahead of {prevNameLink} to {rankWithMedal}",
            $"{currNameLink} snatches {rankWithMedal} from {prevNameLink}",
            $"{currNameLink} nudges ahead of {prevNameLink} for {rankWithMedal}",
            $"{currNameLink} outpaces {prevNameLink} for {rankWithMedal}"
        );
    }

    private static string BuildDonation(string rawText)
    {
        var (tx, sats) = ParseDonationTokens(rawText);
        if (sats is null || sats <= 0) return Escape(rawText);

        var btc = SatsToBtc(sats.Value);
        var btcFormatted = btc.ToString("0.########", CultureInfo.InvariantCulture);

        // Build Slack-styled link: <url|label>. Do NOT HTML-encode this string for Slack.
        // (Redirect to the donation section instead of mempool.)
        string donationUrl = "https://longevityworldcup.com/#donation-section";
        string amountMd = $"<{donationUrl}|{btcFormatted} BTC>";

        // Extra visual space before emoji (thin no-break spaces). If too subtle, use "\u2003".
        const string GapBeforeEmoji = "  ";

        return Pick(
            $"Someone has donated {amountMd}{GapBeforeEmoji}:tada:",
            $"Donation of {amountMd} received{GapBeforeEmoji}:tada:",
            $"A generous donor contributed {amountMd}{GapBeforeEmoji}:raised_hands:",
            $"We just received {amountMd} — thank you{GapBeforeEmoji}:yellow_heart:",
            $"Support just came in: {amountMd}{GapBeforeEmoji}:rocket:",
            $"{amountMd} donated — much appreciated{GapBeforeEmoji}:sparkles:",
            $"New donation: {amountMd}{GapBeforeEmoji}:dizzy:",
            $"Thanks for the {amountMd} gift{GapBeforeEmoji}:pray:",
            $"A kind supporter sent {amountMd}{GapBeforeEmoji}:gift:",
            $"Donation confirmed: {amountMd}{GapBeforeEmoji}:white_check_mark:",
            $"Appreciate your support — {amountMd}{GapBeforeEmoji}:star2:",
            $"Your generosity fuels us: {amountMd}{GapBeforeEmoji}:fire:"
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

    // --- existing helpers below ---

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
