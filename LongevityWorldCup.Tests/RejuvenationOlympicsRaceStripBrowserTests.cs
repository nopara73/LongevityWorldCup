using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class RejuvenationOlympicsRaceStripBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData("2026-10-21T12:00:00Z", "next,future,future", 0, 0)]
    [InlineData("2027-01-15T12:00:00Z", "past,next,future", 0.4, 0)]
    [InlineData("2027-04-05T12:00:00Z", "past,past,next", 1, 0.2)]
    [InlineData("2027-05-01T12:00:00Z", "past,past,past", 1, 1)]
    public async Task Timeline_TracksTodayAlongTheRaceStrip(string now, string states, double firstLegAtLeast, double secondLegAtLeast)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse(now, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        await page.GotoAsync("/rejuvenation-olympics");

        var timeline = await page.EvaluateAsync<TimelineState>(
            """
            () => {
                const items = [...document.querySelectorAll('.ro-milestones li')];
                const leg = item => parseFloat(getComputedStyle(item).getPropertyValue('--ro-leg'));
                return {
                    States: items.map(item => item.dataset.roState ?? '').join(','),
                    FirstLeg: leg(items[0]),
                    SecondLeg: leg(items[1]),
                    Strip: getComputedStyle(document.querySelector('.ro-timeline')).backgroundColor,
                    Poster: getComputedStyle(document.querySelector('.ro-artwork')).backgroundColor,
                    PastNodes: items.filter(item => item.dataset.roState === 'past')
                        .map(item => getComputedStyle(item, '::before').backgroundColor),
                    Racing: getComputedStyle(items[0], '::before').borderTopColor,
                    NextHalo: items.filter(item => item.dataset.roState === 'next')
                        .map(item => getComputedStyle(item, '::before').boxShadow)
                };
            }
            """);

        Assert.Equal(states, timeline.States);
        Assert.InRange(timeline.FirstLeg, firstLegAtLeast, firstLegAtLeast == 0 ? 0 : 1);
        Assert.InRange(timeline.SecondLeg, secondLegAtLeast, secondLegAtLeast == 0 ? 0 : 1);
        Assert.Equal(timeline.Poster, timeline.Strip);
        Assert.All(timeline.PastNodes, color => Assert.Equal(timeline.Racing, color));
        Assert.All(timeline.NextHalo, shadow => Assert.NotEqual("none", shadow));
    }

    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task ClosingBand_CrossesAFinishLineToInternalActionsThatStayOnOneLine(int width, ColorScheme colorScheme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = colorScheme,
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await page.EvaluateAsync("() => document.fonts.ready");

        var issues = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                const finish = getComputedStyle(document.querySelector('.ro-actions'), '::before');
                if (!finish.backgroundImage.includes('conic-gradient')) issues.push('no finish line');
                const strip = document.querySelector('.ro-timeline').getBoundingClientRect();
                const actions = document.querySelector('.ro-actions').getBoundingClientRect();
                if (Math.abs(actions.top - strip.bottom) > 1) issues.push('finish line does not follow the strip');
                for (const link of document.querySelectorAll('.ro-actions a')) {
                    const arrow = link.querySelector('[aria-hidden="true"]')?.textContent;
                    if (arrow !== '→') issues.push(`${link.textContent.trim()} uses an external-link arrow for an internal page`);
                    const lineHeight = parseFloat(getComputedStyle(link).lineHeight) || 24;
                    const text = [...link.childNodes].find(node => node.nodeType === Node.TEXT_NODE);
                    const range = document.createRange();
                    range.selectNodeContents(text);
                    if (range.getClientRects().length > 1 || range.getBoundingClientRect().height > lineHeight * 1.5)
                        issues.push(`${link.textContent.trim()} wraps`);
                }
                const note = getComputedStyle(document.querySelector('.ro-timeline-note'));
                if (note.borderLeftWidth !== '0px') issues.push('the qualifying rule is still a separate callout card');
                return issues;
            }
            """);

        Assert.True(issues.Length == 0, $"{width}px {colorScheme}: {string.Join("; ", issues)}");
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    private sealed class TimelineState
    {
        public string States { get; set; } = "";
        public double FirstLeg { get; set; }
        public double SecondLeg { get; set; }
        public string Strip { get; set; } = "";
        public string Poster { get; set; } = "";
        public string[] PastNodes { get; set; } = [];
        public string Racing { get; set; } = "";
        public string[] NextHalo { get; set; } = [];
    }
}
