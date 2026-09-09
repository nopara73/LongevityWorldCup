using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteDirectoryRecoveryBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(390, false)]
    [InlineData(390, true)]
    [InlineData(1280, false)]
    [InlineData(1280, true)]
    public async Task Selection_StalledRequestOffersRetryWithoutLosingTypingOrAcceptingLateData(int width, bool stalledBody)
    {
        await using var context = await NewContextAsync(Browser, App, new() {
            ViewportSize = new() { Width = width, Height = 844 }, IsMobile = width == 390, HasTouch = width == 390
        });
        await StallFirstDirectoryAsync(context, stalledBody, "[{\"Name\":\"Martin Helstáb\",\"Biomarkers\":[]}]");
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync("/select-athlete", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(page.Locator("#playSelectionBackBtn")).ToBeEnabledAsync();
        var input = page.Locator("#playAthleteInput");
        await input.FillAsync("martin helstab");
        await ExpireDirectoryAsync(page);
        var feedback = page.Locator("#playAthleteError");
        var retry = feedback.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Assertions.Expect(retry).ToBeVisibleAsync();
        await Assertions.Expect(input).ToHaveValueAsync("martin helstab");
        await Assertions.Expect(input).ToBeFocusedAsync();
        Assert.DoesNotContain("No matching", await feedback.InnerTextAsync());
        Assert.Equal(1, await page.EvaluateAsync<int>("window.directoryAborts"));
        await retry.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var option = page.GetByRole(AriaRole.Option, new() { Name = "Martin Helstáb", Exact = true });
        await Assertions.Expect(option).ToBeVisibleAsync();
        await page.EvaluateAsync("() => window.finishLateDirectory()");
        await input.PressAsync("ArrowDown");
        await input.PressAsync("Enter");
        await Assertions.Expect(input).ToHaveValueAsync("Martin Helstáb");
        await Assertions.Expect(page.Locator("#playConfirmAthleteBtn")).ToBeEnabledAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("window.directoryAttempts"));
    }

    [Theory]
    [InlineData("/leaderboard", ".leaderboard-retry-button", "#leaderboardStatus", "Leaderboard loaded.")]
    [InlineData("/athlete/michael-lustgarten", ".leaderboard-retry-button", "#athleteName", "Michael Lustgarten")]
    [InlineData("/event-board-embed.html?athlete=michael_lustgarten&embed=1", ".events-retry-button", "#eventsStatus", "No events yet.")]
    public async Task SharedDirectory_RecoversInTheCallingViewIncludingEmbeds(string path, string retrySelector, string loadedSelector, string loadedText)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ViewportSize = new() { Width = 390, Height = 844 } });
        await context.RouteAsync("**/api/events", route => route.FulfillAsync(new() { ContentType = "application/json", Body = "[]" }));
        await StallFirstDirectoryAsync(context, stalledBody: true);
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        var response = await page.GotoAsync(path, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.True(response?.Ok, $"Recovery view failed to load: {response?.Status} {page.Url}");
        await ExpireDirectoryAsync(page);
        var retry = page.Locator(retrySelector).First;
        await Assertions.Expect(retry).ToBeVisibleAsync();
        var urlBeforeRetry = page.Url;
        await retry.ClickAsync();
        await Assertions.Expect(page.Locator(loadedSelector)).ToContainTextAsync(loadedText);
        await page.EvaluateAsync("() => window.finishLateDirectory()");
        Assert.Equal(urlBeforeRetry, page.Url);
        await Assertions.Expect(retry).ToBeHiddenAsync();
        Assert.Equal(2, await page.EvaluateAsync<int>("window.directoryAttempts"));
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
    }

    [Theory]
    [InlineData("pheno")]
    [InlineData("bortz")]
    public async Task CalculatorPreview_RecoversFromAnUnfinishedDirectoryBody(string clock)
    {
        await using var context = await NewContextAsync(Browser, App, new());
        await StallFirstDirectoryAsync(context, stalledBody: true, "[]");
        var page = await context.NewPageAsync();
        await page.Clock.InstallAsync();
        await page.GotoAsync($"/{clock}-age", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => Boolean(window.LwcBioAgeRankPreview)");
        await page.EvaluateAsync("""
            clock => {
                document.body.classList.add('bioage-result-ready');
                document.getElementById(`${clock}AgeResult`)?.classList.add('show');
                void window.LwcBioAgeRankPreview.render(`${clock}AgeRankPreview`, {
                    clock, ageReduction: -5, dateOfBirth: new Date(1990, 0, 1)
                });
            }
            """, clock);
        await ExpireDirectoryAsync(page);
        var preview = page.Locator($"#{clock}AgeRankPreview");
        await preview.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Assertions.Expect(preview.Locator(".bioage-rank-summary")).ToContainTextAsync("#1");
        await page.EvaluateAsync("() => window.finishLateDirectory()");
        await Assertions.Expect(preview.Locator(".bioage-rank-summary")).ToContainTextAsync("#1");
        Assert.Equal(2, await page.EvaluateAsync<int>("window.directoryAttempts"));
    }

    internal static async Task ExpireDirectoryAsync(IPage page)
    {
        await page.WaitForFunctionAsync("() => typeof window.finishLateDirectory === 'function'");
        await page.Clock.FastForwardAsync(11000);
    }

    internal static Task StallFirstDirectoryAsync(IBrowserContext context, bool stalledBody, string? recoveryJson = null)
    {
        var recovery = recoveryJson is null ? "null" : recoveryJson;
        return context.AddInitScriptAsync($$$"""
            localStorage.setItem('gmaSkipAll', 'true');
            window.directoryAttempts = 0;
            window.directoryAborts = 0;
            const originalDirectoryFetch = window.fetch.bind(window);
            window.fetch = (input, options) => {
                const url = new URL(typeof input === 'string' ? input : input.url, location.href);
                if (url.pathname !== '/api/data/athletes') return originalDirectoryFetch(input, options);
                if (++window.directoryAttempts !== 1) {
                    const recovery = {{{recovery}}};
                    return recovery === null ? originalDirectoryFetch(input, options)
                        : Promise.resolve(new Response(JSON.stringify(recovery), {headers:{'content-type':'application/json'}}));
                }
                options?.signal?.addEventListener('abort', () => window.directoryAborts++);
                const late = [{Name:'Stale Athlete', Biomarkers:[]}];
                if ({{{JsonSerializer.Serialize(stalledBody)}}}) {
                    const response = new Response('', {headers:{'content-type':'application/json'}});
                    response.json = () => new Promise(resolve => { window.finishLateDirectory = () => resolve(late); });
                    return Promise.resolve(response);
                }
                return new Promise(resolve => {
                    window.finishLateDirectory = () => resolve(new Response(JSON.stringify(late), {headers:{'content-type':'application/json'}}));
                });
            };
            """);
    }
}
