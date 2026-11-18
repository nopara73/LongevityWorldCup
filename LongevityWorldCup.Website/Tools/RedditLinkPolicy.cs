using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class RedditLinkPolicy
{
    // Preventive subset of the AutoModerator URL Shortener Ban library, checked 2026-11-10:
    // https://www.reddit.com/r/AutoModerator/wiki/library/#wiki_url_shortener_ban
    // This is not Reddit's unpublished, definitive sitewide ban list. Direct publisher URLs
    // remain allowed; never expand or disguise these links to evade a removal.
    internal static readonly string[] PreventiveDomains =
    [
        "a.co", "adf.ly", "alturl.com", "amzn.to", "any.gs", "bc.vc", "bit.do", "bit.ly",
        "bitly.com", "bl.ink", "buff.ly", "chilp.it", "clck.ru", "cli.gs", "cli.re", "clk.im",
        "cutt.ly", "cutt.us", "db.tt", "dlvr.it", "ebay.to", "eepurl.com", "exe.io", "fb.me",
        "gg.gg", "goo.gl", "ht.ly", "hubs.ly", "hyperurl.co", "ift.tt", "is.gd", "ity.im",
        "j.gs", "j.mp", "lc.cx", "lnkd.in", "mcaf.ee", "ow.ly", "po.st", "q.gs", "qr.net",
        "rb.gy", "reut.rs", "sh.st", "shar.es", "short.cm", "shorte.st", "shorturl.at",
        "shorturl.com", "shrinke.me", "shrinkr.com", "smarturl.it", "snip.ly", "snipurl.com",
        "snurl.com", "soo.gd", "t.co", "t.me", "t2m.io", "tiny.cc", "tinyurl.com", "tr.im",
        "u.to", "ur1.ca", "v.gd", "v.ht", "x.co", "yourls.org"
    ];

    internal static string? FindBlockedPostLink(RedditPostRequest post, string[]? confirmedBans) =>
        FindBlockedLink(post.Title, confirmedBans) ?? FindBlockedLink(post.Text, confirmedBans);

    internal static string? FindBlockedLink(string text, string[]? confirmedBans)
    {
        foreach (var link in CustomEventMarkup.GetHyperlinks(text))
        {
            if (IsBlocked(link, confirmedBans)) return link;
        }

        var plainText = CustomEventMarkup.ToPlainText(text);
        foreach (Match match in WebLinks().Matches(plainText))
        {
            var link = TrimProsePunctuation(match.Value);
            if (IsBlocked(link, confirmedBans)) return link;
            if (link != match.Value && IsBlocked(match.Value, confirmedBans)) return match.Value;
        }

        return null;
    }

    private static bool IsBlocked(string link, string[]? confirmedBans)
    {
        var absolute = link.Contains("://", StringComparison.Ordinal) ? link : "https://" + link;
        if (!TryWebUrl(absolute, out var url)) return false;

        var host = url.IdnHost.TrimEnd('.');
        if (PreventiveDomains.Any(domain => MatchesDomain(host, domain))) return true;

        foreach (var entry in confirmedBans ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var rule = entry.Trim();
            if (TryWebUrl(rule, out var blockedUrl))
            {
                if (url.Scheme == blockedUrl.Scheme && url.Port == blockedUrl.Port
                    && host.Equals(blockedUrl.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase)
                    && url.PathAndQuery == blockedUrl.PathAndQuery)
                    return true;
            }
            else if (Uri.CheckHostName(rule) != UriHostNameType.Unknown
                && TryWebUrl("https://" + rule, out var domain)
                && MatchesDomain(host, domain.IdnHost.TrimEnd('.')))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryWebUrl(string value, [NotNullWhen(true)] out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static bool MatchesDomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase)
        || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static string TrimProsePunctuation(string link)
    {
        link = link.TrimEnd('.', ',', '!', '?', ';', ':', ']', '}');
        var unmatched = link.Count(ch => ch == ')') - link.Count(ch => ch == '(');
        while (unmatched-- > 0 && link.EndsWith(')'))
            link = link[..^1];
        return link;
    }

    [GeneratedRegex(@"(?<![\w@./])(?:https?://[^\s<>""'“”‘’]+|(?:[a-z0-9-]+\.)+[a-z]{2,}/[^\s<>""'“”‘’]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebLinks();
}
