using Microsoft.Playwright;
using Xunit;
using static LongevityWorldCup.Tests.AestheticSystemBrowserTests;
using static LongevityWorldCup.Tests.FlowActionDockBrowserTests;

namespace LongevityWorldCup.Tests;

[Collection(BrowserTestCollections.WorkloadB)]
public sealed class ProofUploadLayoutBrowserTests(
    PlaywrightBrowserFixture browserFixture,
    BrowserTestAppFixture appFixture)
    : BrowserIntegrationTest(browserFixture, appFixture)
{
    private const string AthleteState = """
        window.sessionStorage.setItem('selectedAthlete', JSON.stringify({
            Name: 'Browser Test Athlete', DisplayName: 'Browser Test Athlete', Biomarkers: []
        }));
        window.sessionStorage.setItem('biomarkerData', JSON.stringify({
            Biomarkers: [{ Date: '2026-06-19', AlbGL: 45, GluMmolL: 5.1, CrpMgL: 1.35,
                AlpUL: 60, AltUL: 20, ApoA1GL: 1.6, CholesterolMmolL: 4.5, CreatUmolL: 75,
                CystatinCMgL: 0.8, GgtUL: 29, Hba1cMmolMol: 35.5, LymPc: 28.6, MchPg: 31.8,
                McvFL: 92, MonocytePc: 7.2, NeutrophilPc: 64.2, Rbc10e12L: 4.5, RdwPc: 13.4,
                ShbgNmolL: 45.6, UreaMmolL: 5.4, VitaminDNmolL: 50, Wbc1000cellsuL: 6.54 }]
        }));
        """;

    [Theory]
    [InlineData(1440, 100, false)]
    [InlineData(320, 200, true)]
    public async Task ThumbnailHover_ExplainsEditingAndKeepsTheEnlargedIconUnclipped(int width, int textPercent, bool reducedMotion)
    {
        await using var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = 900 },
            ColorScheme = reducedMotion ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        await context.AddInitScriptAsync(AthleteState);
        var page = await OpenAsync(context);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 1));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled");
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        var preview = page.Locator(".proof-page-preview").First;
        var icon = preview.Locator(".proof-page-zoom-hint");
        var originalWidth = (await icon.BoundingBoxAsync())!.Width;
        await preview.HoverAsync();
        await Assertions.Expect(icon).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(preview.Locator(".proof-page-edit-hint")).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(preview.Locator(".proof-page-edit-hint")).ToHaveTextAsync("Click to censor");
        var bounds = (await preview.BoundingBoxAsync())!;
        var badge = (await icon.BoundingBoxAsync())!;
        var drawing = (await icon.Locator("svg").BoundingBoxAsync())!;
        var hint = (await preview.Locator(".proof-page-edit-hint").BoundingBoxAsync())!;
        if (width == 320)
        {
            var uploadOverlap = await page.Locator("#uploadProofButton").EvaluateAsync<bool>("""
                button => {
                    const label = button.querySelector('.flow-action__label');
                    const icon = button.querySelector('i').getBoundingClientRect();
                    const range = document.createRange();
                    range.selectNodeContents(label);
                    return [...range.getClientRects()].some(text =>
                        Math.min(text.right, icon.right) - Math.max(text.left, icon.left) > 0.5
                        && Math.min(text.bottom, icon.bottom) - Math.max(text.top, icon.top) > 0.5);
                }
                """);
            Assert.False(uploadOverlap, "The enlarged upload label must not overlap its icon.");
        }
        Assert.True(badge.Width > originalWidth * 1.2 && drawing.Width >= 32);
        Assert.True(drawing.X >= badge.X + 8 && drawing.X + drawing.Width <= badge.X + badge.Width - 8);
        Assert.True(drawing.Y >= badge.Y + 8 && drawing.Y + drawing.Height <= badge.Y + badge.Height - 8);
        Assert.True(badge.Y >= bounds.Y && badge.Y + badge.Height <= hint.Y + 1);
        Assert.True(hint.X >= bounds.X && hint.X + hint.Width <= bounds.X + bounds.Width);
        Assert.True(hint.Y + hint.Height <= bounds.Y + bounds.Height);
        await ProofRedactionBrowserTests.SaveEvidenceAsync(page, $"thumbnail-hover-{width}-text-{textPercent}");
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task Intro_KeepsInstructionsAndPublicNoteOnOneEdgeInsideOneCard(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenAsync(context);

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.querySelector('.proof-upload-intro').getBoundingClientRect();
                const main = document.getElementById('mainProofInstructions').getBoundingClientRect();
                const note = document.getElementById('subProofInstructions').getBoundingClientRect();
                const symbol = document.querySelector('.proof-upload-symbol').getBoundingClientRect();
                return [main.left, note.left, main.bottom, note.top, card.left, card.right, card.top, card.bottom,
                    symbol.left, symbol.right, symbol.top, symbol.bottom, symbol.height];
            }
            """);
        var scenario = $"{width}x{height}";
        Assert.True(Math.Abs(layout[0] - layout[1]) <= 1, $"{scenario}: the public note should share the instructions' left edge ({layout[0]} vs {layout[1]}).");
        Assert.True(layout[3] >= layout[2], $"{scenario}: the public note should follow the instructions.");
        foreach (var (left, right, top, bottom) in new[] { (layout[0], layout[0], layout[2], layout[3]), (layout[8], layout[9], layout[10], layout[11]) })
            Assert.True(left >= layout[4] && right <= layout[5] && top >= layout[6] && bottom <= layout[7], $"{scenario}: the intro content should sit inside its card.");
        Assert.InRange(layout[12], 80, 104);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false, 320)]
    [InlineData(false, 390)]
    [InlineData(false, 1440)]
    [InlineData(true, 320)]
    [InlineData(true, 390)]
    [InlineData(true, 1440)]
    public async Task EnlargedText_KeepsProofActionsAndInstructionsReadableInBothFlows(bool onboarding, int width)
    {
        await using var context = await CreateContextAsync(width, 900, dark: true);
        var page = onboarding
            ? await ProofReviewBrowserTests.PrepareAsync(context, onboarding: true)
            : await OpenAsync(context);
        await page.EvaluateAsync("document.documentElement.style.fontSize = '200%'");
        // Fake onboarding starts with a tiny sample proof; use the real upload controls for these pages.
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled && document.querySelectorAll('.proof-page-card').length === 3");

        var issues = await page.EvaluateAsync<string[]>(
            """
            () => {
                const issues = [];
                for (const element of document.querySelectorAll('.proof-page-remove, .proof-undo, .proof-upload-intro p')) {
                    const bounds = element.getBoundingClientRect();
                    const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
                    let text;
                    while (text = walker.nextNode()) for (const word of text.textContent.matchAll(/\S+/g)) {
                        const range = document.createRange();
                        range.setStart(text, word.index);
                        range.setEnd(text, word.index + word[0].length);
                        const rects = [...range.getClientRects()];
                        if (new Set(rects.map(rect => Math.round(rect.top))).size > 1)
                            issues.push(`Split word: ${word[0]}`);
                        if (rects.some(rect => rect.left < bounds.left - 1 || rect.right > bounds.right + 1))
                            issues.push(`Clipped word: ${word[0]}`);
                    }
                }
                return issues;
            }
            """);
        Assert.Empty(issues);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1280, 720, 100)]
    [InlineData(1440, 900, 200)]
    [InlineData(844, 390, 200)]
    public async Task LongTracker_UsesThePageScrollAndKeepsItsLastCheckboxReachable(int width, int height, int textPercent)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenAsync(context);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled && document.querySelectorAll('.proof-page-card').length === 3");

        var tracker = page.Locator("#biomarker-checklist");
        Assert.True(await tracker.EvaluateAsync<bool>("element => element.scrollHeight <= element.clientHeight + 1"),
            "The tracker should grow with its content rather than add an inner scrollbar.");
        var checkboxes = tracker.Locator("input");
        Assert.True(await checkboxes.CountAsync() > 20, "Exercise the complete Bortz tracker.");
        await checkboxes.Nth(await checkboxes.CountAsync() - 2).FocusAsync();
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(checkboxes.Last).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Space");
        await Assertions.Expect(checkboxes.Last).ToBeCheckedAsync();
        await checkboxes.Last.ScrollIntoViewIfNeededAsync();
        var last = (await checkboxes.Last.BoundingBoxAsync())!;
        Assert.InRange(last.Y, 0, height - last.Height);
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, true)]
    [InlineData(1280, 720, true)]
    [InlineData(390, 844, false)]
    public async Task UploadedProofs_PlaceTheTrackerBesideThePagesOnDesktop(int width, int height, bool besideExpected)
    {
        await using var context = await CreateContextAsync(width, height);
        var page = await OpenAsync(context);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync(
            """
            () => !document.documentElement.hasAttribute('data-initial-view')
                && document.body.classList.contains('proof-upload-has-proofs')
                && !document.getElementById('proofPicInput').disabled
                && !document.querySelector('#proofImageContainer .proof-preparation')
                && document.querySelectorAll('#proofImageContainer img').length === 3
            """);
        await page.EvaluateAsync(
            "() => Promise.all([...document.querySelectorAll('#proofImageContainer img')].map(image => image.decode()))");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow(); }");
        // File elements appear while the upload workspace is still being laid out.
        // Wait for the same geometry asserted below, rather than assuming two frames settle it.
        try
        {
            await page.WaitForFunctionAsync(
                """
                beside => {
                    const proofs = document.getElementById('proofImageContainer').getBoundingClientRect();
                    const tracker = document.getElementById('biomarker-checklist').getBoundingClientRect();
                    return proofs.width > 0 && tracker.width > 0 && (beside
                        ? tracker.left >= proofs.right && Math.abs(tracker.top - proofs.top) <= 2 && tracker.width >= 260
                        : tracker.top >= proofs.bottom);
                }
                """, besideExpected, new() { Timeout = 5_000 });
        }
        catch (System.TimeoutException)
        {
            var diagnostics = await page.EvaluateAsync<string>(
                """
                () => JSON.stringify({
                    viewport: [innerWidth, innerHeight],
                    rootFont: getComputedStyle(document.documentElement).fontSize,
                    classes: document.body.className,
                    inputDisabled: document.getElementById('proofPicInput').disabled,
                    workspaceDisplay: getComputedStyle(document.querySelector('.proof-upload-workspace')).display,
                    main: document.querySelector('.proof-upload-main').getBoundingClientRect(),
                    proofs: document.getElementById('proofImageContainer').getBoundingClientRect(),
                    tracker: document.getElementById('biomarker-checklist').getBoundingClientRect()
                })
                """);
            throw new Xunit.Sdk.XunitException($"{width}x{height}: the completed proof layout did not settle: {diagnostics}");
        }

        var layout = await page.EvaluateAsync<double[]>(
            """
            () => {
                const proofs = document.getElementById('proofImageContainer').getBoundingClientRect();
                const tracker = document.getElementById('biomarker-checklist').getBoundingClientRect();
                return [proofs.left, proofs.right, proofs.top, proofs.bottom, tracker.left, tracker.right, tracker.top, tracker.width];
            }
            """);
        var scenario = $"{width}x{height}";
        if (besideExpected)
        {
            Assert.True(layout[4] >= layout[1], $"{scenario}: the proof tracker should sit beside the proof pages.");
            Assert.True(Math.Abs(layout[6] - layout[2]) <= 2, $"{scenario}: the tracker should start level with the proof pages.");
            Assert.True(layout[7] >= 260, $"{scenario}: the tracker is too narrow: {layout[7]}px.");
        }
        else
        {
            Assert.True(layout[6] >= layout[3], $"{scenario}: phones should keep the tracker below the proof pages.");
        }
        Assert.Contains("Albumin", await page.Locator("#biomarker-checklist").InnerTextAsync());
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 900, false)]
    [InlineData(390, 844, true)]
    [InlineData(320, 640, false)]
    public async Task UploadedPages_AreTilesWithARoundRemoveMarkOnTheirCorner(int width, int height, bool dark)
    {
        await using var context = await CreateContextAsync(width, height, dark);
        var page = await OpenAsync(context);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled && document.querySelectorAll('.proof-page-card').length === 3");

        var tiles = await page.EvaluateAsync<double[][]>(
            """
            () => [...document.querySelectorAll('.proof-page-card')].map(card => {
                const box = card.getBoundingClientRect();
                const image = card.querySelector('.proof-page-preview').getBoundingClientRect();
                const caption = card.querySelector('.proof-page-caption').getBoundingClientRect();
                const remove = card.querySelector('.proof-page-remove').getBoundingClientRect();
                return [box.left, box.right, box.top, box.bottom, image.left, image.right, image.top, image.bottom,
                    caption.top, caption.bottom, remove.left, remove.right, remove.top, remove.bottom];
            })
            """);
        var scenario = $"{width}x{height}";
        for (var index = 0; index < tiles.Length; index++)
        {
            var t = tiles[index];
            var label = $"{scenario} page {index + 1}";
            Assert.True(t[4] - t[0] <= 2 && t[1] - t[5] <= 2 && t[6] - t[2] <= 2, $"{label}: the page mat should fill its tile edge to edge.");
            Assert.True(t[8] >= t[7] - 1 && t[9] <= t[3] + 1, $"{label}: the caption should sit below the page inside the tile.");
            Assert.True(t[11] - t[10] >= 44 && t[13] - t[12] >= 44, $"{label}: the remove target should stay at least 44px.");
            Assert.True(t[10] >= t[4] && t[11] <= t[5] + 1 && t[12] >= t[6] - 1 && t[13] <= t[7],
                $"{label}: removing should be a mark on the page itself, not a bar below it.");
            Assert.True(t[5] - t[11] <= 8 && t[12] - t[6] <= 8, $"{label}: the remove mark should sit on the page's top-right corner.");
            await Assertions.Expect(page.Locator(".proof-page-remove").Nth(index)).ToHaveAccessibleNameAsync($"Remove proof page {index + 1}");
        }
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(1440, 100)]
    [InlineData(390, 100)]
    [InlineData(320, 200)]
    [InlineData(1440, 200)]
    public async Task ApplicationProofs_ShareTheStageContentColumn(int width, int textPercent)
    {
        await using var context = await CreateContextAsync(width, 900);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding: true);
        await page.EvaluateAsync("percent => document.documentElement.style.fontSize = percent + '%'", textPercent);
        await page.Locator("#proofPicInput").SetInputFilesAsync(await CreateProofImagesAsync(context, 3));
        await page.WaitForFunctionAsync("() => !document.getElementById('proofPicInput').disabled && document.querySelectorAll('.proof-page-card').length >= 3");
        await page.EvaluateAsync("async () => { await document.fonts.ready; window.LwcFlowActionDock?.refreshNow(); }");

        var columns = await page.EvaluateAsync<double[][]>(
            """
            () => ['#proofUploadSection', '#proofUploadSection .convergence-upload-action',
                '#proofImageContainer', '#biomarker-checklist']
                .map(selector => {
                    const rect = document.querySelector(selector).getBoundingClientRect();
                    return [rect.left, rect.right];
                })
            """);
        foreach (var column in columns.Skip(1))
        {
            Assert.InRange(Math.Abs(column[0] - columns[0][0]), 0, 1);
            Assert.InRange(Math.Abs(column[1] - columns[0][1]), 0, 1);
        }
        // The page count band and the tiles fill the card inside its border.
        var inner = await page.EvaluateAsync<double[]>(
            """
            () => {
                const card = document.getElementById('proofImageContainer').getBoundingClientRect();
                return ['.proof-review-head', '.proof-page-grid'].flatMap(selector => {
                    const rect = document.querySelector(selector).getBoundingClientRect();
                    return [rect.left - card.left, card.right - rect.right];
                });
            }
            """);
        Assert.All(inner, inset => Assert.InRange(inset, 0, 2));
        await ExpectNoHorizontalOverflowAsync(page);
    }

    [Theory]
    [InlineData(false, 1440)]
    [InlineData(true, 1440)]
    [InlineData(false, 390)]
    [InlineData(true, 390)]
    public async Task OneTwoAndManyPages_KeepTheirTileWidthInBothFlows(bool onboarding, int width)
    {
        await using var context = await CreateContextAsync(width, 900);
        var page = await ProofReviewBrowserTests.PrepareAsync(context, onboarding);
        while (await page.Locator(".proof-page-remove").CountAsync() > 0)
            await page.Locator(".proof-page-remove").First.ClickAsync();
        var files = await CreateProofImagesAsync(context, 7);
        files[0].Name = "Laboratory-source-with-a-very-long-filename-showing-the-complete-blood-panel-and-collection-date.png";
        double? tileWidth = null;
        var uploaded = 0;
        foreach (var count in new[] { 1, 2, 7 })
        {
            await page.Locator("#proofPicInput").SetInputFilesAsync(files[uploaded..count]);
            await page.WaitForFunctionAsync("count => !document.getElementById('proofPicInput').disabled && document.querySelectorAll('.proof-page-card').length === count", count);
            await page.EvaluateAsync("async () => { await document.fonts.ready; await Promise.all([...document.querySelectorAll('#proofImageContainer img')].map(image => image.decode())); }");
            await WaitForFiniteAnimationsAsync(page);
            var currentWidth = (await page.Locator(".proof-page-card").First.BoundingBoxAsync())!.Width;
            if (tileWidth is not null)
                Assert.InRange(Math.Abs(currentWidth - tileWidth.Value), 0, 1);
            tileWidth = currentWidth;
            uploaded = count;
        }
        // A page is a tile even when its neighbouring tracks are empty.
        var gridWidth = (await page.Locator(".proof-page-grid").BoundingBoxAsync())!.Width;
        Assert.True(tileWidth < gridWidth / 1.5, "One or two pages should not stretch across the proof column.");
        var overflow = await page.EvaluateAsync<bool>(
            """
            () => [...document.querySelectorAll('.proof-page-caption')].some(caption => {
                const card = caption.closest('.proof-page-card').getBoundingClientRect();
                const range = document.createRange();
                range.selectNodeContents(caption);
                return [...range.getClientRects()].some(rect => rect.left < card.left || rect.right > card.right || rect.bottom > card.bottom);
            })
            """);
        Assert.False(overflow, "Captions, including the complete source filename, should remain inside their tiles.");
        await Assertions.Expect(page.Locator(".proof-page-preview").First)
            .ToHaveAccessibleNameAsync($"Hide details on proof page 1: {files[0].Name}");
        await ExpectNoHorizontalOverflowAsync(page);
    }

    private async Task<IBrowserContext> CreateContextAsync(int width, int height, bool dark = false)
    {
        var context = await NewContextAsync(Browser, App, new()
        {
            ViewportSize = new() { Width = width, Height = height },
            ColorScheme = dark ? ColorScheme.Dark : ColorScheme.Light,
            ReducedMotion = ReducedMotion.Reduce,
            IsMobile = width < 600,
            HasTouch = width < 600
        });
        await context.AddInitScriptAsync(AthleteState);
        return context;
    }

    private static async Task<IPage> OpenAsync(IBrowserContext context)
    {
        var page = await context.NewPageAsync();
        await page.GotoAsync("/proofs");
        await page.WaitForFunctionAsync(
            "() => document.getElementById('uploadProofButton')?.getAttribute('data-listener') === 'true' && document.getElementById('mainProofInstructions')?.textContent.trim()");
        await page.EvaluateAsync("() => document.fonts.ready");
        return page;
    }

    // Distinct small report images, so duplicate detection keeps every page.
    private static async Task<FilePayload[]> CreateProofImagesAsync(IBrowserContext context, int count)
    {
        var painter = await context.NewPageAsync();
        await painter.SetViewportSizeAsync(300, 400);
        var files = new FilePayload[count];
        for (var index = 0; index < count; index++)
        {
            await painter.SetContentAsync(
                $"<body style=\"margin:0;font:20px Arial;background:#fff\"><div style=\"padding:24px\"><b>Lab report page {index + 1}</b><p>Albumin {40 + index} g/L</p></div></body>");
            files[index] = new FilePayload
            {
                Name = $"lab-page-{index + 1}.png",
                MimeType = "image/png",
                Buffer = await painter.ScreenshotAsync()
            };
        }
        await painter.CloseAsync();
        return files;
    }
}
