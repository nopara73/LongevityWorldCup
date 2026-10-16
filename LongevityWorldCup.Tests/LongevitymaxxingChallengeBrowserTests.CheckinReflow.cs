using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;

namespace LongevityWorldCup.Tests;

public sealed partial class LongevitymaxxingChallengeBrowserTests
{
    [Theory]
    [InlineData(320, false, false)]
    [InlineData(390, true, false)]
    [InlineData(320, false, true)]
    [InlineData(390, true, true)]
    [InlineData(820, false, true)]
    [InlineData(1440, true, true)]
    public async Task CheckIn_ReflowsWithLargeTextAndKeepsFieldsAboveSave(int width, bool direct, bool largeText)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ColorScheme = direct ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            HasTouch = width < 600
        });
        var page = await OpenCheckInWorkspaceAsync(context, direct: direct);
        if (largeText) await page.AddStyleTagAsync(new() { Content = "html { font-size: 200% !important; }" });
        await page.EvaluateAsync("document.fonts.ready");
        var form = page.Locator(".lmx-checkin-card");
        foreach (var key in new[] { "sleep", "exercise", "nutrition", "vices" })
        {
            var question = form.Locator($".lmx-question[data-key='{key}']");
            Assert.True(await question.EvaluateAsync<bool>("""
                question => [...question.querySelectorAll('.lmx-answer-face')].every(face => {
                    const target = face.getBoundingClientRect();
                    const row = question.getBoundingClientRect();
                    return target.width >= 43.99 && target.height >= 43.99
                        && target.left >= row.left && target.right <= row.right + 1;
                })
                """));
            await AnswerHabitAsync(form, key, "yes");
        }
        var note = form.Locator("textarea");
        await note.FillAsync("Readable remarks and reachable photos with enlarged text.");
        await AddCheckInPhotosAsync(form, "walk.png");
        await Assertions.Expect(form.Locator("[data-photo-previews] img")).ToHaveCountAsync(1);
        await note.EvaluateAsync("e => e.scrollIntoView({block:'center', behavior:'instant'})");
        await note.FocusAsync();
        await Assertions.Expect(note).ToBeFocusedAsync();
        Assert.True(await form.EvaluateAsync<bool>("""
            form => {
                const fields = form.querySelector('.lmx-checkin-fields');
                const bottom = fields.getBoundingClientRect().bottom;
                const footer = form.querySelector('.lmx-checkin-actions').getBoundingClientRect();
                const note = form.querySelector('textarea').getBoundingClientRect();
                return bottom <= footer.top + 1 && note.top < bottom
                    && fields.scrollWidth <= fields.clientWidth + 1
                    && document.documentElement.scrollWidth <= innerWidth;
            }
            """));
        var save = form.Locator("button[type='submit']");
        await Assertions.Expect(save).ToBeEnabledAsync();
        var saveBox = await save.BoundingBoxAsync();
        Assert.NotNull(saveBox);
        Assert.InRange(saveBox.Y, 0, 844 - saveBox.Height + 1);
    }
}
