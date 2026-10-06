using System.Text;

namespace LongevityWorldCup.Website.Tools;

public enum CustomEventPostMode
{
    Text,
    Image
}

public sealed record CustomEventSocialPlan(
    CustomEventPostMode Mode,
    string TitleText,
    string BodyText,
    string EventUrl,
    string PostText);

public static class CustomEventSocialComposer
{
    private const string SiteBaseUrl = "https://longevityworldcup.com";

    public static string BuildEventUrl(string eventId)
    {
        return $"{SiteBaseUrl}/events?event={Uri.EscapeDataString(eventId ?? string.Empty)}";
    }

    public static CustomEventSocialPlan BuildPlan(string eventId, string rawText, int maxTextLength)
    {
        return BuildPlan(eventId, rawText, maxTextLength, mentionResolver: null, includeEventUrl: true);
    }

    public static CustomEventSocialPlan BuildPlan(string eventId, string rawText, int maxTextLength, Func<string, string>? mentionResolver, bool includeEventUrl = true,
        Func<string, int>? textLength = null, Func<string, int, string>? truncate = null)
    {
        textLength ??= text => text.Length;
        var (titleRaw, contentRaw) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var titleText = CollapseWhitespace(CustomEventMarkup.ToPlainText(titleRaw, keepHyperlinkLabels: true, mentionResolver)).Trim();
        var bodyText = CustomEventMarkup.ToPlainText(contentRaw, keepHyperlinkLabels: true, mentionResolver).Trim();
        var singleHyperlink = CustomEventMarkup.GetSingleHyperlink(rawText);
        var eventUrl = singleHyperlink ?? (includeEventUrl ? BuildEventUrl(eventId) : string.Empty);
        var hasHyperlinks = CustomEventMarkup.ContainsHyperlink(rawText);

        var textPostWithoutEventUrl = BuildTextPost(titleText, bodyText, eventUrl: null);
        if (!hasHyperlinks || string.IsNullOrWhiteSpace(eventUrl))
        {
            if (!string.IsNullOrWhiteSpace(textPostWithoutEventUrl) && textLength(textPostWithoutEventUrl) <= maxTextLength)
                return new CustomEventSocialPlan(CustomEventPostMode.Text, titleText, bodyText, eventUrl, textPostWithoutEventUrl);
        }

        var textPostWithEventUrl = BuildTextPost(titleText, bodyText, eventUrl);
        if (!string.IsNullOrWhiteSpace(textPostWithEventUrl) && textLength(textPostWithEventUrl) <= maxTextLength)
        {
            return new CustomEventSocialPlan(CustomEventPostMode.Text, titleText, bodyText, eventUrl, textPostWithEventUrl);
        }

        var imageCaption = BuildImageCaption(titleText, hasHyperlinks ? eventUrl : string.Empty, maxTextLength, textLength, truncate);
        return new CustomEventSocialPlan(CustomEventPostMode.Image, titleText, bodyText, eventUrl, imageCaption);
    }

    private static string BuildTextPost(string titleText, string bodyText, string? eventUrl)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(titleText))
            sb.Append(titleText.Trim());

        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(bodyText.Trim());
        }

        if (!string.IsNullOrWhiteSpace(eventUrl))
        {
            if (sb.Length > 0)
                sb.Append("\n\n");
            sb.Append(eventUrl.Trim());
        }

        return sb.ToString();
    }

    private static string BuildImageCaption(string titleText, string eventUrl, int maxTextLength, Func<string, int> textLength, Func<string, int, string>? truncate)
    {
        if (string.IsNullOrWhiteSpace(eventUrl))
        {
            var title = CollapseWhitespace(titleText).Trim();
            // Callers supplying a platform-specific counter also supply safe truncation.
            return truncate is not null ? truncate(title, maxTextLength) : title;
        }

        var normalizedTitle = CollapseWhitespace(titleText).Trim();
        var reserved = textLength(eventUrl) + 2;
        var maxTitleLength = Math.Max(0, maxTextLength - reserved);
        if (textLength(normalizedTitle) > maxTitleLength)
        {
            if (maxTitleLength <= 1)
                normalizedTitle = string.Empty;
            else
                normalizedTitle = truncate is not null ? truncate(normalizedTitle, maxTitleLength)
                    : normalizedTitle[..(maxTitleLength - 1)].TrimEnd() + "…";
        }

        if (string.IsNullOrWhiteSpace(normalizedTitle))
            return eventUrl;

        return $"{normalizedTitle}\n\n{eventUrl}";
    }

    private static string CollapseWhitespace(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sb = new StringBuilder(text.Length);
        var prevWhitespace = false;
        foreach (var ch in text)
        {
            var isWhitespace = char.IsWhiteSpace(ch);
            if (!isWhitespace)
            {
                sb.Append(ch);
                prevWhitespace = false;
                continue;
            }

            if (prevWhitespace)
                continue;

            sb.Append(' ');
            prevWhitespace = true;
        }

        return sb.ToString();
    }
}
