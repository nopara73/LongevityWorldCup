using IOPath = System.IO.Path;

namespace LongevityWorldCup.Website.Business;

public sealed class AthleteCountMilestoneMemeService
{
    private const string SiteBaseUrl = "https://longevityworldcup.com";
    private static readonly IReadOnlyDictionary<int, (string FileName, string ContentType)> MemeFiles =
        new Dictionary<int, (string FileName, string ContentType)>
        {
            [404] = ("athletes-404-not-found.jpg", "image/jpeg"),
            [666] = ("athletes-666-this-is-fine.jpg", "image/jpeg"),
            [777] = ("athletes-777-slot-machine.png", "image/png"),
            [1337] = ("athletes-1337-hackerman.jpg", "image/jpeg"),
            [9001] = ("athletes-9001-over-9000.jpg", "image/jpeg")
        };

    private readonly IWebHostEnvironment _env;

    public AthleteCountMilestoneMemeService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public bool TryGetMeme(int athleteCount, out AthleteCountMilestoneMeme meme)
    {
        meme = default;
        if (!MemeFiles.TryGetValue(athleteCount, out var file))
            return false;

        var fullPath = IOPath.Combine(_env.WebRootPath, "assets", "social", "memes", file.FileName);
        if (!File.Exists(fullPath))
            return false;

        meme = new AthleteCountMilestoneMeme(
            athleteCount,
            fullPath,
            $"{SiteBaseUrl}/assets/social/memes/{Uri.EscapeDataString(file.FileName)}",
            file.ContentType);
        return true;
    }
}

public readonly record struct AthleteCountMilestoneMeme(
    int AthleteCount,
    string FullPath,
    string PublicUrl,
    string ContentType);
