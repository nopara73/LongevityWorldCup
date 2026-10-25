using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Theory]
    [InlineData(844, 390, 16, false)]
    [InlineData(844, 390, 16, true)]
    [InlineData(711, 402, 16, false)]
    [InlineData(711, 402, 16, true)]
    [InlineData(320, 640, 32, false)]
    [InlineData(320, 640, 32, true)]
    [InlineData(390, 844, 32, false)]
    [InlineData(390, 844, 32, true)]
    public async Task ProfileShelf_UsesTheProfileScrollerForBadgesAndExplanations(
        int width, int height, int textSize, bool dark)
    {
        await using var context = await NewBadgeContextAsync(width, height, dark);
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, $"/athlete/{Slug}");
        await page.AddStyleTagAsync(new() { Content = $"html {{ font-size: {textSize}px !important; }}" });
        var shelf = page.Locator("#modalBadgeStrip");
        await page.WaitForFunctionAsync("""
            () => document.querySelectorAll('#modalBadgeStrip .modal-badge-item').length >= 7
                && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')
            """);
        var toggle = shelf.Locator(".modal-badge-toggle").First;
        await toggle.ClickAsync();
        var panel = shelf.Locator(".modal-badge-explainer");
        await Assertions.Expect(panel).ToBeVisibleAsync();
        foreach (var element in new[] { shelf, panel })
        {
            var dimensions = await element.EvaluateAsync<int[]>(
                "e => [e.scrollWidth, e.clientWidth, e.scrollHeight, e.clientHeight]");
            Assert.True(dimensions[0] <= dimensions[1] + 1, $"Horizontal overflow: {string.Join(", ", dimensions)}");
            Assert.True(dimensions[2] <= dimensions[3] + 1, $"Unnecessary nested scrollbar: {string.Join(", ", dimensions)}");
        }

        // The profile remains the single scroll area even when its shelf is
        // taller than the viewport. Keyboard users can reach its final badge.
        var last = shelf.Locator(".modal-badge-toggle").Last;
        await last.FocusAsync();
        await Assertions.Expect(last).ToBeInViewportAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(last).ToHaveAttributeAsync("aria-expanded", "true");
        await panel.ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(panel).ToBeInViewportAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(panel).ToHaveCountAsync(0);
        await Assertions.Expect(last).ToBeFocusedAsync();
    }
}
