using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class DocumentationTimelineBrowserTests
{
    [Theory]
    [InlineData(1440, 900, 100, false)]
    [InlineData(1440, 900, 100, true)]
    [InlineData(390, 844, 100, false)]
    [InlineData(390, 844, 200, true)]
    [InlineData(320, 640, 200, false)]
    [InlineData(305, 640, 200, false)] // 320px window with a native scrollbar gutter.
    [InlineData(820, 1180, 100, true)]
    [InlineData(844, 390, 100, false)]
    [InlineData(599, 900, 200, false)]
    [InlineData(600, 900, 200, false)]
    [InlineData(601, 900, 200, false)]
    public async Task LivingTimeline_StopsAtAnnouncedBoundaryAndKeepsRailsAligned(int width, int height, int textPercent, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history#2025-january---longevity-world-cup");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("() => document.fonts.ready");
        foreach (var index in new[] { 0, 4, 10, 4 })
        {
            await ScrollToEntryAsync(page, index);
            var issues = await page.EvaluateAsync<string[]>("""
                () => {
                    const issues = [];
                    const timeline = document.querySelector('.documentation-timeline');
                    const entries = [...timeline.querySelectorAll('.documentation-timeline-entry')];
                    const line = innerHeight * .42;
                    const centers = entries.map(e => { const r = e.querySelector('.documentation-timeline-date').getBoundingClientRect(); return r.top + r.height/2; });
                    const current = centers.findLastIndex(y => y <= line);
                    if (timeline.querySelectorAll('.is-current').length !== 1 || !entries[current]?.classList.contains('is-current')) issues.push('wrong current entry');
                    entries.forEach((e,i) => { if(e.classList.contains('is-passed') !== (i <= current)) issues.push('wrong passed entry'); });
                    for (const heading of timeline.querySelectorAll('.documentation-timeline-heading')) {
                        if (heading.scrollWidth > heading.clientWidth+1) issues.push('heading overflows its reading column');
                    }
                    const year = timeline.querySelector('.documentation-timeline-year-face');
                    const yearBox = year.getBoundingClientRect();
                    const yearHost = year.parentElement.getBoundingClientRect();
                    if (yearBox.left < yearHost.left-1 || yearBox.right > yearHost.right+1) issues.push('year overflows its gutter');
                    if (year.textContent !== entries[current]?.querySelector('.documentation-timeline-date').textContent.match(/\d{4}/)[0]) issues.push('wrong year');
                    if (getComputedStyle(timeline,'::after').content !== 'none') issues.push('legacy rail still paints');
                    const first = entries.findIndex(e => e.classList.contains('is-announced'));
                    const boundary = (centers[first-1] + centers[first])/2;
                    const fill = timeline.querySelector('.documentation-timeline-fill').getBoundingClientRect();
                    const future = timeline.querySelector('.documentation-timeline-future').getBoundingClientRect();
                    const now = timeline.querySelector('.documentation-timeline-now').getBoundingClientRect();
                    if (Math.abs(fill.bottom - Math.max(centers[0],Math.min(boundary,line))) > 1) issues.push('fill crossed the reading line or future');
                    if (Math.abs(future.top-boundary) > 1 || Math.abs(now.top+now.height/2-boundary) > 1) issues.push('future boundary moved');
                    const railStyle = getComputedStyle(timeline,'::before');
                    const railX = timeline.getBoundingClientRect().left+parseFloat(railStyle.left)+parseFloat(railStyle.width)/2;
                    for (const rect of [fill,future,now]) if(Math.abs(rect.left+rect.width/2-railX)> .6) issues.push('rail decoration is off center');
                    if (document.documentElement.scrollWidth>innerWidth+1) issues.push('horizontal overflow');
                    return issues;
                }
                """);
            Assert.True(issues.Length == 0, $"{width}x{height}, {textPercent}%, dark={dark}, entry={index}: {string.Join("; ", issues)}");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LivingTimeline_YearStaysFaintAndSettlesOnMotionChange(bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light
        });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        await ScrollToEntryAsync(page, 0);
        await page.WaitForFunctionAsync("() => document.querySelector('.documentation-timeline-year-face').getAnimations().every(a => a.playState === 'finished')");
        await ScrollToEntryAsync(page, 2);
        var maxOpacity = await page.EvaluateAsync<double>("""
            async () => {
                const face = document.querySelector('.documentation-timeline-year-face');
                let max = 0;
                for (let i=0; i<12; i++) {
                    max = Math.max(max, Number(getComputedStyle(face).opacity)*Number(getComputedStyle(face.parentElement).opacity));
                    await new Promise(requestAnimationFrame);
                }
                return max;
            }
            """);
        Assert.InRange(maxOpacity, 0, .121);
        await ScrollToEntryAsync(page, 4);
        await ScrollToEntryAsync(page, 0);
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        await page.WaitForFunctionAsync("() => document.querySelector('.documentation-timeline-year-face').getAnimations().length === 0");
        Assert.Equal("2018", await page.Locator(".documentation-timeline-year-face").TextContentAsync());
        // Intentional wait: a canceled outgoing/incoming animation must not overwrite the settled year after its full 480ms lifetime.
        await page.WaitForTimeoutAsync(550);
        Assert.Equal("2018", await page.Locator(".documentation-timeline-year-face").TextContentAsync());
        Assert.Equal("none", await page.Locator(".documentation-timeline-now").EvaluateAsync<string>("e=>getComputedStyle(e,'::after').animationName"));
    }

    [Fact]
    public async Task LivingTimeline_PrintHasNoReadingDecorationsAndNoScriptKeepsTheOriginalDocument()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        await NavigateAndSettleAsync(page, "/history");
        await page.EmulateMediaAsync(new() { Media = Media.Print });
        Assert.Empty(await page.Locator(".documentation-timeline > :not(.documentation-timeline-entry)").EvaluateAllAsync<string[]>(
            "els=>els.filter(e=>getComputedStyle(e).display!=='none').map(e=>e.className)"));
        await page.EmulateMediaAsync(new() { Media = Media.Screen });
        Assert.True(await page.Locator(".documentation-timeline-year").EvaluateAsync<bool>("e=>getComputedStyle(e).display!=='none'"));

        await using var staticContext = await NewContextAsync(Browser, App, new() { JavaScriptEnabled = false });
        var staticPage = await staticContext.NewPageAsync();
        await staticPage.GotoAsync("/history");
        Assert.Equal(await page.Locator(".documentation-timeline-entry h3").AllTextContentsAsync(), await staticPage.Locator(".documentation-timeline-entry h3").AllTextContentsAsync());
        Assert.Null(await staticPage.Locator(".documentation-timeline").GetAttributeAsync("data-live"));
    }

    private static Task ScrollToEntryAsync(IPage page, int index) => page.EvaluateAsync("""
        async index => {
            const entry = document.querySelectorAll('.documentation-timeline-entry')[index];
            const r = entry.querySelector('.documentation-timeline-date').getBoundingClientRect();
            scrollTo({ top:scrollY+r.top+r.height/2-innerHeight*.42+2, behavior:'instant' });
            window.LwcHistoryTimeline.refresh();
            await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
        }
        """, index);
}
