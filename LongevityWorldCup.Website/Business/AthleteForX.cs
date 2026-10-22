using LongevityWorldCup.Website.Tools;

namespace LongevityWorldCup.Website.Business;

public record AthleteForX(
    string Slug,
    string Name,
    int? CurrentRank,
    double? LowestPhenoAge,
    double? LowestBortzAge,
    double? ChronoAge,
    double? PhenoAgeDiffFromBaseline,
    double? BortzAgeDiffFromBaseline,
    string? PodcastLink,
    string? XHandle,
    string? MediaContact,
    string? ThreadsHandle = null,
    string? InstagramHandle = null)
{
    public string? MentionFor(SocialPlatform platform)
    {
        var stored = platform switch
        {
            SocialPlatform.X => XHandle,
            SocialPlatform.Threads => ThreadsHandle,
            SocialPlatform.Instagram => InstagramHandle,
            _ => null
        };
        return SocialContactParser.TryBuildMention(stored, platform, allowBareHandle: true)
            ?? SocialContactParser.TryBuildMention(MediaContact, platform);
    }
}
