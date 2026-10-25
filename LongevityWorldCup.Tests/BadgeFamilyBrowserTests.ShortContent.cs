using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Theory]
    [InlineData(720, 402, false)]
    [InlineData(720, 402, true)]
    [InlineData(844, 390, false)]
    [InlineData(844, 390, true)]
    public async Task BadgeShelf_UsesTheProfileScrollerOnShortScreens(int width, int height, bool dark)
    {
        await using var context = await NewBadgeContextAsync(width, height, dark);
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        await page.Locator("#modalBadgeStrip .modal-badge-toggle").First.ClickAsync();
        var shelf = page.Locator("#modalBadgeStrip");
        Assert.True(await shelf.EvaluateAsync<bool>("el => el.scrollHeight === el.clientHeight && el.scrollWidth === el.clientWidth"),
            "A short badge shelf must grow with its explanation instead of introducing a second scrollbar.");
        var last = shelf.Locator(".modal-badge-toggle").Last;
        await last.FocusAsync();
        await last.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(last).ToBeInViewportAsync();
        await Assertions.Expect(page.Locator("#stickyCloseBtn")).ToBeInViewportAsync();
        await page.Locator("#stickyCloseBtn").ClickAsync();
        await Assertions.Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(1440, 900, false, 100)]
    [InlineData(1440, 900, true, 200)]
    [InlineData(390, 844, false, 100)]
    [InlineData(390, 844, true, 200)]
    public async Task ShortExplanation_FitsWithoutAnInternalScrollbar(int width, int height, bool dark, int textPercent)
    {
        await using var context = await NewBadgeContextAsync(width, height, dark);
        if (width < 1000)
        {
            // Compact leaderboards intentionally hide row badges. The real
            // mobile explanation lives on the profile shelf.
            var profile = await context.NewPageAsync();
            await AestheticSystemBrowserTests.NavigateAndSettleAsync(profile, $"/athlete/{Slug}");
            await profile.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
            var toggle = profile.Locator("#modalBadgeStrip .modal-badge-toggle").Filter(new() { HasText = "The Regular" });
            await toggle.ClickAsync();
            var panel = profile.Locator(".modal-badge-explainer");
            await Assertions.Expect(panel).ToBeVisibleAsync();
            Assert.True(await panel.EvaluateAsync<bool>("el => el.scrollHeight === el.clientHeight && el.scrollWidth === el.clientWidth"));
            await toggle.ClickAsync();
            await Assertions.Expect(panel).ToHaveCountAsync(0);
            return;
        }
        var page = await OpenLeaderboardAsync(context);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        var badge = MichaelRow(page).Locator(".badge-section .badge-explained:visible").First;
        await badge.ScrollIntoViewIfNeededAsync();
        await badge.FocusAsync();
        var tip = page.Locator(".badge-tip");
        await Assertions.Expect(tip).ToBeVisibleAsync();
        var sizes = await tip.EvaluateAsync<double[]>("""
            tip => {
                const content = tip.querySelector('.badge-tip-content');
                return [content.scrollHeight, content.clientHeight, content.scrollWidth, content.clientWidth];
            }
            """);
        Assert.Equal(sizes[0], sizes[1]);
        Assert.Equal(sizes[2], sizes[3]);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(tip).ToBeHiddenAsync();
        await Assertions.Expect(badge).ToBeFocusedAsync();
    }
}
