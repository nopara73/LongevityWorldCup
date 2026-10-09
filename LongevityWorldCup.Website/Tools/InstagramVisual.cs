using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal sealed record InstagramVisual(
    string Headline, string Details, string? Platform, string? Address, string? VideoId,
    IReadOnlyList<string> AthleteSlugs)
{
    internal static InstagramVisual ForCustom(string rawText, Func<string, string>? resolveName = null)
    {
        var (title, body) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var headline = Plain(title, resolveName);
        var details = Plain(body, resolveName);
        var link = CustomEventMarkup.GetSingleHyperlink(rawText);
        var platform = FindPlatform(headline, link);
        var slugs = Regex.Matches(rawText, @"\[mention\]\(([^()]*)\)", RegexOptions.IgnoreCase)
            .Select(x => x.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        return new(headline, details, platform, AddressFor(platform, link), FindVideoId(rawText), slugs);
    }

    internal static InstagramVisual ForEvent(EventType type, string rawText, string message)
    {
        var (headline, details) = CustomEventMarkup.SplitTitleAndContent(message);
        var slugs = new List<string>();
        if (EventHelpers.TryExtractSlug(rawText, out var slug)) slugs.Add(slug);
        if (type == EventType.NewRank && EventHelpers.TryExtractPrev(rawText, out var previous)) slugs.Add(previous);
        return new(Plain(headline), Plain(details), null, null, FindVideoId(message), slugs);
    }

    private static string? FindVideoId(string text) => CustomEventMarkup.GetHyperlinks(text)
        .Concat(Regex.Matches(text, @"https?://[^\s<>]+", RegexOptions.IgnoreCase).Select(x => x.Value.TrimEnd('.', ',', ')')))
        .Select(YouTubePreviewService.TryGetVideoId).FirstOrDefault(x => x is not null);

    // URLs remain in the caption. They must not consume the image's headline area.
    private static string Plain(string text, Func<string, string>? resolveName = null) =>
        Regex.Replace(Regex.Replace(CustomEventMarkup.ToPlainText(text, true, resolveName),
            @"https?://\S+", ""), @"\s+", " ").Trim();

    private static string? FindPlatform(string headline, string? link)
    {
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        var host = uri.Host.ToLowerInvariant().Replace("www.", "");
        var title = headline.Replace("🔔", "").TrimEnd('.', ' ');
        return (title, host) switch
        {
            ("LWC is now on Mastodon" or "LWC announcements are now on Mastodon", "mastodon.social") => "mastodon",
            ("LWC is now on Bluesky" or "LWC announcements are now on Bluesky", "bsky.app") => "bluesky",
            ("LWC is now on Instagram" or "LWC announcements are now on Instagram", "instagram.com") => "instagram",
            ("LWC is now on Reddit" or "LWC announcements are now on Reddit", "reddit.com") => "reddit",
            ("Follow LWC by RSS", "longevityworldcup.com") when uri.AbsolutePath == "/feeds/events.rss" => "rss",
            ("LWC announcements, straight to your browser", "longevityworldcup.com") when uri.AbsolutePath == "/events" => "browser",
            _ => null
        };
    }

    private static string? AddressFor(string? platform, string? link)
    {
        if (platform is null || !Uri.TryCreate(link, UriKind.Absolute, out var uri)) return null;
        return platform switch
        {
            "bluesky" => "@" + uri.AbsolutePath.Split('/').Last(),
            "instagram" => "@" + uri.AbsolutePath.Trim('/'),
            "reddit" => uri.AbsolutePath.Trim('/'),
            _ => uri.Host + uri.AbsolutePath.TrimEnd('/')
        };
    }
}
