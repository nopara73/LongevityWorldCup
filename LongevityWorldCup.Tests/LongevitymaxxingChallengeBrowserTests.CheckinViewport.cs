using System.Globalization;
using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, 640, ColorScheme.Light)]
    [InlineData(360, 640, ColorScheme.Dark)]
    [InlineData(384, 690, ColorScheme.Dark)]
    [InlineData(390, 700, ColorScheme.Light)]
    [InlineData(412, 740, ColorScheme.Dark)]
    [InlineData(760, 900, ColorScheme.Light)]
    [InlineData(1280, 720, ColorScheme.Dark)]
    public async Task CheckInDialog_FitsAllFourHabitsWithoutScrollingThroughTheQuestions(int width, int height, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        var state = JsonSerializer.SerializeToNode(BuildParticipantState(
            eligibleDayDate: DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)))!.AsObject();
        var page = await OpenCheckInWorkspaceAsync(context, state, direct: true);
        await page.EvaluateAsync("document.fonts.ready");
        var dialog = page.Locator(".lmx-checkin-dialog-panel");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        var form = dialog.Locator(".lmx-checkin-card");
        await CaptureCheckInViewportAsync(page, $"dialog-{width}-{height}-{theme}");
        await AssertHabitControlsFitAsync(page, form, width, height);
        await AnswerAllHabitsAsync(form);
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        Assert.InRange(await dialog.EvaluateAsync<int>("e => e.scrollTop"), 0, 1);
        await AssertHabitControlsFitAsync(page, form, width, height);
    }

    [Theory]
    [InlineData(320, 640, ColorScheme.Light)]
    [InlineData(384, 690, ColorScheme.Dark)]
    [InlineData(390, 700, ColorScheme.Light)]
    [InlineData(760, 900, ColorScheme.Light)]
    [InlineData(1100, 720, ColorScheme.Light)]
    [InlineData(1280, 720, ColorScheme.Dark)]
    [InlineData(1440, 900, ColorScheme.Light)]
    [InlineData(1920, 1080, ColorScheme.Light)]
    public async Task RegularCheckIn_FitsAllFourHabitsWithSavedDaysAndRemarksBelow(int width, int height, ColorScheme theme)
    {
        var state = CheckInWorkspaceState();
        state["eligibleDays"]!.AsArray().RemoveAt(0);
        state["eligibleDays"]!.AsArray().Last()!["date"] = DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var day in state["eligibleDays"]!.AsArray()) day!["existing"] = SavedCheckIn("Published remark.");
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme,
            TimezoneId = "UTC"
        });
        var page = await OpenCheckInWorkspaceAsync(context, state);
        await page.Locator("#lmxCheckinTab").ClickAsync();
        await page.EvaluateAsync("document.fonts.ready");
        await Assertions.Expect(page.Locator(".lmx-checkin-dialog-panel")).ToHaveCountAsync(0);
        var form = page.Locator(".lmx-checkin-card");
        await Assertions.Expect(form).ToBeVisibleAsync();
        // The dashboard precedes the form in the stacked mobile layout.
        if (width <= 1080)
            await form.EvaluateAsync("""
                e => {
                    const first = e.querySelector('.lmx-question');
                    const headerBottom = Math.max(0,
                        document.querySelector('header')?.getBoundingClientRect().bottom || 0,
                        document.querySelector('#site-sticky-header')?.getBoundingClientRect().bottom || 0);
                    scrollTo({top: scrollY + first.getBoundingClientRect().top - headerBottom - 8, behavior: 'instant'});
                }
                """);
        await CaptureCheckInViewportAsync(page, $"regular-{width}-{height}-{theme}");
        await AssertHabitControlsFitAsync(page, form, width, height);
        await AnswerAllHabitsAsync(form);
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
        await AssertHabitControlsFitAsync(page, form, width, height);

        await form.Locator("textarea").FillAsync("A remark that may be reached by scrolling below the four habits.");
        await Assertions.Expect(form.Locator("button[type='submit']")).ToBeEnabledAsync();
    }

    private static async Task AssertHabitControlsFitAsync(IPage page, ILocator form, int width, int height)
    {
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));
        var questions = form.Locator(".lmx-question");
        Assert.Equal(4, await questions.CountAsync());
        var footer = await form.Locator(".lmx-checkin-actions").BoundingBoxAsync();
        Assert.NotNull(footer);
        var visibleTop = await form.EvaluateAsync<double>("e => e.closest('.lmx-checkin-dialog-panel') ? 0 : Math.max(0, document.querySelector('header')?.getBoundingClientRect().bottom || 0, document.querySelector('#site-sticky-header')?.getBoundingClientRect().bottom || 0)");
        foreach (var question in await questions.AllAsync())
        {
            var box = await question.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.InRange(box.X, 0, width - box.Width + 1);
            Assert.InRange(box.Y, visibleTop, height - box.Height + 1);
            Assert.True(box.Y + box.Height <= footer.Y + 1,
                $"The {width}x{height} check-in footer at {footer.Y}px covers a habit ending at {box.Y + box.Height}px.");
        }
        var targets = await form.Locator(".lmx-answer-face, button[type='submit']").AllAsync();
        Assert.Equal(13, targets.Count);
        foreach (var target in targets)
        {
            var box = await target.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Width >= 44 && box.Height >= 44, $"Tap target is {box.Width}x{box.Height}.");
            Assert.InRange(box.X, 0, width - box.Width + 1);
            Assert.InRange(box.Y, 0, height - box.Height + 1);
        }
    }

    private static async Task CaptureCheckInViewportAsync(IPage page, string name)
    {
        var capture = Environment.GetEnvironmentVariable("LWC_CHECKIN_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            Directory.CreateDirectory(capture);
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"checkin-fit-{name}.png") });
        }
    }
}
