using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class NewsletterUnsubscribeBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("", "Enter your email address.")]
    [InlineData("not-an-email", "Enter a valid email address.")]
    [InlineData("reader@", "Enter a valid email address.")]
    public async Task InvalidEmail_IsExplainedBesideTheFieldWithoutARequest(string email, string explanation)
    {
        await using var context = await UnsubscribeContextAsync();
        var requests = 0;
        await context.RouteAsync("**/api/home/unsubscribe", route => { requests++; return route.AbortAsync(); });
        var page = await OpenAsync(context);
        var input = page.Locator("#email");
        await input.FillAsync(email);
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync(explanation);
        await Assertions.Expect(input).ToHaveCSSAsync("border-top-color", await page.Locator("#message").EvaluateAsync<string>("el => getComputedStyle(el).borderLeftColor"));
        Assert.Equal(0, requests);
        await input.FillAsync("reader@example.test");
        await Assertions.Expect(input).Not.ToHaveAttributeAsync("aria-invalid", "true");
        await Assertions.Expect(page.Locator("#message")).ToBeEmptyAsync();
    }

    [Theory]
    [InlineData(200)]
    [InlineData(503)]
    [InlineData(400)]
    public async Task LateResponse_ConfirmsOnlyItsAddressAndPreservesTheNextEdit(int status)
    {
        await using var context = await UnsubscribeContextAsync();
        var requests = new ConcurrentQueue<RemovalRequest>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/home/unsubscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            if (requests.Count == 1)
            {
                started.TrySetResult();
                await release.Task;
                await route.FulfillAsync(new() { Status = status, ContentType = "text/plain", Body = status == 400 ? "Too many newsletter attempts. Please try again later." : "Response" });
            }
            else await route.FulfillAsync(new() { Status = 200, Body = "Unsubscription successful." });
        });
        try
        {
            var page = await OpenAsync(context);
            var input = page.Locator("#email");
            var button = page.Locator("#unsubscribe-button");
            await input.FillAsync("first@example.test");
            await button.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(button).ToBeFocusedAsync();
            await input.FillAsync("second@example.test");
            await input.PressAsync("Enter");
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
            release.TrySetResult();
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(input).ToHaveValueAsync("second@example.test");
            await Assertions.Expect(input).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#message")).ToContainTextAsync("first@example.test");
            await Assertions.Expect(button).ToHaveTextAsync("Unsubscribe");
            Assert.Single(requests);

            await input.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync("Removed second@example.test from the yearly newsletter.");
            await Assertions.Expect(input).ToHaveValueAsync("");
            await Assertions.Expect(input).ToBeFocusedAsync();
            Assert.Equal(["first@example.test", "second@example.test"], requests.Select(request => request.Email));
            Assert.All(requests, request =>
            {
                Assert.Equal("POST", request.Method);
                Assert.Equal("application/json", request.ContentType);
            });
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("network")]
    [InlineData("gateway")]
    [InlineData("validation-json")]
    [InlineData("limit")]
    public async Task Failure_KeepsTheAddressAndFocusForKeyboardRetry(string failure)
    {
        await using var context = await UnsubscribeContextAsync();
        var requests = new ConcurrentQueue<RemovalRequest>();
        const string limitMessage = "Too many newsletter attempts. Please try again later.";
        await context.RouteAsync("**/api/home/unsubscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            if (requests.Count > 1) await route.FulfillAsync(new() { Status = 200, Body = "Unsubscription successful." });
            else if (failure == "network") await route.AbortAsync();
            else await route.FulfillAsync(new()
            {
                Status = failure == "gateway" ? 503 : 400,
                ContentType = failure == "gateway" ? "text/html" : failure == "limit" ? "text/plain" : "application/problem+json",
                Body = failure == "gateway" ? "<h1>Unavailable</h1>" : failure == "limit" ? limitMessage : "{\"errors\":{\"Email\":[\"Invalid email\"]}}"
            });
        });
        var page = await OpenAsync(context);
        var input = page.Locator("#email");
        var button = page.Locator("#unsubscribe-button");
        await input.FillAsync("reader@example.test");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Retry");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("reader@example.test");
        await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync(failure == "limit" ? limitMessage : "Couldn’t confirm the removal. Try again.");
        await button.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync("Removed reader@example.test from the yearly newsletter.");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("");
        Assert.Equal(["reader@example.test", "reader@example.test"], requests.Select(request => request.Email));
    }

    [Fact]
    public async Task Timeout_RecoversAndALateResponseCannotOverwriteTheNextRemoval()
    {
        await using var context = await UnsubscribeContextAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new ConcurrentQueue<RemovalRequest>();
        await context.RouteAsync("**/api/home/unsubscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            if (requests.Count == 1)
            {
                started.TrySetResult();
                await release.Task;
                try { await route.FulfillAsync(new() { Status = 200, Body = "Unsubscription successful." }); }
                finally { finished.TrySetResult(); }
            }
            else await route.FulfillAsync(new() { Status = 200, Body = "Unsubscription successful." });
        });
        try
        {
            var page = await context.NewPageAsync();
            await page.Clock.InstallAsync();
            await page.GotoAsync("/unsubscribe", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var input = page.Locator("#email");
            var button = page.Locator("#unsubscribe-button");
            await input.FillAsync("first@example.test");
            await button.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Clock.FastForwardAsync(10001);
            await Assertions.Expect(button).ToHaveTextAsync("Retry");
            await Assertions.Expect(button).ToBeFocusedAsync();
            Assert.Single(requests);
            await input.FillAsync("second@example.test");
            await input.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync("Removed second@example.test from the yearly newsletter.");
            release.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync("Removed second@example.test from the yearly newsletter.");
            await Assertions.Expect(input).ToHaveValueAsync("");
            Assert.Equal(["first@example.test", "second@example.test"], requests.Select(request => request.Email));
        }
        finally { release.TrySetResult(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
    }

    [Theory]
    [InlineData(320, 844, false, "/unsubscribe.html")]
    [InlineData(1280, 844, true, "/unsubscribe")]
    [InlineData(844, 390, false, "/unsubscribe")]
    public async Task Confirmation_ContainsLongAddressesAndClearsWhenEditing(int width, int height, bool dark, string path)
    {
        await using var context = await UnsubscribeContextAsync(width, height, dark);
        await context.RouteAsync("**/api/home/unsubscribe", route => route.FulfillAsync(new() { Status = 200, Body = "Unsubscription successful." }));
        var page = await OpenAsync(context, path);
        var email = new string('a', 60) + "@" + new string('b', 60) + "." + new string('c', 60) + ".test";
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#unsubscribe-button").ClickAsync();
        await Assertions.Expect(page.Locator("#message")).ToHaveTextAsync($"Removed {email} from the yearly newsletter.");
        var geometry = await page.EvaluateAsync<bool>("""
            () => {
                const message = document.querySelector('#message');
                const form = document.querySelector('#unsubscribe-form').getBoundingClientRect();
                const box = message.getBoundingClientRect();
                return document.documentElement.scrollWidth <= innerWidth && message.scrollWidth <= message.clientWidth
                    && box.left >= form.left - 1 && box.right <= form.right + 1
                    && document.querySelector('#unsubscribe-button').getBoundingClientRect().height >= 44;
            }
            """);
        Assert.True(geometry);
        await page.Locator("#email").FillAsync("another@example.test");
        await Assertions.Expect(page.Locator("#message")).ToBeEmptyAsync();
        await Assertions.Expect(page.Locator("#unsubscribe-button")).ToHaveTextAsync("Unsubscribe");
        Assert.EndsWith("/unsubscribe", page.Url);
    }

    private Task<IBrowserContext> UnsubscribeContextAsync(int width = 390, int height = 844, bool dark = false) =>
        NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = width, Height = height }, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce });

    private static async Task<IPage> OpenAsync(IBrowserContext context, string path = "/unsubscribe")
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#unsubscribe-button")).ToBeVisibleAsync();
        return page;
    }

    private static RemovalRequest ReadRequest(IRoute route)
    {
        using var payload = JsonDocument.Parse(route.Request.PostData!);
        return new(route.Request.Method, route.Request.Headers["content-type"], payload.RootElement.GetProperty("email").GetString()!);
    }
    private sealed record RemovalRequest(string Method, string ContentType, string Email);
}
