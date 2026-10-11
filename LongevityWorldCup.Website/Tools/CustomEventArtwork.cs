namespace LongevityWorldCup.Website.Tools;

// The artwork is attached separately; the landing page remains the destination.
public sealed record CustomEventArtwork(string AssetPath, string AltText, string? QrAssetPath = null)
{
    public string PublicUrl => "https://longevityworldcup.com/" + AssetPath;
    public string FullPath(string webRoot) => Path.Combine(webRoot, AssetPath.Replace('/', Path.DirectorySeparatorChar));

    public static CustomEventArtwork? ForLinkedPage(string rawText)
        => ForUrl(CustomEventMarkup.GetSingleHyperlink(rawText));

    public static CustomEventArtwork? ForUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var link)
            || link.Scheme != Uri.UriSchemeHttps || !link.IsDefaultPort || link.UserInfo.Length != 0
            || link.Host is not ("longevityworldcup.com" or "www.longevityworldcup.com"))
            return null;

        return link.AbsolutePath.TrimEnd('/') switch
        {
            "/rejuvenation-olympics" => new("assets/social/rejuvenation-olympics.png",
                "RO × LWC: a phoenix over racing lanes. Keep competing at Longevity World Cup.",
                "assets/social/rejuvenation-olympics-qr.png"),
            _ => null
        };
    }
}
