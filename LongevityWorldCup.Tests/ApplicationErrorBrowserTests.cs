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
}
