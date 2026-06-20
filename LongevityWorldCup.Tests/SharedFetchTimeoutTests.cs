using Xunit;

namespace LongevityWorldCup.Tests;

public sealed class SharedFetchTimeoutTests
{
    [Fact]
    public async Task SharedFetchWithTimeout_AbortsTimedOutRequestsWhenSupported()
    {
        using var factory = new TestWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("function fetchWithTimeout(url, options = {}, timeout = 10000)", html);
        Assert.Contains("new AbortController()", html);
        Assert.Contains("signal: timeoutController.signal", html);
        Assert.Contains("timeoutController.abort();", html);
        Assert.Contains("err.name === 'AbortError' ? new Error('Request timed out') : err", html);
    }
}
