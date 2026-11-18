using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadD)]
public sealed class ApplicationErrorBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("{\"type\":\"https://example.test/errors/invalid-request\",\"title\":\"Invalid request\",\"status\":400,\"traceId\":\"internal-trace\"}", "Invalid request")]
    [InlineData("{\"title\":\"Invalid request\",\"errors\":{\"Email\":[\"Enter a valid email address.\"]},\"traceId\":\"internal-trace\"}", "Enter a valid email address.")]
    [InlineData("{\"title\":\"Invalid request\",\"detail\":\"The selected athlete no longer exists.\"}", "The selected athlete no longer exists.")]
    [InlineData("{\"Email\":[\"Enter an email address.\"],\"Name\":[\"Enter a name.\"]}", "Enter an email address.\nEnter a name.")]
    [InlineData("<!doctype html><html><body>Proxy failure</body></html>", "HTTP 502")]
    public async Task SubmissionErrors_ShowTheActionableMessage(string body, string expected)
    {
        await using var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.BaseAddress.ToString()
        });
        await context.RouteAsync("**/frontend-error-test", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "text/html",
            Body = "<html><body><script type='module' src='/js/misc.js'></script></body></html>"
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/frontend-error-test");
        await page.WaitForFunctionAsync("() => typeof window.readApplicationErrorMessage === 'function'");

        var actual = await page.EvaluateAsync<string>(
            "body => window.readApplicationErrorMessage(new Response(body, { status: 502 }))", body);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SubmissionPreflight_CountsUtf8BytesAndKeepsTheExactBoundarySubmittable()
    {
        await using var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString() });
        await context.RouteAsync("**/frontend-error-test", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<html><body><script type='module' src='/js/misc.js'></script></body></html>"
        }));
        var requests = 0;
        await context.RouteAsync("**/api/application/application", route =>
        {
            requests++;
            return route.FulfillAsync(new() { Status = 200, ContentType = "application/json", Body = "{\"success\":true}" });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/frontend-error-test");
        await page.WaitForFunctionAsync("() => typeof window.submitApplicationWithRecovery === 'function'");
        var result = await page.EvaluateAsync<System.Text.Json.JsonElement>("""
            async () => {
                const data = {proofPics:['kept-proof'], name:''};
                const overhead = new Blob([JSON.stringify(data)]).size;
                // Exactly 50 MiB by character count, but one byte over in UTF-8.
                data.name = 'x'.repeat(50*1024*1024-overhead-1) + 'é';
                const rejected = await window.submitApplicationWithRecovery(data);
                const message = await window.readApplicationErrorMessage(rejected.response);
                const unchanged = data.proofPics.length === 1 && data.proofPics[0] === 'kept-proof' && data.name.endsWith('é');
                data.name = data.name.slice(0,-1) + 'e';
                const accepted = await window.submitApplicationWithRecovery(data);
                const proxyMessage = await window.readApplicationErrorMessage(new Response('<html>Too large</html>', {status:413}));
                return {status:rejected.response.status, message, unchanged, accepted:accepted.ok, proxyMessage};
            }
            """);
        Assert.Equal(413, result.GetProperty("status").GetInt32());
        Assert.Contains("Your selections are still here", result.GetProperty("message").GetString());
        Assert.Equal(result.GetProperty("message").GetString(), result.GetProperty("proxyMessage").GetString());
        Assert.True(result.GetProperty("unchanged").GetBoolean());
        Assert.True(result.GetProperty("accepted").GetBoolean());
        Assert.Equal(1, requests);
    }
}
