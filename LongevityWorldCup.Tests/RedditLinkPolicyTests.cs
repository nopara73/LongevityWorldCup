using LongevityWorldCup.Website.Tools;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class RedditLinkPolicyTests
{
    [Theory]
    [InlineData("[source](https://BIT.LY./article)", "https://BIT.LY./article")]
    [InlineData("https://t.me/longevityworldcup", "https://t.me/longevityworldcup")]
    [InlineData("Follow t.me/longevityworldcup", "t.me/longevityworldcup")]
    [InlineData("Read www.bit.ly/abc", "www.bit.ly/abc")]
    [InlineData("Read https://sub.tinyurl.com/news.", "https://sub.tinyurl.com/news")]
    [InlineData("https://allowed.example/path?redirect=https://cutt.ly/news", null)]
    [InlineData("https://bit.ly.example/article", null)]
    [InlineData("https://notbit.ly/article", null)]
    [InlineData("https://longevityworldcup.com/events", null)]
    [InlineData("https://pubmed.ncbi.nlm.nih.gov/123/", null)]
    public void ChecksHostBoundariesAndPreservesDirectSources(string text, string? blocked) =>
        Assert.Equal(blocked, RedditLinkPolicy.FindBlockedLink(text, null));
}
