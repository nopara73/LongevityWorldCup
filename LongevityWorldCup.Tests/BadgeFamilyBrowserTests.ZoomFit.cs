using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;

namespace LongevityWorldCup.Tests;

public sealed partial class BadgeFamilyBrowserTests
{
    [Theory]
    [InlineData(195, 422, false)]
    [InlineData(195, 422, true)]
    [InlineData(720, 450, false)]
    [InlineData(720, 450, true)]
    public async Task ZoomSizedProfile_UsesOneScrollOwnerAndKeepsShortExplanationsWhole(int width, int height, bool dark)
    {
        // These are the CSS viewports observed at native 200% browser zoom.
        // Keep native gutters visible; the independent review captures also
        // exercise chrome.tabs.setZoom(2), rather than treating a viewport as zoom.
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new()
        {
            Headless = true, IgnoreDefaultArgs = ["--hide-scrollbars"]
        });
        await using var context = await AestheticSystemBrowserTests.NewContextAsync(browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce
        });
        await context.AddInitScriptAsync("localStorage.setItem('gmaSkipAll', 'true');");
        await RouteAthleteFixtureAsync(context, athletes =>
        {
            var michael = athletes.OfType<JsonObject>().Single(athlete =>
                athlete["AthleteSlug"]!.GetValue<string>() == "michael_lustgarten");
            // The same five real badge categories as the public review snapshot.
            michael["Badges"] = new JsonArray(
                new JsonObject { ["BadgeLabel"] = "Age reduction", ["LeagueCategory"] = "Amateur", ["LeagueValue"] = "Amateur", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Crowd – most guessed", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "Podcast" },
                new JsonObject { ["BadgeLabel"] = "S25", ["Place"] = 1 },
                new JsonObject { ["BadgeLabel"] = "≥2 submissions" });
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/athlete/{Slug}");
        var toggle = page.Locator("#modalBadgeStrip .modal-badge-toggle").Filter(new() { HasText = "The Regular" });
        await toggle.ClickAsync();
        await WaitForFiniteAnimationsAsync(page);
        await page.EvaluateAsync("() => document.fonts.ready");
        var panel = page.Locator(".modal-badge-explainer");
        await panel.ScrollIntoViewIfNeededAsync();
        foreach (var selector in new[] { "#detailsModal .modal-content", "#modalBadgeStrip", ".modal-badge-explainer" })
        {
            var dimensions = await page.Locator(selector).EvaluateAsync<int[]>(
                "e => [e.scrollWidth, e.clientWidth, e.scrollHeight, e.clientHeight]");
            Assert.True(dimensions[0] <= dimensions[1], $"{selector} scrolls horizontally: {string.Join(',', dimensions)}");
            if (selector != "#detailsModal .modal-content")
                Assert.Equal(dimensions[3], dimensions[2]);
        }
        await Assertions.Expect(panel).ToContainTextAsync("two or more tests submitted");
        await Assertions.Expect(toggle).ToBeFocusedAsync();
        Assert.True(await page.Locator("#ageResultDate").EvaluateAsync<bool>("""
            e => {
                const r = e.closest('.age-result-current').getBoundingClientRect();
                const range = document.createRange(); range.selectNodeContents(e);
                return [...range.getClientRects()].every(line => line.left >= r.left && line.right <= r.right);
            }
            """));
    }
}
