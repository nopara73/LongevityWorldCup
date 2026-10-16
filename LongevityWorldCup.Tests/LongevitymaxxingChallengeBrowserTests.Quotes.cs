using Microsoft.Playwright;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(390, false, "sleep")]
    [InlineData(1280, false, "mindset")]
    [InlineData(390, true, "mindset")]
    [InlineData(1280, true, "sleep")]
    public async Task LastDueImperfectCheckIn_ShowsQuoteAfterAutomaticHomeSwitch(int width, bool retry, string bucket)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            IsMobile = width == 390,
            HasTouch = width == 390
        });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState())!.AsObject();
        var page = await OpenCheckInWorkspaceAsync(context, state, direct: true);
        var requests = new List<JsonObject>();
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            requests.Add(JsonNode.Parse(route.Request.PostData!)!.AsObject());
            if (retry && requests.Count == 1)
            {
                await route.FulfillAsync(new() { Status = 503, ContentType = "application/json", Body = "{\"message\":\"Try again.\"}" });
                return;
            }

            var saved = SavedCheckIn("", 2);
            saved["sleep"] = 1;
            if (bucket == "mindset") saved["exercise"] = 1;
            state["eligibleDays"]![0]!["existing"] = saved;
            await FulfillJsonAsync(route, state.ToJsonString());
        });

        var form = page.Locator(".lmx-checkin-card");
        await AnswerAllHabitsAsync(form);
        await AnswerHabitAsync(form, "sleep", "somewhat");
        if (bucket == "mindset") await AnswerHabitAsync(form, "exercise", "somewhat");
        await form.Locator("button[type='submit']").ClickAsync();
        if (retry)
        {
            await Assertions.Expect(form.Locator("[data-checkin-status]")).ToContainTextAsync("Not saved. Try again.");
            await Assertions.Expect(page.Locator("#lmxQuoteDialog")).ToBeHiddenAsync();
            await Assertions.Expect(form.Locator("[data-key='sleep'] input[value='1']")).ToBeCheckedAsync();
            await form.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true }).ClickAsync();
        }

        var quote = page.Locator("#lmxQuoteDialog");
        await Assertions.Expect(quote).ToBeVisibleAsync();
        await Assertions.Expect(quote).ToHaveAttributeAsync("data-quote-bucket", bucket);
        Assert.False(string.IsNullOrWhiteSpace(await quote.Locator("#lmxQuoteDialogText").InnerTextAsync()));
        await Assertions.Expect(quote.Locator(".lmx-quote-athlete a")).ToHaveAttributeAsync("href", new System.Text.RegularExpressions.Regex("^/athlete/"));
        await Assertions.Expect(quote.Locator("#lmxQuoteDialogOk")).ToBeFocusedAsync();
        await Assertions.Expect(page.Locator("#lmxHomeTab")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#lmxParticipantPanel.lmx-checkin-dialog-panel")).ToHaveCountAsync(0);
        Assert.False(await page.Locator("main").EvaluateAsync<bool>("main => main.inert"));
        Assert.Equal(retry ? 2 : 1, requests.Count);
        Assert.True(Guid.TryParse(requests[0]["submissionId"]!.GetValue<string>(), out _));
        if (retry) Assert.True(JsonNode.DeepEquals(requests[0], requests[1]));

        await quote.Locator("#lmxQuoteDialogOk").ClickAsync();
        await Assertions.Expect(quote).ToBeHiddenAsync();
        await Assertions.Expect(page.Locator("#lmxHomePanel")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task SlowImperfectCheckIn_DoesNotInterruptAnotherTab()
    {
        var state = CheckInWorkspaceState();
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("", 2);
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.Locator(".lmx-checkin-switcher button[data-day='17']").ClickAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/api/longevitymaxxing/check-in", async route =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(TimeSpan.FromSeconds(30));
            state["eligibleDays"]![0]!["existing"]!["sleep"] = 1;
            await FulfillJsonAsync(route, state.ToJsonString());
        });
        var form = page.Locator(".lmx-checkin-card");
        await AnswerHabitAsync(form, "sleep", "somewhat");
        try
        {
            await form.Locator("button[type='submit']").ClickAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await page.Locator("#lmxHomeTab").ClickAsync();
            await Assertions.Expect(page.Locator("#lmxHomePanel")).ToBeVisibleAsync();
        }
        finally { release.TrySetResult(); }

        await Assertions.Expect(form).ToHaveAttributeAsync("data-original-sleep", "1");
        await Assertions.Expect(page.Locator("#lmxHomeTab")).ToHaveAttributeAsync("aria-selected", "true");
        await Assertions.Expect(page.Locator("#lmxHomePanel")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#lmxQuoteDialog")).ToBeHiddenAsync();
    }
}
