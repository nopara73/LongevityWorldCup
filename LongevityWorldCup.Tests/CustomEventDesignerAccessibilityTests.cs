using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class CustomEventDesignerAccessibilityTests
{
    [Fact]
    public async Task GeneratedOutputs_HaveAccessibleNames()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/internal/custom-event-designer.html");

        Assert.Contains("id=\"secretHashOutput\" class=\"token\" aria-label=\"Generated configuration hash\"", html);
        Assert.Contains("id=\"cleanupCommandOutput\" class=\"token\" aria-label=\"Generated cleanup SQL\"", html);
        Assert.Contains("id=\"commandOutput\" class=\"token\" aria-label=\"Generated server command\"", html);
    }
}
