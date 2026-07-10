using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class EventBoardInteractionAccessibilityTests
{
    [Fact]
    public async Task CustomEventExpander_HasVisibleKeyboardFocus()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains(".custom-event-expander:focus-visible{", html);
        Assert.Contains("box-shadow:0 0 0 3px rgba(0,188,212,.28) !important;", html);
    }
}
