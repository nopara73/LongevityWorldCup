using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

// Days are groups and announcements are the news: a hairline and a breath open
// each new day, an announcement is a card whose story continues the same card,
// and on phones the day is a label over its group instead of a repeated footnote.
public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, 900, "light")]
    [InlineData(1024, 768, "dark")]
    [InlineData(390, 844, "dark")]
    [InlineData(320, 640, "light")]
    public async Task Days_AreGroupsAndAnnouncementsAreCards(int width, int height, string scheme)
    {
        await using var context = await OpenAsync(width, height, scheme, Events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug-a']").WaitForAsync();

        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const row = id => document.querySelector(`tr.main-row[data-event-id="${id}"]`);
                const avatar = id => row(id).querySelector('.col-avatar .avatar, .col-avatar .avatar-fallback').getBoundingClientRect();
                const layers = r => (getComputedStyle(r).backgroundImage.match(/gradient/g) || []).length;

                // oct-a and oct-b share a day; oct-c opens the next one.
                if (layers(row('oct-c')) < 2) issues.push('a new day opens without a hairline');
                if (layers(row('oct-b')) > 1) issues.push('a repeated day is divided from its own events');
                const within = avatar('oct-b').top - avatar('oct-a').bottom;
                const between = avatar('oct-c').top - avatar('oct-b').bottom;
                if (between < within + 8) issues.push(`days are ${between}px apart, events in a day ${within}px`);

                // The announcement is a card on the timeline.
                const card = row('sep-a').querySelector('.event-message-cell');
                const style = getComputedStyle(card);
                if (style.backgroundColor === 'rgba(0, 0, 0, 0)') issues.push('the announcement has no surface');
                if (parseFloat(style.borderTopLeftRadius) < 4) issues.push('the announcement card is square');
                if (!style.boxShadow.includes('inset')) issues.push('the announcement card has no accent edge');
                if (card.getBoundingClientRect().height < 44) issues.push(`the card is ${card.getBoundingClientRect().height}px tall`);
                const plain = row('oct-a').querySelector('.event-message-cell');
                if (getComputedStyle(plain).backgroundColor !== 'rgba(0, 0, 0, 0)') issues.push('an ordinary event became a card');

                if (innerWidth <= 600) {
                    // The day is a label over the first event of its group.
                    const date = row('oct-a').querySelector('.col-date').getBoundingClientRect();
                    const message = plain.getBoundingClientRect();
                    if (date.bottom > message.top + 1) issues.push('the phone date sits below its event');
                    if (getComputedStyle(row('oct-a').querySelector('.col-date')).textTransform !== 'uppercase') issues.push('the phone date is not a label');
                    if (row('oct-b').querySelector('.col-date').getBoundingClientRect().width > 1) issues.push('a repeated day is printed again on phones');
                    if (!row('oct-b').querySelector('.col-date').textContent.trim()) issues.push('a repeated day lost its accessible date');

                    // The month's rail still starts at its first portrait.
                    const first = row('oct-a'), box = first.getBoundingClientRect();
                    const railTop = box.top + parseFloat(getComputedStyle(first).backgroundPositionY.split(',')[0]);
                    const centre = (avatar('oct-a').top + avatar('oct-a').bottom) / 2;
                    if (Math.abs(railTop - centre) > 2) issues.push(`the rail starts ${railTop - centre}px from the first portrait`);
                }
                if (document.documentElement.scrollWidth > innerWidth) issues.push('page overflows');
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px {scheme}: {string.Join("; ", problems)}");

        // Opened, the story continues the card instead of hanging under it.
        await page.Locator("tr.main-row[data-event-id='sep-a'] .custom-event-expander").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('tr.custom-event-row.is-open + tr .custom-event-body').getBoundingClientRect().height > 40");
        await page.Mouse.MoveAsync(5, 5);
        await WaitForFiniteAnimationsAsync(page);
        var opened = await page.EvaluateAsync<string[]>(
            """
            async () => {
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const issues = [];
                const row = document.querySelector('tr.main-row[data-event-id="sep-a"]');
                const head = row.querySelector('.event-message-cell'), body = row.nextElementSibling.querySelector('.custom-event-body');
                const h = head.getBoundingClientRect(), b = body.getBoundingClientRect();
                if (Math.abs(b.top - h.bottom) > 1) issues.push(`the story starts ${b.top - h.bottom}px from its title`);
                if (Math.abs(b.left - h.left) > 1 || Math.abs(b.right - h.right) > 1) issues.push('the story and its title differ in width');
                if (parseFloat(getComputedStyle(head).borderBottomLeftRadius) > 0) issues.push('the title closes the card');
                if (parseFloat(getComputedStyle(body).borderTopLeftRadius) > 0) issues.push('the story opens a second card');
                if (parseFloat(getComputedStyle(body).borderBottomLeftRadius) < 4) issues.push('the card has a square end');
                if (getComputedStyle(head).backgroundColor !== getComputedStyle(body).backgroundColor) issues.push('the title and story differ in surface');
                return issues;
            }
            """);
        Assert.True(opened.Length == 0, $"{width}px {scheme}: {string.Join("; ", opened)}");
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(390, 844)]
    public async Task Loading_HasTheAnatomyOfDaysAndAnAnnouncement(int width, int height)
    {
        await using var context = await OpenAsync(width, height, "light", null);
        var page = context.Pages[0];
        await page.Locator("tr.events-skeleton-row").First.WaitForAsync();
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const rows = [...document.querySelectorAll('.event-board-page tr.events-skeleton-row')];
                const tinted = rows.filter(r => getComputedStyle(r.querySelector('.event-message-cell')).backgroundColor !== 'rgba(0, 0, 0, 0)');
                if (tinted.length !== 1) issues.push(`${tinted.length} loading cards`);
                const ruled = rows.filter(r => (getComputedStyle(r).backgroundImage.match(/gradient/g) || []).length > 1);
                if (ruled.length < 1) issues.push('loading shows no day divisions');
                if (innerWidth <= 600) {
                    for (const row of rows) {
                        const startsDay = row === rows[0] || ruled.includes(row);
                        const date = row.querySelector('.col-date');
                        const shown = getComputedStyle(date).display !== 'none';
                        if (shown !== startsDay) issues.push(`loading row ${rows.indexOf(row) + 1}: date does not match its day division`);
                    }
                }
                return issues;
            }
            """);
        Assert.True(problems.Length == 0, $"{width}px: {string.Join("; ", problems)}");
    }
}
