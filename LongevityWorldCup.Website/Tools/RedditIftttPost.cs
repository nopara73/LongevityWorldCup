using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace LongevityWorldCup.Website.Tools;

internal static partial class RedditIftttPost
{
    internal const string Author = "nopara73";
    internal const string EventName = "lwc_reddit_announcement";
    internal static readonly TimeSpan ConfirmationWindow = TimeSpan.FromHours(2);

    internal static bool IsConfigured(Config config) =>
        config.RedditIftttWebhookKey is { } key && WebhookKeyPattern().IsMatch(key) &&
        !string.IsNullOrWhiteSpace(config.RedditIftttReceiptSecretHash);

    internal static string FormatBody(string text) =>
        WebUtility.HtmlEncode(text.ReplaceLineEndings("\n")).Replace("\n", "<br>", StringComparison.Ordinal);

    internal static string Fingerprint(string title, string text)
    {
        var copy = JsonSerializer.Serialize(new[] { Normalize(title), Normalize(text) });
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(copy)));
    }

    internal static string FingerprintFromHtml(string title, string text) =>
        Fingerprint(title, WebUtility.HtmlDecode(LineBreakPattern().Replace(text, "\n")));

    internal static string? GetPostId(string? postUrl)
    {
        if (postUrl is not { Length: <= 2048 } || !Uri.TryCreate(postUrl, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort || url.UserInfo.Length != 0 ||
            url.Host is not ("www.reddit.com" or "reddit.com" or "old.reddit.com"))
            return null;

        var match = PermalinkPattern().Match(url.AbsolutePath);
        return match.Success ? "t3_" + match.Groups[1].Value.ToLowerInvariant() : null;
    }

    private static string Normalize(string text) =>
        WhitespacePattern().Replace(text.Normalize(), " ").Trim();

    [GeneratedRegex(@"\A[A-Za-z0-9_-]{12,128}\z")]
    private static partial Regex WebhookKeyPattern();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LineBreakPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"\A/r/LongevityWorldCup/comments/([a-z0-9]{1,32})(?:/[^/]+)?/?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PermalinkPattern();
}
