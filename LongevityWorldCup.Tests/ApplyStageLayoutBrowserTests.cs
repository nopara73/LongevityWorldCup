using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyStageLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(1440, 900, 100, true)]
    [InlineData(1280, 720, 100, true)]
    [InlineData(1440, 900, 200, false)]
    [InlineData(390, 844, 100, false)]
    public async Task StoryStages_PlaceTheTaskBesideTheStoryWhenThereIsRoom(int width, int height, int textPercent, bool besideExpected)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1' && !document.getElementById('nextButton').disabled");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = `${percent}%`", textPercent);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        var stageOneTitleTop = await page.Locator("h1").EvaluateAsync<double>("title => title.getBoundingClientRect().top + scrollY");

        foreach (var stage in new[] { "2", "3" })
        {
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync("stage => document.body.dataset.convergenceStage === stage", stage);
            await page.EvaluateAsync("() => { document.documentElement.style.scrollBehavior = 'auto'; window.scrollTo(0, 0); }");
            await page.EvaluateAsync("() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))");

            var layout = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const story = document.getElementById('descriptionForm').getBoundingClientRect();
                    const task = document.getElementById('inputForm').getBoundingClientRect();
                    const title = document.querySelector('h1').getBoundingClientRect();
                    return [story.left, story.right, story.top, story.bottom, task.left, task.right, task.top, title.top + scrollY, innerHeight];
                }
                """);
            var scenario = $"stage {stage} @ {width}x{height} / {textPercent}%";
            if (besideExpected)
            {
                Assert.True(layout[1] <= layout[4], $"{scenario}: the task card should sit beside the story card.");
                Assert.True(Math.Abs(layout[2] - layout[6]) <= 2, $"{scenario}: both cards should start level.");
                Assert.True(layout[6] <= layout[8] / 2, $"{scenario}: the task should start in the first screen ({layout[6]}px).");
            }
            else
            {
                Assert.True(layout[6] >= layout[3] - 1, $"{scenario}: the task card should follow the story card.");
            }
            // Short desktops deliberately tighten stage one's title, so only taller windows compare it.
            if (height > 780)
                Assert.True(Math.Abs(layout[7] - stageOneTitleTop) <= 1, $"{scenario}: the title moved between stages ({stageOneTitleTop} -> {layout[7]}).");
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }
}
