using System.Globalization;

namespace LongevityWorldCup.Website.Tools;

public static class InstagramPost
{
    public const int MaxCaptionLength = 2200;
    public const int MaxAltTextLength = 1000;

    // A UTF-16 budget is conservative for Instagram's character limit and never
    // splits an emoji, combining sequence, or surrogate pair at the boundary.
    public static string Truncate(string text, int limit)
    {
        if (text.Length <= limit) return text;
        if (limit <= 0) return "";
        var end = 0;
        foreach (var start in StringInfo.ParseCombiningCharacters(text))
        {
            if (start > limit - 1) break;
            end = start;
        }
        return text[..end].TrimEnd() + "…";
    }

    public static CustomEventSocialPlan BuildPlan(string eventId, string rawText, Func<string, string>? resolveName, bool visibleOnWebsite)
    {
        var plan = CustomEventSocialComposer.BuildPlan(eventId, rawText, MaxCaptionLength, resolveName,
            includeEventUrl: visibleOnWebsite, truncate: Truncate);
        // A single hyperlink can itself exceed the whole caption budget. Keep
        // readable copy on the image post instead of emitting a broken URL.
        var caption = plan.PostText.Length <= MaxCaptionLength ? plan.PostText
            : Truncate(string.IsNullOrWhiteSpace(plan.TitleText) ? plan.BodyText : plan.TitleText, MaxCaptionLength);
        return plan with { Mode = CustomEventPostMode.Image, PostText = caption };
    }
}
