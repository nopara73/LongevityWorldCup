using LongevityWorldCup.Website;
using LongevityWorldCup.Website.Business;
using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class InstagramVisualTests
{
    [Theory]
    [InlineData("Mastodon", "https://mastodon.social/@longevityworldcup", "mastodon")]
    [InlineData("Bluesky", "https://bsky.app/profile/longevityworldcup.bsky.social", "bluesky")]
    [InlineData("Instagram", "https://www.instagram.com/longevityworldcup/", "instagram")]
    [InlineData("Reddit", "https://www.reddit.com/r/LongevityWorldCup/", "reddit")]
    [InlineData("Telegram", "https://t.me/longevityworldcup", "telegram")]
    public void LinkedLaunchHeadline_IdentifiesThePlatformWithoutABody(string label, string url, string platform)
    {
        var raw = $"LWC is now on [{label}]({url})";
        var visual = InstagramVisual.ForCustom(raw);
        Assert.Equal("LWC is now on " + label, visual.Headline);
        Assert.Equal("", visual.Details);
        Assert.Equal(platform, visual.Platform);
        Assert.NotNull(visual.Address);
        var caption = InstagramPost.BuildPlan("event", raw, null, true).PostText;
        Assert.Equal("LWC is now on " + label + "\n\n" + url, caption);
    }

    [Fact]
    public void CustomImage_KeepsTheAnnouncementSeparateFromItsSupportingDetail()
    {
        var visual = InstagramVisual.ForCustom("A new competition season\n\n[mention](alice_smith) leads the field. [Results](https://example.com/results)", _ => "Alice Smith");
        Assert.Equal("A new competition season", visual.Headline);
        Assert.Equal("Alice Smith leads the field. Results", visual.Details);
        Assert.Equal(new[] { "alice_smith" }, visual.AthleteSlugs);
        Assert.Null(visual.Platform);
        Assert.Null(InstagramVisual.ForCustom("Mastodon moderation news\n\n[More](https://mastodon.social/@longevityworldcup)").Platform);
    }

    [Theory]
    [InlineData(EventType.AgeImprovementTop10Change, "clock[pheno] place[2] improvement[-6.56] ageReduction[-14.2]", 2, "Pheno Improvement")]
    [InlineData(EventType.AgeImprovementTop10Change, "clock[bortz] place[10] improvement[-3] ageReduction[-12]", 10, "Bortz Improvement")]
    [InlineData(EventType.PhenoAgeTop10Change, "place[7] ageReduction[-14.2]", 7, "Pheno Age")]
    [InlineData(EventType.CrowdAgeTop10Change, "place[1] crowdAge[34.5] crowdCount[1200]", 1, "Crowd Age")]
    [InlineData(EventType.NewRank, "rank[2] prevPlace[10]", 2, "Overall standings")]
    [InlineData(EventType.NewRank, "rank[1]", 1, "Overall standings")]
    public void PlacementImage_UsesTheRecordedStandingAndNamesTheFeaturedAthleteOnce(
        EventType type, string fields, int rank, string league)
    {
        var visual = InstagramVisual.ForEvent(type, $"slug[alice_smith] {fields} prev[bob_jones]",
            "Announcement with a runner-up and a link.", slug => slug == "alice_smith" ? "Alice Smith" : "Bob Jones");
        Assert.Equal("Alice Smith", visual.Headline);
        Assert.Equal("", visual.Details);
        Assert.Equal(new[] { "alice_smith" }, visual.AthleteSlugs);
        Assert.Equal(new InstagramPlacement(rank, league), visual.Placement);
    }

    [Fact]
    public void PodcastArtwork_IsFoundAlongsideOtherAnnouncementLinks()
    {
        var visual = InstagramVisual.ForCustom("A new Immortal Combat episode\n\n[Watch](https://www.youtube.com/watch?v=kOWAsyQCtH4) [LWC](https://longevityworldcup.com)");
        Assert.Equal("kOWAsyQCtH4", visual.VideoId);
        var athlete = InstagramVisual.ForEvent(EventType.Joined, "slug[alice_smith]",
            "Alice Smith joins LWC.\n\nhttps://youtu.be/kOWAsyQCtH4", _ => "Alice Smith");
        Assert.Equal("kOWAsyQCtH4", athlete.VideoId);
        Assert.Null(InstagramVisual.ForCustom("An update\n\n[Watch](https://youtube.com.evil.example/watch?v=kOWAsyQCtH4)").VideoId);
    }
}
