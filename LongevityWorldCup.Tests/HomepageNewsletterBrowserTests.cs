using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class HomepageNewsletterBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(200, "Subscription successful.", false)]
    [InlineData(400, "This email is already subscribed.", false)]
    [InlineData(503, "Unavailable.", true)]
    public async Task LateResponse_KeepsANewerEmailAndItsFocus(int status, string body, bool isError)
    {
        await using var context = await NewsletterContextAsync();
        var requests = new ConcurrentQueue<SubscriptionRequest>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/home/subscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            if (requests.Count == 1)
            {
                started.TrySetResult();
                await release.Task;
                await route.FulfillAsync(new() { Status = status, ContentType = "text/plain", Body = body });
            }
            else await route.FulfillAsync(new() { Status = 200, Body = "Subscription successful." });
        });
        try
        {
            var page = await OpenNewsletterAsync(context);
            var input = page.Locator("#emailInput");
            var button = page.Locator("#newsletter-form button[type=submit]");
            await input.FillAsync("first@example.test");
            await button.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(button).ToBeFocusedAsync();
            await input.FillAsync("second@example.test");
            await input.PressAsync("Enter");
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(input).ToBeFocusedAsync();
            release.TrySetResult();

            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(input).ToHaveValueAsync("second@example.test");
            await Assertions.Expect(input).ToBeFocusedAsync();
            await Assertions.Expect(input).ToBeInViewportAsync();
            await Assertions.Expect(page.Locator("#newsletterStatus")).ToContainTextAsync("first@example.test");
            Assert.Equal(isError, await page.Locator("#newsletterStatus").EvaluateAsync<bool>("el => el.classList.contains('is-error')"));
            await Assertions.Expect(button).ToHaveTextAsync("Subscribe");
            Assert.Single(requests);
            await AssertNoDialogAsync(page);

            await input.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync("Subscribed with second@example.test.");
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
    [InlineData("limit")]
    [InlineData("validation-json")]
    public async Task Failure_RetainsTheAddressAndOffersKeyboardRetry(string failure)
    {
        await using var context = await NewsletterContextAsync();
        var requests = new ConcurrentQueue<SubscriptionRequest>();
        const string limitMessage = "Too many subscription attempts. Please try again later.";
        await context.RouteAsync("**/api/home/subscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            if (requests.Count > 1)
                await route.FulfillAsync(new() { Status = 200, Body = "Subscription successful." });
            else if (failure == "network") await route.AbortAsync("failed");
            else if (failure == "gateway")
                await route.FulfillAsync(new() { Status = 503, ContentType = "text/html", Body = "<h1>Gateway unavailable</h1>" });
            else if (failure == "limit")
                await route.FulfillAsync(new() { Status = 400, ContentType = "text/plain", Body = limitMessage });
            else
                await route.FulfillAsync(new() { Status = 400, ContentType = "application/problem+json", Body = "{\"errors\":{\"Email\":[\"Invalid email\"]}}" });
        });
        var page = await OpenNewsletterAsync(context);
        var input = page.Locator("#emailInput");
        var button = page.Locator("#newsletter-form button[type=submit]");
        await input.FillAsync("reader@example.test");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Retry");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("reader@example.test");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync(
            failure == "limit" ? limitMessage : "Couldn’t confirm the subscription. Try again.");
        Assert.Null(await input.GetAttributeAsync("aria-invalid"));
        await AssertNoDialogAsync(page);

        await button.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync("Subscribed with reader@example.test.");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("");
        Assert.Equal(["reader@example.test", "reader@example.test"], requests.Select(request => request.Email));
    }

    [Theory]
    [InlineData("", "Enter your email address.")]
    [InlineData("not-an-email", "Enter a valid email address.")]
    public async Task InvalidAddress_ShowsItsErrorBesideTheFieldAndClearsItWhenEdited(string email, string message)
    {
        await using var context = await NewsletterContextAsync();
        var requests = new ConcurrentQueue<SubscriptionRequest>();
        await context.RouteAsync("**/api/home/subscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            await route.FulfillAsync(new() { Status = 200, Body = "Subscription successful." });
        });
        var page = await OpenNewsletterAsync(context);
        var input = page.Locator("#emailInput");
        await input.FillAsync(email);
        await page.Locator("#newsletter-form button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync(message);
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-invalid", "true");
        Assert.Empty(requests);
        await AssertNoDialogAsync(page);

        await input.FillAsync("Reader@example.test");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToBeEmptyAsync();
        Assert.Null(await input.GetAttributeAsync("aria-invalid"));
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync("Subscribed with Reader@example.test.");
        Assert.Equal("Reader@example.test", Assert.Single(requests).Email);
    }

    [Fact]
    public async Task LostSuccessResponse_CanRecoverThroughTheServersDuplicateReply()
    {
        await using var context = await NewsletterContextAsync();
        var subscribed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requests = new ConcurrentQueue<SubscriptionRequest>();
        await context.RouteAsync("**/api/home/subscribe", async route =>
        {
            var request = ReadRequest(route);
            requests.Enqueue(request);
            if (subscribed.Add(request.Email)) await route.AbortAsync("failed");
            else await route.FulfillAsync(new() { Status = 400, ContentType = "text/plain", Body = "This email is already subscribed." });
        });
        var page = await OpenNewsletterAsync(context);
        var input = page.Locator("#emailInput");
        var button = page.Locator("#newsletter-form button[type=submit]");
        await input.FillAsync("reader@example.test");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Retry");
        Assert.Single(subscribed);
        await button.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync("reader@example.test is already subscribed.");
        await Assertions.Expect(input).ToHaveValueAsync("");
        await Assertions.Expect(button).ToHaveTextAsync("Subscribe");
        Assert.False(await page.Locator("#newsletterStatus").EvaluateAsync<bool>("el => el.classList.contains('is-error')"));
        Assert.Equal(2, requests.Count);
        Assert.Single(subscribed);
        await AssertNoDialogAsync(page);
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(1280, true)]
    public async Task LongAddressFeedback_WrapsAndClearsBeforeTheNextSubscription(int width, bool dark)
    {
        await using var context = await NewsletterContextAsync(width, dark);
        var requests = new ConcurrentQueue<SubscriptionRequest>();
        await context.RouteAsync("**/api/home/subscribe", async route =>
        {
            requests.Enqueue(ReadRequest(route));
            await route.FulfillAsync(new() { Status = 200, Body = "Subscription successful." });
        });
        var page = await OpenNewsletterAsync(context);
        var input = page.Locator("#emailInput");
        var email = new string('a', 64) + "@" + new string('b', 60) + ".example";
        await input.FillAsync(email);
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToHaveTextAsync($"Subscribed with {email}.");
        var fits = await page.Locator("#newsletterStatus").EvaluateAsync<bool>(
            "el => el.scrollWidth <= el.clientWidth + 1 && document.documentElement.scrollWidth <= innerWidth + 1");
        Assert.True(fits, $"Subscription feedback overflowed at {width}px (dark={dark}).");
        await input.FillAsync("next@example.test");
        await Assertions.Expect(page.Locator("#newsletterStatus")).ToBeEmptyAsync();
        Assert.Single(requests);
    }

    private Task<IBrowserContext> NewsletterContextAsync(int width = 390, bool dark = false) =>
        NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });

    private static async Task<IPage> OpenNewsletterAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => typeof fetchWithTimeout === 'function'");
        return page;
    }

    private static async Task AssertNoDialogAsync(IPage page) =>
        Assert.False(await page.EvaluateAsync<bool>(
            "() => [...document.querySelectorAll('[role=dialog],[role=alertdialog]')].some(el => el.getBoundingClientRect().width > 0)"));

    private static SubscriptionRequest ReadRequest(IRoute route)
    {
        using var body = JsonDocument.Parse(route.Request.PostData ?? "{}");
        return new(body.RootElement.GetProperty("email").GetString()!, route.Request.Method,
            route.Request.Headers.GetValueOrDefault("content-type", ""));
    }

    private sealed record SubscriptionRequest(string Email, string Method, string ContentType);
}
