using System.Text.RegularExpressions;
using LongevityWorldCup.Website.Business;

namespace LongevityWorldCup.Website.Tools;

internal sealed record InstagramPlacement(int Place, string Leaderboard, string? Detail)
{
    internal static InstagramPlacement? FromEvent(EventType type, string raw) => type switch
    {
        EventType.AgeImprovementTop10Change when EventHelpers.TryExtractAgeImprovementTop10Change(raw,
            out var clock, out var place, out _, out var improvement, out _) =>
            new(place, clock == "bortz" ? "Bortz Improvement" : "Pheno Improvement",
                FormattableString.Invariant($"{improvement:+0.#;−0.#;0} years from worst to latest")),
        EventType.PhenoAgeTop10Change when EventHelpers.TryExtractPhenoAgeTop10Change(raw,
            out var place, out _, out var reduction) =>
            new(place, "Pheno Age", FormattableString.Invariant($"{reduction:+0.#;−0.#;0} years vs. actual age")),
        EventType.CrowdAgeTop10Change when EventHelpers.TryExtractCrowdAgeTop10Change(raw,
            out var place, out _, out var age, out var count) =>
            new(place, "Crowd Age", FormattableString.Invariant($"{age:0.#} years · {count:N0} {(count == 1 ? "guess" : "guesses")}")),
        EventType.NewRank when EventHelpers.TryExtractTop10Rank(raw, out var place) =>
            new(place, "Ultimate League", EventHelpers.TryExtractPreviousPlace(raw, out var previous) && previous > place
                ? FormattableString.Invariant($"Previously #{previous}") : null),
        _ => null
    };
}

internal sealed record InstagramVisual(
    string Headline, string Details, string? Platform, string? Address, string? VideoId,
    IReadOnlyList<string> AthleteSlugs, CustomEventArtwork? Artwork = null, InstagramPlacement? Placement = null)
{
    internal static InstagramVisual ForCustom(string rawText, Func<string, string>? resolveName = null)
    {
        var (title, body) = CustomEventMarkup.SplitTitleAndContent(rawText);
        var headline = Plain(title, resolveName);
        var details = Plain(body, resolveName);
        var link = CustomEventMarkup.GetSingleHyperlink(rawText);
        var platform = FindPlatform(headline, link);
        var artwork = CustomEventArtwork.ForLinkedPage(rawText);
        var slugs = Regex.Matches(rawText, @"\[mention\]\(([^()]*)\)", RegexOptions.IgnoreCase)
            .Select(x => x.Groups[1].Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
        var artworkAddress = artwork is not null && Uri.TryCreate(link, UriKind.Absolute, out var destination)
            ? destination.Host.Replace("www.", "") + destination.AbsolutePath.TrimEnd('/') + destination.Query : null;
        return new(headline, details, platform, AddressFor(platform, link) ?? artworkAddress, FindVideoId(rawText), slugs, artwork);
    }

    internal static InstagramVisual ForEvent(EventType type, string rawText, string message, Func<string, string> resolveName)
    {
        if (EventHelpers.TryExtractSlug(rawText, out var slug)
            && InstagramPlacement.FromEvent(type, rawText) is { } placement)
            return new(resolveName(slug), "", null, null, null, [slug], Placement: placement);

        var (headline, details) = CustomEventMarkup.SplitTitleAndContent(message);
        var slugs = new List<string>();
        if (!string.IsNullOrWhiteSpace(slug)) slugs.Add(slug);
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
            ("LWC is now on Telegram" or "LWC announcements are now on Telegram", "t.me") => "telegram",
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
