using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class RedditLinkPolicy
{
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
        if (!TryWebUrl(link, out var url)) return false;

        var host = url.IdnHost.TrimEnd('.');
        if (MatchesDomain(host, "ift.tt")) return true;

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

    [GeneratedRegex(@"(?<![\w@./])https?://[^\s<>""'“”‘’]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WebLinks();
}
