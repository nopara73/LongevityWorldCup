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
    public async Task CheckInDialog_FitsTheCompleteDailyTaskWithoutScrolling(int width, int height, ColorScheme theme)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = theme
        });
        var page = await OpenCheckInWorkspaceAsync(context, direct: true);
        await page.EvaluateAsync("document.fonts.ready");
        var dialog = page.Locator(".lmx-checkin-dialog-panel");
        await Assertions.Expect(dialog).ToBeVisibleAsync();
        var capture = Environment.GetEnvironmentVariable("LWC_CHECKIN_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(capture))
        {
            Directory.CreateDirectory(capture);
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"checkin-fit-{width}-{height}-{theme}.png") });
        }
        var overflow = await dialog.EvaluateAsync<int>("e => e.scrollHeight - e.clientHeight");
        Assert.True(overflow <= 1, $"The {width}x{height} check-in needs {overflow}px of scrolling.");
        Assert.False(await page.EvaluateAsync<bool>("document.documentElement.scrollWidth > innerWidth"));

        var questions = dialog.Locator(".lmx-question");
        Assert.Equal(4, await questions.CountAsync());
        var targets = await dialog.Locator(".lmx-checkin-card .lmx-answer-face, .lmx-checkin-card textarea, .lmx-checkin-card [data-photo-button], .lmx-checkin-card button[type='submit'], #lmxCheckinDialogClose")
            .AllAsync();
        Assert.Equal(16, targets.Count);
        foreach (var target in targets)
        {
            var box = await target.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Width >= 44 && box.Height >= 44, $"Tap target is {box.Width}x{box.Height}.");
            Assert.InRange(box.X, 0, width - box.Width + 1);
            Assert.InRange(box.Y, 0, height - box.Height + 1);
        }

        await AnswerAllHabitsAsync(dialog.Locator(".lmx-checkin-card"));
        await Assertions.Expect(dialog.Locator(".lmx-checkin-card button[type='submit']")).ToBeEnabledAsync();
        Assert.InRange(await dialog.EvaluateAsync<int>("e => e.scrollTop"), 0, 1);

        if (!string.IsNullOrWhiteSpace(capture))
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(capture, $"checkin-fit-answered-{width}-{height}-{theme}.png") });
        }
    }
}
