using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SocialContactParserTests
{
    [Theory]
    [InlineData("@alice", "@alice")]
    [InlineData("https://x.com/alice", "@alice")]
    [InlineData("x.com/@alice/status/123", "@alice")]
    [InlineData("https://twitter.com/alice?lang=en", "@alice")]
    [InlineData("https://mobile.twitter.com/alice", "@alice")]
    public void TryBuildMention_ExtractsXHandlesFromSupportedFormats(string mediaContact, string expectedMention)
    {
        Assert.Equal(expectedMention, SocialContactParser.TryBuildMention(mediaContact, SocialPlatform.X));
    }

    [Theory]
    [InlineData("https://threads.com/@alice", "@alice")]
    [InlineData("www.threads.com/alice", "@alice")]
    public void TryBuildMention_ExtractsThreadsHandlesFromSupportedFormats(string mediaContact, string expectedMention)
    {
        Assert.Equal(expectedMention, SocialContactParser.TryBuildMention(mediaContact, SocialPlatform.Threads));
    }

    [Theory]
    [InlineData("")]
    [InlineData("https://notx.com/alice")]
    [InlineData("https://x.com.evil.example/alice")]
    [InlineData("https://threads.com/alice")]
    public void TryBuildMention_RejectsUnsupportedXContacts(string? mediaContact)
    {
        Assert.Null(SocialContactParser.TryBuildMention(mediaContact, SocialPlatform.X));
    }

    [Theory]
    [InlineData("https://notthreads.com/alice")]
    [InlineData("https://threads.com.evil.example/alice")]
    [InlineData("https://x.com/alice")]
    public void TryBuildMention_RejectsUnsupportedThreadsContacts(string mediaContact)
    {
        Assert.Null(SocialContactParser.TryBuildMention(mediaContact, SocialPlatform.Threads));
    }

    [Fact]
    public void TryBuildMention_DoesNotBuildFacebookMentions()
    {
        Assert.Null(SocialContactParser.TryBuildMention("https://facebook.com/alice", SocialPlatform.Facebook));
    }
}
