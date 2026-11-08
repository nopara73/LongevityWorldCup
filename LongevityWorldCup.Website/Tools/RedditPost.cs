using System.Globalization;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public sealed record RedditPostRequest(string Title, string Text);

public static partial class RedditPost
{
    public const int MaxTitleLength = 300;
    public const int MaxTextLength = 40_000;

    internal static string? BuildLeaderText(EventItem item, IReadOnlyList<AthleteForX> athletes)
    {
        if (item.Type != EventType.NewRank || !EventHelpers.TryExtractRank(item.Text, out var rank) || rank != 1 ||
            !EventHelpers.TryExtractSlug(item.Text, out var slug)) return null;
        var leader = athletes.FirstOrDefault(a => a.CurrentRank == 1 &&
            string.Equals(a.Slug, slug, StringComparison.OrdinalIgnoreCase));
        if (leader is null || string.IsNullOrWhiteSpace(leader.Name)) return null;
        var age = leader.LowestBortzAge ?? leader.LowestPhenoAge;
        if (age is not double value || !double.IsFinite(value)) return null;
        var clock = leader.LowestBortzAge.HasValue ? "bortz" : "pheno";
        var track = leader.LowestBortzAge.HasValue ? "Pro" : "Amateur";
        var lines = new List<string>
        {
            $"{leader.Name} takes the Ultimate League lead",
            "",
            $"{leader.Name} moved into first place on the overall Longevity World Cup leaderboard on {item.OccurredAtUtc.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)}.",
            "",
            $"Their current lowest submitted {clock} age is {value.ToString("0.0", CultureInfo.InvariantCulture)} years ({track} track)."
        };
        var runnerUp = athletes.FirstOrDefault(a => a.CurrentRank == 2);
        if (runnerUp is not null && !string.IsNullOrWhiteSpace(runnerUp.Name))
            lines.Add($"{runnerUp.Name} is currently in second place.");
        lines.Add("");
        lines.Add("The Ultimate League ranks Pro athletes ahead of Amateur athletes. Within each track, rankings use biological age minus chronological age, with lower values ranking higher.");
        lines.Add("");
        lines.Add($"Full standings and athlete results: {SocialPostLinks.LeaderboardUrl}");
        return string.Join("\n", lines);
    }

    internal static RedditPostRequest? Build(EventItem item, string ordinaryText, Func<string, string> resolveName)
    {
        string title;
        string text;
        if (item.Type == EventType.CustomEvent)
        {
            var plan = CustomEventSocialComposer.BuildPlan(item.Id, item.Text, MaxTextLength, resolveName,
                includeEventUrl: item.VisibleOnWebsite);
            if (plan.Mode != CustomEventPostMode.Text) return null;
            title = plan.TitleText;
            text = plan.PostText;
        }
        else
        {
            var parts = ordinaryText.Trim().Split('\n', 2);
            title = UrlPattern().Replace(parts[0], "");
            text = parts.Length == 2 ? parts[1].Trim() : parts[0];
        }

        if (string.IsNullOrWhiteSpace(text)) return null;
        // Keep a relevant existing link; otherwise link the public Event itself.
        if (item.VisibleOnWebsite && !text.Contains("https://longevityworldcup.com/", StringComparison.OrdinalIgnoreCase))
            text += "\n\n" + CustomEventSocialComposer.BuildEventUrl(item.Id);
        if (text.Length > MaxTextLength) return null;

        title = WhitespacePattern().Replace(title, " ").Trim();
        if (title.Length == 0) title = "Longevity World Cup";
        if (title.Length > MaxTitleLength)
        {
            // Do not split a surrogate pair or a combining-character sequence.
            var elements = StringInfo.ParseCombiningCharacters(title);
            var end = elements.Last(index => index < MaxTitleLength - 1);
            title = title[..end].TrimEnd() + "…";
        }
        return new(title, text);
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();
}
