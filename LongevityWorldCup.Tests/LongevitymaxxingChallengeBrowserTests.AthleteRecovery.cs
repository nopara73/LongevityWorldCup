using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    private const string RecoveryAthletes = """
        [{"Name":"Recovery Athlete","AthleteSlug":"recovery-athlete"},
         {"Name":"Second Athlete","AthleteSlug":"second_athlete","ProfilePicLeaderboardThumb":"/assets/content-images/cr7.webp?v=directory-version"}]
        """;

    [Theory]
    [InlineData("http")]
    [InlineData("network")]
    [InlineData("json")]
    [InlineData("shape")]
    public async Task AthleteDirectory_RetryPreservesTheFormAndUsesTheLatestQuery(string failure)
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        var requests = 0;
        var retryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseRetry = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                if (failure == "network") await route.AbortAsync();
                else await route.FulfillAsync(new()
                {
                    Status = failure == "http" ? 503 : 200, ContentType = "application/json",
                    Body = failure == "json" ? "not json" : "{}"
                });
                return;
            }
            retryStarted.TrySetResult();
            await releaseRetry.Task;
            await FulfillJsonAsync(route, RecoveryAthletes);
        });

        try
        {
            var input = await OpenAthleteRecoveryFormAsync(page);
            await input.FillAsync("Recovery Athlete");
            await Assertions.Expect(page.Locator(".lmx-athlete-feedback")).ToContainTextAsync("Couldn't load athletes");
            Assert.Equal(0, await page.GetByText("No listed athlete found", new() { Exact = true }).CountAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
            await retryStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(input).ToBeFocusedAsync();
            await Assertions.Expect(input).ToHaveAttributeAsync("aria-busy", "true");
            await input.FillAsync("Second");
            await input.PressAsync("Enter");
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__athleteRecoverySubmits"));
            Assert.Equal(2, requests);
            releaseRetry.TrySetResult();
            var option = page.Locator(".lmx-athlete-option");
            await Assertions.Expect(option).ToHaveCountAsync(1);
            await Assertions.Expect(option).ToContainTextAsync("Second Athlete");
            await input.PressAsync("ArrowDown");
            await input.PressAsync("Enter");
            await Assertions.Expect(input).ToHaveAttributeAsync("data-athlete-slug", "second_athlete");
            Assert.Equal("Second Athlete", await input.InputValueAsync());
            Assert.Equal("recover@example.test", await page.Locator("#lmxSignupEmail").InputValueAsync());
            await Assertions.Expect(page.Locator("#lmxSignupAthleteSelected img"))
                .ToHaveAttributeAsync("src", "/assets/content-images/cr7.webp?v=directory-version");
            Assert.Equal(0, await page.Locator(".lmx-athlete-feedback").CountAsync());
            Assert.Equal(0, await page.EvaluateAsync<int>("window.__athleteRecoverySubmits"));
            Assert.Empty(errors);
        }
        finally { releaseRetry.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AthleteDirectory_DelayedLoadKeepsTypingAndRespectsEscape(bool dismiss)
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        var releaseDirectory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseDirectory.Task;
            await FulfillJsonAsync(route, RecoveryAthletes);
        });
        try
        {
            var input = await OpenAthleteRecoveryFormAsync(page);
            await input.FillAsync("Recovery");
            await Assertions.Expect(input).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(page.Locator(".lmx-athlete-feedback")).ToContainTextAsync("Loading athletes");
            await input.FillAsync("Second");
            if (dismiss) await input.PressAsync("Escape");
            releaseDirectory.TrySetResult();
            await Assertions.Expect(input).Not.ToHaveAttributeAsync("aria-busy", "true");
            Assert.Equal("Second", await input.InputValueAsync());
            await Assertions.Expect(input).ToBeFocusedAsync();
            if (dismiss)
            {
                Assert.Equal(0, await page.Locator("#lmxSignupAthlete-autocomplete-list").CountAsync());
                await input.PressAsync("ArrowDown");
            }
            await Assertions.Expect(page.Locator(".lmx-athlete-option")).ToHaveCountAsync(1);
            await Assertions.Expect(page.Locator(".lmx-athlete-option")).ToContainTextAsync("Second Athlete");
        }
        finally { releaseDirectory.TrySetResult(); }
    }

    [Fact]
    public async Task AthleteDirectory_RepeatedRetrySupportsKeyboardAndDoesNotStealFocus()
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        var requests = 0;
        var releaseDirectory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            if (Interlocked.Increment(ref requests) < 3)
                await route.FulfillAsync(new() { Status = 503 });
            else
            {
                await releaseDirectory.Task;
                await FulfillJsonAsync(route, RecoveryAthletes);
            }
        });
        try
        {
            var input = await OpenAthleteRecoveryFormAsync(page);
            await input.FillAsync("Recovery");
            var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });
            await Assertions.Expect(retry).ToBeVisibleAsync();
            await input.PressAsync("Tab");
            await Assertions.Expect(page.Locator("#lmxSignupAthleteClear")).ToBeFocusedAsync();
            await page.Keyboard.PressAsync("Tab");
            await Assertions.Expect(retry).ToBeFocusedAsync();
            await retry.PressAsync("Enter");
            await Assertions.Expect(input).ToBeFocusedAsync();
            await Assertions.Expect(retry).ToBeVisibleAsync();
            Assert.Equal(2, requests);
            await retry.FocusAsync();
            await retry.PressAsync("Escape");
            await Assertions.Expect(input).ToBeFocusedAsync();
            Assert.Equal(0, await retry.CountAsync());
            await input.PressAsync("ArrowDown");
            await retry.ClickAsync();
            await Assertions.Expect(input).ToHaveAttributeAsync("aria-busy", "true");
            await page.Locator("#lmxSignupEmail").FocusAsync();
            releaseDirectory.TrySetResult();
            await Assertions.Expect(input).Not.ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(page.Locator("#lmxSignupEmail")).ToBeFocusedAsync();
            Assert.Equal(0, await page.Locator("#lmxSignupAthlete-autocomplete-list").CountAsync());
            Assert.Equal("Recovery", await input.InputValueAsync());
            await input.FocusAsync();
            await page.Locator(".lmx-athlete-option").ClickAsync();
            await Assertions.Expect(input).ToHaveAttributeAsync("data-athlete-slug", "recovery-athlete");
            Assert.Equal(3, requests);
        }
        finally { releaseDirectory.TrySetResult(); }
    }

    [Fact]
    public async Task AthleteDirectory_ValidEmptyDirectoryShowsNoMatchWithoutRetry()
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/data/athletes", route => FulfillJsonAsync(route, "[]"));
        var input = await OpenAthleteRecoveryFormAsync(page);
        await input.FillAsync("Recovery");
        await Assertions.Expect(page.GetByText("No listed athlete found", new() { Exact = true })).ToBeVisibleAsync();
        Assert.Equal(0, await page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).CountAsync());
        await Assertions.Expect(input).Not.ToHaveAttributeAsync("aria-busy", "true");
    }

    [Fact]
    public async Task AthleteDirectory_FailureDoesNotBlockSignupWithoutAnAthleteProfile()
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        JsonElement? signup = null;
        await page.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { Status = 503 }));
        await page.RouteAsync("**/api/longevitymaxxing/signup", async route =>
        {
            signup = JsonSerializer.Deserialize<JsonElement>(route.Request.PostData!);
            await FulfillJsonAsync(route, """{"message":"Check your email."}""");
        });
        await page.GotoAsync("/longevitymaxxing", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#lmxSignupEmail").FillAsync("recover@example.test");
        await page.Locator("#lmxSignupName").FillAsync("Recovery Participant");
        Assert.Equal(0, await page.Locator(".lmx-athlete-feedback").CountAsync());
        await page.Locator("#lmxSignupForm button[type=submit]").ClickAsync();
        await Assertions.Expect(page.Locator("#lmxSignupStatus")).ToContainTextAsync("Check your email.");
        Assert.NotNull(signup);
        Assert.Equal("Recovery Participant", signup.Value.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, signup.Value.GetProperty("athleteLink").ValueKind);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AthleteDirectory_VisibleFeedbackUpdatesAfterClickingNonFocusableContent(bool succeeds)
    {
        await using var context = await NewAthleteRecoveryContextAsync();
        var page = await context.NewPageAsync();
        var releaseDirectory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/data/athletes", async route =>
        {
            await releaseDirectory.Task;
            await route.FulfillAsync(new() { Status = succeeds ? 200 : 503, ContentType = "application/json", Body = RecoveryAthletes });
        });
        try
        {
            var input = await OpenAthleteRecoveryFormAsync(page);
            await input.FillAsync("Recovery");
            var message = page.Locator(".lmx-athlete-feedback [role=status]");
            await Assertions.Expect(message).ToContainTextAsync("Loading athletes");
            await page.Locator(".lmx-athlete-search > i").ClickAsync();
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement === document.body"));
            releaseDirectory.TrySetResult();
            if (succeeds)
                await Assertions.Expect(page.Locator(".lmx-athlete-option")).ToHaveCountAsync(1);
            else
                await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToBeVisibleAsync();
            Assert.True(await page.EvaluateAsync<bool>("document.activeElement === document.body"));
            Assert.Equal("Recovery", await input.InputValueAsync());
        }
        finally { releaseDirectory.TrySetResult(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AthleteDirectory_RetryRemainsReadableOnHoverAndPress(bool dark)
    {
        await using var context = await NewAthleteRecoveryContextAsync(width: 320, dark: dark);
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { Status = 503 }));
        var input = await OpenAthleteRecoveryFormAsync(page);
        await input.FillAsync("Recovery");
        var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });
        await retry.HoverAsync();
        await retry.EvaluateAsync("async element => { getComputedStyle(element).color; await Promise.all(element.getAnimations().map(animation => animation.finished.catch(() => {}))); }");
        BrowserContrast.AssertMinimum("Retry hover", await BrowserContrast.MeasureVisibleTextAsync(page, ".lmx-athlete-retry"));
        await page.Mouse.DownAsync();
        try
        {
            await retry.EvaluateAsync("async element => { getComputedStyle(element).color; await Promise.all(element.getAnimations().map(animation => animation.finished.catch(() => {}))); }");
            BrowserContrast.AssertMinimum("Retry pressed", await BrowserContrast.MeasureVisibleTextAsync(page, ".lmx-athlete-retry"));
        }
        finally { await page.Mouse.UpAsync(); }
    }

    private async Task<IBrowserContext> NewAthleteRecoveryContextAsync(int width = 390, bool dark = false)
    {
        var context = await Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 },
            TimezoneId = "Asia/Bangkok", ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        return context;
    }

    private static async Task<ILocator> OpenAthleteRecoveryFormAsync(IPage page)
    {
        await page.GotoAsync("/longevitymaxxing", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.Locator("#lmxSignupEmail").FillAsync("recover@example.test");
        await page.Locator("label:has(input[name=lmxSignupIdentity][value=athlete])").ClickAsync();
        await page.Locator("#lmxSignupForm").EvaluateAsync("""
            form => {
                window.__athleteRecoverySubmits = 0;
                form.addEventListener('submit', event => {
                    event.preventDefault(); event.stopImmediatePropagation(); window.__athleteRecoverySubmits++;
                }, true);
            }
            """);
        return page.Locator("#lmxSignupAthlete");
    }
}
