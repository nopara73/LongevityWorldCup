using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyJourneyPacingBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string MeasureFoot = """
        () => {
            const rect = element => {
                if (!element) return [];
                const box = element.getBoundingClientRect();
                return [box.left, box.right, box.width, box.height];
            };
            const actions = document.querySelector('.convergence-actions');
            return {
                Docked: actions.classList.contains('flow-action-stack--docked'),
                Card: rect(document.querySelector('.convergence-stage-layout')),
                Foot: rect(actions),
                Back: rect(document.getElementById('backButton')),
                Next: rect(document.getElementById('nextButton')),
                FootBackground: getComputedStyle(actions).backgroundColor,
                BackBackground: getComputedStyle(document.getElementById('backButton')).backgroundColor,
                Field: rect(Array.from(document.querySelectorAll('#inputForm input, #inputForm textarea'))
                    .find(element => element.getBoundingClientRect().width > 0))
            };
        }
        """;

    private async Task<IPage> OpenApplyAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1' && !document.getElementById('nextButton').disabled");
        return page;
    }

    private static async Task<StageFoot> ShowStageAsync(IPage page, int stage, bool docked)
    {
        await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); window.LwcFlowActionDock?.refreshNow(); }", stage);
        await page.WaitForFunctionAsync(
            "docked => document.querySelector('.convergence-actions').classList.contains('flow-action-stack--docked') === docked",
            docked);
        await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
        return await page.EvaluateAsync<StageFoot>(MeasureFoot);
    }

    [Fact]
    public async Task DesktopActions_KeepOnePlaceWhetherTheStageDocksOrNot()
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await OpenApplyAsync(context);

        // The privacy story overflows a 900px window; the motivation stage fits in it.
        var docked = await ShowStageAsync(page, 3, docked: true);
        var inline = await ShowStageAsync(page, 2, docked: false);

        Assert.True(Math.Abs(docked.Foot[0] - docked.Card[0]) <= 1 && Math.Abs(docked.Foot[1] - docked.Card[1]) <= 1,
            $"The docked actions should sit in the card's column ({docked.Foot[0]}-{docked.Foot[1]} vs {docked.Card[0]}-{docked.Card[1]}).");
        Assert.True(inline.Foot[0] - inline.Card[0] <= 1.5 && inline.Card[1] - inline.Foot[1] <= 1.5,
            "The inline actions should span the card as its foot band.");
        foreach (var (name, a, b) in new[] { ("Back", docked.Back, inline.Back), ("Next", docked.Next, inline.Next) })
        {
            Assert.True(Math.Abs(a[0] - b[0]) <= 1 && Math.Abs(a[1] - b[1]) <= 1 && Math.Abs(a[3] - b[3]) <= 1,
                $"{name} should keep its place and size between stages ({a[0]}-{a[1]}x{a[3]} vs {b[0]}-{b[1]}x{b[3]}).");
        }
        Assert.Equal(inline.FootBackground, docked.FootBackground);
        Assert.NotEqual("rgba(0, 0, 0, 0)", inline.FootBackground);
        Assert.NotEqual(inline.FootBackground, inline.BackBackground);
        Assert.Equal(inline.BackBackground, docked.BackBackground);
    }

    [Theory]
    [InlineData(390, 844, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task PhoneDock_LinesTheActionsUpWithTheFields(int width, int height, bool dark)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            IsMobile = true,
            HasTouch = true
        });
        var page = await OpenApplyAsync(context);

        foreach (var stage in new[] { 1, 6, 7 })
        {
            var foot = await ShowStageAsync(page, stage, docked: true);
            Assert.True(Math.Abs(foot.Back[0] - foot.Field[0]) <= 1.5,
                $"{width}px stage {stage}: Back should start at the field edge ({foot.Back[0]} vs {foot.Field[0]}).");
            Assert.True(Math.Abs(foot.Next[1] - foot.Field[1]) <= 1.5,
                $"{width}px stage {stage}: the primary action should end at the field edge ({foot.Next[1]} vs {foot.Field[1]}).");
            Assert.NotEqual(foot.FootBackground, foot.BackBackground);
        }

        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task PrivacyDecision_ReadsAsANoteLikeTheUploadGuidance(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await OpenApplyAsync(context);
        const string measureNote = """
            selector => {
                const element = document.querySelector(selector);
                const style = getComputedStyle(element);
                return [style.borderInlineStartWidth, style.borderTopLeftRadius, style.backgroundColor, style.paddingLeft].join('|');
            }
            """;

        await page.EvaluateAsync("() => { currentStage = 3; goToStage(3); }");
        var decision = (await page.EvaluateAsync<string>(measureNote, "#privacyDetails")).Split('|');
        var decisionSize = await page.EvaluateAsync<double>("() => parseFloat(getComputedStyle(document.querySelector('#privacyDetails .smaller-text')).fontSize)");
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var guidance = (await page.EvaluateAsync<string>(measureNote, ".convergence-upload-guidance")).Split('|');

        Assert.Equal(guidance[0], decision[0]);
        Assert.Equal(guidance[1], decision[1]);
        Assert.Equal(guidance[2], decision[2]);
        Assert.NotEqual("0px", decision[3]);
        Assert.True(decisionSize >= 14, $"The consent terms should stay readable ({decisionSize}px).");
    }

    [Fact]
    public async Task FieldLabels_MarkTheirRequirementTheSameWayOnEveryStage()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var page = await OpenApplyAsync(context);

        foreach (var stage in new[] { 1, 2, 6, 7 })
        {
            await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); }", stage);
            var unmarked = await page.EvaluateAsync<string[]>("""
                () => Array.from(document.querySelectorAll('#inputForm label'))
                    .filter(label => label.getBoundingClientRect().width > 0)
                    .filter(label => !label.querySelector('.field-requirement'))
                    .map(label => label.textContent.trim())
                """);
            Assert.True(unmarked.Length == 0, $"Stage {stage} labels without a requirement mark: {string.Join(", ", unmarked)}");
        }

        await page.EvaluateAsync("() => { currentStage = 6; goToStage(6); }");
        Assert.Equal("Link (optional)", (await page.Locator("label[for=personalLink]").TextContentAsync())?.Trim());
        Assert.Equal("Media contact (required)", (await page.Locator("label[for=mediaContact]").TextContentAsync())?.Trim());
    }

    [Fact]
    public async Task StageChanges_EnterFromTheDirectionOfTravel()
    {
        await using var context = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.NoPreference });
        var page = await OpenApplyAsync(context);
        const string entering = """
            () => Array.from(document.querySelectorAll('.convergence-stage-head h1, #descriptionForm .convergence-stage-copy'))
                .filter(element => getComputedStyle(element).display !== 'none')
                .map(element => getComputedStyle(element).animationName)
            """;

        await page.EvaluateAsync("() => { currentStage = 2; goToStage(2); }");
        Assert.All(await page.EvaluateAsync<string[]>(entering), name => Assert.Equal("convergence-stage-forward", name));

        await page.EvaluateAsync("() => { currentStage = 1; goToStage(1); }");
        Assert.All(await page.EvaluateAsync<string[]>(entering), name => Assert.Equal("convergence-stage-back", name));

        // Editable controls stay still and fully visible while the story enters.
        Assert.Equal("none", await page.Locator("#personalDetails").EvaluateAsync<string>("fields => getComputedStyle(fields).animationName"));
        await page.WaitForFunctionAsync("() => !document.querySelector('.convergence-stage-layout').classList.contains('convergence-stage-enter')");

        await using var reduced = await NewContextAsync(Browser, App, new() { ReducedMotion = ReducedMotion.Reduce });
        var still = await OpenApplyAsync(reduced);
        await still.EvaluateAsync("() => { currentStage = 2; goToStage(2); }");
        Assert.All(await still.EvaluateAsync<string[]>(entering), name => Assert.Equal("none", name));
    }

    public sealed class StageFoot
    {
        public bool Docked { get; set; }
        public double[] Card { get; set; } = [];
        public double[] Foot { get; set; } = [];
        public double[] Back { get; set; } = [];
        public double[] Next { get; set; } = [];
        public double[] Field { get; set; } = [];
        public string FootBackground { get; set; } = "";
        public string BackBackground { get; set; } = "";
    }
}
