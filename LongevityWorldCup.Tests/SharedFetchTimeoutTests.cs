using System.Text.RegularExpressions;
using Xunit;

namespace LongevityWorldCup.Tests;


[Collection(HttpTestCollections.ReadOnly)]
public sealed class SharedFetchTimeoutTests(TestWebApplicationFactory sharedFactory)
{
    [Fact]
    public async Task SharedFetchWithTimeout_AbortsTimedOutRequestsWhenSupported()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var script = await ReadHeaderScriptAsync(client);

        Assert.Contains("function fetchWithTimeout(url, options = {}, timeout = 10000)", script);
        Assert.Contains("new AbortController()", script);
        Assert.Contains("signal: timeoutController.signal", script);
        Assert.Contains("timeoutController.abort();", script);
        Assert.Contains("err.name === 'AbortError' ? new Error('Request timed out') : err", script);
    }

    [Fact]
    public async Task SharedHeaderModals_AvoidNativeDialogTopLayer()
    {
        var factory = sharedFactory;
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.Contains("id=\"custom-alert\" role=\"alertdialog\"", html);
        Assert.Contains("id=\"loading-dialog\" role=\"status\" aria-modal=\"true\"", html);
        var script = await ReadHeaderScriptAsync(client);
        Assert.Contains("customAlertDialog.hidden = false;", script);
        Assert.Contains("loadingDialog.hidden = false;", script);
        Assert.Contains("function trapFocusWithin(container, event)", script);
        Assert.Contains("loadingDialog.addEventListener('keydown'", script);
        Assert.DoesNotContain("<dialog", html);
        Assert.DoesNotContain("showModal", script);
        Assert.DoesNotContain("::backdrop", await client.GetStringAsync("/css/site-header.css"));
    }

    private static async Task<string> ReadHeaderScriptAsync(HttpClient client)
    {
        var html = await client.GetStringAsync("/");
        var source = Regex.Match(html, "src=\"(?<url>/js/site-header\\.js\\?v=[^\"]+)\"");
        Assert.True(source.Success, "The shared header must load its versioned script.");
        return await client.GetStringAsync(source.Groups["url"].Value);
    }
}
