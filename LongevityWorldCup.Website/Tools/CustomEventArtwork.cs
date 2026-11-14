namespace LongevityWorldCup.Website.Tools;

// The artwork is attached separately; the landing page remains the destination.
public sealed record CustomEventArtwork(string AssetPath, string AltText, string? QrAssetPath = null,
    string? PreviewTitle = null, string? PreviewDescription = null, string? InstagramAssetPath = null)
{
    public string PublicUrl => "https://longevityworldcup.com/" + AssetPath;
    public string FullPath(string webRoot) => Path.Combine(webRoot, AssetPath.Replace('/', Path.DirectorySeparatorChar));

    public static CustomEventArtwork? ForLinkedPage(string rawText)
        => ForUrl(CustomEventMarkup.GetSingleHyperlink(rawText));

    public static CustomEventArtwork? ForUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var link)
            || link.Scheme != Uri.UriSchemeHttps || !link.IsDefaultPort || link.UserInfo.Length != 0)
            return null;

        return (link.Host, link.AbsolutePath.TrimEnd('/')) switch
        {
            ("longevityworldcup.com" or "www.longevityworldcup.com", "/leaderboard") when link.Query == "?group=flags" => new("assets/social/by-flag-20261017.png",
                "Ultimate Flag League standings on 17 October 2026: Australia, Singapore and the United States in Bortz Age, followed by Germany in Pheno Age. Each flag shows its top three athletes and total age reduction.",
                PreviewTitle: "Ultimate Flag League", PreviewDescription: "Flags compete on their top three athletes' total age reduction.",
                InstagramAssetPath: "assets/social/by-flag-20261017-instagram.png"),
            ("longevityworldcup.com" or "www.longevityworldcup.com", "/rejuvenation-olympics") => new("assets/social/rejuvenation-olympics.png",
                "RO × LWC: a phoenix over racing lanes. Keep competing at Longevity World Cup.",
                "assets/social/rejuvenation-olympics-qr.png", "Coming from the Rejuvenation Olympics?",
                "Keep competing at Longevity World Cup."),
            ("nopara73.medium.com", "/crossfit-vs-hyrox-4d151061c611") => new("assets/social/crossfit-vs-hyrox.png",
                "CrossFit's red line falls as HYROX's yellow line rises behind a flexing Longevity World Cup athlete.",
                PreviewTitle: "CrossFit vs HYROX",
                PreviewDescription: "What can Longevity World Cup learn from their growth?"),
            ("open.spotify.com", "/episode/0CaM0p28Lvyutei1FWiP9I") => new("assets/social/buky-anna-kulturfitnesz-20261103.png",
                "Büky Anna in a turquoise top beside the title Kultúrfitnesz · Petőfi Rádió.",
                InstagramAssetPath: "assets/social/buky-anna-kulturfitnesz-20261103-instagram.png"),
            ("www.fvmglobal.com" or "fvmglobal.com", "/science-of-longevity") => new("assets/social/siim-land-fvm-20261109.png",
                "Siim Land beside the title Longevity Summit India, with copper track lines on a dark background.",
                InstagramAssetPath: "assets/social/siim-land-fvm-20261109-instagram.png"),
            ("kyleyep.podbean.com", "/e/what-the-top-longevity-athletes-actually-do-ft-adam-ficsor") => new("assets/social/adam-ficsor-longevity-unlocked-20261112.png",
                "Kyle Hulbert and Adam Ficsor at microphones on a Longevity Unlocked podcast cover with the Longevity World Cup phoenix.",
                InstagramAssetPath: "assets/social/adam-ficsor-longevity-unlocked-20261112-instagram.png"),
            _ => null
        };
    }
}
