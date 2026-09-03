using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class HomepageLinkAccessibilityTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task LeaderboardsAboutLink_ReplacesTheDuplicateHomepageTagline()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var homepage = await client.GetStringAsync("/");
        var about = await client.GetStringAsync("/about");

        Assert.Matches(@"<p class=""game-description"">\s*<a href=""/about"">longevity&nbsp;leaderboards</a>\s*</p>", homepage);
        Assert.DoesNotContain("Too old for your sport?", homepage);
        Assert.DoesNotContain("Reverse&nbsp;your&nbsp;age", homepage);
        Assert.DoesNotContain("rise&nbsp;on&nbsp;the&nbsp;leaderboard!", homepage);
        Assert.DoesNotContain("<span class=\"tagline\">", homepage);
        Assert.Contains("<span class=\"tagline\">", about);
    }

    [Fact]
    public async Task HistoryCallToAction_IsOneNamedLink()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<i class=\"fas fa-book-open\" aria-hidden=\"true\"></i> History of longevity as a sport", html);
        Assert.DoesNotContain("text-decoration: none;\"><i class=\"fas fa-book-open\"", html);
    }

    [Fact]
    public async Task ExtendedFaqCallToAction_IsOneNamedLink()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<a href=\"/ruleset#faq\"><i class=\"fas fa-circle-question\" aria-hidden=\"true\"></i> Extended FAQ</a>", html);
        Assert.DoesNotContain("homepage-faq-icon-link", html);
    }

    [Fact]
    public async Task ContentSections_HaveStableDeepLinkTargets()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<section id=\"hall-of-fame\" class=\"section-container archive-section\" aria-labelledby=\"hall-of-fame-title\"", html);
        Assert.Contains("<section id=\"faq\" class=\"section-container homepage-faq-section\" aria-labelledby=\"homepage-faq-title\"", html);
        Assert.Contains("<section id=\"contribute\" class=\"section-container contribute-section\" aria-labelledby=\"contribute-title\"", html);
        Assert.Contains("<section id=\"newsletter\" class=\"section-container newsletter-section\" aria-labelledby=\"newsletter-title\"", html);
        Assert.Contains("<div id=\"donation-section\" class=\"contribute-content\">", html);
    }

    [Fact]
    public async Task MerchCarousel_OnlyTabsToTheVisibleSlide()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("slide.setAttribute('aria-hidden', String(!isActive));", html);
        Assert.Contains("slide.tabIndex = isActive ? 0 : -1;", html);
    }
}
