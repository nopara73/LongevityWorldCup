using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageLinkAccessibilityTests
{
    [Fact]
    public async Task LeaderboardsAboutLink_ReplacesTheDuplicateHomepageTagline()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var homepage = await client.GetStringAsync("/");
        var about = await client.GetStringAsync("/about");

        Assert.Contains("<a href=\"/about\">longevity&nbsp;leaderboards</a>. Reverse&nbsp;your&nbsp;age", homepage);
        Assert.DoesNotContain("Too old for your sport?", homepage);
        Assert.DoesNotContain("<span class=\"tagline\">", homepage);
        Assert.Contains("<span class=\"tagline\">", about);
    }

    [Fact]
    public async Task HistoryCallToAction_IsOneNamedLink()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<i class=\"fas fa-book-open\" aria-hidden=\"true\"></i> History of longevity as a sport", html);
        Assert.DoesNotContain("text-decoration: none;\"><i class=\"fas fa-book-open\"", html);
    }

    [Fact]
    public async Task ExtendedFaqCallToAction_IsOneNamedLink()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<a href=\"/ruleset#faq\"><i class=\"fas fa-circle-question\" aria-hidden=\"true\"></i> Extended FAQ</a>", html);
        Assert.DoesNotContain("homepage-faq-icon-link", html);
    }

    [Fact]
    public async Task MerchCarousel_OnlyTabsToTheVisibleSlide()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("slide.setAttribute('aria-hidden', String(!isActive));", html);
        Assert.Contains("slide.tabIndex = isActive ? 0 : -1;", html);
    }
}
