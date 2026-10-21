using System.Net;
using System.Text.RegularExpressions;

// Markdown owns destinations. Only choose a default browsing context for links
// that do not already specify one; never rewrite a URL or its fragment.
internal static class DocumentationLinks
{
    public static string OpenExternalLinksInNewTabs(string html) => Regex.Replace(
        html, "<a\\b(?<attributes>[^>]*)>", match =>
        {
            var attributeGroup = match.Groups["attributes"];
            var attributeMatches = Regex.Matches(attributeGroup.Value,
                "(?<name>[^\\s=]+)(?:\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<bare>[^\\s>]+)))?");
            var attributes = attributeMatches.ToDictionary(attribute => attribute.Groups["name"].Value,
                    attribute => WebUtility.HtmlDecode(attribute.Groups["double"].Success ? attribute.Groups["double"].Value
                        : attribute.Groups["single"].Success ? attribute.Groups["single"].Value : attribute.Groups["bare"].Value),
                    StringComparer.OrdinalIgnoreCase);
            if (!attributes.TryGetValue("href", out var href) || attributes.ContainsKey("target") || attributes.ContainsKey("download"))
                return match.Value;

            var absolute = href.StartsWith("//", StringComparison.Ordinal) ? "https:" + href : href;
            if (!Uri.TryCreate(absolute, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return match.Value;

            var host = uri.IdnHost.TrimEnd('.');
            var sameSite = uri.IsDefaultPort && uri.UserInfo.Length == 0 &&
                (host.Equals("longevityworldcup.com", StringComparison.OrdinalIgnoreCase) ||
                 host.Equals("www.longevityworldcup.com", StringComparison.OrdinalIgnoreCase));
            var rel = attributes.GetValueOrDefault("rel", "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (sameSite && !rel.Contains("external", StringComparer.OrdinalIgnoreCase))
                return match.Value;

            var result = match.Value[..^1] + " target=\"_blank\"";
            if (attributes.ContainsKey("rel"))
            {
                var tokens = rel.Concat(["noopener", "noreferrer"]).Distinct(StringComparer.OrdinalIgnoreCase);
                var relAttribute = attributeMatches.First(attribute =>
                    attribute.Groups["name"].Value.Equals("rel", StringComparison.OrdinalIgnoreCase));
                var start = attributeGroup.Index - match.Index + relAttribute.Index;
                result = result.Remove(start, relAttribute.Length).Insert(start,
                    $"rel=\"{WebUtility.HtmlEncode(string.Join(' ', tokens))}\"");
            }
            else result += " rel=\"noopener noreferrer\"";
            return result + ">";
        }, RegexOptions.IgnoreCase);
}
