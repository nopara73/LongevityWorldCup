using System.Text.Json;
using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ApplyJourneyBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    [Theory]
    [InlineData(320)]
    [InlineData(390)]
    [InlineData(768)]
    [InlineData(1440)]
    public async Task FlagSuggestions_ReserveSpaceAndLeaveNameAndDivisionReachable(int width)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.RouteAsync("**/api/data/flags", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = """["United Kingdom","United States","Hungary"]"""
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        var flag = page.Locator("#flag");
        await Assertions.Expect(flag).ToHaveAttributeAsync("data-keydown-listener", "true");
        await page.Locator("#name").FillAsync("Review Athlete");
        await flag.FillAsync("United");
        await Assertions.Expect(page.Locator("#flag-autocomplete-list")).ToBeVisibleAsync();
        var positions = await page.EvaluateAsync<double[]>("""
            () => [document.getElementById('flag-autocomplete-list').getBoundingClientRect().top,
                   document.getElementById('flag').getBoundingClientRect().bottom]
            """);
        Assert.True(positions[0] >= positions[1], "Suggestions must follow the field without covering earlier controls.");

        // A real pointer gesture must reach its intended field without Escape.
        if (width < 600) await page.Locator("#name").TapAsync();
        else await page.Locator("#name").ClickAsync();
        await Assertions.Expect(page.Locator("#name")).ToBeFocusedAsync();
        await Assertions.Expect(flag).ToHaveValueAsync("United");
        await Assertions.Expect(page.Locator("#flag-autocomplete-list")).ToHaveCountAsync(0);

        await flag.FocusAsync();
        await page.Locator("#division").ClickAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.Locator("#division")).ToBeFocusedAsync();
        await Assertions.Expect(flag).ToHaveValueAsync("United");
        await flag.FocusAsync();
        await flag.PressAsync("ArrowDown");
        await flag.PressAsync("Enter");
        Assert.StartsWith("United ", await flag.InputValueAsync());
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-expanded", "false");
        await flag.FillAsync("Hungary");
        var option = page.GetByRole(AriaRole.Option, new() { Name = "Hungary", Exact = true });
        if (width < 600) await option.TapAsync();
        else await option.ClickAsync();
        await Assertions.Expect(flag).ToHaveValueAsync("Hungary");
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-expanded", "false");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(320, 480)]
    [InlineData(390, 400)]
    [InlineData(844, 390)]
    [InlineData(1440, 900)]
    public async Task FlagSuggestions_KeepEveryKeyboardOptionAboveTheActionDock(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 900
        });
        await context.RouteAsync("**/api/data/athletes", route => route.FulfillAsync(new()
        {
            ContentType = "application/json", Body = "[]"
        }));
        await context.RouteAsync("**/api/data/flags", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(Enumerable.Range(1, 15).Select(n => $"United Territory {n:00}"))
        }));
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        var flag = page.Locator("#flag");
        await Assertions.Expect(flag).ToHaveAttributeAsync("data-keydown-listener", "true");
        await flag.FillAsync("United");
        await page.WaitForFunctionAsync("""
            () => {
                const list = document.getElementById('flag-autocomplete-list')?.getBoundingClientRect();
                const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                return list && list.bottom <= (dock?.top ?? innerHeight);
            }
            """);
        for (var index = 0; index < 15; index++)
        {
            await flag.PressAsync("ArrowDown");
            var visible = await page.EvaluateAsync<bool>("""
                () => {
                    const active = document.getElementById(document.getElementById('flag').getAttribute('aria-activedescendant')).getBoundingClientRect();
                    const dock = document.querySelector('.flow-action-stack--docked')?.getBoundingClientRect();
                    const sticky = document.querySelector('#site-sticky-header.visible')?.getBoundingClientRect();
                    return active.top >= (sticky?.bottom ?? 0) - 1 && active.bottom <= (dock?.top ?? innerHeight) + 1;
                }
                """);
            Assert.True(visible, $"Suggestion {index + 1} must remain visible above the action dock.");
        }
        await flag.PressAsync("Enter");
        await Assertions.Expect(flag).ToHaveValueAsync("United Territory 15");
        await Assertions.Expect(flag).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Fact]
    public async Task FinalContactStage_KeepsACurrentStepUntilSubmission()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton').disabled");
        await page.EvaluateAsync("() => { currentStage = 7; goToStage(7); }");
        await Assertions.Expect(page.Locator("#subProgressContainerItem [aria-current=step]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#subStage7")).ToHaveAttributeAsync("aria-current", "step");
        await page.Locator("#backButton").ClickAsync();
        await Assertions.Expect(page.Locator("#subProgressContainerItem [aria-current=step]")).ToHaveCountAsync(1);
        await Assertions.Expect(page.Locator("#subStage6")).ToHaveAttributeAsync("aria-current", "step");
    }

    [Fact]
    public async Task PortraitBanner_ReopensUploadAfterReturningToThePhotoStage()
    {
        await using var context = await NewContextAsync(Browser, App, new());
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => !document.getElementById('nextButton').disabled");
        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        await page.RunAndWaitForFileChooserAsync(() => page.Locator("#illustrationImage").ClickAsync());
        await page.Locator("#nextButton").ClickAsync();
        await page.Locator("#backButton").ClickAsync();
        await page.RunAndWaitForFileChooserAsync(() => page.Locator("#illustrationImage").ClickAsync());
    }

    private const string SeedProofSession = """
        window.sessionStorage.setItem('biomarkerData', JSON.stringify({
            Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1 }]
        }));
        """;

    private const string MeasureStageHeader = """
        () => {
            const card = document.querySelector('.convergence-stage-layout');
            const title = document.querySelector('h1');
            const banner = document.getElementById('illustrationImage');
            const segments = Array.from(document.querySelectorAll('#subProgressContainerItem .stage'));
            const visible = element => {
                const rect = element.getBoundingClientRect();
                const style = getComputedStyle(element);
                return rect.width > 1 && rect.height > 1 && style.display !== 'none' && style.visibility !== 'hidden';
            };
            const cardRect = card.getBoundingClientRect();
            const bannerRect = banner.getBoundingClientRect();
            return {
                Title: title.textContent.trim(),
                TitleInCard: card.contains(title),
                TitleTop: title.getBoundingClientRect().top - cardRect.top,
                BannerVisible: visible(banner),
                BannerTop: bannerRect.top - cardRect.top,
                BannerHeight: bannerRect.height,
                Segments: segments.filter(visible).length,
                Filled: segments.filter(segment => segment.classList.contains('active') || segment.classList.contains('completed')).length,
                Current: segments.filter(segment => segment.getAttribute('aria-current') === 'step').map(segment => segments.indexOf(segment) + 1),
                DrawnNumbers: Array.from(document.querySelectorAll('#subProgressContainerItem .stage-dot')).filter(visible).length,
                StoryTiles: Array.from(document.querySelectorAll('#descriptionForm .convergence-stage-copy'))
                    .filter(visible)
                    .filter(element => getComputedStyle(element).backgroundColor !== 'rgba(0, 0, 0, 0)').length
            };
        }
        """;

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task EveryStage_SharesOneBannerMeterAndTitleHeaderInsideTheCard(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(SeedProofSession);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1' && !document.getElementById('nextButton').disabled");

        var headers = new List<StageHeader>();
        for (var stage = 1; stage <= 7; stage++)
        {
            if (stage > 1)
                await page.EvaluateAsync("stage => { currentStage = stage; goToStage(stage); }", stage);
            await page.Locator("#illustrationImage").EvaluateAsync("image => image.decode()");
            await page.EvaluateAsync("async () => { await document.fonts.ready; await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))); }");
            var header = await page.EvaluateAsync<StageHeader>(MeasureStageHeader);
            var scenario = $"{width}px stage {stage} ({header.Title})";

            Assert.True(header.TitleInCard, $"{scenario}: the stage title should head the card.");
            Assert.DoesNotMatch(@"^\d", header.Title);
            Assert.True(header.BannerVisible, $"{scenario}: every stage should open with its banner.");
            Assert.True(Math.Abs(header.BannerTop) <= 2, $"{scenario}: the banner should be the card's top edge.");
            Assert.Equal(7, header.Segments);
            Assert.Equal(stage, header.Filled);
            Assert.Equal(new[] { stage }, header.Current);
            Assert.Equal(0, header.DrawnNumbers);
            Assert.Equal(0, header.StoryTiles);
            headers.Add(header);
        }

        Assert.All(headers, header => Assert.InRange(header.BannerHeight, headers[0].BannerHeight - 1, headers[0].BannerHeight + 1));
        // Stage 4 with a saved portrait drops the title below the portrait; the others share one title line.
        Assert.All(headers.Where((_, index) => index != 3),
            header => Assert.InRange(header.TitleTop, headers[0].TitleTop - 1, headers[0].TitleTop + 1));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task UploadStagesAndNotes_UseOneControlWidthAndOneNoteShape(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(SeedProofSession);
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply");
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1' && typeof window.setupProofUploadHTML === 'function'");

        const string measureUpload = """
            selector => {
                const form = document.getElementById('inputForm');
                const style = getComputedStyle(form);
                const column = form.getBoundingClientRect().width - parseFloat(style.paddingLeft) - parseFloat(style.paddingRight);
                return [document.querySelector(selector).getBoundingClientRect().width, column];
            }
            """;
        const string measureNote = """
            selector => {
                const style = getComputedStyle(document.querySelector(selector));
                return [style.borderInlineStartWidth, style.borderTopWidth, style.borderTopLeftRadius, style.backgroundColor].join('|');
            }
            """;

        await page.EvaluateAsync("() => { currentStage = 4; goToStage(4); }");
        var profileUpload = await page.EvaluateAsync<double[]>(measureUpload, "#uploadButton");
        var guidance = await page.EvaluateAsync<string>(measureNote, ".convergence-upload-guidance");

        await page.EvaluateAsync("() => { currentStage = 5; goToStage(5); }");
        await page.WaitForFunctionAsync("() => document.querySelector('#biomarker-checklist .biomarker-item')");
        var proofUpload = await page.EvaluateAsync<double[]>(measureUpload, "#uploadProofButton");
        var tracker = await page.EvaluateAsync<string>(measureNote, "#biomarker-checklist");
        await page.Locator("#proofPicInput").SetInputFilesAsync(new FilePayload { Name = "page.png", MimeType = "image/png", Buffer = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==") });
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled && document.querySelector('#proofImageContainer .proof-page-card')");
        var proofCard = await page.EvaluateAsync<string>(measureNote, "#proofImageContainer");

        await page.EvaluateAsync("() => { currentStage = 6; goToStage(6); }");
        var hint = await page.EvaluateAsync<string>(measureNote, ".convergence-helper-text");

        await page.EvaluateAsync("() => { currentStage = 7; goToStage(7); }");
        var congrats = await page.EvaluateAsync<string>(measureNote, "#congratsText");

        Assert.InRange(profileUpload[0], profileUpload[1] - 1, profileUpload[1] + 1);
        Assert.InRange(proofUpload[0], proofUpload[1] - 1, proofUpload[1] + 1);
        // The tracker is a card in the same shape as the proof pages, not a note.
        Assert.Equal(proofCard, tracker);
        Assert.NotEqual(guidance, tracker);
        var guidanceParts = guidance.Split('|');
        var congratsParts = congrats.Split('|');
        Assert.Equal("3px", guidanceParts[0]);
        Assert.Equal("0px", guidanceParts[1]);
        Assert.Equal(guidanceParts[..3], congratsParts[..3]);
        Assert.Equal("rgba(0, 0, 0, 0)", hint.Split('|')[3]);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(320, 640)]
    [InlineData(768, 700)]
    [InlineData(1024, 600)]
    public async Task DockedStages_EndTheCardAtItsContent(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = true,
            HasTouch = true
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/apply?fake=1");
        await page.WaitForFunctionAsync("() => document.body.dataset.convergenceStage === '1' && !document.getElementById('nextButton').disabled");

        foreach (var stage in new[] { 1, 3, 6 })
        {
            // The previous iteration scrolls to the card's end to check clearance.
            // Reset the anchor so every stage exercises its overflowing first screen.
            await page.EvaluateAsync("stage => { window.scrollTo({ top: 0, behavior: 'instant' }); currentStage = stage; goToStage(stage); window.LwcFlowActionDock?.refreshNow(); }", stage);
            await page.WaitForFunctionAsync("() => document.querySelector('.convergence-actions')?.classList.contains('flow-action-stack--docked')");
            const string measureTail = """
                () => {
                    const card = document.querySelector('.convergence-stage-layout').getBoundingClientRect();
                    const fieldset = Array.from(document.querySelectorAll('#inputForm > fieldset'))
                        .find(element => getComputedStyle(element).display !== 'none');
                    return card.bottom - fieldset.getBoundingClientRect().bottom;
                }
                """;
            try
            {
                await page.WaitForFunctionAsync($"() => ({measureTail})() <= 48", options: new() { Timeout = 3_000 });
            }
            catch (TimeoutException)
            {
            }
            var tail = await page.EvaluateAsync<double[]>(
                $$"""
                () => {
                    const tail = ({{measureTail}})();
                    const dock = document.querySelector('.convergence-actions').getBoundingClientRect();
                    window.scrollTo(0, document.documentElement.scrollHeight);
                    const scrolledCard = document.querySelector('.convergence-stage-layout').getBoundingClientRect();
                    return [tail, scrolledCard.bottom, dock.height, innerHeight];
                }
                """);
            Assert.True(tail[0] <= 48, $"{width}px stage {stage}: the card keeps an empty {tail[0]}px tail under its content.");
            Assert.True(tail[1] <= tail[3] - tail[2] + 1, $"{width}px stage {stage}: the docked actions cover the card's end.");
        }

        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900)]
    [InlineData(390, 844)]
    public async Task Review_ClosesTheJourneyWithTheStageCardAnatomy(int width, int height)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync("/review");
        await page.WaitForSelectorAsync(".application-review-copy.primary");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow(); }");

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const panel = document.querySelector('.application-review-panel');
                const rect = panel.getBoundingClientRect();
                const visual = document.querySelector('.application-review-visual').getBoundingClientRect();
                const title = document.querySelector('.application-review-title').getBoundingClientRect();
                const titleStyle = getComputedStyle(document.querySelector('.application-review-title'));
                const foot = document.querySelector('.application-review-foot');
                const footStyle = getComputedStyle(foot);
                const footContent = foot.getBoundingClientRect().width - parseFloat(footStyle.paddingLeft) - parseFloat(footStyle.paddingRight);
                const home = document.querySelector('.application-review-actions .option-button').getBoundingClientRect();
                return [rect.width, visual.top - rect.top, visual.width, title.top - visual.bottom,
                    titleStyle.textAlign === 'center' ? 1 : 0, Math.abs(home.width - footContent), parseFloat(getComputedStyle(panel).borderTopLeftRadius)];
            }
            """);

        Assert.True(layout[0] <= 640.5, $"{width}px: the review card should match the stage card width ({layout[0]}px).");
        Assert.True(Math.Abs(layout[1]) <= 2 && Math.Abs(layout[2] - layout[0]) <= 2, $"{width}px: the artwork band should be the card's top edge.");
        Assert.True(layout[3] >= 0, $"{width}px: the title should follow the artwork.");
        Assert.Equal(1, layout[4]);
        Assert.True(layout[5] <= 1, $"{width}px: Home should span the footer band.");
        Assert.Equal(12, layout[6]);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    public sealed class StageHeader
    {
        public string Title { get; set; } = "";
        public bool TitleInCard { get; set; }
        public double TitleTop { get; set; }
        public bool BannerVisible { get; set; }
        public double BannerTop { get; set; }
        public double BannerHeight { get; set; }
        public int Segments { get; set; }
        public int Filled { get; set; }
        public int[] Current { get; set; } = [];
        public int DrawnNumbers { get; set; }
        public int StoryTiles { get; set; }
    }
}
