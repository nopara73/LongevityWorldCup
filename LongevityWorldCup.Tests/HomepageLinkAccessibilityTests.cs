using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class HomepageLinkAccessibilityTests
{
    [Fact]
    public async Task HistoryCallToAction_IsOneNamedLink()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("<i class=\"fas fa-book-open\" aria-hidden=\"true\"></i> History of longevity as a sport", html);
        Assert.DoesNotContain("text-decoration: none;\"><i class=\"fas fa-book-open\"", html);
    }
}
