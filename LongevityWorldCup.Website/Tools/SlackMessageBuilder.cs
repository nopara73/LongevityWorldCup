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

        if (prev is null)
        {
            return Pick(
                $"{currNameLink} is now {ord}{medal}",
                $"{currNameLink} takes {ord} place{medal}",
                $"{currNameLink} climbs to {ord}{medal}",
                $"{currNameLink} moves up to {ord}{medal}",
                $"{currNameLink} rises to {ord}{medal}",
                $"{currNameLink} ascends to {ord}{medal}",
                $"{currNameLink} secures {ord}{medal}",
                $"{currNameLink} locks in {ord}{medal}",
                $"{currNameLink} vaults to {ord}{medal}",
                $"{currNameLink} jumps to {ord}{medal}"
            );
        }

        var prevName = slugToName(prev);
        var prevNameLink = Link(AthleteUrl(prev), prevName);

        return Pick(
            $"{currNameLink} took {ord} from {prevNameLink}{medal}",
            $"{currNameLink} grabs {ord} from {prevNameLink}{medal}",
            $"{currNameLink} overtakes {prevNameLink} for {ord}{medal}",
            $"{currNameLink} edges past {prevNameLink} into {ord}{medal}",
            $"{currNameLink} passes {prevNameLink} for {ord}{medal}",
            $"{currNameLink} displaces {prevNameLink} at {ord}{medal}",
            $"{currNameLink} leaps ahead of {prevNameLink} to {ord}{medal}",
            $"{currNameLink} snatches {ord} from {prevNameLink}{medal}",
            $"{currNameLink} nudges ahead of {prevNameLink} for {ord}{medal}",
            $"{currNameLink} outpaces {prevNameLink} for {ord}{medal}"
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