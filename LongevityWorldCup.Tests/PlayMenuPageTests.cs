using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class PlayMenuPageTests
{
    [Fact]
    public async Task PlayMenu_HandlesUnavailableApplicationStorage()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/play/menu.html");

        Assert.Contains("function hasSubmittedApplication()", html);
        Assert.Contains("return localStorage.getItem('hasApplication') === 'true';", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("return false;", html);
        Assert.Contains("const hasApp = hasSubmittedApplication();", html);
        Assert.DoesNotContain("const hasApp = localStorage.getItem('hasApplication') === 'true';", html);
    }
}
