using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class CustomEventDesignerAccessibilityTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task GeneratedOutputs_HaveAccessibleNames()
    {
        using var client = sharedFactory.CreateClient();

        var html = await client.GetStringAsync("/internal/custom-event-designer.html");

        Assert.Contains("id=\"secretHashOutput\" class=\"token\" aria-label=\"Generated configuration hash\"", html);
        Assert.Contains("id=\"cleanupCommandOutput\" class=\"token\" aria-label=\"Generated cleanup SQL\"", html);
        Assert.Contains("id=\"commandOutput\" class=\"token\" aria-label=\"Generated server command\"", html);
    }
}
