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
    [InlineData("2026-10-30T23:59:59.999Z", "next,future,future", 0, 0)]
    [InlineData("2026-10-31T00:00:00Z", "past,next,future", 0, 0)]
    [InlineData("2027-01-14T12:00:00Z", "past,next,future", 0.5, 0)]
    [InlineData("2027-03-31T00:00:00Z", "past,past,next", 1, 0)]
    [InlineData("2027-04-08T00:00:00Z", "past,past,next", 1, 0.5)]
    [InlineData("2027-04-16T00:00:00Z", "past,past,past", 1, 1)]
    [InlineData("2027-05-01T12:00:00Z", "past,past,past", 1, 1)]
    public async Task Timeline_TracksTodayAlongTheRaceStrip(string now, string states, double firstLeg, double secondLeg)
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
        Assert.True(double.IsFinite(timeline.FirstLeg) && double.IsFinite(timeline.SecondLeg));
        Assert.Equal(firstLeg, timeline.FirstLeg, precision: 10);
        Assert.Equal(secondLeg, timeline.SecondLeg, precision: 10);
        Assert.Equal(timeline.Poster, timeline.Strip);
        Assert.All(timeline.PastNodes, color => Assert.Equal(timeline.Racing, color));
        Assert.All(timeline.NextHalo, shadow => Assert.NotEqual("none", shadow));
    }

    [Fact]
    public async Task OpenTimeline_AdvancesAtTheInclusiveDeadlineAndWhenRestored()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        var before = DateTime.Parse("2026-10-30T23:59:59Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        await page.Clock.InstallAsync(new() { Time = before.ToString("O") });
        // Freeze Date before pausing timers; the Playwright round trip may have
        // already advanced an installed, running clock past the target instant.
        await page.Clock.SetFixedTimeAsync(before);
        await page.Clock.PauseAtAsync(before);
        await page.GotoAsync("/rejuvenation-olympics");
        var items = page.Locator(".ro-milestones li");
        await Assertions.Expect(items.Nth(0)).ToHaveAttributeAsync("data-ro-state", "next");
        await page.Clock.RunForAsync(1000);
        await Assertions.Expect(items.Nth(0)).ToHaveAttributeAsync("data-ro-state", "past");
        await Assertions.Expect(items.Nth(1)).ToHaveAttributeAsync("aria-current", "step");

        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2027-04-08T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        await page.Clock.RunForAsync(60000);
        await Assertions.Expect(items.Nth(2)).ToHaveAttributeAsync("aria-current", "step");
        Assert.Equal(0.5, await items.Nth(1).EvaluateAsync<double>("e => Number(e.style.getPropertyValue('--ro-leg'))"));

        foreach (var resumeEvent in new[] { "pageshow", "visibilitychange" })
        {
            await page.Clock.SetFixedTimeAsync(DateTime.Parse("2026-10-21T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal));
            await page.EvaluateAsync("name => (name === 'pageshow' ? window : document).dispatchEvent(new Event(name))", resumeEvent);
            await Assertions.Expect(items.Nth(0)).ToHaveAttributeAsync("aria-current", "step");
            await page.Clock.SetFixedTimeAsync(DateTime.Parse("2027-05-01T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal));
            await page.EvaluateAsync("name => (name === 'pageshow' ? window : document).dispatchEvent(new Event(name))", resumeEvent);
            await Assertions.Expect(page.Locator(".ro-milestones [aria-current]")).ToHaveCountAsync(0);
            Assert.Equal(new[] { "past", "past", "past" }, await items.EvaluateAllAsync<string[]>("items => items.map(item => item.dataset.roState)"));
        }
    }

    [Theory]
    [InlineData("Pacific/Kiritimati")]
    [InlineData("America/Los_Angeles")]
    public async Task DateOnlyDeadlines_IncludeTheWholeUtcDayInEveryVisitorTimezone(string timezone)
    {
        await using var context = await NewContextAsync(Browser, App, new() { TimezoneId = timezone });
        var page = await context.NewPageAsync();
        var ends = new[] { "2026-10-31", "2027-03-31", "2027-04-16" };
        for (var i = 0; i < ends.Length; i++)
        {
            var end = DateTime.Parse(ends[i] + "T00:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
            await page.Clock.SetFixedTimeAsync(end.AddMilliseconds(-1));
            await page.GotoAsync("/rejuvenation-olympics");
            await Assertions.Expect(page.Locator(".ro-milestones li").Nth(i)).ToHaveAttributeAsync("data-ro-state", "next");
            await page.Clock.SetFixedTimeAsync(end);
            await page.ReloadAsync();
            await Assertions.Expect(page.Locator(".ro-milestones li").Nth(i)).ToHaveAttributeAsync("data-ro-state", "past");
        }
    }

    [Theory]
    [InlineData(ColorScheme.Light)]
    [InlineData(ColorScheme.Dark)]
    public async Task PastMilestones_KeepReadableContrastAndExposeTheirStatus(ColorScheme scheme)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ColorScheme = scheme });
        var page = await context.NewPageAsync();
        await page.Clock.SetFixedTimeAsync(DateTime.Parse("2027-01-14T12:00:00Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal));
        await page.GotoAsync("/rejuvenation-olympics");
        // Contrast belongs to the revealed reading state, after its entrance has settled.
        await page.Locator(".ro-timeline").ScrollIntoViewIfNeededAsync();
        await Assertions.Expect(page.Locator(".ro-milestones li[data-ro-state=past] time"))
            .ToHaveCSSAsync("opacity", "1");
        await page.WaitForFunctionAsync("() => [...document.querySelectorAll('.ro-milestones li > :is(time,h3,p)')].every(e => getComputedStyle(e).opacity === '1')");
        Assert.True(await page.Locator(".ro-milestones li[data-ro-state=past] > :is(time,h3,p)")
            .EvaluateAllAsync<bool>("elements => elements.every(e => getComputedStyle(e).opacity === '1')"),
            "Fading milestone children compounds with their text alpha and loses contrast.");
        var contrast = await BrowserContrast.MeasureVisibleTextAsync(page, ".ro-milestones time", ".ro-milestones h3", ".ro-milestones p");
        Assert.Equal(9, contrast.Length);
        BrowserContrast.AssertMinimum($"{scheme} race strip", contrast);
        var snapshot = await page.Locator(".ro-milestones").AriaSnapshotAsync();
        Assert.Contains("Deadline passed.", snapshot);
        Assert.Contains("Next deadline.", snapshot);
        Assert.Contains("Upcoming deadline.", snapshot);
        await Assertions.Expect(page.Locator(".ro-milestones [aria-current=step]")).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableScript_KeepsTheStaticDatesAndEntryLinksUsable(bool javascript)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            JavaScriptEnabled = javascript,
            ViewportSize = new() { Width = 320, Height = 640 }
        });
        await context.RouteAsync("**/js/ro-timeline.js*", route => route.AbortAsync());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await Assertions.Expect(page.Locator(".ro-standings")).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator(".ro-milestones li")).ToHaveCountAsync(3);
        await Assertions.Expect(page.Locator(".ro-milestones [data-ro-state], .ro-milestones [aria-current]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator(".ro-milestones")).ToContainTextAsync("30 Oct 2026");
        await Assertions.Expect(page.Locator(".ro-timeline-note")).ToBeVisibleAsync();
        var join = page.GetByRole(AriaRole.Link, new() { Name = "Join Longevity World Cup" });
        await Assertions.Expect(join).ToHaveAttributeAsync("href", "/join");
        await join.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "View leaderboard" })).ToBeFocusedAsync();
        await FlowActionDockBrowserTests.ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task SlowTimelineDownload_KeepsTheRuleAndClosingLinksReadable()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await context.RouteAsync("**/js/ro-timeline.js*", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        var page = await context.NewPageAsync();
        try
        {
            await page.GotoAsync("/rejuvenation-olympics", new() { WaitUntil = WaitUntilState.Commit });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(page.Locator(".ro-timeline-note")).ToBeVisibleAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Join Longevity World Cup" }))
                .ToHaveAttributeAsync("href", "/join");
            await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "View leaderboard" }))
                .ToHaveAttributeAsync("href", "/leaderboard");
            await Assertions.Expect(page.Locator(".ro-milestones [data-ro-state]")).ToHaveCountAsync(0);
        }
        finally
        {
            release.TrySetResult();
        }
        await Assertions.Expect(page.Locator(".ro-milestones[data-ro-live]")).ToHaveCountAsync(1);
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
                const finishHost = document.querySelector('.ro-standings:not([hidden])') || document.querySelector('.ro-actions');
                const finish = getComputedStyle(finishHost, '::before');
                if (!finish.backgroundImage.includes('conic-gradient')) issues.push('no finish line');
                const strip = document.querySelector('.ro-timeline').getBoundingClientRect();
                const actions = finishHost.getBoundingClientRect();
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
