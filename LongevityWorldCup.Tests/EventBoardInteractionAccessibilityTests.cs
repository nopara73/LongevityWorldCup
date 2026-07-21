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
        Assert.Contains("width:44px;", html);
        Assert.Contains("height:44px;", html);
    }

    [Fact]
    public async Task EventLoadingFailure_HasLiveStatusAndRetryAction()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/events");

        Assert.Contains("id=\"eventsStatus\" class=\"events-status\" role=\"status\" aria-live=\"polite\"", html);
        Assert.Contains("function renderEventsLoadError(retry)", html);
        Assert.Contains("recovery.setAttribute('role', 'alert');", html);
        Assert.Contains("retryButton.type = 'button';", html);
        Assert.Contains("retryButton.className = 'events-retry-button';", html);
        Assert.Contains("window.loadEventsTable(maxRows, showViewAll, athleteId, linkNames, options);", html);
    }
}
