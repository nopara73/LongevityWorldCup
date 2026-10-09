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

    [Fact]
    public void AthleteEvent_UsesItsActualParticipantsAndKeepsCaptionUrlsOffTheImage()
    {
        var visual = InstagramVisual.ForEvent(EventType.NewRank, "slug[alice_smith] rank[1] prev[bob_jones]",
            "Alice Smith is now 1st, ahead of Bob Jones.\n\nhttps://longevityworldcup.com/athlete/alice-smith");
        Assert.Equal("Alice Smith is now 1st, ahead of Bob Jones.", visual.Headline);
        Assert.Equal("", visual.Details);
        Assert.Equal(new[] { "alice_smith", "bob_jones" }, visual.AthleteSlugs);
    }

    [Fact]
    public void PodcastArtwork_IsFoundAlongsideOtherAnnouncementLinks()
    {
        var visual = InstagramVisual.ForCustom("A new Immortal Combat episode\n\n[Watch](https://www.youtube.com/watch?v=kOWAsyQCtH4) [LWC](https://longevityworldcup.com)");
        Assert.Equal("kOWAsyQCtH4", visual.VideoId);
        var athlete = InstagramVisual.ForEvent(EventType.Joined, "slug[alice_smith]",
            "Alice Smith joins LWC.\n\nhttps://youtu.be/kOWAsyQCtH4");
        Assert.Equal("kOWAsyQCtH4", athlete.VideoId);
        Assert.Null(InstagramVisual.ForCustom("An update\n\n[Watch](https://youtube.com.evil.example/watch?v=kOWAsyQCtH4)").VideoId);
    }
}
