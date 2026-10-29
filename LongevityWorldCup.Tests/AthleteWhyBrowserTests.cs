using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class AthleteWhyBrowserTests(PlaywrightBrowserFixture browserFixture, BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, false, 100)]
    [InlineData(390, true, 200)]
    [InlineData(320, false, 200)]
    [InlineData(768, true, 100)]
    public async Task Why_PreservesTextWithoutEmptyDecorationAccessibleQuotesOrClipping(int width, bool dark, int textPercent)
    {
        using var client = App.CreateClient();
        var athletes = (await client.GetFromJsonAsync<JsonArray>("/api/data/athletes"))!;
        var athlete = athletes.Single(a => a?["Name"]?.GetValue<string>() == "Michael Lustgarten")!;
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await context.RouteAsync("**/api/data/athletes*", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = athletes.ToJsonString()
        }));
        var page = await context.NewPageAsync();
        foreach (var why in new string?[] { null, "", " \t\r\n\u00a0\u0085\u2003", "  Santé & 家族 — let's live!\nSecond line <script>alert('x')</script>  ", new('W', 240), "  「Already quoted — 健康」" })
        {
            if (why is null) athlete.AsObject().Remove("Why");
            else athlete["Why"] = why;
            await page.GotoAsync("/athlete/michael-lustgarten");
            await page.WaitForFunctionAsync("() => !document.querySelector('#detailsModal .modal-content').hasAttribute('data-server-rendered-profile') && !document.querySelector('#detailsModal .modal-content').classList.contains('is-loading')");
            await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
            await HomepageChromeRegressionBrowserTests.SettleLayoutAsync(page);
            var bio = page.Locator("#athleteBio");
            Assert.Equal(why ?? "", await bio.TextContentAsync());
            if (string.IsNullOrWhiteSpace(why))
            {
                Assert.False(await bio.IsVisibleAsync(), $"{width}px: blank source must occupy no space or show a mark.");
                continue;
            }
            Assert.True(await bio.IsVisibleAsync());
            Assert.Equal(0, await bio.Locator("script").CountAsync());
            var accessible = await bio.AriaSnapshotAsync();
            Assert.DoesNotContain("“", accessible);
            var problems = await bio.EvaluateAsync<string[]>("""
                bio => {
                    const bounds = bio.getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(bio);
                    const failures = [];
                    if ([...range.getClientRects()].filter(r => r.width > 0).some(r => r.left < bounds.left - 1 || r.right > bounds.right + 1 || r.top < bounds.top - 1 || r.bottom > bounds.bottom + 1))
                        failures.push('text leaves quote');
                    const modal = document.querySelector('#detailsModal .modal-content');
                    if (modal.scrollWidth > modal.clientWidth + 1) failures.push('dialog horizontal overflow');
                    if (getComputedStyle(bio).whiteSpace !== 'pre-wrap') failures.push('line breaks or whitespace collapsed');
                    let following = bio.nextElementSibling;
                    while (following && !following.getClientRects().length) following = following.nextElementSibling;
                    if (following && following.getBoundingClientRect().top < bounds.bottom - 1) failures.push('following section overlaps');
                    return failures;
                }
                """);
            Assert.Empty(problems);
        }
    }
}
