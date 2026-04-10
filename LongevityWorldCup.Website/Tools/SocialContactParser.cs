namespace LongevityWorldCup.Website.Tools;

public enum SocialPlatform
{
    X,
    Threads,
    Facebook
}

public static class SocialContactParser
{
    public static string? TryBuildMention(string? mediaContact, SocialPlatform platform)
    {
        var handle = platform switch
        {
            SocialPlatform.X => TryExtractXHandle(mediaContact),
            SocialPlatform.Threads => TryExtractThreadsHandle(mediaContact),
            SocialPlatform.Facebook => null,
            _ => null
        };

        return string.IsNullOrWhiteSpace(handle)
            ? null
            : "@" + handle;
    }

    public static string? TryExtractXHandle(string? mediaContact)
    {
        return TryExtractHandle(mediaContact, isSupportedHost: host =>
            host.EndsWith("x.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("twitter.com", StringComparison.OrdinalIgnoreCase));
    }

    public static string? TryExtractThreadsHandle(string? mediaContact)
    {
        return TryExtractHandle(mediaContact, isSupportedHost: host =>
            host.EndsWith("threads.com", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith("instagram.com", StringComparison.OrdinalIgnoreCase));
    }

    private static string? TryExtractHandle(string? mediaContact, Func<string, bool> isSupportedHost)
    {
        if (string.IsNullOrWhiteSpace(mediaContact))
            return null;

        var trimmed = mediaContact.Trim();
        if (trimmed.StartsWith('@'))
            return NormalizeHandle(trimmed[1..]);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return null;

        if (!isSupportedHost(uri.Host))
            return null;

        var path = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var firstSegment = path.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstSegment))
            return null;

        return NormalizeHandle(firstSegment.TrimStart('@'));
    }

    private static string? NormalizeHandle(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = value.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        return normalized;
    }
}
