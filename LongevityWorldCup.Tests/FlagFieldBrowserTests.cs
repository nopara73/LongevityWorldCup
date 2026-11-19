using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.AutocompleteInteractionBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class FlagFieldBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("/apply", "flag", 1280)]
    [InlineData("/edit-profile", "flagDisplayInput", 390)]
    public async Task FlagsWithArtwork_AppearInSuggestionsAndTheSelectedSlot(string path, string id, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        using var client = App.CreateClient();
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        foreach (var (query, flag, code, artwork) in new[]
        {
            ("Liber", "Liberland", "liberland", "/assets/flags/liberland.webp"),
            ("Belgi", "Belgium", "be", "/vendor/flag-icons/flags/4x3/be.svg")
        })
        {
            await input.FillAsync(query);
            var tile = page.Locator($"#{id}-autocomplete-list [role=option] .fi-{code}");
            await Assertions.Expect(tile).ToBeVisibleAsync();
            await Assertions.Expect(tile).ToHaveCSSAsync("background-image", new Regex(Regex.Escape(artwork)));
            using var response = await client.GetAsync(artwork);
            response.EnsureSuccessStatusCode();
            Assert.StartsWith("image/", response.Content.Headers.ContentType?.MediaType);

            await input.PressAsync("ArrowDown");
            await input.PressAsync("Enter");
            await Assertions.Expect(input).ToHaveValueAsync(flag);
            await Assertions.Expect(page.Locator($".lwc-flag-slot .fi-{code}")).ToBeVisibleAsync();
        }
    }

    [Theory]
    [InlineData("/apply", "flag")]
    [InlineData("/edit-profile", "flagDisplayInput")]
    public async Task RapidReopen_KeepsTheActiveDescendantInTheLiveList(string path, string id)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        await input.FillAsync("United");
        // All three keys occur before the closing animation can finish.
        var state = await input.EvaluateAsync<bool[]>("""
            input => {
                const press = key => input.dispatchEvent(new KeyboardEvent('keydown', {key, bubbles:true, cancelable:true}));
                press('ArrowDown'); press('Escape'); press('ArrowDown');
                const id = input.getAttribute('aria-activedescendant');
                const active = document.getElementById(id);
                const live = document.getElementById(input.getAttribute('aria-controls'));
                return [document.querySelectorAll('#' + id).length === 1,
                        live.contains(active), !active.closest('[aria-hidden=true]'),
                        active.getAttribute('aria-selected') === 'true'];
            }
            """);
        Assert.All(state, Assert.True);
        await input.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync("United Kingdom");
        await Assertions.Expect(input).ToBeFocusedAsync();
        await Assertions.Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator(".lwc-flag-flight, .lwc-flag-suggestions--leaving")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("/apply", "flag")]
    [InlineData("/edit-profile", "flagDisplayInput")]
    public async Task RestoredValues_UpdateTheSlotWithoutRewritingTheFlag(string path, string id)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await PrepareAsync(context, path);
        var input = page.Locator("#" + id);
        var slot = page.Locator(".lwc-flag-slot");
        const string customFlag = "My Custom Team";
        foreach (var trigger in new[] { "assignment", "change", "pageshow" })
        {
            await input.FillAsync(customFlag);
            await input.EvaluateAsync("""
                (input, trigger) => {
                    if (trigger === 'assignment') input.value = 'Japan';
                    else {
                        Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, 'Japan');
                        (trigger === 'change' ? input : window).dispatchEvent(new Event(trigger));
                    }
                }
                """, trigger);
            await Assertions.Expect(input).ToHaveValueAsync("Japan");
            await Assertions.Expect(slot.Locator(".fi-jp")).ToHaveCountAsync(1);
        }
        await input.EvaluateAsync("input => input.value = 'USA'");
        await Assertions.Expect(slot.Locator(".fi-us")).ToHaveCountAsync(1);
        await Assertions.Expect(input).ToHaveValueAsync("USA");
        await input.EvaluateAsync("(input, value) => input.value = value", customFlag);
        await Assertions.Expect(slot.Locator(".lwc-flag-tile--mono")).ToHaveTextAsync("M");
        if (path == "/edit-profile")
        {
            await input.DispatchEventAsync("input");
            await page.Locator("#restoreFlagBtn").ClickAsync();
            await Assertions.Expect(input).ToHaveValueAsync("Hungary");
            await Assertions.Expect(slot.Locator(".fi-hu")).ToHaveCountAsync(1);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisabledMotion_KeepsKeyboardSelectionStillAndVisible(bool siteSetting)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        { ReducedMotion = siteSetting ? ReducedMotion.NoPreference : ReducedMotion.Reduce });
        var page = await PrepareAsync(context, "/apply");
        var input = page.Locator("#flag");
        await input.FillAsync("United");
        // Consume the site's first-interaction motion gate before explicitly disabling it.
        await input.PressAsync("Shift");
        if (siteSetting) await page.EvaluateAsync("document.documentElement.dataset.lwcMotion = 'off'");
        await input.PressAsync("ArrowDown");
        var still = await page.EvaluateAsync<bool>("""
            () => {
                const row = document.querySelector('#flag-autocomplete-list [aria-selected=true]');
                return ['.lwc-flag-glider', '.lwc-flag-tile', '.lwc-flag-share'].every(selector =>
                    [...document.querySelectorAll('#flag-autocomplete-list ' + selector)]
                        .every(el => getComputedStyle(el).transitionDuration.split(',').every(t => parseFloat(t) === 0)))
                    && getComputedStyle(row.querySelector('.lwc-flag-tile')).transform === 'none';
            }
            """);
        Assert.True(still);
        await input.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync("United Kingdom");
        Assert.Equal(0, await page.Locator(".lwc-flag-flight, .lwc-flag-suggestions--leaving").CountAsync());
    }
}
