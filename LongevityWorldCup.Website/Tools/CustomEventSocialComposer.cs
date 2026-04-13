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

    public static CustomEventSocialPlan BuildPlan(string eventId, string rawText, int maxTextLength, Func<string, string>? mentionResolver, bool includeEventUrl = true)
    {
        var (titleRaw, contentRaw) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var titleText = CollapseWhitespace(CustomEventMarkup.ToPlainText(titleRaw, keepHyperlinkLabels: true, mentionResolver)).Trim();
        var bodyText = CustomEventMarkup.ToPlainText(contentRaw, keepHyperlinkLabels: true, mentionResolver).Trim();
        var eventUrl = includeEventUrl ? BuildEventUrl(eventId) : string.Empty;
        var hasHyperlinks = CustomEventMarkup.ContainsHyperlink(rawText);

        var textPostWithoutEventUrl = BuildTextPost(titleText, bodyText, eventUrl: null);
        if (!hasHyperlinks || string.IsNullOrWhiteSpace(eventUrl))
        {
            if (!string.IsNullOrWhiteSpace(textPostWithoutEventUrl) && textPostWithoutEventUrl.Length <= maxTextLength)
                return new CustomEventSocialPlan(CustomEventPostMode.Text, titleText, bodyText, eventUrl, textPostWithoutEventUrl);
        }
        else
        {
            var textPostWithEventUrl = BuildTextPost(titleText, bodyText, eventUrl);
            if (!string.IsNullOrWhiteSpace(textPostWithEventUrl) && textPostWithEventUrl.Length <= maxTextLength)
                return new CustomEventSocialPlan(CustomEventPostMode.Text, titleText, bodyText, eventUrl, textPostWithEventUrl);
        }

        var imageCaption = BuildImageCaption(titleText, hasHyperlinks ? eventUrl : string.Empty, maxTextLength);
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

    private static string BuildImageCaption(string titleText, string eventUrl, int maxTextLength)
    {
        if (string.IsNullOrWhiteSpace(eventUrl))
            return CollapseWhitespace(titleText).Trim();

        var normalizedTitle = CollapseWhitespace(titleText).Trim();
        var reserved = eventUrl.Length + 2;
        var maxTitleLength = Math.Max(0, maxTextLength - reserved);
        if (normalizedTitle.Length > maxTitleLength)
        {
            if (maxTitleLength <= 1)
                normalizedTitle = string.Empty;
            else
                normalizedTitle = normalizedTitle[..(maxTitleLength - 1)].TrimEnd() + "…";
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
