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
    [InlineData(1440, 900, 100)]
    [InlineData(1280, 720, 100)]
    [InlineData(390, 844, 100)]
    [InlineData(320, 640, 100)]
    [InlineData(1440, 900, 200)]
    public async Task StoryStages_ShowStoryAndTaskAsOneCardWithTheTaskInTheFirstScreen(int width, int height, int textPercent)
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
        await WaitForFiniteAnimationsAsync(page);
        var stageOneTitleTop = await page.Locator("h1").EvaluateAsync<double>("title => title.getBoundingClientRect().top + scrollY");

        await page.Locator("#nextButton").ClickAsync();
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '2'");
        await page.EvaluateAsync("async () => { document.documentElement.style.scrollBehavior = 'auto'; window.scrollTo(0, 0); await document.fonts.ready; window.LwcFlowActionDock?.refreshNow(); }");
        // Stage focus and docking can still change scroll/layout after the stage attribute changes.
        // Observe the completed stage before taking one geometry snapshot.
        await page.WaitForFunctionAsync("""
            () => {
                const story = document.getElementById('descriptionForm').getBoundingClientRect();
                const task = document.getElementById('inputForm').getBoundingClientRect();
                return story.height > 0 && task.height > 0 && Math.abs(task.top - story.bottom) <= 1;
            }
            """, options: new() { Timeout = 5_000 });
        await WaitForFiniteAnimationsAsync(page);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.convergence-stage-layout').getBoundingClientRect();
                const story = document.getElementById('descriptionForm').getBoundingClientRect();
                const task = document.getElementById('inputForm').getBoundingClientRect();
                const banner = document.getElementById('illustrationImage').getBoundingClientRect();
                const field = document.getElementById('why').getBoundingClientRect();
                const title = document.querySelector('h1').getBoundingClientRect();
                const cardStyle = getComputedStyle(document.querySelector('.convergence-stage-layout'));
                return [card.left, card.right, card.top, card.bottom, story.left, story.right, story.bottom, task.left, task.right, task.top, task.bottom,
                    banner.left, banner.right, banner.top, banner.height, field.top, innerHeight, title.top + scrollY,
                    parseFloat(cardStyle.borderTopWidth)];
            }
            """);
        var scenario = $"{width}x{height} / {textPercent}%";
        Assert.True(layout[18] >= 1, $"{scenario}: the stage card should draw one border around story and task.");
        foreach (var (left, right) in new[] { (layout[4], layout[5]), (layout[7], layout[8]), (layout[11], layout[12]) })
            Assert.True(Math.Abs(left - layout[0]) <= 2 && Math.Abs(right - layout[1]) <= 2, $"{scenario}: story, banner and task should span the one card.");
        Assert.True(Math.Abs(layout[9] - layout[6]) <= 1, $"{scenario}: the task should follow the story inside the same card (story bottom {layout[6]}, task top {layout[9]}).");
        Assert.True(Math.Abs(layout[13] - layout[2]) <= 2, $"{scenario}: the illustration should be the card's top banner.");
        Assert.True(layout[14] <= Math.Max(240, layout[16] * 0.32), $"{scenario}: the banner is too tall ({layout[14]}px).");
        if (textPercent == 100)
            Assert.True(layout[15] <= layout[16] * 0.85, $"{scenario}: the motivation field should start in the first screen ({layout[15]}px).");
        if (height > 780)
            Assert.True(Math.Abs(layout[17] - stageOneTitleTop) <= 1, $"{scenario}: the title moved between stages ({stageOneTitleTop} -> {layout[17]}).");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 640)]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task UploadStages_KeepThePortraitSquareAndButtonWordsWholeWithEnlargedText(int width, int height)
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
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton').disabled");
        await page.EvaluateAsync("async () => { await document.fonts.ready; document.documentElement.style.fontSize = '200%'; }");

        for (var stage = 2; stage <= 5; stage++)
        {
            await page.Locator("#nextButton").ClickAsync();
            await page.WaitForFunctionAsync("stage => document.body.dataset.convergenceStage === String(stage)", stage);
            if (stage < 4) continue;
            await page.EvaluateAsync("async () => { window.LwcFlowActionDock?.refreshNow(); await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");

            if (stage == 4)
            {
                var portrait = await page.Locator("#profileImage").BoundingBoxAsync();
                Assert.NotNull(portrait);
                Assert.True(Math.Abs(portrait.Width - portrait.Height) <= 1,
                    $"{width}px / 200%: the portrait is distorted ({portrait.Width} x {portrait.Height}).");
            }

            var buttonIssues = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const issues = [];
                    for (const button of document.querySelectorAll('.convergence-upload-action .flow-action')) {
                        if (!button.getBoundingClientRect().width) continue;
                        const label = button.querySelector('.flow-action__label');
                        const icon = button.querySelector('i')?.getBoundingClientRect();
                        const walker = document.createTreeWalker(label, NodeFilter.SHOW_TEXT);
                        let node;
                        while (node = walker.nextNode()) {
                            for (const word of node.textContent.matchAll(/\S+/g)) {
                                const range = document.createRange();
                                range.setStart(node, word.index);
                                range.setEnd(node, word.index + word[0].length);
                                const rects = Array.from(range.getClientRects());
                                if (new Set(rects.map(rect => Math.round(rect.top))).size > 1)
                                    issues.push('Split word: ' + word[0]);
                                if (icon && rects.some(rect => Math.min(rect.right, icon.right) - Math.max(rect.left, icon.left) > 1
                                    && Math.min(rect.bottom, icon.bottom) - Math.max(rect.top, icon.top) > 1))
                                    issues.push('Icon overlaps: ' + word[0]);
                            }
                        }
                    }
                    return issues;
                }
                """);
            Assert.Empty(buttonIssues);
            await ExpectNoHorizontalOverflowAsync(page);
        }
    }
}
