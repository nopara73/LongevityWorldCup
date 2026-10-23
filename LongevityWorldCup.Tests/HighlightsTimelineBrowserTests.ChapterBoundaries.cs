using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class HighlightsTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, 900, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(320, 640, 200)]
    public async Task Chapters_KeepKeyboardTargetsClearOfBothStickyBands(int width, int height, int percent)
    {
        var events = Enumerable.Range(0, 72).Select(i => new
        {
            Id = "focus-" + i, Type = 6, Text = $"Milestone {i}\n\nMore details",
            OccurredAt = new DateTime(2026, 10 - i / 24, 28 - i % 24, 12, 0, 0, DateTimeKind.Utc).ToString("O")
        });
        await using var context = await OpenAsync(width, height, "light", JsonSerializer.Serialize(events));
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='focus-71']").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await page.EvaluateAsync("p => document.documentElement.style.fontSize=`${p}%`", percent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.Locator("tr.main-row[data-event-id='focus-32'] .custom-event-expander").FocusAsync();
        await page.EvaluateAsync("() => { const row=document.querySelector('tr.main-row[data-event-id=\"focus-32\"]'); scrollTo({top:scrollY+row.getBoundingClientRect().top-140,behavior:'instant'}); }");
        for (var i = 0; i < 10; i++)
        {
            await page.Keyboard.PressAsync("Shift+Tab");
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            var visible = await page.EvaluateAsync<bool>(
                """
                () => {
                    const button=document.activeElement, box=button.getBoundingClientRect();
                    const hit=document.elementFromPoint(box.left+box.width/2,box.top+box.height/2);
                    return button.matches('.custom-event-expander')&&button.contains(hit);
                }
                """);
            Assert.True(visible, $"{width}px {percent}%: keyboard target {i} is covered by a sticky band.");
        }
    }

    [Fact]
    public async Task Chapters_ReconcileRetainedAppendAndTrimWithoutLosingFocusOrDetails()
    {
        var events = """
            [{"Id":"oct","Type":6,"Text":"October announcement\n\n[Read the rules](https://longevityworldcup.com/ruleset)","OccurredAt":"2026-10-23T12:00:00Z"},
             {"Id":"sep","Type":6,"Text":"September announcement\n\nMore details","OccurredAt":"2026-09-01T12:00:00Z"},
             {"Id":"aug","Type":6,"Text":"August announcement\n\nMore details","OccurredAt":"2026-08-01T12:00:00Z"}]
            """;
        await using var context = await OpenAsync(1440, 900, "light", events);
        var page = context.Pages[0];
        await page.Locator("tr.main-row[data-event-id='aug']").WaitForAsync();
        await page.EvaluateAsync("() => loadEventsTable(2, false)");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(2);
        await page.Locator("tr.main-row[data-event-id='oct'] .custom-event-expander").ClickAsync();
        var link = page.GetByRole(AriaRole.Link, new() { Name = "Read the rules", Exact = true });
        await link.FocusAsync();
        await page.EvaluateAsync("() => window.__retainedChapterRow=document.querySelector('tr.main-row[data-event-id=\"oct\"]')");
        foreach (var count in new[] { 3, 1, 3 })
        {
            await page.EvaluateAsync("n => loadEventsTable(n,false,null,true,{preserveExistingRows:true})", count);
            await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(count);
            await Assertions.Expect(page.Locator(".events-month-row")).ToHaveCountAsync(count);
            await Assertions.Expect(page.Locator("#eventsTable tbody")).ToHaveCountAsync(count);
            await Assertions.Expect(link).ToBeFocusedAsync();
            Assert.True(await page.EvaluateAsync<bool>("window.__retainedChapterRow===document.querySelector('tr.main-row[data-event-id=\"oct\"]')"));
            await Assertions.Expect(page.Locator("tr.main-row[data-event-id='oct'] .custom-event-expander")).ToHaveAttributeAsync("aria-expanded", "true");
        }
    }

    [Theory]
    [InlineData("ar-EG", 1440)]
    [InlineData("bn-BD", 1440)]
    [InlineData("fa-IR", 1440)]
    [InlineData("th-TH", 1440)]
    [InlineData("ja-JP", 1440)]
    [InlineData("hu-HU", 390)]
    [InlineData("ar-EG", 390)]
    [InlineData("bn-BD", 390)]
    [InlineData("fa-IR", 390)]
    [InlineData("ja-JP", 390)]
    public async Task Chapters_KeepLocalizedDigitsAndAccessibleDatesInTheirActualMonth(string locale, int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 }, Locale = locale,
            TimezoneId = "Asia/Singapore", ReducedMotion = ReducedMotion.Reduce
        });
        var events = """
            [{"Id":"late","Type":0,"Text":"Late October","OccurredAt":"2026-10-23T12:00:00Z"},
             {"Id":"early","Type":0,"Text":"Early October","OccurredAt":"2026-10-01T12:00:00Z"},
             {"Id":"history","Type":0,"Text":"Previous October","OccurredAt":"2025-10-23T12:00:00Z"}]
            """;
        await context.RouteAsync("**/api/events", r => r.FulfillAsync(new() { ContentType = "application/json", Body = events }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(3);
        var problems = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues=[];
                const rows=[...document.querySelectorAll('#eventsTable tr.main-row')];
                const headers=[...document.querySelectorAll('.events-month-row th')];
                const expectedMonth=new Date('2026-10-23T12:00:00Z').toLocaleDateString(undefined,{calendar:'gregory',month:'long',year:'numeric'});
                if(headers.length!==2||headers[0].textContent!==expectedMonth) issues.push('chapter label uses a different calendar');
                for(const row of rows){
                    const time=row.querySelector('time'), date=new Date(time.dateTime);
                    const full=date.toLocaleDateString(undefined,{calendar:'gregory',year:'numeric',month:'long',day:'numeric'});
                    if(time.getAttribute('aria-label')!==full) issues.push('full accessible date is missing');
                    if(date.getFullYear()!==new Date().getFullYear()&&time.textContent!==date.toLocaleDateString(undefined,{calendar:'gregory',year:'numeric',month:'short',day:'numeric'})) issues.push('historical year loses localized digits or order');
                    const day=row.querySelector('.event-date-day');
                    const expected=new Intl.DateTimeFormat(undefined,{calendar:'gregory',month:'short',day:'numeric'}).formatToParts(date).find(p=>p.type==='day').value;
                    if(day?.textContent!==expected) issues.push('localized day digits were not split');
                    const range=document.createRange();range.selectNodeContents(widthIsWide()?day:time);
                    const cell=row.querySelector('.col-date').getBoundingClientRect();
                    for(const rect of range.getClientRects()){
                        if(rect.width>1&&(rect.left<cell.left-1||rect.right>cell.right+1)) issues.push('visible date glyphs leave their cell');
                    }
                    const header=row.parentElement.querySelector('.events-month-row th');
                    const month=date.toLocaleDateString(undefined,{calendar:'gregory',year:'numeric',month:'long'});
                    if(header?.textContent!==month||header.scope!=='rowgroup') issues.push('chapter association includes the wrong rows');
                }
                if(document.documentElement.scrollWidth>innerWidth) issues.push('page overflows');
                return issues;
                function widthIsWide(){return innerWidth>600;}
            }
            """);
        Assert.True(problems.Length == 0, $"{locale}: {string.Join("; ", problems)}");
        var snapshot = await page.Locator("#eventsTable").AriaSnapshotAsync();
        Assert.Contains(await page.Locator("time").First.GetAttributeAsync("aria-label") ?? "missing date", snapshot);
    }

    [Theory]
    [InlineData(1440, 100)]
    [InlineData(390, 100)]
    [InlineData(320, 200)]
    [InlineData(844, 200)]
    public async Task Chapters_YieldAtTheirOwnEndAndFollowTheMeasuredSiteBar(int width, int percent)
    {
        var events = Enumerable.Range(0, 72).Select(i => new
        {
            Id = "long-" + i, Type = i == 23 ? 6 : 0,
            Text = i == 23 ? "End of month\n\n[Read the rules](https://longevityworldcup.com/ruleset)" : "Timeline milestone " + i,
            OccurredAt = new DateTime(2026, 10 - i / 24, 28 - i % 24, 12, 0, 0, DateTimeKind.Utc).ToString("O")
        });
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = width == 844 ? 390 : 900 },
            ReducedMotion = ReducedMotion.Reduce, Locale = "en-US"
        });
        await context.RouteAsync("**/api/events", r => r.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(events) }));
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/events");
        await Assertions.Expect(page.Locator("tr.main-row")).ToHaveCountAsync(72);
        await page.EvaluateAsync("p => { document.documentElement.style.fontSize=`${p}%`; document.querySelector('tr.main-row[data-event-id=\"long-23\"]')._setCustomEventExpanded(true); }", percent);
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        await page.Keyboard.PressAsync("Shift");
        // The final month is deliberately long too: no artificial body padding
        // can conceal a heading that outlives its real group.
        foreach (var index in new[] { 12, 36, 60 })
        {
            await page.EvaluateAsync("i => { const row=document.querySelector(`tr.main-row[data-event-id=\"long-${i}\"]`); scrollTo({top:scrollY+row.getBoundingClientRect().top-innerHeight/2,behavior:'instant'}); }", index);
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            var gap = await page.EvaluateAsync<double>("i => document.querySelector(`tr.main-row[data-event-id=\"long-${i}\"]`).parentElement.querySelector('.events-month-row').getBoundingClientRect().top-document.querySelector('.site-sticky-header').getBoundingClientRect().bottom", index);
            Assert.InRange(gap, -1, 2);
        }
        await page.EvaluateAsync("() => { const next=document.querySelector('tr.main-row[data-event-id=\"long-24\"]').parentElement; scrollTo({top:scrollY+next.getBoundingClientRect().top-parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--site-sticky-height'))-15,behavior:'instant'}); }");
        await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
        Assert.True(await page.EvaluateAsync<bool>(
            """
            () => {
                const headings=[...document.querySelectorAll('.events-month-row')].map(h=>h.getBoundingClientRect());
                return headings[0].bottom<=headings[1].top+1;
            }
            """), "The outgoing heading overlaps the incoming chapter.");
        Assert.DoesNotContain("gradient", await page.Locator("tr.main-row[data-event-id='long-23'] + tr.details-row").EvaluateAsync<string>("r=>getComputedStyle(r).backgroundImage"));
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Read the rules", Exact = true })).ToBeVisibleAsync();
        await ReloadTimelineAsync(page);
        await Assertions.Expect(page.Locator("#eventsTable tbody")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".events-month-row")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator("tr.main-row.is-open")).ToHaveCountAsync(1);
    }
}

