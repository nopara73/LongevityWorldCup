using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// Months are chapters: each opens with a heading band that stays in view while
// its events scroll past, and on wider screens the day leads its events.
public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1024, 768, "dark")]
    [InlineData(768, 1024, "light")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task Months_OpenWithStickyHeadingBandsAndDaysLeadTheirEvents(int width, int height, string scheme)
    {
        await using var context = await OpenAsync(width, height, scheme, Events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const board = document.querySelector('.event-board-page .events-board');
                const headings = [...board.querySelectorAll('tbody tr.events-month-row')];
                const expected = [[2026, 9], [2026, 8], [2026, 7]].map(([y, m]) => new Date(y, m, 1).toLocaleDateString(undefined, { month: 'long', year: 'numeric' }));
                if (headings.map(h => h.textContent.trim()).join('|') !== expected.join('|'))
                    issues.push(`headings: ${headings.map(h => h.textContent.trim()).join('|')}`);
                if (getComputedStyle(board).overflowY !== 'clip') issues.push(`the card scrolls its own headings away (${getComputedStyle(board).overflowY})`);
                const band = getComputedStyle(document.querySelector('.event-board-heading'), '::before').backgroundColor;
                for (const heading of headings) {
                    const row = getComputedStyle(heading), cell = getComputedStyle(heading.firstElementChild);
                    if (row.position !== 'sticky') issues.push('a heading does not stay in view');
                    if (cell.backgroundColor !== band) issues.push(`a heading is ${cell.backgroundColor}, the title band ${band}`);
                    if (cell.textTransform !== 'uppercase' || parseFloat(cell.letterSpacing) <= 0) issues.push('a heading is not set as a section title');
                    const next = heading.nextElementSibling;
                    if (!next?.classList.contains('month-start')) issues.push('a heading does not open its month');
                    if (Math.abs(heading.getBoundingClientRect().width - board.clientWidth) > 1) issues.push('a heading does not span the card');
                }

                const day = id => document.querySelector(`tr.main-row[data-event-id="${id}"] .col-date`);
                for (const id of ['oct-a','oct-b']) {
                    const time=day(id).querySelector('time');
                    const expected=new Date(time.dateTime).toLocaleDateString(undefined,{calendar:'gregory',month:'long',day:'numeric',year:'numeric'});
                    if(time.getAttribute('aria-label')!==expected) issues.push(`${id}: full accessible date is missing`);
                }
                if (innerWidth > 600) {
                    const lead = day('oct-a').querySelector('.event-date-day').getBoundingClientRect();
                    if (lead.height < 20) issues.push(`the day is ${lead.height}px tall`);
                    if (day('oct-a').querySelector('.event-date-rest').getBoundingClientRect().width > 1) issues.push('the month repeats beside the day');
                    if (day('oct-b').querySelector('.event-date-day').getBoundingClientRect().width > 1) issues.push('a repeated day is printed again');
                } else if (day('oct-a').querySelector('.event-date-rest').getBoundingClientRect().width <= 1) {
                    issues.push('the phone date lost its month');
                }

                // Each month keeps its own rail: none runs through a heading.
                for (const heading of headings) {
                    const previousGroup = heading.closest('tbody').previousElementSibling;
                    const before = previousGroup?.matches('tbody') ? previousGroup.lastElementChild : null;
                    if (!before) continue;
                    const row = before.classList.contains('details-row') ? before.previousElementSibling : before;
                    if (!row.classList.contains('month-end')) issues.push(`${row.dataset.eventId} does not end its month`);
                }
                if (headings.some(h => h.parentElement.querySelectorAll('.events-month-row').length !== 1))
                    issues.push('a table row group contains several month headers');
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme}: {string.Join("; ", problems)}");

        // Scrolled into September, its heading waits under the site's bar above its events.
        await page.Keyboard.PressAsync("Shift");
        await page.EvaluateAsync(
            """
            () => {
                document.body.style.paddingBottom = '200vh';
                const row = document.querySelector('tr.main-row[data-event-id="sep-b"]');
                scrollTo({ top: scrollY + row.getBoundingClientRect().top - 140, behavior: 'instant' });
            }
            """);
        await page.WaitForFunctionAsync("() => document.querySelector('.site-sticky-header.visible')");
        var stuck = await page.EvaluateAsync<double[]>(
            """
            () => {
                const heading = document.querySelector('tr.events-month-row[data-month="2026-9"]').getBoundingClientRect();
                const bar = document.querySelector('.site-sticky-header').getBoundingClientRect();
                const row = document.querySelector('tr.main-row[data-event-id="sep-b"]').getBoundingClientRect();
                return [heading.top, bar.bottom, heading.bottom, row.top];
            }
            """);
        Assert.True(Math.Abs(stuck[0] - stuck[1]) <= 2, $"{width}px: the heading sits at {stuck[0]}, the bar ends at {stuck[1]}");
        Assert.True(stuck[2] <= stuck[3] + 1, $"{width}px: the heading covers its event ({stuck[2]} > {stuck[3]})");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    public async Task Loading_ShowsTheFirstHeadingBandAndBackLeadsFromTheCardEdge(int width, int height)
    {
        await using var context = await OpenAsync(width, height, "light", null);
        var page = context.Pages[0];
        await page.Locator(".events-skeleton-row").First.WaitForAsync();
        var result = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const tbody = document.querySelector('.event-board-page .events-board tbody');
                const band = getComputedStyle(tbody, '::before');
                const title = getComputedStyle(document.querySelector('.event-board-heading'), '::before').backgroundColor;
                if (band.content === 'none' || parseFloat(band.height) < 32) issues.push('loading has no heading band');
                else if (!band.background.includes(title) && band.backgroundColor !== title) issues.push(`the loading band is ${band.backgroundColor}`);
                const back = document.querySelector('.event-board-page .back-button').getBoundingClientRect();
                const card = document.querySelector('.events-board').getBoundingClientRect();
                if (Math.abs(back.left - card.left) > 1) issues.push(`Back starts at ${back.left}, the card at ${card.left}`);
                if (innerWidth <= 600 && Math.abs(back.right - card.right) > 1) issues.push('Back does not span the phone card');
                return issues;
            }
            """);
        Assert.True(result.Length == 0, $"{width}px: {string.Join("; ", result)}");
    }
}
