using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, false, 16)]
    [InlineData(390, true, 32)]
    public async Task EmptyPickerHints_KeepTheirTextAndFitNarrowAndEnlargedLayouts(int width, bool dark, int font)
    {
        await using var context = await Browser.NewContextAsync(new() {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, ReducedMotion = ReducedMotion.Reduce
        });
        await RouteChallengeResourcesAsync(context);
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true,
            includeMentionParticipants: true, includeDiscussionIdentityParticipants: true))!.AsObject();
        state["public"]!["phase"] = "signup";
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing");
        await page.EvaluateAsync("font => document.documentElement.style.fontSize = font + 'px'", font);
        async Task AssertHint(string selector, string text)
        {
            var hint = page.Locator(selector);
            await Assertions.Expect(hint).ToHaveTextAsync(text);
            await Assertions.Expect(hint.Locator("svg")).ToHaveCountAsync(1);
            Assert.True(await hint.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"));
            Assert.True(await hint.EvaluateAsync<bool>("e => e.parentElement.scrollWidth <= e.parentElement.clientWidth + 1"));
            await hint.ScrollIntoViewIfNeededAsync();
            var output = Environment.GetEnvironmentVariable("REVIEW_GALLERY");
            if (!string.IsNullOrEmpty(output)) await page.ScreenshotAsync(new() { Path = Path.Combine(output, $"hint-{selector.TrimStart('.')}-{width}-font{font}.png") });
        }
        await page.Locator("#lmxSignupTimeZoneButton").ClickAsync();
        await page.Locator("#lmxSignupTimeZoneSearch").FillAsync("zzzzemptyreview");
        await AssertHint(".lmx-timezone-empty", "No timezone found");
        await page.Keyboard.PressAsync("Escape");
        await page.Locator("label:has(input[name=lmxSignupIdentity][value=athlete])").ClickAsync();
        await page.Locator("#lmxSignupAthlete").FillAsync("zzzzemptyreview");
        await AssertHint(".lmx-athlete-empty", "No listed athlete found");
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await page.EvaluateAsync("font => document.documentElement.style.fontSize = font + 'px'", font);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-empty-state strong")).ToHaveTextAsync("You're in.");
        await Assertions.Expect(page.Locator(".lmx-empty-state > svg")).ToHaveCountAsync(1);
        Assert.True(await page.Locator(".lmx-empty-state").EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"));
        state = JsonSerializer.SerializeToNode(BuildParticipantState(
            includeMentionParticipants: true, includeDiscussionIdentityParticipants: true))!.AsObject();
        await page.ReloadAsync();
        await page.EvaluateAsync("font => document.documentElement.style.fontSize = font + 'px'", font);
        await page.Locator("#lmxCheckinDialogClose").ClickAsync();
        var textarea = page.Locator("textarea[data-mention-input]");
        await textarea.FillAsync("@zzzzemptyreview");
        await AssertHint(".lmx-mention-empty", "No participant found");
        await textarea.FillAsync("@Ari Able @Bea Builder @Cam @Dee @Eli @");
        await AssertHint(".lmx-mention-empty", "Five-participant mention limit reached");
        await textarea.PressAsync("Escape");
        await Assertions.Expect(page.Locator(".lmx-mention-options")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task EmptyDiscussion_DecoratesOnceAndLeavesThePageResponsive(int width)
    {
        await using var context = await Browser.NewContextAsync(new() {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 }
        });
        await RouteChallengeResourcesAsync(context);
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(noEligibleDays: true))!.AsObject();
        state["notes"] = new JsonArray();
        state["public"]!["notes"] = new JsonArray();
        await context.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state["public"]!.ToJsonString()));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/longevitymaxxing?token=browser-token");
        await Assertions.Expect(page.Locator("#lmxNotes [data-lwc-empty=discussion] > svg")).ToHaveCountAsync(1);
        await page.EvaluateAsync("""
            () => {
                window.emptyMutations = 0;
                new MutationObserver(records => window.emptyMutations += records.length)
                    .observe(document.querySelector('#lmxNotes'), {subtree:true, childList:true, attributes:true});
            }
            """);
        await page.WaitForTimeoutAsync(300);
        Assert.Equal(0, await page.EvaluateAsync<int>("window.emptyMutations"));
        await Assertions.Expect(page.Locator("#lmxNotes strong")).ToHaveTextAsync("No discussion yet.");
        await Assertions.Expect(page.Locator(".lmx-empty-state")).ToBeHiddenAsync();
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-empty-state")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".lmx-empty-state strong")).ToHaveTextAsync("Nothing due.");
        Assert.True(await page.Locator(".lmx-empty-state").EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1"));
        Assert.True(await page.EvaluateAsync<bool>("() => !document.documentElement.hasAttribute('data-initial-view')"));
    }
}
