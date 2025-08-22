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
        return $"{nameLink} joined the leaderboard.";
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
        var currSegment = $"{currNameLink} ({currRankLink})";

        if (prev is null) return $"{currSegment} is now {Ordinal(rank.Value)}.";

        var prevName = slugToName(prev);
        var prevNameLink = Link(AthleteUrl(prev), prevName);

        int? prevRank = getRankForSlug?.Invoke(prev);
        if (!prevRank.HasValue) prevRank = rank.Value + 1;

        var prevRankText = prevRank.HasValue ? $"({Link(RankUrl(prevRank.Value), $"#{prevRank.Value}")})" : "";
        var prevSegment = $"{prevNameLink} {prevRankText}".TrimEnd();

        return $"{currSegment} took {Ordinal(rank.Value)} place from {prevSegment}.";
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
}
