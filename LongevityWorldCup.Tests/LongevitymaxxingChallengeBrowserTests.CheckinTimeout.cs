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
        var backOnline = false;
        const string originalNote = "Keep this stalled draft.";
        const string editedNote = "Edited after the connection returned.";
        var receiptUnavailable = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/check-in/receipt", async route =>
        {
            if (backOnline) await FulfillJsonAsync(route, withPhotos ? "true" : "false");
            else
            {
                await route.AbortAsync("internetdisconnected");
                receiptUnavailable.TrySetResult();
            }
        });
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            requests++;
            var saved = SavedCheckIn(withPhotos ? editedNote : originalNote, 0);
            if (withPhotos)
            {
                // The first upload committed. Editing its note must send only the new text.
                Assert.StartsWith("application/json", route.Request.Headers["content-type"]);
                Assert.Contains(editedNote, route.Request.PostData);
                saved["images"] = JsonSerializer.SerializeToNode(new[] { CheckInImage("/generated/longevitymaxxing/check-in-photos/walk.webp") });
            }
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
        await form.Locator("textarea").FillAsync(originalNote);
        if (withPhotos) await AddCheckInPhotosAsync(form, "walk.png");
        var save = form.Locator("button[type='submit']");
        await save.ClickAsync();
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-stalled-checkin-body", "true");
        await Assertions.Expect(save).ToHaveTextAsync("Saved");
        await Assertions.Expect(save).ToHaveAccessibleNameAsync("Saving…");
        await Assertions.Expect(save).ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(form.Locator("[data-checkin-progress]")).ToBeEmptyAsync();
        await page.Clock.FastForwardAsync(65001);
        await receiptUnavailable.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await page.Clock.FastForwardAsync(31000);
        await Assertions.Expect(save).ToHaveTextAsync("Retry");
        await Assertions.Expect(save).ToBeEnabledAsync();
        Assert.Null(await save.GetAttributeAsync("aria-busy"));
        await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Save unconfirmed.");
        await Assertions.Expect(form.Locator("textarea")).ToHaveValueAsync(originalNote);
        await Assertions.Expect(form.Locator("input[value='0']:checked")).ToHaveCountAsync(4);
        await Assertions.Expect(form.Locator("[data-photo-previews] img")).ToHaveCountAsync(withPhotos ? 1 : 0);
        Assert.Equal(0, requests);
        backOnline = true;
        if (withPhotos) await form.Locator("textarea").FillAsync(editedNote);
        await save.ClickAsync();
        await Assertions.Expect(page.Locator(".lmx-checkin-switcher button[data-day='17'] em")).ToHaveTextAsync("Saved");
        Assert.Equal(1, requests);
    }
}
