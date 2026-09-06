using System.Text.Json;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeZonePicker_TabLeavesInOneStepWithoutChangingTheValue(bool profile)
    {
        await using var context = await NewTimeZoneContextAsync();
        var (page, id) = await PrepareTimeZonePickerAsync(context, profile);
        var button = page.Locator("#" + id + "Button");
        var search = page.Locator("#" + id + "Search");
        var original = await page.Locator("#" + id).InputValueAsync();
        await button.ClickAsync();
        await Assertions.Expect(search).ToBeFocusedAsync();
        await search.PressAsync("ArrowDown");
        await search.PressAsync("Tab");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        var next = page.Locator(profile ? "#lmxProfilePictureButton" : "#lmxSignupForm button[type=submit]");
        await Assertions.Expect(next).ToBeFocusedAsync();
        Assert.Equal(original, await page.Locator("#" + id).InputValueAsync());

        await button.PressAsync("Enter");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await search.PressAsync("Shift+Tab");
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal(original, await page.Locator("#" + id).InputValueAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeZonePicker_EscapeWorksFromEveryPartAndPointerCanToggleItClosed(bool profile)
    {
        await using var context = await NewTimeZoneContextAsync();
        var (page, id) = await PrepareTimeZonePickerAsync(context, profile);
        var button = page.Locator("#" + id + "Button");
        var original = await page.Locator("#" + id).InputValueAsync();
        foreach (var target in new[] { "Search", "option", "Button" })
        {
            await button.ClickAsync();
            var search = page.Locator("#" + id + "Search");
            await Assertions.Expect(search).ToBeFocusedAsync();
            await search.FillAsync("London");
            var focused = target == "option"
                ? page.Locator("#" + id + "List [role=option]").First
                : page.Locator("#" + id + target);
            await focused.FocusAsync();
            await focused.PressAsync("Escape");
            await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
            await Assertions.Expect(button).ToBeFocusedAsync();
            Assert.Equal(original, await page.Locator("#" + id).InputValueAsync());
        }

        await button.ClickAsync();
        await Assertions.Expect(page.Locator("#" + id + "Search")).ToBeFocusedAsync();
        await button.ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeZonePicker_AnnouncesKeyboardSelectionAndAcceptsItWithoutSubmitting(bool profile)
    {
        await using var context = await NewTimeZoneContextAsync();
        var (page, id) = await PrepareTimeZonePickerAsync(context, profile);
        var button = page.Locator("#" + id + "Button");
        var search = page.Locator("#" + id + "Search");
        var original = await page.Locator("#" + id).InputValueAsync();
        await button.PressAsync("ArrowDown");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await Assertions.Expect(search).ToHaveAttributeAsync("role", "combobox");
        await Assertions.Expect(search).ToHaveAccessibleNameAsync("Search city or timezone");
        await Assertions.Expect(search).ToHaveAttributeAsync("aria-controls", id + "List");
        await search.FillAsync("United States");
        for (var i = 0; i < 12; i++) await search.PressAsync("ArrowDown");
        var activeId = await search.GetAttributeAsync("aria-activedescendant");
        Assert.False(string.IsNullOrEmpty(activeId));
        var active = page.Locator("#" + activeId);
        await Assertions.Expect(active).ToHaveAttributeAsync("aria-selected", "true");
        Assert.Equal(1, await page.Locator("#" + id + "List [aria-selected=true]").CountAsync());
        await Assertions.Expect(search).ToBeFocusedAsync();
        Assert.Equal(original, await page.Locator("#" + id).InputValueAsync());
        var selectedZone = await active.GetAttributeAsync("data-time-zone");
        var activeBounds = await active.BoundingBoxAsync();
        var listBounds = await page.Locator("#" + id + "List").BoundingBoxAsync();
        Assert.NotNull(activeBounds);
        Assert.NotNull(listBounds);
        Assert.True(activeBounds.Y >= listBounds.Y && activeBounds.Y + activeBounds.Height <= listBounds.Y + listBounds.Height + 1);
        await search.PressAsync("Enter");
        Assert.Equal(selectedZone, await page.Locator("#" + id).InputValueAsync());
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__timezoneSubmits"));

        await button.ClickAsync();
        await search.FillAsync("NoSuchTimezone123");
        Assert.Equal(0, await page.Locator("#" + id + "List [role=option]").CountAsync());
        Assert.Null(await search.GetAttributeAsync("aria-activedescendant"));
        await search.PressAsync("ArrowDown");
        await search.PressAsync("ArrowUp");
        await search.PressAsync("Enter");
        Assert.Equal(selectedZone, await page.Locator("#" + id).InputValueAsync());
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__timezoneSubmits"));
        await search.FillAsync("London");
        await search.PressAsync("Enter");
        Assert.Equal("Europe/London", await page.Locator("#" + id).InputValueAsync());
    }

    [Theory]
    [InlineData(320, false)]
    [InlineData(1280, true)]
    public async Task TimeZonePicker_ExpansionKeepsTheNextControlClearAndAcceptsPointerSelection(int width, bool dark)
    {
        await using var context = await NewTimeZoneContextAsync(width, dark, touch: width == 320);
        var (page, id) = await PrepareTimeZonePickerAsync(context, profile: false);
        await page.Locator("#" + id + "Button").ClickAsync();
        var search = page.Locator("#" + id + "Search");
        await Assertions.Expect(search).ToBeFocusedAsync();
        await search.FillAsync("London");
        var popup = page.Locator("[data-select-id='" + id + "'] .lmx-timezone-popover");
        var popupBounds = await popup.BoundingBoxAsync();
        var nextBounds = await page.Locator("#lmxSignupForm button[type=submit]").BoundingBoxAsync();
        Assert.NotNull(popupBounds);
        Assert.NotNull(nextBounds);
        Assert.True(popupBounds.Y + popupBounds.Height <= nextBounds.Y, "Timezone choices cover Sign up.");
        if (width == 320) await page.SetViewportSizeAsync(width, 440);
        await search.FillAsync("");
        await search.PressAsync("ArrowUp");
        var active = page.Locator("#" + await search.GetAttributeAsync("aria-activedescendant"));
        await Assertions.Expect(active).ToBeInViewportAsync();
        await search.FillAsync("London");
        var option = page.Locator("#" + id + "List [data-time-zone='Europe/London']");
        if (width == 320) await option.TapAsync();
        else await option.ClickAsync();
        Assert.Equal("Europe/London", await page.Locator("#" + id).InputValueAsync());
        await Assertions.Expect(page.Locator("#" + id + "Button")).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__timezoneSubmits"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimeZonePicker_PointerSelectionSurvivesBlurWithoutANewFocusTarget(bool profile)
    {
        await using var context = await NewTimeZoneContextAsync();
        var (page, id) = await PrepareTimeZonePickerAsync(context, profile);
        var button = page.Locator("#" + id + "Button");
        var search = page.Locator("#" + id + "Search");
        await button.ClickAsync();
        await Assertions.Expect(search).ToBeFocusedAsync();
        await search.FillAsync("London");
        var option = page.Locator("#" + id + "List [data-time-zone='Europe/London']");
        await option.EvaluateAsync("""
            option => {
                const search = option.closest('[data-timezone-picker]').querySelector('input');
                window.__timezoneNullBlurs = 0;
                search.addEventListener('focusout', event => {
                    if (event.relatedTarget === null) window.__timezoneNullBlurs++;
                });
                // Reproduce a pointer blur without a focusable destination, as on WebKit.
                option.addEventListener('mousedown', () => search.blur(), { once: true });
            }
            """);
        await option.ClickAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("window.__timezoneNullBlurs"));
        Assert.Equal("Europe/London", await page.Locator("#" + id).InputValueAsync());
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(button).ToBeFocusedAsync();
        Assert.Equal(0, await page.EvaluateAsync<int>("window.__timezoneSubmits"));

        await button.ClickAsync();
        await Assertions.Expect(search).ToBeFocusedAsync();
        await page.Locator("h1").ClickAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal("Europe/London", await page.Locator("#" + id).InputValueAsync());
    }

    private Task<IBrowserContext> NewTimeZoneContextAsync(int width = 390, bool dark = false, bool touch = false)
        => Browser.NewContextAsync(new()
        {
            BaseURL = App.BaseAddress.ToString(), ViewportSize = new() { Width = width, Height = 844 },
            TimezoneId = "Asia/Bangkok", ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light, HasTouch = touch, IsMobile = touch
        });

    private static async Task<(IPage Page, string Id)> PrepareTimeZonePickerAsync(IBrowserContext context, bool profile)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        if (profile) await context.AddInitScriptAsync("localStorage.setItem('lmxAccessToken','browser-token')");
        var page = await context.NewPageAsync();
        await page.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, JsonSerializer.Serialize(BuildPublicState())));
        var participantState = JsonSerializer.SerializeToNode(BuildParticipantState(timeZoneId: "Asia/Bangkok"))!;
        foreach (var day in participantState["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("");
        await page.RouteAsync("**/api/longevitymaxxing/participant", route => FulfillJsonAsync(route, participantState.ToJsonString()));
        await page.GotoAsync("/longevitymaxxing", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        if (profile) await page.Locator("#lmxProfileTab").ClickAsync();
        else
        {
            await page.Locator("#lmxSignupEmail").FillAsync("timezone@example.test");
            await page.Locator("#lmxSignupName").FillAsync("Timezone Tester");
        }
        var id = profile ? "lmxEditTimeZone" : "lmxSignupTimeZone";
        await Assertions.Expect(page.Locator("[data-select-id='" + id + "']")).ToHaveAttributeAsync("data-wired", "true");
        await page.Locator(profile ? "#lmxEditForm" : "#lmxSignupForm").EvaluateAsync("""
            form => {
                window.__timezoneSubmits = 0;
                form.addEventListener('submit', event => {
                    event.preventDefault(); event.stopImmediatePropagation(); window.__timezoneSubmits++;
                }, true);
            }
            """);
        return (page, id);
    }
}
