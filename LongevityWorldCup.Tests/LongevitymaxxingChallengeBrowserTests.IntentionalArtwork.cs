using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(1440, ColorScheme.Light)]
    [InlineData(1440, ColorScheme.Dark)]
    [InlineData(390, ColorScheme.Light)]
    [InlineData(390, ColorScheme.Dark)]
    public async Task IntentionalArtwork_RankSixMonikaKeepsTheApprovedSilhouette(int width, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 }, ColorScheme = theme
        });
        await RouteChallengeResourcesAsync(context);
        var state = JsonSerializer.SerializeToNode(BuildPublicState())!.AsObject();
        var template = state["leaderboard"]![0]!.AsObject();
        var rows = new JsonArray();
        foreach (var (id, name, rank, score) in new[]
        {
            ("nopara", "nopara73", 1, 154), ("dde", "DDE", 5, 128),
            ("monika", "Monika Sepesi", 6, 97)
        })
        {
            var row = template.DeepClone().AsObject();
            row["participantId"] = id;
            row["displayName"] = name;
            row["rank"] = rank;
            row["totalPoints"] = score;
            row["athleteUrl"] = null;
            row["profileImageUrl"] = null;
            row["challengeInactive"] = false;
            rows.Add(row);
        }
        state["leaderboard"] = rows;
        state["podium"] = new JsonArray();
        await context.RouteAsync("**/api/longevitymaxxing/state", route => FulfillJsonAsync(route, state.ToJsonString()));
        await context.RouteAsync("**/api/data/athletes", route => FulfillJsonAsync(route, "[]"));
        await context.RouteAsync("**/js/portrait-system.js*", route =>
            route.Request.Url.Contains("intentional-artwork-probe=1", StringComparison.Ordinal)
                ? route.ContinueAsync()
                : route.FulfillAsync(new() { ContentType = "application/javascript", Body = "" }));
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        for (var pass = 0; pass < 3; pass++)
        {
            await page.GotoAsync("/longevitymaxxing", new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            var row = page.Locator(".lmx-board-row:visible").Filter(new() { HasText = "Monika Sepesi" }).First;
            await Assertions.Expect(row.Locator(".lmx-rank")).ToHaveTextAsync("#6");
            var image = row.Locator(".lmx-participant-avatar img");
            var approved = await page.Locator("meta[name='lwc-athlete-placeholder']").GetAttributeAsync("content");
            await Assertions.Expect(image).ToHaveAttributeAsync("src", approved!);
            await image.EvaluateAsync("image=>image.decode()");
            var pixels = await EventPortraitSemanticsBrowserTests.CaptureSettledLogoAsync(image);
            var crop = await image.EvaluateAsync<string>("image=>{const s=getComputedStyle(image);return JSON.stringify([s.objectFit,s.objectPosition,s.filter,s.transform,s.borderRadius])}");
            await page.AddScriptTagAsync(new() { Url = "/js/portrait-system.js?intentional-artwork-probe=1" });
            await page.WaitForFunctionAsync("() => !!window.LwcPortrait");
            await Assertions.Expect(image).ToHaveAttributeAsync("src", approved!);
            await Assertions.Expect(image).ToHaveAttributeAsync("alt", "");
            await Assertions.Expect(image.Locator("..")).ToHaveAttributeAsync("data-lwc-image-role", "illustration");
            Assert.Null(await image.GetAttributeAsync("data-lwc-portrait"));
            Assert.Null(await image.GetAttributeAsync("data-lwc-portrait-fallback"));
            Assert.Equal(crop, await image.EvaluateAsync<string>("image=>{const s=getComputedStyle(image);return JSON.stringify([s.objectFit,s.objectPosition,s.filter,s.transform,s.borderRadius])}"));
            EventPortraitSemanticsBrowserTests.AssertLogoPixels(pixels,
                await EventPortraitSemanticsBrowserTests.CaptureSettledLogoAsync(image));
        }
        Assert.Empty(errors);
    }
}
