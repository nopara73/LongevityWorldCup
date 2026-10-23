using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(390, "light")]
    [InlineData(390, "dark")]
    [InlineData(320, "light")]
    [InlineData(320, "dark")]
    public async Task PhonePortraits_AreCenteredInFullNonOverlappingTargets(int width, string scheme)
    {
        await using var context = await OpenAsync(width, 844, scheme, Events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                let previousTarget;
                for (const row of document.querySelectorAll('tr.main-row')) {
                    const target = row.querySelector('.avatar-link, .avatar-disabled');
                    if (!target) continue;
                    const hit = target.getBoundingClientRect();
                    const portrait = target.querySelector('.avatar, .avatar-fallback').getBoundingClientRect();
                    const message = row.querySelector('.event-message-cell').getBoundingClientRect();
                    if (hit.width < 44 || hit.height < 44) issues.push(`${row.dataset.eventId}: target is ${hit.width} x ${hit.height}`);
                    if (Math.abs((hit.top + hit.bottom - portrait.top - portrait.bottom) / 2) > 1)
                        issues.push(`${row.dataset.eventId}: portrait is not centered in its target`);
                    if (message.height <= 44 && Math.abs((hit.top + hit.bottom - message.top - message.bottom) / 2) > 1)
                        issues.push(`${row.dataset.eventId}: short message is not centered beside its portrait`);
                    if (previousTarget && hit.top < previousTarget.bottom)
                        issues.push(`${row.dataset.eventId}: portrait targets overlap`);
                    previousTarget = hit;
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme}: {string.Join("; ", problems)}");
    }

    [Theory]
    [InlineData(390, "light")]
    [InlineData(390, "dark")]
    [InlineData(320, "light")]
    [InlineData(320, "dark")]
    public async Task AnnouncementTouch_DoesNotKeepAFilledHighlightAndKeyboardFocusStaysVisible(int width, string scheme)
    {
        await using var context = await OpenAsync(width, 844, scheme, Events);
        var page = context.Pages[0];
        var button = page.Locator("tr.main-row[data-event-id='sep-a'] .custom-event-expander");
        await button.TapAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
        var touch = await button.EvaluateAsync<bool>(
            """
            button => {
                const style = getComputedStyle(button), box = button.getBoundingClientRect();
                return document.activeElement === button && !button.matches(':focus-visible')
                    && style.backgroundColor === 'rgba(0, 0, 0, 0)' && style.boxShadow === 'none'
                    && box.width >= 44 && box.height >= 44;
            }
            """);
        Assert.True(touch, "A touch should retain its full target without a filled hover/focus highlight.");

        await button.TapAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await page.Keyboard.PressAsync("Shift+Tab");
        await page.Keyboard.PressAsync("Tab");
        Assert.True(await button.EvaluateAsync<bool>(
            """
            button => {
                const style = getComputedStyle(button);
                return document.activeElement === button && button.matches(':focus-visible')
                    && style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) >= 2;
            }
            """), "Tab must restore a visible keyboard focus outline.");
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
    }
}
