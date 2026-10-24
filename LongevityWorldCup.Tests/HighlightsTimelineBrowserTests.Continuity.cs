using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440)]
    [InlineData(390)]
    public async Task DirectAnnouncementLink_KeepsTheOpenedStoryInViewAfterOpeningTimers(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, ReducedMotion = ReducedMotion.Reduce
        });
        var events = Enumerable.Range(0, 80).Select(i => new
        {
            Id = "recent-" + i, Type = 0, Text = "Recent timeline milestone " + i, OccurredAt = "2026-10-20T12:00:00Z"
        }).Append(new { Id = "target-story", Type = 6, Text = "Target story\n\nThe requested announcement is here.", OccurredAt = "2026-09-20T12:00:00Z" });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(events) }));
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await GuessMyAgeBrowserTests.PauseClockAsync(page);
        await page.GotoAsync("/events?event=target-story", new() { WaitUntil = WaitUntilState.Load });
        await page.EvaluateAsync("() => document.fonts.ready");
        var story = page.Locator("tr.main-row[data-event-id='target-story'].is-open");
        await story.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.Clock.RunForAsync(1000);
        Assert.True(await story.EvaluateAsync<bool>(
            "row => { const b=row.getBoundingClientRect(), h=document.querySelector('header').getBoundingClientRect(); return b.top>=Math.max(0,h.bottom) && b.bottom<=innerHeight; }"),
            "The default heading scroll moved the opened notification target out of view.");
        await Assertions.Expect(story.Locator(".custom-event-expander")).ToHaveAttributeAsync("aria-expanded", "true");
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Singapore")]
    [InlineData("America/Los_Angeles")]
    public async Task AthleteBursts_KeepTheFullTimelineInLocalCalendarOrder(string zone)
    {
        var midnight = zone switch
        {
            "Asia/Singapore" => new DateTime(2026, 1, 31, 16, 0, 0, DateTimeKind.Utc),
            "America/Los_Angeles" => new DateTime(2026, 2, 1, 8, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var events = new[]
        {
            new { Id = "burst-new", Type = 1, Text = "slug[timeline-a]", OccurredAt = midnight.AddSeconds(10).ToString("O") },
            new { Id = "burst-other", Type = 1, Text = "slug[timeline-b]", OccurredAt = midnight.AddSeconds(-6).ToString("O") },
            new { Id = "burst-old", Type = 2, Text = "slug[timeline-a] rank[3]", OccurredAt = midnight.AddSeconds(-10).ToString("O") }
        };
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = zone, ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(events) }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(3);
        Assert.Equal(["burst-new", "burst-other", "burst-old"], await page.Locator("tr.main-row").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.eventId)"));
        Assert.Equal(["burst-new", "burst-other"], await page.Locator("tr.main-row.month-start").EvaluateAllAsync<string[]>("rows => rows.map(row => row.dataset.eventId)"));
        await Assertions.Expect(page.Locator("tr.main-row[data-event-id='burst-old']")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bday-repeat\b"));
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("Asia/Singapore")]
    [InlineData("America/Los_Angeles")]
    [InlineData("Pacific/Kiritimati")]
    public async Task Grouping_UsesTheDisplayedLocalDayAcrossYearsTiesUnknownDatesAndReorders(string zone)
    {
        var events = new[]
        {
            new { Id = "year-a", Type = 0, Text = "Year boundary A", OccurredAt = "2025-12-31T23:59:59.999Z" },
            new { Id = "year-b", Type = 0, Text = "Year boundary B", OccurredAt = "2025-12-31T23:59:59.999Z" },
            new { Id = "new-year", Type = 6, Text = "New year\n\n[Read the rules](/ruleset)", OccurredAt = "2026-01-01T00:00:00.000Z" },
            new { Id = "month", Type = 0, Text = "Month boundary", OccurredAt = "2026-01-31T16:00:00Z" },
            new { Id = "next-month", Type = 0, Text = "Next month", OccurredAt = "2026-02-01T00:00:00Z" },
            new { Id = "dst-a", Type = 0, Text = "Repeated DST hour A", OccurredAt = "2026-11-01T08:30:00Z" },
            new { Id = "dst-b", Type = 0, Text = "Repeated DST hour B", OccurredAt = "2026-11-01T09:30:00Z" },
            new { Id = "invalid", Type = 0, Text = "Unknown date", OccurredAt = "unknown" },
            new { Id = "missing", Type = 0, Text = "Missing date", OccurredAt = (string)null! }
        };
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }, TimezoneId = zone, Locale = "en-US",
            ReducedMotion = ReducedMotion.Reduce
        });
        var reversed = false;
        await context.RouteAsync("**/api/data/athletes", r => r.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", r => r.FulfillAsync(new()
        {
            ContentType = "application/json", Body = JsonSerializer.Serialize(reversed ? events.Reverse() : events)
        }));
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc));
        await NavigateAndSettleAsync(page, "/events");
        await page.Locator("tr.main-row[data-event-id='year-a']").WaitForAsync();
        var dates = events.ToDictionary(e => e.Id, e => e.OccurredAt);
        for (var pass = 0; pass < 2; pass++)
        {
            var problems = await page.EvaluateAsync<string[]>(
                """
                dates => {
                    const issues = [];
                    let priorDay = '', priorMonth = '', priorTime = Infinity;
                    for (const row of document.querySelectorAll('#eventsTable tr.main-row')) {
                        const raw = dates[row.dataset.eventId];
                        const date = new Date(raw || 'invalid');
                        const valid = Number.isFinite(+date);
                        if (valid && +date > priorTime) issues.push(`${row.dataset.eventId}: events reordered across a date boundary`);
                        if (valid) priorTime = +date;
                        const day = valid ? `${date.getFullYear()}-${date.getMonth()+1}-${date.getDate()}` : '';
                        const month = valid ? `${date.getFullYear()}-${date.getMonth()+1}` : '';
                        const label = valid ? date.toLocaleDateString(undefined, {month:'short',day:'numeric'}).replace(/,/g,'') + (date.getFullYear() === new Date().getFullYear() ? '' : `, ${date.getFullYear()}`) : '';
                        if ((row.dataset.day || '') !== day) issues.push(`${row.dataset.eventId}: shifted local day`);
                        if (row.querySelector('.col-date').textContent !== label) issues.push(`${row.dataset.eventId}: shifted visible date`);
                        if (row.classList.contains('day-repeat') !== (!!day && day === priorDay)) issues.push(`${row.dataset.eventId}: repeated day`);
                        if (row.classList.contains('day-start') !== (!day || day !== priorDay)) issues.push(`${row.dataset.eventId}: day start`);
                        if (row.classList.contains('month-start') !== (!month || month !== priorMonth)) issues.push(`${row.dataset.eventId}: month start`);
                        priorDay = day; priorMonth = month;
                    }
                    if (document.querySelector('.details-row:is(.day-start,.day-repeat,.month-start), .details-row[data-day]')) issues.push('details grouped as an event');
                    const headings=[...document.querySelectorAll('.events-month-row')];
                    if(headings.some(h=>/Invalid Date|1970/.test(h.textContent))) issues.push('an unknown date created a fabricated chapter');
                    for(const group of document.querySelectorAll('#eventsTable tbody')){
                        const known=[...group.querySelectorAll('tr.main-row')].filter(r=>r.dataset.day);
                        const months=new Set(known.map(r=>r.dataset.day.split('-').slice(0,2).join('-')));
                        if(months.size>1) issues.push('one accessible row group spans several months');
                        if(group.querySelectorAll('.events-month-row').length!==(known.length?1:0)) issues.push('missing or stale month heading');
                    }
                    return issues;
                }
                """, dates);
            Assert.True(problems.Length == 0, $"{zone}: {string.Join("; ", problems)}");
            reversed = true;
            await ReloadTimelineAsync(page);
        }
    }

    [Theory]
    [InlineData(1440, "light", 100)]
    [InlineData(390, "light", 100)]
    [InlineData(390, "dark", 200)]
    [InlineData(320, "light", 200)]
    [InlineData(768, "dark", 100)]
    public async Task Timeline_DatesKeepContrastRingsSurviveInteractionAndRailEndsAtThePortrait(int width, string scheme, int percent)
    {
        await using var context = await OpenAsync(width, 900, scheme, Events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();
        await page.EvaluateAsync("p => document.documentElement.style.fontSize = `${p}%`", percent);
        await page.EvaluateAsync("() => document.fonts.ready");
        foreach (var id in new[] { "oct-a", "oct-b" })
        {
            var link = page.Locator($"tr.main-row[data-event-id='{id}'] .avatar-link");
            await link.HoverAsync();
            Assert.Contains("0px 0px 0px 4px", await link.Locator(".avatar, .avatar-fallback").EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
            await link.FocusAsync();
            Assert.Contains("0px 0px 0px 4px", await link.Locator(".avatar, .avatar-fallback").EvaluateAsync<string>("e => getComputedStyle(e).boxShadow"));
        }
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [], rows = [...document.querySelectorAll('tr.main-row')];
                const luminance = rgb => rgb.map(v => v/255).map(v => v <= .04045 ? v/12.92 : ((v+.055)/1.055)**2.4).reduce((s,v,i) => s + v*[.2126,.7152,.0722][i],0);
                const color = value => value.match(/[\d.]+/g).map(Number).slice(0,3);
                const bg = color(getComputedStyle(document.querySelector('.events-board')).backgroundColor);
                for (const row of rows) {
                    const date = row.querySelector('.col-date'), style = getComputedStyle(date), opacity = +style.opacity;
                    const fg = color(style.color).map((v,i) => v*opacity + bg[i]*(1-opacity));
                    const a=luminance(fg), b=luminance(bg), contrast=(Math.max(a,b)+.05)/(Math.min(a,b)+.05);
                    if (contrast < 4.5) issues.push(`${row.dataset.eventId}: date contrast ${contrast}`);
                    const range = document.createRange(); range.selectNodeContents(date);
                    const shown = row.classList.contains('day-repeat') ? [] : [...date.querySelectorAll('.event-date-day')];
                    if (shown.some(day => day.getBoundingClientRect().width <= 1) || ![...range.getClientRects()].some(r => r.width > 0 && r.height > 0)) issues.push('date has no visible glyphs');
                }
                if (getComputedStyle(rows.at(-1)).backgroundImage.includes('gradient')) issues.push('a month of one event has a rail');
                const last = rows.filter(row => row.classList.contains('month-end') && !row.classList.contains('month-start')).at(-1), style=getComputedStyle(last), box=last.getBoundingClientRect();
                const portrait=last.querySelector('.avatar,.avatar-fallback,.milestone-people-avatar').getBoundingClientRect();
                const rail = style.backgroundSize.split(',')[0].trim(), length = rail.slice(rail.indexOf(' ') + 1);
                let height = 0;
                for (const term of length.matchAll(/(-?[\d.]+)(%|px)/g)) height += +term[1]*(term[2] === '%' ? box.height/100 : 1);
                if (Math.abs(box.top + height - (portrait.top + portrait.height/2)) > 1.5) issues.push(`rail misses oldest portrait by ${box.top + height - (portrait.top + portrait.height/2)}`);
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme} {percent}%: {string.Join("; ", problems)}");
    }

    [Fact]
    public async Task RefreshAndReorder_PreserveExpandedStoryFocusAndAnExplicitlyCollapsedNotificationTarget()
    {
        await using var context = await OpenAsync(390, 844, "light", Events);
        var page = context.Pages[0];
        var expander = page.Locator("tr.main-row[data-event-id='sep-a'] .custom-event-expander");
        await expander.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await ReloadTimelineAsync(page);
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(expander).ToBeFocusedAsync();

        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "false");
        Assert.Equal(0, await page.Locator("tr.main-row[data-event-id='sep-a'] + tr").EvaluateAsync<double>("e => e.getBoundingClientRect().height"));
        await page.Keyboard.PressAsync("Enter");

        var changedEvents = Events.Replace("The full announcement is here.", "The updated announcement is here. [Read the rules](https://longevityworldcup.com/ruleset). [Rules details](https://longevityworldcup.com/ruleset)")
            .Replace("2026-09-20T12:00:00Z", "2026-11-20T12:00:00Z");
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/api/events", async route =>
        {
            await pending.Task;
            await route.FulfillAsync(new() { ContentType = "application/json", Body = changedEvents });
        });
        try
        {
            await page.EvaluateAsync("() => { loadEventsTable(null, false); }");
            await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "true");
            await Assertions.Expect(expander).ToBeFocusedAsync();
            await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        }
        finally
        {
            pending.TrySetResult();
        }
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Read the rules", Exact = true })).ToBeVisibleAsync();
        var storyLink = page.GetByRole(AriaRole.Link, new() { Name = "Rules details", Exact = true });
        await storyLink.FocusAsync();
        await ReloadTimelineAsync(page);
        await Assertions.Expect(storyLink).ToBeFocusedAsync();
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "true");
        await page.EvaluateAsync("() => { history.replaceState(null, '', '?event=sep-a'); document.querySelector('tr.main-row[data-event-id=\"sep-a\"]')._setCustomEventExpanded(false); }");
        await Assertions.Expect(expander).ToBeFocusedAsync();
        await ReloadTimelineAsync(page);
        await Assertions.Expect(expander).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(expander).ToBeFocusedAsync();
    }

    private static async Task ReloadTimelineAsync(IPage page)
    {
        await page.EvaluateAsync("async () => { window.__timelinePriorRow = document.querySelector('tr.main-row'); await loadEventsTable(null, false); }");
        Assert.True(await page.EvaluateAsync<bool>("() => window.__timelinePriorRow === document.querySelector('tr.main-row') && document.querySelector('#events-root').getAttribute('aria-busy') === 'false'"));
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task FailedFeed_EnlargedRetryStaysInsideTheNoticeAndKeyboardRetryRecovers(ColorScheme scheme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 320, Height = 640 }, ColorScheme = scheme, ReducedMotion = ReducedMotion.Reduce
        });
        var failed = true;
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new()
        {
            Status = failed ? 503 : 200, ContentType = "application/json", Body = failed ? "{}" : Events
        }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        await NavigateAndSettleAsync(page, "/events");
        var retry = page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });
        await retry.WaitForAsync();
        await page.EvaluateAsync("() => document.documentElement.style.fontSize = '200%'");
        var contained = await retry.EvaluateAsync<bool>(
            """
            button => {
                const b=button.getBoundingClientRect(),n=button.closest('.events-recovery').getBoundingClientRect();
                const r=document.createRange();r.selectNodeContents(button);const t=r.getBoundingClientRect();
                return b.width>=44 && b.height>=44 && b.left>=n.left && b.right<=n.right && b.right<=innerWidth && t.left>=b.left && t.right<=b.right;
            }
            """);
        Assert.True(contained, "The enlarged Retry target or its glyphs leave the error notice.");
        failed = false;
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();
        await Assertions.Expect(page.Locator("#events-root")).ToHaveAttributeAsync("aria-busy", "false");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveTextAsync("Events loaded.");
        await Assertions.Expect(page.Locator("#eventsStatus")).ToHaveAttributeAsync("role", "status");
        await page.Locator(".event-board-page .back-button").ClickAsync();
        // History restoration can reuse a ready document without a new DOMContentLoaded.
        await Assertions.Expect(page).ToHaveURLAsync(new Uri(App.BaseAddress, "ruleset").ToString(), new() { Timeout = 30000 });
        await Assertions.Expect(page.Locator("h1#ruleset")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Back_WorksFromTheLoadingFeedBeforeSharedModulesArrive()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/ruleset");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/misc.js*", async route =>
        {
            await release.Task;
            await route.AbortAsync();
        });
        try
        {
            await page.GotoAsync("/events", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await page.Locator(".event-board-page .back-button").FocusAsync();
            await page.Keyboard.PressAsync("Enter");
            await Assertions.Expect(page).ToHaveURLAsync(new Uri(App.BaseAddress, "ruleset").ToString(), new() { Timeout = 30000 });
            await Assertions.Expect(page.Locator("h1#ruleset")).ToBeVisibleAsync();
        }
        finally
        {
            release.TrySetResult();
        }
    }
}
