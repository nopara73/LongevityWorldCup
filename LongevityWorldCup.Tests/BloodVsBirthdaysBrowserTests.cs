using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LongevityWorldCup.Website.Business;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class BloodVsBirthdaysBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task BackgroundRefresh_PreservesFocusedChoiceAndProofDisclosure()
    {
        await using var context = await ContextAsync(1280, 900);
        await StubAsync(context, () => PuzzleResponse("2026-10-04"));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/blood-vs-birthdays");
        await page.Locator("#startButton").ClickAsync();
        await page.Locator("#timer").Filter(new LocatorFilterOptions { HasText = "s" }).WaitForAsync();
        var choice = page.Locator(".game-choice").First;
        await choice.FocusAsync();
        await RefreshAsync();
        Assert.True(await choice.EvaluateAsync<bool>("button => document.activeElement === button"));
        await choice.PressAsync("Enter");
        var proofs = page.Locator(".game-evidence-links details").First;
        await proofs.Locator("summary").ClickAsync();
        await proofs.Locator("a").First.FocusAsync();
        await RefreshAsync();
        Assert.True(await proofs.EvaluateAsync<bool>("details => details.open"));
        Assert.True(await proofs.Locator("a").First.EvaluateAsync<bool>("link => document.activeElement === link"));

        async Task RefreshAsync()
        {
            var fetch = page.WaitForResponseAsync(response => response.Url.EndsWith("/api/blood-vs-birthdays"));
            await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
            await fetch;
            // Wait for the API response's DOM work, not merely receipt of headers.
            await page.EvaluateAsync("new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");
        }
    }

    [Fact]
    public async Task FiveRounds_PersistRevealsAndScore_AndPlayWithKeyboardOnMobile()
    {
        await using var context = await ContextAsync(390, 844);
        var response = PuzzleResponse("2026-10-04");
        await StubAsync(context, () => response);
        await context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"]);
        var page = await context.NewPageAsync();
        var errors = new List<string>(); page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync("/blood-vs-birthdays");
        await page.Locator("#timedMode").UncheckAsync();
        await page.Locator("#startButton").ClickAsync();
        for (var i = 0; i < 5; i++)
        {
            var correct = page.Locator($".game-choice[data-slug='{response.Puzzle.Rounds[i].WinnerSlug}']");
            await correct.FocusAsync(); await correct.PressAsync("Enter");
            await page.Locator("#answerPanel:not([hidden])").WaitForAsync();
            Assert.Equal(2, await page.Locator(".game-receipt").CountAsync());
            Assert.Equal(2, await page.GetByText("Athlete profile ↗", new PageGetByTextOptions { Exact = true }).CountAsync());
            if (i == 0)
            {
                await page.ReloadAsync();
                await page.Locator("#answerPanel:not([hidden])").WaitForAsync();
                Assert.Contains("1 / 5", await page.Locator("#roundScore").InnerTextAsync());
            }
            await page.Locator("#nextButton").ClickAsync();
        }
        await page.Locator("#resultsPanel:not([hidden])").WaitForAsync();
        Assert.Contains("5", await page.Locator("#finalScore").InnerTextAsync());
        Assert.Equal("1 day streak", await page.Locator("#streakDisplay").InnerTextAsync());
        await page.Locator("#shareButton").ClickAsync();
        await page.GetByText("Score copied. The answers stay a mystery.").WaitForAsync();
        var copied = await page.EvaluateAsync<string>("navigator.clipboard.readText()");
        Assert.Contains("5/5 untimed", copied); Assert.DoesNotContain("Athlete", copied);
        Assert.Contains("https://longevityworldcup.com/blood-vs-birthdays", copied);
        await page.ReloadAsync();
        await page.Locator("#resultsPanel:not([hidden])").WaitForAsync();
        Assert.Equal(5, await page.Locator(".game-result-tile.is-correct").CountAsync());
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.Empty(errors);
    }

    [Fact]
    public async Task TimerDeadline_SurvivesReload_AndTimeoutRevealsWithoutDoubleScoring()
    {
        await using var context = await ContextAsync(1280, 900);
        var response = PuzzleResponse("2026-10-04"); await StubAsync(context, () => response);
        var page = await context.NewPageAsync(); await page.GotoAsync("/blood-vs-birthdays");
        await page.Locator("#startButton").ClickAsync();
        await page.Locator("#timer").Filter(new LocatorFilterOptions { HasText = "s" }).WaitForAsync();
        var deadline = await page.EvaluateAsync<double>("JSON.parse(localStorage.getItem('lwc.blood-vs-birthdays.v1')).games['2026-10-04-test'].deadlineUtc");
        await page.ReloadAsync();
        await page.Locator("#arena:not([hidden])").WaitForAsync();
        Assert.Equal(deadline, await page.EvaluateAsync<double>("JSON.parse(localStorage.getItem('lwc.blood-vs-birthdays.v1')).games['2026-10-04-test'].deadlineUtc"));
        // Expire the saved UTC deadline rather than slowing the whole suite by ten seconds.
        await page.EvaluateAsync("""
            () => {
                const key = 'lwc.blood-vs-birthdays.v1';
                const saved = JSON.parse(localStorage.getItem(key));
                saved.games['2026-10-04-test'].deadlineUtc = 1;
                localStorage.setItem(key, JSON.stringify(saved));
            }
            """);
        await page.ReloadAsync();
        await page.GetByText("Time got away. The blood test didn’t.").WaitForAsync();
        Assert.Contains("0 / 5", await page.Locator("#roundScore").InnerTextAsync());
        Assert.Equal(1, await page.EvaluateAsync<int>("JSON.parse(localStorage.getItem('lwc.blood-vs-birthdays.v1')).games['2026-10-04-test'].answers.length"));
        await page.Locator("#nextButton").ClickAsync();
        Assert.Equal("step", await page.Locator("#roundProgress li").Nth(1).GetAttributeAsync("aria-current"));
    }

    [Fact]
    public async Task NewDay_KeepsCurrentRoundUntilPlayerChoosesNewSet()
    {
        await using var context = await ContextAsync(390, 844);
        var response = PuzzleResponse("2026-10-04"); await StubAsync(context, () => response);
        var page = await context.NewPageAsync(); await page.GotoAsync("/blood-vs-birthdays");
        await page.Locator("#timedMode").UncheckAsync(); await page.Locator("#startButton").ClickAsync();
        await page.Locator(".game-choice").First.ClickAsync();
        await page.Locator("#nextButton").ClickAsync();
        response = PuzzleResponse("2026-10-05");
        await page.EvaluateAsync("window.dispatchEvent(new Event('focus'))");
        await page.Locator("#dayChange:not([hidden])").WaitForAsync();
        Assert.Contains("4 October", await page.Locator("#puzzleDate").InnerTextAsync());
        Assert.Equal("step", await page.Locator("#roundProgress li").Nth(1).GetAttributeAsync("aria-current"));
        await page.Locator("#newDayButton").ClickAsync();
        Assert.Contains("5 October", await page.Locator("#puzzleDate").InnerTextAsync());
        await page.Locator("#introPanel:not([hidden])").WaitForAsync();
        Assert.Equal(1, await page.EvaluateAsync<int>("JSON.parse(localStorage.getItem('lwc.blood-vs-birthdays.v1')).games['2026-10-04-test'].answers.length"));
    }

    [Fact]
    public async Task FailedFetch_RetriesRecover_StorageFailureDoesNotBlockPlay_AndDarkLayoutFits()
    {
        await using var context = await ContextAsync(320, 720);
        var failures = true;
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/blood-vs-birthdays", route => failures
            ? route.FulfillAsync(new RouteFulfillOptions { Status = 503, Body = "unavailable" })
            : route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = JsonSerializer.Serialize(PuzzleResponse("2026-10-04"), Json) }));
        await context.AddInitScriptAsync("Storage.prototype.setItem = function() { throw new Error('No storage'); };");
        var page = await context.NewPageAsync(); await page.EmulateMediaAsync(new PageEmulateMediaOptions { ColorScheme = ColorScheme.Dark, ReducedMotion = ReducedMotion.Reduce });
        await page.GotoAsync("/blood-vs-birthdays");
        await page.Locator("#loadError:not([hidden])").WaitForAsync(); failures = false;
        await page.Locator("#retryButton").ClickAsync(); await page.Locator("#introPanel:not([hidden])").WaitForAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        Assert.True(await page.Locator(".game-intro-copy").EvaluateAsync<bool>("copy => { const card = copy.closest('section').getBoundingClientRect(); const text = copy.getBoundingClientRect(); return text.right <= card.right && text.left >= card.left && copy.scrollWidth <= copy.clientWidth; }"));
        Assert.Contains("their birthday", await page.Locator("#introTitle").InnerTextAsync());
        await page.Locator("#timedMode").UncheckAsync(); await page.Locator("#startButton").ClickAsync();
        await page.Locator("#storageWarning:not([hidden])").WaitForAsync();
        await page.Locator(".game-choice").First.ClickAsync();
        await page.Locator("#answerPanel:not([hidden])").WaitForAsync();
        Assert.True(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth"));
        var boxes = await page.Locator(".game-choice").EvaluateAllAsync<float[]>("els => els.map(el => el.getBoundingClientRect().width)");
        Assert.All(boxes, width => Assert.True(width >= 120));
    }

    [Fact]
    public async Task ReviewPage_HasVersionedAssetsAndNoDiscoveryOrAnnouncementHooks_AndRealEndpointWorks()
    {
        using var factory = new TestWebApplicationFactory(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAthleteSnapshotProvider>();
            services.AddSingleton<IAthleteSnapshotProvider>(new BloodVsBirthdaysTests.GameAthletes(BloodVsBirthdaysTests.Athletes()));
        }));
        using var client = factory.CreateClient();
        using var document = await client.GetAsync("/blood-vs-birthdays");
        Assert.Equal(HttpStatusCode.OK, document.StatusCode);
        Assert.Contains("noindex", string.Join(',', document.Headers.GetValues("X-Robots-Tag")));
        var html = await document.Content.ReadAsStringAsync();
        Assert.Contains("Blood vs. Birthdays | Longevity World Cup", html);
        Assert.DoesNotContain("{{ASSET", html);
        Assert.Contains("/css/blood-vs-birthdays.css?v=", html);
        Assert.Contains("/js/blood-vs-birthdays.js?v=", html);
        Assert.DoesNotContain("blood-vs-birthdays", await client.GetStringAsync("/sitemap.xml"));
        Assert.DoesNotContain("blood-vs-birthdays", await client.GetStringAsync("/"));
        using var api = await client.GetAsync("/api/blood-vs-birthdays");
        api.EnsureSuccessStatusCode();
        var body = await api.Content.ReadFromJsonAsync<BloodBirthdayResponse>();
        Assert.NotNull(body); Assert.Equal(5, body.Puzzle.Rounds.Length);
        Assert.Equal("no-store", api.Headers.CacheControl!.ToString());
        Assert.DoesNotContain("blood-vs-birthdays", await client.GetStringAsync("/swagger/v1/swagger.json"));
    }

    private async Task<IBrowserContext> ContextAsync(int width, int height) => await Browser.NewContextAsync(new BrowserNewContextOptions
    { BaseURL = App.BaseAddress.ToString(), ViewportSize = new ViewportSize { Width = width, Height = height } });

    private static async Task StubAsync(IBrowserContext context, Func<BloodBirthdayResponse> get)
    {
        await BrowserTestApp.RouteExternalResourcesAsync(context);
        await context.RouteAsync("**/api/blood-vs-birthdays", route => route.FulfillAsync(new RouteFulfillOptions
        { ContentType = "application/json", Body = JsonSerializer.Serialize(get(), Json) }));
    }

    private static BloodBirthdayResponse PuzzleResponse(string day)
    {
        var rounds = Enumerable.Range(0, 5).Select(i =>
        {
            var left = new BloodBirthdayAthlete("alpha_" + i, "Alex " + i, "/assets/content-images/play-athlete-placeholder.webp",
                55.5 + i, 34.25 + i, 21.25, "2026-04-01", "/athlete/alpha-" + i, ["/assets/content-images/play-athlete-placeholder.webp"]);
            var right = new BloodBirthdayAthlete("beta_" + i, "Sam " + i, "/assets/content-images/play-athlete-placeholder.webp",
                56.2 + i, 40.0 + i, 16.2, "2026-07-01", "/athlete/beta-" + i, ["/assets/content-images/play-athlete-placeholder.webp"]);
            return new BloodBirthdayRound(left, right, left.Slug);
        }).ToArray();
        var serverNow = DateTimeOffset.Parse(day + "T01:00:00Z");
        return new BloodBirthdayResponse(serverNow, new DateTimeOffset(serverNow.UtcDateTime.Date.AddHours(16), TimeSpan.Zero),
            new BloodBirthdayPuzzle(1, day + "-test", day, "Asia/Singapore", serverNow, rounds));
    }
}
