using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

// The Highlights page is one card: the title band sits on the feed, and the feed
// reads as a timeline grouped by month.
[Collection(BrowserTestCollections.WorkloadB)]
public sealed partial class HighlightsTimelineBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string Athletes = """
        [
          {"Key":"timeline-a","Name":"Ada Timeline","CurrentPlacement":4,"ProfilePic":"/assets/favicon-64x64.png"},
          {"Key":"timeline-b","Name":"Bela Timeline","CurrentPlacement":12},
          {"Key":"timeline-c","Name":"Cleo Timeline","CurrentPlacement":30}
        ]
        """;

    private const string Events = """
        [
          {"Id":"oct-a","Type":1,"Text":"slug[timeline-a]","OccurredAt":"2026-10-16T12:00:00Z","Relevance":10},
          {"Id":"oct-b","Type":1,"Text":"slug[timeline-b]","OccurredAt":"2026-10-16T11:00:00Z","Relevance":10},
          {"Id":"oct-c","Type":3,"Text":"sats[8455]","OccurredAt":"2026-10-09T12:00:00Z","Relevance":10},
          {"Id":"sep-a","Type":6,"Text":"Season update\n\nThe full announcement is here.","OccurredAt":"2026-09-20T12:00:00Z","Relevance":10},
          {"Id":"sep-b","Type":1,"Text":"slug[timeline-c]","OccurredAt":"2026-09-12T12:00:00Z","Relevance":10},
          {"Id":"aug-a","Type":4,"Text":"athletes[250]","OccurredAt":"2026-08-03T12:00:00Z","Relevance":10}
        ]
        """;

    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1024, 768, "dark")]
    [InlineData(768, 1024, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task HighlightsPage_IsOneCardWithAMonthGroupedTimeline(int width, int height, string scheme)
    {
        await using var context = await OpenAsync(width, height, scheme, Events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const heading = document.querySelector('.event-board-heading');
                const board = document.querySelector('.event-board-page .events-board');
                const band = getComputedStyle(heading, '::before');
                const boardBox = board.getBoundingClientRect();
                const headingBox = heading.getBoundingClientRect();
                if (band.content === 'none' || band.backgroundColor === 'rgba(0, 0, 0, 0)')
                    issues.push('the title has no head band');
                if (Math.abs(headingBox.bottom - boardBox.top) > 1) issues.push('the band does not sit on the feed');
                if (parseFloat(getComputedStyle(board).borderTopLeftRadius) !== 0) issues.push('the feed keeps a separate top corner');
                const title = document.querySelector('#eventBoardTitle').getBoundingClientRect();
                const bandTop = headingBox.top + parseFloat(band.top);
                if (title.top < bandTop + 8) issues.push('the title is not inside its band');

                const rows = [...document.querySelectorAll('#eventsTable tbody tr.main-row')];
                const byId = id => rows.find(row => row.dataset.eventId === id);
                const monthStarts = rows.filter(row => row.classList.contains('month-start')).map(row => row.dataset.eventId);
                if (monthStarts.join() !== 'oct-a,sep-a,aug-a') issues.push(`month starts: ${monthStarts.join()}`);
                if (!byId('oct-b').classList.contains('day-repeat')) issues.push('a repeated day is not marked');
                if (getComputedStyle(byId('oct-b').querySelector('.col-date')).color === getComputedStyle(byId('oct-a').querySelector('.col-date')).color)
                    issues.push('a repeated day keeps a loud date');
                for (const id of ['oct-a', 'sep-a', 'aug-a']) {
                    const heading = byId(id).previousElementSibling;
                    if (!heading?.classList.contains('events-month-row')) issues.push(`${id}: no heading opens the month`);
                }
                if (rows.some(row => getComputedStyle(row).boxShadow !== 'none')) issues.push('rows are ruled');
                const backgrounds = new Set(rows.map(row => getComputedStyle(row).backgroundColor));
                if (backgrounds.size !== 1) issues.push('rows are striped');

                // The rail passes through every portrait's centre.
                for (const row of rows) {
                    const style = getComputedStyle(row);
                    const alone = row.classList.contains('month-start') && row.classList.contains('month-end');
                    if (alone !== !style.backgroundImage.includes('gradient')) { issues.push(`${row.dataset.eventId}: rail ${style.backgroundImage}`); continue; }
                    if (alone) continue;
                    const portrait = row.querySelector('.avatar, .avatar-fallback, .donation-gift-avatar, .milestone-people-avatar');
                    const box = portrait.getBoundingClientRect();
                    const rowBox = row.getBoundingClientRect();
                    const railX = rowBox.left + parseFloat(style.backgroundPositionX) + 1;
                    if (Math.abs(railX - (box.left + box.width / 2)) > 1.5)
                        issues.push(`${row.dataset.eventId}: rail ${railX} misses the portrait centre ${box.left + box.width / 2}`);
                    if (Math.abs(box.width - 32) > 1) issues.push(`${row.dataset.eventId}: portrait is ${box.width}px`);
                    if (!getComputedStyle(portrait).boxShadow.includes('0px 0px 0px 4px'))
                        issues.push(`${row.dataset.eventId}: portrait has no ring on the rail`);
                }
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme}: {string.Join("; ", problems)}");

        // An opened announcement is a panel that starts with the message and keeps the rail.
        var row = page.Locator("tr.main-row[data-event-id='sep-a']");
        await row.Locator(".custom-event-expander").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('tr.custom-event-row.is-open + tr .custom-event-body').getBoundingClientRect().height > 40");
        var panel = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const row = document.querySelector('tr.main-row[data-event-id="sep-a"]');
                const details = row.nextElementSibling;
                const body = details.querySelector('.custom-event-body');
                const style = getComputedStyle(body);
                if (Math.abs(body.getBoundingClientRect().left - row.querySelector('.event-message-cell').getBoundingClientRect().left) > 1)
                    issues.push('the panel does not start with the message');
                if (!style.boxShadow.includes('inset')) issues.push('the panel has no accent edge');
                if (parseFloat(style.borderBottomLeftRadius) < 4) issues.push('the panel is square');
                if (parseFloat(style.paddingLeft) < 12) issues.push('the panel text touches its edge');
                if (!getComputedStyle(details).backgroundImage.includes('gradient')) issues.push('the rail breaks at the panel');
                return issues;
            }
            """);
        Assert.True(panel.Length == 0, $"{width}px {scheme}: {string.Join("; ", panel)}");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    public async Task LoadingEmptyAndErrorStates_KeepTheCardsShape(int width, int height)
    {
        await using var loadingContext = await OpenAsync(width, height, "light", null);
        var loading = loadingContext.Pages[0];
        await loading.Locator(".events-skeleton-row").First.WaitForAsync();
        var skeleton = await loading.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const row = document.querySelector('.events-skeleton-row');
                const avatar = row.querySelector('.events-skeleton-avatar').getBoundingClientRect();
                if (Math.abs(avatar.width - 32) > 1) issues.push(`skeleton portrait is ${avatar.width}px`);
                if (getComputedStyle(row.querySelector('.event-message-cell'), '::after').content === 'none')
                    issues.push('the skeleton has one line');
                if (innerWidth > 600 && getComputedStyle(row.querySelector('.col-date'), '::before').content === 'none')
                    issues.push('the skeleton has no date');
                if (!getComputedStyle(row).backgroundImage.includes('gradient')) issues.push('the skeleton has no rail');
                return issues;
            }
            """);
        Assert.True(skeleton.Length == 0, $"{width}px loading: {string.Join("; ", skeleton)}");

        await using var emptyContext = await OpenAsync(width, height, "light", "[]");
        var empty = emptyContext.Pages[0];
        await empty.Locator(".events-empty-row").WaitForAsync();
        var emptyState = await empty.EvaluateAsync<double[]>(
            """
            () => {
                const cell = document.querySelector('.events-empty-row > td');
                const art = cell.querySelector('.lwc-empty__art').getBoundingClientRect();
                const box = cell.getBoundingClientRect();
                return [art.width, parseFloat(getComputedStyle(cell).paddingTop), getComputedStyle(cell).textAlign === 'center' ? 1 : 0,
                    Math.abs((art.left + art.right) / 2 - (box.left + box.right) / 2), getComputedStyle(cell, '::before').content === 'none' ? 1 : 0];
            }
            """);
        Assert.InRange(emptyState[0], 100, 120);
        Assert.True(emptyState[1] >= 40, $"empty state padding {emptyState[1]}");
        Assert.Equal(1, emptyState[2]);
        Assert.InRange(emptyState[3], 0, 1);
        Assert.Equal(1, emptyState[4]);

        await using var failedContext = await OpenAsync(width, height, "light", "fail");
        var failed = failedContext.Pages[0];
        await failed.Locator(".events-recovery").WaitForAsync();
        var error = await failed.EvaluateAsync<double[]>(
            """
            () => {
                const box = document.querySelector('.events-recovery').getBoundingClientRect();
                const board = document.querySelector('.events-board').getBoundingClientRect();
                const cell = document.querySelector('.events-recovery').closest('td');
                return [box.width, board.width, parseFloat(getComputedStyle(cell).paddingTop), Math.abs((box.left + box.right) / 2 - (board.left + board.right) / 2)];
            }
            """);
        Assert.True(error[0] <= 32 * 16 + 1 && error[0] < error[1], $"the notice is {error[0]}px in a {error[1]}px card");
        Assert.True(error[2] >= 40, $"the notice has {error[2]}px of air");
        Assert.InRange(error[3], 0, 1.5);

        // Back starts at the card's leading edge.
        var back = await failed.EvaluateAsync<double[]>(
            "() => { const b = document.querySelector('.event-board-page .back-button').getBoundingClientRect(); const c = document.querySelector('.events-board').getBoundingClientRect(); return [b.left, b.right, c.left, c.right]; }");
        Assert.True(Math.Abs(back[0] - back[2]) <= 1 && back[1] <= back[3] + 1, $"Back runs {back[0]}-{back[1]}, the card {back[2]}-{back[3]}");
    }

    private async Task<IBrowserContext> OpenAsync(int width, int height, string scheme, string? eventsBody)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = scheme == "dark" ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new() { ContentType = "application/json", Body = Athletes }));
        await context.RouteAsync("**/api/events", route => eventsBody switch
        {
            null => Task.CompletedTask,
            "fail" => route.FulfillAsync(new() { Status = 500, Body = "nope" }),
            _ => route.FulfillAsync(new() { ContentType = "application/json", Body = eventsBody })
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        return context;
    }
}
