using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadC)]
public sealed class RejuvenationOlympicsStandingsBrowserTests(
    PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, ColorScheme.Light, 100)]
    [InlineData(1440, 900, ColorScheme.Dark, 200)]
    [InlineData(390, 844, ColorScheme.Light, 100)]
    [InlineData(390, 844, ColorScheme.Dark, 200)]
    [InlineData(320, 480, ColorScheme.Light, 200)]
    [InlineData(768, 1024, ColorScheme.Dark, 200)]
    [InlineData(844, 390, ColorScheme.Light, 200)]
    public async Task Standings_PreserveRanksLinksScoresAndFullTextWithoutInternalOverflow(int width, int height, ColorScheme theme, int text)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height }, ColorScheme = theme, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await Assertions.Expect(page.Locator(".ro-standings")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-ready"));
        await page.AddStyleTagAsync(new() { Content = $"html {{font-size:{text}% !important}}" });
        await page.EvaluateAsync("() => document.fonts.ready");
        var rows = JsonNode.Parse(await (await context.APIRequest.GetAsync(new Uri(App.BaseAddress, "/api/data/standings").ToString())).TextAsync())!.AsArray();
        var links = page.Locator(".ro-step-link, .ro-row-link");
        await Assertions.Expect(links).ToHaveCountAsync(8);
        for (var i = 0; i < rows.Count; i++)
        {
            await Assertions.Expect(links.Nth(i)).ToHaveAttributeAsync("href", rows[i]!["path"]!.GetValue<string>());
            await Assertions.Expect(links.Nth(i)).ToContainTextAsync(rows[i]!["name"]!.GetValue<string>());
        }
        Assert.True(await page.EvaluateAsync<bool>("""
            () => [...document.querySelectorAll('.ro-step-name,.ro-step-score,.ro-row-name,.ro-row-score,.ro-clock')]
                .every(e => e.scrollWidth <= e.clientWidth + 1 && e.scrollHeight <= e.clientHeight + 1)
            """));
        Assert.Equal(0, await page.Locator(".ro-standings").EvaluateAsync<int>("e => e.getAnimations({subtree:true}).filter(a=>a.playState==='running').length"));
        Assert.True(await page.Locator(".ro-clock-part").Last.EvaluateAsync<bool>("""
            e => { const cells=[...e.children].map(c=>c.getBoundingClientRect()); return cells.every(c=>Math.abs(c.top-cells[0].top)<1); }
            """), "The hours, minutes and seconds must remain readable as one time at supported widths.");
        await ExpectNoHorizontalOverflowAsync(page);
        await links.First.FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(links.Nth(1)).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("invalid")]
    [InlineData("stalled")]
    public async Task UnavailableStandings_ReturnFinishLineToActions(string mode)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        if (mode == "stalled")
            await context.AddInitScriptAsync("""
                const originalFetch = window.fetch;
                window.fetch = (url, options) => url === '/api/data/standings'
                    ? new Promise((resolve,reject) => options.signal.addEventListener('abort', () => reject(new Error('aborted'))))
                    : originalFetch(url, options);
                """);
        else
            await context.RouteAsync("**/api/data/standings", route => route.FulfillAsync(new()
            { Status = mode == "failure" ? 503 : 200, ContentType = "application/json", Body = "{}" }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        await Assertions.Expect(page.Locator(".ro-standings")).ToHaveCountAsync(0, new() { Timeout = 15000 });
        Assert.Contains("conic-gradient", await page.Locator(".ro-actions").EvaluateAsync<string>("e => getComputedStyle(e,'::before').backgroundImage"));
        await Assertions.Expect(page.Locator(".ro-actions a").First).ToBeVisibleAsync();
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Fact]
    public async Task Countdown_TicksAcrossEveryInclusiveDeadline_AndDisappearsAfterFinalExpiration()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await context.NewPageAsync();
        var start = DateTime.Parse("2026-10-30T23:59:59Z", null, System.Globalization.DateTimeStyles.AdjustToUniversal);
        await page.Clock.InstallAsync(new() { Time = start.ToString("O") });
        await page.Clock.SetFixedTimeAsync(start);
        await page.Clock.PauseAtAsync(start);
        await page.GotoAsync("/rejuvenation-olympics");
        foreach (var (time, index) in new[] { ("2026-10-30T23:59:59Z", 0), ("2027-03-30T23:59:59Z", 1), ("2027-04-15T23:59:59Z", 2) })
        {
            await page.Clock.SetFixedTimeAsync(DateTime.Parse(time, null, System.Globalization.DateTimeStyles.AdjustToUniversal));
            await page.EvaluateAsync("() => window.dispatchEvent(new Event('pageshow'))");
            await Assertions.Expect(page.Locator(".ro-clock")).ToHaveTextAsync("0d00:00:01");
            await page.Clock.SetFixedTimeAsync(DateTime.Parse(time, null, System.Globalization.DateTimeStyles.AdjustToUniversal).AddSeconds(1));
            await page.Clock.RunForAsync(1000);
            await Assertions.Expect(page.Locator(".ro-milestones li").Nth(index)).ToHaveAttributeAsync("data-ro-state", "past");
        }
        await Assertions.Expect(page.Locator(".ro-clock")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entrance_SettlesImmediatelyForReaderFocusOrReducedMotionChange(bool reduce)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 320 } });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        var standings = page.Locator(".ro-standings");
        await Assertions.Expect(standings).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-ready"));
        await Assertions.Expect(standings).Not.ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-in"));
        await standings.EvaluateAsync("e => e.scrollIntoView({block:'start'})");
        await Assertions.Expect(standings).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-playing"));
        if (reduce) await page.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });
        else await page.Locator(".ro-step-link").First.FocusAsync();
        await Assertions.Expect(standings).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-settled"));
        Assert.Equal(0, await standings.EvaluateAsync<int>("e => e.getAnimations({subtree:true}).filter(a=>a.playState==='running').length"));
        Assert.True(await standings.EvaluateAsync<bool>("""
            e => [...e.querySelectorAll('[data-value]')].every(s => s.textContent ===
                (Number(s.dataset.value)>0?'+':'') + Number(s.dataset.value).toFixed(Number(s.dataset.decimals)) + ' years')
            """));
        if (!reduce) Assert.Equal("1", await page.Locator(":focus").EvaluateAsync<string>("e => getComputedStyle(e).opacity"));
    }

    [Fact]
    public async Task MissingPortrait_UsesADecodedFallbackWithTheAthletesIdentity()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        await context.RouteAsync("**/api/data/standings", async route =>
        {
            var response = await route.FetchAsync();
            var data = JsonNode.Parse(await response.TextAsync())!.AsArray();
            data[0]!["thumb"] = "/missing-review-portrait.webp";
            data[0]!["name"] = "Review Athlete";
            data[1]!["ageReduction"] = null;
            await route.FulfillAsync(new() { Response = response, Body = data.ToJsonString() });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/rejuvenation-olympics");
        var image = page.Locator(".ro-step-portrait img").First;
        await PortraitSystemBrowserTests.AssertDecodedFallbackAsync(image, "RA");
        await Assertions.Expect(image).ToHaveAttributeAsync("alt", "");
        await Assertions.Expect(page.Locator(".ro-step-link").First).ToHaveAttributeAsync("aria-label", new System.Text.RegularExpressions.Regex("^Review Athlete, rank"));
        await Assertions.Expect(page.Locator(".ro-step-score").Nth(1)).ToHaveTextAsync("\u2014");
    }
}
