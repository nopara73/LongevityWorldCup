using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class DocumentationLinksTests
{
    [Theory]
    [InlineData("https://longevityworldcup.com/ruleset#faq", false)]
    [InlineData("https://WWW.LONGEVITYWORLDCUP.COM:443/about?q=1&amp;b=2#why", false)]
    [InlineData("http://longevityworldcup.com:80/", false)]
    [InlineData("https://longevityworldcup.com./history", false)]
    [InlineData("//www.longevityworldcup.com/privacy#contact", false)]
    [InlineData("/privacy#contact", false)]
    [InlineData("#contact", false)]
    [InlineData("mailto:hi@longevityworldcup.com", false)]
    [InlineData("https://github.com/nopara73/LongevityWorldCup", true)]
    [InlineData("https://longevityworldcup.com.evil.test/", true)]
    [InlineData("https://evil.test/?next=https://longevityworldcup.com/", true)]
    [InlineData("https://longevityworldcup.com@evil.test/", true)]
    [InlineData("https://evil.test@longevityworldcup.com/", true)]
    [InlineData("https://longevityworldcup.com:8443/", true)]
    [InlineData("https://other.longevityworldcup.com/", true)]
    [InlineData("//evil.test/", true)]
    public void LinkDefaults_UseParsedSiteIdentityAndKeepTheDestination(string href, bool external)
    {
        var original = $"<a href=\"{href}\">Original text</a>";
        var html = DocumentationLinks.OpenExternalLinksInNewTabs(original);
        Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains(">Original text</a>", html);
        Assert.Equal(external, html.Contains("target=\"_blank\"", StringComparison.Ordinal));
        if (external) Assert.Contains("rel=\"noopener noreferrer\"", html);
        else Assert.Equal(original, html);
        Assert.Equal(html, DocumentationLinks.OpenExternalLinksInNewTabs(html));
    }

    [Theory]
    [InlineData("<a href=\"https://example.test\" target=\"_self\">Text</a>")]
    [InlineData("<a target='_blank' href='https://longevityworldcup.com/'>Text</a>")]
    [InlineData("<a href=\"https://example.test/file\" download>Text</a>")]
    [InlineData("<a href=\"/about\" title=\"Original title\">Text</a>")]
    public void LinkDefaults_PreserveExplicitIntent(string original)
        => Assert.Equal(original, DocumentationLinks.OpenExternalLinksInNewTabs(original));

    [Fact]
    public void ExternalLinks_KeepExistingRelationshipsAndTitles()
    {
        var html = DocumentationLinks.OpenExternalLinksInNewTabs("<a title='Source' rel='nofollow' href='https://example.test/#part'>Text</a>");
        Assert.Equal("<a title='Source' rel=\"nofollow noopener noreferrer\" href='https://example.test/#part' target=\"_blank\">Text</a>", html);
        Assert.Contains("target=\"_blank\"", DocumentationLinks.OpenExternalLinksInNewTabs("<a rel=\"external\" href=\"https://longevityworldcup.com\">Text</a>"));
    }

    [Fact]
    public void RelationshipDefaults_DoNotRewriteQueryOrTitleTextThatLooksLikeAnAttribute()
    {
        const string href = "https://example.test/?rel=source&amp;q=1#part";
        var html = DocumentationLinks.OpenExternalLinksInNewTabs($"<p>Before</p><a href=\"{href}\" title='Keep rel=original' rel='nofollow'>Text</a>");
        Assert.Equal($"<p>Before</p><a href=\"{href}\" title='Keep rel=original' rel=\"nofollow noopener noreferrer\" target=\"_blank\">Text</a>", html);
    }
}
