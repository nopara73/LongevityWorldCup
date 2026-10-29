namespace LongevityWorldCup.Website.Tools;

public enum SocialPlatform
{
    X,
    Threads,
    Facebook,
    Mastodon,
    Nostr,
    Bluesky,
    Instagram
}

public static class SocialContactParser
{
    public static string? TryBuildMention(string? mediaContact, SocialPlatform platform, bool allowBareHandle = false)
    {
        var handle = TryExtractHandle(mediaContact, platform, allowBareHandle);

        return string.IsNullOrWhiteSpace(handle)
            ? null
            : "@" + handle;
    }

    public static string? TryExtractXHandle(string? mediaContact)
    {
        return TryExtractHandle(mediaContact, SocialPlatform.X);
    }

    public static string? TryExtractThreadsHandle(string? mediaContact)
    {
        return TryExtractHandle(mediaContact, SocialPlatform.Threads);
    }

    private static string? TryExtractHandle(string? mediaContact, SocialPlatform platform, bool allowBareHandle = false)
    {
        if (string.IsNullOrWhiteSpace(mediaContact) ||
            platform is not (SocialPlatform.X or SocialPlatform.Threads or SocialPlatform.Instagram))
            return null;

        var trimmed = mediaContact.Trim();
        // Legacy unqualified media contacts were X handles. Never infer that
        // the same username belongs to this athlete on another platform.
        if ((allowBareHandle && !trimmed.Contains('/') && !trimmed.Contains(':')) ||
            (platform == SocialPlatform.X && trimmed.StartsWith('@')))
            return NormalizeHandle(trimmed, platform);

        if (!TryCreateContactUri(trimmed, out var uri))
            return null;

        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort)
            return null;

        var supported = platform switch
        {
            SocialPlatform.X => IsSupportedHost(uri.Host, "x.com") || IsSupportedHost(uri.Host, "twitter.com"),
            SocialPlatform.Threads => IsSupportedHost(uri.Host, "threads.com") || IsSupportedHost(uri.Host, "threads.net"),
            SocialPlatform.Instagram => IsSupportedHost(uri.Host, "instagram.com"),
            _ => false
        };
        if (!supported)
            return null;

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment))
            return null;

        if (IsReservedPath(firstSegment, platform)) return null;
        return NormalizeHandle(firstSegment, platform);
    }

    private static bool TryCreateContactUri(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri!))
            return true;

        return Uri.TryCreate("https://" + value, UriKind.Absolute, out uri!);
    }

    private static bool IsSupportedHost(string host, string domain)
    {
        return string.Equals(host, domain, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "www." + domain, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "mobile." + domain, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(host, "m." + domain, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReservedPath(string value, SocialPlatform platform)
    {
        var path = value.ToLowerInvariant();
        if (path is "share" or "search" or "explore" or "home" or "notifications" or
            "messages" or "settings" or "tos" or "privacy" or "login" or "signup") return true;
        return platform == SocialPlatform.X
            ? path is "i" or "intent"
            : path is "accounts" or "p" or "reel" or "reels" or "stories";
    }

    private static string? NormalizeHandle(string? value, SocialPlatform platform)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim();
        if (normalized.StartsWith('@')) normalized = normalized[1..];
        var maxLength = platform == SocialPlatform.X ? 15 : 30;
        if (normalized.Length is 0 || normalized.Length > maxLength ||
            normalized.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && (platform == SocialPlatform.X || c != '.')) ||
            normalized.StartsWith('.') || normalized.EndsWith('.') || normalized.Contains(".."))
            return null;

        return normalized;
    }
}
