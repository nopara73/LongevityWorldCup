using System.Globalization;
using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

public sealed record RedditPostRequest(string Title, string Text);

public static partial class RedditPost
{
    public const int MaxTitleLength = 300;
    public const int MaxTextLength = 40_000;

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
            text = ordinaryText.Trim();
            title = UrlPattern().Replace(text.Split('\n')[0], "");
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
