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
            EventType.NewRank => BuildNewRank(slug, rank, prev, slugToName, getRankForSlug),
            EventType.Joined  => BuildJoined(slug, slugToName),
            _                 => Escape(rawText)
        };
    }

    private static string BuildJoined(string? slug, Func<string, string> slugToName)
    {
        if (slug is null) return "A new athlete joined the leaderboard.";
        var name = slugToName(slug);
        var nameLink = Link(AthleteUrl(slug), name);

        return Pick(
            $"{nameLink} joined the leaderboard.",
            $"Welcome {nameLink} to the leaderboard!",
            $"New contender: {nameLink} just joined the leaderboard.",
            $"{nameLink} has entered the leaderboard.",
            $"Say hi to {nameLink} — new on the leaderboard.",
            $"{nameLink} steps onto the leaderboard.",
            $"{nameLink} appears on the leaderboard.",
            $"{nameLink} is now on the leaderboard.",
            $"{nameLink} just made the leaderboard.",
            $"A warm welcome to {nameLink} on the leaderboard!"
        );
    }

    private static string BuildNewRank(
        string? slug,
        int? rank,
        string? prev,
        Func<string, string> slugToName,
        Func<string, int?>? getRankForSlug)
    {
        if (slug is null || !rank.HasValue) return Escape($"rank update: {slug} -> {rank}");

        var currName = slugToName(slug);
        var currNameLink = Link(AthleteUrl(slug), currName);
        var currRankLink = Link(RankUrl(rank.Value), $"#{rank.Value}");
        var currPair = $"{currNameLink} ({currRankLink})";
        var ord = Ordinal(rank.Value);

        if (prev is null)
        {
            return Pick(
                $"{currPair} is now {ord}.",
                $"{currPair} takes {ord} place.",
                $"{currPair} climbs to {ord}.",
                $"{currPair} moves up to {ord}.",
                $"{currPair} rises to {ord}.",
                $"{currPair} ascends to {ord}.",
                $"{currPair} secures {ord}.",
                $"{currPair} locks in {ord}.",
                $"{currPair} vaults to {ord}.",
                $"{currPair} jumps to {ord}."
            );
        }

        var prevName = slugToName(prev);
        var prevNameLink = Link(AthleteUrl(prev), prevName);
        int? prevRank = getRankForSlug?.Invoke(prev);
        if (!prevRank.HasValue) prevRank = rank.Value + 1;
        var prevRankLink = prevRank.HasValue ? Link(RankUrl(prevRank.Value), $"#{prevRank.Value}") : null;
        var prevPair = prevRankLink is null ? prevNameLink : $"{prevNameLink} ({prevRankLink})";

        return Pick(
            $"{currPair} took {ord} from {prevPair}.",
            $"{currPair} grabs {ord} from {prevPair}.",
            $"{currPair} overtakes {prevPair} for {ord}.",
            $"{currPair} edges past {prevPair} into {ord}.",
            $"{currPair} passes {prevPair} for {ord}.",
            $"{currPair} displaces {prevPair} at {ord}.",
            $"{currPair} leaps ahead of {prevPair} to {ord}.",
            $"{currPair} snatches {ord} from {prevPair}.",
            $"{currPair} nudges ahead of {prevPair} for {ord}.",
            $"{currPair} outpaces {prevPair} for {ord}."
        );
    }

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

    private static string RankUrl(int rank) =>
        $"https://longevityworldcup.com/leaderboard/leaderboard.html#rank-{rank}";

    private static string Link(string url, string text) => $"<{url}|{Escape(text)}>";
    private static string Escape(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Ordinal(int n)
    {
        if (n % 100 is 11 or 12 or 13) return $"{n}th";
        return (n % 10) switch { 1 => $"{n}st", 2 => $"{n}nd", 3 => $"{n}rd", _ => $"{n}th" };
    }

    private static string Pick(params string[] options) =>
        options.Length == 0 ? "" : options[Random.Shared.Next(options.Length)];
}
