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
        var (titleRaw, contentRaw) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var titleText = CollapseWhitespace(CustomEventMarkup.ToPlainText(titleRaw, keepHyperlinkLabels: true)).Trim();
        var bodyText = CustomEventMarkup.ToPlainText(contentRaw, keepHyperlinkLabels: true).Trim();
        var eventUrl = BuildEventUrl(eventId);
        var hasHyperlinks = CustomEventMarkup.ContainsHyperlink(rawText);

        if (!hasHyperlinks)
        {
            var textPost = BuildTextPost(titleText, bodyText, eventUrl);
            if (!string.IsNullOrWhiteSpace(textPost) && textPost.Length <= maxTextLength)
                return new CustomEventSocialPlan(CustomEventPostMode.Text, titleText, bodyText, eventUrl, textPost);
        }

        var imageCaption = BuildImageCaption(titleText, eventUrl, maxTextLength);
        return new CustomEventSocialPlan(CustomEventPostMode.Image, titleText, bodyText, eventUrl, imageCaption);
    }

    private static string BuildTextPost(string titleText, string bodyText, string eventUrl)
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
