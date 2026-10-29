using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CheckInTimeout_AStalledResponseBodyRetainsTheDraftAndCanRetry(bool withPhotos)
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var state = CheckInWorkspaceState();
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Clock.InstallAsync();
        var requests = 0;
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            requests++;
            var saved = SavedCheckIn("Keep this stalled draft.", 0);
            if (withPhotos)
                saved["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/walk.webp") });
            state["eligibleDays"]![0]!["existing"] = saved;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        await page.EvaluateAsync("""
            () => {
                const realFetch = window.fetch.bind(window);
                let attempts = 0;
                window.fetch = async (url, options) => {
                    if (String(url).endsWith('/api/longevitymaxxing/check-in') && ++attempts === 1) {
                        return new Response(new ReadableStream({
                            start(controller) {
                                document.documentElement.setAttribute('data-stalled-checkin-body', 'true');
                                options.signal.addEventListener('abort', () => controller.error(new DOMException('Aborted body', 'AbortError')), {once:true});
                                controller.enqueue(new TextEncoder().encode('{'));
                            }
                        }), {status:200, headers:{'content-type':'application/json'}});
                    }
                    return realFetch(url, options);
                };
            }
            """);
        var form = page.Locator(".lmx-checkin-card");
        foreach (var habit in new[] { "sleep", "exercise", "nutrition", "vices" }) await AnswerHabitAsync(form, habit, "no");
        await form.Locator("textarea").FillAsync("Keep this stalled draft.");
        if (withPhotos) await AddCheckInPhotosAsync(form, "walk.png");
        var save = form.Locator("button[type='submit']");
        await save.ClickAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-stalled-checkin-body", "true");
        await Assertions.Expect(save).ToHaveTextAsync("Saved");
        await Assertions.Expect(save).ToHaveAccessibleNameAsync("Saving…");
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToBeEmptyAsync();
        await page.Clock.FastForwardAsync(65001);
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(save).ToBeEnabledAsync();
        Assert.Null(await save.GetAttributeAsync("aria-busy"));
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Not saved. Request timed out");
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync("Keep this stalled draft.");
        await Assertions.Expect(form.Locator("input[value='0']:checked")).ToHaveCountAsync(4);
        await Assertions.Expect(form.Locator("[data-photo-previews] img")).ToHaveCountAsync(withPhotos ? 1 : 0);
        await save.ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
        Assert.Equal(1, requests);
    }
}
