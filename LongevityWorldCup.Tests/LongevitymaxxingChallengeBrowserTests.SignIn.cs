using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    private const string SignInFailure = "Couldn’t confirm the email was sent. Try again.";
    private const string SignupMissing = "No challenge signup was found for that email.";
    private const string SignInLimited = "Too many attempts. Please try again later.";

    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(429)]
    public async Task SignIn_LateResponseKeepsTheNewAddressAndPendingStateAcrossTabs(int status)
    {
        await using var context = await SignInContextAsync();
        var requests = new ConcurrentQueue<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/resend", async route =>
        {
            requests.Enqueue(ReadSignInEmail(route));
            if (requests.Count == 1)
            {
                started.TrySetResult();
                await release.Task;
                await route.FulfillAsync(new() { Status = status, ContentType = "application/json", Body = JsonSerializer.Serialize(new { message = status == 400 ? SignupMissing : status == 429 ? SignInLimited : "Link sent." }) });
            }
            else await FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}");
        });
        try
        {
            var page = await OpenSignInAsync(context);
            var input = page.Locator("#lmxResendEmail");
            var button = page.Locator("#lmxResendButton");
            await input.FillAsync("first@example.test");
            await button.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(button).ToBeFocusedAsync();
            await input.FillAsync("second@example.test");
            await input.PressAsync("Enter");
            await page.Locator("#lmxSignupTab").ClickAsync();
            await page.Locator("#lmxSigninTab").ClickAsync();
            await Assertions.Expect(button).ToHaveTextAsync("Sending…");
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "true");
            await input.FocusAsync();
            release.TrySetResult();
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-busy", "false");
            await Assertions.Expect(input).ToHaveValueAsync("second@example.test");
            await Assertions.Expect(input).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToContainTextAsync("first@example.test");
            await Assertions.Expect(button).ToHaveTextAsync("Send check-in link");
            Assert.Single(requests);

            await input.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync("Check second@example.test for a link to continue.");
            await Assertions.Expect(input).ToHaveValueAsync("second@example.test");
            await input.FillAsync("third@example.test");
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToBeEmptyAsync();
            Assert.Equal(["first@example.test", "second@example.test"], requests);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("network")]
    [InlineData("gateway")]
    [InlineData("unexpected-response")]
    [InlineData("missing-signup")]
    [InlineData("rate-limit")]
    public async Task SignIn_FailureKeepsTheEmailAndKeyboardRetry(string failure)
    {
        await using var context = await SignInContextAsync();
        var requests = new ConcurrentQueue<string>();
        await context.RouteAsync("**/api/longevitymaxxing/resend", async route =>
        {
            requests.Enqueue(ReadSignInEmail(route));
            if (requests.Count > 1) await FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}");
            else if (failure == "network") await route.AbortAsync();
            else await route.FulfillAsync(new()
            {
                Status = failure == "gateway" ? 503 : failure == "missing-signup" ? 400 : failure == "rate-limit" ? 429 : 200,
                ContentType = failure == "gateway" ? "text/html" : "application/json",
                Body = failure == "gateway" ? "<h1>Unavailable</h1>" : failure == "unexpected-response" ? "{}" : JsonSerializer.Serialize(new { message = failure == "missing-signup" ? SignupMissing : SignInLimited })
            });
        });
        var page = await OpenSignInAsync(context);
        var input = page.Locator("#lmxResendEmail");
        var button = page.Locator("#lmxResendButton");
        await input.FillAsync("reader@example.test");
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveTextAsync("Retry");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("reader@example.test");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync(failure == "missing-signup" ? SignupMissing : failure == "rate-limit" ? SignInLimited : SignInFailure);
        await button.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync("Check reader@example.test for a link to continue.");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveValueAsync("reader@example.test");
        Assert.Equal(["reader@example.test", "reader@example.test"], requests);
    }

    [Theory]
    [InlineData("Reader <reader@example.test>")]
    [InlineData("mailto:reader@example.test?subject=Challenge")]
    [InlineData("  reader@example.test  ")]
    public async Task SignIn_CopiedEmailFormatsKeepTheExistingRequestContract(string value)
    {
        await using var context = await SignInContextAsync();
        var requests = new ConcurrentQueue<string>();
        await context.RouteAsync("**/api/longevitymaxxing/resend", route =>
        {
            requests.Enqueue(ReadSignInEmail(route));
            return FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}");
        });
        var page = await OpenSignInAsync(context);
        await page.Locator("#lmxResendEmail").FillAsync(value);
        await page.Locator("#lmxResendEmail").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync("Check reader@example.test for a link to continue.");
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToHaveValueAsync("reader@example.test");
        Assert.Equal("reader@example.test", Assert.Single(requests));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    public async Task SignIn_InvalidEmailCanBeCorrectedWithoutSendingFirst(string email)
    {
        await using var context = await SignInContextAsync();
        var requests = 0;
        await context.RouteAsync("**/api/longevitymaxxing/resend", route => { requests++; return FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}"); });
        var page = await OpenSignInAsync(context);
        var input = page.Locator("#lmxResendEmail");
        await input.FillAsync(email);
        await page.Locator("#lmxResendButton").ClickAsync();
        await Assertions.Expect(input).ToBeFocusedAsync();
        Assert.False(await input.EvaluateAsync<bool>("el => el.validity.valid"));
        Assert.Equal(0, requests);
        await input.FillAsync("reader@example.test");
        await input.PressAsync("Enter");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToContainTextAsync("reader@example.test");
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task SignIn_TimeoutRecoversWithoutAResendOrALateOverwrite()
    {
        await using var context = await SignInContextAsync();
        var requests = new ConcurrentQueue<string>();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/longevitymaxxing/resend", async route =>
        {
            requests.Enqueue(ReadSignInEmail(route));
            if (requests.Count == 1)
            {
                started.TrySetResult();
                await release.Task;
                try { await FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}"); }
                finally { finished.TrySetResult(); }
            }
            else await FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}");
        });
        try
        {
            var page = await context.NewPageAsync();
            await page.Clock.InstallAsync();
            await OpenSignInPageAsync(page);
            var input = page.Locator("#lmxResendEmail");
            var button = page.Locator("#lmxResendButton");
            await input.FillAsync("first@example.test");
            await button.ClickAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Clock.FastForwardAsync(65001);
            await Assertions.Expect(button).ToHaveTextAsync("Retry");
            await Assertions.Expect(button).ToBeFocusedAsync();
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync(SignInFailure);
            Assert.Single(requests);
            await input.FillAsync("second@example.test");
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToBeEmptyAsync();
            await Assertions.Expect(button).ToHaveTextAsync("Send check-in link");
            await input.PressAsync("Enter");
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync("Check second@example.test for a link to continue.");
            release.TrySetResult();
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync("Check second@example.test for a link to continue.");
            Assert.Equal(["first@example.test", "second@example.test"], requests);
        }
        finally { release.TrySetResult(); }
    }

    [Theory]
    [InlineData("", "Challenge reminder emails stopped.", "stop-emails")]
    [InlineData("community-call", "Community call emails stopped.", "stop-community-call-emails")]
    [InlineData("discussion", "Discussion activity follows your daily Challenge email setting.", null)]
    public async Task SignIn_EditingKeepsUnrelatedReminderNotices(string scope, string notice, string? endpoint)
    {
        await using var context = await SignInContextAsync();
        var stops = new ConcurrentQueue<string>();
        await context.RouteAsync("**/api/longevitymaxxing/stop-*", route =>
        {
            stops.Enqueue(new Uri(route.Request.Url).AbsolutePath);
            return FulfillJsonAsync(route, "{}");
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/longevitymaxxing?stop=browser-stop&scope={scope}");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync(notice);
        await page.Locator("#lmxResendEmail").FillAsync("reader@example.test");
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToHaveTextAsync(notice);
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToBeFocusedAsync();
        if (endpoint is null) Assert.Empty(stops);
        else Assert.Equal($"/api/longevitymaxxing/{endpoint}", Assert.Single(stops));
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(1280, true)]
    public async Task SignIn_LongAddressFitsTheConfirmation(int width, bool dark)
    {
        await using var context = await SignInContextAsync(width, dark);
        await context.RouteAsync("**/api/longevitymaxxing/resend", route => FulfillJsonAsync(route, "{\"message\":\"Link sent.\"}"));
        var page = await OpenSignInAsync(context);
        var email = new string('a', 60) + "@" + new string('b', 60) + "." + new string('c', 60) + ".test";
        await page.Locator("#lmxResendEmail").FillAsync(email);
        await page.Locator("#lmxResendButton").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxResendStatus")).ToContainTextAsync(email);
        Assert.True(await page.Locator("#lmxResendStatus").EvaluateAsync<bool>("el => { const box = el.getBoundingClientRect(); const form = el.closest('form').getBoundingClientRect(); return document.documentElement.scrollWidth <= innerWidth && el.scrollWidth <= el.clientWidth && box.left >= form.left - 1 && box.right <= form.right + 1 && document.querySelector('#lmxResendButton').getBoundingClientRect().height >= 44; }"));
        await Assertions.Expect(page.Locator("#lmxResendButton")).ToHaveAttributeAsync("aria-describedby", "lmxResendStatus");
        await Assertions.Expect(page.Locator("#lmxResendEmail")).ToHaveAttributeAsync("aria-describedby", "lmxResendStatus");
    }

    private async Task<IBrowserContext> SignInContextAsync(int width = 390, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new() { BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        return context;
    }

    private static async Task<IPage> OpenSignInAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await OpenSignInPageAsync(page);
        return page;
    }

    private static async Task OpenSignInPageAsync(IPage page)
    {
        await page.GotoAsync("/longevitymaxxing");
        await Assertions.Expect(page.Locator("[data-timezone-picker][data-select-id='lmxSignupTimeZone']")).ToHaveAttributeAsync("data-wired", "true");
        await page.Locator("#lmxSigninTab").ClickAsync();
    }

    private static string ReadSignInEmail(IRoute route)
    {
        Assert.Equal("POST", route.Request.Method);
        Assert.Equal("application/json", route.Request.Headers["content-type"]);
        using var payload = JsonDocument.Parse(route.Request.PostData!);
        Assert.Single(payload.RootElement.EnumerateObject());
        return payload.RootElement.GetProperty("email").GetString()!;
    }
}
