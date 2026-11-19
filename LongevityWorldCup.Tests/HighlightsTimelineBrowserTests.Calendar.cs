using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, "light")]
    [InlineData(390, "dark")]
    public async Task CalendarTracksLocalDaysWithoutRescanningAndKeepsFocusedButtons(int width, string theme)
    {
        // Cross the former 60-week boundary and start a new day at a UTC offset.
        var start = new DateTimeOffset(2026, 11, 2, 1, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 200).Select(i => new
        {
            Id = $"calendar-{i}", Type = 6, Text = $"Story {i}\n\nStory details.",
            OccurredAt = start.AddDays(-3 * i).ToString("O")
        }).ToList();
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            TimezoneId = "America/Los_Angeles", Locale = "en-US",
            ColorScheme = theme == "light" ? ColorScheme.Light : ColorScheme.Dark,
            ReducedMotion = ReducedMotion.Reduce, HasTouch = width < 600
        });
        await context.RouteAsync("**/api/events", r => r.FulfillAsync(new()
        {
            ContentType = "application/json", Body = JsonSerializer.Serialize(events)
        }));
        var athletes = "[]";
        await context.RouteAsync("**/api/data/athletes", r => r.FulfillAsync(new() { ContentType = "application/json", Body = athletes }));
        var page = await context.NewPageAsync();
        await page.GotoAsync(new Uri(App.BaseAddress, "/events").ToString());
        var calendar = page.GetByRole(AriaRole.Navigation, new() { Name = "Highlights by day" });
        await Assertions.Expect(calendar.GetByRole(AriaRole.Button)).ToHaveCountAsync(200);
        var latest = calendar.GetByRole(AriaRole.Button, new() { Name = "Sunday, November 1, 2026 · 1 highlight", Exact = true });
        // Jump before idle loading has attached this chapter or its ResizeObserver
        // has measured the month band. Larger text makes the default height inaccurate.
        await page.AddStyleTagAsync(new() { Content = "html { font-size:125% !important; }" });
        var pendingRow = page.Locator("tr.main-row[data-event-id='calendar-145']");
        Assert.Equal(0, await pendingRow.CountAsync());
        var pendingDate = start.AddDays(-3 * 145).AddHours(-8);
        var pendingDay = calendar.Locator($"button[data-day='{pendingDate.Year}-{pendingDate.Month}-{pendingDate.Day}']");
        await pendingDay.EvaluateAsync("""
            button => {
                const count = () => document.querySelectorAll('#eventsTable tr.main-row').length;
                let before;
                button.addEventListener('click', () => { before = count(); }, {once:true});
                button.closest('.event-pulse').addEventListener('click', () => {
                    window.rowsAddedDuringCalendarClick = count() - before;
                }, {once:true});
            }
            """);
        await pendingDay.PressAsync("Enter");
        await Assertions.Expect(pendingDay).ToHaveAttributeAsync("aria-current", "date");
        Assert.InRange(await page.EvaluateAsync<int>("window.rowsAddedDuringCalendarClick"), 0, 40);
        Assert.InRange(await pendingRow.EvaluateAsync<double>("""
            row => row.getBoundingClientRect().top - row.parentElement.querySelector('.events-month-row').getBoundingClientRect().bottom
            """), -1, 2);

        await latest.FocusAsync();
        await latest.PressAsync("End");
        var oldest = calendar.Locator("button:focus");
        var target = await oldest.GetAttributeAsync("data-day");
        await oldest.PressAsync("Enter");
        var row = page.Locator("tr.main-row[data-event-id='calendar-199']");
        await Assertions.Expect(row).ToBeInViewportAsync();
        Assert.Equal(target, await row.GetAttributeAsync("data-day"));

        // A day in the middle can align its first row exactly below the pinned band.
        var middle = calendar.Locator("button[data-day='2026-9-2']");
        await middle.FocusAsync();
        await middle.PressAsync("Space");
        await Assertions.Expect(middle).ToHaveAttributeAsync("aria-current", "date");
        var offset = await page.EvaluateAsync<double>("""
            () => document.querySelector('tr.main-row.is-current-day').getBoundingClientRect().top
                - document.querySelector('.events-month-row.is-pinned').getBoundingClientRect().bottom
            """);
        Assert.InRange(offset, -1, 2);

        await middle.EvaluateAsync("el => window.retainedDayButton = el");
        events.Insert(0, new { Id = "new-calendar-day", Type = 6, Text = "New story\n\nDetails.", OccurredAt = start.AddDays(7).ToString("O") });
        await page.EvaluateAsync("() => loadEventsTable(null, false, null, true, {live:true})");
        await Assertions.Expect(calendar.GetByRole(AriaRole.Button)).ToHaveCountAsync(201);
        Assert.True(await middle.EvaluateAsync<bool>("el => el === window.retainedDayButton && document.activeElement === el"));

        // A nested live edit and a scroll deep in history must not enumerate the
        // timeline. Count geometry calls across the shared reading tracker too.
        athletes = """[{"Key":"outside-feed","Name":"Unrelated athlete","CurrentPlacement":500}]""";
        var counts = await page.EvaluateAsync<int[]>("""
            async () => {
                const row = document.querySelector('tr.main-row[data-event-id="calendar-160"]');
                const y = scrollY + row.getBoundingClientRect().top - 100;
                let scans = 0, reads = 0;
                for (const proto of [Document.prototype, Element.prototype]) {
                    const original = proto.querySelectorAll;
                    proto.querySelectorAll = function(selector) {
                        if (selector.includes('main-row')) scans++;
                        return original.call(this, selector);
                    };
                }
                const getRect = Element.prototype.getBoundingClientRect;
                Element.prototype.getBoundingClientRect = function() {
                    if (this.matches('tr.main-row, tr.details-row')) reads++;
                    return getRect.call(this);
                };
                await loadEventsTable(null, false, null, true, {live:true});
                row.querySelector('td').append(document.createTextNode(' '));
                // Intentional wait: cross the original 120ms mutation debounce to detect a delayed whole-feed scan.
                await new Promise(resolve => setTimeout(resolve, 180));
                scrollTo({top:y, behavior:'instant'});
                await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
                return [scans, reads];
            }
            """);
        Assert.Equal(0, counts[0]);
        Assert.InRange(counts[1], 1, 16);
    }
}
