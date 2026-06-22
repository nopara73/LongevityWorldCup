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

        Assert.Contains("function getBrowserStorageItem(storageName, key)", html);
        Assert.Contains("return window[storageName].getItem(key);", html);
        Assert.Contains("function getLocalItem(key)", html);
        Assert.Contains("function hasSubmittedApplication()", html);
        Assert.Contains("return getLocalItem('hasApplication') === 'true';", html);
        Assert.Contains("} catch (_) {", html);
        Assert.Contains("return null;", html);
        Assert.Contains("const hasApp = hasSubmittedApplication();", html);
        Assert.DoesNotContain("return localStorage.getItem('hasApplication') === 'true';", html);
        Assert.DoesNotContain("const hasApp = localStorage.getItem('hasApplication') === 'true';", html);
    }
}
