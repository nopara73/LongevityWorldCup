using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Json;
using Microsoft.Playwright;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteStoryBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture) : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, false)]
    [InlineData(390, true)]
    public async Task ComparisonToggles_NeverRearmSettledRowsOrRestartTheirCounters(int width, bool dark)
    {
        await using var context = await ContextAsync(width, dark);
        var page = await OpenAsync(context);
        await page.Locator("tr.result-summary").Nth(2).ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(2200);
        await page.EvaluateAsync("""
            () => {
                window.settled = [...document.querySelectorAll('tr.result-summary')].filter(row => !row.hasAttribute('data-draw'));
                window.restarted = [];
                window.rowValues = window.settled.map(row => row.textContent);
                new MutationObserver(records => records.forEach(record => {
                    const row = record.target.closest('tr.result-summary');
                    if (window.settled.includes(row) && record.target.hasAttribute(record.attributeName))
                        window.restarted.push(record.attributeName);
                })).observe(document.getElementById('athlete-biomarkers'), {
                    subtree: true, attributes: true, attributeFilter: ['data-draw', 'data-lwc-count']
                });
            }
            """);
        Assert.True(await page.EvaluateAsync<int>("() => window.settled.length") >= 2);
        var button = page.Locator("tr.result-summary button").Nth(2);
        for (var pass = 0; pass < 4; pass++)
        {
            await button.ClickAsync();
            await Expect(button).ToHaveAttributeAsync("aria-expanded", pass % 2 == 0 ? "true" : "false");
            await page.WaitForTimeoutAsync(200);
        }
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.restarted"));
        Assert.True(await page.EvaluateAsync<bool>("() => window.settled.every((row, i) => row.textContent === window.rowValues[i])"));
        await page.WaitForTimeoutAsync(1600);
        Assert.Empty(await page.EvaluateAsync<string[]>("() => window.restarted"));
    }

    [Fact]
    public async Task FocusAndReducedMotion_RevealImmediatelyAndCancelPendingWork()
    {
        await using var context = await ContextAsync(390, false);
        var page = await OpenAsync(context);
        await page.Locator("tr.result-summary button").Last.FocusAsync();
        Assert.True(await page.Locator("tr.result-summary").Last.EvaluateAsync<bool>("row => !row.dataset.draw && +getComputedStyle(row.cells[0]).opacity === 1"));
        Assert.Equal(1, await page.Locator("#athlete-biomarkers").EvaluateAsync<double>("e => +getComputedStyle(e).opacity"));
        await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        // Media-query change delivery is asynchronous. First observe cleanup,
        // then outwait the original entrances to catch any late resurrection.
        await Expect(page.Locator("#detailsModal [data-story], #detailsModal [data-unlock], #detailsModal [data-draw], #detailsModal [data-pressed], #detailsModal .biomarker-overview-current[data-lwc-count]")).ToHaveCountAsync(0);
        await page.WaitForTimeoutAsync(1800);
        var pending = await page.Locator("#detailsModal [data-story], #detailsModal [data-unlock], #detailsModal [data-draw], #detailsModal [data-pressed], #detailsModal .biomarker-overview-current[data-lwc-count]").EvaluateAllAsync<string[]>("es => es.map(e => JSON.stringify({tag:e.tagName, data:{...e.dataset}}))");
        Assert.True(pending.Length == 0, await page.EvaluateAsync<string>("() => JSON.stringify({reduced:matchMedia('(prefers-reduced-motion: reduce)').matches, assets:[...document.scripts].map(s=>s.src).filter(s=>s.includes('athlete-story'))})") + string.Join("\n", pending));
        await page.Locator("#closeAthleteDetailsModal").ClickAsync();
        await Expect(page.Locator("#detailsModal")).ToBeHiddenAsync();
        await page.EvaluateAsync("() => window.openAthleteModalBySlug('benjamin-garden', { suppressGuessMyAge: true })");
        await ReadyAsync(page);
        Assert.Equal(0, await page.Locator("#detailsModal [data-story], #detailsModal [data-unlock], #detailsModal [data-draw]").CountAsync());
    }

    [Fact]
    public async Task RapidCloseReopenAndGuessMode_ClearOldEntrances()
    {
        await using var context = await ContextAsync(1440, true);
        var page = await OpenAsync(context);
        await page.EvaluateAsync("""
            () => {
                window.closeModal({ fromHistory: true });
                window.openAthleteModalBySlug('valerie-orsoni', { suppressGuessMyAge: true });
            }
            """);
        await ReadyAsync(page);
        await Expect(page.Locator("#athleteName")).ToContainTextAsync("Valerie Orsoni");
        await page.Locator("#athlete-biomarkers").ScrollIntoViewIfNeededAsync();
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').classList.add('guess-mode')");
        await page.WaitForTimeoutAsync(1800);
        Assert.Equal(0, await page.Locator("#detailsModal [data-story], #detailsModal [data-unlock], #detailsModal [data-draw], #detailsModal .biomarker-overview-current[data-lwc-count]").CountAsync());
        await page.EvaluateAsync("() => document.querySelector('#detailsModal .modal-content').classList.remove('guess-mode')");
        await page.WaitForTimeoutAsync(300);
        await Expect(page.Locator("#athleteName")).ToContainTextAsync("Valerie Orsoni");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TimelineAndFallbackGauge_StaySynchronizedWithTheChosenClock(bool reduced)
    {
        await using var context = await ContextAsync(390, true, reduced, history: true);
        var page = await OpenAsync(context);
        foreach (var clock in new[] { "Bortz", "Pheno" })
        {
            await page.Locator("#ageTab" + clock).ClickAsync();
            var select = page.Locator("#ageResultSelect");
            var count = await select.Locator("option").CountAsync();
            Assert.True(count > 1);
            await Expect(page.Locator(".age-story-timeline-dot")).ToHaveCountAsync(count);
            foreach (var index in new[] { 0, count - 1, 0 })
            {
                await select.SelectOptionAsync(index.ToString());
                await page.WaitForTimeoutAsync(100);
                var reading = await page.EvaluateAsync<JsonElement>("""
                    () => ({
                        text: document.getElementById('ageRadarFallbackValue').textContent,
                        date: document.getElementById('ageResultDate').textContent,
                        label: document.getElementById('ageResultSelect').selectedOptions[0].textContent,
                        bio: document.getElementById('ageRadarFallbackBio').textContent,
                        ratio: +document.querySelector('#ageRadarFallback .age-story-gap').style.getPropertyValue('--story-gap'),
                        clock: document.querySelector('#ageRadarFallback .age-story-gap').dataset.clock,
                        selected: [...document.querySelectorAll('.age-story-timeline-dot')].findIndex(dot => dot.classList.contains('is-selected'))
                    })
                    """);
                var diff = double.Parse(reading.GetProperty("text").GetString()!.Split(' ')[0].Replace('−', '-'), System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(Math.Clamp(diff / 20, -1, 1), reading.GetProperty("ratio").GetDouble(), 5);
                Assert.Equal(clock.ToLowerInvariant(), reading.GetProperty("clock").GetString());
                Assert.Equal(index, reading.GetProperty("selected").GetInt32());
                Assert.Contains(reading.GetProperty("date").GetString()!, reading.GetProperty("label").GetString());
                Assert.Contains(reading.GetProperty("bio").GetString()!.Split(": ")[1], reading.GetProperty("label").GetString());
            }
        }
    }

    [Fact]
    public async Task Gauge_ClampsSignedReadingsAndHidesMissingOrCensoredValues()
    {
        await using var context = await ContextAsync(390, false, reduced: true);
        var page = await OpenAsync(context);
        foreach (var (text, ratio, hidden) in new[]
        {
            ("+50 years", 1, false), ("−50 years", -1, false), ("0 years", 0, false),
            ("—", 0, true), ("<0.5", 0, true)
        })
        {
            await page.Locator("#ageRadarFallbackValue").EvaluateAsync("(e, text) => e.textContent = text", text);
            await Expect(page.Locator("#ageRadarFallback .age-story-gap")).ToHaveJSPropertyAsync("hidden", hidden);
            await Expect(page.Locator("#ageRadarFallback .age-story-gap")).ToHaveCSSAsync("--story-gap", ratio.ToString());
            await Expect(page.Locator("#ageRadarFallbackValue")).ToHaveTextAsync(text);
        }
    }

    [Fact]
    public async Task TouchTimeline_ScrubsWithoutSwitchingAthletes_AndVerticalPanOnlyScrolls()
    {
        await using var context = await ContextAsync(390, false, history: true);
        var page = await OpenAsync(context);
        var track = page.Locator(".age-story-timeline");
        await track.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(1000);
        var cdp = await context.NewCDPSessionAsync(page);
        var bounds = (await track.BoundingBoxAsync())!;
        await DragAsync(cdp, bounds.X + bounds.Width - 10, bounds.Y + 22, bounds.X + 10, bounds.Y + 22);
        await Expect(page.Locator("#ageResultSelect")).ToHaveValueAsync("0");
        await Expect(page.Locator("#athleteName")).ToContainTextAsync("Benjamin Garden");
        await track.ScrollIntoViewIfNeededAsync();
        bounds = (await track.BoundingBoxAsync())!;
        var scroll = await page.Locator("#detailsModal .modal-content").EvaluateAsync<double>("e => e.scrollTop");
        await DragAsync(cdp, bounds.X + bounds.Width - 10, bounds.Y + 22, bounds.X + bounds.Width - 10, bounds.Y - 140);
        await Expect(page.Locator("#ageResultSelect")).ToHaveValueAsync("0");
        Assert.True(await page.Locator("#detailsModal .modal-content").EvaluateAsync<double>("e => e.scrollTop") > scroll + 30);
        await Expect(track).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-scrubbing"));
    }

    private static async Task DragAsync(ICDPSession cdp, float x1, float y1, float x2, float y2)
    {
        for (var step = 0; step <= 10; step++)
            await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object>
            {
                ["type"] = step == 0 ? "touchStart" : "touchMove",
                ["touchPoints"] = new[] { new { x = x1 + (x2 - x1) * step / 10, y = y1 + (y2 - y1) * step / 10, id = 1 } }
            });
        await cdp.SendAsync("Input.dispatchTouchEvent", new Dictionary<string, object> { ["type"] = "touchEnd", ["touchPoints"] = Array.Empty<object>() });
    }

    private async Task<IBrowserContext> ContextAsync(int width, bool dark, bool reduced = false, bool history = false)
    {
        var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, HasTouch = width < 600, IsMobile = width < 600,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reduced ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        if (history)
        {
            // Synthetic repeated panels isolate picker/gesture behavior from
            // changes to the public athlete's available laboratory history.
            using var client = App.CreateClient();
            var athletes = (await client.GetFromJsonAsync<JsonArray>("/api/data/athletes"))!;
            var athlete = athletes.Single(a => a?["Name"]?.GetValue<string>() == "Benjamin Garden")!.DeepClone().AsObject();
            athlete["DateOfBirth"] = JsonSerializer.SerializeToNode(AlbuminCapTestData.DateOfBirth);
            var first = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(43))!;
            var latest = JsonSerializer.SerializeToNode(AlbuminCapTestData.Marker(50))!;
            latest["Date"] = "2026-02-01";
            athlete["Biomarkers"] = new JsonArray(first, latest, new JsonObject { ["Date"] = "2026-03-01", ["CrpMgL"] = 0 });
            await context.AddInitScriptAsync($"window.getSharedAthletes = async () => ([{athlete.ToJsonString()}]);");
        }
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await AestheticSystemBrowserTests.NavigateAndSettleAsync(page, "/athlete/benjamin-garden");
        await ReadyAsync(page);
        await page.Keyboard.PressAsync("Shift");
        await page.WaitForTimeoutAsync(200);
        return page;
    }

    private static Task ReadyAsync(IPage page) => page.WaitForFunctionAsync("() => document.querySelector('#detailsModal .modal-content:not(.is-loading)') && document.querySelectorAll('tr.result-summary').length > 3");
}
