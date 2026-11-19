using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, "en-US", "America/Los_Angeles")]
    [InlineData(390, "ar-EG", "Asia/Singapore")]
    public async Task ReadingDay_FollowsLocalDatesAndExpandedStoriesWithReducedMotion(int width, string locale, string zone)
    {
        var events = Enumerable.Range(0, 80).Select(i => new
        {
            Id = "reading-" + i, Type = 6,
            Text = $"Announcement {i}\n\n" + (i == 12 ? string.Join("\n\n", Enumerable.Repeat("A long story keeps its own day while the reader scrolls through its paragraphs.", 35)) : "Short details."),
            OccurredAt = new DateTime(2026, 11, 2, 1, 0, 0, DateTimeKind.Utc).AddDays(-i).ToString("O")
        });
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, Locale = locale, TimezoneId = zone,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(events) }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(80);
        await page.Locator("[data-event-id='reading-12'] .custom-event-expander").ClickAsync();
        await page.EvaluateAsync("""
            () => {
                const row = document.querySelector('tr.main-row[data-event-id="reading-12"]');
                scrollTo({ top: scrollY + row.nextElementSibling.getBoundingClientRect().top + 200, behavior: 'instant' });
            }
            """);
        await AssertReadingDayAsync(page, "reading-12");

        // A retained refresh and a new month loaded in a later chunk keep the
        // same indicator, including the reader's localized digits.
        await page.EvaluateAsync("() => loadEventsTable(null, false, null, true, { live: true })");
        await AssertReadingDayAsync(page, "reading-12");
        await page.EvaluateAsync("""
            () => {
                const row = document.querySelector('tr.main-row[data-event-id="reading-61"]');
                const band = row.parentElement.querySelector('.events-month-row');
                scrollTo({ top: scrollY + row.getBoundingClientRect().top - parseFloat(getComputedStyle(band).top) - band.getBoundingClientRect().height + 2, behavior: 'instant' });
            }
            """);
        await AssertReadingDayAsync(page, "reading-61");
        await page.EvaluateAsync("() => scrollTo({ top: 0, behavior: 'instant' })");
        await Assertions.Expect(page.Locator(".is-current-day")).ToHaveCountAsync(0);
        Assert.Empty(await page.Locator("#eventsTable").EvaluateAsync<string[]>("table => table.getAnimations({subtree:true}).filter(a => a.playState === 'running').map(a => a.animationName || 'script')"));
    }

    private static async Task AssertReadingDayAsync(IPage page, string id)
    {
        await Assertions.Expect(page.Locator("tr.main-row.is-current-day")).ToHaveAttributeAsync("data-event-id", id);
        var row = page.Locator($"tr.main-row[data-event-id='{id}']");
        var day = await row.Locator(".event-date-day").TextContentAsync();
        var chip = page.Locator(".events-month-row.is-pinned .events-day-chip");
        await Assertions.Expect(chip).ToHaveTextAsync(day!);
        await Assertions.Expect(chip).ToHaveAttributeAsync("aria-hidden", "true");
        Assert.True(await chip.EvaluateAsync<bool>("el => el.scrollWidth <= el.clientWidth && el.scrollHeight <= el.clientHeight"));
    }

    [Fact]
    public async Task FreshHighlights_RejectFutureDatesExpireAndStopPulsing()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """
                [{"Id":"future","Type":1,"Text":"slug[timeline-a]","OccurredAt":"2026-11-03T12:00:00Z"},
                 {"Id":"fresh","Type":1,"Text":"slug[timeline-b]","OccurredAt":"2026-11-02T11:00:00Z"},
                 {"Id":"boundary","Type":1,"Text":"slug[timeline-c]","OccurredAt":"2026-11-01T12:00:00Z"}]
                """
        }));
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, "/events");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("tr.is-fresh")).ToHaveAttributeAsync("data-event-id", "fresh");
        var ring = page.Locator("tr.is-fresh .event-avatar-frame");
        var timing = await ring.EvaluateAsync<double[]>("el => { const s=getComputedStyle(el,'::before'); return [parseFloat(s.animationDuration),Number(s.animationIterationCount)]; }");
        Assert.True(timing[0] * timing[1] <= 5, $"Fresh ring runs {timing[0]}s × {timing[1]} times.");
        await page.WaitForTimeoutAsync(5200);
        Assert.Empty(await ring.EvaluateAsync<string[]>("el => el.getAnimations({subtree:true}).filter(a=>a.playState==='running').map(a=>a.animationName)"));
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 11, 3, 11, 0, 0, DateTimeKind.Utc));
        await page.EvaluateAsync("() => loadEventsTable(null, false, null, true, { live: true })");
        await Assertions.Expect(page.Locator("tr.is-fresh")).ToHaveCountAsync(0);
    }
}
